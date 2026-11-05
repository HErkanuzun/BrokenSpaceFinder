using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace BrokenSpaceFinder
{
    // ------------------------------------------------------------------ Uygulama veri klasörleri (AppData)
    class DataFolder
    {
        public string Name = "", Path = "", Where = "";
        public long Size;
        public int Files;
        public DateTime Newest = DateTime.MinValue;
        public bool Protected;

        public double DaysUnused { get { return Newest == DateTime.MinValue ? 9999 : (DateTime.Now - Newest).TotalDays; } }
    }

    static class DataFolders
    {
        // Windows'un kendi işleyişi için gereken klasörler: listede görünür ama silinemez
        static readonly HashSet<string> Keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Packages", "Programs", "Temp", "ConnectedDevicesPlatform", "Comms", "PeerDistRepub", "Publishers",
            "VirtualStore", "History", "Application Data", "Windows", "MicrosoftEdge", "Microsoft_Corporation", "PlaceholderTileLogoFolder",
            "CEF", "IsolatedStorage", "Package Cache", "D3DSCache", ".git",
        };

        public static List<DataFolder> Scan(Func<bool> cancel, Action<int, int> progress)
        {
            var list = new List<DataFolder>();
            var sources = new[]
            {
                new[] { Sys.Local, "Local" }, new[] { Sys.Roaming, "Roaming" }, new[] { Sys.LocalLow, "LocalLow" },
                new[] { Sys.Home, L.T("Kullanıcı klasörü", "Home folder") },
            };
            foreach (var s in sources)
            {
                foreach (var e in Native.List(s[0]))
                {
                    if (!e.IsDir || e.IsLink) continue;
                    // Kullanıcı klasöründe sadece programların açtığı gizli "." klasörleri (.cache, .nuget, .vscode…)
                    if (s[0] == Sys.Home && !e.Name.StartsWith(".")) continue;
                    var f = new DataFolder();
                    f.Name = e.Name;
                    f.Path = System.IO.Path.Combine(s[0], e.Name);
                    f.Where = s[1];
                    f.Protected = Keep.Contains(e.Name);
                    list.Add(f);
                }
            }
            int done = 0;
            var po = new ParallelOptions();
            po.MaxDegreeOfParallelism = 6;
            Parallel.ForEach(list, po, f =>
            {
                if (cancel()) return;
                int n; DateTime t;
                f.Size = Native.Stats(f.Path, out n, out t, cancel);
                f.Files = n;
                f.Newest = t;
                int d = Interlocked.Increment(ref done);
                if (progress != null) progress(d, list.Count);
            });
            return list;
        }
    }

    // ------------------------------------------------------------------ Kurulu programlar
    class InstalledApp
    {
        public string Name = "", Publisher = "", Version = "", Location = "", Uninstall = "", IconExe = "";
        public long Size;
        public bool SizeMeasured;
        public DateTime Installed = DateTime.MinValue, LastRun = DateTime.MinValue;
    }

    static class Programs
    {
        public static List<InstalledApp> Load()
        {
            var list = new List<InstalledApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = new[]
            {
                new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry64),
                new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry32),
                new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.CurrentUser, RegistryView.Registry64),
                new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.CurrentUser, RegistryView.Registry32),
            };
            foreach (var r in roots)
            {
                try
                {
                    using (var b = RegistryKey.OpenBaseKey(r.Key, r.Value))
                    using (var k = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (k == null) continue;
                        foreach (var sub in k.GetSubKeyNames())
                        {
                            try
                            {
                                using (var a = k.OpenSubKey(sub))
                                {
                                    if (a == null) continue;
                                    string name = Str(a, "DisplayName");
                                    if (name == "") continue;
                                    if (Convert.ToInt32(a.GetValue("SystemComponent", 0)) == 1) continue;
                                    if (Str(a, "ParentKeyName") != "") continue;
                                    string rt = Str(a, "ReleaseType");
                                    if (rt == "Update" || rt == "Hotfix" || rt == "Security Update") continue;
                                    string un = Str(a, "UninstallString");
                                    if (un == "") continue;
                                    var app = new InstalledApp();
                                    app.Name = name;
                                    app.Publisher = Str(a, "Publisher");
                                    app.Version = Str(a, "DisplayVersion");
                                    app.Location = Str(a, "InstallLocation").Trim('"').TrimEnd('\\');
                                    app.Uninstall = un;
                                    string icon = Str(a, "DisplayIcon").Trim('"');
                                    int comma = icon.LastIndexOf(',');
                                    if (comma > 2) icon = icon.Substring(0, comma).Trim('"');
                                    app.IconExe = icon;
                                    if (app.Location == "" && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                        try { app.Location = System.IO.Path.GetDirectoryName(icon); } catch { }
                                    object es = a.GetValue("EstimatedSize");
                                    if (es is int) app.Size = (long)(int)es * 1024;
                                    DateTime d;
                                    if (DateTime.TryParseExact(Str(a, "InstallDate"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) app.Installed = d;
                                    if (!seen.Add(name + "|" + app.Version)) continue;
                                    list.Add(app);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        static string Str(RegistryKey k, string name)
        {
            object v = k.GetValue(name);
            return v == null ? "" : v.ToString().Trim();
        }

        // Boyutu kayıtta olmayanların klasörü ölçülür; son çalıştırma Windows'un kayıtlarından bulunur
        public static void FillDetails(List<InstalledApp> apps, Func<bool> cancel)
        {
            var runs = LastRuns();
            var po = new ParallelOptions();
            po.MaxDegreeOfParallelism = 4;
            Parallel.ForEach(apps, po, a =>
            {
                if (cancel()) return;
                if (OwnFolder(a.Location))
                {
                    int n; DateTime t;
                    long s = Native.Stats(a.Location, out n, out t, cancel);
                    if (s > 0) { a.Size = s; a.SizeMeasured = true; }
                }
                DateTime best = DateTime.MinValue;
                foreach (var exe in Exes(a))
                {
                    DateTime t;
                    if (runs.TryGetValue(exe, out t) && t > best) best = t;
                }
                a.LastRun = best;
            });
        }

        // Programın kendine ait bir klasörü mü? (Windows, Program Files'ın kendisi gibi ortak klasörler ölçülmez)
        static bool OwnFolder(string dir)
        {
            if (dir.Length <= 3 || !Directory.Exists(dir)) return false;
            string w = Sys.Windows.TrimEnd('\\');
            if (dir.Equals(w, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(w + "\\", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var c in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                      Sys.Home, Sys.Local, Sys.Roaming, Sys.ProgramData, System.IO.Path.Combine(Sys.Local, "Programs") })
                if (dir.Equals(c.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        static List<string> Exes(InstalledApp a)
        {
            var r = new List<string>();
            if (a.IconExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                r.Add(System.IO.Path.GetFileName(a.IconExe).ToUpperInvariant());
            if (!OwnFolder(a.Location)) return r;
            // Program klasöründe (2 seviyeye kadar) en fazla 200 exe
            var stack = new Stack<KeyValuePair<string, int>>();
            stack.Push(new KeyValuePair<string, int>(a.Location, 0));
            while (stack.Count > 0 && r.Count < 200)
            {
                var cur = stack.Pop();
                foreach (var e in Native.List(cur.Key))
                {
                    if (e.IsDir) { if (!e.IsLink && cur.Value < 2) stack.Push(new KeyValuePair<string, int>(System.IO.Path.Combine(cur.Key, e.Name), cur.Value + 1)); }
                    else if (e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        string u = e.Name.ToUpperInvariant();
                        if (u.Contains("UNINS") || u.Contains("UPDATE") || u.Contains("CRASH") || u.Contains("SETUP") || u.Contains("HELPER")) continue;
                        r.Add(u);
                    }
                }
            }
            return r;
        }

        // exe adı (büyük harf) → son çalıştırılma. Kaynaklar: UserAssist (yönetici gerekmez) ve Prefetch (yönetici gerekir).
        static Dictionary<string, DateTime> LastRuns()
        {
            var d = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var ua = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist"))
                    if (ua != null)
                        foreach (var g in ua.GetSubKeyNames())
                            using (var c = ua.OpenSubKey(g + @"\Count"))
                            {
                                if (c == null) continue;
                                foreach (var vn in c.GetValueNames())
                                {
                                    var data = c.GetValue(vn) as byte[];
                                    if (data == null || data.Length < 68) continue;
                                    long ft = BitConverter.ToInt64(data, 60);
                                    if (ft <= 0) continue;
                                    string path = Rot13(vn);
                                    if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                                    int i = path.LastIndexOf('\\');
                                    Put(d, (i >= 0 ? path.Substring(i + 1) : path).ToUpperInvariant(), DateTime.FromFileTimeUtc(ft).ToLocalTime());
                                }
                            }
            }
            catch { }
            try
            {
                string pf = System.IO.Path.Combine(Sys.Windows, "Prefetch");
                foreach (var e in Native.List(pf))
                {
                    if (!e.Name.EndsWith(".pf", StringComparison.OrdinalIgnoreCase)) continue;
                    int dash = e.Name.LastIndexOf('-');
                    if (dash <= 0) continue;
                    Put(d, e.Name.Substring(0, dash).ToUpperInvariant(), e.Written);
                }
            }
            catch { }
            return d;
        }

        static void Put(Dictionary<string, DateTime> d, string k, DateTime t)
        {
            DateTime old;
            if (!d.TryGetValue(k, out old) || t > old) d[k] = t;
        }

        static string Rot13(string s)
        {
            var c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++)
            {
                char x = c[i];
                if (x >= 'a' && x <= 'z') c[i] = (char)('a' + (x - 'a' + 13) % 26);
                else if (x >= 'A' && x <= 'Z') c[i] = (char)('A' + (x - 'A' + 13) % 26);
            }
            return new string(c);
        }

        // Programın kendi kaldırma aracını açar
        public static void RunUninstall(InstalledApp a)
        {
            string cmd = a.Uninstall.Trim(), exe, args;
            if (cmd.StartsWith("\""))
            {
                int q = cmd.IndexOf('"', 1);
                exe = cmd.Substring(1, q - 1);
                args = cmd.Substring(q + 1).Trim();
            }
            else
            {
                int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (i > 0) { exe = cmd.Substring(0, i + 4); args = cmd.Substring(i + 4).Trim(); }
                else { exe = cmd; args = ""; }
            }
            var psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
    }
}
