; =====================================================================
; Bakım - Windows System Optimizer & Maintenance Tool
; Inno Setup 6 Script - Modern Fluent Installer
; =====================================================================

#define MyAppName "Bakım"
#define MyAppVersion "2.7.8"
#define MyAppPublisher "Eyüp"
#define MyAppURL "https://github.com/Eyupbayuk31/Bakim"
#define MyAppExeName "Bakim.exe"

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
; Masaüstü ve Menü Kısayolları
Name: "desktopicon"; Description: "Masaüstü simgesi oluştur"; GroupDescription: "Kısayollar:"
Name: "startmenu"; Description: "Başlat Menüsü simgesi oluştur"; GroupDescription: "Kısayollar:"

; Yönetici Hakları & Başlangıç Otomasyonu (İstenen Özellikler)
Name: "alwaysadmin"; Description: "Her zaman Yönetici Olarak Çalıştır (RUNASADMIN Uyumluluk Katmanı)"; GroupDescription: "Yönetici ve Sistem Entegrasyonu:"; Flags: checkedonce
Name: "autostart"; Description: "Bilgisayar açıldığında otomatik başlat (En Yüksek Yönetici Yetkisiyle - UAC Uyarısız)"; GroupDescription: "Yönetici ve Sistem Entegrasyonu:"; Flags: unchecked

[Files]
Source: "Releases\Bakim.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{group}\{#MyAppName} (Kaldır)"; Filename: "{uninstallexe}"; Tasks: startmenu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Her zaman yönetici olarak çalıştırma kaydı (HKLM & HKCU AppCompatFlags)
Root: HKLM; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"; ValueType: string; ValueName: "{app}\{#MyAppExeName}"; ValueData: "~ RUNASADMIN"; Flags: uninsdeletevalue; Tasks: alwaysadmin
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"; ValueType: string; ValueName: "{app}\{#MyAppExeName}"; ValueData: "~ RUNASADMIN"; Flags: uninsdeletevalue; Tasks: alwaysadmin

[Run]
; Bilgisayar açılışında UAC istemi olmadan en yüksek yönetici yetkisiyle başlatma (Task Scheduler)
Filename: "schtasks.exe"; Parameters: "/create /tn ""Bakım_Admin_AutoStart"" /tr """"{app}\{#MyAppExeName}"""" /sc onlogon /rl highest /f"; Flags: runhidden; Tasks: autostart

; Kurulum tamamlandıktan sonra uygulamayı başlatma seçeneği (shellexec ile Hata 740 önlenir, sessiz güncellemede otomatik açılır)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall shellexec

[UninstallRun]
; Kaldırma esnasında Görev Zamanlayıcı kaydını temizle
Filename: "schtasks.exe"; Parameters: "/delete /tn ""Bakım_Admin_AutoStart"" /f"; Flags: runhidden; RunOnceId: "DeleteBakimAutoStartTask"

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
