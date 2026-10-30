# Protocolo del Razer Cobra (USB, VID 0x1532 PID 0x00A3)

Sacado por ingenieria inversa de Razer Synapse 4 (`RazerAppEngine 4.0.896`).
Synapse no trae la logica del dispositivo en su `app.asar`: la descarga de
`apps.razer.com/synapse/products/163/mw/*.js` y la guarda en el CacheStorage del
Service Worker (`%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Default\Service Worker`).
`163` es el PID en decimal.

Archivos clave dentro de ese cache:

| Archivo | Que tiene |
|---|---|
| `mw/main.*.js` | Config del Cobra: DPI 200-8500 paso 100, botones, efectos, perfil por defecto |
| `mw/6120.*.js` | Clase `rzDevice25`: armado del paquete, CRC, tabla de comandos, enums |
| `mw/7116.*.js` | Motor OBM: como se traduce un mapeo de boton a bytes de firmware |
| `mw/lighting-manifests/DeviceManifest_163_0.json` | LEDs: una sola region, `RegionId 4` (logo) |

## Transporte

- Interfaz HID 0 (`MI_00`), coleccion del mouse. Se abre con acceso 0: Windows tiene
  el mouse abierto en exclusiva para lectura, pero `HidD_SetFeature/GetFeature` funcionan.
- Reporte feature de 90 bytes (91 con el report id 0 delante).
- Sincronico: se escribe con `SetFeature`, se espera ~5 ms y se lee con `GetFeature`.
  Synapse reintenta hasta 30 veces la lectura y 20 la escritura.

```
byte  0     estado (0 al enviar; 2 ok, 1 ocupado, 3 fallo, 4 timeout, 5 no soportado)
byte  1     transaction id (Synapse lo rota 0..30; 0x1F funciona igual)
byte  2-3   paquetes restantes (0)
byte  4     tipo de protocolo (0)
byte  5     tamano de datos
byte  6     clase de comando
byte  7     id de comando (bit 7 = lectura)
byte  8-87  argumentos (80)
byte  88    CRC = XOR de los bytes 2..87
byte  89    reservado
```

## Comandos usados

`clase:id` en hex; el `Get` es el mismo id con el bit 7 prendido.

| Comando | Tamano | Argumentos |
|---|---|---|
| `00:81` Get Firmware | 2 | -> `[mayor, menor]` |
| `00:82` Get Serial | 22 | -> ASCII |
| `00:05` Set Polling | 1 | `[1=1000Hz, 2=500Hz, 8=125Hz]` |
| `04:05` Set DPI activo | 7 | `[perfil, xH, xL, yH, yL, 0, 0]` |
| `04:06` Set etapas DPI | 3+7n | `[perfil, etapaActiva, n, {id, xH, xL, yH, yL, 0, 0} x n]` |
| `0F:02` Set efecto | 6+3c | `[perfil, region, efecto, p1, p2, nColores, {r,g,b} x c]` |
| `0F:04` Set brillo | 3 | `[perfil, region, 0-255]` |
| `02:84` Get IDs de botones | 80 | -> `[total, id...]` |
| `02:0C` Set boton | 80 | `[perfil, boton, modo, funcion, nDatos, datos x5]` |

`perfil` = 1 (el unico slot de memoria del Cobra, equivale al VARSTORE de OpenRazer).

### Efectos (`0F:02`)

| Id | Efecto | p1 | p2 | Colores |
|---|---|---|---|---|
| 0 | Apagado | 0 | 0 | 0 |
| 1 | Estatico | 0 | 0 | 1 |
| 2 | Respiracion | 1 una / 2 dos / 0 aleatorio | 0 | 1, 2 o 0 |
| 3 | Espectro | 0 | 0 | 0 |
| 5 | Reactivo | 0 | velocidad 1-4 | 1 |

### Botones (`02:0C`)

Ids de boton: 1 izq, 2 der, 3 rueda, 4 boton4, 5 boton5, 9 rueda arriba,
10 rueda abajo, 96 ciclo de DPI. `modo`: 0 normal, 1 Hypershift.

| Funcion | Id | Datos |
|---|---|---|
| Desactivado | 0 | - |
| Boton de mouse | 1 | `[idBoton]` |
| Tecla | 2 | `[modificadores USB, uso HID]` |
| DPI | 6 | `[1 sube, 2 baja, 6 ciclo+, 7 ciclo-]` |
| Multimedia | 10 | `[usoH, usoL]` (uso de Consumer Page) |
| Doble clic | 11 | `[1]` |
| Turbo boton | 14 | `[idBoton, msH, msL]` |

## Avisos del firmware

