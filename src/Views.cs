using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace FrameDeck
{
    internal static class Theme
    {
        private static readonly Color[] Dark = Palette(
            0x12161F, 0x191E29, 0x232A3A, 0x303D60, 0x2B3343, 0xEDF1FA, 0xA6B1C6,
            0xC1CAFF, 0x182550, 0x2B385A, 0xDFE7FF, 0xE7C889, 0x8DC3B0, 0x0D1119);
        private static readonly Color[] Light = Palette(
            0xF2F5FA, 0xFBFCFF, 0xEDF1F8, 0xE3E9F3, 0xBCC7D9, 0x1C2434, 0x526279,
            0x3859A9, 0xFFFFFF, 0xDBE5FF, 0x253D7C, 0x915613, 0x0C715C, 0xE8ECF4);
        private static Color[] current = Dark;
        internal static bool IsDark { get { return current == Dark; } }
        internal static Color Background { get { return current[0]; } }
        internal static Color Panel { get { return current[1]; } }
        internal static Color Raised { get { return current[2]; } }
        internal static Color High { get { return current[3]; } }
        internal static Color Line { get { return current[4]; } }
        internal static Color Text { get { return current[5]; } }
        internal static Color Muted { get { return current[6]; } }
        internal static Color Primary { get { return current[7]; } }
        internal static Color OnPrimary { get { return current[8]; } }
        internal static Color Container { get { return current[9]; } }
        internal static Color OnContainer { get { return current[10]; } }
        internal static Color Amber { get { return current[11]; } }
        internal static Color Green { get { return current[12]; } }
        internal static Color Monitor { get { return current[13]; } }
        internal static readonly Font Small = new Font("Microsoft YaHei UI", 9);
        internal static readonly Font Strong = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        internal static readonly Font Caption = new Font("Microsoft YaHei UI", 8.25f);
        internal static readonly Font Compact = new Font("Microsoft YaHei UI", 7.5f);
        internal static readonly Font Icons = new Font("Segoe MDL2 Assets", 10);
        internal static readonly Font ToolbarIcons = new Font("Segoe MDL2 Assets", 13);
        internal static readonly Font Mono = new Font("Consolas", 10);
        internal static readonly Font CompactMono = new Font("Consolas", 7.75f);

        private static Color[] Palette(params int[] rgb)
        {
            return rgb.Select(value => Color.FromArgb(unchecked((int)0xFF000000) | value)).ToArray();
        }
        internal static Dictionary<int, Color> UseDark(bool dark)
        {
            Color[] next = dark ? Dark : Light;
            var replacements = new Dictionary<int, Color>();
            if (current == next) return replacements;
            for (int i = 0; i < current.Length; i++) replacements.Add(current[i].ToArgb(), next[i]);
            current = next;
            return replacements;
        }

        internal static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb((int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
        }
        internal static GraphicsPath Rounded(RectangleF rectangle, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
            if (d < 2) { path.AddRectangle(rectangle); return path; }
            path.AddArc(rectangle.Left, rectangle.Top, d, d, 180, 90);
            path.AddArc(rectangle.Right - d, rectangle.Top, d, d, 270, 90);
            path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        internal static void FillRounded(Graphics graphics, Color color, Rectangle rectangle, float radius)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(color))
            using (var path = Rounded(rectangle, radius)) graphics.FillPath(brush, path);
            graphics.SmoothingMode = previous;
        }
        internal static void StrokeRounded(Graphics graphics, Color color, Rectangle rectangle, float radius)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color))
            using (var path = Rounded(rectangle, radius)) graphics.DrawPath(pen, path);
            graphics.SmoothingMode = previous;
        }

        internal static void Fill(Graphics graphics, Color color, Rectangle rectangle)
        {
            using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, rectangle);
        }
        internal static void LineAt(Graphics graphics, Color color, int x1, int y1, int x2, int y2)
        {
            using (var pen = new Pen(color)) graphics.DrawLine(pen, x1, y1, x2, y2);
        }
        internal static void Label(Graphics graphics, string text, Font font, Color color, Rectangle box,
            TextFormatFlags flags = TextFormatFlags.Left)
        {
            TextRenderer.DrawText(graphics, text, font, box, color, flags | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
        internal static void DrawIcon(Graphics graphics, string glyph, Font font, Color color, Rectangle box)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            using (var path = new GraphicsPath())
            using (var transform = new Matrix())
            using (var brush = new SolidBrush(color))
            {
                path.AddString(glyph, font.FontFamily, (int)font.Style, font.SizeInPoints * graphics.DpiY / 72f,
                    PointF.Empty, StringFormat.GenericTypographic);
                if (path.PointCount == 0) return;
                path.Flatten(null, .05f);
                RectangleF ink = path.GetBounds();
                transform.Translate(box.Left + (box.Width - ink.Width) / 2f - ink.Left,
                    box.Top + (box.Height - ink.Height) / 2f - ink.Top);
                path.Transform(transform);
                SmoothingMode previous = graphics.SmoothingMode;
                PixelOffsetMode previousOffset = graphics.PixelOffsetMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.FillPath(brush, path);
                graphics.PixelOffsetMode = previousOffset;
                graphics.SmoothingMode = previous;
            }
        }
    }

    internal sealed class Viewer : Control
    {
        private readonly MainForm app;
        private readonly MaterialButton importAction, demoAction;
        private Bitmap importBackdrop;
        private Color backdropColor;
        private int lastPaintedFrame = -1;
        private static readonly int ScrubStep = UiScale.Px(6);
        private Point last;
        private Point leftDown;
        private bool leftDragMoved, previousLeftDragMoved, doubleClickPending;
        private MouseButtons dragButton;
        private bool wiping;
        private int wipeOffset;
        private float zoom = 1;
        private float? fixedZoom;
        private PointF pan;
        internal bool Compare { get { return app.Session.Compare; } }
        internal string Channel { get { return app.Session.Channel; } }
        internal float? FixedZoom { get { return fixedZoom; } }
        internal bool IsFit { get { return !fixedZoom.HasValue && zoom == 1; } }
        private float Split { get { return app.Session.Wipe; } }

        internal Viewer(MainForm app, MaterialButton importAction, MaterialButton demoAction)
        {
            this.app = app;
            this.importAction = importAction; this.demoAction = demoAction;
            importAction.PaintSurround = demoAction.PaintSurround = DrawImportBackdrop;
            DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Monitor;
            Controls.Add(importAction); Controls.Add(demoAction);
            foreach (Control surface in new Control[] { this, importAction, demoAction })
            {
                surface.AllowDrop = true;
                surface.DragEnter += delegate(object s, DragEventArgs e) {
                    if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
                };
                surface.DragDrop += delegate(object s, DragEventArgs e) { app.Import((string[])e.Data.GetData(DataFormats.FileDrop), true); };
            }
        }
        internal void ShowImportActions(bool visible)
        {
            importAction.Visible = demoAction.Visible = visible;
            if (!visible && importBackdrop != null)
            {
                importBackdrop.Dispose();
                importBackdrop = null;
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && importBackdrop != null) importBackdrop.Dispose();
            base.Dispose(disposing);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (importAction == null || demoAction == null) return;
            int gap = UiScale.Px(8);
            int left = (Width - importAction.Width - demoAction.Width - gap) / 2;
            int top = Math.Max(0, Math.Min(Height / 2 + UiScale.Px(48), Height - importAction.Height - UiScale.Px(10)));
            importAction.Location = new Point(left, top);
            demoAction.Location = new Point(importAction.Right + gap, importAction.Top);
        }
        internal void Fit()
        {
            fixedZoom = null; zoom = 1; CenterImage();
        }
        internal void SetZoom(float value)
        {
            fixedZoom = value; CenterImage();
        }
        internal void CenterImage()
        {
            pan = PointF.Empty; Invalidate(); app.UpdateStatus();
        }
        internal float DisplayZoom(Size native)
        {
            if (fixedZoom.HasValue) return fixedZoom.Value;
            if (native.IsEmpty) return zoom;
            float inset = UiScale.Px(app.IsFocusReview ? 4 : 32);
            return Math.Max(.001f, Math.Min((Width / (app.SideBySide ? 2f : 1f) - inset) / native.Width,
                (Height - inset) / native.Height) * zoom);
        }
        internal void ScaleBy(float factor, Point anchor)
        {
            float previous = fixedZoom ?? zoom;
            float next = Math.Max(fixedZoom.HasValue ? .01f : .12f, Math.Min(fixedZoom.HasValue ? 16 : 30, previous * factor));
            float ax = anchor.X - Width / 2f, ay = anchor.Y - Height / 2f;
            pan = new PointF(ax + (pan.X - ax) * next / previous, ay + (pan.Y - ay) * next / previous);
            if (fixedZoom.HasValue) fixedZoom = next;
            else zoom = next;
            Invalidate(); app.UpdateStatus();
        }
        protected override void OnMouseWheel(MouseEventArgs e) { ScaleBy(e.Delta > 0 ? 1.2f : 1 / 1.2f, e.Location); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (dragButton != MouseButtons.None) return;
            if (e.Button == MouseButtons.Left)
            {
                leftDown = e.Location;
                leftDragMoved = false;
                doubleClickPending = e.Clicks == 2 && !previousLeftDragMoved;
            }
            bool panDrag = e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right;
            if (!panDrag && (e.Button != MouseButtons.Left || app.Session.Layers.Count == 0)) return;
            dragButton = e.Button;
            last = e.Location;
            wipeOffset = e.X - (int)(Width * Split);
            bool overDivider = Math.Abs(wipeOffset) <= UiScale.Px(8) ||
                (e.Y >= UiScale.Px(13) && e.Y <= UiScale.Px(37) && Math.Abs(wipeOffset) <= UiScale.Px(29));
            wiping = !panDrag && Compare && !app.Session.CompareSideBySide && app.PreviewLayers.Count >= 2 && overDivider;
            Capture = true;
            Cursor = panDrag ? Cursors.Hand : Cursors.SizeWE;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragButton == MouseButtons.None) return;
            if (dragButton == MouseButtons.Left) leftDragMoved |= IsLeftDrag(e.Location);
            if (wiping) MoveSplit(e.X - wipeOffset);
            else if (dragButton == MouseButtons.Left) ScrubTo(e.X);
            else
            {
                pan.X += e.X - last.X; pan.Y += e.Y - last.Y;
                last = e.Location;
                Invalidate();
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool toggleFocus = false;
            if (e.Button == MouseButtons.Left)
            {
                previousLeftDragMoved = leftDragMoved || IsLeftDrag(e.Location);
                toggleFocus = doubleClickPending && !previousLeftDragMoved && ClientRectangle.Contains(e.Location);
                doubleClickPending = false;
            }
            if (dragButton == e.Button)
            {
                if (dragButton == MouseButtons.Left && !wiping) ScrubTo(e.X);
                EndDrag();
                Capture = false;
            }
            if (toggleFocus) app.ToggleFocus();
        }
        private bool IsLeftDrag(Point location)
        {
            int horizontal = Math.Abs(location.X - leftDown.X);
            return horizontal >= ScrubStep || (wiping && horizontal > 0) ||
                Math.Abs(location.Y - leftDown.Y) >= SystemInformation.DragSize.Height;
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture)
            {
                doubleClickPending = false;
                EndDrag();
            }
        }
        private void EndDrag()
        {
            if (dragButton == MouseButtons.None) return;
            dragButton = MouseButtons.None;
            wiping = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
        private void ScrubTo(int x)
        {
            int steps = (x - last.X) / ScrubStep;
            if (steps == 0) return;
            int frame = app.Frame;
            app.Seek(frame + steps);
            // Consume movement beyond either end so reversing direction responds immediately.
            last.X = app.Frame - frame == steps ? last.X + steps * ScrubStep : x;
        }
        private void MoveSplit(int x)
        {
            app.SetWipe(Math.Max(.02f, Math.Min(.98f, x / (float)Math.Max(1, Width))));
        }

        internal Size GetImageSize(int frame, bool immediate, FrameCache cache = null)
        {
            cache = cache ?? app.Cache;
            int visible = 0;
            Size largest = Size.Empty;
            bool side = app.SideBySide;
            foreach (Layer layer in app.Session.Layers)
            {
                if (!layer.Visible) continue;
                string key = layer.Key(frame);
                if (immediate) cache.Get(key, true, false);
                Size size = cache.OriginalSize(key, displayFallback: !immediate);
                if (!size.IsEmpty && !side) return size;
                largest = new Size(Math.Max(largest.Width, size.Width), Math.Max(largest.Height, size.Height));
                if (Compare && ++visible == 2) break;
            }
            return largest;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (Width < 4 || Height < 4) return;
            int frame = app.Frame;
            Size native = GetImageSize(frame, false);
            bool locating = native.IsEmpty && app.PreviewLayers.Any(layer =>
            {
                string key = layer.Key(frame);
                return key != null && VideoFrameSource.SourceId(key) != null && !app.Cache.Ready(key);
            });
            if (locating && lastPaintedFrame >= 0)
            {
                Size previous = GetImageSize(lastPaintedFrame, false);
                if (!previous.IsEmpty) { native = previous; frame = lastPaintedFrame; }
            }
            if (native.IsEmpty)
            {
                if (app.Session.Layers.Count == 0) PaintImportStage(g);
                else PaintUnavailableFrame(g);
                return;
            }
            bool sideBySide = app.SideBySide;
            float ratio = DisplayZoom(native);
            var center = new PointF(Width / 2f + pan.X, Height / 2f + pan.Y);
            var box = Rectangle.Round(new RectangleF(center.X - native.Width * ratio / 2,
                center.Y - native.Height * ratio / 2, native.Width * ratio, native.Height * ratio));
            if (box.Width > 0 && box.Height > 0)
            {
                using (Brush background = MakeBackground())
                {
                    if (sideBySide)
                    {
                        box.Offset(-Width / 4, 0);
                        g.FillRectangle(background, Rectangle.Intersect(box, new Rectangle(0, 0, Width / 2, Height)));
                        box.Offset(Width / 2, 0);
                        g.FillRectangle(background, Rectangle.Intersect(box, new Rectangle(Width / 2, 0, Width - Width / 2, Height)));
                    }
                    else g.FillRectangle(background, Rectangle.Intersect(box, ClientRectangle));
                }
            }
            bool compared = Render(g, frame, center, ratio, ClientRectangle, false,
                fastPreview: locating || app.IsPlaying || dragButton != MouseButtons.None);
            if (!locating) lastPaintedFrame = frame;
            if (compared)
            {
                int x = sideBySide ? Width / 2 : (int)(Width * Split);
                Theme.LineAt(g, Theme.Primary, x, 0, x, Height);
                if (sideBySide)
                {
                    var badge = UiScale.Box(13, 12, 30, 25);
                    Theme.FillRounded(g, Theme.Container, badge, UiScale.Px(12));
                    Theme.Label(g, "A", Theme.Mono, Theme.OnContainer, badge, TextFormatFlags.HorizontalCenter);
                    badge.Offset(x, 0);
                    Theme.FillRounded(g, Theme.Container, badge, UiScale.Px(12));
                    Theme.Label(g, "B", Theme.Mono, Theme.OnContainer, badge, TextFormatFlags.HorizontalCenter);
                }
                else
                {
                    var badge = UiScale.Box(-29, 13, 58, 24);
                    badge.Offset(x, 0);
                    Theme.FillRounded(g, Theme.Primary, badge, UiScale.Px(12));
                    Theme.Label(g, "A | B", Theme.Mono, Theme.OnPrimary, badge, TextFormatFlags.HorizontalCenter);
                }
            }
            if (locating)
            {
                string label = "正在定位第 " + (app.Frame + 1) + " 帧";
                int width = TextRenderer.MeasureText(g, label, Theme.Small).Width + UiScale.Px(24);
                var badge = new Rectangle((Width - width) / 2, Height - UiScale.Px(48), width, UiScale.Px(29));
                Theme.FillRounded(g, Theme.Panel, badge, UiScale.Px(8));
                Theme.Label(g, label, Theme.Small, Theme.Text, badge, TextFormatFlags.HorizontalCenter);
            }
        }

        private Brush MakeBackground()
        {
            switch (app.Session.PreviewBackground)
            {
                case "Black": return new SolidBrush(Color.Black);
                case "Gray": return new SolidBrush(Color.Gray);
                case "White": return new SolidBrush(Color.White);
                default: return new HatchBrush(HatchStyle.LargeCheckerBoard,
                    Theme.IsDark ? Color.FromArgb(43, 49, 60) : Color.FromArgb(197, 202, 212),
                    Theme.IsDark ? Color.FromArgb(32, 36, 46) : Color.FromArgb(219, 223, 232));
            }
        }

        private void DrawImportBackdrop(Graphics g, Point offset)
        {
            if (Width <= 1 || Height <= 1) return;
            if (importBackdrop == null || importBackdrop.Size != ClientSize || backdropColor != BackColor)
            {
                if (importBackdrop != null) importBackdrop.Dispose();
                backdropColor = BackColor;
                importBackdrop = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
                using (Graphics backdrop = Graphics.FromImage(importBackdrop))
                {
                    backdrop.Clear(backdropColor);
                    PaintImportBackdrop(backdrop);
                }
            }
            g.DrawImage(importBackdrop, new Rectangle(-offset.X, -offset.Y, Width, Height),
                0, 0, Width, Height, GraphicsUnit.Pixel);
        }

        private void PaintImportBackdrop(Graphics g)
        {
            int cx = Width / 2, mid = Height / 2;
            var glow = new Rectangle(cx - Width / 2, mid - Height / 2, Width, Height);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(glow);
                using (var fill = new PathGradientBrush(path))
                {
                    fill.CenterColor = Theme.Blend(Theme.Monitor, Theme.Primary, Theme.IsDark ? .075f : .095f);
                    fill.SurroundColors = new[] { Theme.Monitor };
                    g.FillRectangle(fill, glow);
                }
                using (var grid = new PathGradientBrush(path))
                {
                    grid.CenterColor = Color.FromArgb(17, Theme.Text);
                    grid.SurroundColors = new[] { Color.FromArgb(0, Theme.Text) };
                    int spacing = UiScale.Px(38);
                    for (int x = cx % spacing; x < Width; x += spacing) g.FillRectangle(grid, x, 0, 1, Height);
                    for (int y = mid % spacing; y < Height; y += spacing) g.FillRectangle(grid, 0, y, Width, 1);
                }
            }

        }

        private void PaintImportStage(Graphics g)
        {
            DrawImportBackdrop(g, Point.Empty);
            int cx = Width / 2;
            if (Height >= UiScale.Px(235))
            {
                var symbol = UiScale.Box(-32, -148, 64, 46);
                symbol.Offset(cx, importAction.Top);
                foreach (int angle in new[] { -13, 13 })
                {
                    var state = g.Save();
                    g.TranslateTransform(cx, symbol.Top + UiScale.Px(20));
                    g.RotateTransform(angle);
                    var tile = UiScale.Box(-32, -25, 64, 44);
                    Theme.FillRounded(g, Theme.Container, tile, UiScale.Px(10));
                    Theme.StrokeRounded(g, Theme.Blend(Theme.Line, Theme.Primary, .33f), tile, UiScale.Px(10));
                    g.Restore(state);
                }
                Theme.FillRounded(g, Theme.Container, symbol, UiScale.Px(10));
                Theme.StrokeRounded(g, Theme.Blend(Theme.Line, Theme.Primary, .52f), symbol, UiScale.Px(10));
                Theme.DrawIcon(g, "\uE8B9", Theme.ToolbarIcons, Theme.OnContainer, symbol);
            }
            Theme.Label(g, "导入图像序列或视频", app.DisplayFont, Theme.Text,
                new Rectangle(UiScale.Px(12), importAction.Top - UiScale.Px(83), Width - UiScale.Px(24), UiScale.Px(42)), TextFormatFlags.HorizontalCenter);
            Theme.Label(g, "拖入视频，或将序列中的任意一帧拖到这里。", Theme.Small, Theme.Muted,
                new Rectangle(UiScale.Px(12), importAction.Top - UiScale.Px(37), Width - UiScale.Px(24), UiScale.Px(26)), TextFormatFlags.HorizontalCenter);
        }

        private void PaintUnavailableFrame(Graphics g)
        {
            string error = app.PreviewLayers.Select(x => app.Cache.Error(x.Key(app.Frame)))
                .FirstOrDefault(x => x != null);
            bool loading = app.PreviewLayers.Any(x => x.Key(app.Frame) != null && !app.Cache.Ready(x.Key(app.Frame)));
            int mid = Height / 2;
            Theme.Label(g, error != null ? "当前帧无法解码" : loading ? "正在加载帧…" : "当前帧没有可见素材",
                app.DisplayFont, Theme.Text, new Rectangle(UiScale.Px(12), mid - UiScale.Px(28), Width - UiScale.Px(24), UiScale.Px(40)), TextFormatFlags.HorizontalCenter);
            Theme.Label(g, error ?? "",
                Theme.Small, Theme.Muted, new Rectangle(UiScale.Px(12), mid + UiScale.Px(17), Width - UiScale.Px(24), UiScale.Px(28)), TextFormatFlags.HorizontalCenter);
        }

        internal bool Render(Graphics g, int frame, PointF center, float scale, Rectangle viewport, bool immediate,
            FrameCache cache = null, bool fastPreview = false)
        {
            cache = cache ?? app.Cache;
            string filter = immediate ? "Auto" : app.Session.PreviewFilter;
            switch (filter)
            {
                case "Nearest": g.InterpolationMode = InterpolationMode.NearestNeighbor; break;
                case "Linear": g.InterpolationMode = InterpolationMode.Bilinear; break;
                case "Quality": g.InterpolationMode = InterpolationMode.HighQualityBilinear; break;
                default: g.InterpolationMode = scale >= 2 ? InterpolationMode.NearestNeighbor :
                    fastPreview ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBilinear; break;
            }
            fastPreview = fastPreview && filter == "Auto";
            g.PixelOffsetMode = PixelOffsetMode.Half;
            List<Layer> layers = app.Session.Layers;
            if (Compare)
            {
                Layer first = null;
                foreach (Layer layer in layers)
                {
                    if (!layer.Visible) continue;
                    if (first == null) { first = layer; continue; }
                    bool side = app.Session.CompareSideBySide;
                    float shift = side ? viewport.Width / 4f : 0;
                    int x = viewport.X + (side ? viewport.Width / 2 : (int)(viewport.Width * Split));
                    var state = g.Save();
                    g.SetClip(new Rectangle(viewport.X, viewport.Y, x - viewport.X, viewport.Height), CombineMode.Intersect);
                    DrawLayer(g, first, frame, new PointF(center.X - shift, center.Y), scale, immediate, cache, fastPreview);
                    g.Restore(state);
                    state = g.Save();
                    g.SetClip(new Rectangle(x, viewport.Y, viewport.Right - x, viewport.Height), CombineMode.Intersect);
                    DrawLayer(g, layer, frame, new PointF(center.X + shift, center.Y), scale, immediate, cache, fastPreview);
                    g.Restore(state);
                    return true;
                }
            }
            for (int i = layers.Count - 1; i >= 0; i--)
                if (layers[i].Visible) DrawLayer(g, layers[i], frame, center, scale, immediate, cache, fastPreview);
            return false;
        }
        private void DrawLayer(Graphics g, Layer layer, int frame, PointF center, float scale, bool immediate, FrameCache cache,
            bool fastPreview)
        {
            string key = layer.Key(frame);
            Bitmap bitmap = cache.Get(key, immediate, displayFallback: !immediate);
            if (bitmap == null) return;
            Size native = cache.OriginalSize(key, displayFallback: !immediate);
            float width = native.Width * scale, height = native.Height * scale;
            float left = center.X - width / 2, top = center.Y - height / 2;
            if (fastPreview && scale < 2)
                g.InterpolationMode = width > bitmap.Width && native.Width != bitmap.Width ?
                    InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
            if (!layer.Flip && layer.Exposure == 0 && layer.Opacity == 100 && Channel == "RGB")
            {
                var destination = Rectangle.Round(new RectangleF(left, top, width, height));
                if (destination.Size == bitmap.Size && (fastPreview || (left == destination.X && top == destination.Y)))
                    g.DrawImageUnscaled(bitmap, destination.Location);
                else
                    g.DrawImage(bitmap, destination, 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel);
                return;
            }
            var points = layer.Flip
                ? new[] { new PointF(left + width, top), new PointF(left, top), new PointF(left + width, top + height) }
                : new[] { new PointF(left, top), new PointF(left + width, top), new PointF(left, top + height) };
            using (var attr = new ImageAttributes())
            {
                attr.SetWrapMode(WrapMode.TileFlipXY);
                attr.SetColorMatrix(Matrix(layer));
                g.DrawImage(bitmap, points, new RectangleF(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel, attr);
            }
        }
        private ColorMatrix Matrix(Layer layer)
        {
            float gain = (float)Math.Pow(2, layer.Exposure / 100.0);
            var matrix = new ColorMatrix { Matrix00 = gain, Matrix11 = gain, Matrix22 = gain, Matrix33 = layer.Opacity / 100f };
            if (Channel == "RGB") return matrix;
            matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = 0;
            if (Channel == "A")
            {
                matrix.Matrix30 = matrix.Matrix31 = matrix.Matrix32 = 1;
                matrix.Matrix33 = 0;
                matrix.Matrix43 = 1;
                return matrix;
            }
            float red = Channel == "R" ? gain : Channel == "L" ? .2126f * gain : 0;
            float green = Channel == "G" ? gain : Channel == "L" ? .7152f * gain : 0;
            float blue = Channel == "B" ? gain : Channel == "L" ? .0722f * gain : 0;
            matrix.Matrix00 = matrix.Matrix01 = matrix.Matrix02 = red;
            matrix.Matrix10 = matrix.Matrix11 = matrix.Matrix12 = green;
            matrix.Matrix20 = matrix.Matrix21 = matrix.Matrix22 = blue;
            return matrix;
        }
    }

    internal sealed class Timeline : Control
    {
        private readonly MainForm app;
        private readonly ToolTip toolbarTips;
        private static readonly int Rail = UiScale.Px(188), Ruler = UiScale.Px(33), ClipTitle = UiScale.Px(19), TileWidth = UiScale.Px(102);
        private int rowOffset, dragFrame, dragStart, dragLayer = -1;
        private bool scrub, dragMoved;
        private int first, last = 95;
        private int alternateFirst, alternateLast = 95;
        private bool focusFilmstrip;
        private static readonly int FocusInset = UiScale.Px(10), FocusRuler = UiScale.Px(19), FocusTileWidth = UiScale.Px(58);
        private readonly SortedSet<int> recent = new SortedSet<int>();
        private readonly Queue<int> recentOrder = new Queue<int>();
        private readonly FrameCache thumbnails;
        private readonly List<string> thumbnailRequests = new List<string>();
        internal bool Expanded { get; private set; }
        private int Row { get { return UiScale.Px(Expanded ? 72 : 28); } }
        private Rectangle ThumbnailToggle { get { return UiScale.Box(178 - 39, 4, 29, 35 - 8); } }
        private Rectangle ExrPassButton(int y)
        {
            int height = UiScale.Px(22);
            return new Rectangle(Rail - UiScale.Px(55), y + (Math.Min(Row, UiScale.Px(28)) - height) / 2,
                UiScale.Px(48), height);
        }

        private bool TryExrPassButton(Point point, out int index, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            index = -1;
            if (focusFilmstrip || point.Y < Ruler || point.X >= Rail) return false;
            index = rowOffset + (point.Y - Ruler) / Row;
            if (index >= app.Session.Layers.Count) return false;
            bounds = ExrPassButton(Ruler + (index - rowOffset) * Row);
            var source = app.Session.Layers[index].Source;
            return bounds.Contains(point) && source != null && source.ExrPasses.Length > 1;
        }
        internal Timeline(MainForm app, ToolTip toolbarTips)
        {
            this.app = app; this.toolbarTips = toolbarTips;
            DoubleBuffered = true; ResizeRedraw = true; TabStop = false; Margin = Padding.Empty;
            BackColor = Theme.Panel;
            thumbnails = new FrameCache(this, delegate { Invalidate(); }, 1, new Size(152, 86));
            thumbnails.Configure(1, 16);
        }
        internal void ToggleThumbnails()
        {
            Expanded = !Expanded;
            if (!Expanded) StopThumbnailRequests();
            app.RefreshTimelineLayout();
            ShowSelection();
        }
        internal void SetFocusFilmstrip(bool enabled)
        {
            if (focusFilmstrip == enabled) return;
            focusFilmstrip = enabled;
            toolbarTips.SetToolTip(this, null);
            int oldFirst = first, oldLast = last;
            first = alternateFirst; last = alternateLast;
            alternateFirst = oldFirst; alternateLast = oldLast;
            StopThumbnailRequests();
            if (Capture) Capture = false;
            Invalidate();
        }
        internal void StopThumbnailRequests()
        {
            thumbnailRequests.Clear();
            thumbnails.Request(thumbnailRequests, 0);
        }
        internal void ClearThumbnails()
        {
            thumbnailRequests.Clear();
            thumbnails.Clear();
        }
        private int TrackLeft { get { return focusFilmstrip ? FocusInset : Rail; } }
        private int AreaWidth { get { return Math.Max(1, Width - TrackLeft - (focusFilmstrip ? FocusInset : UiScale.Px(14))); } }
        private int TrackSpan { get { return Math.Max(1, last - first + (focusFilmstrip ? 1 : 0)); } }
        private int VisibleRows { get { return Math.Max(1, (Height - Ruler) / Row); } }
        private int X(int frame) { return TrackLeft + (int)Math.Round((frame - first) * (double)AreaWidth / TrackSpan); }
        private int At(int x, bool clamp = true)
        {
            double offset = (x - TrackLeft) * (double)TrackSpan / AreaWidth;
            int frame = first + (focusFilmstrip ? Math.Min(last - first, (int)Math.Floor(offset)) : (int)Math.Round(offset));
            return clamp ? Math.Max(0, Math.Min(app.MaxFrame, frame)) : frame;
        }
        internal void ResetView()
        {
            first = alternateFirst = 0; last = alternateLast = Math.Max(1, app.MaxFrame);
            rowOffset = Math.Min(rowOffset, Math.Max(0, app.Session.Layers.Count - VisibleRows)); Invalidate();
        }
        internal void ClearCacheMarks()
        {
            recent.Clear(); recentOrder.Clear();
        }
        internal void TrackCacheFrame(int frame)
        {
            if (!recent.Add(frame)) return;
            recentOrder.Enqueue(frame);
            if (recentOrder.Count > 768) recent.Remove(recentOrder.Dequeue());
        }
        internal void RefreshCacheMarks()
        {
            if (focusFilmstrip) return;
            Invalidate(new Rectangle(Rail, Ruler - 3, AreaWidth + 1, 3));
        }
        private void DrawCacheMarks(Graphics g)
        {
            using (var brush = new SolidBrush(Theme.Green))
            {
                int start = -1, end = -1;
                foreach (int frame in recent.GetViewBetween(first, last))
                {
                    if (!app.IsFrameCached(frame)) continue;
                    int left = X(frame), right = Math.Min(Rail + AreaWidth + 1, Math.Max(left + 1, X(frame + 1)));
                    if (left > end)
                    {
                        if (start >= 0) g.FillRectangle(brush, start, Ruler - 3, end - start, 2);
                        start = left;
                    }
                    end = Math.Max(end, right);
                }
                if (start >= 0) g.FillRectangle(brush, start, Ruler - 3, end - start, 2);
            }
        }
        internal void FocusPlayhead()
        {
            ZoomAround(last - first + 1);
        }
        internal void FollowFocusFrame()
        {
            if (focusFilmstrip && (app.Frame < first || app.Frame > last)) FocusPlayhead();
        }
        internal void ZoomAround(int frameCount)
        {
            int limit = Math.Max(1, app.MaxFrame);
            int span = Math.Max(1, Math.Min(limit, frameCount - 1));
            int from = Math.Max(0, Math.Min(limit - span, app.Frame - span / 2));
            ShowFrames(from, from + span);
        }
        internal void ShowFrames(int from, int to)
        {
            int limit = Math.Max(1, app.MaxFrame);
            first = Math.Max(0, Math.Min(limit - 1, from));
            last = Math.Min(limit, Math.Max(first + 1, to));
            Invalidate();
        }
        internal void ShowSelection()
        {
            if (app.Selected < rowOffset) rowOffset = Math.Max(0, app.Selected);
            if (app.Selected >= rowOffset + VisibleRows) rowOffset = app.Selected - VisibleRows + 1;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Panel);
            if (focusFilmstrip) { PaintFocusFilmstrip(g); return; }
            bool hasMedia = app.Session.Layers.Count > 0;
            Theme.Fill(g, Theme.Blend(Theme.Background, Theme.Panel, .45f), new Rectangle(0, 0, Width, Ruler));
            Theme.Label(g, "图层 / 时间轴 ▾", Theme.Small, Theme.Muted, UiScale.Box(12, 0, 178 - 57, 35));
            if (Expanded) Theme.FillRounded(g, Theme.Container, ThumbnailToggle, UiScale.Px(7));
            Theme.DrawIcon(g, "\uE8B9", Theme.Icons, Expanded ? Theme.OnContainer : Theme.Muted, ThumbnailToggle);
            int rangeL = Math.Max(Rail, X(app.WorkIn)), rangeR = Math.Min(Width, X(app.WorkOut) + 1);
            if (app.HasWorkRange && rangeR > rangeL)
            {
                Theme.FillRounded(g, Theme.Container, new Rectangle(rangeL, UiScale.Px(3), rangeR - rangeL, Ruler - UiScale.Px(6)), UiScale.Px(9));
                Theme.Fill(g, Theme.Primary, new Rectangle(rangeL, UiScale.Px(3), rangeR - rangeL, UiScale.Px(2)));
            }
            int interval = TickInterval();
            for (int frame = (first + interval - 1) / interval * interval; frame <= (hasMedia ? last : 0); frame += interval)
            {
                int x = X(frame);
                Theme.LineAt(g, Theme.Line, x, UiScale.Px(24), x, Height);
                var label = UiScale.Box(3, 3, 64, 20); label.Offset(x, 0);
                Theme.Label(g, (frame + 1).ToString(), Theme.Mono, Theme.Muted, label);
            }
            bool updateThumbs = Expanded && e.ClipRectangle.Bottom > Ruler;
            if (updateThumbs) thumbnailRequests.Clear();
            for (int i = rowOffset; i < app.Session.Layers.Count && i < rowOffset + VisibleRows; i++)
            {
                Layer layer = app.Session.Layers[i];
                int y = Ruler + (i - rowOffset) * Row;
                if (i == app.Selected) Theme.Fill(g, Theme.Raised, new Rectangle(0, y, Rail, Row));
                Theme.LineAt(g, Theme.Line, 0, y + Row - 1, Width, y + Row - 1);
                Theme.Label(g, layer.Visible ? "●" : "○", Theme.Small, layer.Visible ? Theme.Green : Theme.Muted, new Rectangle(UiScale.Px(13), y, UiScale.Px(24), Row));
                bool hasPasses = layer.Source != null && layer.Source.ExrPasses.Length > 1;
                Theme.Label(g, layer.Name, Theme.Small, Theme.Text,
                    new Rectangle(UiScale.Px(37), y, Rail - UiScale.Px(hasPasses ? 98 : 48), Row));
                if (hasPasses)
                {
                    var button = ExrPassButton(y);
                    Theme.FillRounded(g, Theme.Container, button, UiScale.Px(6));
                    Theme.Label(g, "EXR ▾", Theme.Caption, Theme.OnContainer, button, TextFormatFlags.HorizontalCenter);
                }
                int l = Math.Max(Rail, X(layer.Start)), r = Math.Min(Width, X(layer.End) + 1);
                if (r <= l) continue;
                var box = new Rectangle(l, y + UiScale.Px(4), Math.Min(Width - l, Math.Max(UiScale.Px(7), r - l)), Row - UiScale.Px(8));
                Theme.FillRounded(g, !layer.Visible ? Theme.Raised : i == app.Selected ? Theme.Container : Theme.High, box, UiScale.Px(8));
                if (box.Width > UiScale.Px(38))
                    Theme.Label(g, layer.Name, Theme.Small, Theme.Text,
                        new Rectangle(box.X + UiScale.Px(7), box.Y, box.Width - UiScale.Px(10), Expanded ? ClipTitle : box.Height));
                if (updateThumbs && layer.Visible) DrawThumbnails(g, layer, box.Y + ClipTitle, Row - ClipTitle - UiScale.Px(9), TileWidth);
            }
            if (updateThumbs) thumbnails.Request(thumbnailRequests, 0);
            if (!hasMedia)
            {
                Theme.Label(g, "暂无图层", Theme.Small, Theme.Muted, new Rectangle(UiScale.Px(12), Ruler + UiScale.Px(3), Rail - UiScale.Px(16), Row));
                int tick = UiScale.Px(40);
                for (int x = Rail + tick; x < Width; x += tick)
                {
                    int bottom = (x - Rail) % (tick * 4) == 0 ? Height : Ruler - 1;
                    Theme.LineAt(g, Theme.Line, x, Ruler - UiScale.Px(7), x, bottom);
                }
            }
            Theme.LineAt(g, Theme.Line, 0, Ruler - 1, Width, Ruler - 1);
            Theme.LineAt(g, Theme.Line, Rail - 1, 0, Rail - 1, Height);
            foreach (int mark in app.Session.Marks.Where(x => x >= first && x <= last))
            {
                int x = X(mark);
                using (var brush = new SolidBrush(Theme.Amber))
                    g.FillPolygon(brush, new[] { new Point(x, UiScale.Px(22)), new Point(x + UiScale.Px(5), UiScale.Px(27)),
                        new Point(x, UiScale.Px(32)), new Point(x - UiScale.Px(5), UiScale.Px(27)) });
            }
            if (e.ClipRectangle.Top < Ruler && e.ClipRectangle.Bottom > Ruler - 3) DrawCacheMarks(g);
            if (app.Frame >= first && app.Frame <= last)
            {
                int x = X(app.Frame);
                var head = UiScale.Box(-4, 14, 9, 12); head.Offset(x, 0);
                Theme.LineAt(g, Theme.Amber, x, head.Top, x, Height);
                Theme.FillRounded(g, Theme.Amber, head, UiScale.Px(4));
            }
        }

        private int TickInterval()
        {
            int interval = 1;
            while (TrackSpan * (double)UiScale.Px(65) / AreaWidth > interval)
                interval = interval.ToString()[0] == '1' ? interval * 2 :
                    interval.ToString()[0] == '2' ? interval / 2 * 5 : interval * 2;
            return interval;
        }

        private void PaintFocusFilmstrip(Graphics g)
        {
            if (app.Session.Layers.Count == 0) return;
            int trackEnd = TrackLeft + AreaWidth;
            var strip = new Rectangle(TrackLeft, FocusRuler, AreaWidth, Math.Max(1, Height - FocusRuler - 3));
            Theme.Fill(g, Theme.Background, strip);
            Layer layer = app.Selected >= 0 && app.Selected < app.Session.Layers.Count ? app.Session.Layers[app.Selected] : null;
            if (!app.PreviewLayers.Contains(layer)) layer = app.PreviewLayers.FirstOrDefault();
            thumbnailRequests.Clear();
            if (layer != null) DrawThumbnails(g, layer, strip.Top, strip.Height, FocusTileWidth);
            thumbnails.Request(thumbnailRequests, 0);

            int interval = TickInterval();
            for (int frame = (first + interval - 1) / interval * interval; frame <= last; frame += interval)
            {
                int x = X(frame);
                Theme.Label(g, (frame + 1).ToString(), Theme.CompactMono, Theme.Muted,
                    new Rectangle(x + UiScale.Px(2), 0, Math.Max(0, Math.Min(UiScale.Px(64), trackEnd - x - UiScale.Px(2))), FocusRuler - 2));
            }
            int left = X(Math.Max(first, app.WorkIn)), right = X(Math.Min(last, app.WorkOut) + 1);
            if (app.HasWorkRange && right > left) Theme.Fill(g, Theme.Primary, new Rectangle(left, FocusRuler - 2, right - left, 2));
            foreach (int mark in app.Session.Marks.Where(frame => frame >= first && frame <= last))
                Theme.FillRounded(g, Theme.Amber, new Rectangle(X(mark) - UiScale.Px(2), FocusRuler - UiScale.Px(4), UiScale.Px(5), UiScale.Px(5)), UiScale.Px(2));
            if (app.Frame < first || app.Frame > last) return;
            int playhead = (X(app.Frame) + X(app.Frame + 1)) / 2;
            Theme.LineAt(g, Theme.Amber, playhead, FocusRuler - UiScale.Px(5), playhead, strip.Bottom);
            Theme.FillRounded(g, Theme.Amber, new Rectangle(playhead - UiScale.Px(3), FocusRuler - UiScale.Px(6), UiScale.Px(7), UiScale.Px(7)), UiScale.Px(3));
        }

        private void DrawThumbnails(Graphics graphics, Layer layer, int y, int tileHeight, int tileWidth)
        {
            int leftFrame = Math.Max(first, layer.Start), rightFrame = Math.Min(last, layer.End);
            int framesPerTile = Math.Max(1, (int)Math.Ceiling(tileWidth * (double)TrackSpan / AreaWidth));
            int lastPixel = TrackLeft + AreaWidth + (focusFilmstrip ? 0 : 1);
            int to;
            for (int slot = leftFrame / framesPerTile * framesPerTile; slot <= rightFrame; slot = to + 1)
            {
                int from = Math.Max(leftFrame, slot);
                to = Math.Min(rightFrame, slot + framesPerTile - 1);
                if (focusFilmstrip && rightFrame - to < framesPerTile / 2.0) to = rightFrame;
                int x = X(from), end = Math.Min(lastPixel, Math.Max(x + 1, X(to + 1)));
                if (end - x < 8) continue;
                var box = new Rectangle(x + 1, y, end - x - 2, tileHeight);
                Theme.Fill(graphics, Theme.Background, box);
                string key = layer.Key(from + (to - from) / 2);
                if (key == null) continue;
                Bitmap bitmap = thumbnails.Get(key, false);
                if (bitmap == null)
                {
                    if (!app.IsPlaying && !thumbnails.Ready(key) && thumbnailRequests.Count < 96)
                        thumbnailRequests.Add(key);
                    continue;
                }
                float scale = Math.Min(box.Width / (float)bitmap.Width, box.Height / (float)bitmap.Height);
                int width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                int height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                graphics.DrawImage(bitmap, new Rectangle(box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2,
                    width, height), 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (focusFilmstrip)
            {
                if (e.Button == MouseButtons.Left && app.Session.Layers.Count > 0 && e.X >= TrackLeft && e.X < TrackLeft + AreaWidth)
                { scrub = true; Capture = true; app.Seek(At(e.X)); }
                return;
            }
            if (e.Button == MouseButtons.Left && ThumbnailToggle.Contains(e.Location))
            { ToggleThumbnails(); return; }
            if (e.Y < Ruler && (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && e.X < Rail)))
            { app.TimelineMenu(e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            int passIndex;
            Rectangle passButton;
            if (TryExrPassButton(e.Location, out passIndex, out passButton)) return;
            int index = rowOffset + (e.Y - Ruler) / Row;
            if (e.Y >= Ruler && index < app.Session.Layers.Count)
            {
                app.SelectLayer(index);
                if (e.X < UiScale.Px(32)) { app.ToggleVisible(); return; }
                if (e.X < Rail) return;
                Layer layer = app.Session.Layers[index];
                if (e.X >= Math.Max(Rail, X(layer.Start)) && e.X <= X(layer.End) + UiScale.Px(4))
                {
                    if (!Expanded || (e.Y - Ruler) % Row < ClipTitle + UiScale.Px(4))
                    {
                        dragLayer = index; dragFrame = At(e.X, false); dragStart = layer.Start;
                        dragMoved = false; Capture = true; return;
                    }
                }
            }
            if (e.X >= Rail)
            { scrub = true; Capture = true; app.Seek(At(e.X)); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            string hint = !focusFilmstrip && ThumbnailToggle.Contains(e.Location) ? (Expanded ? "收起缩略图" : "展开缩略图") : null;
            int passIndex;
            Rectangle passButton;
            if (TryExrPassButton(e.Location, out passIndex, out passButton))
                hint = "切换 EXR 渲染层 · " + app.Session.Layers[passIndex].Source.ResolveExrPass(
                    app.Session.Layers[passIndex].ExrPassId).Name;
            if (!string.Equals(toolbarTips.GetToolTip(this) ?? "", hint ?? "")) toolbarTips.SetToolTip(this, hint);
            if (scrub) app.Seek(At(e.X));
            if (dragLayer < 0) return;
            int delta = At(e.X, false) - dragFrame;
            if (!dragMoved && delta == 0) return;
            dragMoved = true;
            app.MoveLayerStart(Math.Max(0, dragStart + delta), false);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (!focusFilmstrip && !scrub && dragLayer < 0 && e.Y >= Ruler && ClientRectangle.Contains(e.Location))
                {
                    int index = rowOffset + (e.Y - Ruler) / Row;
                    if (index < app.Session.Layers.Count)
                        app.LayerContextMenu(index, e.Location);
                }
                return;
            }
            int passIndex;
            Rectangle passButton;
            if (e.Button == MouseButtons.Left && !scrub && dragLayer < 0 &&
                TryExrPassButton(e.Location, out passIndex, out passButton))
            {
                app.SelectLayer(passIndex);
                app.ShowExrPassMenu(this, new Point(passButton.Left, passButton.Top - UiScale.Px(4)));
                return;
            }
            if (dragLayer >= 0)
            {
                if (!dragMoved) app.Seek(At(e.X));
                else app.RefreshProject(true);
            }
            Capture = false; scrub = false; dragLayer = -1;
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { base.OnMouseCaptureChanged(e); if (!Capture) { scrub = false; dragLayer = -1; } }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!focusFilmstrip && (ModifierKeys & Keys.Control) == 0)
            {
                rowOffset = Math.Max(0, Math.Min(Math.Max(0, app.Session.Layers.Count - VisibleRows), rowOffset - Math.Sign(e.Delta)));
            }
            else
            {
                int limit = Math.Max(1, app.MaxFrame);
                int size = Math.Min(limit, Math.Max(1, (int)Math.Round((last - first) * (e.Delta > 0 ? .72 : 1.39))));
                double position = Math.Max(0, Math.Min(1, (e.X - TrackLeft) / (double)AreaWidth));
                first = Math.Max(0, Math.Min(limit - size, At(e.X) - (int)(size * position)));
                last = first + size;
            }
            Invalidate();
        }
    }
}
