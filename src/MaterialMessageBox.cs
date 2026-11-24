using System;
using System.Drawing;
using System.Windows.Forms;

namespace FrameDeck
{
    internal static class MaterialMessageBox
    {
        private sealed class NoticeIcon : Control
        {
            private readonly string glyph;
            internal NoticeIcon(MessageBoxIcon icon)
            {
                DoubleBuffered = true; BackColor = Theme.Panel;
                glyph = icon == MessageBoxIcon.Question ? "\uE897" :
                    icon == MessageBoxIcon.Warning ? "\uE7BA" : icon == MessageBoxIcon.Error ? "\uEA39" : "\uE946";
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                Theme.FillRounded(e.Graphics, Theme.Container, new Rectangle(0, 0, Width - 1, Height - 1), UiScale.Px(12));
                Theme.DrawIcon(e.Graphics, glyph, Theme.ToolbarIcons, Theme.OnContainer, ClientRectangle);
            }
        }

        internal static DialogResult Show(IWin32Window owner, string message, string caption,
            MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None,
            string confirmLabel = null)
        {
            using (var dialog = new Form {
                Text = caption, Font = Theme.Small, BackColor = Theme.Panel, ForeColor = Theme.Text,
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false, AutoScaleMode = AutoScaleMode.None,
                Icon = BrandInfo.WindowIcon })
            using (var headingFont = new Font("Microsoft YaHei UI", 12, FontStyle.Bold))
            {
                dialog.HandleCreated += delegate { MainForm.ApplyCaptionTheme(dialog.Handle); };
                int width = UiScale.Px(480), inset = UiScale.Px(24), textLeft = UiScale.Px(82);
                int contentWidth = width - textLeft - inset;
                const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
                int measuredHeight = TextRenderer.MeasureText(message, Theme.Small, new Size(contentWidth, int.MaxValue), flags).Height + UiScale.Px(5);
                int bodyHeight = Math.Max(UiScale.Px(46), measuredHeight);
                int maxBodyHeight = Math.Max(UiScale.Px(80), Screen.FromHandle(owner.Handle).WorkingArea.Height - UiScale.Px(230));
                bodyHeight = Math.Min(bodyHeight, maxBodyHeight);
                var heading = new Label { Text = caption, Font = headingFont, ForeColor = Theme.Text,
                    Bounds = new Rectangle(textLeft, inset, contentWidth, UiScale.Px(30)), AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft };
                dialog.Controls.Add(heading);
                dialog.Controls.Add(new NoticeIcon(icon) { Bounds = UiScale.Box(24, 24, 40, 40) });
                var body = new Panel { Bounds = new Rectangle(textLeft, UiScale.Px(62), contentWidth, bodyHeight),
                    BackColor = Theme.Panel, AutoScroll = measuredHeight > bodyHeight };
                var text = new Label { Text = message, Font = Theme.Small, ForeColor = Theme.Muted,
                    Location = Point.Empty, Size = new Size(contentWidth - (body.AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0), measuredHeight),
                    UseMnemonic = false };
                if (body.AutoScroll) {
                    text.Height = TextRenderer.MeasureText(message, Theme.Small, new Size(text.Width, int.MaxValue), flags).Height + UiScale.Px(5);
                }
                body.Controls.Add(text); dialog.Controls.Add(body);
                int buttonY = body.Bottom + UiScale.Px(26), buttonWidth = UiScale.Px(96), buttonHeight = UiScale.Px(34), gap = UiScale.Px(8);
                DialogResult[] results = buttons == MessageBoxButtons.YesNoCancel
                    ? new[] { DialogResult.No, DialogResult.Cancel, DialogResult.Yes }
                    : buttons == MessageBoxButtons.YesNo ? new[] { DialogResult.No, DialogResult.Yes }
                    : buttons == MessageBoxButtons.OKCancel ? new[] { DialogResult.Cancel, DialogResult.OK }
                    : new[] { DialogResult.OK };
                for (int i = 0; i < results.Length; i++)
                {
                    DialogResult result = results[i];
                    bool primary = i == results.Length - 1;
                    string label = result == DialogResult.Yes ? "保存" : result == DialogResult.No ? "不保存" :
                        result == DialogResult.Cancel ? "取消" : (confirmLabel ?? "确定");
                    var button = new MaterialButton { Text = label, Font = Theme.Small, TabStop = true,
                        Bounds = new Rectangle(width - inset - (results.Length - i) * buttonWidth - (results.Length - i - 1) * gap, buttonY, buttonWidth, buttonHeight),
                        BackColor = primary ? Theme.Primary : Theme.Raised, ForeColor = primary ? Theme.OnPrimary : Theme.Text,
                        FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, DialogResult = result, AccessibleName = label };
                    button.FlatAppearance.BorderSize = 0;
                    dialog.Controls.Add(button);
                    if (primary && confirmLabel == null) { dialog.AcceptButton = button; dialog.ActiveControl = button; }
                    if (result == DialogResult.Cancel && confirmLabel != null) dialog.ActiveControl = button;
                    if (result == DialogResult.Cancel || results.Length == 1) dialog.CancelButton = button;
                }
                dialog.ClientSize = new Size(width, buttonY + buttonHeight + inset);
                dialog.FormClosing += delegate { if (dialog.DialogResult == DialogResult.None) dialog.DialogResult = results.Length == 1 ? DialogResult.OK : DialogResult.Cancel; };
                return dialog.ShowDialog(owner);
            }
        }
    }
}
