using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace BrokenSpaceFinder
{
    struct FsEntry
    {
        public string Name;
        public bool IsDir, IsLink;
        public long Size;
        public DateTime Written;
    }

    // Klasörleri FindFirstFileEx ile okur: DirectoryInfo'dan kat kat hızlıdır, milyonlarca dosyada da az bellek harcar.
    static class Native
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
        struct WIN32_FIND_DATA
        {
            public uint Attr;
            public long Created, Accessed, Written;
            public uint SizeHigh, SizeLow, Res0, Res1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AltName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileExW(string path, int infoLevel, out WIN32_FIND_DATA data, int searchOp, IntPtr filter, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool FindNextFileW(IntPtr h, out WIN32_FIND_DATA data);

        [DllImport("kernel32.dll")]
        static extern bool FindClose(IntPtr h);

        const int FindExInfoBasic = 1, FIND_FIRST_EX_LARGE_FETCH = 2;
        const uint DIRECTORY = 0x10, REPARSE_POINT = 0x400;
        static readonly long MaxFileTime = DateTime.MaxValue.ToFileTimeUtc();

        // Bir klasörün içini listeler. ok=false: okunamadı (izin yok ya da yok).
        public static List<FsEntry> List(string dir, out bool ok)
        {
            var result = new List<FsEntry>();
            string p = dir.StartsWith(@"\\?\") ? dir : @"\\?\" + dir;
            p = p.TrimEnd('\\') + @"\*";
            WIN32_FIND_DATA f;
            IntPtr h = FindFirstFileExW(p, FindExInfoBasic, out f, 0, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
            if (h == new IntPtr(-1)) { ok = false; return result; }
            ok = true;
            try
            {
                do
                {
                    if (f.Name == "." || f.Name == "..") continue;
                    var e = new FsEntry();
                    e.Name = f.Name;
                    e.IsDir = (f.Attr & DIRECTORY) != 0;
                    e.IsLink = (f.Attr & REPARSE_POINT) != 0;
                    e.Size = e.IsDir ? 0 : ((long)f.SizeHigh << 32) | f.SizeLow;
                    e.Written = f.Written > 0 && f.Written < MaxFileTime ? DateTime.FromFileTimeUtc(f.Written).ToLocalTime() : DateTime.MinValue;
                    result.Add(e);
                } while (FindNextFileW(h, out f));
            }
            finally { FindClose(h); }
            return result;
        }

        public static List<FsEntry> List(string dir)
        {
            bool ok;
            return List(dir, out ok);
        }

        // Klasör ağacının toplam boyutu, dosya sayısı ve en yeni dosyanın tarihi. Bağlantı klasörlerine (junction) girilmez.
        public static long Stats(string root, out int files, out DateTime newest, Func<bool> cancel)
        {
            long size = 0;
            files = 0;
            newest = DateTime.MinValue;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                if (cancel != null && cancel()) break;
                string d = stack.Pop();
                foreach (var e in List(d))
                {
                    if (e.IsDir) { if (!e.IsLink) stack.Push(Path.Combine(d, e.Name)); }
                    else
                    {
                        size += e.Size;
                        files++;
                        if (e.Written > newest) newest = e.Written;
                    }
                }
            }
            return size;
        }

        // Bir dosyanın ya da klasörün boyutu
        public static long PathSize(string path)
        {
            if (Directory.Exists(path))
            {
                int n; DateTime t;
                return Stats(path, out n, out t, null);
            }
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; } catch { return 0; }
        }

        // ------------------------------------------------------------------ Geri Dönüşüm Kutusu
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO { public int cbSize; public long Size; public long Items; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBinW(string root, ref SHQUERYRBINFO info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBinW(IntPtr hwnd, string root, uint flags);

        public static long RecycleBinSize()
        {
            var info = new SHQUERYRBINFO();
            info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
            try { return SHQueryRecycleBinW(null, ref info) == 0 ? info.Size : 0; } catch { return 0; }
        }

        // Onay sormadan, ilerleme penceresi ve ses olmadan boşaltır (onayı program kendisi alır)
        public static void EmptyRecycleBin()
        {
            try { SHEmptyRecycleBinW(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); } catch { }
        }
    }
}
