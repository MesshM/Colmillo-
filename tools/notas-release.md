Reemplazo liviano de Razer Synapse para el **Razer Cobra**, con la luz de la **RAM Kingston FURY RGB** y la luz, los ventiladores y el overclock de la **gráfica Gigabyte RX 9060 XT**. Sin cuentas ni telemetría.

### Instalar

Baja **Colmillo-Setup.exe** y ejecútalo. Ofrece iniciar con Windows y controlar la luz de la RAM (instala PawnIO). Requiere el runtime de escritorio de .NET 9.

### Novedades

- **Menú por dispositivo**: una cápsula para el mouse, la gráfica y la RAM, con su estado; al abrirla aparecen sus opciones. Animaciones al cambiar de sección, abrir, minimizar y maximizar.
- **Gráfica**
  - La luz cambia sin abrir RGB Fusion, también después de reiniciar la PC.
  - **Ventiladores**: curva de 5 puntos que se arrastra, puntos de partida, cero RPM, y temperatura y RPM en vivo.
  - **Overclock**: los modos automáticos del driver de AMD (Silencioso, Equilibrado, Rage y overclock automático).
  - **Métricas en pantalla**, como RivaTuner: FPS, gráfica, CPU (uso y reloj por núcleo, temperatura Tctl y por CCD) y RAM. `Ctrl+Shift+O` las muestra u oculta. No se inyecta nada en el juego.
- **RAM**
  - Se controla como lo hace MSI Center: los dos módulos van sincronizados y ya no se pierde la detección.
  - Si un módulo deja de responder, Colmillo lo vuelve a buscar solo.
  - Al iniciar, Colmillo arranca PawnIO y aparta el servicio de Kingston FURY CTRL y el LEDKeeper de Mystic Light, que escribían la RAM al mismo tiempo.
- **Todos juntos**: mouse, RAM y gráfica arrancan el efecto en el mismo instante. El ritmo de la respiración de la RAM se ajusta en Ajustes > Componentes.

### Notas

- La temperatura del CPU (Ryzen) necesita que Colmillo corra como administrador, igual que la RAM.
- MSI Afterburner abierto pisa la curva de ventiladores; Gigabyte Control Center abierto puede pisar la luz de la gráfica.
- Si algo falla, el detalle queda en `%APPDATA%\Colmillo\dispositivos.log`.
