using System;
using System.IO;

namespace BrokenSpaceFinder
{
    // Basit ayar dosyası: %AppData%\BrokenSpaceFinder\settings.txt (anahtar=değer)
    class Settings
    {
        public string Lang = "";
        public string Drive = "";
        public int Tab = 0;
        public bool Welcomed = false;
        public string Window = "";      // x,y,w,h,maximized

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrokenSpaceFinder"); }
        }

        static string FilePath
        {
            get
            {
                Directory.CreateDirectory(Folder);
                return Path.Combine(Folder, "settings.txt");
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    int n;
                    switch (k)
                    {
                        case "lang": s.Lang = v; break;
                        case "drive": s.Drive = v; break;
                        case "tab": if (int.TryParse(v, out n)) s.Tab = Math.Max(0, Math.Min(3, n)); break;
                        case "welcomed": s.Welcomed = v == "1"; break;
                        case "window": s.Window = v; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "lang=" + Lang,
                    "drive=" + Drive,
                    "tab=" + Tab,
                    "welcomed=" + (Welcomed ? "1" : "0"),
                    "window=" + Window,
                });
            }
            catch { }
        }
    }
}
