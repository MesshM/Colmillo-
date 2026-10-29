Reemplazo liviano de Razer Synapse para el **Razer Cobra**. Sin cuentas, sin telemetría, sin código de red.

### Instalar o actualizar

En PowerShell:

```
irm https://github.com/MesshM/Colmillo-/releases/latest/download/instalar.ps1 -OutFile $env:TEMP\instalar-colmillo.ps1; powershell -ExecutionPolicy Bypass -File $env:TEMP\instalar-colmillo.ps1
```

Agrega `-BloquearSynapse` al final para que Windows no instale Synapse solo al conectar el mouse (pide administrador).

### Novedades

- Icono, isotipo e imagotipo propios.
- Aviso de DPI sobre la barra de tareas al apretar el botón del mouse; la app se actualiza sola.
- Los colores del LED se corrigen de sRGB a lineal: el rojo ya no sale fucsia.
- Diálogo de confirmación propio.
- Instalador que baja la última versión (instalar de nuevo = actualizar).

Requiere el runtime de escritorio de .NET 9 (el instalador lo instala si falta).
