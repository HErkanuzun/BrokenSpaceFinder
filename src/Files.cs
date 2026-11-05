using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;

namespace BrokenSpaceFinder
{
    // ------------------------------------------------------------------ Biçimlendirme
    static class Fmt
    {
        static CultureInfo Culture { get { return new CultureInfo(L.Lang == "tr" ? "tr-TR" : "en-US"); } }

        public static string Size(long b)
        {
            var c = Culture;
            if (b >= 1L << 40) return (b / (double)(1L << 40)).ToString("N2", c) + " TB";
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString(b >= 100L << 30 ? "N0" : "N1", c) + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("N0", c) + " MB";
            if (b >= 1024) return (b / 1024.0).ToString("N0", c) + " KB";
            return b + " B";
        }

        public static string Num(long n) { return n.ToString("N0", Culture); }

        public static string Ago(DateTime t)
        {
            if (t == DateTime.MinValue) return "-";
            double d = (DateTime.Now - t).TotalDays;
            if (d < 1) return L.T("bugün", "today");
            if (d < 2) return L.T("dün", "yesterday");
            if (d < 60) return (int)d + L.T(" gün önce", " days ago");
            if (d < 730) return (int)(d / 30) + L.T(" ay önce", " months ago");
            return (int)(d / 365) + L.T(" yıl önce", " years ago");
        }

        public static string Pct(double f)
        {
            string n = Math.Round(f * 100).ToString(Culture);
            return L.Lang == "tr" ? "%" + n : n + "%";
        }
    }

    static class Sys
    {
        public static bool IsAdmin
        {
            get
            {
                try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
                catch { return false; }
            }
        }

        // Verilen adlardan çalışan ilk programın adı (yoksa null)
        public static string Running(string[] names)
        {
            foreach (var n in names)
            {
                Process[] p;
                try { p = Process.GetProcessesByName(n); } catch { continue; }
                bool any = p.Length > 0;
                foreach (var x in p) x.Dispose();
                if (any) return n;
            }
            return null;
        }

        public static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        public static string Roaming { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        public static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }
        public static string LocalLow { get { return Path.Combine(Home, "AppData", "LocalLow"); } }
        public static string Windows { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        public static string ProgramData { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); } }
        public static string SystemRoot { get { return Path.GetPathRoot(Windows); } }

        public static void Reveal(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ Korumalar
    static class Safety
    {
        static bool Under(string path, string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            folder = folder.TrimEnd('\\');
            return path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // Elle silinmemesi gereken bir yolsa nedenini döndürür; silinebilirse null
        public static string Reason(string path)
        {
            string full;
            try { full = Path.GetFullPath(path).TrimEnd('\\'); }
            catch { return L.T("Geçersiz yol.", "Invalid path."); }
            if (full.Length <= 2) return L.T("Sürücünün kendisi silinemez.", "A whole drive can't be deleted.");

            string root = Path.GetPathRoot(full);
            if (Under(full, Sys.Windows))
                return L.T("Bu Windows'un kendi klasörü. Buradan elle silme yapılmaz; Temizlik sekmesindeki güvenli seçenekleri kullan.",
                           "This is Windows' own folder. Nothing is deleted here by hand; use the safe options on the Clean up tab.");
            foreach (var n in new[] { "System Volume Information", "Recovery", "$Recycle.Bin", "Boot", "EFI", "$WinREAgent", "$SysReset" })
                if (Under(full, Path.Combine(root, n)))
                    return L.T("Windows'un sistem alanı, silinemez.", "A Windows system area; it can't be deleted.");

            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                       Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                       Path.Combine(root, "Program Files"), Path.Combine(root, "Program Files (x86)") })
                if (Under(full, pf))
                    return L.T("Kurulu bir programın klasörü. Klasörü silmek programı bozar; Programlar sekmesinden \"Kaldır\" ile kaldır.",
                               "This is an installed program's folder. Deleting it breaks the program; remove it with \"Uninstall\" on the Programs tab.");

            string name = Path.GetFileName(full).ToLowerInvariant();
            if (name == "pagefile.sys" || name == "hiberfil.sys" || name == "swapfile.sys" || name == "dumpstack.log.tmp")
                return L.T("Windows'un sistem dosyası. Hazırda bekletme dosyasını Temizlik sekmesinden kapatabilirsin.",
                           "A Windows system file. You can turn off the hibernation file on the Clean up tab.");

            if (Under(full, Path.Combine(Sys.ProgramData, "Microsoft")) || Under(full, Path.Combine(Sys.Local, "Microsoft")) ||
                Under(full, Path.Combine(Sys.Roaming, "Microsoft")) || Under(full, Path.Combine(Sys.Local, "Packages")))
                return L.T("Windows'un ve Microsoft uygulamalarının verileri; elle silinirse Windows bozulabilir.",
                           "Data of Windows and Microsoft apps; deleting it by hand can break Windows.");

            string[] exact =
            {
                Path.Combine(root, "Users"), Sys.ProgramData, Sys.Home, Path.Combine(Sys.Home, "AppData"), Sys.Local, Sys.Roaming, Sys.LocalLow,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Path.Combine(Sys.Home, "Downloads"),
                Path.Combine(Sys.Local, "Programs"), Path.Combine(Sys.Local, "Temp"),
            };
            foreach (var e in exact)
                if (!string.IsNullOrEmpty(e) && full.Equals(e.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return L.T("Bu ana klasörün kendisi silinemez; içindekileri tek tek seç.",
                               "This main folder itself can't be deleted; pick the things inside it one by one.");
            return null;
        }
    }

    // ------------------------------------------------------------------ Silme
    static class FileOps
    {
        // Bir dosyayı ya da klasörü elden geldiğince siler. Kullanımda olan dosyalar atlanır (failed sayılır).
        // Bağlantı klasörlerinde (junction/symlink) sadece bağlantı kaldırılır, hedefine dokunulmaz.
        public static long Delete(string path, ref int failed)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    {
                        try { Directory.Delete(path, false); } catch { failed++; }
                        return 0;
                    }
                    return DeleteDir(path, ref failed);
                }
                if (File.Exists(path))
                {
                    long len = new FileInfo(path).Length;
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    return len;
                }
            }
            catch { failed++; }
            return 0;
        }

        static long DeleteDir(string dir, ref int failed)
        {
            long freed = 0;
            foreach (var e in Native.List(dir))
            {
                string p = Path.Combine(dir, e.Name);
                if (e.IsDir)
                {
                    if (e.IsLink) { try { Directory.Delete(p, false); } catch { failed++; } }
                    else freed += DeleteDir(p, ref failed);
                }
                else
                {
                    try
                    {
                        if (p.Length >= 250) p = @"\\?\" + p;
                        File.SetAttributes(p, FileAttributes.Normal);
                        File.Delete(p);
                        freed += e.Size;
                    }
                    catch { failed++; }
                }
            }
            try { Directory.Delete(dir.Length >= 248 ? @"\\?\" + dir : dir, false); } catch { }
            return freed;
        }

        public static void Recycle(string path)
        {
            if (Directory.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
    }
}
