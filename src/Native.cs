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
        public long Time;   // UTC FILETIME, 0 = bilinmiyor; yerel saate çevirmek pahalı, sadece gerekince yapılır
        public DateTime Written { get { return Native.Local(Time); } }
    }

    // Klasörleri FindFirstFileEx ile okur: DirectoryInfo'dan kat kat hızlıdır, milyonlarca dosyada da az bellek harcar.
    static class Native
    {
        // WIN32_FIND_DATAW ham bellekten okunur: dosya başına sadece adın string'i oluşur (kısa ad ve marshal kopyası yok).
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileExW(string path, int infoLevel, IntPtr data, int searchOp, IntPtr filter, int flags);

        [DllImport("kernel32.dll")]
        static extern bool FindNextFileW(IntPtr h, IntPtr data);

        [DllImport("kernel32.dll")]
        static extern bool FindClose(IntPtr h);

        const int FindExInfoBasic = 1, FIND_FIRST_EX_LARGE_FETCH = 2;
        const uint DIRECTORY = 0x10, REPARSE_POINT = 0x400;
        const int DataSize = 592, OffWritten = 20, OffSizeHigh = 28, OffSizeLow = 32, OffName = 44;
        static readonly long MaxFileTime = DateTime.MaxValue.ToFileTimeUtc();

        [ThreadStatic] static IntPtr buf;

        public static DateTime Local(long fileTime)
        {
            return fileTime > 0 ? DateTime.FromFileTimeUtc(fileTime).ToLocalTime() : DateTime.MinValue;
        }

        // Bir klasörün içini listeler. ok=false: okunamadı (izin yok ya da yok).
        public static List<FsEntry> List(string dir, out bool ok)
        {
            var result = new List<FsEntry>();
            ok = Read(dir, result);
            return result;
        }

        // Listeyi verilen listeye ekler (tarayıcı aynı listeyi tekrar kullanır)
        public static bool Read(string dir, List<FsEntry> into)
        {
            if (buf == IntPtr.Zero) buf = Marshal.AllocHGlobal(DataSize);   // iş parçacığı başına bir kez
            string p = dir.StartsWith(@"\\?\") ? dir : @"\\?\" + dir;
            p = p.TrimEnd('\\') + @"\*";
            IntPtr h = FindFirstFileExW(p, FindExInfoBasic, buf, 0, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
            if (h == new IntPtr(-1)) return false;
            try
            {
                do
                {
                    char c0 = (char)Marshal.ReadInt16(buf, OffName), c1 = (char)Marshal.ReadInt16(buf, OffName + 2);
                    if (c0 == '.' && (c1 == '\0' || (c1 == '.' && Marshal.ReadInt16(buf, OffName + 4) == 0))) continue;
                    uint attr = (uint)Marshal.ReadInt32(buf, 0);
                    var e = new FsEntry();
                    e.Name = Marshal.PtrToStringUni(buf + OffName);
                    e.IsDir = (attr & DIRECTORY) != 0;
                    e.IsLink = (attr & REPARSE_POINT) != 0;
                    if (!e.IsDir) e.Size = ((long)(uint)Marshal.ReadInt32(buf, OffSizeHigh) << 32) | (uint)Marshal.ReadInt32(buf, OffSizeLow);
                    long w = Marshal.ReadInt64(buf, OffWritten);
                    e.Time = w > 0 && w < MaxFileTime ? w : 0;
                    into.Add(e);
                } while (FindNextFileW(h, buf));
            }
            finally { FindClose(h); }
            return true;
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
