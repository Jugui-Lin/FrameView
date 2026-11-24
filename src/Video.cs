using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed class VideoTools
    {
        internal static readonly string[] Extensions = { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v" };
        private readonly string scratch = Path.Combine(Path.GetTempPath(), "FrameDeck-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, MediaSource> videos = new Dictionary<string, MediaSource>(StringComparer.OrdinalIgnoreCase);
        internal static bool IsVideo(string path)
        {
            return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }
        private static string FindEncoder()
        {
            string configured = Environment.GetEnvironmentVariable("FRAMEDECK_FFMPEG");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            string bundled = Bundle.Extract("ffmpeg.exe");
            if (bundled != null) return bundled;
            string nearby = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(nearby)) return nearby;
            foreach (string raw in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string candidate = Path.Combine(raw.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (string relative in new[] { @"CR TubeGet\ffmpeg\ffmpeg.exe", @"KeyShot Studio\bin\ffmpeg.exe" })
            {
                string candidate = Path.Combine(programs, relative);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("视频功能需要 FFmpeg。请将 ffmpeg.exe 放在 " + BrandInfo.Name + ".exe 旁，或加入 PATH。");
        }
        internal string NewScratch()
        {
            string path = Path.Combine(scratch, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
        internal static void RemoveScratch(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        internal void Close()
        {
            foreach (MediaSource source in videos.Values)
                if (source.Video != null) source.Video.Dispose();
            videos.Clear();
            RemoveScratch(scratch);
        }

        internal MediaSource Open(string path, decimal fps, CancellationToken cancel)
        {
            return OpenCore(path, fps, false, cancel);
        }

        internal MediaSource OpenAtSourceRate(string path, decimal fallbackFps, CancellationToken cancel)
        {
            return OpenCore(path, fallbackFps, true, cancel);
        }

        private MediaSource OpenCore(string path, decimal fps, bool useSourceRate, CancellationToken cancel)
        {
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("找不到视频素材。", path);
            string key = file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks + "|" +
                (useSourceRate ? "source:" : "") + fps.ToString(CultureInfo.InvariantCulture);
            MediaSource source;
            if (videos.TryGetValue(key, out source)) return source;
            string ffmpeg = FindEncoder();
            string probe = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ffmpeg)), "ffprobe.exe");
            if (!File.Exists(probe)) probe = Bundle.Extract("ffprobe.exe");
            if (probe != null)
            {
                source = VideoFrameSource.TryOpen(ffmpeg, probe, file.FullName, ref fps, useSourceRate, cancel);
                if (source != null)
                {
                    videos[key] = source;
                    return source;
                }
            }
            string folder = NewScratch();
            try
            {
                string rate = fps.ToString(CultureInfo.InvariantCulture);
                Run(ffmpeg, "-i " + Quote(file.FullName) + " -map 0:V:0 -an -sn -vf fps=" + rate +
                    " -frames:v 1000001 -start_number 0 -c:v png -threads 1 " + Quote(Path.Combine(folder, "video_%07d.png")), cancel);
                source = new MediaSource { Name = Path.GetFileNameWithoutExtension(path), FrameRate = fps };
                foreach (string frame in Directory.EnumerateFiles(folder, "video_*.png"))
                {
                    int index = int.Parse(Path.GetFileNameWithoutExtension(frame).Substring(6), CultureInfo.InvariantCulture);
                    source.Files.Add(index, frame);
                }
                if (source.Files.Count == 0 || source.Files.Count > 1000000)
                    throw new InvalidDataException("视频为空，或超过 1,000,000 帧。");
                source.Length = source.Files.Count;
                videos[key] = source;
                return source;
            }
            catch { RemoveScratch(folder); throw; }
        }
        internal static string Encoder() { return FindEncoder(); }
        internal static void Trim(string ffmpeg, string input, string output, decimal start, decimal duration,
            decimal fps, CancellationToken cancel)
        {
            Run(ffmpeg, "-ss " + start.ToString("0.#########", CultureInfo.InvariantCulture) + " -i " + Quote(input) +
                " -t " + duration.ToString("0.#########", CultureInfo.InvariantCulture) +
                " -map 0:V:0 -map 0:a:0? -vf fps=" + fps.ToString(CultureInfo.InvariantCulture) +
                ",pad=ceil(iw/2)*2:ceil(ih/2)*2 -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p" +
                " -c:a aac -b:a 160k -movflags +faststart " + Quote(output), cancel);
        }

        internal static void Encode(string ffmpeg, Size size, string output, decimal fps, MoviePreset preset, CancellationToken cancel,
            Action<Stream> writeFrames)
        {
            Run(ffmpeg, "-f rawvideo -pixel_format bgra -video_size " + size.Width + "x" + size.Height +
                " -framerate " + fps.ToString(CultureInfo.InvariantCulture) + " -i pipe:0" +
                " -an -vf pad=ceil(iw/2)*2:ceil(ih/2)*2 -c:v libx264 -preset " + preset.EncoderSpeed +
                " -crf " + preset.Crf + " -pix_fmt yuv420p -movflags +faststart " +
                Quote(output), cancel, writeFrames);
        }
        internal static void EncodeGif(string ffmpeg, Size size, string output, decimal fps, int interval,
            int rangeLength, int colors, CancellationToken cancel, Action<Stream> writeFrames)
        {
            string rate = (fps / interval).ToString("0.#########", CultureInfo.InvariantCulture);
            int finalFrames = (rangeLength - 1) % interval + 1;
            int finalDelay = Math.Max(2, (int)Math.Round(100m * finalFrames / fps, MidpointRounding.AwayFromZero));
            string dither = colors == 256 ? "sierra2_4a" : "bayer:bayer_scale=" + (colors == 64 ? "5" : "3");
            string filter = "split[pixels][palette];[palette]palettegen=stats_mode=single:max_colors=" + colors +
                "[colors];[pixels][colors]paletteuse=new=1:diff_mode=rectangle:dither=" + dither;
            Run(ffmpeg, "-f rawvideo -pixel_format bgra -video_size " + size.Width + "x" + size.Height +
                " -framerate " + rate + " -i pipe:0 -filter_complex_threads 2 -filter_complex " + Quote(filter) +
                " -an -loop 0 -final_delay " + finalDelay + " -f gif " + Quote(output), cancel, writeFrames);
        }
        internal static string Quote(string path)
        {
            // Windows paths cannot contain quotation marks. Arguments here are complete file paths.
            return "\"" + path + "\"";
        }
        private static void Run(string exe, string args, CancellationToken cancel, Action<Stream> writeFrames = null)
        {
            cancel.ThrowIfCancellationRequested();
            var errors = new StringBuilder();
            using (var process = new Process { StartInfo = new ProcessStartInfo(exe, "-hide_banner -loglevel error -nostdin " + args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = writeFrames != null } })
            {
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null && errors.Length < 4000) errors.AppendLine(e.Data);
                };
                process.Start();
                process.BeginErrorReadLine();
                using (cancel.Register(() => Stop(process)))
                {
                    Exception pipeError = null;
                    if (writeFrames != null)
                    {
                        try { using (Stream input = process.StandardInput.BaseStream) writeFrames(input); }
                        catch (IOException ex) { pipeError = ex; }
                        catch { Stop(process); process.WaitForExit(); throw; }
                    }
                    process.WaitForExit();
                    cancel.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("FFmpeg 处理失败。\n" + errors.ToString().Trim(), pipeError);
                    if (pipeError != null) throw new IOException("无法向 FFmpeg 写入帧。", pipeError);
                }
            }
        }
        internal static void Stop(Process process)
        {
            try { process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    internal sealed class MediaProgress : Form
    {
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private bool finished;

        private MediaProgress(string title, string message)
        {
            Text = title; Font = Theme.Small; BackColor = Theme.Panel; ForeColor = Theme.Text;
            ClientSize = new Size(400, 138); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            var label = new Label { Text = message, Location = new Point(20, 14), Size = new Size(360, 30), AutoEllipsis = true };
            var progress = new ProgressBar { Location = new Point(20, 47), Size = new Size(360, 14), Style = ProgressBarStyle.Marquee };
            var button = new Button { Text = "取消", Location = new Point(297, 86), Size = new Size(83, 30),
                BackColor = Theme.Raised, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
            button.Click += delegate { stop.Cancel(); button.Enabled = false; label.Text = "正在取消…"; };
            Controls.Add(label); Controls.Add(progress); Controls.Add(button);
            UiScale.ApplyLayout(this);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (finished) return;
                e.Cancel = true; button.PerformClick();
            };
        }
        internal static T Run<T>(Form owner, string title, string message, Func<CancellationToken, T> operation)
        {
            T result = default(T);
            Exception failure = null;
            using (var form = new MediaProgress(title, message))
            using (form.stop)
            {
                form.Shown += async delegate
                {
                    try { result = await Task.Run(() => operation(form.stop.Token)); }
                    catch (Exception ex) { failure = ex; }
                    form.finished = true; form.Close();
                };
                form.ShowDialog(owner);
            }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }
    }
}
