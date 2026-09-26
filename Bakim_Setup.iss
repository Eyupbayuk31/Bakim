; =====================================================================
; Bakım - Windows System Optimizer & Maintenance Tool
; Inno Setup 6 Script - Modern Fluent Installer
; =====================================================================

#define MyAppName "Bakım"
; Sürüm iş akışı /DMyAppVersion=X.Y.Z verir. Yerel derleme varsayılanı
; Directory.Build.props içindeki BakimVersion ile aynı olmalı (Core testi denetler).
#ifndef MyAppVersion
  #define MyAppVersion "4.4.0"
#endif
#define MyAppPublisher "Eyüp"
#define MyAppURL "https://github.com/Eyupbayuk31/Bakim"
#define MyAppExeName "Bakim.exe"
#define MyShellExeName "BakimShell.exe"

[Setup]
; Benzersiz GUID kimliği
AppId={{D8E5F678-31A9-4B5C-8D12-9A4E2B5C6D7E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Releases
OutputBaseFilename=Bakim-v{#MyAppVersion}-Setup
SetupIconFile=Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} v{#MyAppVersion} (Kaldır)
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Masaüstü ve Menü Kısayolları (Varsayılan olarak seçili)
Name: "desktopicon"; Description: "Masaüstü simgesi oluştur"; GroupDescription: "Kısayollar:"
Name: "startmenu"; Description: "Başlat Menüsü simgesi oluştur"; GroupDescription: "Kısayollar:"

[Files]
Source: "Releases\Bakim.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Releases\BakimShell.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{group}\{#MyAppName} (Kaldır)"; Filename: "{uninstallexe}"; Tasks: startmenu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Her zaman yönetici olarak çalıştırma kaydı (HKLM & HKCU AppCompatFlags - Otomatik & Koşulsuz)
; NOT: BakimShell.exe UAC olmadan Explorer'dan çalışması gerektiği için buraya EKLENMEZ (asInvoker).
Root: HKLM; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"; ValueType: string; ValueName: "{app}\{#MyAppExeName}"; ValueData: "~ RUNASADMIN"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"; ValueType: string; ValueName: "{app}\{#MyAppExeName}"; ValueData: "~ RUNASADMIN"; Flags: uninsdeletevalue

; Sağ Tık Menüsü Entegrasyonu ("Bakım ile Kaldır" - Otomatik & Koşulsuz, BakimShell asInvoker üzerinden UAC'siz)
; 1. HKCR (Tüm Sistem Dosya ve Klasörleri)
Root: HKCR; Subkey: "lnkfile\shell\BakimUninstall"; ValueType: string; ValueName: ""; ValueData: "Bakım ile Kaldır"; Flags: uninsdeletekey
Root: HKCR; Subkey: "lnkfile\shell\BakimUninstall"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName},0"; Flags: uninsdeletekey
Root: HKCR; Subkey: "lnkfile\shell\BakimUninstall\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyShellExeName}"" --uninstall-target ""%1"""; Flags: uninsdeletekey

Root: HKCR; Subkey: "exefile\shell\BakimUninstall"; ValueType: string; ValueName: ""; ValueData: "Bakım ile Kaldır"; Flags: uninsdeletekey
Root: HKCR; Subkey: "exefile\shell\BakimUninstall"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName},0"; Flags: uninsdeletekey
Root: HKCR; Subkey: "exefile\shell\BakimUninstall\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyShellExeName}"" --uninstall-target ""%1"""; Flags: uninsdeletekey

Root: HKCR; Subkey: "Directory\shell\BakimUninstall"; ValueType: string; ValueName: ""; ValueData: "Bakım ile Kaldır"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\shell\BakimUninstall"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName},0"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\shell\BakimUninstall\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyShellExeName}"" --uninstall-target ""%1"""; Flags: uninsdeletekey

; 2. HKCU Classes (Mevcut Kullanıcı Kabuğu Garantisi)
Root: HKCU; Subkey: "Software\Classes\lnkfile\shell\BakimUninstall"; ValueType: string; ValueName: ""; ValueData: "Bakım ile Kaldır"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\lnkfile\shell\BakimUninstall"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName},0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\lnkfile\shell\BakimUninstall\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyShellExeName}"" --uninstall-target ""%1"""; Flags: uninsdeletekey

Root: HKCU; Subkey: "Software\Classes\exefile\shell\BakimUninstall"; ValueType: string; ValueName: ""; ValueData: "Bakım ile Kaldır"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\exefile\shell\BakimUninstall"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName},0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\exefile\shell\BakimUninstall\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyShellExeName}"" --uninstall-target ""%1"""; Flags: uninsdeletekey

Root: HKCU; Subkey: "Software\Classes\Directory\shell\BakimUninstall"; ValueType: string; ValueName: ""; ValueData: "Bakım ile Kaldır"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\BakimUninstall"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName},0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\BakimUninstall\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyShellExeName}"" --uninstall-target ""%1"""; Flags: uninsdeletekey

[Run]
; Sağ tık "Bakım ile Kaldır" menüsünün sisteme otomatik ve koşulsuz kaydedilmesi
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-contextmenu"; Flags: runhidden

; Bilgisayar açılışında UAC istemi olmadan en yüksek yönetici yetkisiyle başlatma (Task Scheduler - Otomatik Kayıt)
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-autostart"; Flags: runhidden

; Kurulum tamamlandıktan sonra uygulamayı başlatma seçeneği (shellexec ile Hata 740 önlenir, sessiz güncellemede otomatik açılır)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall shellexec

[UninstallRun]
; Kaldırma esnasında Görev Zamanlayıcı ve Sağ Tık kaydını temizle
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-contextmenu"; Flags: runhidden; RunOnceId: "UnregisterBakimContextMenu"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-autostart"; Flags: runhidden; RunOnceId: "DeleteBakimAutoStartTask"
Filename: "schtasks.exe"; Parameters: "/delete /tn ""Bakım_Admin_AutoStart"" /f"; Flags: runhidden; RunOnceId: "DeleteBakimAutoStartTaskFallback"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// Kurulum öncesi açık çalışan Bakım süreçlerini denetle ve kapat
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  // Gerekirse çalışan eski süreç kapatılır
  Exec('taskkill.exe', '/f /im Bakim.exe /im Bakım.exe', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
end;
