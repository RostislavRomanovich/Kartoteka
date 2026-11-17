; PROTOTYPE ONLY — not a release installer.
; Requires a reviewed PostgreSQL installer, tested unattended parameters,
; service/configuration scripts, published app binaries, and clean-VM validation.

#define MyAppName "Картотека"
#define MyAppVersion "0.17.0-prototype"
#define PublishDir "..\\artifacts\\publish\\"

[Setup]
AppId={{D67A16E4-86A7-4C7A-A95D-9CB8D3F1A217}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\\Kartoteka
DefaultGroupName=Картотека
OutputDir=..\\artifacts\\installer
OutputBaseFilename=Kartoteka-Setup-PROTOTYPE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
Uninstallable=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные параметры:"; Flags: unchecked

[Files]
; Server files are included only for the server choice. These are not sufficient
; to run the server until service/database/bootstrap steps are implemented.
Source: "{#PublishDir}*"; DestDir: "{app}\\server"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsServerInstall

[Icons]
Name: "{autoprograms}\\Картотека (сервер)"; Filename: "{app}\\server\\Kartoteka.Server.exe"; Check: IsServerInstall
Name: "{autodesktop}\\Картотека"; Filename: "{code:GetWorkstationUrl}"; Tasks: desktopicon; Check: IsWorkstationInstall

[Code]
var
  InstallModePage: TInputOptionWizardPage;
  ServerUrlPage: TInputQueryWizardPage;

function IsServerInstall(): Boolean;
begin
  Result := (InstallModePage.SelectedValueIndex = 0);
end;

function IsWorkstationInstall(): Boolean;
begin
  Result := (InstallModePage.SelectedValueIndex = 1);
end;

function GetWorkstationUrl(Param: String): String;
begin
  Result := ServerUrlPage.Values[0];
end;

procedure InitializeWizard();
begin
  InstallModePage := CreateInputOptionPage(wpWelcome,
    'Тип установки', 'Выберите назначение этого компьютера',
    'Сервер хранит общую базу и обслуживает подключения сотрудников. Рабочее место открывает уже установленную картотеку на сервере.',
    True, False);
  InstallModePage.Add('Сервер — приложение, серверная часть и база данных PostgreSQL');
  InstallModePage.Add('Рабочий компьютер — ярлык для подключения к существующему серверу');
  InstallModePage.SelectedValueIndex := 0;

  ServerUrlPage := CreateInputQueryPage(InstallModePage.ID,
    'Подключение к серверу', 'Укажите адрес картотеки',
    'Введите адрес, который предоставил администратор, например https://kartoteka-server:8443. Не вводите адрес базы PostgreSQL.');
  ServerUrlPage.Add('Адрес картотеки:', False);
  ServerUrlPage.Values[0] := 'https://';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Url: String;
begin
  Result := True;
  if CurPageID = ServerUrlPage.ID then begin
    if IsWorkstationInstall() then begin
      Url := Trim(ServerUrlPage.Values[0]);
      if (Pos('https://', Lowercase(Url)) <> 1) then begin
        MsgBox('Укажите адрес, начинающийся с https://. Для рабочей сети безопасное соединение обязательно.', mbError, MB_OK);
        Result := False;
      end;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  ; Guard against users mistaking this prototype for a functional installer.
  Result := 'Это прототип мастера установки, а не готовый установщик. Установка остановлена: автоматическая настройка PostgreSQL, службы Windows, секретов, первичного администратора и проверка готовности ещё не реализованы.';
end;
