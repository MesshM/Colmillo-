Reemplazo liviano de Razer Synapse para el **Razer Cobra**, ahora también con la luz de la **RAM Kingston FURY RGB**. Sin cuentas, sin telemetría, sin código de red.

### Instalar o actualizar

En PowerShell:

```
irm https://github.com/MesshM/Colmillo-/releases/latest/download/instalar.ps1 -OutFile $env:TEMP\instalar-colmillo.ps1; powershell -ExecutionPolicy Bypass -File $env:TEMP\instalar-colmillo.ps1 -ConRam
```

- `-ConRam` instala PawnIO (driver firmado y de código abierto) para controlar la RAM.
- `-BloquearSynapse` evita que Windows instale Synapse solo al conectar el mouse.

### Novedades

- **RAM Kingston FURY Beast DDR4 RGB**: estático, respiración (1 o 2 colores), espectro, arcoíris, dinámico, cometa, llama y crepúsculo, con brillo y velocidad.
- **Iluminación "Todos juntos" o "Por separado"**: en grupo solo los efectos que tienen el mouse y la RAM (apagado, estático, respiración, espectro).
- Vista previa animada de la RAM y estado de cada dispositivo.
- La primera vez Colmillo pide administrador (lo exige PawnIO) y crea una tarea de inicio: desde ahí arranca elevado sin preguntar.
- La gráfica Gigabyte RX 9060 XT aparece como **no disponible**: el driver actual de AMD bloquea su control de luz (Gigabyte Control Center tampoco puede).

Requiere el runtime de escritorio de .NET 9 (el instalador lo instala si falta).
