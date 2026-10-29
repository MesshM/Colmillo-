namespace Colmillo.Core.Macros;

public enum StepKind
{
    KeyDown,
    KeyUp,
    MouseDown,
    MouseUp,
    Wheel,
    Delay,
    Text,
}

public sealed class MacroStep
{
    public StepKind Kind { get; set; }
    /// <summary>Tecla virtual (VK_*) para KeyDown/KeyUp.</summary>
    public ushort Vk { get; set; }
    /// <summary>Boton de mouse: 1 izq, 2 der, 3 medio, 4 X1, 5 X2. En Wheel, el delta en muescas.</summary>
    public int Button { get; set; }
    public int DelayMs { get; set; }
    public string? Text { get; set; }

    public MacroStep Clone() => (MacroStep)MemberwiseClone();
}

public enum PlaybackMode
{
    /// <summary>Una vez por pulsacion.</summary>
    Once,
    /// <summary>Repite mientras el boton siga apretado.</summary>
    WhileHeld,
    /// <summary>Una pulsacion arranca, otra detiene.</summary>
    Toggle,
    /// <summary>Repite N veces.</summary>
    Repeat,
}

public sealed class Macro
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "Macro nuevo";
    public PlaybackMode Mode { get; set; } = PlaybackMode.Once;
    public int RepeatCount { get; set; } = 3;
    public List<MacroStep> Steps { get; set; } = [];
}
