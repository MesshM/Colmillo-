using Colmillo.Core.Hid;

namespace Colmillo.Core.Razer;

/// <summary>
/// Operaciones de alto nivel del Razer Cobra. Todos los valores salen de la config
/// de Synapse (mw/main.js) y de pruebas contra el mouse real; ver PROTOCOLO.md.
/// </summary>
public sealed class CobraMouse : IDisposable
{
    public const ushort VendorId = 0x1532;
    public const ushort ProductId = 0x00A3;
    public const int MinDpi = 200;
    public const int MaxDpi = 8500;
    public const int DpiStep = 100;
    public const int MaxStages = 5;

    // Unico slot de memoria del Cobra (05:81 devuelve [1 perfil, id 1]).
    const byte Profile = 1;
    // Region del manifest de iluminacion: una sola, el logo.
    const byte LogoRegion = 4;

    readonly RazerTransport _t;

    public string Firmware { get; }
    public string Serial { get; }

    CobraMouse(RazerTransport t)
    {
        _t = t;
        var fw = _t.Send(0x00, 0x81, 2);
        Firmware = fw.Ok && fw.Args.Length >= 2 ? $"{fw.Args[0]}.{fw.Args[1]:D2}" : "?";
        var sn = _t.Send(0x00, 0x82, 22);
        Serial = sn.Ok ? System.Text.Encoding.ASCII.GetString(sn.Args).TrimEnd('\0') : "";
    }

    public static CobraMouse? Connect()
    {
        var t = RazerTransport.Open(VendorId, ProductId);
        return t is null ? null : new CobraMouse(t);
    }

    /// <summary>
    /// Modo normal (0) o driver (3). Synapse deja el mouse en modo driver, donde el
    /// firmware le pasa los botones especiales al software; sin Synapse hay que
    /// volverlo a normal para que el boton de DPI funcione solo.
    /// </summary>
    public bool SetDeviceMode(byte mode) => _t.Send(0x00, 0x04, 2, [mode, 0]).Ok;

    public byte? GetDeviceMode()
    {
        var r = _t.Send(0x00, 0x84, 2);
        return r.Ok ? r.Args[0] : null;
    }

    // ---------- Rendimiento ----------

    public bool SetPollingRate(int hz)
    {
        byte code = hz switch { >= 1000 => 1, >= 500 => 2, _ => 8 };
        return _t.Send(0x00, 0x05, 1, [code]).Ok;
    }

    public int? GetPollingRate()
    {
        var r = _t.Send(0x00, 0x85, 1);
        if (!r.Ok) return null;
        return r.Args[0] switch { 1 => 1000, 2 => 500, 8 => 125, _ => null };
    }

    public bool SetDpi(int dpi)
    {
        dpi = ClampDpi(dpi);
        return _t.Send(0x04, 0x05, 7, [Profile, Hi(dpi), Lo(dpi), Hi(dpi), Lo(dpi), 0, 0]).Ok;
    }

    public int? GetDpi()
    {
        var r = _t.Send(0x04, 0x85, 7, [Profile]);
        return r.Ok ? r.Args[1] << 8 | r.Args[2] : null;
    }

    /// <summary>Etapas que recorre el boton de DPI. <paramref name="active"/> empieza en 1.</summary>
    public bool SetDpiStages(IReadOnlyList<int> stages, int active)
    {
        var n = Math.Clamp(stages.Count, 1, MaxStages);
        var args = new byte[3 + 7 * n];
        args[0] = Profile;
        args[1] = (byte)Math.Clamp(active, 1, n);
        args[2] = (byte)n;
        for (var i = 0; i < n; i++)
        {
            var d = ClampDpi(stages[i]);
            var o = 3 + 7 * i;
            // Ids de etapa 1..n: asi los devuelve el firmware en 04:86.
            args[o] = (byte)(i + 1);
            args[o + 1] = Hi(d);
            args[o + 2] = Lo(d);
            args[o + 3] = Hi(d);
            args[o + 4] = Lo(d);
        }
        return _t.Send(0x04, 0x06, (byte)args.Length, args).Ok;
    }

