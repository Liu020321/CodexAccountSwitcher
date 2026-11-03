#define AppVersion "7.2.7"
#ifndef PayloadDir
  #error PayloadDir must be supplied
#endif
#ifndef OutputDirPath
  #error OutputDirPath must be supplied
#endif
[Setup]
AppId={{B47DC62D-BA22-4FDB-ACD1-032C71EA27D5}
AppName=Codex Account Switcher
AppVersion={#AppVersion}
AppPublisher=Liu020321
AppPublisherURL=https://github.com/Liu020321/CodexAccountSwitcher
DefaultDirName={localappdata}\Programs\CodexAccountSwitcher
DefaultGroupName=Codex Account Switcher
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDirPath}
OutputBaseFilename=CodexSettings-Setup-v{#AppVersion}
SetupIconFile=app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\CodexSettings.exe
CloseApplications=no
RestartApplications=no
[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked
[Files]
Source: "{#PayloadDir}\CodexSettings.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\CodexSettings.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\使用说明.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Inno-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\SHA256SUMS.txt"; DestDir: "{app}"; Flags: ignoreversion
[Dirs]
Name: "{app}\accounts\life"
Name: "{app}\accounts\work"
[Icons]
Name: "{group}\Codex Account Switcher"; Filename: "{app}\CodexSettings.exe"
Name: "{autodesktop}\Codex Account Switcher"; Filename: "{app}\CodexSettings.exe"; Tasks: desktopicon
; User accounts, settings, logs and unfinished journals are intentionally not uninstall targets.
