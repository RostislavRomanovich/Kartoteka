
#define MyAppName "Картотека"
#define MyAppVersion "0.25.0"
#define PublishDir "..\artifacts\publish\"
#define PayloadDir "payload\"
#define ScriptsDir "scripts\"

[Setup]
AppId={{D67A16E4-86A7-4C7A-A95D-9CB8D3F1A217}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Kartoteka
DefaultGroupName=Картотека
OutputDir=..\artifacts\installer
OutputBaseFilename=Kartoteka-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
Uninstallable=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "{#PublishDir}*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsServerInstall
Source: "{#PayloadDir}postgresql-*-windows-x64.exe"; DestDir: "{app}\installer\payload"; Flags: ignoreversion; Check: IsServerInstall
Source: "{#ScriptsDir}Install-KartotekaServer.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion; Check: IsServerInstall
Source: "{#ScriptsDir}Install-KartotekaServerService.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion; Check: IsServerInstall
Source: "{#ScriptsDir}Install-KartotekaWorkstation.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion; Check: IsWorkstationInstall
Source: "{#ScriptsDir}Uninstall-Kartoteka.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion

[Registry]
Root: HKLM; Subkey: "Software\Kartoteka"; ValueType: string; ValueName: "InstallMode"; ValueData: "server"; Flags: uninsdeletevalue; Check: IsServerInstall
Root: HKLM; Subkey: "Software\Kartoteka"; ValueType: string; ValueName: "InstallMode"; ValueData: "workstation"; Flags: uninsdeletevalue; Check: IsWorkstationInstall

[Run]
; Установочные сценарии запускаются из CurStepChanged с обязательной проверкой кода завершения.

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\installer\Uninstall-Kartoteka.ps1"""; Flags: runhidden waituntilterminated

[Code]
var
  InstallModePage: TInputOptionWizardPage;
  ServerPage: TInputQueryWizardPage;
  CertificatePage: TInputFileWizardPage;
  InstallationFailed: Boolean;

function IsServerInstall(): Boolean;
begin
  Result := InstallModePage.SelectedValueIndex = 0;
end;

function IsWorkstationInstall(): Boolean;
begin
  Result := InstallModePage.SelectedValueIndex = 1;
end;

function GetServerUrl(Param: String): String;
begin
  Result := Trim(ServerPage.Values[0]);
end;

function GetCertificateFile(Param: String): String;
begin
  Result := Trim(CertificatePage.Values[0]);
end;

function GetServerDnsName(Param: String): String;
var
  U: String;
  P: Integer;
begin
  U := Trim(ServerPage.Values[0]);

  if IsWorkstationInstall() then
  begin
    Delete(U, 1, Length('https://'));
    P := Pos(':', U);
    if P > 0 then
      U := Copy(U, 1, P - 1);
    Result := U;
  end
  else
    Result := U;
end;

function GetServerPort(Param: String): String;
var
  U: String;
  P: Integer;
begin
  U := Trim(ServerPage.Values[0]);

  if IsWorkstationInstall() then
  begin
    Delete(U, 1, Length('https://'));
    P := Pos(':', U);

    if P > 0 then
      Result := Copy(U, P + 1, Length(U))
    else
      Result := '443';
  end
  else
    Result := '8443';
end;

procedure InitializeWizard();
begin
  InstallationFailed := False;
  InstallModePage := CreateInputOptionPage(
    wpWelcome,
    'Тип установки',
    'Выберите назначение компьютера',
    'На серверном компьютере будут храниться база и служба. Рабочий компьютер подключается к уже настроенному серверу.',
    True,
    False
  );

  InstallModePage.Add('Сервер — программа, PostgreSQL и общая база данных');
  InstallModePage.Add('Рабочий компьютер — подключение к существующему серверу');
  InstallModePage.SelectedValueIndex := 0;

  ServerPage := CreateInputQueryPage(
    InstallModePage.ID,
    'Параметры подключения',
    'Укажите имя сервера или HTTPS-адрес',
    'Сервер: DNS-имя без протокола и порта. Рабочее место: полный HTTPS-адрес, например https://kartoteka-server:8443.'
  );

  ServerPage.Add('Имя сервера / HTTPS-адрес:', False);
  ServerPage.Values[0] := 'kartoteka-server';
    CertificatePage := CreateInputFilePage(
    ServerPage.ID,
    'Сертификат сервера',
    'Выберите публичный сертификат сервера Картотеки',
    'Файл сертификата (.cer), полученный с серверного компьютера:'
  );
    CertificatePage.Add('Публичный сертификат:', '.cer', '');
end;
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result :=
    ((PageID = CertificatePage.ID) and IsServerInstall()) or
    (InstallationFailed and (PageID = wpFinished));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  V, U, PortText: String;
  P, PortNumber, I: Integer;
begin
  Result := True;
    if CurPageID = CertificatePage.ID then
  begin
    V := Trim(CertificatePage.Values[0]);

    if (V = '') or not FileExists(V) then
    begin
      MsgBox(
        'Выберите существующий публичный сертификат сервера (.cer).',
        mbError,
        MB_OK
      );
      Result := False;
      Exit;
    end;

    if Lowercase(ExtractFileExt(V)) <> '.cer' then
    begin
      MsgBox('Необходимо выбрать файл с расширением .cer.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    Exit;
  end;

  if CurPageID <> ServerPage.ID then
    Exit;

  V := Trim(ServerPage.Values[0]);

  if V = '' then
  begin
    MsgBox('Поле адреса не может быть пустым.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if IsServerInstall() then
  begin
    if (Pos('://', V) > 0) or
       (Pos('/', V) > 0) or
       (Pos(':', V) > 0) or
       (Pos(' ', V) > 0) then
    begin
      MsgBox(
        'Для сервера укажите только DNS-имя или IPv4-адрес без протокола, порта и пути.',
        mbError,
        MB_OK
      );
      Result := False;
    end;
    Exit;
  end;

  if Pos('https://', Lowercase(V)) <> 1 then
  begin
    MsgBox('Для рабочего места адрес должен начинаться с https://.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  U := Copy(V, Length('https://') + 1, Length(V));

  if (U = '') or
     (Pos('/', U) > 0) or
     (Pos('\', U) > 0) or
     (Pos('?', U) > 0) or
     (Pos('#', U) > 0) or
     (Pos('@', U) > 0) or
     (Pos(' ', U) > 0) then
  begin
    MsgBox(
      'Укажите только HTTPS-адрес сервера и, при необходимости, порт. Пути и дополнительные параметры не допускаются.',
      mbError,
      MB_OK
    );
    Result := False;
    Exit;
  end;

  P := Pos(':', U);
  if P > 0 then
  begin
    PortText := Copy(U, P + 1, Length(U));
    U := Copy(U, 1, P - 1);

    if PortText = '' then
    begin
      MsgBox('После двоеточия необходимо указать порт.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    for I := 1 to Length(PortText) do
    begin
      if (PortText[I] < '0') or (PortText[I] > '9') then
      begin
        MsgBox('Порт должен состоять только из цифр.', mbError, MB_OK);
        Result := False;
        Exit;
      end;
    end;

    PortNumber := StrToIntDef(PortText, 0);
    if (PortNumber < 1) or (PortNumber > 65535) then
    begin
      MsgBox('Порт должен быть в диапазоне от 1 до 65535.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if U = '' then
  begin
    MsgBox('Не указано имя сервера.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure RunPowerShellScript(const ScriptPath, Parameters, FailureText: String);
var
  ResultCode: Integer;
  PowerShellPath, ErrorText: String;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  if not FileExists(PowerShellPath) then
  begin
    InstallationFailed := True;
    MsgBox('Не найден Windows PowerShell: ' + PowerShellPath, mbError, MB_OK);
    Exit;
  end;

  if not Exec(PowerShellPath, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    InstallationFailed := True;
    ErrorText := 'Не удалось запустить сценарий: ' + ScriptPath + #13#10 +
      'Код Windows: ' + IntToStr(ResultCode) + ' (' + SysErrorMessage(ResultCode) + ')';
    MsgBox(ErrorText, mbError, MB_OK);
    Exit;
  end;

  if ResultCode <> 0 then
  begin
    InstallationFailed := True;
    ErrorText := FailureText + #13#10 +
      'Код завершения PowerShell: ' + IntToStr(ResultCode) + #13#10 +
      'Журнал: ' + ExpandConstant('{commonappdata}\Kartoteka\Logs\');
    MsgBox(ErrorText, mbError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ScriptPath, Params: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  if IsServerInstall() then
  begin
    ScriptPath := ExpandConstant('{app}\installer\Install-KartotekaServer.ps1');
    Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"' +
      ' -InstallDirectory "' + ExpandConstant('{app}\server') + '"' +
      ' -PayloadDirectory "' + ExpandConstant('{app}\installer\payload') + '"' +
      ' -ServerDnsName "' + GetServerDnsName('') + '"' +
      ' -Port ' + GetServerPort('') +
      ' -LogPath "' + ExpandConstant('{commonappdata}\Kartoteka\Logs\server-install.log') + '"';
    RunPowerShellScript(ScriptPath, Params,
      'Настройка сервера Картотеки завершилась ошибкой. Установка не считается успешной.');
  end
  else if IsWorkstationInstall() then
  begin
    ScriptPath := ExpandConstant('{app}\installer\Install-KartotekaWorkstation.ps1');
    Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"' +
      ' -ServerUrl "' + GetServerUrl('') + '"' +
      ' -ServerDnsName "' + GetServerDnsName('') + '"' +
      ' -CertificateFile "' + GetCertificateFile('') + '"' +
      ' -TrustCertificate' +
      ' -LogPath "' + ExpandConstant('{commonappdata}\Kartoteka\Logs\workstation-install.log') + '"';
    RunPowerShellScript(ScriptPath, Params,
      'Настройка рабочего места Картотеки завершилась ошибкой. Установка не считается успешной.');
  end;
end;


function GetCustomSetupExitCode(): Integer;
begin
  if InstallationFailed then
    Result := 1
  else
    Result := 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
end;
