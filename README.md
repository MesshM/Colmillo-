# Colmillo

Reemplazo liviano de Razer Synapse para el **Razer Cobra** (USB, PID 0x00A3), con la luz
de la RAM Kingston FURY y de la grafica Gigabyte RX 9060 XT (luz y ventiladores).
Sin cuentas ni telemetria. C# + WPF sobre .NET 9.

| | Synapse 4 | Colmillo |
|---|---|---|
| Procesos | 9 de RazerAppEngine + servicios | 1 |
| RAM en bandeja | ~470 MB | ~25 MB |
| RAM con la ventana abierta | | ~145 MB (se libera al cerrarla) |
| Red | siempre | solo al apretar "Buscar actualizaciones" (GitHub) |

## Que hace

El menu tiene **Iluminacion** (de todos los dispositivos) y una capsula por dispositivo con
su estado; al abrirla aparecen sus opciones. Mouse: rendimiento, botones, macros y puntero
de Windows. Grafica: ventiladores, overclock y metricas. RAM: solo luz (su capsula abre
Iluminacion). Las paginas entran con una animacion corta y la ventana usa las animaciones de
Windows al minimizar y maximizar.

- **Rendimiento**: hasta 5 etapas de DPI (200-8500, paso 100) y sondeo 125/500/1000 Hz.
- **Iluminacion**: mouse, RAM y grafica, todos juntos (apagado, estatico, respiracion,
  espectro) o por separado. Mouse: ademas reactivo y respiracion aleatoria. RAM Kingston
  FURY RGB: ademas arcoiris, dinamico, cometa, llama y crepusculo, con una paleta propia de
  hasta 10 colores para respiracion, dinamico y cometa. Grafica Gigabyte RX 9060 XT
  (RGB Fusion): ademas destello y doble destello, con velocidad; no necesita Gigabyte
  Control Center ni administrador.
- **Botones**: capa normal y Hypershift. Teclas/atajos, multimedia, DPI, doble clic y
  turbo se graban en el mouse. Macros y "abrir programa" corren en Colmillo.
- **Macros**: grabador (teclado y opcionalmente clics), editor de pasos, 4 modos.
- **Puntero de Windows**: esquemas de cursores (y uno propio), tamano, velocidad,
  precision, estela, sonar, rueda, doble clic, botones invertidos.
- **Ventiladores de la grafica**: curva de 5 puntos arrastrable (como MSI Afterburner),
  tres puntos de partida, cero RPM, y temperatura, punto caliente y RPM en vivo. Va por
  ADLX, la API del driver de AMD; sin administrador. Colmillo reaplica la curva al iniciar.
- **Overclock**: los modos automaticos del driver de AMD (los de Gigabyte Control Center):
  Silencioso, Equilibrado, Rage y overclock automatico. Se reaplican al iniciar.
- **Metricas**: FPS, grafica (uso, temperaturas, relojes, VRAM, consumo, ventilador), CPU
  (uso y reloj, total y por nucleo; temperatura Tctl y por CCD) y RAM, encima de los juegos
  como RivaTuner. `Ctrl+Shift+O` lo muestra u oculta.
- **Componentes** (Ajustes): al iniciar, Colmillo arranca PawnIO si estaba detenido y
  detiene el servicio de Kingston FURY CTRL, que pisa la luz de la RAM. Si la RAM o la
  grafica no aparecen, reintenta solo; se ve el detalle en `%APPDATA%\Colmillo\dispositivos.log`.

DPI, luz y botones quedan en la memoria del mouse: funcionan aunque Colmillo no corra.
Al apretar el boton de DPI aparece un aviso abajo a la derecha y la app se actualiza.

## Instalar y actualizar

