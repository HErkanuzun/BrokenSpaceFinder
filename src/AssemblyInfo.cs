using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// exe'nin "Özellikler > Ayrıntılar" bilgileri
[assembly: AssemblyTitle("Broken Space Finder")]
[assembly: AssemblyDescription("Finds what is filling up your disk and cleans it safely")]
[assembly: AssemblyCompany("HErkanuzun")]
[assembly: AssemblyProduct("Broken Space Finder")]
[assembly: AssemblyCopyright("Copyright (c) 2026 HErkanuzun. MIT License.")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1")]
[assembly: NeutralResourcesLanguage("en")]
[assembly: ComVisible(false)]

namespace BrokenSpaceFinder
{
    static class AppInfo
    {
        public const string Name = "Broken Space Finder";
        public const string Version = "1.1";
        public const string Repo = "https://github.com/HErkanuzun/BrokenSpaceFinder";
    }
}
