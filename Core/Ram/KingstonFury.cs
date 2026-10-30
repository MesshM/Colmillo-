using Colmillo.Core.Hid;
using Colmillo.Core.Razer;

namespace Colmillo.Core.Ram;

public enum RamEffect
{
    Off,
    Static,
    Breathing,
    Spectrum,
    Rainbow,
    Dynamic,
    Comet,
    Flame,
    Twilight,
}

/// <summary>
/// Controlador de luz de Kingston FURY Beast DDR4 RGB. Cada modulo tiene el suyo en
/// SPD + 8 (0x58-0x5F); en esta PC, 0x5A y 0x5B. Lecturas de palabra con el valor en el
/// byte alto, escrituras de un byte, y cada cambio entre 08=53 (abrir) y 08=44 (aplicar).
/// La secuencia, los tiempos y el indice de cada modulo son los de MSI Center
/// (KingstonDDR5_Change de MBAPI_x86.dll, que maneja tambien los DDR4). Ver PROTOCOLO.md.
/// </summary>
public sealed class KingstonFury
{
    // Solo se escribe en este rango: 0x50-0x57 es el SPD (la configuracion de fabrica
    // de la memoria) y 0x30-0x37 cambia de pagina el SPD de DDR4.
    const byte FirstAddress = 0x58;
    const byte LastAddress = 0x5F;

    const byte RegModel = 0x06;
    const byte RegApply = 0x08;
    const byte RegMode = 0x09;
    const byte RegIndex = 0x0B;
    const byte RegDirection = 0x0C;
    const byte RegDelay = 0x0D;
    const byte RegSpeed = 0x0E;
    const byte RegDynamicHoldHi = 0x12, RegDynamicHoldLo = 0x13, RegDynamicFadeHi = 0x14, RegDynamicFadeLo = 0x15;
    const byte RegBreathMinToMid = 0x16, RegBreathMidToMax = 0x17, RegBreathMaxToMid = 0x18, RegBreathMidToMin = 0x19;
    const byte RegBreathMinHold = 0x1A, RegBreathMax = 0x1B, RegBreathMid = 0x1C, RegBreathMin = 0x1D;
    const byte RegBrightness = 0x20;
    const byte RegLength = 0x26;
    const byte RegNumSlots = 0x27;
    const byte RegNumColors = 0x30;
    const byte RegColorBase = 0x31;
    const byte Begin = 0x53, End = 0x44;
    const byte BottomToTop = 0x01;
    const byte ModelBeastDdr4 = 0x23, ModelBeastWhiteDdr4 = 0x21;
    // Tiempos de MSI: 55 ms tras abrir, 5 ms entre registros, 30 ms tras fijar los indices
    // y 50 ms al terminar. Con 10 ms fijos y sin la pausa de apertura, a veces un modulo
    // se perdia la primera escritura y corria el efecto con datos viejos.
    static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(55);
    static readonly TimeSpan Step = TimeSpan.FromMilliseconds(5);
    static readonly TimeSpan IndexDelay = TimeSpan.FromMilliseconds(30);
    static readonly TimeSpan EndDelay = TimeSpan.FromMilliseconds(50);

    readonly PawnIoSmbus _bus;
    readonly byte[] _addresses;

    public int Modules => _addresses.Length;

    /// <summary>
    /// Algun modulo dejo de responder durante el ultimo cambio, aun con los reintentos: hay
    /// que volver a buscarlos antes del proximo.
    /// </summary>
    public bool Lost { get; private set; }

    KingstonFury(PawnIoSmbus bus, byte[] addresses)
    {
        _bus = bus;
        _addresses = addresses;
    }