El mouse publica eventos por su cuenta como reporte de entrada en la coleccion
`MI_01&COL05` (pagina 0x01, uso 0x00, 16 bytes; se abre con `GENERIC_READ`).
Formato del parser de Synapse (`rzHardwareEventsProtocol25Parser`):

```
byte 0   tipo: 5 = aviso (4 = teclas Razer, 2 = multimedia)
byte 1   aviso: 2 = cambio de DPI (1 perfil, 8 brillo)
byte 2-5 DPI X (H, L) e Y (H, L)
```

Verificado apretando el boton de DPI en modo normal:
`05-02-03-20-03-20` (800), `05-02-06-40-06-40` (1600), `05-02-0C-80-0C-80` (3200).

## Efectos personalizados en el Cobra (no disponibles)

Synapse no da velocidad para los efectos del Cobra: su interfaz solo expone, por efecto,
`Breathing {color1, color2, isRandom}`, `Reactive {color1, duration}` y `Spectrum` sin
parametros. Los efectos de Chroma Studio son cuadros enviados desde el PC:
`0F:03 [perfil 0, region, fila, inicio, fin, RGB...]` y despues `0F:02 [0, region, 8]`
(efecto "cuadro personalizado"). En este Cobra eso apaga el LED y no muestra color, probado
con region 0 y 4, modo normal y driver, perfil 0 y 1, `0F:10` (Force Chroma Ctrl) y
brillo del perfil 0. Tampoco se ve un estatico en el perfil 0: el LED solo muestra el
perfil activo (1), que se guarda en la memoria del mouse, y animarlo escribiendo ahi 20
veces por segundo podria desgastarla. Por eso Colmillo no anima el mouse por software.

## Color de los LED

Synapse no corrige el color en su codigo. Los LED son lineales y un color de pantalla
viene con gamma sRGB: mandado tal cual, el 20 % de verde y azul de `#FF3B30` se ve
fuerte y el rojo sale fucsia. Colmillo convierte sRGB a lineal antes de enviarlo.

## Por que Windows instala Synapse solo

Windows Update baja `rz00a3dev.inf` (proveedor Razer Inc). El paquete registra
`RazerS3CoinstallerEx.dll` como co-instalador, y ese lanza `RzS3WizardPkgS3.exe`
(copiado a `Program Files (x86)\Razer\RzS3WizardPkg`), que abre el instalador de
Synapse. `scripts/instalar.ps1 -BloquearSynapse` corta ese camino.

## RAM: Kingston FURY Beast DDR4 RGB (SMBus)

El SMBus de la placa (AMD, compatible PIIX4, base `0xB00`) solo se alcanza desde el kernel.
Colmillo usa **PawnIO** (driver firmado y de codigo abierto) con su modulo
`SmbusPIIX4.bin`, que viene incrustado en el ejecutable (LGPL-2.1). PawnIO solo atiende a
procesos con privilegios de administrador. Toda transferencia toma el mutex global
`Global\Access_SMBUS.HTP.Method`, el mismo que usan OpenRGB, HWiNFO e iCUE.

Direcciones en esta PC (escaneo de solo lectura):

| Direccion | Que es | Colmillo |
|---|---|---|
| `0x52`, `0x53` | SPD de cada modulo (configuracion de fabrica) | solo lee (tipo, fabricante, numero de parte) |
| `0x36`, `0x37` | Cambio de pagina del SPD de DDR4 | "send byte" para leer la pagina 1 y volver a la 0 |
| `0x5A`, `0x5B` | Controlador de luz de cada modulo (SPD + 8) | lee y escribe |

- Lecturas: **palabra** (protocolo 3); el valor util va en el byte alto. Una lectura de
  un byte devuelve basura (el eco de la direccion).
- Escrituras: un byte. Algunas fallan con `0x800701B1` (sin respuesta); se reintenta
  hasta 30 veces, como MSI Center.
- Cada cambio va entre `08=53` (abrir) y `08=44` (aplicar).

### Deteccion

El modelo (`0x06`) daba `0x23` (Beast DDR4) o `0x21` (Beast blanco), pero desde que FURY
CTRL actualizo los modulos (su servicio los llama `KingstonFury_Beast_Refresh`) devuelve
`0x00`, abierto o cerrado. El resto de los registros sigue igual. Con solo ese chequeo la RAM
"dejaba de reconocerse" y quedaba apagada. MSI Center y FURY CTRL identifican el modulo por
el SPD, y Colmillo hace lo mismo: un controlador en SPD + 8 cuenta si da el modelo o si su
SPD es DDR4 (byte 2 = `0x0C`) de Kingston (pagina 1, bytes 320-321 = `01 98`). En esta PC:
`KF3200C16D4/32GX`. La pagina del SPD siempre vuelve a 0.

