# Colmillo

Reemplazo liviano de Razer Synapse para el **Razer Cobra** (USB, PID 0x00A3).
Sin cuentas, sin telemetria, sin codigo de red. C# + WPF sobre .NET 9.

| | Synapse 4 | Colmillo |
|---|---|---|
| Procesos | 9 de RazerAppEngine + servicios | 1 |
| RAM en bandeja | ~470 MB | ~9 MB |
| RAM con la ventana abierta | | ~145 MB (se libera al cerrarla) |
| Red | si | no |

## Que hace

- **Rendimiento**: hasta 5 etapas de DPI (200-8500, paso 100) y sondeo 125/500/1000 Hz.
- **Iluminacion**: apagado, estatico, respiracion (1, 2 o aleatorio), espectro, reactivo; brillo.
- **Botones**: capa normal y Hypershift. Teclas/atajos, multimedia, DPI, doble clic y
  turbo se graban en el mouse. Macros y "abrir programa" corren en Colmillo.
- **Macros**: grabador (teclado y opcionalmente clics), editor de pasos, 4 modos.
- **Puntero de Windows**: esquemas de cursores (y uno propio), tamano, velocidad,
  precision, estela, sonar, rueda, doble clic, botones invertidos.

DPI, luz y botones quedan en la memoria del mouse: funcionan aunque Colmillo no corra.
Al apretar el boton de DPI aparece un aviso abajo a la derecha y la app se actualiza.

## Instalar y actualizar

En PowerShell (tambien en un Windows recien instalado):

```
irm https://github.com/MesshM/Colmillo-/releases/latest/download/instalar.ps1 -OutFile $env:TEMP\instalar-colmillo.ps1; powershell -ExecutionPolicy Bypass -File $env:TEMP\instalar-colmillo.ps1 -BloquearSynapse
```

Baja la ultima version de [Releases](https://github.com/MesshM/Colmillo-/releases),
instala el runtime de .NET 9 si falta, copia Colmillo a `%LOCALAPPDATA%\Programs\Colmillo`
y lo deja arrancando con Windows. **Para actualizar, se corre lo mismo**: los ajustes
(`%APPDATA%\Colmillo`) no se tocan. Colmillo no busca actualizaciones solo; en
Ajustes hay un boton que abre la pagina de versiones en el navegador.

`-BloquearSynapse` (pide administrador) evita que Windows Update instale el driver de
Razer que abre el instalador de Synapse al conectar el mouse; conviene correrlo
**antes** de conectarlo. Contra: Windows deja de bajar drivers solo para cualquier
dispositivo. Si ya desactivas la instalacion automatica de drivers al formatear, no
hace falta.

### Por que no se modifica el driver de Razer

El paquete de Razer esta firmado por Microsoft; cambiar una linea del `.inf` (por
ejemplo, quitar el co-instalador) invalida la firma del catalogo y Windows de 64 bits
se niega a instalarlo. Tampoco deberia hacer falta: Colmillo habla con el mouse con la
API HID estandar de Windows (`hid.dll`), que no depende de ningun driver de Razer.
Pendiente de confirmar en un equipo que nunca tuvo los drivers de Razer.

## Compilar

Requiere el .NET 9 SDK.

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish
```

Sale `publish\Colmillo.exe` (~1 MB; usa el runtime de escritorio de .NET 9).
`--bandeja` arranca sin ventana. Icono, isotipo e imagotipo se regeneran desde los
PNG de la marca con `pwsh tools/assets.ps1 -Origen <carpeta>`.

## Como esta armado

```
Core/Hid        P/Invoke de hid.dll/SetupAPI y el transporte del protocolo Razer
Core/Razer      CobraMouse (comandos) y ButtonAction (codificacion de botones)
Core/Input      SendInput, hooks de bajo nivel, macros, tabla VK <-> HID
Core/Windows    Ajustes de puntero (SystemParametersInfo) y esquemas de cursores
Core            DeviceService (conexion, aplicar perfil) y ajustes JSON
Shell           Bandeja (Shell_NotifyIcon directo), inicio con Windows, memoria
Theme, UI       Tokens de GenI (crema/carmesi y oscuro) y paginas
```

El protocolo, y de donde se saco, esta en [PROTOCOLO.md](PROTOCOLO.md).

### Decisiones que no se deducen del codigo

- **WPF y no WebView/Electron**: sin Chromium no hay telemetria de navegador ni
  ~100 MB de procesos extra. Al cerrar la ventana se destruye y se recorta la memoria.
- **Bandeja sin WinForms**: `NotifyIcon` cargaria todo WinForms; se usa `Shell_NotifyIcon`.
- **Macros por F13-F24**: el Cobra no guarda macros. El boton manda una tecla F13-F24
  (asignada en orden) y un hook de teclado la traga y corre el macro. Sin
  modificadores: Ctrl+Shift o Alt+Shift sueltos cambian el idioma del teclado.
  El hook solo existe si hay alguna accion por software asignada.
- **Modo normal al conectar**: Synapse deja el mouse en "modo driver"; sin Synapse
  el boton de DPI no haria nada hasta volverlo a modo normal (`00:04`).
- **Sin `InvariantGlobalization`**: WPF la necesita para mostrar objetos en un ComboBox.
- **Pinceles y estilos no comparten clave**: `Cta` es el estilo del boton, `CtaFill` el
  degradado. Con la misma clave, `DynamicResource` resolvia al estilo y la app se caia.

## Limitaciones

- Solo el Cobra cableado. Otro mouse Razer necesita sus ids (ver PROTOCOLO.md).
- El firmware no responde a leer brillo ni asignaciones de botones: Colmillo es la
  fuente de verdad y las reescribe al conectar.
- Con Synapse abierto, Synapse pisa la luz y el modo. La app avisa y ofrece cerrarlo.
