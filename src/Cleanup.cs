using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace BrokenSpaceFinder
{
    enum Risk { Safe, Minor, Careful }

    // Bir temizlik seçeneği: neyi sildiği (Targets), ne zaman yapılamayacağı (Procs, NeedsAdmin) ve riski
    class Rule
    {
        public string Title = "", Desc = "";
        public Risk Risk;
        public bool NeedsAdmin;
        public string[] Procs = new string[0];
        public Func<List<string>> Targets;
        public Func<long> CustomSize;      // hedef listesi yerine özel ölçüm (ör. Geri Dönüşüm Kutusu)
        public Func<string> CustomRun;     // hedef silmek yerine özel işlem; sonuç metnini döndürür
        public string OpenPath;

        // hesaplanan
        public long Size = -1;
        public int Count;

        public string Blocked()
        {
            string p = Sys.Running(Procs);
            if (p != null) return string.Format(L.T("{0} açık; kapatınca temizlenebilir", "{0} is running; close it first"), p);
            if (NeedsAdmin && !Sys.IsAdmin) return L.T("yönetici izni gerekir", "needs administrator rights");
            return null;
        }

        public static string RiskName(Risk r)
        {
            return r == Risk.Safe ? L.T("Güvenli", "Safe") : r == Risk.Minor ? L.T("Küçük yan etki", "Minor side effect") : L.T("Dikkat", "Careful");
        }
    }

    static class Rules
    {
        // ------------------------------------------------------------------ hedef yardımcıları
        static List<string> Kids(params string[] dirs) { return KidsOlder(0, dirs); }

        // Klasörlerin içindekiler (klasörün kendisi kalır). days > 0: sadece o kadar gündür değişmemiş olanlar.
        static List<string> KidsOlder(int days, params string[] dirs)
        {
            var r = new List<string>();
            DateTime cut = DateTime.Now.AddDays(-days);
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                foreach (var e in Native.List(d))
                    if (days <= 0 || e.Written < cut) r.Add(Path.Combine(d, e.Name));
            }
            return r;
        }

        static List<string> Existing(params string[] paths)
        {
            var r = new List<string>();
            foreach (var p in paths) if (Directory.Exists(p) || File.Exists(p)) r.Add(p);
            return r;
        }

        // Sürüm klasörlerinden en yenisi hariç hepsi (ör. NeoStack her güncellemede eskisini silmeden yenisini indirir)
        static List<string> AllButNewest(string dir)
        {
            var r = new List<string>();
            if (!Directory.Exists(dir)) return r;
            var dirs = new List<FsEntry>();
            foreach (var e in Native.List(dir)) if (e.IsDir && !e.IsLink) dirs.Add(e);
            dirs.Sort((a, b) => b.Written.CompareTo(a.Written));
            for (int i = 1; i < dirs.Count; i++) r.Add(Path.Combine(dir, dirs[i].Name));
            return r;
        }

        static string Lc(params string[] p) { return Path.Combine(Sys.Local, Path.Combine(p)); }
        static string Rm(params string[] p) { return Path.Combine(Sys.Roaming, Path.Combine(p)); }

        // ------------------------------------------------------------------ Unreal projeleri
        // Belgeler\Unreal Projects içindekiler + Unreal'ın "son açılan projeler" listesi
        public static List<string> UnrealProjects()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Unreal Projects");
            try
            {
                if (Directory.Exists(docs))
                    foreach (var d in Directory.GetDirectories(docs))
                        if (Directory.GetFiles(d, "*.uproject").Length > 0) set.Add(d);
            }
            catch { }
            try
            {
                string ue = Lc("UnrealEngine");
                if (Directory.Exists(ue))
                    foreach (var ver in Directory.GetDirectories(ue))
                    {
                        string ini = Path.Combine(ver, "Saved", "Config", "WindowsEditor", "EditorSettings.ini");
                        if (!File.Exists(ini)) continue;
                        foreach (Match m in Regex.Matches(File.ReadAllText(ini), "([A-Za-z]:[^\"=\\r\\n]*?\\.uproject)"))
                        {
                            string dir = Path.GetDirectoryName(m.Groups[1].Value.Replace('/', '\\'));
                            if (Directory.Exists(dir)) set.Add(dir.TrimEnd('\\'));
                        }
                    }
            }
            catch { }
            return new List<string>(set);
        }

        static readonly string[] UnrealProcs = { "UnrealEditor", "UnrealEditor-Cmd", "UE4Editor", "ShaderCompileWorker" };

        // ------------------------------------------------------------------ bütün seçenekler
        public static List<Rule> All()
        {
            var list = new List<Rule>();
            Rule r;

            r = new Rule();
            r.Title = L.T("Geçici dosyalar (Temp)", "Temporary files (Temp)");
            r.Desc = L.T("Programların bırakıp unuttuğu geçici dosyalar. Son 24 saatte değişenlere dokunulmaz.",
                         "Temporary files left behind by programs. Anything changed in the last 24 hours is kept.");
            r.Risk = Risk.Safe;
            r.OpenPath = Path.GetTempPath();
            r.Targets = () => KidsOlder(1, Path.GetTempPath().TrimEnd('\\'));
            list.Add(r);

            r = new Rule();
            r.Title = L.T("Windows'un geçici dosyaları", "Windows temporary files");
            r.Desc = L.T("C:\\Windows\\Temp içindeki 24 saatten eski dosyalar.", "Files older than 24 hours in C:\\Windows\\Temp.");
            r.Risk = Risk.Safe;
            r.NeedsAdmin = true;
            r.OpenPath = Path.Combine(Sys.Windows, "Temp");
            r.Targets = () => KidsOlder(1, Path.Combine(Sys.Windows, "Temp"));
            list.Add(r);

            r = new Rule();
            r.Title = L.T("Çökme dökümleri ve hata raporları", "Crash dumps and error reports");
            r.Desc = L.T("Çöken programların bıraktığı döküm dosyaları ve Windows hata raporları.", "Dump files left by crashed programs and Windows error reports.");
            r.Risk = Risk.Safe;
            r.OpenPath = Lc("CrashDumps");
            r.Targets = () =>
            {
                var t = Kids(Lc("CrashDumps"), Lc("Microsoft", "Windows", "WER", "ReportArchive"), Lc("Microsoft", "Windows", "WER", "ReportQueue"),
                             Path.Combine(Sys.ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"),
                             Path.Combine(Sys.ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"),
                             Path.Combine(Sys.Windows, "Minidump"));
                t.AddRange(Existing(Path.Combine(Sys.Windows, "MEMORY.DMP")));
                return t;
            };
            list.Add(r);

            r = new Rule();
            r.Title = L.T("Windows Update indirmeleri", "Windows Update downloads");
            r.Desc = L.T("Kurulmuş güncellemelerin indirme dosyaları. Gerekirse Windows yeniden indirir.", "Download files of installed updates. Windows downloads them again if needed.");
            r.Risk = Risk.Safe;
            r.NeedsAdmin = true;
            r.OpenPath = Path.Combine(Sys.Windows, "SoftwareDistribution", "Download");
            r.Targets = () => Kids(Path.Combine(Sys.Windows, "SoftwareDistribution", "Download"));
            list.Add(r);

            // Tarayıcılar: her biri ayrı, çünkü açıkken önbelleği silinmemeli
            AddBrowser(list, "Google Chrome", "chrome", Lc("Google", "Chrome", "User Data"));
            AddBrowser(list, "Microsoft Edge", "msedge", Lc("Microsoft", "Edge", "User Data"));
            AddBrowser(list, "Brave", "brave", Lc("BraveSoftware", "Brave-Browser", "User Data"));
            AddBrowser(list, "Opera", "opera", Rm("Opera Software"));
            if (Directory.Exists(Lc("Mozilla", "Firefox", "Profiles")))
            {
                r = new Rule();
                r.Title = L.T("Firefox önbelleği", "Firefox cache");
                r.Desc = L.T("Sitelerin geçici kopyaları. Girişlerin ve geçmişin silinmez.", "Temporary copies of websites. Logins and history are kept.");
                r.Risk = Risk.Safe;
                r.Procs = new[] { "firefox" };
                r.OpenPath = Lc("Mozilla", "Firefox", "Profiles");
                r.Targets = () =>
                {
                    var t = new List<string>();
                    foreach (var p in Native.List(Lc("Mozilla", "Firefox", "Profiles")))
                        if (p.IsDir) t.AddRange(Existing(Lc("Mozilla", "Firefox", "Profiles", p.Name, "cache2")));
                    return t;
                };
                list.Add(r);
            }

            r = new Rule();
            r.Title = L.T("Uygulama önbellekleri (Discord, VS Code, Cursor…)", "App caches (Discord, VS Code, Cursor…)");
            r.Desc = L.T("Electron tabanlı uygulamaların Cache / Code Cache / GPUCache klasörleri. Ayarlara ve girişlere dokunulmaz.",
                         "Cache / Code Cache / GPUCache folders of Electron-based apps. Settings and logins are kept.");
            r.Risk = Risk.Safe;
            r.OpenPath = Sys.Roaming;
            r.Targets = ElectronCaches;
            list.Add(r);

            r = new Rule();
            r.Title = L.T("Geliştirici paket önbellekleri", "Developer package caches");
            r.Desc = L.T("pip, uv, npm, NuGet, Yarn indirme önbellekleri. Paket tekrar gerekirse yeniden indirilir.",
                         "pip, uv, npm, NuGet and Yarn download caches. Packages are downloaded again when needed.");
            r.Risk = Risk.Safe;
            r.OpenPath = Sys.Local;
            r.Targets = () => Kids(Lc("pip", "cache"), Lc("uv", "cache"), Lc("npm-cache"), Lc("NuGet", "v3-cache"), Lc("Yarn", "Cache"), Lc("pnpm-cache"));
            list.Add(r);

            if (Directory.Exists(Lc("NeoStack")))
            {
                r = new Rule();
                r.Title = L.T("NeoStack eski sürümleri", "Old NeoStack versions");
                r.Desc = L.T("NeoStack her güncellemede yenisini indirir ama eskisini silmez. En yeni sürüm kalır.",
                             "NeoStack downloads each update but never removes the old one. The newest version is kept.");
                r.Risk = Risk.Safe;
                r.OpenPath = Lc("NeoStack");
                r.Targets = () =>
                {
                    var t = new List<string>();
                    string rt = Lc("NeoStack", "DesktopV2", "runtimes");
                    foreach (var a in Native.List(Path.Combine(rt, "adapters")))
                        if (a.IsDir && !a.IsLink) t.AddRange(AllButNewest(Path.Combine(rt, "adapters", a.Name)));
                    t.AddRange(AllButNewest(Path.Combine(rt, "node")));
                    t.AddRange(AllButNewest(Path.Combine(rt, "bun")));
                    return t;
                };
                list.Add(r);
            }

            r = new Rule();
            r.Title = L.T("Unreal çökme raporları ve log'ları", "Unreal crash reports and logs");
            r.Desc = L.T("Projelerin Saved\\Crashes ve Saved\\Logs klasörleri. Autosaves'e dokunulmaz.",
                         "The Saved\\Crashes and Saved\\Logs folders of your projects. Autosaves are never touched.");
            r.Risk = Risk.Safe;
            r.Procs = UnrealProcs;
            r.Targets = () =>
            {
                var t = new List<string>();
                foreach (var p in UnrealProjects())
                    t.AddRange(Kids(Path.Combine(p, "Saved", "Crashes"), Path.Combine(p, "Saved", "Logs")));
                return t;
            };
            list.Add(r);

            r = new Rule();
            r.Title = L.T("Geri Dönüşüm Kutusu", "Recycle Bin");
            r.Desc = L.T("Sildiğin dosyalar burada yer kaplamaya devam eder. Boşaltınca geri getirilemez.",
                         "Files you deleted keep taking space here. Once emptied they can't be restored.");
            r.Risk = Risk.Minor;
            r.CustomSize = Native.RecycleBinSize;
            r.CustomRun = () => { Native.EmptyRecycleBin(); return null; };
            list.Add(r);

            r = new Rule();
            r.Title = L.T("Ekran kartı shader önbellekleri", "Graphics shader caches");
            r.Desc = L.T("NVIDIA / AMD / DirectX önbellekleri. Sınırsız büyüyebilir. Silince oyunlar ve Unreal ilk açılışta kısa süre takılır, sonra yeniden oluşur.",
                         "NVIDIA / AMD / DirectX caches. They can grow without limit. After clearing, games and Unreal stutter briefly on first start while it's rebuilt.");
            r.Risk = Risk.Minor;
            r.OpenPath = Lc("NVIDIA");
            r.Targets = () => Kids(Lc("NVIDIA", "DXCache"), Lc("NVIDIA", "GLCache"), Lc("D3DSCache"), Lc("AMD", "DxCache"), Lc("AMD", "DxcCache"),
                                   Lc("AMD", "GLCache"), Lc("AMD", "VkCache"), Path.Combine(Sys.LocalLow, "NVIDIA", "PerDriverVersion", "DXCache"));
            list.Add(r);

            r = new Rule();
            r.Title = L.T("Unreal ortak önbelleği", "Unreal shared cache");
            r.Desc = L.T("Bütün projelerin derlenmiş shader'ları (DerivedDataCache, Zen). Projeyi bir sonraki açışında shader'lar baştan derlenir; uzun sürer.",
                         "Compiled shaders of all projects (DerivedDataCache, Zen). Shaders are recompiled the next time you open a project; that takes a while.");
            r.Risk = Risk.Minor;
            r.Procs = new[] { "UnrealEditor", "UnrealEditor-Cmd", "zenserver", "ShaderCompileWorker", "UE4Editor" };
            r.OpenPath = Lc("UnrealEngine", "Common");
            r.Targets = () => Existing(Lc("UnrealEngine", "Common", "DerivedDataCache"), Lc("UnrealEngine", "Common", "Zen"));
            list.Add(r);

            r = new Rule();
            r.Title = L.T("Unreal proje önbellekleri", "Unreal project caches");
            r.Desc = L.T("Projelerin Intermediate ve DerivedDataCache klasörleri. İçerik ve ayarlar kalır; eklentilerin indirdikleri (ör. PipInstall) yeniden iner.",
                         "The Intermediate and DerivedDataCache folders of your projects. Content and settings stay; plugin downloads (e.g. PipInstall) are fetched again.");
            r.Risk = Risk.Minor;
            r.Procs = UnrealProcs;
            r.Targets = () =>
            {
                var t = new List<string>();
                foreach (var p in UnrealProjects())
                    t.AddRange(Existing(Path.Combine(p, "Intermediate"), Path.Combine(p, "DerivedDataCache")));
                return t;
            };
            list.Add(r);

            r = new Rule();
            r.Title = L.T("Hazırda bekletme dosyası", "Hibernation file");
            r.Desc = L.T("hiberfil.sys. Kapatınca hazırda bekletme ve Hızlı Başlangıç kapanır; açılış birkaç saniye uzayabilir. Yönetici izni sorulur.",
                         "hiberfil.sys. Turning it off disables hibernate and Fast Startup; booting may take a few seconds longer. Asks for admin rights.");
            r.Risk = Risk.Minor;
            r.CustomSize = () =>
            {
                foreach (var e in Native.List(Sys.SystemRoot))
                    if (e.Name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase)) return e.Size;
                return 0;
            };
            r.CustomRun = () =>
            {
                var psi = new ProcessStartInfo("powercfg.exe", "/hibernate off");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (var p = Process.Start(psi)) p.WaitForExit(30000);
                return null;
            };
            list.Add(r);

            string nv = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NVIDIA");
            r = new Rule();
            r.Title = L.T("NVIDIA ekran kayıtları", "NVIDIA recordings");
            r.Desc = L.T("NVIDIA App'in kaydettiği videolar (Alt+F9 ile yanlışlıkla başlayan kayıt tek gecede 15 GB olabilir). Kişisel dosyaların; önce bak.",
                         "Videos saved by the NVIDIA app (an accidental Alt+F9 recording can reach 15 GB in a night). These are your files; look first.");
            r.Risk = Risk.Careful;
            r.OpenPath = nv;
            r.Targets = () =>
            {
                var t = new List<string>();
                foreach (var d in Kids(nv))
                    if (Directory.Exists(d)) t.AddRange(Kids(d)); else t.Add(d);
                return t;
            };
            list.Add(r);

            string dl = Path.Combine(Sys.Home, "Downloads");
            r = new Rule();
            r.Title = L.T("İndirilenler: 90 günden eski", "Downloads older than 90 days");
            r.Desc = L.T("İndirilenler klasöründe 3 aydır değişmemiş dosyalar (eski kurulum dosyaları, zip'ler). Kişisel dosyaların; önce bak.",
                         "Files in Downloads that haven't changed for 3 months (old installers, zips). These are your files; look first.");
            r.Risk = Risk.Careful;
            r.OpenPath = dl;
            r.Targets = () => KidsOlder(90, dl);
            list.Add(r);

            return list;
        }

        static void AddBrowser(List<Rule> list, string name, string proc, string userData)
        {
            if (!Directory.Exists(userData)) return;
            var r = new Rule();
            r.Title = string.Format(L.T("{0} önbelleği", "{0} cache"), name);
            r.Desc = L.T("Sitelerin geçici kopyaları. Girişlerin, şifrelerin ve geçmişin silinmez.", "Temporary copies of websites. Logins, passwords and history are kept.");
            r.Risk = Risk.Safe;
            r.Procs = new[] { proc };
            r.OpenPath = userData;
            r.Targets = () =>
            {
                var t = new List<string>();
                foreach (var prof in Native.List(userData))
                {
                    if (!prof.IsDir || prof.IsLink) continue;
                    string p = Path.Combine(userData, prof.Name);
                    t.AddRange(Existing(Path.Combine(p, "Cache"), Path.Combine(p, "Code Cache"), Path.Combine(p, "GPUCache"),
                                        Path.Combine(p, "Service Worker", "CacheStorage"), Path.Combine(p, "Service Worker", "ScriptCache")));
                }
                t.AddRange(Existing(Path.Combine(userData, "ShaderCache"), Path.Combine(userData, "GrShaderCache"), Path.Combine(userData, "component_crx_cache")));
                return t;
            };
            list.Add(r);
        }

        // Roaming ve Local altında Cache + GPUCache klasörü olan uygulamalar (Electron); tarayıcılar ayrı ele alınır
        static List<string> ElectronCaches()
        {
            var t = new List<string>();
            foreach (var root in new[] { Sys.Roaming, Sys.Local })
                foreach (var app in Native.List(root))
                {
                    if (!app.IsDir || app.IsLink) continue;
                    string n = app.Name.ToLowerInvariant();
                    if (n == "google" || n == "microsoft" || n == "bravesoftware" || n == "mozilla" || n == "opera software" || n == "packages") continue;
                    string p = Path.Combine(root, app.Name);
                    if (!Directory.Exists(Path.Combine(p, "GPUCache"))) continue;
                    t.AddRange(Existing(Path.Combine(p, "Cache"), Path.Combine(p, "Code Cache"), Path.Combine(p, "GPUCache"),
                                        Path.Combine(p, "Service Worker", "CacheStorage"), Path.Combine(p, "DawnCache"), Path.Combine(p, "DawnGraphiteCache")));
                }
            return t;
        }
    }
}