### Secuencia de MSI Center

Sacada de `KingstonDDR5_Change` en `MSI Center\Mystic Light\Lib\MBAPI_x86.dll`
(desensamblada con capstone; maneja tambien los DDR4, la lista de soporte de Mystic Light
pone "Kingston Fury Beast DDR5 RGB" aunque el modulo sea DDR4):

1. Toma `Global\Access_SMBUS.HTP.Method` (espera hasta 1 s) y **lo retiene toda la
   secuencia**. Antes Colmillo lo tomaba por transferencia, y el servicio de Kingston o
   Mystic Light podian escribir entre el "abrir" y el "aplicar".
2. `08=53` en todos, 55 ms.
3. Si hay mas de un modulo: `0B = n - i - 1` en el modulo i (el primero recibe el indice
   mas alto). Colmillo ponia `0B=0` en todos: cada uno se creia el primero y los efectos
   animados se desfasaban (una barra respiraba antes o mas fuerte que la otra).
4. `08=44`, 30 ms.
5. `08=53`, 55 ms; registros del efecto con 5 ms entre cada uno (todos los modulos seguidos);
   `27` = cantidad de modulos; `08=44`. Suelta el mutex y espera 50 ms.

Valores de MSI por efecto (el indice es el de su lista de estilos):

| MSI | Registros |
|---|---|
| 0 estatico | `09=00 20=50 30=01 31-33=RGB` |
| 1 arcoiris | `09=01 0C=01 0D=00 0E=19 20=50` |
| 3 respiracion | `09=03 16=0F 17=05 18=05 19=0F 1A=01 1B=64 1C=40 1D=01 20=50 0D=00 0E=00 30=01 31-33=RGB` |
| 4 dinamico | `09=04 12-13=012C (300 ms) 14-15=05DC (1500 ms) 20=50 30=0A 0D=00 0E=00` |
| 6 cometa | `09=06 0C=01 0D=00 0E=62 20=50 26=07 30=01 31-33=RGB` |
| 9 llama | `09=09 0C=01 0E=40 20=50 0D=00` |
| 10 espectro | `09=01 0C=01 0E=40 20=50 0D=04` |

Los tiempos del dinamico son de 16 bits (alto, bajo); Colmillo mandaba `ms >> 8` y un 1
fijo en el byte bajo. El minimo de la respiracion (`1D`) es 1, no 0. MSI fija el brillo en
80 (`20=50`); Colmillo manda el del slider.

### Modulo que deja de responder

Con el espectro y Mystic Light (`LEDKeeper2.exe`, lo lanza una tarea de MSI al iniciar
sesion) escribiendo a la vez, `0x5B` dejo de confirmar (NACK) y Colmillo siguio solo con
`0x5A`: una barra quedaba congelada en un color. Ahora se recuerda cuantos modulos habia y,
si falta uno, se vuelve a buscar a los 2, 5, 15 y 60 s. Tras reiniciar la PC los dos vuelven
a dar el modelo (`0x06 = 0x23`): el `0x00` de antes era el mismo estado trabado.

### Sincronia con el mouse

En "todos juntos" cada dispositivo prepara su cambio y espera en una barrera (`LightSync`)
antes del comando que arranca la animacion (RAM: el `08=44` final; grafica: `12`; mouse:
`0F:02`). Asi arrancan en el mismo instante. El ritmo de la respiracion del mouse lo fija su
firmware; el de la RAM (`16-19`) se calibra en Ajustes (por defecto el mas lento). En grupo no
hay velocidad: RAM y grafica van a la mas lenta.

### Programas que pisan la luz

`FURYController_Service` (`Program Files (x86)\Kingston Fury\FuryCTRL_SDK`, lo instala FURY
CTRL o MSI Center) arranca con Windows, reaplica su efecto ("Keep LED effect") y escribe el
bus con su propio driver (NTIOLib). Usa el mismo mutex, pero por transferencia. Colmillo
lo detiene al iniciar (ajuste "Detener el servicio de Kingston FURY al iniciar"); no lo
deshabilita. PawnIO arranca a demanda: si nadie lo inicio, Colmillo (elevado) lo inicia.

| Registro | Uso |
|---|---|
| `09` | modo: 00 estatico, 01 arcoiris/espectro, 03 respiracion, 04 dinamico, 06 cometa, 09 llama, 0A crepusculo |
| `0C` / `0D` / `0E` | direccion / retardo entre LEDs / velocidad |
| `12-15` | tiempos del dinamico; `16-1D` tiempos y niveles de la respiracion |
| `20` | brillo 0-100 |
| `26` / `27` | largo del cometa / cantidad de modulos |
| `30` | cantidad de colores; `31..` R, G, B de cada color |

