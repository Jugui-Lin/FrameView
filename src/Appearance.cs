using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FrameDeck
{
    internal enum AppearanceMode { System, Light, Dark }

    internal static class AppearanceSettings
    {
        private static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrameDeck", "appearance.txt");

        internal static AppearanceMode Load()
        {
            try
            {
                AppearanceMode mode;
                if (File.Exists(PathName) && new FileInfo(PathName).Length <= 32 &&
                    Enum.TryParse(File.ReadAllText(PathName).Trim(), out mode) && Enum.IsDefined(typeof(AppearanceMode), mode)) return mode;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return AppearanceMode.System;
        }

        internal static void Save(AppearanceMode mode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            string temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, mode.ToString());
                if (File.Exists(PathName)) File.Replace(temporary, PathName, null);
                else File.Move(temporary, PathName);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static bool IsDark(AppearanceMode mode)
        {
            if (mode != AppearanceMode.System) return mode == AppearanceMode.Dark;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    return key != null && Equals(key.GetValue("AppsUseLightTheme", 1), 0);
                }
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }
    }

    internal sealed partial class MainForm
    {
        private AppearanceMode appearance;
        private MaterialButton appearanceButton;

        private void ToggleAppearance()
        {
            SetAppearance(Theme.IsDark ? AppearanceMode.Light : AppearanceMode.Dark);
        }

        private void SyncAppearanceButton()
        {
            appearanceButton.Glyph = Theme.IsDark ? "\uE706" : "\uE708";
            SetToolbarHint(appearanceButton, Theme.IsDark ? "切换到白天模式" : "切换到夜间模式");
            appearanceButton.Invalidate();
        }

        private void SetAppearance(AppearanceMode mode)
        {
            if (appearance == mode) return;
            appearance = mode;
            ApplyAppearanceTheme(animate: true);
            try { AppearanceSettings.Save(mode); }
            catch (IOException) { SetStatus("已切换外观，当前设置未能保存。"); }
            catch (UnauthorizedAccessException) { SetStatus("已切换外观，当前设置未能保存。"); }
        }

        private void SystemAppearanceChanged(object sender, UserPreferenceChangedEventArgs args)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke((Action)delegate { if (appearance == AppearanceMode.System) ApplyAppearanceTheme(); }); }
            catch (InvalidOperationException) { }
        }

        private void ApplyAppearanceTheme(bool animate = false)
        {
            bool dark = AppearanceSettings.IsDark(appearance);
            if (dark == Theme.IsDark) { SyncAppearanceButton(); return; }
            Color fromCaptionBackground = captionBackground.IsEmpty ? Theme.Background : captionBackground;
            Color fromCaptionForeground = captionForeground.IsEmpty ? Theme.Text : captionForeground;
            bool fromCaptionDark = captionDark;
            Bitmap before = null;
            if (animate && Motion.Enabled && Visible && WindowState != FormWindowState.Minimized)
            {
                try { before = CaptureThemeFrame(); }
                catch (ArgumentException) { }
                catch (System.Runtime.InteropServices.ExternalException) { }
                catch (OutOfMemoryException) { }
            }
            EndThemeTransition(finishCaption: false);
            if (before != null)
            {
                Point origin = PointToClient(appearanceButton.PointToScreen(
                    new Point(appearanceButton.Width / 2, appearanceButton.Height / 2)));
                themeTransition = new ThemeTransition(before, origin, () => EndThemeTransition());
                Controls.Add(themeTransition); themeTransition.BringToFront(); themeTransition.Update();
            }
            var replacements = Theme.UseDark(dark);
            SyncAppearanceButton();
            if (replacements.Count == 0) return;
            Recolor(this, replacements);
            if (layerPage.Parent == null) Recolor(layerPage, replacements);
            if (themeTransition == null) ApplyCaptionTheme();
            Invalidate(true);
            if (themeTransition != null)
            {
                Color targetBackground = Theme.Background, targetForeground = Theme.Text;
                themeTransition.ProgressChanged = progress => AnimateCaption(fromCaptionBackground, fromCaptionForeground,
                    fromCaptionDark, targetBackground, targetForeground, dark, progress);
                try { themeTransition.Start(CaptureThemeFrame(composite: false)); }
                catch (ArgumentException) { EndThemeTransition(); }
                catch (System.Runtime.InteropServices.ExternalException) { EndThemeTransition(); }
                catch (OutOfMemoryException) { EndThemeTransition(); }
            }
        }

        private static void Recolor(Control control, Dictionary<int, Color> colors)
        {
            Color color;
            if (colors.TryGetValue(control.BackColor.ToArgb(), out color)) control.BackColor = color;
            if (colors.TryGetValue(control.ForeColor.ToArgb(), out color)) control.ForeColor = color;
            var button = control as MaterialButton;
            if (button != null) button.ResetMotion();
            foreach (Control child in control.Controls) Recolor(child, colors);
        }
    }
}
