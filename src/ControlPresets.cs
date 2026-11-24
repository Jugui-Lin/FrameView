using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class MainForm
    {
        private sealed class Choice
        {
            internal readonly string Label;
            internal readonly string Shortcut;
            internal readonly Action Apply;
            internal readonly bool Selected, Enabled;

            internal Choice(string label, Action apply, bool selected = false, bool enabled = true, string shortcut = null)
            {
                Label = label; Shortcut = shortcut; Apply = apply; Selected = selected; Enabled = enabled;
            }
        }

        private Button fpsButton, speedButton, loopButton, zoomButton, displayButton, layerPresets, range;
        private readonly List<Button> layerValueMenus = new List<Button>();
        private static readonly decimal[] CommonFps =
            { 1, 3, 6, 12, 15, 16, 18, 23.976m, 24, 25, 29.970m, 30, 48, 50, 59.940m, 60, 90, 96, 119.880m, 120 };
        private static readonly decimal[] CommonSpeeds = { .25m, .5m, .75m, 1m, 1.25m, 1.5m, 2m, 3m, 4m };

        private void ShowChoices(Control anchor, IEnumerable<Choice> choices, Point? position = null, bool above = false, string title = null)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false };
            Choice[] entries = choices.ToArray();
            int minimum = UiScale.Px(178);
            int width = Math.Max(minimum,
                entries.Where(x => x != null).Select(x => MaterialMenuItem.Measure(x.Label, x.Shortcut)).DefaultIfEmpty(minimum).Max());
            if (!string.IsNullOrEmpty(title)) menu.Items.Add(new MaterialMenuHeading(title, width));
            foreach (Choice choice in entries)
            {
                if (choice == null) { menu.Items.Add(new ToolStripSeparator()); continue; }
                var entry = new MaterialMenuItem(choice.Label, choice.Shortcut, width, choice.Selected, choice.Enabled);
                entry.Click += delegate { choice.Apply(); };
                menu.Items.Add(entry);
            }
            int gap = UiScale.Px(5);
            ShowPopup(menu, anchor, position ?? new Point(0, above ? -gap : anchor.Height + gap), above);
        }

        private void ShowPopup(ToolStripDropDown menu, Control anchor, Point position, bool above = false)
        {
            MaterialMenuRenderer.Style(menu);
            // The native menu measures its default label column, not the additional painted shortcut column.
            var context = menu as ContextMenuStrip;
            if (context != null)
            {
                int contentWidth = context.Items.Cast<ToolStripItem>().OfType<MaterialMenuItem>()
                    .Select(item => item.Width).DefaultIfEmpty(UiScale.Px(178)).Max();
                context.MinimumSize = new Size(contentWidth + context.Padding.Horizontal + 4, 0);
            }
            MaterialMenuRenderer.FadeIn(menu);
            // WinForms still uses the native menu handle after raising Closed.
            menu.Closed += delegate
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)menu.Dispose);
            };
            Form owner = anchor.FindForm();
            Point screenPosition = anchor.PointToScreen(position);
            int right = owner == null ? Screen.FromControl(anchor).WorkingArea.Right : owner.Right;
            int popupWidth = Math.Max(menu.MinimumSize.Width, menu.GetPreferredSize(Size.Empty).Width);
            bool alignRight = screenPosition.X + popupWidth > right;
            if (alignRight && position.X == 0) position.X = anchor.Width;
            menu.Show(anchor, position, above ?
                (alignRight ? ToolStripDropDownDirection.AboveLeft : ToolStripDropDownDirection.AboveRight) :
                (alignRight ? ToolStripDropDownDirection.BelowLeft : ToolStripDropDownDirection.BelowRight));
        }

        private static IEnumerable<Choice> NumericChoices(decimal[] values, decimal current, Action<decimal> apply,
            Func<decimal, string> label)
        {
            foreach (decimal value in values)
                yield return new Choice(label(value), delegate { apply(value); }, current == value);
        }

        private void FpsMenu()
        {
            var menu = new FpsDropDown(CommonFps, Session.Fps, value => fps.Value = value);
            ShowPopup(menu, fpsButton, new Point(0, -UiScale.Px(5)), true);
        }

        private void SpeedMenu()
        {
            var options = NumericChoices(CommonSpeeds, Session.PlaybackSpeed, SetSpeed,
                value => value.ToString("0.##") + "×" + (value == 1 ? " · 正常" : "")).ToList();
            options.Add(null);
            options.Add(new Choice("自定义倍率…", delegate
            {
                decimal value;
                if (AskNumber("自定义播放倍率", "0.25–4 倍，导出沿用时间轴 FPS。", Session.PlaybackSpeed, .25m, 4, 2, out value))
                    SetSpeed(value);
            }));
            options.Add(null);
            bool canPlay = WorkIn < WorkOut;
            options.Add(new Choice("正向播放", delegate { Play(1); }, enabled: canPlay, shortcut: "↑"));
            options.Add(new Choice("反向播放", delegate { Play(-1); }, enabled: canPlay, shortcut: "↓"));
            ShowChoices(speedButton, options, above: true, title: "播放倍率");
        }

        private void SetSpeed(decimal speed)
        {
            if (Session.PlaybackSpeed == speed) return;
            Session.PlaybackSpeed = speed;
            lastTick = clock.ElapsedTicks;
            SyncTransportPresets();
            Dirty();
        }

        private void SyncTransportPresets()
        {
            speedButton.Text = Session.PlaybackSpeed.ToString("0.##") + "× ▾";
            loopButton.Text = Session.Repeat + " ▾";
        }

        private void LoopMenu()
        {
            var labels = new[] { "循环 · 默认", "单次播放", "往返播放" };
            ShowChoices(loopButton, new[] { "循环", "单次", "往返" }.Select((mode, i) => new Choice(labels[i], delegate
            {
                if (Session.Repeat == mode) return;
                Session.Repeat = mode;
                SyncTransportPresets(); Dirty(); RefreshFrame();
            }, Session.Repeat == mode)), above: true);
        }

        private void ZoomMenu()
        {
            Size original = viewer.GetImageSize(Frame, false);
            bool available = !original.IsEmpty;
            var choices = new List<Choice> { new Choice("适配窗口", viewer.Fit, viewer.IsFit), null };
            foreach (int percent in new[] { 12, 25, 50, 100, 200, 400, 800 })
            {
                int value = percent;
                choices.Add(new Choice(value + "%" + (value == 100 ? " · 原尺寸" : ""),
                    delegate { viewer.SetZoom(value / 100f); },
                    viewer.FixedZoom.HasValue && Math.Abs(viewer.FixedZoom.Value - value / 100f) < .0001f, available));
            }
            choices.Add(null);
            choices.Add(new Choice("自定义缩放…", delegate
            {
                decimal value;
                if (AskNumber("自定义缩放", "输入 1–1600 的缩放百分比。", Math.Round((decimal)viewer.DisplayZoom(original) * 100),
                    1, 1600, 1, out value)) viewer.SetZoom((float)value / 100);
            }, enabled: available));
            choices.Add(new Choice("居中画面", viewer.CenterImage, enabled: available));
            ShowChoices(zoomButton, choices, title: "监看缩放");
        }

        private bool AskNumber(string title, string prompt, decimal initial, decimal minimum, decimal maximum,
            int places, out decimal value)
        {
            string input = AskText(title, prompt, initial.ToString("0.##"), 10);
            value = 0;
            if (input == null) return false;
            if (decimal.TryParse(input, out value) && value >= minimum && value <= maximum)
            {
                value = Math.Round(value, places);
                return true;
            }
            SetStatus("请输入 " + minimum + "–" + maximum + " 之间的数值。");
            return false;
        }

        private void CompareChoices()
        {
            bool available = previewLayers.Count >= 2 || Session.Layers.Count(x => x.Visible) >= 2;
            var choices = new List<Choice>
            {
                new Choice("图层合成", delegate { SetCompare(false, false); }, !Session.Compare),
                new Choice("A/B 擦拭", delegate { SetCompare(true, false); }, Session.Compare && !Session.CompareSideBySide, available),
                new Choice("A/B 并排", delegate { SetCompare(true, true); }, Session.Compare && Session.CompareSideBySide, available),
                null
            };
            bool wiping = Session.Compare && !Session.CompareSideBySide && available;
            foreach (int position in new[] { 25, 50, 75 })
            {
                float fraction = position / 100f;
                choices.Add(new Choice("分隔线 " + position + "%" + (position == 50 ? " · 居中" : ""),
                    delegate { SetWipe(fraction); }, wiping && Math.Abs(Session.Wipe - fraction) < .001f, wiping));
            }
            ShowChoices(compare, choices, title: "A/B 对比");
        }

        private void PreviewChoices()
        {
            var choices = NumericChoices(new decimal[] { 1, 2, 4 }, Session.PreviewDivisor,
                value => SetPreview((int)value, Session.CacheMegabytes),
                value => value == 1 ? "原始分辨率" : "1/" + value + " 预览").ToList();
            choices.Add(null);
            bool presetCache = false;
            foreach (int percent in new[] { 10, 20, 30 })
            {
                int megabytes = CacheMemory.Megabytes(percent);
                bool selected = Session.CacheMegabytes == megabytes;
                presetCache |= selected;
                choices.Add(new Choice("缓存 " + percent + "% · " + CacheMemory.Capacity(megabytes),
                    delegate { SetPreview(Session.PreviewDivisor, megabytes); }, selected));
            }
            choices.Add(new Choice("自定义缓存…（" + CacheMemory.Label(Session.CacheMegabytes) + "）", delegate
            {
                int value;
                if (AskCacheLimit(Session.CacheMegabytes, out value)) SetPreview(Session.PreviewDivisor, value);
            }, !presetCache));
            choices.Add(null);
            choices.Add(new Choice("恢复默认 · 原始 / 10% 内存", delegate { SetPreview(1, CacheMemory.Megabytes(10)); }));
            ShowChoices(previewModeButton, choices, title: "预览与缓存");
        }

        private void DisplayMenu()
        {
            string[] filters = { "滤镜 · 自动", "滤镜 · 最近邻", "滤镜 · 线性", "滤镜 · 高质量" };
            string[] backgrounds = { "透明区 · 棋盘格", "透明区 · 黑色", "透明区 · 灰色", "透明区 · 白色" };
            var choices = Project.PreviewFilters.Select((filter, i) =>
                new Choice(filters[i], delegate { SetDisplay(filter, Session.PreviewBackground); }, Session.PreviewFilter == filter)).ToList();
            choices.Add(null);
            choices.AddRange(Project.PreviewBackgrounds.Select((background, i) =>
                new Choice(backgrounds[i], delegate { SetDisplay(Session.PreviewFilter, background); }, Session.PreviewBackground == background)));
            choices.Add(null);
            choices.Add(new Choice("恢复默认显示", delegate { SetDisplay("Auto", "Checker"); }));
            ShowChoices(displayButton, choices, title: "显示设置");
        }

        private void SetDisplay(string filter, string background)
        {
            if (Session.PreviewFilter == filter && Session.PreviewBackground == background) return;
            Session.PreviewFilter = filter; Session.PreviewBackground = background;
            Dirty(); viewer.Invalidate();
        }

        private void RangeMenu()
        {
            bool hasLayers = Session.Layers.Count > 0;
            int before = Session.Marks.Where(mark => mark <= Frame && mark >= 0).DefaultIfEmpty(-1).Max();
            int after = Session.Marks.Where(mark => mark > Frame && mark <= MaxFrame).DefaultIfEmpty(-1).Min();
            ShowChoices(range, new[]
            {
                new Choice("整条时间轴 · 默认", ResetRange, !HasWorkRange, hasLayers),
                new Choice("选中图层范围", delegate { ApplyRange(Selection.Start, Selection.End); }, enabled: Selection != null),
                new Choice("播放头两侧标记之间", delegate { ApplyRange(before, after); }, enabled: before >= 0 && after >= 0),
                null,
                new Choice("播放头附近 1 秒", delegate { RangeAround(1); }, enabled: hasLayers),
                new Choice("播放头附近 3 秒", delegate { RangeAround(3); }, enabled: hasLayers),
                new Choice("播放头附近 5 秒", delegate { RangeAround(5); }, enabled: hasLayers),
                null,
                new Choice("当前帧设为入点", delegate { SetRange(true); }, enabled: hasLayers, shortcut: "Alt+Home"),
                new Choice("当前帧设为出点", delegate { SetRange(false); }, enabled: hasLayers, shortcut: "Alt+End")
            }, above: true);
        }

        private void RangeAround(int seconds)
        {
            int length = Math.Min(MaxFrame + 1, Math.Max(1, (int)Math.Round(Session.Fps * seconds)));
            int first = Math.Max(0, Math.Min(MaxFrame + 1 - length, Frame - length / 2));
            ApplyRange(first, first + length - 1);
        }

        private void ApplyRange(int first, int last)
        {
            Session.RangeIn = Math.Max(0, Math.Min(MaxFrame, first));
            Session.RangeOut = Math.Max(Session.RangeIn, Math.Min(MaxFrame, last));
            Dirty(); RefreshFrame();
        }

        internal void TimelineMenu(Point position)
        {
            bool hasLayers = Session.Layers.Count > 0;
            var choices = new List<Choice>
            {
                new Choice("显示整条时间轴", timeline.ResetView),
                new Choice("显示工作区", delegate { timeline.ShowFrames(WorkIn, WorkOut); }, enabled: hasLayers),
                new Choice("定位播放头", timeline.FocusPlayhead, enabled: hasLayers, shortcut: "Ctrl+F"),
                null
            };
            foreach (int seconds in new[] { 1, 2, 5, 10 })
                choices.Add(new Choice("显示播放头附近 " + seconds + " 秒",
                    delegate { timeline.ZoomAround((int)Math.Round(Session.Fps * seconds)); }, enabled: hasLayers));
            choices.Add(null);
            choices.Add(new Choice(timeline.Expanded ? "收起缩略图" : "展开缩略图", timeline.ToggleThumbnails, timeline.Expanded));
            choices.Add(new Choice("折叠时间轴", ToggleTimeline, shortcut: "Tab"));
            ShowChoices(timeline, choices, position, above: true, title: "时间轴");
        }

        internal void LayerContextMenu(int index, Point position)
        {
            SelectLayer(index);
            Layer layer = Selection;
            if (layer == null) return;
            bool atLayer = Frame >= layer.Start && Frame <= layer.End;
            var choices = new List<Choice>
            {
                new Choice("编辑图层属性…", ShowLayerProperties),
                new Choice("重命名图层…", RenameLayer),
                new Choice("显示图层", ToggleVisible, layer.Visible),
                null,
                new Choice("裁切入点到播放头", () => TrimAtFrame(true), enabled: atLayer && Frame > layer.Start, shortcut: "Shift+I"),
                new Choice("裁切出点到播放头", () => TrimAtFrame(false), enabled: atLayer && Frame < layer.End, shortcut: "Shift+O"),
                null,
                new Choice("上移图层", () => Reorder(-1), enabled: index > 0),
                new Choice("下移图层", () => Reorder(1), enabled: index < Session.Layers.Count - 1),
                new Choice("移除图层", RemoveLayer, shortcut: "Ctrl+Del")
            };
            if (layer.Source != null && layer.Source.ExrPasses.Length > 1)
                choices.Insert(1, new Choice("切换 EXR 渲染层…", () => ShowExrPassMenu(timeline, position)));
            ShowChoices(timeline, choices, new Point(position.X, position.Y - UiScale.Px(4)), above: true, title: "图层");
        }

        internal void ShowExrPassMenu(Control anchor, Point? position)
        {
            Layer layer = Selection;
            if (layer == null || layer.Source == null) return;
            var current = layer.Source.ResolveExrPass(layer.ExrPassId);
            ShowChoices(anchor, layer.Source.ExrPasses.Select(pass => new Choice(pass.Name, delegate
            {
                if (pass == current) return;
                EditLayer(selected => selected.ExrPassId = pass.Id, false);
                SetStatus("EXR 渲染层 · " + pass.Name);
            }, pass == current)), position, above: position.HasValue, title: "EXR 渲染层");
        }

        private void RenameLayer()
        {
            Layer layer = Selection;
            if (layer == null) return;
            Pause();
            string name = AskText("重命名图层", "输入新的图层名称。", layer.Name, 255);
            if (name == null) return;
            name = name.Trim();
            if (name.Length > 0 && name != layer.Name)
                EditLayer(selected => selected.Name = name, false);
        }

        private void LayerAppearanceMenu()
        {
            if (Selection == null) return;
            ShowChoices(layerPresets, new[]
            {
                new Choice("默认外观 · 100% / 0 EV", delegate { ApplyAppearance(100, 0, false, true); }),
                new Choice("半透明叠加 · 50% / 0 EV", delegate { ApplyAppearance(50, 0, false, true); }),
                new Choice("提亮一档 · +1 EV", delegate { EditLayer(layer => layer.Exposure = 100, false); }),
                new Choice("压暗一档 · −1 EV", delegate { EditLayer(layer => layer.Exposure = -100, false); }),
                null,
                new Choice("水平翻转", delegate { EditLayer(layer => layer.Flip = !layer.Flip, false); }, Selection.Flip),
                new Choice("可见", ToggleVisible, Selection.Visible)
            });
        }

        private void ApplyAppearance(int alpha, int ev, bool mirrored, bool shown)
        {
            EditLayer(layer =>
            {
                layer.Opacity = alpha; layer.Exposure = ev; layer.Flip = mirrored; layer.Visible = shown;
            }, false);
        }

        private IEnumerable<Choice> StartChoices()
        {
            yield return new Choice("时间轴开头 · 默认", delegate { start.Value = 1; }, start.Value == 1);
            yield return new Choice("与播放头对齐", delegate { start.Value = Math.Min(start.Maximum, Frame + 1); });
            yield return new Choice("与工作区入点对齐", delegate { start.Value = Math.Min(start.Maximum, WorkIn + 1); });
        }

        private IEnumerable<Choice> TrimChoices(bool isIn)
        {
            var number = isIn ? trimIn : trimOut;
            decimal endpoint = isIn ? 1 : number.Maximum;
            yield return new Choice(isIn ? "素材首帧 · 默认" : "素材末帧 · 默认",
                delegate { number.Value = endpoint; }, number.Value == endpoint);
            Layer layer = Selection;
            yield return new Choice("裁到播放头", delegate { TrimAtFrame(isIn); },
                enabled: layer != null && Frame >= layer.Start && Frame <= layer.End);
            yield return new Choice("恢复完整素材", delegate { EditLayer(x => { x.In = 0; x.Out = x.Length - 1; }, true); });
        }

        private IEnumerable<Choice> OpacityChoices()
        {
            return NumericChoices(new decimal[] { 0, 25, 50, 75, 100 }, opacity.Value, value => opacity.Value = value,
                value => value + "%" + (value == 100 ? " · 默认" : ""));
        }

        private IEnumerable<Choice> ExposureChoices()
        {
            return NumericChoices(new decimal[] { -3, -2, -1, -.5m, 0, .5m, 1, 2, 3 }, exposure.Value, value => exposure.Value = value,
                value => value.ToString("+0.##;-0.##;0") + " EV" + (value == 0 ? " · 默认" : ""));
        }
    }
}
