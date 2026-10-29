namespace Colmillo.Core.Input;

/// <summary>Traduccion entre teclas virtuales de Windows y usos HID de teclado (pagina 0x07).</summary>
public static class Keys
{
    static readonly Dictionary<ushort, byte> VkToHid = Build();
    static readonly Dictionary<byte, ushort> HidToVk = VkToHid.ToDictionary(p => p.Value, p => p.Key);

    static Dictionary<ushort, byte> Build()
    {
        var m = new Dictionary<ushort, byte>();
        for (var i = 0; i < 26; i++) m[(ushort)(0x41 + i)] = (byte)(0x04 + i); // A-Z
        for (var i = 1; i <= 9; i++) m[(ushort)(0x30 + i)] = (byte)(0x1D + i); // 1-9
        m[0x30] = 0x27; // 0
        for (var i = 0; i < 12; i++) m[(ushort)(0x70 + i)] = (byte)(0x3A + i); // F1-F12
        for (var i = 0; i < 12; i++) m[(ushort)(0x7C + i)] = (byte)(0x68 + i); // F13-F24
        for (var i = 1; i <= 9; i++) m[(ushort)(0x60 + i)] = (byte)(0x58 + i); // Num1-Num9
        ushort[] vk =
        [
            0x0D, 0x1B, 0x08, 0x09, 0x20, 0xBD, 0xBB, 0xDB, 0xDD, 0xDC, 0xBA, 0xDE, 0xC0, 0xBC, 0xBE, 0xBF,
            0x14, 0x2C, 0x91, 0x13, 0x2D, 0x24, 0x21, 0x2E, 0x23, 0x22, 0x27, 0x25, 0x28, 0x26, 0x90, 0x6F,
            0x6A, 0x6D, 0x6B, 0x60, 0x6E, 0xE2, 0x5D,
        ];
        byte[] hid =
        [
            0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38,
            0x39, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54,
            0x55, 0x56, 0x57, 0x62, 0x63, 0x64, 0x65,
        ];
        for (var i = 0; i < vk.Length; i++) m[vk[i]] = hid[i];
        return m;
    }

    public static byte? ToHid(ushort vk) => VkToHid.TryGetValue(vk, out var h) ? h : null;

    public static ushort ToVk(byte hid) => HidToVk.TryGetValue(hid, out var v) ? v : (ushort)0;

    /// <summary>Bit de modificador USB si la tecla es Ctrl/Shift/Alt/Win (lado indistinto).</summary>
    public static byte ModifierBit(ushort vk) => vk switch
    {
        0x11 or 0xA2 => 0x01,
        0xA3 => 0x10,
        0x10 or 0xA0 => 0x02,
        0xA1 => 0x20,
        0x12 or 0xA4 => 0x04,
        0xA5 => 0x40,
        0x5B => 0x08,
        0x5C => 0x80,
        _ => 0,
    };

    public static string Name(ushort vk) => vk switch
    {
        >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
        0x0D => "Enter",
        0x1B => "Esc",
        0x08 => "Retroceso",
        0x09 => "Tab",
        0x20 => "Espacio",
        0x14 => "Bloq Mayús",
        0x2C => "Impr Pant",
        0x91 => "Bloq Despl",
        0x13 => "Pausa",
        0x2D => "Insert",
        0x2E => "Supr",
        0x24 => "Inicio",
        0x23 => "Fin",
        0x21 => "Re Pág",
        0x22 => "Av Pág",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        0x10 or 0xA0 => "Shift",
        0xA1 => "Shift der",
        0x11 or 0xA2 => "Ctrl",
        0xA3 => "Ctrl der",
        0x12 or 0xA4 => "Alt",
        0xA5 => "AltGr",
        0x5B or 0x5C => "Win",
        0x5D => "Menú",
        0x90 => "Bloq Num",
        0x6A => "Num *",
        0x6B => "Num +",
        0x6D => "Num -",
        0x6E => "Num .",
        0x6F => "Num /",
        0xBA => ";",
        0xBB => "+",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        0xE2 => "<",
        0xAD => "Silenciar",
        0xAE => "Vol -",
        0xAF => "Vol +",
        0xB0 => "Siguiente",
        0xB1 => "Anterior",
        0xB3 => "Reproducir",
        _ => $"Tecla 0x{vk:X2}",
    };

    public static string Combo(byte modifiers, byte hid)
    {
        var parts = new List<string>();
        if ((modifiers & 0x11) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x22) != 0) parts.Add("Shift");
        if ((modifiers & 0x44) != 0) parts.Add("Alt");
        if ((modifiers & 0x88) != 0) parts.Add("Win");
        if (hid != 0) parts.Add(Name(ToVk(hid)));
        return parts.Count == 0 ? "Sin tecla" : string.Join(" + ", parts);
    }
}
