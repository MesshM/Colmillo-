; Instalador de Colmillo (Inno Setup 6). Se compila con tools\publicar-release.ps1:
;   ISCC /DAppVersion=0.4.0 installer\Colmillo.iss
; Instala por usuario (sin administrador) en %LOCALAPPDATA%\Programs\Colmillo.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; GUID fijo: es lo que hace que una version nueva actualice a la anterior en vez de duplicarla.
AppId={{7C2A9E4B-3F1D-4C8E-9B6A-2D5E8F1A0C37}
AppName=Colmillo
AppVersion={#AppVersion}
AppVerName=Colmillo {#AppVersion}
AppPublisher=Manuel Galindo
AppPublisherURL=https://github.com/MesshM/Colmillo-
AppSupportURL=https://github.com/MesshM/Colmillo-/issues
AppUpdatesURL=https://github.com/MesshM/Colmillo-/releases
DefaultDirName={localappdata}\Programs\Colmillo
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\publish\setup
OutputBaseFilename=Colmillo-Setup
SetupIconFile=..\Assets\colmillo.ico
UninstallDisplayIcon={app}\Colmillo.exe
UninstallDisplayName=Colmillo
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Colmillo se cierra solo (ver PrepareToInstall); el gestor de reinicio no puede con
; un proceso elevado y solo agregaria una pregunta.
CloseApplications=no
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "inicio"; Description: "Iniciar Colmillo con Windows (en la bandeja)"; GroupDescription: "Opciones:"
Name: "escritorio"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Opciones:"; Flags: unchecked
Name: "ram"; Description: "Controlar la luz de la RAM (instala PawnIO, driver firmado de código abierto)"; GroupDescription: "Opciones:"; Check: not TienePawnIO

[Files]
Source: "..\publish\Colmillo.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "LEEME.md"; Flags: ignoreversion
Source: "..\Assets\PawnIO\COPYING"; DestDir: "{app}"; DestName: "PawnIO-LICENCIA.txt"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Colmillo"; Filename: "{app}\Colmillo.exe"
Name: "{userdesktop}\Colmillo"; Filename: "{app}\Colmillo.exe"; Tasks: escritorio

[Run]
Filename: "winget.exe"; Parameters: "install --id namazso.PawnIO --source winget --accept-package-agreements --accept-source-agreements"; StatusMsg: "Instalando PawnIO..."; Flags: runhidden waituntilterminated; Tasks: ram
; Primera instalacion: se ofrece abrir Colmillo al final. Con PawnIO se abre elevado (un
; UAC): asi crea sus tareas de inicio y despues ya no vuelve a preguntar.
Filename: "{app}\Colmillo.exe"; Parameters: "--relanzado {code:InicioArgs}"; Description: "Abrir Colmillo"; Flags: postinstall nowait skipifsilent shellexec; Verb: "runas"; Check: TienePawnIO
Filename: "{app}\Colmillo.exe"; Parameters: "{code:InicioArgs}"; Description: "Abrir Colmillo"; Flags: postinstall nowait skipifsilent; Check: not TienePawnIO
; Actualizacion silenciosa (desde Ajustes): se vuelve a abrir solo, en la bandeja.
Filename: "{app}\Colmillo.exe"; Parameters: "--bandeja"; Flags: nowait; Check: EsActualizacion

[UninstallRun]
; Las tareas programadas las creo Colmillo elevado: hace falta elevar para borrarlas.
Filename: "{app}\Colmillo.exe"; Parameters: "--desinstalar"; Flags: shellexec waituntilterminated; Verb: "runas"; RunOnceId: "QuitarTareas"


[Code]
function TienePawnIO: Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf64}\PawnIO\PawnIOLib.dll'));
end;

function EsActualizacion: Boolean;
begin
  Result := ExpandConstant('{param:ACTUALIZACION|}') <> '' ;
end;

function InicioArgs(Param: String): String;
begin
  if WizardIsTaskSelected('inicio') then Result := '' else Result := '--sin-inicio';
end;

function RegisterWindowMessage(Name: String): Cardinal;
  external 'RegisterWindowMessageW@user32.dll stdcall';
function PostMessage(Wnd: HWND; Msg: Cardinal; WParam, LParam: Longint): BOOL;
  external 'PostMessageW@user32.dll stdcall';

{ Colmillo puede estar corriendo elevado (por la RAM) y un instalador sin elevar no lo
  puede cerrar. Se le manda por difusion el mensaje "Colmillo.Cerrar", que su ventana
  de la bandeja acepta a proposito desde procesos sin elevar (ChangeWindowMessageFilterEx). }
procedure PedirCierre;
var
  I: Integer;
  Codigo: Integer;
begin
  PostMessage($FFFF, RegisterWindowMessage('Colmillo.Cerrar'), 0, 0); { HWND_BROADCAST }
  for I := 1 to 50 do
  begin
    Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq Colmillo.exe" | find /I "Colmillo.exe"', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
    if Codigo <> 0 then Break;
    Sleep(200);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  PedirCierre;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  PedirCierre;
  Result := True;
end;
