using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FrameDeck
{
    internal static class CacheMemory
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            internal uint Length, Load;
            internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        internal static readonly int TotalMegabytes = ReadTotal();
        private static int ReadTotal()
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            return GlobalMemoryStatusEx(ref status) ? (int)Math.Min(int.MaxValue, status.TotalPhysical / (1024UL * 1024)) : 8192;
        }
        internal static int Megabytes(int percent)
        {
            return (int)Math.Max(ViewerDefaults.MinimumCacheMegabytes,
                Math.Min(ViewerDefaults.MaximumCacheMegabytes, (long)TotalMegabytes * percent / 100));
        }
        internal static int Percent(int megabytes)
        {
            return Math.Max(1, Math.Min(100, (int)Math.Round(megabytes * 100.0 / TotalMegabytes)));
        }
        internal static string Capacity(int megabytes)
        {
            return megabytes >= 1024 ? (megabytes / 1024.0).ToString("0.##") + " GB" : megabytes + " MB";
        }
        internal static string Label(int megabytes) { return (megabytes * 100.0 / TotalMegabytes).ToString("0.##") + "% · " + Capacity(megabytes); }
    }

    internal sealed class CachePercentSlider : Control
    {
        private int value = 1;
        private bool dragging;
        internal event EventHandler ValueChanged;
        internal int Value
        {
            get { return value; }
            set {
                int next = Math.Max(1, Math.Min(100, value));
                if (this.value == next) return;
                this.value = next; Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        internal CachePercentSlider() { DoubleBuffered = true; ResizeRedraw = true; TabStop = true; Cursor = Cursors.Hand; AccessibleName = "缓存占总内存百分比"; }
        private void SetFromMouse(int x)
        {
            int inset = UiScale.Px(10);
            Value = 1 + (int)Math.Round(99.0 * (x - inset) / Math.Max(1, Width - inset * 2));
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus(); Capture = dragging = true; SetFromMouse(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFromMouse(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) { dragging = false; Capture = false; } }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Left) Value--; else if (e.KeyCode == Keys.Right) Value++; else if (e.KeyCode == Keys.Home) Value = 1; else if (e.KeyCode == Keys.End) Value = 100; else return; e.Handled = true; }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            int inset = UiScale.Px(10), y = Height / 2, thickness = UiScale.Px(5);
            int x = inset + (int)Math.Round((Width - inset * 2) * (Value - 1) / 99.0);
            Theme.FillRounded(e.Graphics, Theme.Line, new Rectangle(inset, y - thickness / 2, Width - inset * 2, thickness), thickness / 2f);
            if (x > inset) Theme.FillRounded(e.Graphics, Theme.Primary, new Rectangle(inset, y - thickness / 2, x - inset, thickness), thickness / 2f);
            int radius = UiScale.Px(8);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Theme.Primary)) e.Graphics.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
            if (Focused) Theme.StrokeRounded(e.Graphics, Theme.Primary, new Rectangle(0, 0, Width - 1, Height - 1), UiScale.Px(8));
        }
    }

    internal sealed partial class MainForm
    {
        private bool AskCacheLimit(int initial, out int megabytes)
        {
            megabytes = initial;
            using (var dialog = new Form { Text = "自定义缓存上限", Font = Theme.Small, BackColor = Theme.Panel, ForeColor = Theme.Text,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false, AutoScaleMode = AutoScaleMode.None })
            {
                dialog.HandleCreated += delegate { ApplyCaptionTheme(dialog.Handle); };
                SettingsLabel(dialog, "按本机总内存分配缓存", 20, 12, 380, Theme.Text, true);
                SettingsLabel(dialog, "本机总内存 " + CacheMemory.Capacity(CacheMemory.TotalMegabytes), 20, 43, 380, Theme.Muted);
                var percent = NumberOf(1, 100, CacheMemory.Percent(initial), 74, integratedArrows: true);
                percent.AccessibleName = "缓存百分比";
                dialog.Controls.Add(new MaterialNumberHost(percent, 86) { Location = new Point(20, 84) });
                SettingsLabel(dialog, "%", 116, 84, 28, Theme.Text);
                var capacity = SettingsLabel(dialog, "", 172, 84, 228, Theme.OnContainer, true);
                var slider = new CachePercentSlider { Location = new Point(20, 124), Size = new Size(380, 34), BackColor = Theme.Panel, Value = (int)percent.Value };
                dialog.Controls.Add(slider);
                SettingsLabel(dialog, "1%", 24, 158, 80, Theme.Muted);
                var high = SettingsLabel(dialog, "100%", 324, 158, 76, Theme.Muted); high.TextAlign = ContentAlignment.MiddleRight;
                Action sync = delegate { int size = CacheMemory.Megabytes((int)percent.Value); capacity.Text = CacheMemory.Capacity(size) + "（" + size + " MB）"; slider.Value = (int)percent.Value; };
                percent.ValueChanged += delegate { sync(); };
                slider.ValueChanged += delegate { percent.Value = slider.Value; };
                sync();
                foreach (int preset in new[] { 10, 20, 30 }) {
                    int selected = preset;
                    AddSettingsButton(dialog, preset + "%", 20 + (preset / 10 - 1) * 74, 198, 66, delegate { percent.Value = selected; });
                }
                var cancel = AddSettingsButton(dialog, "取消", 222, 250, 78, delegate { }); cancel.DialogResult = DialogResult.Cancel;
                var apply = AddSettingsButton(dialog, "应用", 310, 250, 90, delegate { }, true); apply.DialogResult = DialogResult.OK;
                dialog.AcceptButton = apply; dialog.CancelButton = cancel;
                dialog.Scale(new SizeF(UiScale.Factor, UiScale.Factor)); dialog.ClientSize = UiScale.Box(0, 0, 420, 300).Size;
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                megabytes = CacheMemory.Megabytes((int)percent.Value);
                return true;
            }
        }
    }
}
