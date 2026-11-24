using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;

namespace FrameDeck
{
    internal sealed class ExrPass
    {
        internal const string KeyPrefix = "exr-pass:";
        internal readonly int Part;
        internal readonly string Prefix, Name;
        internal ExrPass(int part, string prefix, string name)
        { Part = part; Prefix = prefix; Name = name; }
        internal string Id { get { return Part.ToString(CultureInfo.InvariantCulture) + "|" + Prefix; } }
        internal string Key(string path)
        {
            return KeyPrefix + Part.ToString(CultureInfo.InvariantCulture) + "|" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(Prefix)) + "|" + path;
        }

        private const int HeaderLimit = 4 * 1024 * 1024;

        internal static ExrPass[] Read(string path, out Size size)
        {
            size = Size.Empty;
            var passes = new List<ExrPass>();
            using (var reader = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
            {
                if (reader.ReadInt32() != 20000630) throw new InvalidDataException("无效的 OpenEXR 文件。");
                int version = reader.ReadInt32();
                if ((version & 0xff) != 2 || (version & 0x800) != 0) return passes.ToArray();
                bool multipart = (version & 0x1000) != 0;
                for (int part = 0; ; part++)
                {
                    string attribute = ReadName(reader);
                    if (attribute.Length == 0) break;
                    string partName = null;
                    var groups = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                    do
                    {
                        string type = ReadName(reader);
                        int length = reader.ReadInt32();
                        long end = reader.BaseStream.Position + length;
                        if (length < 0 || end > HeaderLimit || end > reader.BaseStream.Length)
                            throw new InvalidDataException("OpenEXR 文件头无效或过大。");
                        if (attribute == "channels" && type == "chlist")
                        {
                            while (reader.BaseStream.Position < end)
                            {
                                string channel = ReadName(reader, end);
                                if (channel.Length == 0) break;
                                if (end - reader.BaseStream.Position < 16) throw new InvalidDataException("OpenEXR 通道记录不完整。");
                                reader.BaseStream.Position += 16;
                                int dot = channel.LastIndexOf('.');
                                string prefix = dot < 0 ? "" : channel.Substring(0, dot);
                                HashSet<string> channels;
                                if (!groups.TryGetValue(prefix, out channels))
                                    groups.Add(prefix, channels = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                                channels.Add(channel.Substring(dot + 1));
                            }
                        }
                        else if (attribute == "name" && type == "string" && length <= 256)
                            partName = Encoding.UTF8.GetString(reader.ReadBytes(length)).TrimEnd('\0');
                        else if (part == 0 && attribute == "displayWindow" && type == "box2i" && length == 16)
                        {
                            int left = reader.ReadInt32(), top = reader.ReadInt32();
                            long width = (long)reader.ReadInt32() - left + 1, height = (long)reader.ReadInt32() - top + 1;
                            if (width > 0 && height > 0 && width <= int.MaxValue && height <= int.MaxValue)
                                size = new Size((int)width, (int)height);
                        }
                        reader.BaseStream.Position = end;
                        attribute = ReadName(reader);
                    } while (attribute.Length != 0);
                    string partLabel = string.IsNullOrWhiteSpace(partName) ? "Part " + (part + 1) : partName;
                    foreach (var group in groups.OrderBy(x => x.Key, StringComparer.Ordinal))
                    {
                        var c = group.Value;
                        if (!(c.Contains("R") && c.Contains("G") && c.Contains("B")) && !c.Contains("Y") &&
                            !(c.Contains("X") && c.Contains("Y") && c.Contains("Z")) &&
                            !(c.Contains("U") && c.Contains("V"))) continue;
                        string name = group.Key.Length == 0 ? (multipart ? partLabel : "主画面 · RGB") :
                            (multipart ? partLabel + " / " : "") + group.Key;
                        passes.Add(new ExrPass(part, group.Key, name));
                    }
                    if (!multipart) break;
                }
            }
            return passes.ToArray();
        }

        private static string ReadName(BinaryReader reader, long end = HeaderLimit)
        {
            using (var bytes = new MemoryStream())
            {
                while (bytes.Length <= 255 && reader.BaseStream.Position < end)
                {
                    byte value = reader.ReadByte();
                    if (value == 0) return Encoding.UTF8.GetString(bytes.ToArray());
                    bytes.WriteByte(value);
                }
            }
            throw new InvalidDataException("OpenEXR 文件头名称无效。");
        }

        internal static Bitmap Decode(string key, int divisor, Size previewBox, CancellationToken cancel, out Size original)
        {
            string[] fields = key.Substring(KeyPrefix.Length).Split(new[] { '|' }, 3);
            int part = int.Parse(fields[0], CultureInfo.InvariantCulture);
            string layer = Encoding.UTF8.GetString(Convert.FromBase64String(fields[1]));
            cancel.ThrowIfCancellationRequested();
            var decoder = Native.Value;
            NativeImage frame;
            IsCanceled canceled = cancel.CanBeCanceled ? (IsCanceled)(() => cancel.IsCancellationRequested ? 1 : 0) : null;
            int result = decoder.Decode(fields[2], part, Encoding.UTF8.GetBytes(layer + "\0"), out frame, canceled);
            try
            {
                cancel.ThrowIfCancellationRequested();
                if (result != 0 || frame.Pixels == IntPtr.Zero)
                    throw new InvalidDataException("无法读取 EXR 图层 " + (layer.Length == 0 ? "Part " + (part + 1) : layer) +
                        "（OpenEXR " + result + "）。");
                original = new Size(frame.Width, frame.Height);
                using (var image = new Bitmap(frame.Width, frame.Height, frame.Width * 4, PixelFormat.Format32bppArgb, frame.Pixels))
                {
                    Size target = MediaSource.PreviewSize(original, divisor, previewBox);
                    return MediaSource.MakeProxy(image, target);
                }
            }
            finally { if (frame.Pixels != IntPtr.Zero) decoder.Free(frame.Pixels); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeImage
        {
            internal int Width, Height;
            internal IntPtr Pixels;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate int DecodeFrame(string path, int part, byte[] prefix, out NativeImage frame, IsCanceled canceled);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int IsCanceled();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FreeFrame(IntPtr pixels);

        private static readonly Lazy<NativeDecoder> Native = new Lazy<NativeDecoder>(() => new NativeDecoder());

        private sealed class NativeDecoder
        {
            internal readonly DecodeFrame Decode;
            internal readonly FreeFrame Free;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);
            [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
            private static extern IntPtr GetProcAddress(IntPtr module, string name);

            internal NativeDecoder()
            {
                const string name = "FrameViewExr.dll";
                string path = Bundle.Extract(name);
                if (path != null)
                {
                    foreach (string dependency in new[] { "OpenEXRCore.dll", "deflate.dll", "zstd.dll", "openjph.dll" })
                        Bundle.Extract(dependency);
                }
                else path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exr-runtime", name);
                const uint dllDirectory = 0x100, defaultDirectories = 0x1000;
                IntPtr module = LoadLibraryEx(path, IntPtr.Zero, dllDirectory | defaultDirectories);
                if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "无法加载 OpenEXR 解码组件。请确认已安装 Microsoft Visual C++ x64 运行库。");
                Decode = (DecodeFrame)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module, "fv_decode_exr"), typeof(DecodeFrame));
                Free = (FreeFrame)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module, "fv_free_exr"), typeof(FreeFrame));
            }
        }
    }
}
