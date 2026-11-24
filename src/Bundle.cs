using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FrameDeck
{
    internal static class Bundle
    {
        private const string Prefix = "FrameCue.Bundle.";
        private const string NativeMagick = "Magick.Native-Q8-x64.dll";
        private static readonly Assembly Executable = typeof(Bundle).Assembly;
        private static readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Verified = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly object Gate = new object();
        private static string cache;
        private static IntPtr nativeMagick;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);

        internal static void Register()
        {
            using (Stream stream = Executable.GetManifestResourceStream(Prefix + "manifest"))
            {
                if (stream == null) return;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    var manifest = new StringBuilder();
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] fields = line.Split(new[] { ' ' }, 2);
                        if (fields.Length != 2 || fields[0].Length != 64 || Path.GetFileName(fields[1]) != fields[1])
                            throw new InvalidDataException("程序内的组件清单无效。");
                        Files.Add(fields[1], fields[0]);
                        manifest.AppendLine(line);
                    }
                    using (var sha = SHA256.Create())
                    {
                        string version = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(manifest.ToString())));
                        cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "FrameCue", "Components", version.Substring(0, 24));
                    }
                }
            }
            AppDomain.CurrentDomain.AssemblyResolve += ResolveMagick;
        }

        private static Assembly ResolveMagick(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name != "Magick.NET.Core" && name != "Magick.NET-Q8-x64") return null;
            lock (Gate)
            {
                string path = Extract(name + ".dll");
                if (path == null) return null;
                if (name == "Magick.NET-Q8-x64" && nativeMagick == IntPtr.Zero)
                {
                    const uint dllDirectory = 0x100, defaultDirectories = 0x1000;
                    nativeMagick = LoadLibraryEx(Extract(NativeMagick), IntPtr.Zero, dllDirectory | defaultDirectories);
                    if (nativeMagick == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载内置图像解码组件。");
                }
                return Assembly.LoadFrom(path);
            }
        }

        internal static string Extract(string name)
        {
            lock (Gate)
            {
                string expected, path;
                if (!Files.TryGetValue(name, out expected)) return null;
                if (Verified.TryGetValue(name, out path)) return path;
                Directory.CreateDirectory(cache);
                path = Path.Combine(cache, name);
                if (!Matches(path, expected))
                {
                    string temporary = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        using (Stream resource = Executable.GetManifestResourceStream(Prefix + name))
                        {
                            if (resource == null) throw new InvalidDataException("程序内缺少组件 " + name);
                            using (var zip = new GZipStream(resource, CompressionMode.Decompress))
                            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                zip.CopyTo(output);
                        }
                        if (!Matches(temporary, expected)) throw new InvalidDataException("内置组件校验失败：" + name);
                        try
                        {
                            if (File.Exists(path)) File.Replace(temporary, path, null);
                            else File.Move(temporary, path);
                        }
                        catch (IOException) { if (!Matches(path, expected)) throw; }
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                Verified[name] = path;
                return path;
            }
        }

        private static bool Matches(string path, string expected)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (var sha = SHA256.Create())
                    return string.Equals(Hex(sha.ComputeHash(stream)), expected, StringComparison.OrdinalIgnoreCase);
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        private static string Hex(byte[] hash) { return BitConverter.ToString(hash).Replace("-", ""); }

        internal static string Notices()
        {
            using (Stream stream = Executable.GetManifestResourceStream(Prefix + "notices"))
            {
                if (stream == null) return null;
                using (var zip = new GZipStream(stream, CompressionMode.Decompress))
                using (var reader = new StreamReader(zip, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }
    }
}