    /// <summary>
    /// Busca modulos FURY DDR4 RGB. Solo lee. Un controlador cuenta si responde en SPD + 8 y,
    /// o da el modelo en 0x06, o su modulo es de Kingston segun el SPD (asi detectan MSI Center
    /// y FURY CTRL). Desde que FURY CTRL actualizo los modulos ("Beast Refresh") el registro
    /// de modelo devuelve 0: con solo ese chequeo la RAM dejaba de aparecer y quedaba apagada.
    /// </summary>
    public static KingstonFury? Detect(PawnIoSmbus bus)
    {
        var found = new List<byte>();
        var log = new List<string>();
        using (bus.Lock())
        {
            for (var a = FirstAddress; a <= LastAddress; a++)
            {
                var word = bus.ReadWord(a, RegModel);
                if (word is null) continue;
                var model = (byte)(word.Value >> 8);
                var spd = (byte)(a - 8);
                if (model is ModelBeastDdr4 or ModelBeastWhiteDdr4)
                {
                    found.Add(a);
                    log.Add($"0x{a:X2} modelo {model:X2}");
                }
                else if (KingstonPart(bus, spd) is { } part)
                {
                    found.Add(a);
                    log.Add($"0x{a:X2} {part}");
                }
                else
                {
                    log.Add($"0x{a:X2} no es Kingston (modelo {model:X2})");
                }
            }
        }
        DeviceLog.Write($"RAM: {(log.Count > 0 ? string.Join(", ", log) : "ningún controlador responde")}");
        return found.Count > 0 ? new KingstonFury(bus, [.. found]) : null;
    }

    const byte SpdDeviceType = 2, Ddr4 = 0x0C;
    // Pagina 1 del SPD de DDR4: fabricante del modulo (bytes 320-321) y numero de parte (329-348).
    const byte SpdManufacturer = 320 - 256, SpdPart = 329 - 256, SpdPartLength = 20;
    const byte KingstonBank = 0x01, KingstonId = 0x98;

    /// <summary>Numero de parte si el modulo del SPD es un DDR4 de Kingston; si no, null.</summary>
    static string? KingstonPart(PawnIoSmbus bus, byte spd)
    {
        if (bus.ReadByte(spd, SpdDeviceType) != Ddr4) return null;
        bus.SpdPage(1);
        try
        {
            // El bit 7 del primer byte es paridad: se ignora.
            if ((bus.ReadByte(spd, SpdManufacturer) & 0x7F) != KingstonBank || bus.ReadByte(spd, SpdManufacturer + 1) != KingstonId)
                return null;
            var part = new char[SpdPartLength];
            for (var i = 0; i < SpdPartLength; i++) part[i] = (char)(bus.ReadByte(spd, (byte)(SpdPart + i)) ?? 0x20);
            var text = new string(part).Trim();
            return text.Length > 0 ? text : "Kingston";
        }
        finally
        {
            // Siempre de vuelta a la pagina 0: es la que esperan la BIOS y los demas programas.
            bus.SpdPage(0);
        }
    }

    void All(byte reg, byte value)
    {
        foreach (var a in _addresses)
            if (!_bus.WriteByte(a, reg, value)) Lost = true;
        Thread.Sleep(Step);
    }

