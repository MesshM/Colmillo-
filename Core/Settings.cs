using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Colmillo.Core.Macros;
using Colmillo.Core.Razer;

namespace Colmillo.Core;

public sealed class DeviceProfile
{
    public List<int> DpiStages { get; set; } = [400, 800, 1600, 3200, 6400];
    /// <summary>Etapa activa, empieza en 1.</summary>
    public int ActiveStage { get; set; } = 3;
    public int PollingRate { get; set; } = 1000;
    public LightingEffect Effect { get; set; } = LightingEffect.Spectrum;
    public string Color1 { get; set; } = "#D7264F";
    public string Color2 { get; set; } = "#F5E6C8";
    public int ReactiveSpeed { get; set; } = 2;
    public int Brightness { get; set; } = 100;
    /// <summary>Clave: id de boton, con sufijo "h" para la capa Hypershift.</summary>
    public Dictionary<string, ButtonAction> Buttons { get; set; } = [];

    public static string Key(byte id, bool hypershift) => hypershift ? $"{id}h" : $"{id}";

    public ButtonAction Resolve(byte id, bool hypershift)
    {
        if (Buttons.TryGetValue(Key(id, hypershift), out var a) && a.Kind != ActionKind.Default) return a;
        return CobraButtons.Get(id).Factory;
    }
}

public sealed class RamProfile
{
    public Ram.RamEffect Effect { get; set; } = Ram.RamEffect.Spectrum;
    public string Color1 { get; set; } = "#D7264F";
    public string Color2 { get; set; } = "#F5E6C8";
    public int Brightness { get; set; } = 80;
    /// <summary>1 lento ... 5 rapido.</summary>
    public int Speed { get; set; } = 3;
    /// <summary>Respiracion alternando entre dos colores.</summary>
    public bool TwoColors { get; set; }
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "sistema";
    public bool StartWithWindows { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public DeviceProfile Device { get; set; } = new();
    public List<Macro> Macros { get; set; } = [];
    public RamProfile Ram { get; set; } = new();
    /// <summary>Iluminacion de todos los dispositivos junta (solo efectos que tienen todos).</summary>
    public bool LightingTogether { get; set; } = true;
    /// <summary>Esquema de cursores de Windows que habia antes del primer cambio, para poder volver.</summary>
    public string? OriginalCursorScheme { get; set; }

    /// <summary>
    /// En "todos juntos" manda el mouse: la RAM copia su efecto, colores y brillo. Lo que
    /// no existe en la RAM (reactivo) queda en estatico.
    /// </summary>
    public void SyncRamToMouse()
    {
        Ram.Effect = Device.Effect switch
        {
            LightingEffect.Off => Core.Ram.RamEffect.Off,
            LightingEffect.Spectrum => Core.Ram.RamEffect.Spectrum,
            LightingEffect.Breathing or LightingEffect.BreathingDual or LightingEffect.BreathingRandom => Core.Ram.RamEffect.Breathing,
            _ => Core.Ram.RamEffect.Static,
        };
        Ram.TwoColors = Device.Effect == LightingEffect.BreathingDual;
        Ram.Color1 = Device.Color1;
        Ram.Color2 = Device.Color2;
        Ram.Brightness = Device.Brightness;
    }

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Colmillo");
    static readonly string FilePath = Path.Combine(Dir, "ajustes.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (JsonException)
        {
            // Un archivo roto no puede impedir que la app arranque: se guarda aparte y se empieza de cero.
            File.Copy(FilePath, FilePath + ".roto", overwrite: true);
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        // Escritura atomica: si se corta la luz a mitad, queda el archivo anterior.
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
