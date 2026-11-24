using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class MainForm
    {
        private static readonly string ExportFolderSetting = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameDeck", "clip-output.txt");

        private bool ConfirmExportRange(string format)
        {
            if (HasWorkRange) return true;
            string message = "当前没有选区。可以直接将整个时间轴导出为 " + format + "。\n" +
                "如需只导出片段，请先用入点（I）和出点（O）设置选区。";
            return MaterialMessageBox.Show(this, message, "未设置选区",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information,
                confirmLabel: "导出完整范围") == DialogResult.OK;
        }

        private void ShowMissingExportMedia()
        {
            bool empty = Session.Layers.Count == 0;
            string message = empty ? "请先导入图像序列或视频。" : "当前范围内没有可导出的可见素材。";
            SetStatus(message);
            MaterialMessageBox.Show(this, message, empty ? "请先导入素材" : "没有可导出的画面",
                icon: MessageBoxIcon.Information);
        }

        private static string GetExportFolder()
        {
            try
            {
                var setting = new FileInfo(ExportFolderSetting);
                if (setting.Exists && setting.Length <= 32768)
                {
                    string value = File.ReadAllText(setting.FullName);
                    if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(videos)) videos = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(videos, "FrameView Exports");
        }

        private void ChooseExportFolder()
        {
            PickExportFolder(this);
        }

        private void PickExportFolder(IWin32Window owner)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "选择片段 MP4 和 GIF 共用的导出目录",
                SelectedPath = GetExportFolder(),
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    Directory.CreateDirectory(dialog.SelectedPath);
                    SaveExportFolder(dialog.SelectedPath);
                    SetStatus("导出目录 · " + dialog.SelectedPath);
                }
                catch (Exception ex) { MaterialMessageBox.Show(owner, ex.Message, "无法设置导出目录"); }
            }
        }

        private static void SaveExportFolder(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ExportFolderSetting));
            string temporary = ExportFolderSetting + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, path);
                if (File.Exists(ExportFolderSetting)) File.Replace(temporary, ExportFolderSetting, null);
                else File.Move(temporary, ExportFolderSetting);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private void OpenExportFolder()
        {
            try
            {
                string directory = Directory.CreateDirectory(GetExportFolder()).FullName;
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
            catch (Exception ex) { MaterialMessageBox.Show(this, ex.Message, "无法打开导出目录"); }
        }

        private string WriteClip(string title, string message, int first, int last, string extension,
            Action<string, CancellationToken> render)
        {
            string directory = Directory.CreateDirectory(GetExportFolder()).FullName;
            string basename = "clip_" + (first + 1).ToString("0000") + "-" + (last + 1).ToString("0000");
            string staging = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp" + extension);
            try
            {
                MediaProgress.Run(this, title, message, cancel =>
                {
                    render(staging, cancel);
                    cancel.ThrowIfCancellationRequested();
                    return true;
                });
                return PublishClip(staging, directory, basename, extension);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }

        private static string PublishClip(string staging, string directory, string basename, string extension)
        {
            for (int index = 1; ; index++)
            {
                string suffix = index == 1 ? "" : " (" + index + ")";
                string output = Path.Combine(directory, basename + suffix + extension);
                try { File.Move(staging, output); return output; }
                catch (IOException)
                {
                    if (!File.Exists(output) && !Directory.Exists(output)) throw;
                }
            }
        }
    }
}
