# Security Policy · Güvenlik Politikası

## Supported versions · Desteklenen sürümler

Only the [latest release](https://github.com/HErkanuzun/BrokenSpaceFinder/releases/latest) receives fixes.
Düzeltmeler yalnızca [son sürüme](https://github.com/HErkanuzun/BrokenSpaceFinder/releases/latest) gelir.

## Reporting a vulnerability · Güvenlik açığı bildirme

Please **do not** open a public issue for security problems. Report them privately on GitHub:
**Security → Report a vulnerability** ([link](https://github.com/HErkanuzun/BrokenSpaceFinder/security/advisories/new)).

Güvenlik sorunları için lütfen herkese açık bir konu (issue) **açma**. GitHub'da gizli olarak bildir:
**Security → Report a vulnerability** ([bağlantı](https://github.com/HErkanuzun/BrokenSpaceFinder/security/advisories/new)).

## What the app does on your computer · Program bilgisayarında ne yapar

| | |
|---|---|
| Administrator rights · Yönetici izni | Not by default. Only when you press "Run as administrator" (the app restarts elevated) or choose an item that needs it (turning off hibernation runs `powercfg /hibernate off` with a UAC prompt). · Varsayılan olarak hayır. Sadece "Yönetici olarak aç"a basınca ya da gerektiren bir seçeneği seçince (hazırda bekletmeyi kapatmak UAC onayıyla `powercfg /hibernate off` çalıştırır). |
| Reading · Okuma | Scans only list folders and read file sizes and dates. File contents are never read. Programs' last-use dates come from the registry (`HKCU\…\Explorer\UserAssist`, read-only) and `C:\Windows\Prefetch` file dates. · Tarama sadece klasörleri listeler, dosya boyutu ve tarihlerini okur; dosya içerikleri hiç okunmaz. Son kullanım kayıt defterinden (salt okunur) ve Prefetch dosyalarının tarihlerinden gelir. |
| Deleting · Silme | Only after you confirm, and only the listed paths. Windows, Program Files, system areas and main user folders are refused. Junctions/symlinks are removed as links, never followed. Files in use are skipped. · Yalnızca onayından sonra ve yalnızca listelenen yollar. Windows, Program Files, sistem alanları ve ana kullanıcı klasörleri reddedilir. Bağlantı klasörlerinin hedefine dokunulmaz. Kullanımdaki dosyalar atlanır. |
| Uninstalling · Kaldırma | "Uninstall…" starts the program's own registered uninstaller; the app doesn't remove programs itself. · Programın kayıtlı kendi kaldırma aracını açar; program kendisi kaldırmaz. |
| Network · Ağ | Only when you press "Check for updates" in About: one request to the GitHub API. No telemetry. · Yalnızca Hakkında'da "Güncellemeleri denetle"ye basınca GitHub API'sine tek istek. Veri toplamaz. |
| Files · Dosyalar | `%AppData%\BrokenSpaceFinder\settings.txt` (language, window position, last tab and drive). · Dil, pencere konumu, son sekme ve sürücü. |
| Registry · Kayıt defteri | Writes nothing except the installer's own uninstall entry. · Kurulum paketinin kaldırma kaydı dışında hiçbir şey yazmaz. |

Uninstalling from Windows "Apps" removes the program and asks whether to delete `%AppData%\BrokenSpaceFinder`.
Windows "Uygulamalar"dan kaldırınca program silinir ve ayar klasörünün de silinip silinmeyeceği sorulur.

## Verifying releases · Sürümleri doğrulama

Release files are built from this repository by GitHub Actions. Each release includes `SHA256SUMS.txt`:

```powershell
Get-FileHash .\BrokenSpaceFinder-1.0-Setup.exe -Algorithm SHA256
```
