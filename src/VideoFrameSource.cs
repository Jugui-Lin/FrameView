using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace FrameDeck
{
    internal sealed class VideoFrameSource : IDisposable
    {
        private static readonly ConcurrentDictionary<string, VideoFrameSource> Sources = new ConcurrentDictionary<string, VideoFrameSource>();
        private readonly string id = Guid.NewGuid().ToString("N");
        private readonly Size size;
        private readonly Reader preview, thumbnails;

        private VideoFrameSource(string ffmpeg, string path, decimal fps, Size size)
        {
            this.size = size;
            preview = new Reader(ffmpeg, path, fps, 24, size.Width >= 1920 || size.Height >= 1920 ? 6 : 3);
            thumbnails = new Reader(ffmpeg, path, fps, 6, 3);
            Sources[id] = this;
        }

        internal string Key(int index) { return "video:" + id + ":" + index.ToString(CultureInfo.InvariantCulture); }
        internal Size NativeSize { get { return size; } }
        internal static string SourceId(string key)
        {
            return key.Length > 39 && key.StartsWith("video:", StringComparison.Ordinal) ? key.Substring(6, 32) : null;
        }

        internal static int PlaybackDivisor(string key, int divisor)
        {
            string id = SourceId(key);
            VideoFrameSource source;
            if (id == null || !Sources.TryGetValue(id, out source)) return divisor;
            if (source.size.Width <= 2048 && source.size.Height <= 1152) return divisor;
            // Keep motion previews close to monitor size and reduce pixel transfer for UHD sources.
            int playback = Math.Max((source.size.Width + 1279) / 1280, (source.size.Height + 719) / 720);
            return Math.Max(divisor, playback);
        }

        internal static Bitmap Decode(string key, int divisor, Size box, CancellationToken cancel, out Size original)
        {
            string id = SourceId(key);
            VideoFrameSource source;
            int index;
            if (id == null || !Sources.TryGetValue(id, out source) ||
                !int.TryParse(key.Substring(39), NumberStyles.None, CultureInfo.InvariantCulture, out index))
                throw new IOException("视频素材已关闭。");
            original = source.size;
            Size target = MediaSource.PreviewSize(original, divisor, box);
            return (box.IsEmpty ? source.preview : source.thumbnails).Get(index, target, cancel);
        }

        internal static MediaSource TryOpen(string ffmpeg, string probe, string path, ref decimal fps, bool useSourceRate,
            CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            const string entries = "stream=width,height,start_time,duration,duration_ts,time_base,nb_frames,avg_frame_rate,r_frame_rate:" +
                "stream_tags=DURATION:stream_side_data=rotation:format=start_time,duration,nb_streams";
            string args = "-v error -select_streams V:0 -show_entries " + entries + " -of json " + VideoTools.Quote(path);
            string json = Capture(probe, args, cancel);
            if (json == null) return null;
            var result = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            var streams = Value(result, "streams") as object[];
            if (streams == null || streams.Length == 0) return null;
            var stream = streams[0] as Dictionary<string, object>;
            var format = Value(result, "format") as Dictionary<string, object>;
            decimal nativeRate = Ratio(stream, "avg_frame_rate");
            if (useSourceRate)
            {
                decimal candidate = nativeRate >= 1 && nativeRate <= 240 ? nativeRate : Ratio(stream, "r_frame_rate");
                if (candidate >= 1 && candidate <= 240) fps = candidate;
            }
            int width = (int)Number(stream, "width"), height = (int)Number(stream, "height");
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue / 4) return null;
            if (Math.Abs(Number(stream, "start_time") - Number(format, "start_time")) > .001m) return null;

            decimal duration = Number(stream, "duration");
            decimal ticks = Number(stream, "duration_ts");
            decimal timeBase = Ratio(stream, "time_base");
            if (ticks > 0 && timeBase > 0) duration = ticks * timeBase;
            if (duration <= 0)
            {
                var tags = Value(stream, "tags") as Dictionary<string, object>;
                string timestamp = Value(tags, "DURATION") as string;
                // Matroska's duration tag is an end timestamp and may have nanosecond precision.
                TimeSpan end;
                if (timestamp != null && TimeSpan.TryParse(timestamp.Length > 16 ? timestamp.Substring(0, 16) : timestamp,
                    CultureInfo.InvariantCulture, out end))
                    duration = (decimal)end.TotalSeconds - Number(stream, "start_time");
            }
            bool streamHasDuration = duration > 0;
            if (!streamHasDuration) duration = Number(format, "duration");
            decimal frames = Math.Round(duration * fps, MidpointRounding.AwayFromZero);
            bool constant = streamHasDuration && nativeRate > 0 && nativeRate == Ratio(stream, "r_frame_rate") &&
                Math.Abs(duration * nativeRate - Number(stream, "nb_frames")) < .0001m;
            if (!constant)
            {
                // Container duration can disagree with the last resampled frame on VFR material.
                int from = (int)Math.Max(0, Math.Min(1000000, frames - Math.Ceiling(fps / 2)));
                int count = CountFrames(ffmpeg, path, fps, from, cancel);
                if (count == 0 && from > 0) { from = 0; count = CountFrames(ffmpeg, path, fps, from, cancel); }
                if (count == 0) return null;
                frames = from + count;
            }
            if (frames > 1000000) throw new InvalidDataException("视频超过 1,000,000 帧。");
            if (frames < 1) return null;

            var sideData = Value(stream, "side_data_list") as object[];
            if (sideData != null)
                foreach (object entry in sideData)
                {
                    decimal rotation = Math.Abs(Number(entry as Dictionary<string, object>, "rotation")) % 360;
                    if (rotation % 90 != 0) return null;
                    if (rotation % 180 == 90) { int previous = width; width = height; height = previous; }
                }
            return new MediaSource { Name = Path.GetFileNameWithoutExtension(path), Length = (int)frames, FrameRate = fps,
                Video = new VideoFrameSource(ffmpeg, path, fps, new Size(width, height)) };
        }

        private static int CountFrames(string ffmpeg, string path, decimal fps, int from, CancellationToken cancel)
        {
            string progress = Capture(ffmpeg, "-hide_banner -loglevel error -nostdin -copyts -start_at_zero " +
                SeekOption(from, fps) + "-threads 3 -i " + VideoTools.Quote(path) + " -map 0:V:0 -an -sn -dn -vf " +
                FpsFilter(fps, from) + " -frames:v " + (1000001 - from) + " -progress pipe:1 -f null NUL", cancel);
            Match tail = Regex.Match(progress ?? "", @"^frame=\s*(\d+)", RegexOptions.Multiline | RegexOptions.RightToLeft);
            int count;
            return tail.Success && int.TryParse(tail.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out count) ? count : 0;
        }

        private static string Capture(string executable, string arguments, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            using (var process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            }))
            using (cancel.Register(() => VideoTools.Stop(process)))
            {
                process.BeginErrorReadLine();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                cancel.ThrowIfCancellationRequested();
                return process.ExitCode == 0 ? output : null;
            }
        }

        private static string SeekOption(int frame, decimal fps)
        {
            // Seek just before the target. The demuxer supplies the preceding keyframe,
            // and the FPS filter still sees enough timestamps to select the same frame.
            decimal seek = Math.Max(0, (frame - 2) / fps);
            return seek == 0 ? "" : "-ss " + seek.ToString("0.#########", CultureInfo.InvariantCulture) + " -noaccurate_seek ";
        }

        private static string FpsFilter(decimal fps, int frame)
        {
            return "fps=" + fps.ToString(CultureInfo.InvariantCulture) + ",trim=start_pts=" + frame;
        }

        private static object Value(Dictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) ? value : null;
        }

        private static decimal Number(Dictionary<string, object> values, string key)
        {
            decimal number;
            return decimal.TryParse(Convert.ToString(Value(values, key), CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out number) ? number : 0;
        }
        private static decimal Ratio(Dictionary<string, object> values, string key)
        {
            string value = Value(values, key) as string;
            if (value == null) return 0;
            string[] parts = value.Split('/');
            decimal numerator, denominator;
            return parts.Length == 2 && decimal.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out numerator) &&
                decimal.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out denominator) && denominator != 0
                ? numerator / denominator : 0;
        }

        public void Dispose()
        {
            VideoFrameSource ignored;
            Sources.TryRemove(id, out ignored);
            preview.Dispose();
            thumbnails.Dispose();
        }

        private sealed class Reader : IDisposable
        {
            private readonly object gate = new object();
            private readonly string ffmpeg, path;
            private readonly decimal fps;
            private readonly int maxSkip, threads;
            private readonly Timer idle;
            private readonly Dictionary<int, byte[]> recent = new Dictionary<int, byte[]>();
            private readonly Queue<int> order = new Queue<int>();
            private Process process;
            private int next;
            private Size size;
            private long lastAccess;
            private bool disposed;

            internal Reader(string ffmpeg, string path, decimal fps, int maxSkip, int threads)
            {
                this.ffmpeg = ffmpeg;
                this.path = path;
                this.fps = fps;
                this.maxSkip = maxSkip;
                this.threads = threads;
                idle = new Timer(ReleaseIdle);
            }

            internal Bitmap Get(int index, Size target, CancellationToken cancel)
            {
                lock (gate)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (disposed) throw new IOException("视频素材已关闭。");
                    try
                    {
                        if (size != target)
                        {
                            StopProcess();
                            recent.Clear(); order.Clear();
                            size = target;
                        }
                        byte[] pixels;
                        if (!recent.TryGetValue(index, out pixels))
                        {
                            if (process == null || index < next || index - next > maxSkip) Start(index);
                            int frameBytes = checked(size.Width * size.Height * 4);
                            int capacity = Math.Max(1, Math.Min(4, 40 * 1024 * 1024 / frameBytes));
                            Process decoding = process;
                            using (cancel.Register(() => VideoTools.Stop(decoding)))
                            do
                            {
                                cancel.ThrowIfCancellationRequested();
                                if (order.Count < capacity) pixels = new byte[frameBytes];
                                else
                                {
                                    int oldest = order.Dequeue();
                                    pixels = recent[oldest];
                                    recent.Remove(oldest);
                                }
                                Stream output = process.StandardOutput.BaseStream;
                                int offset = 0;
                                while (offset < pixels.Length)
                                {
                                    int count = output.Read(pixels, offset, pixels.Length - offset);
                                    if (count == 0)
                                    {
                                        cancel.ThrowIfCancellationRequested();
                                        throw new EndOfStreamException("无法解码视频的第 " + (next + 1) + " 帧。");
                                    }
                                    offset += count;
                                }
                                recent[next] = pixels;
                                order.Enqueue(next++);
                            } while (next <= index);
                        }
                        cancel.ThrowIfCancellationRequested();
                        return CopyFrame(pixels, target);
                    }
                    catch
                    {
                        StopProcess();
                        cancel.ThrowIfCancellationRequested();
                        throw;
                    }
                    finally
                    {
                        lastAccess = Stopwatch.GetTimestamp();
                        idle.Change(2500, Timeout.Infinite);
                    }
                }
            }

            private void Start(int index)
            {
                StopProcess();
                // Keep original timestamps so seeking and continuous playback use the same FPS grid.
                string filter = FpsFilter(fps, index) +
                    ",scale=" + size.Width + ":" + size.Height + ":flags=bilinear,format=bgra";
                string args = "-hide_banner -loglevel error -nostdin -copyts -start_at_zero " + SeekOption(index, fps) +
                    "-threads " + threads + " -i " + VideoTools.Quote(path) +
                    " -map 0:V:0 -an -sn -dn -filter_threads 2 -vf " + filter +
                    " -c:v rawvideo -threads 1 -fps_mode passthrough -f rawvideo pipe:1";
                process = Process.Start(new ProcessStartInfo(ffmpeg, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                });
                process.BeginErrorReadLine();
                next = index;
                recent.Clear(); order.Clear();
            }

            private static Bitmap CopyFrame(byte[] pixels, Size target)
            {
                var bitmap = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb);
                try
                {
                    var locked = bitmap.LockBits(new Rectangle(Point.Empty, target), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                    try
                    {
                        int rowBytes = target.Width * 4;
                        if (locked.Stride == rowBytes) Marshal.Copy(pixels, 0, locked.Scan0, pixels.Length);
                        else for (int row = 0; row < target.Height; row++)
                            Marshal.Copy(pixels, row * rowBytes, IntPtr.Add(locked.Scan0, row * locked.Stride), rowBytes);
                    }
                    finally { bitmap.UnlockBits(locked); }
                    return bitmap;
                }
                catch { bitmap.Dispose(); throw; }
            }

            private void ReleaseIdle(object state)
            {
                lock (gate)
                {
                    if (disposed || Stopwatch.GetTimestamp() - lastAccess < 2 * Stopwatch.Frequency) return;
                    StopProcess();
                    recent.Clear(); order.Clear();
                }
            }

            private void StopProcess()
            {
                if (process == null) return;
                VideoTools.Stop(process);
                process.Dispose();
                process = null;
            }

            public void Dispose()
            {
                lock (gate)
                {
                    disposed = true;
                    idle.Dispose();
                    StopProcess();
                    recent.Clear(); order.Clear();
                }
            }
        }
    }
}