Baja **`Colmillo-Setup.exe`** de [Releases](https://github.com/MesshM/Colmillo-/releases/latest)
y ejecutalo. Instala por usuario en `%LOCALAPPDATA%\Programs\Colmillo` (sin administrador),
crea el acceso directo del menu Inicio, se registra en "Aplicaciones instaladas" y ofrece:

- **Iniciar con Windows** (en la bandeja).
- **Controlar la luz de la RAM**: instala **PawnIO** (driver firmado y de codigo abierto).
  PawnIO solo atiende a programas con privilegios de administrador, asi que al terminar
  Colmillo se abre elevado (un UAC) y crea dos tareas programadas: una al iniciar sesion
  y otra que usa el acceso directo para abrirlo elevado **sin** UAC.

**Actualizar**: Ajustes > "Buscar actualizaciones". Es el unico momento en que Colmillo
se conecta a internet, y solo a la API de GitHub. Si hay una version nueva, baja el
instalador, verifica su SHA-256 contra el publicado y se instala en silencio; Colmillo
se vuelve a abrir solo. Los ajustes (`%APPDATA%\Colmillo`) no se tocan, ni al desinstalar.

Requiere el runtime de escritorio de .NET 9.

### Script (opcional)

`scripts\instalar.ps1` sigue disponible para instalar sin el asistente; `-ConRam` instala
PawnIO y `-BloquearSynapse` (pide administrador) evita que Windows Update instale el driver de
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
`--bandeja` arranca sin ventana. `--sin-elevar` (para depurar) no se relanza elevado por la
tarea: sin RAM, pero se puede manejar con UI Automation. Icono, isotipo e imagotipo se regeneran desde los
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
- **Cierre por mensaje registrado**: el instalador corre sin elevar y no puede cerrar a
  Colmillo elevado. Le manda por difusion `Colmillo.Cerrar`, que la ventana de la bandeja
  acepta con `ChangeWindowMessageFilterEx`. Un vigilante de archivos no funciono: el
  proceso lanzado por el Programador de tareas no recibia los eventos.
- **Dos tareas y no una con una marca**: la copia que lanza el Programador de tareas no
  veia un valor de HKCU escrito por la copia sin elevar. Una tarea sin disparador para
  "abrir con ventana" evita pasar estado entre procesos.
- **RAM como MSI Center**: secuencia, tiempos e indice por modulo copiados de su
  `MBAPI_x86.dll`, con el mutex del SMBus tomado toda la secuencia. Deteccion por el SPD
  (fabricante Kingston), porque el registro de modelo da 0 tras la actualizacion de FURY CTRL.
- **Nada espera de forma sincronica en el hilo de UI**: `AmdService.EndMonitor` esperaba el
  semaforo de ADLX con `Wait()` desde un `Unloaded`, y la lectura que lo tenia necesitaba volver
  a ese hilo para soltarlo: la app quedaba trabada (con la ventana cerrada Windows igual la ve
  "respondiendo") y no atendia ni la orden de cerrarse. `Shell/UiWatchdog` deja en
  `dispositivos.log` si la UI deja de responder o su cola de mensajes se llena.
- **Ventiladores por ADLX y no por ADL**: el Overdrive8 de ADL no responde en RDNA 4.
  ADLX se abre al iniciar y con la pagina de ventiladores a la vista; despues se cierra.
- **Grafica por ADL, abierto y cerrado en cada uso**: el controlador de luz responde una
  sola lectura por sesion de ADL, y asi `atiadlxx.dll` no queda cargada en la bandeja.
  Guardar en la tarjeta escribe su memoria: se hace 3 s despues del ultimo cambio.
- **Modulos de PawnIO incrustados**: `SmbusPIIX4.bin` (RAM) y `AMDFamily17.bin` (temperatura del Ryzen) de PawnIO.Modules 0.2.11 (LGPL-2.1,
  `Assets/PawnIO/COPYING`). El driver PawnIO se instala aparte (winget `namazso.PawnIO`).
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
- Con Gigabyte Control Center abierto, su RGB Fusion puede pisar la luz de la grafica.
  No hace falta abrirlo: la grafica se controla sin el.
- Con MSI Afterburner abierto, su curva pisa la de Colmillo. La pagina avisa.
- Mystic Light (MSI Center) tambien escribe la RAM al abrirse.