    public (List<int> Stages, int Active)? GetDpiStages()
    {
        var r = _t.Send(0x04, 0x86, 80, [Profile]);
        if (!r.Ok) return null;
        var list = new List<int>();
        int n = r.Args[2];
        for (var i = 0; i < n && 3 + 7 * i + 4 < r.Args.Length; i++)
        {
            var o = 3 + 7 * i;
            list.Add(r.Args[o + 1] << 8 | r.Args[o + 2]);
        }
        return (list, r.Args[1]);
    }

    // ---------- Iluminacion ----------

    public bool SetEffect(LightingEffect effect, Rgb primary, Rgb secondary, int speed)
    {
        primary = primary.ToLed();
        secondary = secondary.ToLed();
        byte[] args = effect switch
        {
            LightingEffect.Off => [Profile, LogoRegion, 0, 0, 0, 0],
            LightingEffect.Static => [Profile, LogoRegion, 1, 0, 0, 1, primary.R, primary.G, primary.B],
            LightingEffect.Breathing => [Profile, LogoRegion, 2, 1, 0, 1, primary.R, primary.G, primary.B],
            LightingEffect.BreathingDual => [Profile, LogoRegion, 2, 2, 0, 2,
                primary.R, primary.G, primary.B, secondary.R, secondary.G, secondary.B],
            LightingEffect.BreathingRandom => [Profile, LogoRegion, 2, 0, 0, 0],
            LightingEffect.Spectrum => [Profile, LogoRegion, 3, 0, 0, 0],
            LightingEffect.Reactive => [Profile, LogoRegion, 5, 0, (byte)Math.Clamp(speed, 1, 4), 1,
                primary.R, primary.G, primary.B],
            _ => throw new ArgumentOutOfRangeException(nameof(effect)),
        };
        return _t.Send(0x0F, 0x02, (byte)args.Length, args).Ok;
    }

    /// <summary>Brillo 0-100. El firmware no responde al Get (03 fallo), solo al Set.</summary>
    public bool SetBrightness(int percent)
    {
        var v = (byte)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * 255);
        return _t.Send(0x0F, 0x04, 3, [Profile, LogoRegion, v]).Ok;
    }

    // ---------- Botones ----------

    /// <summary>Ids de boton que acepta el firmware (02:84). En el Cobra: 1 2 3 4 5 96 9 10.</summary>
    public List<byte> GetButtonIds()
    {
        var r = _t.Send(0x02, 0x84, 80, [Profile]);
        if (!r.Ok || r.Args.Length == 0) return [];
        return r.Args.Skip(1).Take(r.Args[0]).ToList();
    }

    /// <param name="softwareKey">Uso HID F13-F24 que manda el boton si la accion es por software.</param>
    public bool SetButton(byte buttonId, bool hypershift, ButtonAction action, byte softwareKey = 0)
    {
        var (fn, data) = action.Encode(softwareKey);
        var args = new byte[80];
        args[0] = Profile;
        args[1] = buttonId;
        args[2] = (byte)(hypershift ? 1 : 0);
        args[3] = fn;
        args[4] = (byte)data.Length;
        data.CopyTo(args, 5);
        return _t.Send(0x02, 0x0C, 80, args).Ok;
    }

    static int ClampDpi(int dpi) => Math.Clamp(dpi / DpiStep * DpiStep, MinDpi, MaxDpi);
    static byte Hi(int v) => (byte)(v >> 8);
    static byte Lo(int v) => (byte)v;

    public void Dispose() => _t.Dispose();
}

public enum LightingEffect
{
    Off,
    Static,
    Breathing,
    BreathingDual,
    BreathingRandom,
    Spectrum,
    Reactive,
}

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return new Rgb(255, 255, 255);
        var v = Convert.ToInt32(hex, 16);
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>
    /// Color de pantalla (sRGB, con gamma) a intensidad de LED (lineal). Sin esto,
    /// el 20 % de verde y azul de un rojo como #FF3B30 se ve fuerte en el LED y el
    /// rojo sale fucsia; los rojos claros salen rosados.
    /// </summary>
    public Rgb ToLed() => new(Linear(R), Linear(G), Linear(B));

    static byte Linear(byte v)
    {
        var c = v / 255.0;
        var l = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return (byte)Math.Round(l * 255);
    }
}
