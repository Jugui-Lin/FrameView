using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class MainForm
    {
        private void ExportGif()
        {
            int first = WorkIn, last = WorkOut;
            if (!Session.Layers.Any(layer => layer.Visible && layer.Start <= last && layer.End >= first &&
                (layer.Source != null || layer.Demo > 0)))
            {
                ShowMissingExportMedia();
                return;
            }
            if (!ConfirmExportRange("GIF")) return;
            Pause();
            try
            {
                int interval, edge, colors;
                if (!AskGifSettings(first, last, out interval, out edge, out colors)) return;
                string encoder = VideoTools.Encoder();
                string output = WriteClip("导出 GIF 片段", "正在渲染并编码 GIF…", first, last, ".gif",
                    (staging, cancel) => RenderGif(encoder, staging, cancel, first, last, interval, edge, colors));
                SetStatus("已导出 GIF · " + Path.GetFileName(output));
            }
            catch (OperationCanceledException) { SetStatus("已取消导出 GIF。"); }
            catch (Exception ex) { MaterialMessageBox.Show(this, ex.Message, "导出 GIF 失败"); }
        }

        private void RenderGif(string encoder, string output, CancellationToken cancel, int first, int last,
            int interval, int edge, int colors)
        {
            decimal fps = Session.Fps;
            RenderClip(first, last, interval, size => edge == 0 ? 1f : Math.Min(1f, edge / (float)Math.Max(size.Width, size.Height)),
                cancel, (size, write) => VideoTools.EncodeGif(encoder, size, output, fps, interval, last - first + 1, colors, cancel, write));
        }

        private bool AskGifSettings(int first, int last, out int interval, out int edge, out int colors)
        {
            int length = last - first + 1;
            int minimum = (int)Math.Ceiling(Session.Fps / 50m);
            int[] edges = ViewerDefaults.GifEdges, palettes = ViewerDefaults.GifPalettes;
            int selectedEdge = Array.IndexOf(edges, defaults.GifEdge);
            int selectedPalette = Array.IndexOf(palettes, defaults.GifColors);
            using (var dialog = new Form { Text = "导出 GIF 片段", Font = Theme.Small,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Theme.Panel, ForeColor = Theme.Text, ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false })
            {
                dialog.HandleCreated += delegate { ApplyCaptionTheme(dialog.Handle); };
                var summaryCard = new MaterialSurface(12, Theme.Container) { Dock = DockStyle.None, Bounds = new Rectangle(20, 16, 418, 55) };
                var summaryContent = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Container };
                var frames = LabelOf("第 " + (first + 1) + "–" + (last + 1) + " 帧", 178, 55, Theme.OnContainer);
                frames.Location = new Point(15, 0);
                var summary = LabelOf("", 210, 55, Theme.OnContainer);
                summary.Font = Theme.Strong;
                summary.Location = new Point(193, 0);
                summary.TextAlign = ContentAlignment.MiddleRight;
                summaryContent.Controls.AddRange(new Control[] { frames, summary });
                summaryCard.Controls.Add(summaryContent);

                var spacing = NumberOf(minimum, 120, Math.Max(minimum, Math.Min(length, defaults.GifInterval)), 80, integratedArrows: true);
                spacing.AccessibleName = "帧间隔，每隔几帧取一帧";
                var intervalField = new MaterialNumberHost(spacing, 90) { Location = new Point(118, 86) };
                var intervalLabel = LabelOf("帧间隔", 90, 30, Theme.Text);
                intervalLabel.Location = new Point(20, 86);
                var intervalHint = LabelOf("每隔 N 帧取一帧", 212, 30, Theme.Muted);
                intervalHint.Location = new Point(220, 86);
                Action update = delegate
                {
                    int step = (int)spacing.Value;
                    int samples = (length - 1) / step + 1;
                    summary.Text = "输出 " + samples + " 帧 · " + (Session.Fps / step).ToString("0.##") + " FPS";
                };
                spacing.ValueChanged += delegate { update(); };
                update();

                var sizeLabel = LabelOf("输出尺寸", 112, 24, Theme.Text);
                sizeLabel.Location = new Point(20, 127);
                var sizeHint = LabelOf("长边上限 (px) · 等比缩小", 220, 24, Theme.Muted);
                sizeHint.Location = new Point(218, 127);
                sizeHint.TextAlign = ContentAlignment.MiddleRight;
                AddGifOptions(dialog, new[] { "480", "640", "960", "1280", "原尺寸" },
                    156, 78, "输出尺寸", selectedEdge, index => selectedEdge = index);
                var qualityLabel = LabelOf("输出质量", 112, 24, Theme.Text);
                qualityLabel.Location = new Point(20, 200);
                AddGifOptions(dialog, new[] { "精简 · 64 色", "均衡 · 128 色", "精细 · 256 色" },
                    229, 134, "输出质量", selectedPalette, index => selectedPalette = index);

                var loop = LabelOf("循环播放", 140, 34, Theme.Muted);
                loop.Location = new Point(20, 273);
                var save = ButtonOf("导出 GIF", 116, delegate { }, true);
                var cancel = ButtonOf("取消", 76, delegate { });
                save.Location = new Point(322, 273);
                cancel.Location = new Point(236, 273);
                save.Height = cancel.Height = 34;
                save.TabStop = cancel.TabStop = true;
                save.DialogResult = DialogResult.OK;
                cancel.DialogResult = DialogResult.Cancel;
                dialog.AcceptButton = save;
                dialog.CancelButton = cancel;
                dialog.Controls.AddRange(new Control[] { summaryCard, intervalLabel, intervalField, intervalHint,
                    sizeLabel, sizeHint, qualityLabel, loop, cancel, save });
                intervalField.TabIndex = 0;
                cancel.TabIndex = 9;
                save.TabIndex = 10;
                // The dialog's bounds are authored in logical pixels, just like the toolbar.
                dialog.AutoScaleMode = AutoScaleMode.None;
                dialog.Scale(new SizeF(UiScale.Factor, UiScale.Factor));
                dialog.ClientSize = UiScale.Box(0, 0, 458, 317).Size;
                dialog.ActiveControl = spacing;
                bool accepted = dialog.ShowDialog(this) == DialogResult.OK;
                interval = (int)spacing.Value;
                edge = edges[selectedEdge];
                colors = palettes[selectedPalette];
                return accepted;
            }
        }

        private static void AddGifOptions(Form dialog, string[] labels, int y, int width, string name, int current, Action<int> select)
        {
            var buttons = new Button[labels.Length];
            Action<int> apply = index =>
            {
                select(index);
                for (int i = 0; i < buttons.Length; i++)
                {
                    SetActive(buttons[i], i == index);
                    buttons[i].AccessibleDescription = i == index ? "当前选中" : "";
                }
            };
            int startTab = dialog.Controls.Count + 1;
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var button = ButtonOf(labels[i], width, () => apply(index));
                button.Location = new Point(20 + i * (width + 8), y);
                button.Height = 34;
                button.TabIndex = startTab + i;
                button.TabStop = true;
                button.AccessibleName = name + " · " + labels[i];
                buttons[i] = button;
                dialog.Controls.Add(button);
            }
            apply(current);
        }
    }
}
