using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FrameDeck
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Bundle.Register();
            Application.Run(new MainForm(args, hideUntilReady: true));
        }
    }

    internal sealed partial class MainForm : Form
    {
        internal Project Session;
        internal readonly FrameCache Cache;
        internal readonly Font DisplayFont = new Font("Microsoft YaHei UI", 18, FontStyle.Bold);
        internal int Selected = -1;
        internal int Frame { get { return Session.Frame; } }
        internal bool IsPlaying { get { return playing; } }
        internal bool SideBySide { get { return Session.Compare && Session.CompareSideBySide && previewLayers.Count == 2; } }
        internal List<Layer> PreviewLayers { get { return previewLayers; } }
        internal int MaxFrame { get { return Session.Layers.Count == 0 ? 0 : Session.Layers.Max(layer => layer.End); } }
        internal int WorkIn { get { return Math.Min(Session.RangeIn, MaxFrame); } }
        internal int WorkOut { get { return Session.RangeOut < 0 ? MaxFrame : Math.Max(WorkIn, Math.Min(Session.RangeOut, MaxFrame)); } }
        internal bool HasWorkRange { get { return Session.Layers.Count > 0 && (Session.RangeIn != 0 || Session.RangeOut >= 0); } }
        private Layer Selection { get { return Selected >= 0 && Selected < Session.Layers.Count ? Session.Layers[Selected] : null; } }

        private readonly Viewer viewer;
        private readonly Timeline timeline;
        private readonly Control timelineSection, topSection;
        private readonly Control monitorHeader, statusSection, standardTransport, focusTransport;
        private readonly Button focusPlay, focusCounter, focusFilmstrip;
        private readonly Label info, cacheLabel, layerTitle, sourceInfo, status;
        private readonly Button counter;
        private readonly CacheMeter cacheMeter;
        private readonly Button play, compare, channels;
        private readonly Button clearRange;
        private readonly NumericUpDown fps, start, trimIn, trimOut, opacity, exposure;
        private readonly CheckBox visible, flip;
        private readonly Timer timer = new Timer { Interval = 10 };
        private readonly ToolTip toolbarTips = new ToolTip();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly VideoTools video = new VideoTools();
        private readonly List<Layer> previewLayers = new List<Layer>();
        private readonly List<string> frameRequests = new List<string>();
        private Size statusSize;
        private float statusZoom;
        private bool statusFit;
        private string projectFile;
        private bool syncing, playing, dirty, full;
        private int direction = 1;
        private long lastTick;
        private bool playbackPrimed;
        private FormWindowState previousState;
        private Rectangle previousBounds;

        private bool hideUntilReady, startupCloaked;
        private bool compactHeader;
        internal bool IsFocusReview { get { return focusReview; } }

        internal MainForm(string[] args, bool hideUntilReady = false)
        {
            SuspendLayout();
            this.hideUntilReady = hideUntilReady;
            defaults = ViewerDefaults.Load();
            Session = defaults.NewProject();
            appearance = AppearanceSettings.Load();
            Theme.UseDark(AppearanceSettings.IsDark(appearance));
            Text = BrandInfo.Name + " · 图像序列播放器";
            Icon = BrandInfo.WindowIcon;
            ClientSize = new Size(1250, 792);
            MinimumSize = new Size(1000, 610);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background; ForeColor = Theme.Text;
            DoubleBuffered = true;
            Font = Theme.Small;
            KeyPreview = true;
            Cache = new FrameCache(this, delegate(bool current)
            {
                if (current) { viewer.Invalidate(); UpdateStatus(); }
                else UpdateCacheStatus();
                timeline.RefreshCacheMarks();
            });
            Cache.Configure(Session.PreviewDivisor, Session.CacheMegabytes);
            var import = (MaterialButton)HeaderButton("导入素材", 166, delegate { OpenMedia(true); }, true, "\uE8E5");
            import.Pill = false;
            import.ShortcutCaption = "Ctrl+L";
            SetToolbarHint(import, "导入素材 (Ctrl+L)");
            var demo = (MaterialButton)HeaderButton("载入示例", 98, LoadDemo);
            demo.Pill = false;
            viewer = new Viewer(this, import, demo) { Dock = DockStyle.Fill };
            timeline = new Timeline(this, toolbarTips) { Dock = DockStyle.Fill };

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                Margin = Padding.Empty, Padding = new Padding(20, 0, 20, 0), BackColor = Theme.Background };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, MainLayout.Header));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, MainLayout.Timeline));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, MainLayout.Status));
            Controls.Add(grid);

            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2,
                BackColor = Theme.Background, Margin = Padding.Empty };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 398));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 324));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            topSection = toolbar;
            var top = new CenteredToolbarStrip();
            Button projectButton = null;
            projectButton = HeaderButton("项目 ▾", 63, delegate { FileMenu(projectButton); });
            projectButton.Height = 36;
            top.Controls.Add(projectButton);
            top.Controls.Add(HeaderIconButton("\uE8E5", "导入素材 (Ctrl+L)", delegate { OpenMedia(true); }));
            exportButton = HeaderButton("导出 ▾", 60, ExportMenu);
            exportButton.Height = 36;
            exportButton.BackColor = Theme.Background;
            top.Controls.Add(exportButton);
            var exportGroup = new MaterialSurface(11, Theme.Panel, true) {
                Dock = DockStyle.None, Size = new Size(214, 36), Margin = new Padding(0, 0, 6, 0) };
            var exportTools = Strip(new Padding(3, 3, 0, 0));
            var trim = (MaterialButton)ButtonOf("导出片段", 88, ExportTrim);
            trim.Glyph = "\uE8C6";
            trim.BackColor = Theme.Panel;
            var gif = ButtonOf("GIF 动图", 68, ExportGif);
            gif.BackColor = Theme.Panel;
            var folder = (MaterialButton)ButtonOf("", 32, OpenExportFolder);
            folder.Glyph = "\uE8B7"; folder.BackColor = Theme.Panel;
            SetToolbarHint(folder, "打开导出目录");
            foreach (var item in new[] { trim, gif, folder }) {
                item.Height = 28; item.Margin = new Padding(0, 0, 4, 0);
                exportTools.Controls.Add(item);
            }
            exportGroup.Controls.Add(exportTools);
            top.Controls.Add(exportGroup);
            var actions = new CenteredToolbarStrip { Padding = new Padding(6, 0, 0, 0) };
            BuildReviewButtons(actions);
            focusButton = HeaderIconButton("\uE8A9", "工作区", WorkspaceMenu);
            actions.Controls.Add(focusButton);
            appearanceButton = HeaderIconButton("", "切换主题", ToggleAppearance);
            SyncAppearanceButton();
            actions.Controls.Add(appearanceButton);
            actions.Controls.Add(HeaderIconButton("\uE713", "设置", OpenSettings));
            toolbar.Controls.Add(top, 0, 0); toolbar.Controls.Add(actions, 2, 0);
            grid.Controls.Add(toolbar, 0, 0);

            var previewShell = new MaterialSurface(18, Theme.Monitor, true);
            var viewHeader = new CenteredToolbarStrip { Padding = new Padding(8, 0, 0, 0) };
            viewHeader.BackColor = Theme.Background;
            monitorHeader = viewHeader;
            compare = ButtonOf("A/B 对比 ▾", 84, CompareChoices);
            viewHeader.Controls.Add(compare);
            channels = ButtonOf("RGB ▾", 48, ChannelMenu);
            viewHeader.Controls.Add(channels);
            zoomButton = ButtonOf("适配 ▾", 58, ZoomMenu);
            viewHeader.Controls.Add(zoomButton);
            displayButton = ButtonOf("显示 ▾", 52, DisplayMenu);
            viewHeader.Controls.Add(displayButton);
            foreach (var tool in new[] { compare, channels, zoomButton, displayButton }) {
                tool.Height = 36;
                tool.Margin = new Padding(0, 0, 6, 0);
                ((MaterialButton)tool).CompactCaptionFont = Theme.Caption;
            }
            foreach (var tool in new[] { channels, zoomButton, displayButton }) tool.BackColor = Theme.Background;
            info = LabelOf("", 96, 36, Theme.Muted);
            info.Font = Theme.Caption;
            viewHeader.Controls.Add(info);
            viewHeader.SizeChanged += delegate { info.Visible = viewHeader.ClientSize.Width >= UiScale.Px(360); };
            toolbar.Controls.Add(viewHeader, 1, 0);
            previewShell.Controls.Add(viewer);
            grid.Controls.Add(previewShell, 0, 1);

            layerPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Small };
            var stack = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 413, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Padding = new Padding(15, 9, 8, 0), Margin = Padding.Empty };
            layerPage.Controls.Add(stack);
            layerTitle = LabelOf("未选中图层", 216, 31, Theme.Text);
            layerTitle.Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
            stack.Controls.Add(layerTitle);
            sourceInfo = LabelOf("—", 215, 29, Theme.Muted);
            stack.Controls.Add(sourceInfo);
            var layerActions = Strip(Padding.Empty);
            layerActions.Dock = DockStyle.None; layerActions.Size = new Size(215, 41);
            layerActions.Controls.Add(ButtonOf("上移", 62, delegate { Reorder(-1); }));
            layerActions.Controls.Add(ButtonOf("下移", 62, delegate { Reorder(1); }));
            layerActions.Controls.Add(ButtonOf("移除", 62, RemoveLayer));
            stack.Controls.Add(layerActions);
            start = AddField(stack, "时间轴起点", 1, 1000001, 1, StartChoices);
            trimIn = AddField(stack, "素材入点", 1, 1000000, 1, delegate { return TrimChoices(true); });
            trimOut = AddField(stack, "素材出点", 1, 1000000, 1, delegate { return TrimChoices(false); });
            opacity = AddField(stack, "不透明度 %", 0, 100, 100, OpacityChoices);
            exposure = AddField(stack, "曝光 EV", -6, 6, 0, ExposureChoices);
            exposure.DecimalPlaces = 2; exposure.Increment = .25m;
            var switches = Strip(Padding.Empty);
            switches.Dock = DockStyle.None; switches.Size = new Size(215, 39);
            visible = new CheckBox { Text = "可见", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 6, 22, 0) };
            flip = new CheckBox { Text = "水平翻转", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 6, 0, 0) };
            switches.Controls.Add(visible); switches.Controls.Add(flip); stack.Controls.Add(switches);
            layerPresets = ButtonOf("外观预设 ▾", 211, LayerAppearanceMenu);
            stack.Controls.Add(layerPresets);
            start.ValueChanged += delegate { if (!syncing) MoveLayerStart((int)start.Value - 1, true); };
            trimIn.ValueChanged += delegate { EditLayer(l => l.In = Math.Min((int)trimIn.Value - 1, l.Out), true); };
            trimOut.ValueChanged += delegate { EditLayer(l => l.Out = Math.Max((int)trimOut.Value - 1, l.In), true); };
            opacity.ValueChanged += delegate { EditLayer(l => l.Opacity = (int)opacity.Value, false); };
            exposure.ValueChanged += delegate { EditLayer(l => l.Exposure = (int)(exposure.Value * 100), false); };
            visible.CheckedChanged += delegate { EditLayer(l => l.Visible = visible.Checked, false); };
            flip.CheckedChanged += delegate { EditLayer(l => l.Flip = flip.Checked, false); };
            layerPage.Scale(new SizeF(UiScale.Factor, UiScale.Factor));

            var bottomShell = new MaterialSurface(15, Theme.Panel, true) { Margin = new Padding(0, 14, 0, 0) };
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = 3,
                BackColor = Theme.Panel };
            timelineSection = bottomShell;
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, MainLayout.Transport));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            var transportBar = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty,
                ColumnCount = 3, RowCount = 1, BackColor = Theme.Line, Padding = new Padding(0, 0, 0, 1) };
            transportBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 334));
            transportBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            transportBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 334));
            transportBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            standardTransport = transportBar;
            var transport = Strip(new Padding(10, 7, 0, 0));
            var selectionSlot = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty,
                ColumnCount = 1, RowCount = 1, BackColor = Theme.Panel };
            selectionSlot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selectionSlot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var selectionGroup = new MaterialSurface(11, Theme.Background, true) {
                Dock = DockStyle.None, Size = new Size(240, 36), MinimumSize = new Size(240, 36), Anchor = AnchorStyles.None, Margin = Padding.Empty };
            var rangeActions = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty,
                Padding = new Padding(3, 2, 3, 2), BackColor = Theme.Background,
                ColumnCount = 4, RowCount = 1 };
            rangeActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            rangeActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            rangeActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rangeActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
            rangeActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selectionGroup.Controls.Add(rangeActions);
            selectionSlot.Controls.Add(selectionGroup, 0, 0);
            var playbackOptions = Strip(new Padding(4, 7, 4, 0));
            transport.Controls.Add(PlaybackStep("|◀", "跳到入点 (Home)", delegate { Seek(WorkIn); }));
            transport.Controls.Add(PlaybackStep("◂", "上一帧 (←)", delegate { Seek(Frame - 1); }));
            play = ButtonOf("", 43, TogglePlay);
            play.BackColor = Theme.Container; play.ForeColor = Theme.OnContainer;
            transport.Controls.Add(play);
            transport.Controls.Add(PlaybackStep("▸", "下一帧 (→)", delegate { Seek(Frame + 1); }));
            transport.Controls.Add(PlaybackStep("▶|", "跳到出点 (End)", delegate { Seek(WorkOut); }));
            counter = ButtonOf("", 106, GoToFrame); counter.Font = Theme.Mono; counter.BackColor = Theme.Background;
            ((MaterialButton)counter).CompactCaptionFont = Theme.CompactMono;
            transport.Controls.Add(counter);
            fps = NumberOf(1, 240, 24, 68, integratedArrows: true);
            fps.DecimalPlaces = 3; fps.Increment = 1; fps.ThousandsSeparator = false;
            fps.AccessibleName = "时间轴帧率";
            fpsButton = ButtonOf("FPS ▾", 53, FpsMenu);
            fpsButton.Margin = new Padding(0, 1, 4, 0);
            fpsButton.AccessibleName = "时间轴帧率预设";
            playbackOptions.Controls.Add(new MaterialNumberHost(fps, 126, fpsButton));
            speedButton = ButtonOf("1× ▾", 58, SpeedMenu);
            speedButton.AccessibleName = "播放倍率预设";
            playbackOptions.Controls.Add(speedButton);
            loopButton = ButtonOf("循环 ▾", 65, LoopMenu);
            playbackOptions.Controls.Add(loopButton);
            var markIn = (MaterialButton)ButtonOf("", 36, delegate { SetRange(true); });
            markIn.RangeStartIcon = true;
            SetToolbarHint(markIn, "将当前帧设为入点 (I)");
            var markOut = (MaterialButton)ButtonOf("", 36, delegate { SetRange(false); });
            markOut.RangeStartIcon = false;
            SetToolbarHint(markOut, "将当前帧设为出点 (O)");
            markIn.Anchor = markOut.Anchor = AnchorStyles.Left;
            markIn.Margin = markOut.Margin = Padding.Empty;
            markIn.Width = markOut.Width = 32;
            markIn.Height = markOut.Height = 28;
            markIn.BackColor = markOut.BackColor = Theme.Background;
            rangeActions.Controls.Add(markIn, 0, 0);
            rangeActions.Controls.Add(markOut, 1, 0);
            range = ButtonOf("范围 ▾", 118, RangeMenu);
            range.Anchor = AnchorStyles.Left;
            range.Margin = Padding.Empty;
            ((MaterialButton)range).CompactCaptionFont = Theme.Compact;
            range.Dock = DockStyle.Fill; range.BackColor = Theme.Background;
            rangeActions.Controls.Add(range, 2, 0);
            var clear = (MaterialButton)ButtonOf("", 32, ResetRange);
            clear.Glyph = "\uE711";
            clear.Anchor = AnchorStyles.Left;
            clear.Margin = Padding.Empty;
            clear.Enabled = false;
            SetToolbarHint(clear, "清除选区");
            clearRange = clear;
            clear.Width = 28; clear.Height = 28; clear.BackColor = Theme.Background;
            rangeActions.Controls.Add(clearRange, 3, 0);
            markerButton = ButtonOf("标记 ▾", 58, MarkerMenu);
            playbackOptions.Controls.Add(markerButton);
            fps.ValueChanged += delegate { if (!syncing) { Session.Fps = fps.Value; lastTick = clock.ElapsedTicks; Dirty(); } };
            transportBar.Controls.Add(transport, 0, 0);
            transportBar.Controls.Add(selectionSlot, 1, 0);
            transportBar.Controls.Add(playbackOptions, 2, 0);
            bottom.Controls.Add(transportBar, 0, 0); bottom.Controls.Add(timeline, 0, 1);
            var reviewBar = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty,
                BackColor = Theme.Panel, ColumnCount = 2, RowCount = 1, Visible = false };
            focusTransport = reviewBar;
            reviewBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            reviewBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208));
            var playback = Strip(new Padding(10, 4, 0, 0));
            var previous = ButtonOf("◂", 34, delegate { Seek(Frame - 1); });
            previous.AccessibleName = "上一帧";
            playback.Controls.Add(previous);
            focusPlay = ButtonOf("▶ 播放", 74, TogglePlay, true);
            UpdatePlayButtons();
            playback.Controls.Add(focusPlay);
            var next = ButtonOf("▸", 34, delegate { Seek(Frame + 1); });
            next.AccessibleName = "下一帧";
            playback.Controls.Add(next);
            focusCounter = ButtonOf("", 158, GoToFrame);
            focusCounter.Font = Theme.Mono; focusCounter.ForeColor = Theme.Amber;
            ((MaterialButton)focusCounter).CompactCaptionFont = Theme.CompactMono;
            playback.Controls.Add(focusCounter);
            var reviewActions = Strip(new Padding(0, 4, 4, 0));
            focusFilmstrip = ButtonOf("隐藏缩略图", 98, ToggleTimeline);
            reviewActions.Controls.Add(focusFilmstrip);
            reviewActions.Controls.Add(ButtonOf("退出专注", 94, ToggleFocus));
            reviewBar.Controls.Add(playback, 0, 0);
            reviewBar.Controls.Add(reviewActions, 1, 0);
            bottom.Controls.Add(reviewBar, 0, 2);
            bottomShell.Controls.Add(bottom);
            grid.Controls.Add(bottomShell, 0, 2);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 4,
                RowCount = 1, BackColor = Theme.Background, Padding = new Padding(4, 0, 5, 0) };
            statusSection = footer;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 15));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
            status = LabelOf("就绪", 600, 29, Theme.Muted); status.Dock = DockStyle.Fill;
            cacheLabel = LabelOf("", 174, 29, Theme.Muted); cacheLabel.Dock = DockStyle.Fill; cacheLabel.TextAlign = ContentAlignment.MiddleRight;
            cacheMeter = new CacheMeter { Dock = DockStyle.Fill, BackColor = Theme.Background };
            footer.Controls.Add(LabelOf("●", 15, 29, Theme.Green), 0, 0);
            footer.Controls.Add(status, 1, 0); footer.Controls.Add(cacheMeter, 2, 0); footer.Controls.Add(cacheLabel, 3, 0);
            grid.Controls.Add(footer, 0, 3);

            timer.Tick += delegate { Advance(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!ConfirmDiscard()) e.Cancel = true; };
            Disposed += delegate { EndThemeTransition(); toolbarTips.Dispose(); layerPage.Dispose(); };
            FormClosed += delegate
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemAppearanceChanged;
                timer.Stop(); Cache.Clear(); timeline.ClearThumbnails(); video.Close();
            };
            Load += delegate
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemAppearanceChanged;
                SyncReviewPane();
                RefreshProject(true);
            };
            Shown += delegate
            {
                RevealStartupWindow();
                if (args.Length == 1 && IsProject(args[0])) OpenProject(args[0]);
                else if (args.Length > 0) Import(args, true);
            };
            viewer.Resize += delegate { UpdateStatus(); };
            UiScale.ApplyLayout(this);
            var workSize = Screen.PrimaryScreen.WorkingArea.Size;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, workSize.Width), Math.Min(MinimumSize.Height, workSize.Height));
            Size = new Size(Math.Min(Width, workSize.Width), Math.Min(Height, workSize.Height));
            ResumeLayout(true);
            SizeChanged += delegate { EndThemeTransition(); ArrangeHeader(); };
            ArrangeHeader();
        }

        private void ArrangeHeader()
        {
            var toolbar = (TableLayoutPanel)topSection;
            bool compact = ClientSize.Width < UiScale.Px(1105);
            compactHeader = compact;
            toolbar.SuspendLayout();
            try
            {
                Control commands = toolbar.GetControlFromPosition(0, 0);
                toolbar.SetColumnSpan(commands, compact ? 2 : 1);
                toolbar.SetCellPosition(monitorHeader, new TableLayoutPanelCellPosition(compact ? 0 : 1, compact ? 1 : 0));
                toolbar.SetColumnSpan(monitorHeader, compact ? 3 : 1);
                toolbar.RowStyles[0].SizeType = SizeType.Absolute;
                toolbar.RowStyles[0].Height = UiScale.Px(compact ? 54 : MainLayout.Header);
                toolbar.RowStyles[1].Height = compact ? UiScale.Px(44) : 0;
            }
            finally { toolbar.ResumeLayout(true); }
            UpdateWorkspace();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();
        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);

        private const int DwmCloak = 13;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (hideUntilReady)
            {
                int cloak = 1;
                startupCloaked = DwmSetWindowAttribute(Handle, DwmCloak, ref cloak, sizeof(int)) >= 0;
            }
            ApplyCaptionTheme();
        }
        private void RevealStartupWindow()
        {
            hideUntilReady = false;
            if (!startupCloaked) return;
            try
            {
                PaintWindowBeforeReveal(Handle);
            }
            finally
            {
                int cloak = 0;
                DwmSetWindowAttribute(Handle, DwmCloak, ref cloak, sizeof(int));
                startupCloaked = false;
            }
        }

        private static void PaintWindowBeforeReveal(IntPtr handle)
        {
            // Paint the title bar and child controls before the compositor presents the window.
            const uint invalidate = 0x0001, allChildren = 0x0080, updateNow = 0x0100, frame = 0x0400;
            RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, invalidate | allChildren | updateNow | frame);
            DwmFlush();
        }
        private void ApplyCaptionTheme()
        {
            ApplyCaptionTheme(Handle);
            captionBackground = Theme.Background;
            captionForeground = Theme.Text;
            captionDark = Theme.IsDark;
        }

        internal static void ApplyCaptionTheme(IntPtr handle)
        {
            if (SystemInformation.HighContrast) return;
            int enabled = Theme.IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
            SetCaptionColors(handle, Theme.Background, Theme.Text);
        }

        private Color captionBackground, captionForeground;
        private bool captionDark;

        private static void SetCaptionColors(IntPtr handle, Color background, Color foreground)
        {
            int fill = background.R | background.G << 8 | background.B << 16;
            int text = foreground.R | foreground.G << 8 | foreground.B << 16;
            DwmSetWindowAttribute(handle, 35, ref fill, sizeof(int));
            DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        }

        private void AnimateCaption(Color fromBackground, Color fromForeground, bool fromDark,
            Color toBackground, Color toForeground, bool toDark, double progress)
        {
            if (!IsHandleCreated || IsDisposed || SystemInformation.HighContrast) return;
            float mix = (float)(progress * progress * (3 - 2 * progress));
            bool nextDark = progress < .5 ? fromDark : toDark;
            if (captionDark != nextDark)
            {
                int enabled = nextDark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
                captionDark = nextDark;
            }
            captionBackground = Theme.Blend(fromBackground, toBackground, mix);
            captionForeground = Theme.Blend(fromForeground, toForeground, mix);
            SetCaptionColors(Handle, captionBackground, captionForeground);
        }

        private static FlowLayoutPanel Strip(Padding padding)
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = padding,
                WrapContents = false, BackColor = Theme.Panel };
        }
        private static Label LabelOf(string text, int width, int height, Color color)
        {
            return new Label { Text = text, Size = new Size(width, height), ForeColor = color,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty };
        }
        private static Button ButtonOf(string text, int width, Action action, bool primary = false)
        {
            var button = new MaterialButton { Text = text, Size = new Size(width, 30), FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 1, 6, 0), BackColor = primary ? Theme.Primary : Theme.Raised,
                ForeColor = primary ? Theme.OnPrimary : Theme.Text, TabStop = false, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderSize = 0;
            button.Click += delegate { action(); };
            return button;
        }
        private static Button HeaderButton(string text, int width, Action action, bool primary = false, string glyph = null)
        {
            var button = (MaterialButton)ButtonOf(text, width, action, primary);
            button.Glyph = glyph;
            button.Height = 40;
            button.Margin = new Padding(0, 2, 6, 0);
            return button;
        }
        private MaterialButton HeaderIconButton(string glyph, string hint, Action action)
        {
            var button = (MaterialButton)HeaderButton("", 36, action, glyph: glyph);
            button.Height = 36;
            SetToolbarHint(button, hint);
            return button;
        }
        private Button PlaybackStep(string text, string hint, Action action)
        {
            var button = ButtonOf(text, 31, action);
            button.BackColor = Theme.Panel; button.ForeColor = Theme.Muted;
            SetToolbarHint(button, hint);
            return button;
        }
        private void SetToolbarHint(Control button, string hint)
        {
            if (button.AccessibleName == hint) return;
            button.AccessibleName = hint;
            toolbarTips.SetToolTip(button, hint);
        }
        private static NumericUpDown NumberOf(decimal min, decimal max, decimal value, int width, bool integratedArrows = false)
        {
            return new MaterialNumber(integratedArrows) { Minimum = min, Maximum = max, Value = value, Width = width,
                BackColor = Theme.Raised, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Right };
        }
        private NumericUpDown AddField(FlowLayoutPanel stack, string title, int min, int max, int value, Func<IEnumerable<Choice>> choices)
        {
            var line = Strip(Padding.Empty);
            line.Dock = DockStyle.None; line.Size = new Size(231, 32);
            line.Controls.Add(LabelOf(title, 105, 25, Theme.Muted));
            var number = NumberOf(min, max, value, 86);
            number.Margin = new Padding(0, 2, 2, 0);
            Button arrow = null;
            arrow = ButtonOf("▾", 29, delegate { ShowChoices(arrow, choices()); });
            arrow.AccessibleName = title + "预设";
            arrow.Margin = new Padding(0, 2, 0, 0); arrow.Height = 23;
            layerValueMenus.Add(arrow);
            line.Controls.Add(number); line.Controls.Add(arrow); stack.Controls.Add(line);
            return number;
        }
        private void FileMenu(Control anchor)
        {
            ShowChoices(anchor, new[]
            {
                new Choice("新建项目", NewProject, shortcut: "Ctrl+N"),
                new Choice("打开项目", ChooseProject, shortcut: "Ctrl+O"),
                new Choice("保存项目", delegate { SaveProject(false); }, shortcut: "Ctrl+S"),
                new Choice("项目另存为", delegate { SaveProject(true); }),
                new Choice("导入单张静帧", delegate { OpenMedia(false); }),
                new Choice("载入示例", LoadDemo),
                new Choice("重新扫描素材", Reload, shortcut: "U"),
                new Choice("重置工作区", ResetRange),
                null,
                new Choice("内置组件许可", ShowBundledNotices)
            });
        }
        private void ShowBundledNotices()
        {
            string notices = Bundle.Notices();
            if (notices == null) { SetStatus("组件许可见程序目录中的 THIRD-PARTY-NOTICES.txt。"); return; }
            using (var dialog = new Form { Text = "内置组件许可", ClientSize = new Size(720, 480), Font = Theme.Small,
                StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Panel, ForeColor = Theme.Text,
                ShowInTaskbar = false, Icon = BrandInfo.WindowIcon })
            {
                dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                    BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
                    ScrollBars = ScrollBars.Both, WordWrap = false, Text = notices });
                UiScale.ApplyLayout(dialog);
                dialog.ShowDialog(this);
            }
        }
        private void ChannelMenu()
        {
            string[] names = { "RGB · 彩色", "R · 红", "G · 绿", "B · 蓝", "A · Alpha", "L · 亮度" };
            var choices = new[] { "RGB", "R", "G", "B", "A", "L" }.Select((c, i) =>
                new Choice(names[i], delegate { SetChannel(c, false); }, Session.Channel == c)).ToList();
            Layer layer = Selection;
            if (layer != null && layer.Source != null && layer.Source.ExrPasses.Length > 1)
            {
                choices.Add(null);
                choices.Add(new Choice("切换 EXR 渲染层…", () => ShowExrPassMenu(channels, null)));
            }
            ShowChoices(channels, choices, title: "颜色通道");
        }
        private void SetChannel(string channel, bool toggle = true)
        {
            string next = toggle && Session.Channel == channel ? "RGB" : channel;
            if (Session.Channel == next) return;
            Session.Channel = next;
            channels.Text = Session.Channel + " ▾"; Dirty(); viewer.Invalidate();
        }
        private void ToggleCompare()
        {
            SetCompare(!Session.Compare, Session.CompareSideBySide);
        }
        private void SetCompare(bool enabled, bool sideBySide)
        {
            if (enabled && Session.Layers.Count(x => x.Visible) < 2) { SetStatus("A/B 对比需要两个可见图层。"); return; }
            if (enabled == Session.Compare && sideBySide == Session.CompareSideBySide) return;
            Session.Compare = enabled;
            Session.CompareSideBySide = sideBySide;
            SyncViewButtons(); Dirty();
            SetStatus(!enabled ? "已切换至图层合成。" : sideBySide ? "并排查看最上方的两个可见图层。" :
                "拖动分隔线对比最上方的两个可见图层。");
            RefreshFrame();
        }
        private void SetPreview(int divisor, int cacheMegabytes)
        {
            if (Session.PreviewDivisor == divisor && Session.CacheMegabytes == cacheMegabytes) return;
            Session.PreviewDivisor = divisor; Session.CacheMegabytes = cacheMegabytes;
            Cache.Configure(divisor, cacheMegabytes);
            SyncViewButtons(); Dirty(); RefreshFrame();
        }
        internal void SetWipe(float value)
        {
            if (Session.Wipe == value) return;
            Session.Wipe = value; Dirty(); viewer.Invalidate();
        }
        private void Help(IWin32Window owner)
        {
            MaterialMessageBox.Show(owner, "Ctrl+L  导入素材     Ctrl+O  打开项目     Ctrl+S  保存项目\n" +
                "空格 / Enter  播放或暂停     ↑ / ↓  正放或倒放\n" +
                "← / →  单步     Shift / Ctrl + ← / →  跳 10 / 100 帧\n" +
                "Ctrl+F  定位播放头     Ctrl+W  切换 A/B     Home / End  到工作区首尾\n" +
                "I / O 或 Alt+Home / Alt+End  设工作区入点 / 出点\n" +
                "Shift+I / Shift+O  将选中图层裁至当前帧\n" +
                "R / G / B / A / L  切换通道     M  标记     U  重新扫描素材\n" +
                "F9  专注审片     F11  全屏     Tab  折叠时间轴\n" +
                "Ctrl+G  跳到指定帧     Shift+M / Ctrl+M  上一 / 下一标记\n" +
                "Ctrl+Del  移除图层\n\n" +
                "监看区左键双击切换专注审片，左键横拖逐帧浏览。\n" +
                "滚轮缩放，右键或中键拖动平移。\n" +
                "A/B 擦拭模式下，直接拖动分隔线调整对比位置。\n" +
                "点击时间尺定位，拖动图层色条移动图层。Ctrl+滚轮缩放时间轴。", BrandInfo.Name + " · 快捷键");
        }

        internal void SelectLayer(int index)
        {
            Selected = index; SyncInspector(); timeline.ShowSelection();
        }
        internal void ToggleVisible() { EditLayer(l => l.Visible = !l.Visible, false); }
        internal void MoveLayerStart(int frame, bool reset)
        {
            EditLayer(l => l.Start = Math.Max(0, Math.Min(1000000, frame)), reset);
        }
        private void EditLayer(Action<Layer> edit, bool reset)
        {
            if (syncing || Selection == null) return;
            edit(Selection); Dirty(); RefreshProject(reset);
        }
        private void Reorder(int delta)
        {
            int index = Selected + delta;
            if (Selection == null || index < 0 || index >= Session.Layers.Count) return;
            var layer = Selection;
            Session.Layers.RemoveAt(Selected); Session.Layers.Insert(index, layer);
            Selected = index; Dirty(); RefreshProject(false);
        }
        private void RemoveLayer()
        {
            if (Selection == null) return;
            Session.Layers.RemoveAt(Selected);
            Selected = Math.Min(Selected, Session.Layers.Count - 1);
            if (Session.Layers.Count == 0) { Pause(); Session.Compare = false; SyncViewButtons(); }
            Dirty(); RefreshProject(true);
        }
        private void TrimAtFrame(bool isIn)
        {
            var layer = Selection;
            if (layer == null || Frame < layer.Start || Frame > layer.End) return;
            int delta = Frame - layer.Start;
            EditLayer(l =>
            {
                if (isIn) { l.In += delta; l.Start = Frame; }
                else l.Out = l.In + delta;
            }, true);
        }
        private void SyncInspector()
        {
            layerPropertiesButton.Enabled = Selection != null;
            syncing = true;
            try
            {
                Layer layer = Selection;
                layerTitle.Text = layer == null ? "未选中图层" : layer.Name;
                sourceInfo.Text = layer == null ? "—" : layer.Demo > 0 ? "示例序列 · 96 帧" :
                    layer.Source == null ? "素材离线" :
                    layer.VideoFps > 0 ? "视频 · " + layer.Length + " 帧" :
                    (layer.Length > 1 ? "序列 · " + layer.Source.First + "–" + (layer.Source.First + layer.Length - 1) : "单张静帧");
                foreach (Control control in new Control[] { start, trimIn, trimOut, opacity, exposure, visible, flip })
                    control.Enabled = layer != null;
                foreach (Button menu in layerValueMenus) menu.Enabled = layer != null;
                layerPresets.Enabled = layer != null;
                if (layer == null) return;
                start.Value = layer.Start + 1;
                trimIn.Maximum = trimOut.Maximum = layer.Length;
                trimIn.Value = layer.In + 1; trimOut.Value = layer.Out + 1;
                opacity.Value = layer.Opacity; exposure.Value = layer.Exposure / 100m;
                visible.Checked = layer.Visible; flip.Checked = layer.Flip;
            }
            finally { syncing = false; }
        }
        internal void RefreshProject(bool reset)
        {
            timeline.ClearCacheMarks();
            Session.Frame = Math.Min(MaxFrame, Math.Max(0, Frame));
            Session.RangeIn = WorkIn;
            if (Session.RangeOut >= 0) Session.RangeOut = WorkOut;
            SyncInspector();
            syncing = true;
            fps.Value = Session.Fps; SyncTransportPresets(); syncing = false;
            if (reset) timeline.ResetView();
            UpdateWorkspace();
            timeline.ShowSelection();
            RefreshFrame();
        }
        internal void UpdateStatus()
        {
            Size size = viewer.GetImageSize(Frame, false);
            float displayZoom = viewer.DisplayZoom(size);
            bool fit = viewer.IsFit;
            if (size != statusSize || displayZoom != statusZoom || fit != statusFit)
            {
                statusSize = size; statusZoom = displayZoom; statusFit = fit;
                info.Text = size.IsEmpty ? "" : size.Width + " × " + size.Height;
                zoomButton.Text = fit ? "适配 ▾" : (displayZoom * 100).ToString("0.#") + "% ▾";
            }
            UpdateCacheStatus();
        }
        private void UpdateCacheStatus()
        {
            string text = "帧缓存  " + Cache.Summary;
            if (cacheLabel.Text != text) cacheLabel.Text = text;
            cacheMeter.SetFill(Cache.FillRatio);
        }
        private void RefreshFrame()
        {
            int max = MaxFrame, workIn = Math.Min(Session.RangeIn, max);
            int workOut = Session.RangeOut < 0 ? max : Math.Max(workIn, Math.Min(Session.RangeOut, max));
            bool hasMedia = Session.Layers.Count > 0;
            viewer.ShowImportActions(!hasMedia);
            string frameCount = hasMedia ? (Frame + 1).ToString("0000") + " / " + (max + 1).ToString("0000") : "— / —";
            counter.Text = frameCount;
            focusCounter.Text = frameCount;
            range.Text = hasMedia ? (workIn + 1) + "–" + (workOut + 1) + " ▾" : "范围 ▾";
            bool hasWorkRange = HasWorkRange;
            // Move focus before disabling the clicked button; otherwise WinForms
            // selects the next input control, which is the FPS editor.
            if (!hasWorkRange && clearRange.ContainsFocus) viewer.Focus();
            clearRange.Enabled = hasWorkRange;
            range.AccessibleName = "工作区 " + (workIn + 1) + " 到 " + (workOut + 1);
            previewLayers.Clear();
            foreach (Layer layer in Session.Layers)
            {
                if (!layer.Visible) continue;
                previewLayers.Add(layer);
                if (Session.Compare && previewLayers.Count == 2) break;
            }
            frameRequests.Clear();
            timeline.FollowFocusFrame();
            timeline.TrackCacheFrame(Frame);
            foreach (Layer layer in previewLayers) frameRequests.Add(layer.Key(Frame));
            bool hasExrPasses = previewLayers.Any(layer => layer.Source != null && layer.Source.ExrPasses.Length > 0);
            int lookAhead = Cache.LookAhead(previewLayers.Count, hasExrPasses);
            if (!playing && previewLayers.Any(layer => layer.VideoFps > 0)) lookAhead = Math.Min(2, lookAhead);
            if (Frame >= workIn && Frame <= workOut && workIn < workOut)
                for (int offset = 1; offset <= lookAhead && (offset == 1 || frameRequests.Count < 48); offset++)
                {
                    int unused;
                    int next = PlaybackFrame(offset, workIn, workOut, out unused);
                    if (next == Frame) continue;
                    timeline.TrackCacheFrame(next);
                    foreach (Layer layer in previewLayers) frameRequests.Add(layer.Key(next));
                }
            Cache.Request(frameRequests, previewLayers.Count);
            viewer.Invalidate(); timeline.Invalidate(); UpdateStatus();
        }
        internal bool IsFrameCached(int frame)
        {
            bool hasImage = false;
            foreach (Layer layer in previewLayers)
            {
                string key = layer.Key(frame);
                if (key == null) continue;
                if (Cache.OriginalSize(key).IsEmpty) return false;
                hasImage = true;
            }
            return hasImage;
        }
        internal void Seek(int frame)
        {
            Pause();
            int target = Math.Max(0, Math.Min(MaxFrame, frame));
            if (target == Frame) return;
            Session.Frame = target;
            RefreshFrame();
        }
        private void TogglePlay() { if (playing) Pause(); else Play(direction); }
        private void Play(int sign)
        {
            if (Session.Layers.Count == 0 || WorkOut == WorkIn) return;
            direction = sign; playing = true; Cache.VideoPlayback = true;
            if (Frame < WorkIn || Frame > WorkOut || (Session.Repeat == "单次" &&
                (direction > 0 ? Frame == WorkOut : Frame == WorkIn)))
                Session.Frame = direction > 0 ? WorkIn : WorkOut;
            clock.Restart(); lastTick = 0; playbackPrimed = false; timer.Start(); UpdatePlayButtons(); RefreshFrame();
        }
        private void Pause()
        {
            if (!playing) return;
            playing = false; Cache.VideoPlayback = false; clock.Stop(); timer.Stop(); UpdatePlayButtons();
            RefreshFrame();
        }
        private void UpdatePlayButtons()
        {
            ((MaterialButton)play).Glyph = playing ? "\uE769" : "\uE768";
            SetToolbarHint(play, playing ? "暂停 (空格)" : "播放 (空格)");
            play.Invalidate();
            focusPlay.Text = playing ? "Ⅱ 暂停" : "▶ 播放";
        }
        private void Advance()
        {
            int first = WorkIn, last = WorkOut;
            if (first >= last) { Pause(); return; }
            if (Frame < first || Frame > last)
            {
                Session.Frame = direction > 0 ? first : last;
                lastTick = clock.ElapsedTicks;
                RefreshFrame();
                return;
            }
            long step = (long)(Stopwatch.Frequency / (double)(Session.Fps * Session.PlaybackSpeed));
            long now = clock.ElapsedTicks;
            if (!playbackPrimed)
            {
                int unused;
                if (!FrameReady(Frame) || !FrameReady(PlaybackFrame(1, first, last, out unused))) return;
                playbackPrimed = true;
                lastTick = now - step;
            }
            long steps = (now - lastTick) / step;
            if (steps < 1) return;
            if (!FrameReady(Frame)) return;
            int nextDirection;
            int next = PlaybackFrame(steps, first, last, out nextDirection);
            if (!FrameReady(next))
            {
                next = PlaybackFrame(1, first, last, out nextDirection);
                if (!FrameReady(next)) return;
            }
            lastTick += steps * step;
            bool reachedEnd = Session.Repeat == "单次" && (direction > 0 ? next == last : next == first);
            direction = nextDirection;
            Session.Frame = next;
            if (reachedEnd) Pause();
            else RefreshFrame();
        }
        private bool FrameReady(int frame)
        {
            // Very large stacks may not fit in memory together with the next frame.
            if (!Cache.CanFit(previewLayers.Count)) return true;
            foreach (Layer layer in previewLayers)
            {
                string key = layer.Key(frame);
                if (key != null && !Cache.Ready(key)) return false;
            }
            return true;
        }
        private int PlaybackFrame(long steps, int first, int last, out int nextDirection)
        {
            nextDirection = direction;
            if (Session.Repeat == "往返")
            {
                int span = last - first;
                long phase = direction > 0 ? Frame - first : span + last - Frame;
                phase = (phase + steps) % (2L * span);
                if (phase < 0) phase += 2L * span;
                nextDirection = phase < span ? 1 : -1;
                return first + (int)(phase <= span ? phase : 2L * span - phase);
            }
            long next = Frame + steps * direction;
            if (Session.Repeat == "单次") return (int)Math.Max(first, Math.Min(last, next));
            int length = last - first + 1;
            return first + (int)(((next - first) % length + length) % length);
        }
        private void SetRange(bool beginning)
        {
            if (Session.Layers.Count == 0) return;
            if (beginning)
            {
                if (Frame > WorkOut) Session.RangeOut = Frame;
                Session.RangeIn = Frame;
            }
            else
            {
                if (Frame < WorkIn) Session.RangeIn = Frame;
                Session.RangeOut = Frame;
            }
            Dirty(); RefreshFrame();
        }
        private void ResetRange()
        {
            if (Session.RangeIn == 0 && Session.RangeOut == -1) return;
            Session.RangeIn = 0; Session.RangeOut = -1; Dirty(); RefreshFrame();
            SetStatus("已清除选区。");
        }
        private void ToggleMark()
        {
            if (Session.Layers.Count == 0) return;
            if (!Session.Marks.Remove(Frame)) Session.Marks.Add(Frame);
            Dirty(); timeline.Invalidate();
        }
        private void SetStatus(string value) { status.Text = value; }
        private void Dirty()
        {
            dirty = true; UpdateTitle();
            SyncReviewPane();
        }
        private void UpdateTitle()
        {
            Text = BrandInfo.Name + " · " + (projectFile == null ? "未命名项目" : Path.GetFileNameWithoutExtension(projectFile)) + (dirty ? " *" : "");
        }
        private void SyncViewButtons()
        {
            SetActive(compare, Session.Compare);
            compare.Text = !Session.Compare ? "A/B 对比 ▾" : Session.CompareSideBySide ? "A/B 并排 ▾" : "A/B 擦拭 ▾";
            channels.Text = Session.Channel + " ▾";
            SyncReviewPane();
        }
        private void Replace(Project project, string path)
        {
            Pause(); Cache.Clear(); timeline.ClearThumbnails(); direction = 1;
            Session = project; projectFile = path; dirty = false;
            Cache.Configure(Session.PreviewDivisor, Session.CacheMegabytes);
            Selected = Session.Layers.Count > 0 ? 0 : -1;
            UpdateTitle(); SyncViewButtons(); viewer.Fit(); RefreshProject(true);
        }
        private bool ConfirmDiscard()
        {
            if (!dirty) return true;
            DialogResult result = MaterialMessageBox.Show(this, "是否保存当前项目的更改？", BrandInfo.Name,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            return result == DialogResult.No || (result == DialogResult.Yes && SaveProject(false));
        }
        private void NewProject()
        {
            if (!ConfirmDiscard()) return;
            Replace(defaults.NewProject(), null); SetStatus("已新建项目。");
        }
        private void LoadDemo()
        {
            if (!ConfirmDiscard()) return;
            var project = new Project();
            project.Layers.Add(new Layer { Name = "Horizon · v01", Demo = 1, Out = 95 });
            project.Layers.Add(new Layer { Name = "Horizon · v02", Demo = 2, Out = 95 });
            project.Marks.AddRange(new[] { 23, 47, 71 });
            project.Compare = true;
            Replace(project, null); SetStatus("拖动分隔线比较两个渲染版本，按空格播放。");
        }
        private void OpenMedia(bool sequence)
        {
            var extensions = sequence ? MediaSource.Extensions.Concat(VideoTools.Extensions) : MediaSource.Extensions;
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = sequence ? "导入图像序列或视频" : "导入静帧",
                Filter = "支持的素材|" + string.Join(";", extensions.Select(x => "*" + x)) + "|所有文件|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK) Import(dialog.FileNames, sequence);
        }
        private static bool IsProject(string path) { return Path.GetExtension(path).Equals(".fdeck", StringComparison.OrdinalIgnoreCase); }
        private MediaSource OpenSource(string path, bool sequence, decimal videoFps, bool useSourceRate = false)
        {
            if (videoFps <= 0) return MediaSource.Open(path, sequence);
            return MediaProgress.Run(this, "正在导入视频", Path.GetFileName(path),
                cancel => useSourceRate ? video.OpenAtSourceRate(path, videoFps, cancel) : video.Open(path, videoFps, cancel));
        }
        internal void Import(string[] paths, bool sequence)
        {
            if (paths.Length == 1 && IsProject(paths[0])) { OpenProject(paths[0]); return; }
            Pause();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();
            var layers = new List<Layer>();
            bool chooseSourceRate = Session.Layers.Count == 0;
            decimal importFps = Session.Fps;
            foreach (string input in paths)
            {
                try
                {
                    IEnumerable<string> files = Directory.Exists(input)
                        ? (IEnumerable<string>)Directory.EnumerateFiles(input).Where(x => MediaSource.IsImage(x) ||
                            (sequence && VideoTools.IsVideo(x))).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        : new[] { input };
                    foreach (string file in files)
                    {
                        bool isVideo = sequence && VideoTools.IsVideo(file);
                        if (!isVideo && !MediaSource.IsImage(file)) { failures.Add(Path.GetFileName(file) + "：不支持此格式"); continue; }
                        string path = Path.GetFullPath(file);
                        if (!seen.Add(MediaSource.GroupKey(path, sequence && !isVideo))) continue;
                        var source = OpenSource(path, sequence, isVideo ? importFps : 0, chooseSourceRate && isVideo);
                        // Read dimensions from metadata, without decoding a full-resolution frame.
                        var nativeSize = source.NativeSize;
                        if (chooseSourceRate && isVideo)
                        {
                            importFps = source.FrameRate;
                            chooseSourceRate = false;
                        }
                        layers.Add(new Layer { Name = source.Name, Path = path, Sequence = sequence, VideoFps = source.FrameRate,
                            Source = source, Out = source.Length - 1 });
                    }
                }
                catch (OperationCanceledException) { failures.Add("已取消剩余导入。"); break; }
                catch (Exception ex) { failures.Add(ex.Message); }
            }
            if (layers.Count > 0)
            {
                if (Session.Layers.Count == 0) viewer.Fit();
                Session.Fps = importFps;
                Session.PreviewDivisor = layers.Concat(Session.Layers).Where(layer => layer.Source != null)
                    .Select(layer => MediaSource.DefaultPreviewDivisor(layer.Source.NativeSize)).DefaultIfEmpty(1).Max();
                Session.Layers.InsertRange(0, layers); Selected = 0; Dirty(); RefreshProject(true);
            }
            SetStatus((layers.Count > 0 ? "已导入 " + layers.Count + " 个图层。" : "未导入素材。") +
                (failures.Count > 0 ? " " + failures[0] : ""));
        }
        private void Reload()
        {
            Pause(); Cache.Clear(); timeline.ClearThumbnails();
            int failures = 0;
            foreach (var layer in Session.Layers.Where(x => x.Demo == 0))
            {
                try
                {
                    var updated = OpenSource(layer.Path, layer.Sequence, layer.VideoFps);
                    int previousIn = layer.In, previousOut = layer.Out;
                    bool extends = layer.Out == layer.Length - 1;
                    int shift = layer.Source == null ? 0 : layer.Source.First - updated.First;
                    layer.In = Math.Max(0, Math.Min(updated.Length - 1, layer.In + shift));
                    layer.Out = extends ? updated.Length - 1 : Math.Max(layer.In, Math.Min(updated.Length - 1, layer.Out + shift));
                    if (previousIn != layer.In || previousOut != layer.Out) Dirty();
                    layer.Source = updated;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception)
                {
                    layer.Source = null;
                    failures++;
                }
            }
            RefreshProject(true);
            SetStatus(failures == 0 ? "已重新扫描素材并清空帧缓存。" : failures + " 个素材无法重新读取。");
        }
        private void ChooseProject()
        {
            using (var dialog = new OpenFileDialog { Filter = BrandInfo.Name + " 项目|*.fdeck" })
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenProject(dialog.FileName);
        }
        private static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 16000000 }; }
        private void OpenProject(string path)
        {
            if (!ConfirmDiscard()) return;
            try
            {
                Project project = Serializer().Deserialize<Project>(File.ReadAllText(path));
                if (project == null || project.Version != 1 || project.Layers == null ||
                    project.Layers.Any(x => x == null)) throw new InvalidDataException("无法读取此项目格式。");
                project.Fps = Math.Max(1, Math.Min(240, project.Fps));
                if (project.PlaybackSpeed < .25m || project.PlaybackSpeed > 4) project.PlaybackSpeed = 1;
                if (!Project.PreviewFilters.Contains(project.PreviewFilter)) project.PreviewFilter = "Auto";
                if (!Project.PreviewBackgrounds.Contains(project.PreviewBackground)) project.PreviewBackground = "Checker";
                project.RangeIn = Math.Max(0, project.RangeIn);
                project.RangeOut = Math.Max(-1, project.RangeOut);
                if (project.Repeat != "单次" && project.Repeat != "往返") project.Repeat = "循环";
                if (!new[] { "RGB", "R", "G", "B", "A", "L" }.Contains(project.Channel)) project.Channel = "RGB";
                project.Wipe = float.IsNaN(project.Wipe) ? .5f : Math.Max(.02f, Math.Min(.98f, project.Wipe));
                if (project.PreviewDivisor != 2 && project.PreviewDivisor != 4) project.PreviewDivisor = 1;
                if (project.CacheMegabytes < ViewerDefaults.MinimumCacheMegabytes || project.CacheMegabytes > ViewerDefaults.MaximumCacheMegabytes)
                    project.CacheMegabytes = 384;
                if (project.Marks == null) project.Marks = new List<int>();
                int offline = 0;
                foreach (Layer layer in project.Layers)
                {
                    layer.Name = layer.Name ?? "图层";
                    layer.Start = Math.Max(0, Math.Min(1000000, layer.Start));
                    layer.Opacity = Math.Max(0, Math.Min(100, layer.Opacity));
                    layer.Exposure = Math.Max(-600, Math.Min(600, layer.Exposure));
                    if (layer.Demo != 1 && layer.Demo != 2)
                    {
                        layer.Demo = 0;
                        try
                        {
                            bool isVideo = VideoTools.IsVideo(layer.Path);
                            if (!isVideo && !MediaSource.IsImage(layer.Path)) throw new InvalidDataException();
                            layer.VideoFps = isVideo ? Math.Max(1, Math.Min(240, layer.VideoFps > 0 ? layer.VideoFps : project.Fps)) : 0;
                            layer.Source = OpenSource(layer.Path, layer.Sequence, layer.VideoFps);
                        }
                        catch (OperationCanceledException) { SetStatus("已取消打开项目。"); return; }
                        catch (Exception) { offline++; }
                    }
                    layer.In = Math.Max(0, Math.Min(layer.Length - 1, layer.In));
                    layer.Out = Math.Max(layer.In, Math.Min(layer.Length - 1, layer.Out));
                }
                Replace(project, path);
                SetStatus(offline == 0 ? "已打开项目。" : "已打开项目，" + offline + " 个素材离线。");
            }
            catch (Exception ex) { MaterialMessageBox.Show(this, ex.Message, "打开项目失败"); }
        }
        private bool SaveProject(bool choose)
        {
            string path = projectFile;
            if (choose || path == null)
            {
                using (var dialog = new SaveFileDialog { Filter = BrandInfo.Name + " 项目|*.fdeck", FileName = "Untitled.fdeck" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                    path = dialog.FileName;
                }
            }
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, Serializer().Serialize(Session), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                projectFile = path; dirty = false; UpdateTitle(); SetStatus("已保存项目。"); return true;
            }
            catch (Exception ex)
            {
                MaterialMessageBox.Show(this, ex.Message, "保存项目失败"); return false;
            }
            finally { if (File.Exists(temporary)) try { File.Delete(temporary); } catch (IOException) { } }
        }
        private void ExportFrame(MoviePreset sizePreset)
        {
            Pause();
            var cache = new FrameCache(null, null);
            try
            {
                Size size = OutputSize(Frame, cache);
                if (size.IsEmpty) { SetStatus("当前帧没有可导出的画面。"); return; }
                float scale = sizePreset.Scale(size);
                size = sizePreset.OutputSize(size);
                using (var dialog = new SaveFileDialog { Filter = "PNG 图像|*.png", FileName = "frame_" + (Frame + 1).ToString("0000") + ".png" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    using (var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        viewer.Render(graphics, Frame, new PointF(size.Width / 2f, size.Height / 2f), scale,
                            new Rectangle(Point.Empty, size), true, cache);
                        bitmap.Save(dialog.FileName, ImageFormat.Png);
                    }
                    SetStatus("已导出 " + Path.GetFileName(dialog.FileName));
                }
            }
            catch (Exception ex) { MaterialMessageBox.Show(this, ex.Message, "导出失败"); }
            finally { cache.Clear(); }
        }
        private Size OutputSize(int frame, FrameCache cache)
        {
            Size size = viewer.GetImageSize(frame, true, cache);
            return SideBySide ? new Size(checked(size.Width * 2), size.Height) : size;
        }
        private void ExportTrim() { ExportMovie(MoviePreset.Original, trim: true); }

        private Layer DirectTrimLayer()
        {
            if (Session.Channel != "RGB" || Session.Compare) return null;
            var intersecting = Session.Layers.Where(x => x.Visible && x.Start <= WorkOut && x.End >= WorkIn).Take(2).ToList();
            if (intersecting.Count != 1) return null;
            Layer layer = intersecting[0];
            return layer.VideoFps == Session.Fps && layer.Start <= WorkIn && layer.End >= WorkOut &&
                layer.Opacity == 100 && layer.Exposure == 0 && !layer.Flip && VideoTools.IsVideo(layer.Path) &&
                File.Exists(layer.Path) ? layer : null;
        }

        private void ExportMovie(MoviePreset preset, bool trim = false)
        {
            if (!Session.Layers.Any(x => x.Visible && (x.Source != null || x.Demo > 0)))
            {
                if (trim) ShowMissingExportMedia();
                else SetStatus("工作区没有可导出的视频画面。");
                return;
            }
            if (trim && !ConfirmExportRange("MP4")) return;
            Pause();
            try
            {
                string encoder = VideoTools.Encoder();
                int first = WorkIn, last = WorkOut;
                Layer source = trim ? DirectTrimLayer() : null;
                if (trim)
                {
                    string output = WriteClip("导出片段 · MP4",
                        source == null ? "正在渲染并编码片段…" : "正在从源视频裁剪并保留音频…",
                        first, last, ".mp4", (staging, cancel) =>
                        {
                            if (source == null) RenderMovie(encoder, staging, cancel, preset);
                            else
                            {
                                decimal startTime = (source.In + first - source.Start) / source.VideoFps;
                                decimal duration = (last - first + 1) / source.VideoFps;
                                VideoTools.Trim(encoder, source.Path, staging, startTime, duration, source.VideoFps, cancel);
                            }
                        });
                    SetStatus("已导出片段 · " + Path.GetFileName(output));
                    return;
                }
                using (var dialog = new SaveFileDialog { Title = "导出 MP4", Filter = "H.264 视频|*.mp4", FileName = "review.mp4" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    string output = dialog.FileName;
                    if (Session.Layers.Any(layer => !string.IsNullOrEmpty(layer.Path) &&
                        string.Equals(layer.Path, output, StringComparison.OrdinalIgnoreCase)))
                    {
                        SetStatus("请为导出文件选择与源素材不同的路径。");
                        return;
                    }
                    string staging = output + "." + Guid.NewGuid().ToString("N") + ".mp4";
                    try
                    {
                        MediaProgress.Run(this, "导出 MP4", "正在编码「" + preset.Name + "」H.264…", cancel =>
                        {
                            RenderMovie(encoder, staging, cancel, preset);
                            return true;
                        });
                        if (File.Exists(output)) File.Replace(staging, output, null);
                        else File.Move(staging, output);
                        SetStatus("已导出「" + preset.Name + "」 · " + Path.GetFileName(output));
                    }
                    finally { if (File.Exists(staging)) File.Delete(staging); }
                }
            }
            catch (OperationCanceledException) { SetStatus("已取消导出。"); }
            catch (Exception ex) { MaterialMessageBox.Show(this, ex.Message, "导出 MP4 失败"); }
        }
        private void RenderMovie(string encoder, string output, System.Threading.CancellationToken cancel, MoviePreset preset)
        {
            RenderClip(WorkIn, WorkOut, 1, preset.Scale, cancel,
                (size, write) => VideoTools.Encode(encoder, size, output, Session.Fps, preset, cancel, write));
        }

        private void RenderClip(int first, int last, int interval, Func<Size, float> scaleFor,
            System.Threading.CancellationToken cancel, Action<Size, Action<Stream>> encode)
        {
            var cache = new FrameCache(null, null);
            try
            {
                Size size = Size.Empty;
                for (int frame = first; frame <= last && size.IsEmpty; frame += interval)
                {
                    cancel.ThrowIfCancellationRequested();
                    size = OutputSize(frame, cache);
                }
                if (size.IsEmpty) throw new InvalidDataException("工作区内没有可解码的画面。");
                float scale = scaleFor(size);
                size = MoviePreset.ScaledSize(size, scale);
                encode(size, stream =>
                {
                    var center = new PointF(size.Width / 2f, size.Height / 2f);
                    var bounds = new Rectangle(Point.Empty, size);
                    int rowBytes = checked(size.Width * 4);
                    var pixels = new byte[checked(rowBytes * size.Height)];
                    using (var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        for (int frame = first; frame <= last; frame += interval)
                        {
                            cancel.ThrowIfCancellationRequested();
                            graphics.Clear(Color.Black);
                            viewer.Render(graphics, frame, center, scale, bounds, true, cache);
                            var locked = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, bitmap.PixelFormat);
                            try
                            {
                                if (locked.Stride == rowBytes) Marshal.Copy(locked.Scan0, pixels, 0, pixels.Length);
                                else
                                    for (int y = 0; y < size.Height; y++)
                                        Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), pixels, y * rowBytes, rowBytes);
                            }
                            finally { bitmap.UnlockBits(locked); }
                            stream.Write(pixels, 0, pixels.Length);
                        }
                    }
                });
            }
            finally { cache.Clear(); }
        }
        private void ToggleTimeline()
        {
            if (focusReview) hideFocusFilmstrip = !hideFocusFilmstrip;
            else compactTimeline = !compactTimeline;
            UpdateWorkspace();
        }
        internal void RefreshTimelineLayout() { UpdateWorkspace(); }
        private void ToggleFull()
        {
            if (!full)
            {
                previousState = WindowState; previousBounds = RestoreBounds;
                WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.None;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                if (previousBounds.Width > 0) Bounds = previousBounds;
                WindowState = previousState;
            }
            full = !full;
            UpdateWorkspace();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            switch (keys)
            {
                case Keys.Control | Keys.L: OpenMedia(true); return true;
                case Keys.Control | Keys.O: ChooseProject(); return true;
                case Keys.Control | Keys.S: SaveProject(false); return true;
                case Keys.Control | Keys.Shift | Keys.S: SaveProject(true); return true;
                case Keys.Control | Keys.N: NewProject(); return true;
                case Keys.Control | Keys.Delete: RemoveLayer(); return true;
                case Keys.Control | Keys.G: GoToFrame(); return true;
                case Keys.Control | Keys.E: ExportMenu(); return true;
                case Keys.Control | Keys.F: timeline.FocusPlayhead(); return true;
                case Keys.Control | Keys.W: ToggleCompare(); return true;
                case Keys.F9: ToggleFocus(); return true;
                case Keys.F11: ToggleFull(); return true;
                case Keys.Escape:
                    if (full) { ToggleFull(); return true; }
                    if (focusReview) { ToggleFocus(); return true; }
                    break;
            }
            Control focus = ActiveControl;
            while (focus is ContainerControl && ((ContainerControl)focus).ActiveControl != null)
                focus = ((ContainerControl)focus).ActiveControl;
            if (focus is NumericUpDown || focus is ComboBox || focus is TextBox) return base.ProcessCmdKey(ref msg, keys);
            switch (keys)
            {
                case Keys.Space: case Keys.Enter: TogglePlay(); return true;
                case Keys.Left: Seek(Frame - 1); return true;
                case Keys.Right: Seek(Frame + 1); return true;
                case Keys.Shift | Keys.Left: Seek(Frame - 10); return true;
                case Keys.Shift | Keys.Right: Seek(Frame + 10); return true;
                case Keys.Control | Keys.Left: Seek(Frame - 100); return true;
                case Keys.Control | Keys.Right: Seek(Frame + 100); return true;
                case Keys.Up: Play(1); return true;
                case Keys.Down: Play(-1); return true;
                case Keys.Home: Seek(WorkIn); return true;
                case Keys.End: Seek(WorkOut); return true;
                case Keys.Alt | Keys.Home: SetRange(true); return true;
                case Keys.Alt | Keys.End: SetRange(false); return true;
                case Keys.I: SetRange(true); return true;
                case Keys.O: SetRange(false); return true;
                case Keys.Shift | Keys.I: TrimAtFrame(true); return true;
                case Keys.Shift | Keys.O: TrimAtFrame(false); return true;
                case Keys.Tab: ToggleTimeline(); return true;
                case Keys.NumPad5: viewer.Fit(); return true;
                case Keys.M: ToggleMark(); return true;
                case Keys.Shift | Keys.M: JumpMarker(-1); return true;
                case Keys.Control | Keys.M: JumpMarker(1); return true;
                case Keys.U: Reload(); return true;
                case Keys.R: case Keys.G: case Keys.B: case Keys.A: case Keys.L:
                    SetChannel(keys.ToString()); return true;
            }
            return base.ProcessCmdKey(ref msg, keys);
        }
    }
}
