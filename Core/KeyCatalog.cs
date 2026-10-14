namespace VoicePTT.Core;

public sealed record KeyOption(string Name, int Vk)
{
    public override string ToString() => Name;
}

public static class KeyCatalog
{
    public static IReadOnlyList<KeyOption> All { get; } = Build();

    private static IReadOnlyList<KeyOption> Build()
    {
        var keys = new List<KeyOption>
        {
            new("Scroll Lock", 0x91), new("Caps Lock", 0x14), new("Num Lock", 0x90),
            new("Print Screen", 0x2C), new("Pause", 0x13), new("Space", 0x20),
            new("Tab", 0x09), new("Enter", 0x0D), new("Escape", 0x1B), new("Backspace", 0x08),
            new("Insert", 0x2D), new("Delete", 0x2E), new("Home", 0x24), new("End", 0x23),
            new("Page Up", 0x21), new("Page Down", 0x22), new("Up", 0x26), new("Down", 0x28),
            new("Left", 0x25), new("Right", 0x27), new("Left Ctrl", 0xA2), new("Right Ctrl", 0xA3),
            new("Left Shift", 0xA0), new("Right Shift", 0xA1), new("Left Alt", 0xA4), new("Right Alt", 0xA5)
        };
        for (var i = 1; i <= 24; i++) keys.Add(new KeyOption($"F{i}", 0x6F + i));
        for (var c = 'A'; c <= 'Z'; c++) keys.Add(new KeyOption(c.ToString(), c));
        for (var c = '0'; c <= '9'; c++) keys.Add(new KeyOption(c.ToString(), c));
        return keys;
    }

    public static bool TryParse(string? name, out KeyOption key)
    {
        var normalized = (name ?? "").Trim();
        key = All.FirstOrDefault(k => k.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase))!;
        if (key is null && normalized.StartsWith("Key 0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalized[6..], System.Globalization.NumberStyles.HexNumber, null, out var vk) && vk is > 0 and < 256)
            key = new KeyOption($"Key 0x{vk:X2}", vk);
        return key is not null;
    }

    public static KeyOption FromVk(int vk) => All.FirstOrDefault(k => k.Vk == vk) ?? new KeyOption($"Key 0x{vk:X2}", vk);
}

public sealed record HotkeySpec(int Vk, bool Ctrl, bool Alt, bool Shift, bool Win)
{
    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            parts.Add(KeyCatalog.FromVk(Vk).Name);
            return string.Join("+", parts);
        }
    }

    public static bool TryParse(string? text, out HotkeySpec shortcut)
    {
        shortcut = new HotkeySpec(0, false, false, false, false);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var ctrl = false; var alt = false; var shift = false; var win = false;
        KeyOption? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.ToLowerInvariant();
            switch (part)
            {
                case "ctrl": case "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win": case "windows": win = true; break;
                default:
                    if (key is not null || !KeyCatalog.TryParse(part, out var found)) return false;
                    key = found;
                    break;
            }
        }
        if (key is null) return false;
        shortcut = new HotkeySpec(key.Vk, ctrl, alt, shift, win);
        return true;
    }
}
