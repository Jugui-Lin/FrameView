using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed class MaterialMenuRenderer : ToolStripProfessionalRenderer
    {
        internal static readonly MaterialMenuRenderer Instance = new MaterialMenuRenderer();
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        private MaterialMenuRenderer() { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Theme.Raised);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Theme.LineAt(e.Graphics, Theme.Line, UiScale.Px(11), e.Item.Height / 2, e.Item.Width - UiScale.Px(12), e.Item.Height / 2);
        }

        internal static void Style(ToolStripDropDown popup)
        {
            popup.Renderer = Instance;
            popup.BackColor = Theme.Raised;
            popup.ForeColor = Theme.Text;
            popup.Font = Theme.Small;
            popup.Padding = new Padding(UiScale.Px(7));
            popup.DropShadowEnabled = true;
            popup.Opened += delegate
            {
                const int cornerPreference = 33;
                int round = 2;
                DwmSetWindowAttribute(popup.Handle, cornerPreference, ref round, sizeof(int));
            };
        }

        internal static void FadeIn(ToolStripDropDown popup)
        {
            if (!Motion.Enabled) return;
            popup.AllowTransparency = true;
            popup.Opacity = .80;
            var clock = new Stopwatch();
            var timer = new Timer { Interval = 15 };
            timer.Tick += delegate
            {
                float mix = Motion.Ease(clock.Elapsed.TotalMilliseconds / 115);
                popup.Opacity = .80 + .20 * mix;
                if (mix >= 1) timer.Stop();
            };
            popup.Opened += delegate { clock.Restart(); timer.Start(); };
            popup.Disposed += delegate { timer.Dispose(); };
            popup.Closed += delegate { timer.Stop(); };
        }
    }

    internal sealed class MaterialMenuItem : ToolStripMenuItem
    {
        private static readonly int Leading = UiScale.Px(31), Trailing = UiScale.Px(12), ColumnGap = UiScale.Px(20);
        private const TextFormatFlags MeasureFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        private readonly string hint;
        private readonly int hintWidth;

        internal MaterialMenuItem(string label, string shortcut, int width, bool current, bool enabled) : base(label)
        {
            hint = shortcut ?? "";
            hintWidth = MeasureText(hint, Theme.Caption);
            ShowShortcutKeys = false;
            AutoSize = false;
            Size = new Size(width, UiScale.Px(34));
            Margin = Padding.Empty;
            Checked = current;
            Enabled = enabled;
            AccessibleName = Text + (hint.Length == 0 ? "" : "，" + hint) + (current ? "，当前选中" : "");
        }
        private static int MeasureText(string text, Font font)
        {
            return string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, font, Size.Empty, MeasureFlags).Width;
        }
        internal static int Measure(string label, string shortcut)
        {
            int shortcutWidth = MeasureText(shortcut, Theme.Caption);
            return Leading + MeasureText(label, Theme.Small) + Trailing + 3 + (shortcutWidth == 0 ? 0 : ColumnGap + shortcutWidth);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var row = new Rectangle(1, 1, Width - 2, Height - 2);
            Color foreground = !Enabled ? Theme.Blend(Theme.Raised, Theme.Text, .45f) : Checked ? Theme.OnContainer : Theme.Text;
            if (Checked || Selected)
                Theme.FillRounded(g, Checked ? (Selected ? Theme.Blend(Theme.Container, Theme.Text, .08f) : Theme.Container) :
                    Theme.High, row, UiScale.Px(11));
            if (Checked)
            {
                using (var pen = new Pen(foreground, 1.7f * UiScale.Factor))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    int y = Height / 2;
                    g.DrawLines(pen, new[] { new Point(UiScale.Px(11), y), new Point(UiScale.Px(15), y + UiScale.Px(4)),
                        new Point(UiScale.Px(22), y - UiScale.Px(4)) });
                }
            }
            int endOfLabel = Width - Trailing - (hintWidth == 0 ? 0 : ColumnGap + hintWidth);
            Theme.Label(g, Text, Theme.Small, foreground, new Rectangle(Leading, 0, Math.Max(1, endOfLabel - Leading), Height));
            if (hintWidth > 0)
                Theme.Label(g, hint, Theme.Caption, Enabled ? Theme.Muted : foreground,
                    new Rectangle(Width - hintWidth - Trailing, 0, hintWidth, Height), TextFormatFlags.Right);
        }
    }

    internal sealed class MaterialMenuHeading : ToolStripLabel
    {
        internal MaterialMenuHeading(string text, int width) : base(text)
        {
            AutoSize = false; Size = new Size(width, UiScale.Px(26)); Margin = Padding.Empty;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Label(e.Graphics, Text, Theme.Caption, Theme.Muted, new Rectangle(UiScale.Px(13), 1, Width - UiScale.Px(26), Height - 2));
        }
    }

    internal sealed class FpsPresetGrid : Panel
    {
        private const int Columns = 4;
        private readonly MaterialButton[] items;
        private readonly Action<decimal> apply;
        private int current;

        internal FpsPresetGrid(decimal[] values, decimal selected, Action<decimal> apply)
        {
            BackColor = Theme.Raised;
            Size = UiScale.Box(0, 0, 305, 275).Size;
            Margin = Padding.Empty;
            this.apply = apply;
            items = new MaterialButton[values.Length + 1];
            current = Array.IndexOf(values, selected);
            if (current < 0) current = Array.IndexOf(values, 24m);
            var title = new Label { Text = "时间轴帧率", Font = Theme.Strong, ForeColor = Theme.Text,
                Bounds = UiScale.Box(8, 4, 139, 29), TextAlign = ContentAlignment.MiddleLeft };
            var value = new Label { Text = selected.ToString("0.###", CultureInfo.CurrentCulture) + " FPS",
                Font = Theme.Mono, ForeColor = Theme.Primary, Bounds = UiScale.Box(157, 4, 140, 29),
                TextAlign = ContentAlignment.MiddleRight };
            Controls.Add(title); Controls.Add(value);

            for (int i = 0; i < values.Length; i++)
            {
                decimal rate = values[i];
                var chip = Chip(rate.ToString("0.###", CultureInfo.CurrentCulture), rate, selected == rate,
                    UiScale.Box(6 + i % Columns * 74, 40 + i / Columns * 37, 69, 32));
                items[i] = chip;
                Controls.Add(chip);
            }
            var reset = Chip("恢复默认 · 24 FPS", 24, false, UiScale.Box(6, 232, 291, 33));
            items[values.Length] = reset;
            Controls.Add(reset);
        }

        private MaterialButton Chip(string text, decimal rate, bool selected, Rectangle bounds)
        {
            var button = new MaterialButton { Text = text, Bounds = bounds, Margin = Padding.Empty, Font = Theme.Small,
                BackColor = selected ? Theme.Primary : Theme.High, ForeColor = selected ? Theme.OnPrimary : Theme.Text,
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, TabStop = true,
                AccessibleName = text + (selected ? " FPS，当前选中" : "") };
            button.FlatAppearance.BorderSize = 0;
            button.GotFocus += delegate { current = Array.IndexOf(items, button); };
            button.Click += delegate { apply(rate); };
            return button;
        }

        internal void FocusCurrent()
        {
            if (current >= 0 && current < items.Length) items[current].Focus();
        }

        internal bool Navigate(Keys key)
        {
            int next;
            switch (key)
            {
                case Keys.Left: next = current - 1; break;
                case Keys.Right: next = current + 1; break;
                case Keys.Up: next = current - Columns; break;
                case Keys.Down: next = current + Columns; break;
                case Keys.Home: next = 0; break;
                case Keys.End: next = items.Length - 1; break;
                case Keys.Enter: case Keys.Space:
                    if (current >= 0) items[current].PerformClick();
                    return true;
                default: return false;
            }
            current = Math.Max(0, Math.Min(items.Length - 1, next));
            FocusCurrent();
            return true;
        }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            return Navigate(keyData) || base.ProcessDialogKey(keyData);
        }
    }

    internal sealed class FpsDropDown : ToolStripDropDown
    {
        internal FpsPresetGrid Grid { get; private set; }

        internal FpsDropDown(decimal[] rates, decimal value, Action<decimal> apply)
        {
            Grid = new FpsPresetGrid(rates, value, rate =>
            {
                Close(ToolStripDropDownCloseReason.ItemClicked);
                apply(rate);
            });
            Items.Add(new ToolStripControlHost(Grid)
            {
                AutoSize = false, Size = Grid.Size, Padding = Padding.Empty, Margin = Padding.Empty
            });
            Opened += delegate
            {
                BeginInvoke((Action)delegate { if (!IsDisposed && Visible) Grid.FocusCurrent(); });
            };
        }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            return Grid.Navigate(keyData) || base.ProcessDialogKey(keyData);
        }
    }
}
