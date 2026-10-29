namespace Colmillo.Core.Razer;

public enum ActionKind
{
    Default,
    Disabled,
    MouseButton,
    Key,
    Media,
    Dpi,
    DoubleClick,
    Turbo,
    Hypershift,
    // Las dos ultimas no existen en el firmware: el boton manda una tecla F13-F24
    // y el hook de teclado de Colmillo la cambia por el macro o el programa.
    Macro,
    Launch,
}

/// <summary>
/// Lo que hace un boton. Serializable a JSON tal cual; <see cref="Encode"/> lo
/// traduce a la funcion de firmware de 02:0C (ver PROTOCOLO.md, seccion Botones).
/// </summary>
public sealed class ButtonAction
{
    public ActionKind Kind { get; set; } = ActionKind.Default;
    /// <summary>MouseButton y Turbo: id de boton (1 izq, 2 der, 3 rueda, 4, 5, 9/10 rueda arriba/abajo).</summary>
    public byte Button { get; set; }
    /// <summary>Key: uso HID del teclado (pagina 0x07).</summary>
    public byte KeyUsage { get; set; }
    /// <summary>Key: mascara de modificadores USB (1 Ctrl, 2 Shift, 4 Alt, 8 Win; x16 los derechos).</summary>
    public byte Modifiers { get; set; }
    /// <summary>Media: uso de la pagina Consumer (0x0C).</summary>
    public ushort MediaUsage { get; set; }
    /// <summary>Dpi: 1 sube, 2 baja, 6 ciclo adelante, 7 ciclo atras.</summary>
    public byte DpiOp { get; set; }
    /// <summary>Turbo: pulsaciones por segundo.</summary>
    public int TurboRate { get; set; } = 10;
    /// <summary>Macro: id del macro.</summary>
    public string? MacroId { get; set; }
    /// <summary>Launch: ruta del programa o archivo a abrir.</summary>
    public string? LaunchPath { get; set; }

    public static ButtonAction Mouse(byte id) => new() { Kind = ActionKind.MouseButton, Button = id };

    public bool IsSoftware => Kind is ActionKind.Macro or ActionKind.Launch;

    public ButtonAction Clone() => (ButtonAction)MemberwiseClone();

    /// <summary>
    /// Funcion y datos del firmware. Los codificadores de Synapse estan en
    /// mw/6120 (mouseGroupToFnDataByte, keyboardGroupToFnDataByte, etc).
    /// </summary>
    public (byte Fn, byte[] Data) Encode(byte softwareKeyUsage = 0)
    {
        var ms = (ushort)Math.Clamp(1000 / Math.Max(TurboRate, 1), 1, 1000);
        return Kind switch
        {
            ActionKind.Disabled => (0, []),
            ActionKind.MouseButton => (1, [Button]),
            ActionKind.Key => (2, [Modifiers, KeyUsage]),
            ActionKind.Media => (10, [(byte)(MediaUsage >> 8), (byte)MediaUsage]),
            ActionKind.Dpi => (6, [DpiOp]),
            ActionKind.DoubleClick => (11, [1]),
            ActionKind.Turbo => (14, [Button, (byte)(ms >> 8), (byte)ms]),
            ActionKind.Hypershift => (12, [1]),
            ActionKind.Macro or ActionKind.Launch => (2, [0, softwareKeyUsage]),
            _ => throw new InvalidOperationException("Default se resuelve antes de codificar"),
        };
    }
}

/// <summary>Botones fisicos del Cobra con su id de firmware y su accion de fabrica.</summary>
public static class CobraButtons
{
    public sealed record Info(byte Id, string Name, string Short, ButtonAction Factory, bool Remappable = true);

    public static readonly Info[] All =
    [
        new(1, "Clic izquierdo", "Izq", ButtonAction.Mouse(1), Remappable: false),
        new(2, "Clic derecho", "Der", ButtonAction.Mouse(2)),
        new(3, "Clic de rueda", "Med", ButtonAction.Mouse(3)),
        new(9, "Rueda arriba", "R▲", ButtonAction.Mouse(9)),
        new(10, "Rueda abajo", "R▼", ButtonAction.Mouse(10)),
        new(5, "Lateral delantero", "Adelante", ButtonAction.Mouse(5)),
        new(4, "Lateral trasero", "Atrás", ButtonAction.Mouse(4)),
        new(96, "Botón de DPI", "DPI", new ButtonAction { Kind = ActionKind.Dpi, DpiOp = 6 }),
    ];

    public static Info Get(byte id) => All.First(b => b.Id == id);

    /// <summary>
    /// Teclas que el firmware manda en lugar de un macro: F13..F24 (usos HID 0x68..0x73).
    /// Ningun teclado comun las tiene. Sin modificadores a proposito: Ctrl+Shift o
    /// Alt+Shift sueltos cambian el idioma del teclado en Windows.
    /// </summary>
    public const byte FirstSoftwareKey = 0x68;
    public const int SoftwareKeyCount = 12;
}
