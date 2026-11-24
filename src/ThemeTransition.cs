using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace FrameDeck
{
    internal sealed class ThemeTransition : Control
    {
        private Bitmap before, after, softBefore, softAfter;
        private Bitmap compositeFrame;
        private int[] beforePixels, afterPixels, beforeSoftPixels, afterSoftPixels, framePixels;
        private readonly Point origin;
        private readonly double radius;
        private readonly Timer timer;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Action completed;
        private const double Duration = 650;
        internal Action<double> ProgressChanged { get; set; }

        internal ThemeTransition(Bitmap before, Point origin, Action completed)
        {
            this.before = before; this.origin = origin; this.completed = completed;
            radius = Math.Sqrt(Math.Pow(Math.Max(origin.X, before.Width - origin.X), 2) +
                Math.Pow(Math.Max(origin.Y, before.Height - origin.Y), 2)) + 2;
            Dock = DockStyle.Fill; Margin = Padding.Empty; TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            timer = new Timer { Interval = 15 };
            timer.Tick += delegate
            {
                if (ProgressChanged != null) ProgressChanged(Math.Min(1, clock.Elapsed.TotalMilliseconds / Duration));
                if (clock.Elapsed.TotalMilliseconds >= Duration) { timer.Stop(); completed(); }
                else Invalidate();
            };
        }

        // Let the existing controls receive clicks, including another theme click.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
            base.WndProc(ref m);
        }

        internal void Start(Bitmap after)
        {
            this.after = after;
            softBefore = Soften(before); softAfter = Soften(after);
            beforePixels = ReadPixels(before); afterPixels = ReadPixels(after);
            beforeSoftPixels = ExpandPixels(softBefore, before.Size);
            afterSoftPixels = ExpandPixels(softAfter, after.Size);
            framePixels = new int[before.Width * before.Height];
            compositeFrame = new Bitmap(before.Width, before.Height, PixelFormat.Format32bppPArgb);
            if (ProgressChanged != null) ProgressChanged(0);
            clock.Restart(); timer.Start(); Invalidate();
        }

        private static Bitmap Soften(Bitmap source)
        {
            var soft = new Bitmap(Math.Max(1, source.Width / 6), Math.Max(1, source.Height / 6), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(soft))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(Point.Empty, soft.Size));
            }
            return soft;
        }

        internal Bitmap CaptureCurrent()
        {
            var frame = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(frame)) Render(g);
            return frame;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { Render(e.Graphics); }

        private static int[] ReadPixels(Bitmap bitmap)
        {
            var pixels = new int[bitmap.Width * bitmap.Height];
            var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
            finally { bitmap.UnlockBits(data); }
            return pixels;
        }

        private static int[] ExpandPixels(Bitmap small, Size size)
        {
            using (var expanded = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(expanded))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(small, new Rectangle(Point.Empty, size));
                }
                return ReadPixels(expanded);
            }
        }

        private static int Blend(int first, int second, int amount)
        {
            uint a = (uint)first, b = (uint)second, weight = (uint)amount, inverse = 256 - weight;
            uint rb = (((a & 0x00FF00FF) * inverse + (b & 0x00FF00FF) * weight) >> 8) & 0x00FF00FF;
            uint green = (((a & 0x0000FF00) * inverse + (b & 0x0000FF00) * weight) >> 8) & 0x0000FF00;
            return unchecked((int)(0xFF000000 | rb | green));
        }

        private static void BlendSpan(int[] output, int[] sharp, int[] soft, int start, int end, int amount)
        {
            if (end <= start) return;
            if (amount == 0) { Array.Copy(sharp, start, output, start, end - start); return; }
            if (amount == 256) { Array.Copy(soft, start, output, start, end - start); return; }
            for (int i = start; i < end; i++) output[i] = Blend(sharp[i], soft[i], amount);
        }

        private void Render(Graphics g, double? frameProgress = null)
        {
            if (before == null) return;
            if (compositeFrame == null) { g.DrawImageUnscaled(before, Point.Empty); return; }
            double progress = Math.Max(0, Math.Min(1, frameProgress ?? clock.Elapsed.TotalMilliseconds / Duration));
            double eased = progress * progress * (3 - 2 * progress);
            int blur = (int)Math.Round(Math.Pow(Math.Sin(Math.PI * progress), 1.4) * 256);
            double r = radius * eased;
            double feather = Math.Max(1, UiScale.Factor * .8);
            double outerSquared = (r + feather) * (r + feather);
            double innerSquared = Math.Max(0, r - feather) * Math.Max(0, r - feather);
            int width = before.Width, height = before.Height;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                double dy = y + .5 - origin.Y;
                double horizontal = outerSquared - dy * dy;
                if (horizontal <= 0 || progress <= 0)
                {
                    BlendSpan(framePixels, beforePixels, beforeSoftPixels, row, row + width, blur);
                    continue;
                }
                double extent = Math.Sqrt(horizontal);
                int left = Math.Max(0, Math.Min(width, (int)Math.Floor(origin.X - extent - .5)));
                int right = Math.Max(left, Math.Min(width, (int)Math.Ceiling(origin.X + extent + .5)));
                double innerHorizontal = innerSquared - dy * dy;
                double innerExtent = innerHorizontal > 0 ? Math.Sqrt(innerHorizontal) : 0;
                int innerLeft = Math.Max(left, Math.Min(right, (int)Math.Ceiling(origin.X - innerExtent - .5)));
                int innerRight = Math.Max(innerLeft, Math.Min(right, (int)Math.Floor(origin.X + innerExtent - .5) + 1));
                if (innerHorizontal <= 0) innerRight = innerLeft;
                BlendSpan(framePixels, beforePixels, beforeSoftPixels, row, row + left, blur);
                BlendSpan(framePixels, afterPixels, afterSoftPixels, row + innerLeft, row + innerRight, blur);
                BlendSpan(framePixels, beforePixels, beforeSoftPixels, row + right, row + width, blur);
                for (int edge = 0; edge < 2; edge++)
                {
                    int start = edge == 0 ? left : innerRight;
                    int end = edge == 0 ? innerLeft : right;
                    for (int x = start; x < end; x++)
                    {
                        double dx = x + .5 - origin.X;
                        double coverage = Math.Max(0, Math.Min(1, (r + feather - Math.Sqrt(dx * dx + dy * dy)) / (2 * feather)));
                        coverage = coverage * coverage * (3 - 2 * coverage);
                        int oldPixel = Blend(beforePixels[row + x], beforeSoftPixels[row + x], blur);
                        int newPixel = Blend(afterPixels[row + x], afterSoftPixels[row + x], blur);
                        framePixels[row + x] = Blend(oldPixel, newPixel, (int)Math.Round(coverage * 256));
                    }
                }
            }
            var data = compositeFrame.LockBits(new Rectangle(Point.Empty, compositeFrame.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(framePixels, 0, data.Scan0, framePixels.Length); }
            finally { compositeFrame.UnlockBits(data); }
            g.DrawImageUnscaled(compositeFrame, Point.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop(); timer.Dispose();
                if (before != null) before.Dispose();
                if (after != null) after.Dispose();
                if (softBefore != null) softBefore.Dispose();
                if (softAfter != null) softAfter.Dispose();
                if (compositeFrame != null) compositeFrame.Dispose();
                before = after = softBefore = softAfter = compositeFrame = null;
                beforePixels = afterPixels = beforeSoftPixels = afterSoftPixels = framePixels = null;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed partial class MainForm
    {
        private ThemeTransition themeTransition;

        private Bitmap CaptureThemeFrame(bool composite = true)
        {
            if (composite && themeTransition != null) return themeTransition.CaptureCurrent();
            var root = topSection.Parent;
            var frame = new Bitmap(root.Width, root.Height, PixelFormat.Format32bppPArgb);
            try
            {
                root.DrawToBitmap(frame, new Rectangle(Point.Empty, frame.Size));
                // Native child-window regions are binary masks. Reconstruct their
                // rounded corners with coverage before the blur/animation samples them.
                using (var original = (Bitmap)frame.Clone())
                using (var graphics = Graphics.FromImage(frame))
                    SmoothThemeSnapshot(root, graphics, original, Point.Empty);
                return frame;
            }
            catch { frame.Dispose(); throw; }
        }

        private static void SmoothThemeSnapshot(Control parent, Graphics graphics, Bitmap original, Point origin)
        {
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible) continue;
                var location = new Point(origin.X + child.Left, origin.Y + child.Top);
                var surface = child as MaterialSurface;
                if (surface != null) surface.SmoothSnapshotCorners(graphics, original, location);
                SmoothThemeSnapshot(child, graphics, original, location);
            }
        }

        private void EndThemeTransition(bool finishCaption = true)
        {
            if (themeTransition == null) return;
            var overlay = themeTransition; themeTransition = null;
            Controls.Remove(overlay); overlay.Dispose();
            if (finishCaption && IsHandleCreated && !IsDisposed) ApplyCaptionTheme();
            if (!IsDisposed) Invalidate(true);
        }
    }
}
