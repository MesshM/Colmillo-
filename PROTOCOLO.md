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

## Color de los LED

Synapse no corrige el color en su codigo. Los LED son lineales y un color de pantalla
viene con gamma sRGB: mandado tal cual, el 20 % de verde y azul de `#FF3B30` se ve
fuerte y el rojo sale fucsia. Colmillo convierte sRGB a lineal antes de enviarlo.

## Por que Windows instala Synapse solo

Windows Update baja `rz00a3dev.inf` (proveedor Razer Inc). El paquete registra
`RazerS3CoinstallerEx.dll` como co-instalador, y ese lanza `RzS3WizardPkgS3.exe`
(copiado a `Program Files (x86)\Razer\RzS3WizardPkg`), que abre el instalador de
Synapse. `scripts/instalar.ps1 -BloquearSynapse` corta ese camino.

## Macros

Los **macros no se pueden guardar en el Cobra** (`macroGroup: false` en la config de
Synapse). Colmillo los ejecuta por software con un hook de bajo nivel.
