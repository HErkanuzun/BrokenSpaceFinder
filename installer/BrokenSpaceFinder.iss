; Broken Space Finder - kurulum paketi (Inno Setup 6)
; Derleme: paketle.bat  (ya da: ISCC.exe /DAppVersion=1.0 installer\BrokenSpaceFinder.iss, önce build.bat)
;
; - Program normal kullanıcı olarak çalışır; kurulum Program Files'a yapıldığı için kurulum paketi yönetici izni ister.
; - Program kuruluysa aynı yere üzerine kurar (güncelleme); çalışan kopya önce düzgünce kapatılır.
; - Windows "Uygulamalar" listesinde sürümüyle görünür. Kaldırırken ayarların da silinip silinmeyeceği sorulur.

#ifndef AppVersion
  #define AppVersion "1.0"
#endif
#define AppName "Broken Space Finder"
#define AppExe "BrokenSpaceFinder.exe"
#define AppUrl "https://github.com/HErkanuzun/BrokenSpaceFinder"

[Setup]
; AppId hiç değişmemeli: güncellemenin eski kurulumu bulması buna bağlı
AppId={{9F55E652-A631-470B-8E88-F40B25CCECA9}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=HErkanuzun
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 HErkanuzun. MIT License.
VersionInfoVersion={#AppVersion}.0.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=BrokenSpaceFinder-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
turkish.DeleteData=Ayarlar da silinsin mi?%n%n(%1)
english.DeleteData=Also delete the settings?%n%n(%1)
turkish.Warning=Kalıcı silinen şeyler geri getirilemez. Emin olmadığın bir şeyi silmeden önce "Explorer'da göster" ile içine bak.
english.Warning=Things deleted permanently can't be restored. Before deleting something you're not sure about, use "Show in Explorer" to look inside.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\packaging\README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Açık kopya varsa önce düzgünce kapatılır
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: waituntilterminated runhidden; RunOnceId: "CloseApp"

[Code]
// Dosyalar değiştirilmeden önce çalışan kopya düzgünce kapatılır:
// yeni exe geçici klasörden "--exit" ile çalıştırılır.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  ExtractTemporaryFile('{#AppExe}');
  Exec(ExpandConstant('{tmp}\{#AppExe}'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + CustomMessage('Warning');
end;

// Kaldırma bitince kullanıcı verisi sorulur (sessiz kaldırmada dokunulmaz)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\BrokenSpaceFinder');
    if DirExists(DataDir) and not UninstallSilent then
      if MsgBox(FmtMessage(CustomMessage('DeleteData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