    /// <summary>Aplica un efecto a todos los modulos a la vez, sincronizados.</summary>
    /// <param name="speed">1 lento ... 5 rapido.</param>
    /// <param name="palette">Si viene, reemplaza a c1/c2 en respiracion, dinamico y cometa (hasta 10).</param>
    /// <param name="breathStep">Paso de la respiracion (1 rapido ... 9 lento) en lugar del de la velocidad.</param>
    /// <param name="start">Se llama justo antes del "aplicar" que arranca el efecto (sincronia).</param>
    public void Apply(RamEffect effect, Rgb c1, Rgb c2, int brightness, int speed, bool twoColors, Rgb[]? palette = null,
        int? breathStep = null, Action? start = null)
    {
        // Todo el cambio con el bus tomado: si otro programa escribe entre el "abrir" y el
        // "aplicar", el controlador mezcla los dos y puede quedar apagado.
        using var hold = _bus.Lock();
        Lost = false;
        speed = Math.Clamp(speed, 1, 5) - 1;
        c1 = c1.ToLed();
        c2 = c2.ToLed();
        var list = palette is { Length: > 0 } ? palette.Take(10).Select(c => c.ToLed()).ToArray() : null;
        if (effect == RamEffect.Off)
        {
            effect = RamEffect.Static;
            c1 = new Rgb(0, 0, 0);
            brightness = 0;
        }

        // Preambulo: cada modulo recibe su posicion en la cadena (el primero, la mas alta).
        // Con el mismo indice en todos, cada uno se cree el primero y los efectos animados
        // se desfasan: una barra respiraba antes o mas fuerte que la otra.
        Open();
        if (_addresses.Length > 1)
        {
            for (var i = 0; i < _addresses.Length; i++)
                if (!_bus.WriteByte(_addresses[i], RegIndex, (byte)(_addresses.Length - i - 1))) Lost = true;
            Thread.Sleep(Step);
        }
        All(RegApply, End);
        Thread.Sleep(IndexDelay);

        Open();
        All(RegMode, effect switch
        {
            RamEffect.Static => 0x00,
            RamEffect.Rainbow or RamEffect.Spectrum => 0x01,
            RamEffect.Breathing => 0x03,
            RamEffect.Dynamic => 0x04,
            RamEffect.Comet => 0x06,
            RamEffect.Flame => 0x09,
            RamEffect.Twilight => 0x0A,
            _ => 0x00,
        });

        switch (effect)
        {
            case RamEffect.Static:
                Colors(c1);
                break;

            case RamEffect.Rainbow:
            case RamEffect.Spectrum:
                // Arcoiris y espectro son el mismo modo; los distingue el retardo entre LEDs.
                All(RegDirection, BottomToTop);
                All(RegDelay, (byte)(effect == RamEffect.Spectrum ? 4 : 0));
                // El mas lento es el valor de MSI para el espectro (0x40).
                All(RegSpeed, (byte)new[] { 64, 40, 25, 15, 6 }[speed]);
                break;

            case RamEffect.Breathing:
                // Tiempos de fundido entre niveles de brillo; MSI usa 15/5/5/15 (velocidad 3).
                var s = (byte)(breathStep is { } step ? Math.Clamp(step, 1, 9) : new[] { 9, 7, 5, 3, 1 }[speed]);
                All(RegBreathMinToMid, (byte)(s * 3));
                All(RegBreathMidToMax, s);
                All(RegBreathMaxToMid, s);
                All(RegBreathMidToMin, (byte)(s * 3));
                All(RegBreathMinHold, 1);
                All(RegBreathMax, 100);
                All(RegBreathMid, 64);
                // MSI deja el minimo en 1, no en 0.
                All(RegBreathMin, 1);
                All(RegDelay, 0);
                All(RegSpeed, 0);
                if (list is not null) Colors(list);
                else if (twoColors) Colors(c1, c2);
                else Colors(c1);
                break;

            case RamEffect.Dynamic:
                // Milisegundos en 16 bits (alto, bajo): MSI manda 300 de espera y 1500 de fundido.
                var wait = new[] { 800, 500, 300, 200, 100 }[speed];
                var fade = wait * 5;
                All(RegDynamicHoldHi, (byte)(wait >> 8));
                All(RegDynamicHoldLo, (byte)wait);
                All(RegDynamicFadeHi, (byte)(fade >> 8));
                All(RegDynamicFadeLo, (byte)fade);
                All(RegDelay, 0);
                All(RegSpeed, 0);
                Colors(list ?? [c1, c2]);
                break;

            case RamEffect.Comet:
                All(RegDirection, BottomToTop);
                All(RegDelay, 0);
                All(RegSpeed, (byte)new[] { 60, 40, 25, 12, 4 }[speed]);
                All(RegLength, 7);
                Colors(list ?? [c1]);
                break;

            case RamEffect.Flame:
                All(RegDirection, BottomToTop);
                All(RegSpeed, (byte)new[] { 64, 58, 52, 46, 40 }[speed]);
                All(RegDelay, 0);
                break;

            case RamEffect.Twilight:
                All(RegDelay, 0);
                All(RegSpeed, (byte)new[] { 128, 96, 64, 40, 20 }[speed]);
                break;
        }

        All(RegBrightness, (byte)Math.Clamp(brightness, 0, 100));
        All(RegNumSlots, (byte)_addresses.Length);
        start?.Invoke();
        All(RegApply, End);
        Thread.Sleep(EndDelay);
    }

    void Open()
    {
        All(RegApply, Begin);
        Thread.Sleep(OpenDelay);
    }

    void Colors(params Rgb[] colors)
    {
        All(RegNumColors, (byte)colors.Length);
        for (var i = 0; i < colors.Length; i++)
        {
            All((byte)(RegColorBase + i * 3), colors[i].R);
            All((byte)(RegColorBase + i * 3 + 1), colors[i].G);
            All((byte)(RegColorBase + i * 3 + 2), colors[i].B);
        }
    }
}
