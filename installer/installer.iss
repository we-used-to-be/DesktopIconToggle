; Inno Setup script for DesktopIconToggle
; ---------------------------------------------------------------------------
; Build from the repository root:
;   iscc "installer\installer.iss" /DMyAppVersion=2.1.0 /DMyAppSourceDir="C:\path\to\portable-exe"
;
; MyAppSourceDir must contain the self-contained single-file executable named
; "DesktopIconToggle-win_x64-Portable.exe" (the PORTABLE artifact).
; The installer ships the self-contained build so end users do not need the
; .NET 8 Desktop Runtime installed.
; ---------------------------------------------------------------------------

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#ifndef MyAppSourceDir
  #define MyAppSourceDir "..\artifacts\portable-x64"
#endif

#define MyAppName "DesktopIconToggle"
#define MyAppPublisher "Hexandcube"
#define MyAppURL "https://github.com/hexandcube/DesktopIconToggle"
#define MyAppExeName "DesktopIconToggle-win_x64-Portable.exe"

; Matches the single-instance mutex created in Program.cs.
; Inno Setup probes both the session and the Global\ namespace, so the
; "Global\" prefix used by the application is intentionally omitted here.
#define MyAppMutex "DesktopIconToggle_370A329E-902E-4B19-A164-34D43A1F5014"

[Setup]
AppId={{13D0EE67-4E72-4775-94E9-DDAFD00FCCB5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=auto

; The bundled executable is x64 (it also runs on ARM64 Windows via emulation).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Ask the user to close a running instance before (un)installing it.
AppMutex={#MyAppMutex}
CloseApplications=auto
RestartApplications=no

SetupIconFile=..\active.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
WizardStyle=modern
WizardResizable=no

Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

OutputDir=..\artifacts
OutputBaseFilename=DesktopIconToggle-Setup

PrivilegesRequiredOverridesAllowed=dialog
; Keep the {group} name stable across 32/64 bit and language variants.
UsePreviousGroup=yes
UsePreviousAppDir=yes
AllowNoIcons=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Additional options:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional options:"; Flags: unchecked

[Files]
Source: "{#MyAppSourceDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; HKA resolves to HKLM when installing elevated and to HKCU otherwise, so the
; auto-start entry always lands on the account that will actually use the app.
Root: HKA; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; \
    Tasks: autostart; Flags: uninsdeletevalue

[Run]
; skipifsilent keeps package managers (winget / choco / /VERYSILENT) from
; launching the app as the installing (possibly elevated) user.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; \
    Flags: nowait postinstall skipifsilent runascurrentuser
