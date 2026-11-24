# FrameView

Windows x64 图像序列播放器与审片工具。支持多层 EXR、A/B 对比、帧缓存、区间标记和片段导出。

## 下载与使用

从 [GitHub Releases](https://github.com/Jugui-Lin/FrameView/releases/latest) 下载并运行 `FrameView.exe`。

- **多层 EXR** · 从时间轴图层右侧的「EXR ▾」切换渲染层。加载组件失败时，安装 [Microsoft Visual C++ x64 运行库](https://aka.ms/vc14/vc_redist.x64.exe)。
- **视频导入、MP4 和 GIF 导出** · 下载 [FFmpeg Windows Essentials](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2)，将解压后的 `bin/ffmpeg.exe` 和 `bin/ffprobe.exe` 放在 `FrameView.exe` 旁。
- **操作说明** · [使用说明](使用说明.md)

## 从源码构建

需要 Windows x64、.NET Framework 4.8、PowerShell、支持 Zstandard 的 `tar.exe`，以及 PATH 中的 Zig。依赖准备脚本会下载固定版本并验证 SHA-256。

```powershell
.\scripts\prepare-dependencies.ps1
.\src\build.ps1
```

程序输出到 `build/FrameView.exe`。可用 `-Zig` 指定 Zig 可执行文件的路径。

第三方组件的许可和来源见 [third-party](third-party)。