Arcoiris y espectro son el mismo modo: los distingue el retardo (`0D` = 0 o 4).

## Grafica: Gigabyte RX 9060 XT Gaming OC 16G

Sacado de `ucVga.dll` de Gigabyte Control Center (.NET, descompilado con ILSpy):
clases `GvLedApiEx` e `I2CApiEx`. Esta tarjeta no usa el "RGB Fusion 2 GPU" de `0x73`
de modelos anteriores (y de OpenRGB) sino un controlador nuevo:

- I2C por la API del driver de AMD: `ADL2_Display_WriteAndReadI2C`, linea 1, direccion
  `0x75` (`0xEA` en 8 bits), 50 kHz al escribir y 100 kHz al leer. Funciona **sin
  administrador** y sin ninguna DLL de Gigabyte.
- Paquetes de 64 bytes: `[comando, 0x01, datos...]`, el resto en cero.
- El `device.db` de Gigabyte (JSON, clave `VID_SVID_SSID_DID`) da `UiId 1` para
  `1002_1458_2429_7590`: una sola zona.

| Comando | Uso |
|---|---|
| `10 01` | leer version (4 bytes: `01 01 01 01` = 1.1) |
| `11 01` | leer modelo (4 bytes: `02 01 24 29`, el SSID) |
| `12 01 efecto velocidad brillo R G B 0 0` | cambiar el efecto (al instante, no se guarda) |
| `13 01` | guardar el efecto en la memoria de la tarjeta |
| `16 01 ...` | color en tiempo real para la sincronizacion de RGB Fusion |
| `17 01` | leer si salio del modo app (byte 2 = 1: todavia no) |
| `18 01` | "LedAPPClose": salir del modo en que lo maneja una app |

**Tras reiniciar la PC el controlador arranca en modo app** (el de la sincronizacion de RGB
Fusion, `16` cuadro a cuadro) y no hace caso a `12` hasta recibir `18`. Por eso la grafica
solo cambiaba con Gigabyte Control Center abierto: `LedControlEx.SettingApply` de `ucVga.dll`
manda `18`, lee `17` y recien ahi `12`. GCC solo usa estos 7 comandos (`10`-`13`, `16`-`18`).
Colmillo manda `18` antes de cada cambio pero **no lee `17`**: en esta tarjeta esa lectura
falla y la escritura siguiente de la misma sesion de ADL tambien (probado: `w18=0 w17=0
r17=-1 w12=-1`; sin leer, `w18=0 w12=0`). IL leido con un volcador propio sobre
`System.Reflection.Metadata` (`ucVga.dll` es .NET).

Efectos (`12`): 0 apagado, 1 estatico, 2 respiracion, 3 destello, 4 doble destello,
5 ciclo de colores. Velocidad y brillo van de 0 a 10. Apagado manda el color en cero.
Gigabyte espera 120 ms tras cada escritura y 200 ms tras cada lectura.

El controlador responde **una sola lectura** por sesion de ADL: la segunda falla (y la
escritura siguiente tambien) hasta crear otra sesion. Por eso Colmillo lee solo el modelo
para detectarla, y abre y cierra ADL en cada uso; los registros de Gigabyte Control
Center muestran el mismo `GetModelSSID ReadData fail`.

Por que las primeras pruebas fallaban: se mandaba el paquete de 8 bytes a `0x73` con la
estructura de `GvDisplayA.dll` incompleta (`nI2cSpeed` y `nDDCPort` en cero). `ucVga`
llama a `I2CApiEx.SendData(..., nSavePort = 194)` pero adentro pisa la direccion con
`234` (`0xEA`).

Gigabyte publica sus modulos en
`mb.download.gigabyte.com/FileList/Swhttp/LiveUpdate4/GCC/<ApName>/ver2.ini` y
`.../GCC/<ApName>/<ApName>_<version>.exe` (NSIS firmado, se instala con `/S`).

### Deteccion de la grafica

La primera transferencia por ADL a veces falla (justo al iniciar sesion, o la lectura que
toca en el turno "malo" del controlador). Colmillo buscaba una sola vez y quedaba "no
encontrada" hasta reiniciarlo: parecia que hacia falta abrir RGB Fusion, pero no. Probado con
Gigabyte Control Center cerrado: responde en el adaptador 0 (y en el 3, la misma tarjeta).
Ahora reintenta a 1, 3, 5, 10, 20 y 30 s, y vuelve a buscar al salir de suspension.

