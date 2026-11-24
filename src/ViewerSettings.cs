using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FrameDeck
{
    public sealed class ViewerDefaults
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameDeck", "defaults.json");
        internal static readonly int[] PreviewLevels = { 1, 2, 4 };
        internal const int MinimumCacheMegabytes = 64, MaximumCacheMegabytes = 1048576;
        internal static readonly string[] LoopModes = { "循环", "单次", "往返" };
        internal static readonly int[] GifEdges = { 480, 640, 960, 1280, 0 };
        internal static readonly int[] GifPalettes = { 64, 128, 256 };

        public decimal Fps { get; set; }
        public int PreviewDivisor { get; set; }
        public int CacheMegabytes { get; set; }
        public string Repeat { get; set; }
        public int GifInterval { get; set; }
        public int GifEdge { get; set; }
        public int GifColors { get; set; }

        public ViewerDefaults()
        {
            Fps = 24; PreviewDivisor = 1; CacheMegabytes = CacheMemory.Megabytes(10); Repeat = "循环";
            GifInterval = 2; GifEdge = 640; GifColors = 128;
        }

        internal Project NewProject()
        {
            return new Project { Fps = Fps, PreviewDivisor = PreviewDivisor, CacheMegabytes = CacheMegabytes, Repeat = Repeat };
        }

        internal static ViewerDefaults Load(string path = null)
        {
            try
            {
                var file = new FileInfo(path ?? FilePath);
                if (file.Exists && file.Length <= 8192)
                {
                    var stored = new JavaScriptSerializer().Deserialize<ViewerDefaults>(File.ReadAllText(file.FullName));
                    if (stored != null)
                    {
                        if (stored.Fps < 1 || stored.Fps > 240) stored.Fps = 24;
                        if (!PreviewLevels.Contains(stored.PreviewDivisor)) stored.PreviewDivisor = 1;
                        if (stored.CacheMegabytes < MinimumCacheMegabytes || stored.CacheMegabytes > MaximumCacheMegabytes)
                            stored.CacheMegabytes = 384;
                        if (!LoopModes.Contains(stored.Repeat)) stored.Repeat = "循环";
                        if (stored.GifInterval < 1 || stored.GifInterval > 120) stored.GifInterval = 2;
                        if (!GifEdges.Contains(stored.GifEdge)) stored.GifEdge = 640;
                        if (!GifPalettes.Contains(stored.GifColors)) stored.GifColors = 128;
                        return stored;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return new ViewerDefaults();
        }

        internal void Save(string path = null)
        {
            path = path ?? FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal sealed partial class MainForm
    {
        private ViewerDefaults defaults;

        private void OpenSettings()
        {
            var edited = new ViewerDefaults
            {
                Fps = defaults.Fps, PreviewDivisor = defaults.PreviewDivisor, CacheMegabytes = defaults.CacheMegabytes,
                Repeat = defaults.Repeat, GifInterval = defaults.GifInterval, GifEdge = defaults.GifEdge, GifColors = defaults.GifColors
            };
            AppearanceMode requestedAppearance = appearance;
            using (var dialog = new Form
            {
                Text = "FrameView 设置", Font = Theme.Small, BackColor = Theme.Panel, ForeColor = Theme.Text,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false, AutoScaleMode = AutoScaleMode.None
            })
            {
                dialog.HandleCreated += delegate { ApplyCaptionTheme(dialog.Handle); };
                SettingsLabel(dialog, "外观与新项目默认值", 20, 13, 330, Theme.OnContainer, strong: true);
                SettingsLabel(dialog, BrandInfo.Version, 366, 13, 114, Theme.Muted).TextAlign = ContentAlignment.MiddleRight;
                AddSettingsChoice(dialog, "主题", 46, appearance, new[] { AppearanceMode.System, AppearanceMode.Light, AppearanceMode.Dark },
                    mode => mode == AppearanceMode.System ? "跟随系统" : mode == AppearanceMode.Light ? "白天" : "夜间",
                    mode => requestedAppearance = mode);
                var rate = AddSettingsNumber(dialog, "默认帧率", 84, 1, 240, edited.Fps);
                rate.DecimalPlaces = 3;
                SettingsLabel(dialog, "首段视频采用源帧率", 318, 84, 160, Theme.Muted);
                AddSettingsChoice(dialog, "预览分辨率", 122, edited.PreviewDivisor, ViewerDefaults.PreviewLevels,
                    level => level == 1 ? "原始" : "1/" + level + " 预览", level => edited.PreviewDivisor = level);
                SettingsLabel(dialog, "帧缓存上限", 20, 160, 152, Theme.Text);
                Button cacheLimit = null;
                cacheLimit = AddSettingsButton(dialog, CacheMemory.Label(edited.CacheMegabytes), 174, 160, 220, delegate
                {
                    int value;
                    if (!AskCacheLimit(edited.CacheMegabytes, out value)) return;
                    edited.CacheMegabytes = value;
                    cacheLimit.Text = CacheMemory.Label(value);
                });
                AddSettingsChoice(dialog, "播放方式", 198, edited.Repeat, ViewerDefaults.LoopModes,
                    mode => mode, mode => edited.Repeat = mode);
                SettingsLabel(dialog, "导出", 20, 239, 460, Theme.OnContainer, strong: true);
                var interval = AddSettingsNumber(dialog, "GIF 帧间隔", 273, 1, 120, edited.GifInterval);
                SettingsLabel(dialog, "每隔 N 帧取一帧", 318, 273, 160, Theme.Muted);
                AddSettingsChoice(dialog, "GIF 输出尺寸", 311, edited.GifEdge, ViewerDefaults.GifEdges,
                    edge => edge == 0 ? "原尺寸" : "长边 " + edge + " px", edge => edited.GifEdge = edge);
                AddSettingsChoice(dialog, "GIF 质量", 349, edited.GifColors, ViewerDefaults.GifPalettes,
                    colors => (colors == 64 ? "精简" : colors == 128 ? "均衡" : "精细") + " · " + colors + " 色",
                    colors => edited.GifColors = colors);
                SettingsLabel(dialog, "片段导出目录", 20, 387, 152, Theme.Text);
                var folder = SettingsLabel(dialog, GetExportFolder(), 174, 387, 218, Theme.Muted);
                SetToolbarHint(folder, folder.Text);
                AddSettingsButton(dialog, "更改…", 400, 387, 80, delegate
                {
                    PickExportFolder(dialog);
                    folder.Text = GetExportFolder();
                    SetToolbarHint(folder, folder.Text);
                });

                AddSettingsButton(dialog, "快捷键", 20, 437, 88, () => Help(dialog));
                var cancel = AddSettingsButton(dialog, "取消", 276, 437, 84, delegate { });
                cancel.DialogResult = DialogResult.Cancel;
                dialog.CancelButton = cancel;
                var save = AddSettingsButton(dialog, "保存设置", 370, 437, 110, delegate
                {
                    edited.Fps = rate.Value;
                    edited.GifInterval = (int)interval.Value;
                    try
                    {
                        edited.Save();
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { MaterialMessageBox.Show(dialog, ex.Message, "无法保存设置"); }
                }, true);
                dialog.AcceptButton = save;
                dialog.Scale(new SizeF(UiScale.Factor, UiScale.Factor));
                Size contentSize = UiScale.Box(0, 0, 500, 485).Size;
                int availableHeight = Screen.FromControl(this).WorkingArea.Height - UiScale.Px(54);
                if (contentSize.Height > availableHeight)
                {
                    dialog.AutoScroll = true;
                    dialog.AutoScrollMinSize = contentSize;
                    dialog.ClientSize = new Size(contentSize.Width + SystemInformation.VerticalScrollBarWidth, availableHeight);
                }
                else dialog.ClientSize = contentSize;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
            }
            defaults = edited;
            if (!dirty && projectFile == null && Session.Layers.Count == 0) Replace(defaults.NewProject(), null);
            SetStatus("已保存默认设置。");
            SetAppearance(requestedAppearance);
        }

        private static Label SettingsLabel(Form dialog, string text, int x, int y, int width, Color color, bool strong = false)
        {
            var label = LabelOf(text, width, 30, color);
            label.Location = new Point(x, y);
            if (strong) label.Font = Theme.Strong;
            dialog.Controls.Add(label);
            return label;
        }

        private static Button AddSettingsButton(Form dialog, string text, int x, int y, int width, Action apply, bool primary = false)
        {
            var button = ButtonOf(text, width, apply, primary);
            button.Location = new Point(x, y);
            button.TabStop = true;
            dialog.Controls.Add(button);
            return button;
        }

        private static NumericUpDown AddSettingsNumber(Form dialog, string label, int y, int min, int max, decimal value)
        {
            SettingsLabel(dialog, label, 20, y, 152, Theme.Text);
            var number = NumberOf(min, max, value, 122, integratedArrows: true);
            number.AccessibleName = label;
            dialog.Controls.Add(new MaterialNumberHost(number, 132) { Location = new Point(174, y) });
            return number;
        }

        private void AddSettingsChoice<T>(Form dialog, string label, int y, T initial, T[] options, Func<T, string> text, Action<T> apply)
        {
            SettingsLabel(dialog, label, 20, y, 152, Theme.Text);
            T current = initial;
            Button button = null;
            button = AddSettingsButton(dialog, text(initial) + " ▾", 174, y, 306, delegate
            {
                ShowChoices(button, options.Select(value => new Choice(text(value), delegate
                {
                    current = value;
                    apply(value);
                    button.Text = text(value) + " ▾";
                }, Equals(value, current))), title: label);
            });
            button.AccessibleName = label;
        }
    }
}
