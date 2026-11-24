using System.Drawing;
using System.Reflection;

[assembly: AssemblyTitle("FrameView")]
[assembly: AssemblyProduct("FrameView")]
[assembly: AssemblyDescription("FrameView · 图像序列播放器与审片工具")]
[assembly: AssemblyVersion("2026.11.24.0")]
[assembly: AssemblyFileVersion("2026.11.24.0")]
[assembly: AssemblyInformationalVersion(FrameDeck.BrandInfo.Version)]

namespace FrameDeck
{
    internal static class BrandInfo
    {
        internal const string Name = "FrameView";
        internal const string Version = "2026.11.24";
        internal static readonly Icon WindowIcon = Icon.ExtractAssociatedIcon(typeof(BrandInfo).Assembly.Location);
    }
}
