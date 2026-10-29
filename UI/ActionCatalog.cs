using System.IO;
using Colmillo.Core.Input;
using Colmillo.Core.Razer;

namespace Colmillo.UI;

/// <summary>Textos y opciones para mostrar y elegir acciones de boton.</summary>
public static class ActionCatalog
{
    public sealed record Option<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    public static readonly Option<ActionKind>[] Kinds =
    [
        new("Predeterminado", ActionKind.Default),
        new("Desactivado", ActionKind.Disabled),
        new("Botón del mouse", ActionKind.MouseButton),
        new("Tecla o atajo", ActionKind.Key),
        new("Multimedia", ActionKind.Media),
        new("Sensibilidad (DPI)", ActionKind.Dpi),
        new("Doble clic", ActionKind.DoubleClick),
        new("Turbo (clic repetido)", ActionKind.Turbo),
        new("Hypershift", ActionKind.Hypershift),
        new("Macro", ActionKind.Macro),
        new("Abrir programa", ActionKind.Launch),
    ];

    public static readonly Option<byte>[] MouseButtons =
    [
        new("Clic izquierdo", 1),
        new("Clic derecho", 2),
        new("Clic de rueda", 3),
        new("Atrás", 4),
        new("Adelante", 5),
        new("Rueda arriba", 9),
        new("Rueda abajo", 10),
        new("Rueda a la izquierda", 104),
        new("Rueda a la derecha", 105),
    ];

    /// <summary>Usos de la pagina Consumer (0x0C) del HID.</summary>
    public static readonly Option<ushort>[] Media =
    [
        new("Reproducir / pausa", 0xCD),
        new("Siguiente pista", 0xB5),
        new("Pista anterior", 0xB6),
        new("Detener", 0xB7),
        new("Subir volumen", 0xE9),
        new("Bajar volumen", 0xEA),
        new("Silenciar", 0xE2),
        new("Calculadora", 0x192),
        new("Navegador", 0x223),
        new("Correo", 0x18A),
        new("Explorador de archivos", 0x194),
    ];

    public static readonly Option<byte>[] Dpi =
    [
        new("Siguiente etapa (ciclo)", 6),
        new("Etapa anterior (ciclo)", 7),
        new("Subir DPI", 1),
        new("Bajar DPI", 2),
    ];

    public static string Describe(ButtonAction a, Func<string, string?> macroName) => a.Kind switch
    {
        ActionKind.Default => "Predeterminado",
        ActionKind.Disabled => "Desactivado",
        ActionKind.MouseButton => Label(MouseButtons, a.Button),
        ActionKind.Key => Keys.Combo(a.Modifiers, a.KeyUsage),
        ActionKind.Media => Label(Media, a.MediaUsage),
        ActionKind.Dpi => Label(Dpi, a.DpiOp),
        ActionKind.DoubleClick => "Doble clic",
        ActionKind.Turbo => $"Turbo: {Label(MouseButtons, a.Button)} ({a.TurboRate}/s)",
        ActionKind.Hypershift => "Hypershift (mantener)",
        ActionKind.Macro => "Macro: " + (a.MacroId is null ? "sin elegir" : macroName(a.MacroId) ?? "borrado"),
        ActionKind.Launch => "Abrir: " + (string.IsNullOrEmpty(a.LaunchPath) ? "sin elegir" : Path.GetFileName(a.LaunchPath)),
        _ => "",
    };

    static string Label<T>(Option<T>[] options, T value) =>
        options.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, value))?.Label ?? $"{value}";
}
