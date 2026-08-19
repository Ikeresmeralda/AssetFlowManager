; ============================================================================
;  INSTALADOR DE INVENTARIO
; ============================================================================
;  Compilar:  iscc installer\AssetFlow.iss
;  Requiere:  haber publicado antes la aplicacion:
;             dotnet publish src/AssetFlow.Desktop -p:PublishProfile=win-x64
;
;  POR QUE INNO SETUP Y NO MSIX NI WIX
;  -----------------------------------
;  MSIX exige que el paquete vaya firmado con un certificado en el que confie
;  el equipo de destino. Sin firma, Windows lo rechaza. Para un proyecto
;  publico y sin certificado de firma de codigo eso convierte la instalacion
;  en un ejercicio de importar certificados a mano, que es peor experiencia
;  que la que se quiere arreglar.
;
;  WiX produce un MSI apto para despliegue por directiva de grupo, pero su
;  modelo de componentes y GUID es bastante ceremonia para una aplicacion de
;  un unico ejecutable y una carpeta de dependencias.
;
;  Inno Setup genera un .exe autonomo, no necesita firma para ejecutarse
;  (Windows mostrara el aviso de SmartScreen habitual en software sin firmar,
;  que se documenta en el README), instala por usuario sin pedir permisos de
;  administrador y desinstala limpiamente. Es lo que mejor encaja con
;  "descargar, instalar, ejecutar".
; ============================================================================

#define Nombre        "AssetFlow Manager"
#define Version       "1.0.0"
#define Autor         "Iker"
#define Ejecutable    "AssetFlow.exe"
#define CarpetaOrigen "..\src\AssetFlow.Desktop\bin\publish\win-x64"

[Setup]
AppId={{8F3A6C21-4E7B-4D19-9C5A-2B6E1F0D7A34}
AppName={#Nombre}
AppVersion={#Version}
AppVerName={#Nombre} {#Version}
AppPublisher={#Autor}
VersionInfoVersion={#Version}

; Instalacion por usuario: no pide elevacion. Una aplicacion de escritorio que
; solo escribe en su propia carpeta y en AppData no necesita permisos de
; administrador, y pedirlos sin necesitarlos es una mala costumbre.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#Nombre}
DefaultGroupName={#Nombre}
DisableProgramGroupPage=yes
DisableDirPage=no

OutputDir=Output
OutputBaseFilename=AssetFlow-{#Version}-win-x64-setup
SetupIconFile=..\src\AssetFlow.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\{#Ejecutable}
UninstallDisplayName={#Nombre} {#Version}

; LZMA2 al maximo: la carpeta publicada ronda los 148 MB porque incluye el
; runtime de .NET completo, y comprimir bien es lo que mantiene la descarga en
; un tamano razonable.
Compression=lzma2/max
SolidCompression=yes

WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Crear un acceso directo en el escritorio"; \
    GroupDescription: "Accesos directos:"

[Files]
; La carpeta publicada entera: ejecutable, dependencias y runtime de .NET.
; Se excluyen los simbolos de depuracion, que no pintan nada en una
; distribucion y solo abultan.
Source: "{#CarpetaOrigen}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
    Excludes: "*.pdb,*.xml"

[Icons]
Name: "{group}\{#Nombre}"; Filename: "{app}\{#Ejecutable}"
Name: "{group}\Desinstalar {#Nombre}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#Nombre}"; Filename: "{app}\{#Ejecutable}"; Tasks: escritorio

[Run]
Filename: "{app}\{#Ejecutable}"; Description: "Abrir {#Nombre}"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
; La configuracion del usuario (servidor configurado, ultimo usuario) y la
; sesion guardada viven en AppData. Se borran al desinstalar para no dejar
; restos, incluida la sesion cifrada.
Type: filesandordirs; Name: "{userappdata}\AssetFlow"
