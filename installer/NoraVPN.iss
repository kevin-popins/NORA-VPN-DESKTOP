#ifndef PayloadDir
  #error PayloadDir must point to the verified Windows publish directory
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0-pre.10"
#endif
#ifndef InstallerVersion
  #define InstallerVersion "0.1.0.10"
#endif
#ifndef BuildId
  #define BuildId "local"
#endif
#ifndef InstallerBackground
  #error InstallerBackground must point to the generated NORA wizard background
#endif
#ifndef InstallerLogo
  #error InstallerLogo must point to the generated canonical NORA logo PNG
#endif

#ifdef VisualQaMode
  #define ProductAppId "8A238707-9865-442C-8862-A39D3ED80518"
  #define SetupPrivileges "lowest"
  #define UninstallRegistryRoot "HKCU"
  #define ProductRegistryRoot "HKCU"
  #define UserDataDirName "NORA VPN Installer Data QA"
  #define SingleInstanceMutexName "Local\NORA.VPN.Desktop.InstallerQA.v1"
  #define UrlSchemeName "noravpn-installer-qa"
#else
  #define ProductAppId "7CBB6718-2B47-4EFC-9DE7-726C494E611A"
  #define SetupPrivileges "admin"
  #define UninstallRegistryRoot "HKLM64"
  #define ProductRegistryRoot "HKLM64"
  #define UserDataDirName "NORA VPN"
  #define SingleInstanceMutexName "Local\NORA.VPN.Desktop.SingleInstance.v1"
  #define UrlSchemeName "noravpn"
#endif
#define UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{" + ProductAppId + "}_is1"

