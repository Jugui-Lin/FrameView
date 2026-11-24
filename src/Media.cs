using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;

namespace FrameDeck
{
    public sealed class Layer
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool Sequence { get; set; }
        public decimal VideoFps { get; set; }
        public bool Visible { get; set; }
        public int Start { get; set; }
        public int In { get; set; }
        public int Out { get; set; }
        public int Opacity { get; set; }
        public int Exposure { get; set; }
        public bool Flip { get; set; }
        public int Demo { get; set; }
        public string ExrPassId { get; set; }
        internal MediaSource Source;
        internal int End { get { return Start + Out - In; } }

        public Layer() { Name = ""; Path = ""; Visible = true; Opacity = 100; }
        internal string Key(int frame)
        {
            if (frame < Start || frame > End) return null;
            int index = In + frame - Start;
            if (Demo > 0) return "demo:" + Demo + ":" + index;
            if (Source == null) return null;
            return Source.KeyForPass(index, ExrPassId);
        }
        internal int Length { get { return Demo > 0 ? 96 : Source == null ? Math.Max(1, Math.Min(999999, Out) + 1) : Source.Length; } }
    }

    public sealed class Project
    {
        internal static readonly string[] PreviewFilters = { "Auto", "Nearest", "Linear", "Quality" };
        internal static readonly string[] PreviewBackgrounds = { "Checker", "Black", "Gray", "White" };
        public int Version { get; set; }
        public List<Layer> Layers { get; set; }
        public decimal Fps { get; set; }
        public decimal PlaybackSpeed { get; set; }
        public string PreviewFilter { get; set; }
        public string PreviewBackground { get; set; }
        public int RangeIn { get; set; }
        public int RangeOut { get; set; }
        public int Frame { get; set; }
        public string Repeat { get; set; }
        public bool Compare { get; set; }
        public bool CompareSideBySide { get; set; }
        public int PreviewDivisor { get; set; }
        public int CacheMegabytes { get; set; }
        public float Wipe { get; set; }
        public string Channel { get; set; }
        public List<int> Marks { get; set; }

        public Project()
        {
            Version = 1; Layers = new List<Layer>(); Fps = 24; PlaybackSpeed = 1;
            RangeOut = -1; Repeat = "循环"; Marks = new List<int>(); Wipe = .5f; Channel = "RGB";
            PreviewDivisor = 1; CacheMegabytes = 384;
            PreviewFilter = "Auto"; PreviewBackground = "Checker";
        }
    }

    internal sealed class MediaSource
    {
        internal readonly Dictionary<int, string> Files = new Dictionary<int, string>();
        internal int First, Length;
        internal decimal FrameRate;
        internal string Name, Group;
        internal VideoFrameSource Video;
        private Size nativeSize;
        private ExrPass[] exrPasses;
        internal ExrPass[] ExrPasses
        {
            get
            {
                if (exrPasses != null) return exrPasses;
                string path = Files.Values.FirstOrDefault();
                if (path != null && string.Equals(System.IO.Path.GetExtension(path), ".exr", StringComparison.OrdinalIgnoreCase))
                {
                    try { exrPasses = ExrPass.Read(path, out nativeSize); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return exrPasses ?? (exrPasses = new ExrPass[0]);
            }
        }
        internal ExrPass ResolveExrPass(string id)
        {
            var passes = ExrPasses;
            return (id == null ? null : passes.FirstOrDefault(pass => pass.Id == id)) ?? passes.FirstOrDefault();
        }
        internal Size NativeSize
        {
            get
            {
                if (Video != null) return Video.NativeSize;
                if (ExrPasses.Length > 0 && !nativeSize.IsEmpty) return nativeSize;
                if (!nativeSize.IsEmpty) return nativeSize;
                string path = Files.Values.FirstOrDefault();
                if (path == null) return Size.Empty;
                var info = new MagickImageInfo(path);
                nativeSize = new Size(checked((int)info.Width), checked((int)info.Height));
                return nativeSize;
            }
        }

        internal static int DefaultPreviewDivisor(Size size)
        {
            int longer = Math.Max(size.Width, size.Height), shorter = Math.Min(size.Width, size.Height);
            if (longer >= 3840 || shorter >= 2160) return 4;
            return longer <= 1280 && shorter <= 720 ? 1 : 2;
        }
        internal string Key(int index) { return KeyForPass(index, null); }

        internal string KeyForPass(int index, string exrPassId)
        {
            if (index < 0 || index >= Length) return null;
            if (Video != null) return Video.Key(index);
            string path;
            if (!Files.TryGetValue(First + index, out path)) return null;
            var pass = ResolveExrPass(exrPassId);
            return pass == null || (ExrPasses.Length == 1 && pass.Part == 0 && pass.Prefix.Length == 0)
                ? path : pass.Key(path);
        }
        internal static readonly string[] Extensions =
            { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".tga", ".exr", ".dpx", ".webp", ".hdr" };
        private static readonly Regex Number = new Regex(@"^(.*?)([0-9]+)$");

        internal static bool IsImage(string path)
        {
            return Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }

        internal static string GroupKey(string path, bool sequence)
        {
            var match = Number.Match(System.IO.Path.GetFileNameWithoutExtension(path));
            if (!sequence || !match.Success) return path;
            return System.IO.Path.GetDirectoryName(path) + "|" + match.Groups[1].Value + "|" +
                match.Groups[2].Length + System.IO.Path.GetExtension(path);
        }

        internal static MediaSource Open(string path, bool sequence)
        {
            path = System.IO.Path.GetFullPath(path);
            if (!File.Exists(path)) throw new FileNotFoundException("找不到素材。", path);
            var source = new MediaSource { Name = System.IO.Path.GetFileNameWithoutExtension(path),
                Group = GroupKey(path, sequence), Length = 1 };
            if (source.Group == path)
            {
                source.Files.Add(0, path);
                return source;
            }
            var match = Number.Match(source.Name);
            string prefix = match.Groups[1].Value;
            int digits = match.Groups[2].Length;
            string extension = System.IO.Path.GetExtension(path);
            foreach (string file in Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(path)))
            {
                if (!string.Equals(extension, System.IO.Path.GetExtension(file), StringComparison.OrdinalIgnoreCase)) continue;
                var candidate = Number.Match(System.IO.Path.GetFileNameWithoutExtension(file));
                int index;
                if (candidate.Success && candidate.Groups[2].Length == digits &&
                    string.Equals(prefix, candidate.Groups[1].Value, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(candidate.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out index))
                    source.Files[index] = file;
            }
            if (source.Files.Count == 0) { source.Files.Add(0, path); return source; }
            source.First = source.Files.Keys.Min();
            long length = (long)source.Files.Keys.Max() - source.First + 1;
            if (length > 1000000) throw new InvalidDataException("序列跨度超过 1,000,000 帧，请拆分文件。");
            source.Length = (int)length;
            source.Name = prefix.TrimEnd('.', '_', '-', ' ');
            if (source.Name.Length == 0) source.Name = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
            if (source.Name.Length == 0) source.Name = "序列";
            return source;
        }

        internal static Bitmap Decode(string key)
        {
            Size unused;
            return Decode(key, 1, out unused);
        }

        internal static Bitmap Decode(string key, int divisor, out Size originalSize, Size previewBox = default(Size),
            CancellationToken cancel = default(CancellationToken))
        {
            if (key.StartsWith(ExrPass.KeyPrefix, StringComparison.Ordinal))
                return ExrPass.Decode(key, divisor, previewBox, cancel, out originalSize);
            if (key.StartsWith("video:", StringComparison.Ordinal))
                return VideoFrameSource.Decode(key, divisor, previewBox, cancel, out originalSize);
            if (key.StartsWith("demo:", StringComparison.Ordinal))
            {
                string[] args = key.Split(':');
                Bitmap demo = DemoImage.Draw(int.Parse(args[1]), int.Parse(args[2]));
                originalSize = demo.Size;
                Size target = PreviewSize(originalSize, divisor, previewBox);
                if (target == originalSize) return demo;
                using (demo) return MakeProxy(demo, target);
            }
            string ext = System.IO.Path.GetExtension(key).ToLowerInvariant();
            if (ext == ".exr" || ext == ".dpx" || ext == ".tga" || ext == ".webp" || ext == ".hdr")
            {
                using (var image = new MagickImage(key))
                {
                    image.ColorSpace = ColorSpace.sRGB;
                    int width = checked((int)image.Width), height = checked((int)image.Height);
                    originalSize = new Size(width, height);
                    Size target = PreviewSize(originalSize, divisor, previewBox);
                    if (target != originalSize)
                    {
                        width = target.Width; height = target.Height;
                        image.Scale(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
                    }
                    int rowBytes = checked(width * 4);
                    image.Depth = 8;
                    byte[] data = image.ToByteArray(MagickFormat.Bgra);
                    if (data == null || data.Length != checked(rowBytes * height))
                        throw new InvalidDataException("解码器返回的像素大小无效。");
                    var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    try
                    {
                        var locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                        try
                        {
                            if (locked.Stride == rowBytes) Marshal.Copy(data, 0, locked.Scan0, data.Length);
                            else
                                for (int y = 0; y < height; y++)
                                    Marshal.Copy(data, y * rowBytes, IntPtr.Add(locked.Scan0, y * locked.Stride), rowBytes);
                        }
                        finally { bitmap.UnlockBits(locked); }
                        return bitmap;
                    }
                    catch { bitmap.Dispose(); throw; }
                }
            }
            using (var stream = new FileStream(key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var bitmap = new Bitmap(stream))
            {
                originalSize = bitmap.Size;
                Size target = PreviewSize(originalSize, divisor, previewBox);
                return target == originalSize ? new Bitmap(bitmap) : MakeProxy(bitmap, target);
            }
        }

        internal static Size PreviewSize(Size original, int divisor, Size box)
        {
            if (box.IsEmpty)
                return new Size(Math.Max(1, original.Width / divisor), Math.Max(1, original.Height / divisor));
            double scale = Math.Min(1, Math.Min(box.Width / (double)original.Width, box.Height / (double)original.Height));
            return new Size(Math.Max(1, (int)Math.Floor(original.Width * scale)), Math.Max(1, (int)Math.Floor(original.Height * scale)));
        }

        internal static Bitmap MakeProxy(Bitmap original, Size target)
        {
            var result = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (var graphics = Graphics.FromImage(result))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    using (var attributes = new ImageAttributes())
                    {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        graphics.DrawImage(original, new Rectangle(Point.Empty, result.Size), 0, 0,
                            original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                    }
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }
    }

    internal sealed class FrameCache
    {
        private sealed class Entry
        {
            internal Bitmap Bitmap;
            internal Size OriginalSize;
            internal string Error;
            internal long Bytes;
            internal LinkedListNode<string> Node;
        }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> order = new LinkedList<string>();
        private readonly HashSet<string> active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VideoRead> activeVideos = new Dictionary<string, VideoRead>();
        private readonly Dictionary<string, CancellationTokenSource> activeExrs =
            new Dictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> wanted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> priority = new List<string>();
        private readonly HashSet<string> current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Windows.Forms.Control host;
        private readonly Action<bool> ready;
        private readonly int workerCount;
        private readonly Size previewBox;
        private int running;
        private int generation;
        private long bytes;
        private long budget = 384L * 1024 * 1024;
        private int divisor = 1;
        private long frameBytes = 8L * 1024 * 1024;
        private long playbackFrameBytes = 8L * 1024 * 1024;
        internal bool VideoPlayback { get; set; }
        private long ActiveFrameBytes { get { return VideoPlayback ? playbackFrameBytes : frameBytes; } }

        private sealed class VideoRead
        {
            internal readonly string Key;
            internal readonly CancellationTokenSource Stop = new CancellationTokenSource();
            internal VideoRead(string key) { Key = key; }
        }

        internal FrameCache(System.Windows.Forms.Control host, Action<bool> ready, int workerCount = 3,
            Size previewBox = default(Size))
        {
            this.host = host; this.ready = ready; this.workerCount = workerCount; this.previewBox = previewBox;
        }

        internal string Summary { get { return (bytes / (1024 * 1024)) + " / " + (budget / (1024 * 1024)) + " MB"; } }
        internal float FillRatio { get { return Math.Min(1f, bytes / (float)budget); } }
        internal void Configure(int previewDivisor, int cacheMegabytes)
        {
            budget = cacheMegabytes * 1024L * 1024;
            if (divisor == previewDivisor && bytes <= budget) return;
            divisor = previewDivisor;
            Clear();
        }
        internal int LookAhead(int layers, bool hasExr)
        {
            return (int)Math.Max(1, Math.Min(hasExr ? 48 : 16, budget / 2 / (ActiveFrameBytes * Math.Max(1, layers))));
        }
        internal bool CanFit(int layers) { return ActiveFrameBytes * layers <= budget / 2; }
        internal bool Ready(string key) { return key != null && entries.ContainsKey(Variant(key, VideoPlayback)); }
        internal bool UsesPlaybackProxy(string key)
        {
            return key != null && VideoFrameSource.PlaybackDivisor(key, divisor) > divisor;
        }
        private string Variant(string key, bool playback)
        {
            if (!playback) return key;
            int playbackDivisor = VideoFrameSource.PlaybackDivisor(key, divisor);
            return playbackDivisor == divisor ? key : key + "|" + playbackDivisor;
        }
        private Entry Find(string key, bool displayFallback)
        {
            if (key == null) return null;
            Entry entry;
            string preferred = Variant(key, VideoPlayback);
            if (entries.TryGetValue(preferred, out entry)) return entry;
            if (!displayFallback) return null;
            string alternate = Variant(key, !VideoPlayback);
            return alternate != preferred && entries.TryGetValue(alternate, out entry) ? entry : null;
        }
        internal Size OriginalSize(string key, bool displayFallback = false)
        {
            Entry entry = Find(key, displayFallback);
            return entry == null ? Size.Empty : entry.OriginalSize;
        }
        internal string Error(string key)
        {
            Entry entry = Find(key, false);
            return entry == null ? null : entry.Error;
        }
        internal Bitmap Get(string key, bool immediate, bool touch = true, bool displayFallback = false)
        {
            if (key == null) return null;
            Entry entry = Find(key, displayFallback);
            if (entry != null)
            {
                if (touch && entry.Node != order.Last)
                {
                    order.Remove(entry.Node);
                    order.AddLast(entry.Node);
                }
                return entry.Bitmap;
            }
            if (immediate) return Add(key, Read(key, divisor, previewBox));
            return null;
        }

        internal void Request(List<string> keys, int currentCount)
        {
            current.Clear();
            wanted.Clear();
            priority.Clear();
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i] == null) continue;
                string key = Variant(keys[i], VideoPlayback);
                if (i < currentCount) current.Add(key);
                if (wanted.ContainsKey(key)) continue;
                wanted.Add(key, priority.Count);
                priority.Add(key);
            }
            foreach (VideoRead read in activeVideos.Values)
                if (!wanted.ContainsKey(read.Key)) read.Stop.Cancel();
            foreach (var read in activeExrs)
                if (!wanted.ContainsKey(read.Key)) read.Value.Cancel();
            Dispatch();
        }

        private void Dispatch()
        {
            if (host == null || !host.IsHandleCreated || host.IsDisposed) return;
            foreach (string key in priority)
            {
                if (running >= workerCount) break;
                // Leave one worker available for a seek while prefetching.
                if (workerCount > 1 && running == workerCount - 1 && !current.Contains(key)) break;
                if (!wanted.ContainsKey(key) || entries.ContainsKey(key) || active.Contains(key)) continue;
                string videoId = VideoFrameSource.SourceId(key);
                bool exr = key.StartsWith(ExrPass.KeyPrefix, StringComparison.Ordinal);
                // A reader is sequential. Keep a single request per source so obsolete prefetches cannot queue behind its lock.
                if (videoId != null && activeVideos.ContainsKey(videoId)) continue;
                if (exr && activeExrs.ContainsKey(key)) continue;
                active.Add(key);
                VideoRead videoRead = videoId == null ? null : new VideoRead(key);
                var exrRead = exr ? new CancellationTokenSource() : null;
                if (videoRead != null) activeVideos.Add(videoId, videoRead);
                if (exrRead != null) activeExrs.Add(key, exrRead);
                running++;
                int epoch = generation, previewDivisor = divisor;
                string decodeKey = key;
                int split = videoId == null ? -1 : key.LastIndexOf('|');
                if (split >= 0)
                {
                    previewDivisor = int.Parse(key.Substring(split + 1), CultureInfo.InvariantCulture);
                    decodeKey = key.Substring(0, split);
                }
                Task.Run(delegate
                {
                    CancellationToken token = videoRead != null ? videoRead.Stop.Token :
                        exrRead != null ? exrRead.Token : CancellationToken.None;
                    Entry result = Read(decodeKey, previewDivisor, previewBox, token);
                    try
                    {
                        host.BeginInvoke((Action)delegate { Complete(key, epoch, result, videoId, videoRead, exrRead); });
                    }
                    catch (InvalidOperationException)
                    {
                        if (result != null && result.Bitmap != null) result.Bitmap.Dispose();
                        if (exrRead != null) exrRead.Dispose();
                    }
                });
            }
        }

        private void Complete(string key, int epoch, Entry result, string videoId, VideoRead read,
            CancellationTokenSource exrRead)
        {
            running--;
            bool canceled = false;
            if (read != null)
            {
                activeVideos.Remove(videoId);
                canceled = read.Stop.IsCancellationRequested;
                read.Stop.Dispose();
            }
            if (exrRead != null)
            {
                activeExrs.Remove(key);
                canceled = exrRead.IsCancellationRequested;
                exrRead.Dispose();
            }
            if (epoch == generation)
            {
                active.Remove(key);
                if (!canceled && result != null && wanted.ContainsKey(key) && !entries.ContainsKey(key))
                {
                    Add(key, result);
                    ready(current.Contains(key));
                    Dispatch();
                    return;
                }
            }
            if (result != null && result.Bitmap != null) result.Bitmap.Dispose();
            Dispatch();
        }
        private static Entry Read(string key, int previewDivisor, Size previewBox, CancellationToken cancel = default(CancellationToken))
        {
            try
            {
                Size originalSize;
                Bitmap bitmap = MediaSource.Decode(key, previewDivisor, out originalSize, previewBox, cancel);
                return new Entry { Bitmap = bitmap, OriginalSize = originalSize, Bytes = 4L * bitmap.Width * bitmap.Height };
            }
            catch (Exception ex)
            {
                return cancel.IsCancellationRequested ? null : new Entry { Error = ex.Message };
            }
        }
        private Bitmap Add(string key, Entry result)
        {
            Entry old;
            if (entries.TryGetValue(key, out old)) Remove(old);
            result.Node = order.AddLast(key);
            entries.Add(key, result);
            bytes += result.Bytes;
            if (key.StartsWith("video:", StringComparison.Ordinal) && key.IndexOf('|') >= 0)
                playbackFrameBytes = Math.Max(playbackFrameBytes, result.Bytes);
            else
            {
                frameBytes = Math.Max(frameBytes, result.Bytes);
                if (VideoPlayback) playbackFrameBytes = Math.Max(playbackFrameBytes, result.Bytes);
            }
            while ((bytes > budget || entries.Count > 768) && entries.Count > 1)
            {
                // Protect the displayed frame and prefer nearer prefetches regardless of completion order.
                LinkedListNode<string> victim = null;
                int lowestPriority = -1;
                for (var node = order.First; node != null; node = node.Next)
                {
                    if (current.Contains(node.Value)) continue;
                    int rank;
                    if (!wanted.TryGetValue(node.Value, out rank)) { victim = node; break; }
                    if (rank > lowestPriority) { victim = node; lowestPriority = rank; }
                }
                if (victim == result.Node && wanted.ContainsKey(key))
                {
                    wanted.Remove(key);
                    Remove(result);
                    return null;
                }
                if (victim == null || victim == result.Node) victim = order.First;
                wanted.Remove(victim.Value);
                Remove(entries[victim.Value]);
            }
            return result.Bitmap;
        }
        private void Remove(Entry entry)
        {
            if (entry.Bitmap != null) entry.Bitmap.Dispose();
            bytes -= entry.Bytes;
            entries.Remove(entry.Node.Value);
            order.Remove(entry.Node);
        }
        internal void Clear()
        {
            generation++;
            foreach (VideoRead read in activeVideos.Values) read.Stop.Cancel();
            foreach (var read in activeExrs.Values) read.Cancel();
            active.Clear(); wanted.Clear(); current.Clear(); priority.Clear();
            frameBytes = 8L * 1024 * 1024 / (divisor * divisor);
            playbackFrameBytes = frameBytes;
            while (order.Count > 0) Remove(entries[order.First.Value]);
        }
    }

    internal static class DemoImage
    {
        internal static Bitmap Draw(int variant, int frame)
        {
            const int width = 960, height = 540;
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var sky = new LinearGradientBrush(new Rectangle(0, 0, width, height),
                variant == 1 ? Color.FromArgb(15, 31, 56) : Color.FromArgb(32, 25, 58),
                variant == 1 ? Color.FromArgb(159, 95, 112) : Color.FromArgb(188, 92, 72), 82))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillRectangle(sky, 0, 0, width, height);
                double t = frame * Math.PI * 2 / 96;
                for (int i = 0; i < 62; i++)
                {
                    int x = (i * 109 + i * i * 13) % width, y = (i * 43 + i * i * 7) % 285;
                    using (var star = new SolidBrush(Color.FromArgb(75 + i % 80, 220, 229, 250)))
                        g.FillEllipse(star, x, y, i % 8 == 0 ? 2.4f : 1.2f, i % 8 == 0 ? 2.4f : 1.2f);
                }
                float sunX = (float)(570 + 70 * Math.Sin(t)), sunY = (float)(244 - 28 * Math.Cos(t));
                for (int i = 23; i >= 0; i--)
                {
                    int size = 132 + i * 12;
                    using (var halo = new SolidBrush(Color.FromArgb(i == 0 ? 220 : 7, 255, 168, 110)))
                        g.FillEllipse(halo, sunX - size / 2, sunY - size / 2, size, size);
                }
                for (int layer = 0; layer < 4; layer++)
                {
                    var points = new List<PointF>();
                    for (int i = 0; i <= 64; i++)
                    {
                        float x = i * width / 64f;
                        double phase = i * (.22 + layer * .054) + layer * 1.7 + .21 * Math.Sin(t);
                        double ridge = Math.Sin(phase) * 36 + Math.Cos(phase * 2.37) * 12 + Math.Sin(phase * 6.13) * 9;
                        points.Add(new PointF(x, (float)(315 + layer * 47 + ridge)));
                    }
                    points.Add(new PointF(width, height)); points.Add(new PointF(0, height));
                    using (var land = new SolidBrush(Color.FromArgb(75 - layer * 16, 72 - layer * 15, 105 - layer * 17)))
                        g.FillPolygon(land, points.ToArray());
                    using (var edge = new Pen(Color.FromArgb(55, 194, 164, 202)))
                        g.DrawLines(edge, points.Take(65).ToArray());
                }
                using (var font = new Font("Consolas", 13))
                using (var text = new SolidBrush(Color.FromArgb(205, 233, 230, 242)))
                {
                    g.DrawString("HORIZON  /  RENDER TEST", font, text, 26, 24);
                    g.DrawString("V0" + variant + "   •   " + (1001 + frame), font, text, 26, 490);
                }
            }
            return bmp;
        }
    }
}