## Ventiladores de la grafica (ADLX)

En RDNA 4 el Overdrive8 de ADL responde `ADL_ERR_NOT_SUPPORTED` (-9). Adrenalin usa
**ADLX** (`amdadlx64.dll`, viene con el driver). Es C++ con tablas virtuales; Colmillo las
llama por indice, sacado de los encabezados del SDK (GPUOpen-LibrariesAndSDKs/ADLX,
`SDK/Include`). No necesita administrador.

- `ADLXQueryFullVersion` (el driver de esta PC da 1.5) y `ADLXInitialize(version, &sistema)`.
- `IADLXSystem`: 1 `GetGPUs`, 8 `GetGPUTuningServices`, 9 `GetPerformanceMonitoringServices`.
- `IADLXGPUTuningServices`: 10 `IsSupportedManualFanTuning`, 16 `GetManualFanTuning`
  (devuelve la interfaz base: `QueryInterface(L"IADLXManualFanTuning")`, y
  `L"IADLXManualFanTuning1"` para la curva de fabrica).
- `IADLXManualFanTuning`: 3 rangos, 4 curva actual, 5 curva vacia, 6 validar, 7 aplicar,
  8-10 cero RPM. `IADLXManualFanTuning1`: 23 curva de fabrica, 27 cero RPM de fabrica.
- Lista de estados: 3 `Size`, 11 `At`; estado: 3/4 velocidad, 5/6 temperatura.
- `IADLXPerformanceMonitoringServices` 18 `GetCurrentGPUMetrics(gpu, &m)`; metricas:
  7 temperatura, 8 punto caliente, 11 RPM.

RX 9060 XT: 5 puntos, velocidad 20-100 %, temperatura 25-100 °C, cero RPM disponible. Curva
de fabrica `30:20 50:36 58:52 67:68 75:100`, con cero RPM. El driver rechaza una curva
cuya temperatura baja de un punto al siguiente (`IsValidFanTuningStates` da el indice).

## Tuning de la grafica (ADLX)

`IADLXGPUTuningServices` 12 `GetAutoTuning` y 13 `GetPresetTuning` (devuelven la interfaz
base: `QueryInterface(L"IADLXGPUAutoTuning")` / `L"IADLXGPUPresetTuning"`).

- `IADLXGPUAutoTuning`: 3-5 `IsSupported` (undervolt, OC GPU, OC VRAM), 6-8 `IsCurrent`,
  9-11 `Start*` con un listener (`IADLXGPUAutoTuningCompleteListener`: tabla con un solo
  metodo, `OnGPUAutoTuningComplete`). Colmillo lo arma en memoria nativa.
- `IADLXGPUPresetTuning`: 3-7 `IsSupported`, 8-12 `IsCurrent`, 13-17 `Set` (PowerSaver,
  Quiet, Balanced, Turbo, Rage).
- `ADLX_RESET_NEEDED` (18): hay que llamar `ResetToFactory` (5), que borra tambien la curva
  de ventiladores; Colmillo la repone.

RX 9060 XT: overclock automatico de GPU (no de VRAM ni undervolt) y Quiet / Balanced / Rage.
Son los modos que ofrece Gigabyte Control Center para esta tarjeta.

## Metricas

- Grafica y FPS: ADLX `GetCurrentGPUMetrics` (18) y `GetCurrentFPS` (20; -1 sin juego 3D).
- CPU por nucleo: contadores de Windows (PDH) `\Processor Information(*)\% Processor Utility`,
  `% Processor Performance` y `Processor Frequency`; reloj = frecuencia x rendimiento / 100.
  Los hilos se agrupan por nucleo fisico con `GetLogicalProcessorInformation`.
- Temperatura Ryzen: SMN `0x59800` (Tctl, bits 31:21 en octavos, -49 si el bit 19) y
  `0x59954 + 4n` (CCD n, bit 11 valida). Necesita el modulo `AMDFamily17.bin` de
  PawnIO.Modules 0.2.11 (`ioctl_read_smn`, incluido en `Assets/PawnIO`, mismo zip y hash
  de SmbusPIIX4 que el de la RAM).
  Ryzen no tiene sensor por nucleo.
- Overlay: ventana WPF transparente, `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`, siempre arriba.
  No inyecta nada en el juego (seguro con Vanguard/EAC); no se ve en pantalla completa
  exclusiva de DX9/DX11 antiguos. `Ctrl+Shift+O` lo muestra u oculta.

## Macros

Los **macros no se pueden guardar en el Cobra** (`macroGroup: false` en la config de
Synapse). Colmillo los ejecuta por software con un hook de bajo nivel.
