using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class MainForm
    {

        private Panel layerPage;
        private MaterialButton previewModeButton;
        private Button layerPropertiesButton, exportButton, focusButton, markerButton;

        private bool focusReview, compactTimeline, hideFocusFilmstrip;

        private void BuildReviewButtons(Control toolbar)
        {
            layerPropertiesButton = AddReviewButton(toolbar, "\uE8FD", "图层属性", ShowLayerProperties);
            layerPropertiesButton.Enabled = false;
            previewModeButton = (MaterialButton)AddReviewButton(toolbar, "\uE8B9", "原画检查", CyclePreviewQuality);
            previewModeButton.Width = 138;
            previewModeButton.Height = 36;
            previewModeButton.ShowCycle = true;
            previewModeButton.LongPress = PreviewChoices;
            previewModeButton.DropDown = PreviewChoices;
            SyncReviewPane();
        }
        private Button AddReviewButton(Control toolbar, string glyph, string hint, Action apply)
        {
            var button = HeaderIconButton(glyph, hint, apply);
            button.Width = 34;
            button.Margin = new Padding(0, 2, 4, 0);
            toolbar.Controls.Add(button);
            return button;
        }

        private void ShowLayerProperties()
        {
            if (Selection == null) return;
            Pause();
            using (var dialog = new Form
            {
                Text = "图层属性", Font = Theme.Small, ClientSize = UiScale.Box(0, 0, 268, 414).Size,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Theme.Panel, ForeColor = Theme.Text, ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false,
                AutoScaleMode = AutoScaleMode.None
            })
            {
                bool cloaked = false;
                dialog.HandleCreated += delegate
                {
                    int cloak = 1;
                    cloaked = DwmSetWindowAttribute(dialog.Handle, DwmCloak, ref cloak, sizeof(int)) >= 0;
                    ApplyCaptionTheme(dialog.Handle);
                };
                // Prepare the hidden handle before entering the modal message loop.
                var handle = dialog.Handle;
                if (!cloaked) dialog.Opacity = 0;
                dialog.Shown += delegate
                {
                    try { PaintWindowBeforeReveal(dialog.Handle); }
                    finally
                    {
                        if (cloaked)
                        {
                            int cloak = 0;
                            DwmSetWindowAttribute(dialog.Handle, DwmCloak, ref cloak, sizeof(int));
                        }
                        else dialog.Opacity = 1;
                    }
                };
                dialog.KeyPreview = true;
                dialog.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; dialog.Close(); }
                };
                dialog.Controls.Add(layerPage);
                try { SyncInspector(); dialog.ShowDialog(this); }
                finally { dialog.Controls.Remove(layerPage); }
            }
        }

        private static void SetActive(Button button, bool active)
        {
            button.BackColor = active ? Theme.Container : Theme.Raised;
            button.ForeColor = active ? Theme.OnContainer : Theme.Text;
        }

        private void SyncReviewPane()
        {
            bool original = Session.PreviewDivisor == 1;
            previewModeButton.Text = original ? "原画检查" : "流畅播放";
            previewModeButton.Glyph = original ? "\uE8B9" : "\uE945";
            previewModeButton.FilledLightning = Session.PreviewDivisor == 4;
            previewModeButton.ToolbarDescription = null;
            SetToolbarHint(previewModeButton, previewModeButton.Text + " · " +
                (original ? "原始" : "1/" + Session.PreviewDivisor) + "，点击主体切换档位，点击右侧三角或长按设置预览与缓存");
            SetActive(previewModeButton, !original);
            previewModeButton.Invalidate();
        }

        private void CyclePreviewQuality()
        {
            int next = Session.PreviewDivisor == 1 ? 2 : Session.PreviewDivisor == 2 ? 4 : 1;
            SetPreview(next, Session.CacheMegabytes);
            SetStatus(next == 1 ? "原画检查 · 原始预览" : "流畅播放 · 1/" + next + " 预览");
        }
        internal void ToggleFocus()
        {
            focusReview = !focusReview;
            UpdateWorkspace();
        }

        private void WorkspaceMenu()
        {
            ShowChoices(focusButton, new[]
            {
                new Choice("标准布局 · 默认", delegate { SetWorkspace(false, false); }, !focusReview && !compactTimeline),
                new Choice("紧凑时间轴", delegate { SetWorkspace(false, true); }, !focusReview && compactTimeline, shortcut: "Tab"),
                new Choice("专注审片", delegate { SetWorkspace(true, false); }, focusReview, shortcut: "F9"),
                null,
                new Choice("切换全屏", ToggleFull, full, shortcut: "F11")
            }, title: "窗口布局");
        }

        private void SetWorkspace(bool focus, bool compact)
        {
            focusReview = focus; compactTimeline = compact;
            UpdateWorkspace();
        }

        private void UpdateWorkspace()
        {
            var grid = (TableLayoutPanel)timelineSection.Parent;
            var transport = (TableLayoutPanel)standardTransport.Parent;
            grid.SuspendLayout(); transport.SuspendLayout();
            try
            {
                bool showHeader = !full && !focusReview;
                topSection.Visible = showHeader;
                grid.RowStyles[0].Height = showHeader ? UiScale.Px(compactHeader ? 98 : MainLayout.Header) : 0;
                int inset = UiScale.Px(focusReview ? 6 : 20);
                grid.Padding = new Padding(inset, 0, inset, 0);
                statusSection.Visible = !focusReview;
                grid.RowStyles[3].Height = focusReview ? 0 : UiScale.Px(MainLayout.Status);
                standardTransport.Visible = !focusReview;
                focusTransport.Visible = focusReview;
                transport.RowStyles[0].Height = focusReview ? 0 : UiScale.Px(MainLayout.Transport);
                transport.RowStyles[2].Height = focusReview ? UiScale.Px(MainLayout.FocusTransport) : 0;
                timeline.SetFocusFilmstrip(focusReview);
                timeline.FollowFocusFrame();
                bool showTimeline = focusReview ? !hideFocusFilmstrip : !compactTimeline;
                timeline.Visible = showTimeline;
                if (!showTimeline) timeline.StopThumbnailRequests();
                timelineSection.Margin = new Padding(0, UiScale.Px(focusReview ? 4 : 14), 0, 0);
                int stripInset = timelineSection.Margin.Top + timelineSection.Padding.Vertical;
                grid.RowStyles[2].Height = focusReview ? UiScale.Px(MainLayout.FocusTransport) + stripInset +
                    (showTimeline ? UiScale.Px(MainLayout.FocusFilmstrip) : 0) : showTimeline ? UiScale.Px(timeline.Expanded ? MainLayout.Filmstrip :
                        MainLayout.Timeline + 28 * Math.Max(0, Math.Min(2, Session.Layers.Count - 1))) :
                    UiScale.Px(MainLayout.Transport) + stripInset;
                focusFilmstrip.Text = hideFocusFilmstrip ? "显示缩略图" : "隐藏缩略图";
                SetToolbarHint(focusButton, focusReview ? "工作区 · 专注审片" : compactTimeline ? "工作区 · 紧凑时间轴" : "工作区");
                SetActive(focusButton, focusReview || compactTimeline);
            }
            finally { transport.ResumeLayout(true); grid.ResumeLayout(true); }
            viewer.Invalidate(); UpdateStatus();
        }

        private void ExportMenu()
        {
            ShowChoices(exportButton.Visible ? exportButton : focusReview ? focusCounter : counter, new[]
            {
                new Choice("导出片段 · 工作区 MP4", ExportTrim),
                new Choice("导出片段 · 工作区 GIF…", ExportGif),
                new Choice("打开导出目录", OpenExportFolder),
                new Choice("设置导出目录…", ChooseExportFolder),
                null,
                new Choice("PNG · 当前帧原尺寸", delegate { ExportFrame(MoviePreset.Original); }),
                new Choice("PNG · 审片 1080p", delegate { ExportFrame(MoviePreset.Review); }),
                new Choice("PNG · 快速 720p", delegate { ExportFrame(MoviePreset.Quick); }),
                null,
                new Choice("MP4 · 原尺寸", delegate { ExportMovie(MoviePreset.Original); }),
                new Choice("MP4 · 交付 4K UHD", delegate { ExportMovie(MoviePreset.Delivery); }),
                new Choice("MP4 · 审片 1080p", delegate { ExportMovie(MoviePreset.Review); }),
                new Choice("MP4 · 快速 720p", delegate { ExportMovie(MoviePreset.Quick); }),
                new Choice("MP4 · 自定义…", CustomMovie)
            }, above: !exportButton.Visible, title: "导出");
        }

        private void CustomMovie()
        {
            if (Session.Layers.Count == 0) { SetStatus("请先导入素材。"); return; }
            Pause();
            MoviePreset preset;
            using (var dialog = new Form { Text = "自定义 MP4", Font = Theme.Small, StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(435, 240), FormBorderStyle = FormBorderStyle.FixedDialog, BackColor = Theme.Panel,
                ForeColor = Theme.Text, ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false })
            {
                var size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145, BackColor = Theme.Raised,
                    ForeColor = Theme.Text, Location = new Point(121, 17) };
                size.Items.AddRange(new object[] { "原尺寸", "4K UHD", "1080p", "720p", "自定义尺寸" });
                var width = NumberOf(2, 8192, 1920, 78); width.Location = new Point(121, 56);
                var height = NumberOf(2, 8192, 1080, 78); height.Location = new Point(220, 56);
                var crf = NumberOf(0, 51, 21, 78); crf.Location = new Point(121, 95);
                var speed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145, BackColor = Theme.Raised,
                    ForeColor = Theme.Text, Location = new Point(121, 134) };
                speed.Items.AddRange(new object[] { "veryfast", "fast", "medium", "slow" }); speed.SelectedIndex = 1;
                size.SelectedIndexChanged += delegate
                {
                    int index = size.SelectedIndex;
                    width.Enabled = height.Enabled = index == 4;
                    if (index >= 1 && index <= 3)
                    {
                        width.Value = new[] { 3840, 1920, 1280 }[index - 1];
                        height.Value = new[] { 2160, 1080, 720 }[index - 1];
                    }
                    crf.Value = index <= 1 ? 18 : index == 3 ? 25 : 21;
                    speed.SelectedIndex = index == 1 ? 2 : index == 3 ? 0 : 1;
                };
                size.SelectedIndex = 2;
                var captions = new[] { "尺寸预设", "宽 × 高上限", "质量 CRF", "编码速度" };
                for (int i = 0; i < captions.Length; i++)
                {
                    var label = LabelOf(captions[i], 96, 25, Theme.Muted);
                    label.Location = new Point(17, 15 + i * 39);
                    dialog.Controls.Add(label);
                }
                var qualityHint = LabelOf("0–51，数值越低质量越高", 215, 25, Theme.Muted);
                qualityHint.Location = new Point(211, 93);
                var limitHint = LabelOf("按宽高比缩小，沿用时间轴 FPS。", 300, 23, Theme.Muted);
                limitHint.Location = new Point(17, 165);
                var ok = ButtonOf("继续", 77, delegate { }, true);
                var cancel = ButtonOf("取消", 77, delegate { });
                ok.Location = new Point(339, 197); cancel.Location = new Point(253, 197);
                ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
                dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                dialog.Controls.AddRange(new Control[] { size, width, height, crf, speed, qualityHint, limitHint, ok, cancel });
                UiScale.ApplyLayout(dialog);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                bool original = size.SelectedIndex == 0;
                preset = new MoviePreset("自定义", original ? 0 : (int)width.Value,
                    original ? 0 : (int)height.Value, (int)crf.Value, (string)speed.SelectedItem);
            }
            ExportMovie(preset);
        }

        private void MarkerMenu()
        {
            ShowChoices(markerButton, new[]
            {
                new Choice("添加 / 移除当前帧标记", ToggleMark, shortcut: "M"),
                new Choice("上一标记", delegate { JumpMarker(-1); }, shortcut: "Shift+M"),
                new Choice("下一标记", delegate { JumpMarker(1); }, shortcut: "Ctrl+M")
            }, above: true);
        }

        private void JumpMarker(int direction)
        {
            int maxFrame = MaxFrame;
            var marks = Session.Marks.Where(x => x >= 0 && x <= maxFrame && (direction < 0 ? x < Frame : x > Frame)).ToList();
            if (marks.Count == 0) { SetStatus(direction < 0 ? "前方没有标记。" : "后方没有标记。"); return; }
            Seek(direction < 0 ? marks.Max() : marks.Min());
        }

        private void GoToFrame()
        {
            if (Session.Layers.Count == 0) { SetStatus("请先导入素材。"); return; }
            Pause();
            string input = AskText("跳到指定帧", "输入时间轴帧号（1–" + (MaxFrame + 1) + "）。", (Frame + 1).ToString(), 9);
            if (input == null) return;
            int frame;
            if (!int.TryParse(input, out frame) || frame < 1 || frame > MaxFrame + 1)
            { SetStatus("请输入 1–" + (MaxFrame + 1) + " 之间的帧号。"); return; }
            Seek(frame - 1); timeline.FocusPlayhead();
        }

        private string AskText(string title, string caption, string initial, int limit)
        {
            using (var dialog = new Form { Text = title, Font = Theme.Small, StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(380, 139), FormBorderStyle = FormBorderStyle.FixedDialog, BackColor = Theme.Panel,
                ForeColor = Theme.Text, ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false })
            {
                var hint = LabelOf(caption, 342, 30, Theme.Text);
                hint.Location = new Point(19, 10);
                var text = new TextBox { Text = initial, MaxLength = limit, Location = new Point(19, 47),
                    Width = 342, BackColor = Theme.Raised, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
                var ok = ButtonOf("确定", 76, delegate { }, true);
                var cancel = ButtonOf("取消", 76, delegate { });
                ok.Location = new Point(285, 90); cancel.Location = new Point(199, 90);
                ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
                dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                dialog.Controls.Add(hint); dialog.Controls.Add(text); dialog.Controls.Add(ok); dialog.Controls.Add(cancel);
                dialog.ActiveControl = text;
                dialog.Shown += delegate { text.SelectAll(); };
                UiScale.ApplyLayout(dialog);
                return dialog.ShowDialog(this) == DialogResult.OK ? text.Text : null;
            }
        }
    }
}
