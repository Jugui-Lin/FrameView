using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace FrameDeck
{
    public sealed class ReviewPreset
    {
        public string Name { get; set; }
        public int PreviewDivisor { get; set; }
        public int CacheMegabytes { get; set; }
        public string Channel { get; set; }
        public bool Compare { get; set; }
        public bool SideBySide { get; set; }
        public float Wipe { get; set; }
        public decimal Fps { get; set; }
        public decimal PlaybackSpeed { get; set; }
        public string PreviewFilter { get; set; }
        public string PreviewBackground { get; set; }
        public string Repeat { get; set; }

        internal static ReviewPreset Capture(string name, Project project)
        {
            return new ReviewPreset { Name = name, PreviewDivisor = project.PreviewDivisor, CacheMegabytes = project.CacheMegabytes,
                Channel = project.Channel, Compare = project.Compare, SideBySide = project.CompareSideBySide, Wipe = project.Wipe,
                Fps = project.Fps, PlaybackSpeed = project.PlaybackSpeed, Repeat = project.Repeat,
                PreviewFilter = project.PreviewFilter, PreviewBackground = project.PreviewBackground };
        }

        internal void Apply(Project project)
        {
            project.PreviewDivisor = PreviewDivisor; project.CacheMegabytes = CacheMegabytes;
            project.Channel = Channel; project.Compare = Compare; project.CompareSideBySide = SideBySide; project.Wipe = Wipe;
            if (Fps > 0) project.Fps = Fps;
            if (PlaybackSpeed > 0) project.PlaybackSpeed = PlaybackSpeed;
            if (PreviewFilter != null) project.PreviewFilter = PreviewFilter;
            if (PreviewBackground != null) project.PreviewBackground = PreviewBackground;
            if (Repeat != null) project.Repeat = Repeat;
        }

        internal bool Matches(Project project)
        {
            return PreviewDivisor == project.PreviewDivisor && CacheMegabytes == project.CacheMegabytes &&
                Channel == project.Channel && Compare == project.Compare &&
                (!Compare || (SideBySide == project.CompareSideBySide && (SideBySide || Math.Abs(Wipe - project.Wipe) < .001f))) &&
                (Fps == 0 || Fps == project.Fps) && (PlaybackSpeed == 0 || PlaybackSpeed == project.PlaybackSpeed) &&
                (PreviewFilter == null || PreviewFilter == project.PreviewFilter) &&
                (PreviewBackground == null || PreviewBackground == project.PreviewBackground) &&
                (Repeat == null || Repeat == project.Repeat);
        }

        internal bool IsValid
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Name) && Name.Length <= 32 && !Name.Any(char.IsControl) &&
                    (PreviewDivisor == 1 || PreviewDivisor == 2 || PreviewDivisor == 4) &&
                    (CacheMegabytes == 384 || CacheMegabytes == 768 || CacheMegabytes == 1536) &&
                    new[] { "RGB", "R", "G", "B", "A", "L" }.Contains(Channel) &&
                    Wipe >= .02f && Wipe <= .98f && Fps >= 1 && Fps <= 240 &&
                    (PlaybackSpeed == 0 || (PlaybackSpeed >= .25m && PlaybackSpeed <= 4)) &&
                    (PreviewFilter == null || Project.PreviewFilters.Contains(PreviewFilter)) &&
                    (PreviewBackground == null || Project.PreviewBackgrounds.Contains(PreviewBackground)) &&
                    (Repeat == "循环" || Repeat == "单次" || Repeat == "往返");
            }
        }
    }

    internal sealed class ReviewLibrary
    {
        internal static readonly ReviewPreset SmoothPlayback = SmoothPlaybackAt(2);
        internal static readonly ReviewPreset SmoothPlaybackQuarter = SmoothPlaybackAt(4);
        internal static readonly ReviewPreset[] BuiltIn =
        {
            new ReviewPreset { Name = "原画检查", PreviewDivisor = 1, CacheMegabytes = 384, Channel = "RGB", Wipe = .5f },
            SmoothPlayback,
            new ReviewPreset { Name = "版本对比", PreviewDivisor = 2, CacheMegabytes = 768, Channel = "RGB", Compare = true, SideBySide = true, Wipe = .5f },
            new ReviewPreset { Name = "透明检查", PreviewDivisor = 1, CacheMegabytes = 384, Channel = "A", Wipe = .5f }
        };

        private static ReviewPreset SmoothPlaybackAt(int divisor)
        {
            return new ReviewPreset { Name = "流畅播放", PreviewDivisor = divisor, CacheMegabytes = 768, Channel = "RGB", Wipe = .5f };
        }
        internal List<ReviewPreset> Custom { get; private set; }
        private readonly string path;
        private static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 65536 }; }

        internal ReviewLibrary(string path = null)
        {
            this.path = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FrameDeck", "review-presets.json"));
            Reload();
        }

        internal void Reload() { Custom = Read(); }

        private List<ReviewPreset> Read()
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length <= 65536)
                {
                    var saved = Json().Deserialize<List<ReviewPreset>>(File.ReadAllText(path));
                    if (saved != null)
                    {
                        var names = new HashSet<string>(BuiltIn.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
                        return saved.Where(x => x != null && x.IsValid && names.Add(x.Name)).Take(40).ToList();
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return new List<ReviewPreset>();
        }

        internal void Save(ReviewPreset preset)
        {
            if (!preset.IsValid) throw new InvalidDataException("预设名称或设置无效。");
            if (BuiltIn.Any(x => SameName(x.Name, preset.Name))) throw new InvalidDataException("请选择与内置预设不同的名称。");
            Update(items =>
            {
                int index = items.FindIndex(x => SameName(x.Name, preset.Name));
                if (index >= 0) items[index] = preset;
                else
                {
                    if (items.Count >= 40) throw new InvalidDataException("已保存 40 个自定义预设。");
                    items.Add(preset);
                }
            });
        }

        internal void Remove(ReviewPreset preset)
        {
            Update(items => items.RemoveAll(x => SameName(x.Name, preset.Name)));
        }

        internal static bool SameName(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private void Update(Action<List<ReviewPreset>> edit)
        {
            string key;
            using (var hash = SHA256.Create())
                key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
            using (var gate = new Mutex(false, @"Local\FrameDeckPresets-" + key))
            {
                bool acquired = false;
                try
                {
                    try { acquired = gate.WaitOne(3000); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("另一个窗口正在保存预设，请稍后重试。");
                    var updated = Read();
                    edit(updated);
                    Write(updated);
                }
                finally { if (acquired) gate.ReleaseMutex(); }
            }
        }

        private void Write(List<ReviewPreset> updated)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, Json().Serialize(updated), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                Custom = updated;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal sealed class MoviePreset
    {
        internal static readonly MoviePreset Original = new MoviePreset("原尺寸", 0, 0, 18, "fast");
        internal static readonly MoviePreset Delivery = new MoviePreset("交付 · 4K UHD", 3840, 2160, 18, "medium");
        internal static readonly MoviePreset Review = new MoviePreset("审片 · 1080p", 1920, 1080, 21, "fast");
        internal static readonly MoviePreset Quick = new MoviePreset("快速 · 720p", 1280, 720, 25, "veryfast");
        internal readonly string Name, EncoderSpeed;
        internal readonly int Crf;
        private readonly int maxWidth, maxHeight;

        internal MoviePreset(string name, int width, int height, int crf, string speed)
        {
            Name = name; maxWidth = width; maxHeight = height; Crf = crf; EncoderSpeed = speed;
        }
        internal float Scale(Size original)
        {
            if (maxWidth == 0) return 1;
            return Math.Min(1f, Math.Min(maxWidth / (float)original.Width, maxHeight / (float)original.Height));
        }
        internal Size OutputSize(Size original)
        {
            return ScaledSize(original, Scale(original));
        }
        internal static Size ScaledSize(Size original, float scale)
        {
            return new Size(Math.Max(1, (int)Math.Round(original.Width * scale)), Math.Max(1, (int)Math.Round(original.Height * scale)));
        }
    }
}