[Setup]
AppId={{{#ProductAppId}}
AppName=NORA VPN
AppVersion={#AppVersion}
AppVerName=NORA VPN {#AppVersion}
AppPublisher=NORA VPN contributors
AppPublisherURL=https://github.com/kevin-popins/NORA-VPN-DESKTOP
AppSupportURL=https://github.com/kevin-popins/NORA-VPN-DESKTOP
AppUpdatesURL=https://github.com/kevin-popins/NORA-VPN-DESKTOP/releases
AppCopyright=Copyright 2026 NORA VPN contributors
DefaultDirName={autopf}\NORA VPN
DefaultGroupName=NORA VPN
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=auto
DisableReadyPage=no
DisableFinishedPage=no
AllowNoIcons=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousLanguage=yes
UsePreviousTasks=yes
PrivilegesRequired={#SetupPrivileges}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no
RestartApplications=no
SetupLogging=yes
Uninstallable=yes
UninstallDisplayName=NORA VPN
UninstallDisplayIcon={app}\NoraVPN.exe
OutputDir={#OutputDir}
OutputBaseFilename=NORA-VPN-Setup-v{#AppVersion}-win-x64
SetupIconFile=..\assets\logo.ico
VersionInfoVersion={#InstallerVersion}
VersionInfoProductVersion={#InstallerVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany=NORA VPN contributors
VersionInfoDescription=NORA VPN Installer
VersionInfoProductName=NORA VPN
VersionInfoOriginalFileName=NORA-VPN-Setup.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dark includetitlebar hidebevels
WizardSizePercent=135,130
WizardKeepAspectRatio=yes
WizardBackColor=#070A0F
WizardBackImageFile={#InstallerBackground}
WizardBackImageOpacity=255
WizardImageFile=
WizardSmallImageFile={#InstallerLogo}
WizardSmallImageBackColor=#070A0F
DefaultDialogFontName=Segoe UI
LicenseFile=..\LICENSE
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "USAGE_NOTICES.en.txt"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"; InfoBeforeFile: "USAGE_NOTICES.ru.txt"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
english.WelcomeInstall=Install NORA VPN
english.WelcomeUpdate=Update NORA VPN
english.WelcomeInstallBody=Set up the NORA VPN client and its required runtime components.%n%nSubscriptions, selected servers and preferences are stored separately and remain untouched by installation and updates.
english.WelcomeUpdateBody=Your installed NORA VPN will be updated in place.%n%nSubscriptions, selected servers and preferences remain untouched.
english.UpdateVersions=Installed version: %1%nNew version: %2
english.UpdateAction=Update NORA VPN
english.UpdateReady=Ready to update NORA VPN
english.UpdateProgress=Updating NORA VPN
english.UpdateStageVerify=Verifying the update package…
english.UpdateStageCopy=Replacing NORA VPN components…
english.UpdateStageShortcuts=Refreshing application shortcuts…
english.UpdateStageFinish=Finishing and verifying the update…
english.UpdateFinishText=NORA VPN was updated successfully. Your subscriptions, selected server and settings were preserved.
english.RunningPrompt=NORA VPN is currently running. To update its files, NORA must safely disconnect and exit.%n%nContinue with a graceful shutdown and update now?
english.RunningSilent=NORA VPN is running. Silent setup will not stop an active VPN connection. Exit NORA from the system tray and run the update again.
english.ShutdownFailed=NORA could not finish its safe shutdown. No files were replaced. Exit NORA from the system tray and try again.
english.RunningUninstallPrompt=NORA VPN is currently running. To remove its files, NORA must safely disconnect and exit.%n%nContinue with a graceful shutdown and uninstall now?
english.RunningUninstallSilent=NORA VPN is running. Silent uninstall will not stop an active VPN connection. Exit NORA from the system tray and run uninstall again.
english.DowngradeBlocked=A newer version of NORA VPN is already installed. Downgrade is blocked to protect the application and user data.
english.InstallStageVerify=Verifying the NORA package…
english.InstallStageCopy=Installing NORA VPN components…
english.InstallStageShortcuts=Creating application shortcuts…
english.InstallStageFinish=Finishing and verifying the installation…
english.PurgePrompt=Also permanently delete this Windows user's subscriptions, profiles, saved server credentials and settings?%n%nChoose No to keep all personal data for a future reinstall.
english.DataKept=Personal data is kept in %%LocalAppData%%\NORA VPN by default.
english.FinishText=NORA VPN is installed and ready. Your existing subscriptions, selected server and settings were preserved.
russian.DesktopShortcut=Создать ярлык на рабочем столе
russian.WelcomeInstall=Установите NORA VPN
russian.WelcomeUpdate=Обновление NORA VPN
russian.WelcomeInstallBody=Установщик подготовит клиент NORA VPN и необходимые компоненты.%n%nПодписки, выбранные серверы и настройки хранятся отдельно и не изменяются при установке и обновлениях.
russian.WelcomeUpdateBody=Установленная NORA VPN будет обновлена поверх текущей версии.%n%nПодписки, выбранные серверы и настройки останутся на месте.
russian.UpdateVersions=Установленная версия: %1%nНовая версия: %2
russian.UpdateAction=Обновить NORA VPN
russian.UpdateReady=Всё готово к обновлению NORA VPN
russian.UpdateProgress=Обновляем NORA VPN
russian.UpdateStageVerify=Проверяем пакет обновления…
russian.UpdateStageCopy=Обновляем компоненты NORA VPN…
russian.UpdateStageShortcuts=Обновляем ярлыки приложения…
russian.UpdateStageFinish=Завершаем и проверяем обновление…
russian.UpdateFinishText=NORA VPN успешно обновлена. Подписки, выбранный сервер и настройки сохранены.
russian.RunningPrompt=NORA VPN сейчас работает. Для обновления файлов приложение должно безопасно отключить VPN и завершить работу.%n%nПродолжить безопасное завершение и обновление?
russian.RunningSilent=NORA VPN запущена. Тихая установка не будет останавливать активное VPN-подключение. Завершите NORA через системный трей и повторите обновление.
russian.ShutdownFailed=NORA не смогла безопасно завершить работу. Файлы не заменялись. Завершите NORA через системный трей и повторите попытку.
russian.RunningUninstallPrompt=NORA VPN сейчас работает. Для удаления файлов приложение должно безопасно отключить VPN и завершить работу.%n%nПродолжить безопасное завершение и удаление?
russian.RunningUninstallSilent=NORA VPN запущена. Тихое удаление не будет останавливать активное VPN-подключение. Завершите NORA через системный трей и повторите удаление.
russian.DowngradeBlocked=Уже установлена более новая версия NORA VPN. Понижение версии заблокировано для защиты приложения и пользовательских данных.
russian.InstallStageVerify=Проверяем пакет NORA…
russian.InstallStageCopy=Устанавливаем компоненты NORA VPN…
russian.InstallStageShortcuts=Настраиваем ярлыки приложения…
russian.InstallStageFinish=Завершаем и проверяем установку…
russian.PurgePrompt=Также навсегда удалить подписки, профили, сохранённые реквизиты серверов и настройки этого пользователя Windows?%n%nВыберите «Нет», чтобы сохранить все личные данные для будущей переустановки.
russian.DataKept=По умолчанию личные данные сохраняются в %%LocalAppData%%\NORA VPN.
russian.FinishText=NORA VPN установлена и готова к работе. Существующие подписки, выбранный сервер и настройки сохранены.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "client-profile.json,profiles\*"
Source: "..\LICENSE"; DestDir: "{app}\legal"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}\legal"; DestName: "NOTICE.txt"; Flags: ignoreversion
Source: "..\TRADEMARKS.md"; DestDir: "{app}\legal"; Flags: ignoreversion
Source: "..\docs\THIRD_PARTY.md"; DestDir: "{app}\legal"; Flags: ignoreversion
Source: "..\assets\emoji\ATTRIBUTION.md"; DestDir: "{app}\legal"; DestName: "EMOJI_ATTRIBUTION.md"; Flags: ignoreversion
Source: "..\assets\flags\ATTRIBUTION.md"; DestDir: "{app}\legal"; DestName: "FLAG_ATTRIBUTION.md"; Flags: ignoreversion
Source: "USAGE_NOTICES.en.txt"; DestDir: "{app}\legal"; Flags: ignoreversion
Source: "USAGE_NOTICES.ru.txt"; DestDir: "{app}\legal"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\NORA VPN\NORA VPN"; Filename: "{app}\NoraVPN.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\logo.ico"
Name: "{autodesktop}\NORA VPN"; Filename: "{app}\NoraVPN.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\logo.ico"; Tasks: desktopicon

[Registry]
Root: {#ProductRegistryRoot}; Subkey: "Software\NORA VPN"; ValueType: string; ValueName: "InstallerVersion"; ValueData: "{#InstallerVersion}"; Flags: uninsdeletekey
Root: {#ProductRegistryRoot}; Subkey: "Software\NORA VPN"; ValueType: string; ValueName: "BuildId"; ValueData: "{#BuildId}"; Flags: uninsdeletekey
Root: {#ProductRegistryRoot}; Subkey: "Software\Classes\{#UrlSchemeName}"; ValueType: string; ValueName: ""; ValueData: "URL:NORA VPN Import Protocol"; Flags: uninsdeletekey
Root: {#ProductRegistryRoot}; Subkey: "Software\Classes\{#UrlSchemeName}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: {#ProductRegistryRoot}; Subkey: "Software\Classes\{#UrlSchemeName}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\NoraVPN.exe,0"
Root: {#ProductRegistryRoot}; Subkey: "Software\Classes\{#UrlSchemeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\NoraVPN.exe"" ""%1"""

[Run]
Filename: "{app}\NoraVPN.exe"; Description: "{cm:LaunchProgram,NORA VPN}"; WorkingDir: "{app}"; Verb: "runas"; Flags: postinstall skipifsilent shellexec runasoriginaluser

[Code]
var
  IsUpgradeInstall: Boolean;
  PriorDisplayVersion: String;
  PurgeUserData: Boolean;
  NoraFinishButton: TNewButton;
#ifdef VisualQaScreenshotMode
  VisualQaTarget: String;
  VisualQaPage: Integer;
#endif

procedure NoraFinishButtonClick(Sender: TObject);
begin
  WizardForm.NextButton.OnClick(WizardForm.NextButton);
end;

#ifdef VisualQaScreenshotMode
const
  BmClick = $00F5;
function PostMessage(hWnd: HWND; Msg: UINT; wParam: Longint; lParam: Longint): Boolean;
  external 'PostMessageW@user32.dll stdcall';
#endif

function InstalledVersion(): String;
begin
  Result := '';
  RegQueryStringValue({#UninstallRegistryRoot}, '{#UninstallKey}', 'DisplayVersion', Result);
end;

function InstalledNumericVersion(): String;
begin
  Result := '';
  RegQueryStringValue({#ProductRegistryRoot}, 'Software\NORA VPN', 'InstallerVersion', Result);
end;

function NextVersionPart(var Version: String): Integer;
var
  Separator: Integer;
  Part: String;
begin
  Separator := Pos('.', Version);
  if Separator = 0 then
  begin
    Part := Version;
    Version := '';
  end
  else
  begin
    Part := Copy(Version, 1, Separator - 1);
    Delete(Version, 1, Separator);
  end;
  Result := StrToIntDef(Part, 0);
end;

function CompareNumericVersions(Left, Right: String): Integer;
var
  Index, LeftPart, RightPart: Integer;
begin
  Result := 0;
  for Index := 1 to 4 do
  begin
    LeftPart := NextVersionPart(Left);
    RightPart := NextVersionPart(Right);
    if LeftPart < RightPart then
    begin
      Result := -1;
      exit;
    end;
    if LeftPart > RightPart then
    begin
      Result := 1;
      exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  PriorNumericVersion: String;
begin
  PriorDisplayVersion := InstalledVersion();
  IsUpgradeInstall := PriorDisplayVersion <> '';
  PriorNumericVersion := InstalledNumericVersion();
  Result := (PriorNumericVersion = '') or
    (CompareNumericVersions(PriorNumericVersion, '{#InstallerVersion}') <= 0);
  if not Result then
    MsgBox(CustomMessage('DowngradeBlocked'), mbError, MB_OK);
end;

procedure InitializeWizard();
begin
  WizardForm.Color := $000F0A07;
  WizardForm.WelcomeLabel1.Font.Size := 24;
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];
  WizardForm.WelcomeLabel2.Font.Size := 11;
  WizardForm.WelcomeLabel2.AutoSize := False;
  WizardForm.WelcomeLabel2.Height := ScaleY(220);
  WizardForm.NextButton.Caption := SetupMessage(msgButtonNext);

  NoraFinishButton := TNewButton.Create(WizardForm);
  NoraFinishButton.Parent := WizardForm;
  NoraFinishButton.SetBounds(WizardForm.NextButton.Left, WizardForm.NextButton.Top,
    WizardForm.NextButton.Width, WizardForm.NextButton.Height);
  NoraFinishButton.Caption := SetupMessage(msgButtonFinish);
  NoraFinishButton.OnClick := @NoraFinishButtonClick;
  NoraFinishButton.Visible := False;

  if IsUpgradeInstall then
  begin
    WizardForm.WelcomeLabel1.Caption := CustomMessage('WelcomeUpdate');
    WizardForm.WelcomeLabel2.Caption := FmtMessage(CustomMessage('UpdateVersions'), [PriorDisplayVersion, '{#AppVersion}']) + #13#10#13#10 + CustomMessage('WelcomeUpdateBody');
    WizardForm.NextButton.Width := ScaleX(150);
    WizardForm.NextButton.Left := WizardForm.CancelButton.Left - ScaleX(10) - WizardForm.NextButton.Width;
    WizardForm.BackButton.Left := WizardForm.NextButton.Left - ScaleX(10) - WizardForm.BackButton.Width;
    WizardForm.NextButton.Caption := CustomMessage('UpdateAction');
  end
  else
  begin
    WizardForm.WelcomeLabel1.Caption := CustomMessage('WelcomeInstall');
    WizardForm.WelcomeLabel2.Caption := CustomMessage('WelcomeInstallBody');
  end;

#ifdef VisualQaScreenshotMode
  VisualQaTarget := Lowercase(ExpandConstant('{param:QAPAGE|welcome}'));
  VisualQaPage := wpWelcome;
  if VisualQaTarget = 'license' then VisualQaPage := wpLicense;
  if VisualQaTarget = 'notices' then VisualQaPage := wpInfoBefore;
  if VisualQaTarget = 'folder' then VisualQaPage := wpSelectDir;
  if VisualQaTarget = 'shortcut' then VisualQaPage := wpSelectTasks;
  if VisualQaTarget = 'ready' then VisualQaPage := wpReady;
  if VisualQaTarget = 'progress' then VisualQaPage := wpInstalling;
  if VisualQaTarget = 'success' then VisualQaPage := wpFinished;
  if VisualQaTarget = 'running' then VisualQaPage := wpReady;
#endif
end;

function NoraAppearsRunning(): Boolean;
var
  MarkerPath: String;
begin
  MarkerPath := ExpandConstant('{localappdata}\{#UserDataDirName}\runtime\desktop-instance.json');
#ifdef VisualQaMode
#ifdef VisualQaScreenshotMode
  Result := FileExists(MarkerPath);
#else
  Result := CheckForMutexes('{#SingleInstanceMutexName}');
  if (not Result) and FileExists(MarkerPath) then
  begin
    Log('Removing stale NORA instance marker: ' + MarkerPath);
    if not DeleteFile(MarkerPath) then
      Log('Could not remove stale NORA instance marker; continuing because no live mutex exists.');
  end;
#endif
#else
  Result := CheckForMutexes('{#SingleInstanceMutexName}');
  if (not Result) and FileExists(MarkerPath) then
  begin
    Log('Removing stale NORA instance marker: ' + MarkerPath);
    if not DeleteFile(MarkerPath) then
      Log('Could not remove stale NORA instance marker; continuing because no live mutex exists.');
  end;
#endif
  Log('NORA running check: result=' + IntToStr(Ord(Result)) +
    ' marker=' + MarkerPath + ' marker_exists=' + IntToStr(Ord(FileExists(MarkerPath))));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  ExistingExe: String;
begin
  Result := '';
  if not NoraAppearsRunning() then
    exit;

  if WizardSilent then
  begin
    Result := CustomMessage('RunningSilent');
    exit;
  end;

  if MsgBox(CustomMessage('RunningPrompt'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then
  begin
    Result := CustomMessage('RunningSilent');
    exit;
  end;

  ExistingExe := ExpandConstant('{app}\NoraVPN.exe');
  if (not FileExists(ExistingExe)) or
     (not Exec(ExistingExe, 'shutdown-for-update', '', SW_HIDE, ewWaitUntilTerminated, ExitCode)) or
     (ExitCode <> 0) or
     NoraAppearsRunning() then
    Result := CustomMessage('ShutdownFailed');
end;

function HasCommandLineParam(const Value: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
    if CompareText(ParamStr(Index), Value) = 0 then
    begin
      Result := True;
      exit;
    end;
end;

function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
  ExistingExe: String;
begin
#ifdef VisualQaMode
  if False then
#else
  if NoraAppearsRunning() then
#endif
  begin
    if UninstallSilent then
    begin
      MsgBox(CustomMessage('RunningUninstallSilent'), mbError, MB_OK);
      Result := False;
      exit;
    end;

    if MsgBox(CustomMessage('RunningUninstallPrompt'), mbConfirmation,
      MB_YESNO or MB_DEFBUTTON2) <> IDYES then
    begin
      Result := False;
      exit;
    end;

    ExistingExe := ExpandConstant('{app}\NoraVPN.exe');
    if (not FileExists(ExistingExe)) or
       (not Exec(ExistingExe, 'shutdown-for-update', '', SW_HIDE, ewWaitUntilTerminated, ExitCode)) or
       (ExitCode <> 0) or
       NoraAppearsRunning() then
    begin
      MsgBox(CustomMessage('ShutdownFailed'), mbError, MB_OK);
      Result := False;
      exit;
    end;
  end;

  PurgeUserData := HasCommandLineParam('/PURGEUSERDATA');
  if (not UninstallSilent) and (not PurgeUserData) then
    PurgeUserData := MsgBox(CustomMessage('PurgePrompt'), mbConfirmation,
      MB_YESNO or MB_DEFBUTTON2) = IDYES;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and PurgeUserData then
    DelTree(ExpandConstant('{localappdata}\{#UserDataDirName}'), True, True, True);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if IsUpgradeInstall and ((CurPageID = wpWelcome) or (CurPageID = wpReady)) then
  begin
    WizardForm.NextButton.Width := ScaleX(150);
    WizardForm.NextButton.Left := WizardForm.CancelButton.Left - ScaleX(10) - WizardForm.NextButton.Width;
    WizardForm.BackButton.Left := WizardForm.NextButton.Left - ScaleX(10) - WizardForm.BackButton.Width;
    WizardForm.NextButton.Caption := CustomMessage('UpdateAction');
    if CurPageID = wpReady then
      WizardForm.ReadyLabel.Caption := CustomMessage('UpdateReady');
  end;
  NoraFinishButton.Visible := (CurPageID = wpFinished) and (not WizardSilent);
  if CurPageID = wpInstalling then
  begin
    WizardForm.FilenameLabel.Visible := False;
    if IsUpgradeInstall then
      WizardForm.PageNameLabel.Caption := CustomMessage('UpdateProgress');
  end;
  if CurPageID = wpFinished then
  begin
    if IsUpgradeInstall then
      WizardForm.FinishedLabel.Caption := CustomMessage('UpdateFinishText')
    else
      WizardForm.FinishedLabel.Caption := CustomMessage('FinishText');
    if not WizardSilent then
    begin
      WizardForm.NextButton.Caption := SetupMessage(msgButtonFinish);
      WizardForm.NextButton.Enabled := True;
      WizardForm.NextButton.Visible := False;
    end;
  end;
#ifdef VisualQaScreenshotMode
  if CurPageID = wpLicense then
    WizardForm.LicenseAcceptedRadio.Checked := True;
  if (VisualQaTarget = 'running') and (CurPageID = wpReady) then
  begin
    MsgBox(CustomMessage('RunningPrompt'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
    exit;
  end;
  if CurPageID < VisualQaPage then
  begin
    if CurPageID = wpReady then
      PostMessage(WizardForm.NextButton.Handle, BmClick, 0, 0)
    else
      WizardForm.NextButton.OnClick(WizardForm.NextButton);
  end;
#endif
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  Percent: Integer;
begin
  WizardForm.FilenameLabel.Visible := False;
  if MaxProgress <= 0 then Percent := 0
  else Percent := Round((CurProgress / MaxProgress) * 100.0);

  if IsUpgradeInstall then
  begin
    if Percent < 8 then WizardForm.StatusLabel.Caption := CustomMessage('UpdateStageVerify')
    else if Percent < 88 then WizardForm.StatusLabel.Caption := CustomMessage('UpdateStageCopy')
    else if Percent < 96 then WizardForm.StatusLabel.Caption := CustomMessage('UpdateStageShortcuts')
    else WizardForm.StatusLabel.Caption := CustomMessage('UpdateStageFinish');
  end
  else
  begin
    if Percent < 8 then WizardForm.StatusLabel.Caption := CustomMessage('InstallStageVerify')
    else if Percent < 88 then WizardForm.StatusLabel.Caption := CustomMessage('InstallStageCopy')
    else if Percent < 96 then WizardForm.StatusLabel.Caption := CustomMessage('InstallStageShortcuts')
    else WizardForm.StatusLabel.Caption := CustomMessage('InstallStageFinish');
  end;
end;

#include "LegacyLocationPngCleanup.iss"

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RemoveLegacyLocationPngs();
end;
