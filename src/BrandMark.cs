using System.Drawing;
using System.Drawing.Drawing2D;

namespace FrameDeck
{
    // Vector master for the approved plain FV monogram, without a play symbol.
    internal static class BrandMark
    {
        internal static void Draw(Graphics graphics, RectangleF bounds)
        {
            var state = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 64f, bounds.Height / 64f);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var tile = RoundedTile(2, 2, 60, 60, 14))
            using (var color = new LinearGradientBrush(new RectangleF(2, 2, 60, 60),
                Color.FromArgb(99, 119, 197), Color.FromArgb(88, 109, 184), 60))
                graphics.FillPath(color, tile);
            using (var f = new GraphicsPath())
            using (var v = new GraphicsPath())
            using (var ink = new SolidBrush(Color.White))
            {
                f.StartFigure();
                f.AddLine(9.4f, 44.5f, 13.7f, 23.5f);
                f.AddBezier(13.7f, 23.5f, 14.4f, 20.6f, 15.7f, 19.3f, 18f, 19.3f);
                f.AddLine(18f, 19.3f, 30.6f, 19.3f);
                f.AddBezier(30.6f, 19.3f, 31.3f, 19.3f, 31.6f, 19.6f, 31.5f, 20.4f);
                f.AddLine(31.5f, 20.4f, 31f, 24.4f);
                f.AddBezier(31f, 24.4f, 30.9f, 25f, 30.6f, 25.3f, 30f, 25.3f);
                f.AddLine(30f, 25.3f, 20.2f, 25.3f);
                f.AddBezier(20.2f, 25.3f, 19.6f, 25.3f, 19.4f, 25.5f, 19.3f, 26.1f);
                f.AddLine(19.3f, 26.1f, 18.7f, 28.9f);
                f.AddLine(18.7f, 28.9f, 28.8f, 28.9f);
                f.AddBezier(28.8f, 28.9f, 29.5f, 28.9f, 29.8f, 29.2f, 29.6f, 29.8f);
                f.AddLine(29.6f, 29.8f, 28.8f, 33.8f);
                f.AddBezier(28.8f, 33.8f, 28.7f, 34.5f, 28.5f, 34.8f, 27.9f, 34.8f);
                f.AddLine(27.9f, 34.8f, 17.9f, 34.8f);
                f.AddLine(17.9f, 34.8f, 15.8f, 44.6f);
                f.AddBezier(15.8f, 44.6f, 15.7f, 45.1f, 15.5f, 45.3f, 15f, 45.3f);
                f.AddLine(15f, 45.3f, 10f, 45.3f);
                f.AddBezier(10f, 45.3f, 9.5f, 45.3f, 9.2f, 45.1f, 9.4f, 44.5f);
                f.CloseFigure();
                v.StartFigure();
                v.AddBezier(33.5f, 19.3f, 32.8f, 19.3f, 32.6f, 19.6f, 32.8f, 20.3f);
                v.AddLine(32.8f, 20.3f, 37.5f, 44.3f);
                v.AddBezier(37.5f, 44.3f, 37.7f, 45f, 38f, 45.2f, 38.5f, 45.2f);
                v.AddLine(38.5f, 45.2f, 43.3f, 45.2f);
                v.AddBezier(43.3f, 45.2f, 43.9f, 45.2f, 44.1f, 45f, 44.4f, 44.5f);
                v.AddLine(44.4f, 44.5f, 54.7f, 19.9f);
                v.AddBezier(54.7f, 19.9f, 55f, 19.4f, 54.8f, 19.2f, 54.3f, 19.2f);
                v.AddLine(54.3f, 19.2f, 48f, 19.2f);
                v.AddBezier(48f, 19.2f, 47.4f, 19.2f, 47.1f, 19.4f, 46.9f, 20f);
                v.AddLine(46.9f, 20f, 41.6f, 34.8f);
                v.AddLine(41.6f, 34.8f, 37.8f, 20.1f);
                v.AddBezier(37.8f, 20.1f, 37.6f, 19.5f, 37.4f, 19.3f, 37f, 19.3f);
                v.AddLine(37f, 19.3f, 33.5f, 19.3f);
                v.CloseFigure();
                graphics.FillPath(ink, f); graphics.FillPath(ink, v);
            }
            graphics.Restore(state);
        }
        private static GraphicsPath RoundedTile(float x, float y, float width, float height, float radius)
        {
            float diameter = radius * 2;
            var tile = new GraphicsPath();
            tile.AddArc(x, y, diameter, diameter, 180, 90);
            tile.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            tile.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            tile.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            tile.CloseFigure(); return tile;
        }
    }
}
