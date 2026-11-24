using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FrameDeck
{
    internal static class UiScale
    {
        internal static readonly float Factor = ReadFactor();
        private static float ReadFactor()
        {
            using (var screen = Graphics.FromHwnd(IntPtr.Zero)) return screen.DpiX / 96f;
        }

        internal static int Px(int value) { return (int)Math.Round(value * Factor, MidpointRounding.AwayFromZero); }
        internal static Rectangle Box(int x, int y, int width, int height)
        {
            return new Rectangle(Px(x), Px(y), Px(width), Px(height));
        }

        internal static void ApplyLayout(ContainerControl container)
        {
            container.AutoScaleMode = AutoScaleMode.Dpi;
            container.AutoScaleDimensions = new SizeF(96f, 96f);
            container.PerformAutoScale();
        }
    }

    internal static class Motion
    {
        internal static bool Enabled { get { return !SystemInformation.HighContrast && SystemInformation.IsMenuAnimationEnabled; } }
        internal static float Ease(double progress)
        {
            float t = (float)Math.Max(0, Math.Min(1, progress));
            return 1 - (1 - t) * (1 - t) * (1 - t);
        }
    }

    internal sealed class MaterialButton : Button
    {
        internal string Glyph { get; set; }
        internal string Subtitle { get; set; }
        internal string ToolbarDescription { get; set; }
        internal bool ShowCycle { get; set; }
        internal bool FilledLightning { get; set; }
        internal Action LongPress { get; set; }
        internal Action DropDown { get; set; }
        internal Font CompactCaptionFont { get; set; }
        internal bool Pill { get; set; }
        internal bool SelectedPreset { get; set; }
        internal string ShortcutCaption { get; set; }
        internal bool? RangeStartIcon { get; set; }
        internal Action<Graphics, Point> PaintSurround { get; set; }

        internal MaterialButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        private bool hover, pressed;
        private Timer animation;
        private Timer holdTimer;
        private bool holdTriggered;
        private bool arrowPressed;
        private bool InArrow(Point point) { return DropDown != null && ClientRectangle.Contains(point) && point.X >= Width - UiScale.Px(30); }
        private Point holdOrigin;
        private readonly Stopwatch motionClock = new Stopwatch();
        private Color displayedFace, motionFrom, motionTo;
        protected override void OnMouseEnter(EventArgs e) { hover = true; AnimateFace(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { if (holdTimer != null) holdTimer.Stop(); hover = false; AnimateFace(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            holdTriggered = false; holdOrigin = e.Location;
            arrowPressed = InArrow(e.Location);
            pressed = true; AnimateFace();
            if (arrowPressed) return;
            if (LongPress == null) return;
            if (holdTimer == null)
            {
                holdTimer = new Timer { Interval = 500 };
                holdTimer.Tick += delegate
                {
                    holdTimer.Stop();
                    if (!pressed || !Enabled || !Visible) return;
                    holdTriggered = true; pressed = false; Capture = false; AnimateFace();
                    if (LongPress != null) LongPress();
                };
            }
            holdTimer.Start();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (holdTimer != null && (Math.Abs(e.X - holdOrigin.X) > SystemInformation.DragSize.Width / 2 ||
                Math.Abs(e.Y - holdOrigin.Y) > SystemInformation.DragSize.Height / 2)) holdTimer.Stop();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (holdTimer != null) holdTimer.Stop();
            pressed = false; AnimateFace();
            if (e.Button == MouseButtons.Left && arrowPressed)
            {
                arrowPressed = false;
                bool open = InArrow(e.Location);
                Capture = false;
                if (open && Enabled && DropDown != null) DropDown();
                return;
            }
            if (e.Button == MouseButtons.Left && holdTriggered) { holdTriggered = false; Capture = false; return; }
            base.OnMouseUp(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (holdTimer != null) holdTimer.Stop(); pressed = false; AnimateFace(); base.OnMouseCaptureChanged(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); AnimateFace(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); AnimateFace(); }
        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); AnimateFace(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); AnimateFace(); }

        private Color Face()
        {
            Color surround = Parent == null ? Theme.Panel : Parent.BackColor;
            if (!Enabled) return Theme.Blend(surround, Theme.Text, .045f);
            return pressed || hover || Focused ? Theme.Blend(BackColor, ForeColor, pressed ? .16f : .09f) : BackColor;
        }
        private void AnimateFace()
        {
            Color next = Face();
            if (!IsHandleCreated || !Visible || !Motion.Enabled || displayedFace.IsEmpty)
            {
                if (animation != null) animation.Stop();
                displayedFace = motionTo = next; Invalidate();
                return;
            }
            if (motionTo == next) { Invalidate(); return; }
            motionFrom = displayedFace; motionTo = next; motionClock.Restart();
            if (animation == null)
            {
                animation = new Timer { Interval = 15 };
                animation.Tick += delegate
                {
                    float mix = Motion.Ease(motionClock.Elapsed.TotalMilliseconds / 110);
                    displayedFace = Theme.Blend(motionFrom, motionTo, mix);
                    if (mix >= 1) animation.Stop();
                    Invalidate();
                };
            }
            animation.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && animation != null) animation.Dispose();
            if (disposing && holdTimer != null) holdTimer.Dispose();
            base.Dispose(disposing);
        }
        internal void ResetMotion()
        {
            if (animation != null) animation.Stop();
            displayedFace = motionTo = Face();
            Invalidate();
        }

        internal Font CaptionFont(out Rectangle bounds)
        {
            int padding = Math.Max(UiScale.Px(3), Math.Min(UiScale.Px(7), Width / 10));
            bounds = new Rectangle(padding, 0, Math.Max(1, Width - padding * 2), Height - 1);
            if (CompactCaptionFont == null || TextRenderer.MeasureText(Text, Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width <= bounds.Width) return Font;
            bounds.Inflate(UiScale.Px(4), 0);
            return CompactCaptionFont;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color surround = Parent == null ? Theme.Panel : Parent.BackColor;
            if (PaintSurround == null) g.Clear(surround);
            else PaintSurround(g, Location);
            bool card = !string.IsNullOrEmpty(Subtitle);
            Rectangle body = new Rectangle(0, 0, Width - 1, Height - 1);
            if (animation == null || !animation.Enabled) displayedFace = Face();
            Color face = displayedFace;
            float radius = Pill ? Height / 2f : UiScale.Px(card ? 10 : 9);
            Theme.FillRounded(g, face, body, radius);
            if (Focused && ShowFocusCues)
                Theme.StrokeRounded(g, Theme.Primary, Rectangle.Inflate(body, -2, -2), radius - 2);

            Color ink = Enabled ? ForeColor : Theme.Blend(surround, Theme.Text, .42f);
            if (ShowCycle && string.IsNullOrEmpty(ToolbarDescription))
            {
                var iconBox = new Rectangle(UiScale.Px(10), 0, UiScale.Px(20), Height);
                if (FilledLightning) DrawFilledLightning(g, ink, iconBox);
                else Theme.DrawIcon(g, Glyph, Theme.ToolbarIcons, ink, iconBox);
                Theme.Label(g, Text, Theme.Strong, ink,
                    new Rectangle(UiScale.Px(36), 0, Width - UiScale.Px(66), Height), TextFormatFlags.HorizontalCenter);
                DrawDropDownArrow(g, ink);
                return;
            }
            if (!string.IsNullOrEmpty(ToolbarDescription))
            {
                Theme.DrawIcon(g, Glyph, Theme.ToolbarIcons, ink,
                    new Rectangle(UiScale.Px(9), 0, UiScale.Px(20), Height));
                int left = UiScale.Px(36);
                Theme.Label(g, Text, Theme.Strong, ink,
                    new Rectangle(left, UiScale.Px(3), Width - left - UiScale.Px(ShowCycle ? 23 : 5), UiScale.Px(20)));
                Theme.Label(g, ToolbarDescription, Theme.Caption, Theme.Muted,
                    new Rectangle(left, UiScale.Px(22), Width - left - UiScale.Px(ShowCycle ? 23 : 5), UiScale.Px(16)));
                if (ShowCycle) DrawDropDownArrow(g, ink);
                return;
            }
            if (RangeStartIcon.HasValue)
            {
                DrawRangeIcon(g, ink, RangeStartIcon.Value);
                return;
            }
            if (card)
            {
                Theme.StrokeRounded(g, SelectedPreset ? Theme.Blend(Theme.Line, Theme.Primary, .5f) : Theme.Line, body, radius);
                int iconSize = UiScale.Px(30);
                var tile = new Rectangle(UiScale.Px(10), (Height - iconSize) / 2, iconSize, iconSize);
                Theme.FillRounded(g, Theme.Blend(face, ink, .085f), tile, UiScale.Px(7));
                Theme.DrawIcon(g, Glyph, Theme.Icons, ink, tile);
                int captionLeft = UiScale.Px(49), captionWidth = Width - UiScale.Px(78);
                Theme.Label(g, Text, Theme.Strong, ink, new Rectangle(captionLeft, UiScale.Px(6), captionWidth, UiScale.Px(21)));
                Theme.Label(g, Subtitle, Theme.Caption, Theme.Muted, new Rectangle(captionLeft, UiScale.Px(27), captionWidth, UiScale.Px(17)));
                if (SelectedPreset) Theme.Label(g, "✓", Theme.Strong, ink, new Rectangle(Width - UiScale.Px(28), 0, UiScale.Px(20), Height));
                return;
            }

            if (!string.IsNullOrEmpty(Glyph) && Text.Length == 0)
            {
                Theme.DrawIcon(g, Glyph, Theme.ToolbarIcons, ink, ClientRectangle);
                return;
            }

            Rectangle text;
            Font captionFont = CaptionFont(out text);
            if (!string.IsNullOrEmpty(Glyph))
            {
                int textWidth = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                int shortcutWidth = string.IsNullOrEmpty(ShortcutCaption) ? 0 :
                    TextRenderer.MeasureText(g, ShortcutCaption, Theme.CompactMono, Size.Empty, TextFormatFlags.NoPadding).Width + UiScale.Px(10);
                int labelOffset = UiScale.Px(20);
                int left = Math.Max(UiScale.Px(8), (Width - textWidth - labelOffset - shortcutWidth) / 2);
                Theme.DrawIcon(g, Glyph, Theme.Icons, ink, new Rectangle(left, 0, UiScale.Px(18), Height));
                text = new Rectangle(left + labelOffset, 0, Width - left - UiScale.Px(26) - shortcutWidth, Height - 1);
                if (shortcutWidth > 0)
                {
                    int badgeHeight = UiScale.Px(18);
                    var badge = new Rectangle(left + UiScale.Px(25) + textWidth, (Height - badgeHeight) / 2, shortcutWidth - UiScale.Px(3), badgeHeight);
                    Theme.StrokeRounded(g, Theme.Blend(face, ink, .35f), badge, UiScale.Px(4));
                    Theme.Label(g, ShortcutCaption, Theme.CompactMono, ink, badge, TextFormatFlags.HorizontalCenter);
                }
            }
            Theme.Label(g, Text, captionFont, ink, text,
                string.IsNullOrEmpty(Glyph) ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
        }

        private void DrawDropDownArrow(Graphics graphics, Color ink)
        {
            float x = Width - UiScale.Px(16), y = Height / 2f;
            float halfWidth = UiScale.Px(3), halfHeight = UiScale.Px(2);
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(ink))
                graphics.FillPolygon(brush, new[] { new PointF(x - halfWidth, y - halfHeight),
                    new PointF(x + halfWidth, y - halfHeight), new PointF(x, y + halfHeight) });
            graphics.Restore(state);
        }

        private static void DrawFilledLightning(Graphics graphics, Color ink, Rectangle box)
        {
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(box.Left + box.Width / 2f, box.Top + box.Height / 2f);
            graphics.ScaleTransform(UiScale.Factor, UiScale.Factor);
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(ink))
            {
                path.AddPolygon(new[] { new PointF(-1, -9), new PointF(6, -9),
                    new PointF(1, -2), new PointF(7, -2), new PointF(-6, 9),
                    new PointF(-2, 1), new PointF(-7, 1) });
                graphics.FillPath(brush, path);
            }
            graphics.Restore(state);
        }

        private void DrawRangeIcon(Graphics graphics, Color ink, bool start)
        {
            GraphicsState state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform((Width - 1) / 2f, (Height - 1) / 2f);
            graphics.ScaleTransform((start ? 1 : -1) * UiScale.Factor, UiScale.Factor);
            using (var pen = new Pen(ink, 1.55f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                graphics.DrawLines(pen, new[] { new Point(-2, -6), new Point(-7, -6), new Point(-7, 6), new Point(-2, 6) });
                graphics.DrawLine(pen, -2, 0, 7, 0);
                graphics.DrawLines(pen, new[] { new Point(4, -3), new Point(7, 0), new Point(4, 3) });
            }
            graphics.Restore(state);
        }
    }

    internal sealed class CenteredToolbarStrip : FlowLayoutPanel
    {
        private bool arranging;
        internal CenteredToolbarStrip()
        {
            Dock = DockStyle.Fill; Margin = Padding.Empty; WrapContents = false;
            BackColor = Theme.Background;
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (arranging) { base.OnLayout(e); return; }
            arranging = true;
            try
            {
                int tallest = 0;
                foreach (Control control in Controls)
                {
                    var margin = control.Margin;
                    if (margin.Top != 0 || margin.Bottom != 0)
                        control.Margin = new Padding(margin.Left, 0, margin.Right, 0);
                    tallest = Math.Max(tallest, control.Height);
                }
                int top = Math.Max(0, (ClientSize.Height - tallest) / 2);
                Padding = new Padding(Padding.Left, top, Padding.Right, 0);
                base.OnLayout(e);
            }
            finally { arranging = false; }
        }
    }

    internal sealed class MaterialSurface : Panel
    {
        private readonly int radius;
        private readonly bool outlined;
        private Control clippedContent;
        private Size clippedSize;
        internal MaterialSurface(int radius, Color color, bool outlined = false)
        {
            this.radius = UiScale.Px(radius);
            this.outlined = outlined;
            BackColor = color;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            if (outlined) Padding = new Padding(1);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
            Theme.FillRounded(e.Graphics, outlined ? Theme.Line : BackColor, ClientRectangle, radius);
            if (outlined) Theme.FillRounded(e.Graphics, BackColor, DisplayRectangle, radius - Padding.Left);
        }

        internal void SmoothSnapshotCorners(Graphics graphics, Bitmap original, Point location)
        {
            if (Width <= 0 || Height <= 0) return;
            int corner = Math.Min(radius + 2, Math.Min(Width, Height));
            var bounds = new Rectangle(location, Size);
            var state = graphics.Save();
            using (var corners = new Region())
            {
                corners.MakeEmpty();
                corners.Union(new Rectangle(bounds.Left, bounds.Top, corner, corner));
                corners.Union(new Rectangle(bounds.Right - corner, bounds.Top, corner, corner));
                corners.Union(new Rectangle(bounds.Left, bounds.Bottom - corner, corner, corner));
                corners.Union(new Rectangle(bounds.Right - corner, bounds.Bottom - corner, corner, corner));
                graphics.SetClip(corners, CombineMode.Intersect);
                Theme.Fill(graphics, Parent == null ? Theme.Background : Parent.BackColor, bounds);
                Theme.FillRounded(graphics, outlined ? Theme.Line : BackColor, bounds, radius);
                var content = DisplayRectangle;
                content.Offset(location);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pixels = new TextureBrush(original, WrapMode.TileFlipXY))
                using (var path = Theme.Rounded(content, radius - Padding.Left))
                    graphics.FillPath(pixels, path);
            }
            graphics.Restore(state);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Controls.Count == 0) return;
            Control content = Controls[0];
            if (clippedContent == content && clippedSize == content.ClientSize) return;
            clippedContent = content;
            clippedSize = content.ClientSize;
            Region previous = content.Region;
            using (GraphicsPath path = Theme.Rounded(content.ClientRectangle, radius - Padding.Left))
                content.Region = new Region(path);
            if (previous != null) previous.Dispose();
        }
    }

    internal sealed class MaterialNumber : NumericUpDown
    {
        private readonly bool integratedArrows;

        internal MaterialNumber(bool integratedArrows = false)
        {
            this.integratedArrows = integratedArrows;
            foreach (Control control in Controls)
                if (control.GetType().Name == "UpDownButtons")
                    control.Paint += PaintArrows;
        }

        private void PaintArrows(object sender, PaintEventArgs e)
        {
            var control = (Control)sender;
            int x = control.Width / 2, half = control.Height / 2;
            Theme.Fill(e.Graphics, integratedArrows ? BackColor : Theme.High, control.ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (integratedArrows)
            {
                using (var pen = new Pen(Enabled ? Theme.Muted : Theme.Line, UiScale.Factor)
                    { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float span = UiScale.Px(2);
                    float top = half / 2f, bottom = (half + control.Height) / 2f;
                    e.Graphics.DrawLines(pen, new[] { new PointF(x - span, top + 1), new PointF(x, top - 1), new PointF(x + span, top + 1) });
                    e.Graphics.DrawLines(pen, new[] { new PointF(x - span, bottom - 1), new PointF(x, bottom + 1), new PointF(x + span, bottom - 1) });
                }
                return;
            }
            using (var brush = new SolidBrush(Enabled ? Theme.Text : Theme.Muted))
            {
                int upper = half / 2, lower = half + half / 2;
                e.Graphics.FillPolygon(brush, new[] { new Point(x - 3, upper + 2), new Point(x + 3, upper + 2), new Point(x, upper - 2) });
                e.Graphics.FillPolygon(brush, new[] { new Point(x - 3, lower - 2), new Point(x + 3, lower - 2), new Point(x, lower + 2) });
            }
        }
    }

    internal sealed class MaterialNumberHost : Panel
    {
        private readonly NumericUpDown number;
        private readonly Button unit;

        internal MaterialNumberHost(NumericUpDown number, int width, Button unit = null)
        {
            this.number = number;
            this.unit = unit;
            Size = new Size(width, 30);
            Margin = new Padding(0, 1, 6, 0);
            BackColor = Theme.Raised;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Controls.Add(number);
            if (unit != null) { unit.Margin = Padding.Empty; unit.BackColor = BackColor; Controls.Add(unit); }
            number.Enter += delegate { Invalidate(); };
            number.Leave += delegate { Invalidate(); };
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (number == null) return;
            int left = UiScale.Px(5), right = UiScale.Px(4);
            if (unit != null) {
                int unitWidth = UiScale.Px(49);
                unit.SetBounds(Width - unitWidth - right, UiScale.Px(2), unitWidth, Height - UiScale.Px(4));
                right += unitWidth;
            }
            number.SetBounds(left, (Height - number.Height) / 2,
                Math.Max(1, Width - left - right), number.Height);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Panel : Parent.BackColor);
            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            float radius = UiScale.Px(10);
            Theme.FillRounded(e.Graphics, BackColor, box, radius);
            Theme.StrokeRounded(e.Graphics, number.ContainsFocus ? Theme.Primary : Theme.Line, box, radius);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) number.Focus();
            base.OnMouseDown(e);
        }
    }

    internal sealed class CacheMeter : Control
    {
        private float fill;
        internal CacheMeter() { DoubleBuffered = true; Margin = Padding.Empty; }
        internal void SetFill(float value)
        {
            if (Math.Abs(fill - value) < .002f) return;
            fill = value; Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var track = new Rectangle(0, (Height - 4) / 2, Width, 4);
            Theme.FillRounded(e.Graphics, Theme.Raised, track, 2);
            track.Width = (int)Math.Round(track.Width * fill);
            if (track.Width > 0) Theme.FillRounded(e.Graphics, Theme.Primary, track, 2);
        }
    }

    internal static class MainLayout
    {
        internal const int Header = 68, Transport = 52;
        internal const int Timeline = 130, Filmstrip = 276;
        internal const int MonitorHeader = 45, Status = 32, FocusTransport = 40, FocusFilmstrip = 60;
    }
}
