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
/// SPD + 8 (0x58-0x5F); en esta PC, 0x5A y 0x5B. Protocolo verificado contra los modulos
/// reales y contra el de OpenRGB (KingstonFuryDRAMController): lecturas de palabra con el
/// valor en el byte alto, escrituras de un byte, y cada cambio entre 08=53 (abrir) y
/// 08=44 (aplicar). Ver PROTOCOLO.md.
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
    const byte RegDynamicHoldA = 0x12, RegDynamicHoldB = 0x13, RegDynamicFadeA = 0x14, RegDynamicFadeB = 0x15;
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
    // El controlador es lento: Kingston y OpenRGB esperan 10 ms entre pasos.
    static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    readonly PawnIoSmbus _bus;
    readonly byte[] _addresses;

    public int Modules => _addresses.Length;

    KingstonFury(PawnIoSmbus bus, byte[] addresses)
    {
        _bus = bus;
        _addresses = addresses;
    }

    /// <summary>Busca modulos FURY DDR4 RGB. Solo lee.</summary>
    public static KingstonFury? Detect(PawnIoSmbus bus)
    {
        var found = new List<byte>();
        for (var a = FirstAddress; a <= LastAddress; a++)
        {
            var word = bus.ReadWord(a, RegModel);
            if (word is null) continue;
            var model = (byte)(word.Value >> 8);
            if (model is ModelBeastDdr4 or ModelBeastWhiteDdr4) found.Add(a);
        }
        return found.Count > 0 ? new KingstonFury(bus, [.. found]) : null;
    }

    void All(byte reg, byte value)
    {
        foreach (var a in _addresses) _bus.WriteByte(a, reg, value);
        Thread.Sleep(Step);
    }

    /// <summary>Aplica un efecto a todos los modulos a la vez, sincronizados.</summary>
    /// <param name="speed">1 lento ... 5 rapido.</param>
    /// <param name="palette">Si viene, reemplaza a c1/c2 en respiracion, dinamico y cometa (hasta 10).</param>
    public void Apply(RamEffect effect, Rgb c1, Rgb c2, int brightness, int speed, bool twoColors, Rgb[]? palette = null)
    {
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

        // Preambulo: indice 0 en todos para que corran el efecto juntos.
        All(RegApply, Begin);
        All(RegIndex, 0);
        All(RegApply, End);

        All(RegApply, Begin);
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
                All(RegDirection, BottomToTop);
                All(RegDelay, 0);
                All(RegSpeed, 0);
                Colors(c1);
                break;

            case RamEffect.Rainbow:
            case RamEffect.Spectrum:
                // Arcoiris y espectro son el mismo modo; los distingue el retardo entre LEDs.
                All(RegDelay, (byte)(effect == RamEffect.Spectrum ? 4 : 0));
                All(RegSpeed, (byte)new[] { 50, 37, 25, 15, 6 }[speed]);
                break;

            case RamEffect.Breathing:
                All(RegDirection, BottomToTop);
                All(RegDelay, 0);
                if (list is not null) Colors(list);
                else if (twoColors) Colors(c1, c2);
                else Colors(c1);
                All(RegSpeed, 0);
                // Tiempos de fundido entre niveles de brillo, los mismos que usa Kingston.
                var s = (byte)new[] { 9, 7, 5, 3, 1 }[speed];
                All(RegBreathMinToMid, (byte)(s * 3));
                All(RegBreathMidToMax, s);
                All(RegBreathMaxToMid, s);
                All(RegBreathMidToMin, (byte)(s * 3));
                All(RegBreathMinHold, 1);
                All(RegBreathMax, 100);
                All(RegBreathMid, 64);
                All(RegBreathMin, 0);
                break;

            case RamEffect.Dynamic:
                All(RegDirection, BottomToTop);
                All(RegDelay, 0);
                Colors(list ?? [c1, c2]);
                All(RegSpeed, 0);
                var ms = new[] { 800, 500, 300, 200, 100 }[speed];
                All(RegDynamicHoldA, (byte)(ms >> 8));
                All(RegDynamicHoldB, 1);
                All(RegDynamicFadeA, (byte)((ms * 5) >> 8));
                All(RegDynamicFadeB, 1);
                break;

            case RamEffect.Comet:
                All(RegDirection, BottomToTop);
                All(RegDelay, 0);
                All(RegLength, 7);
                Colors(list ?? [c1]);
                All(RegSpeed, (byte)new[] { 60, 40, 25, 12, 4 }[speed]);
                break;

            case RamEffect.Flame:
                All(RegDelay, 0);
                All(RegSpeed, (byte)new[] { 64, 58, 52, 46, 40 }[speed]);
                break;

            case RamEffect.Twilight:
                All(RegDelay, 0);
                All(RegSpeed, (byte)new[] { 128, 96, 64, 40, 20 }[speed]);
                break;
        }

        All(RegBrightness, (byte)Math.Clamp(brightness, 0, 100));
        All(RegNumSlots, (byte)Math.Min(_addresses.Length, 4));
        All(RegApply, End);
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
