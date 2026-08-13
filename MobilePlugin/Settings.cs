using System.Text;
using System.Text.Json;

namespace JipperKeyViewer.Mobile;

public enum KeyLayout
{
    Key8,
    Key10,
    Key12,
    Key14,
    Key16,
    Key20,
    Key24,
}

public enum KeyViewerFont
{
    MapleStory,
    ImGuiDefault,
    Custom,
}

public sealed class KeyViewerSettings
{
    private static readonly float[] DefaultBackground = { 0.56f, 0.235f, 1f, 0.20f };
    private static readonly float[] DefaultBackgroundPressed = { 1f, 1f, 1f, 0.90f };
    private static readonly float[] DefaultOutline = { 0.55f, 0.24f, 1f, 1f };
    private static readonly float[] DefaultOutlinePressed = { 1f, 1f, 1f, 1f };
    private static readonly float[] DefaultText = { 1f, 1f, 1f, 1f };
    private static readonly float[] DefaultTextPressed = { 0f, 0f, 0f, 1f };
    private static readonly float[] DefaultRainColor = { 0.51f, 0.13f, 0.86f, 0.80f };

    public KeyLayout Layout = KeyLayout.Key16;
    public bool Enabled = true;
    public bool ShowOnlyInGameplay = true;
    public bool TouchInputEnabled = true;
    public bool KeyboardInputEnabled = true;
    public bool ShowTouchRegions;
    public bool TouchFootAreaEnabled = true;
    public float TouchFootAreaHeight = 0.18f;
    public bool ShowKps = true;
    public bool ShowTotal = true;
    public bool StreamerMode;
    public bool ShowMainKeyCount = true;
    public bool ShowPerKeyKps;
    public bool EnableCountFormatting = true;
    public string KpsLabel = "KPS";
    public string TotalLabel = "Total";
    public bool HideKpsTotalLabel;
    public bool KpsTotalCentered;
    public bool KpsTotalStacked;
    public bool EnableRain = true;
    public float RainSpeed = 100f;
    public float RainHeight = 275f;
    public float RainFadePixels = 40f;
    public float RainLength = 1f;
    public float RainWidth = 1f;
    public float Scale = 1f;
    public float KeyWidth = 50f;
    public float KeyHeight = 50f;
    public float KeyFontSize = 20f;
    public float KpsFontSize = 18f;
    public float TotalFontSize = 18f;
    public bool EnablePerKeyTextSize;
    public float[] PerKeyFontSize = new float[32];
    public float PositionX = 0.5f;
    public float PositionY = 0.03f;
    public float KeyGap = 4f;
    public int FootKeyCount = 4;
    public string[] KeyBindings = Defaults.ForLayout(KeyLayout.Key16);
    public string[] KeyLabels = new string[16];
    public string[] FootBindings = { "F8", "F3", "F7", "F2" };
    public string[] FootLabels = new string[4];
    public int[] Counts = new int[32];
    public int TotalCount;

    public float[] Background = { 0.56f, 0.235f, 1f, 0.20f };
    public float[] BackgroundPressed = { 1f, 1f, 1f, 0.90f };
    public float[] Outline = { 0.55f, 0.24f, 1f, 1f };
    public float[] OutlinePressed = { 1f, 1f, 1f, 1f };
    public float[] Text = { 1f, 1f, 1f, 1f };
    public float[] TextPressed = { 0f, 0f, 0f, 1f };
    public float[] RainColor = { 0.51f, 0.13f, 0.86f, 0.80f };
    public float[] KpsBackground = { 0.56f, 0.235f, 1f, 0.20f };
    public float[] KpsOutline = { 0.55f, 0.24f, 1f, 1f };
    public float[] KpsText = { 1f, 1f, 1f, 1f };
    public float[] TotalBackground = { 0.56f, 0.235f, 1f, 0.20f };
    public float[] TotalOutline = { 0.55f, 0.24f, 1f, 1f };
    public float[] TotalText = { 1f, 1f, 1f, 1f };
    public bool EnablePerKeyColors;
    public float[][] PerKeyBackground = CreateColorArray(DefaultBackground);
    public float[][] PerKeyBackgroundPressed = CreateColorArray(DefaultBackgroundPressed);
    public float[][] PerKeyOutline = CreateColorArray(DefaultOutline);
    public float[][] PerKeyOutlinePressed = CreateColorArray(DefaultOutlinePressed);
    public float[][] PerKeyText = CreateColorArray(DefaultText);
    public float[][] PerKeyTextPressed = CreateColorArray(DefaultTextPressed);
    public float[][] PerKeyRainColor = CreateColorArray(DefaultRainColor);
    public KeyViewerFont Font = KeyViewerFont.MapleStory;
    public string CustomFontFile = string.Empty;

    internal string[] CurrentBindings => KeyBindings;
    internal string[] CurrentLabels => KeyLabels;

    public void Normalize()
    {
        if (!Enum.IsDefined(Layout)) Layout = KeyLayout.Key16;
        int count = Defaults.Count(Layout);
        KeyBindings = EnsureStrings(KeyBindings, count, Defaults.ForLayout(Layout));
        KeyLabels = EnsureStrings(KeyLabels, count, Array.Empty<string>());
        FootKeyCount = Math.Clamp(FootKeyCount, 0, 8);
        FootBindings = EnsureStrings(FootBindings, FootKeyCount, Defaults.FootBindings);
        FootLabels = EnsureStrings(FootLabels, FootKeyCount, Array.Empty<string>());
        TouchFootAreaHeight = ClampFinite(TouchFootAreaHeight, 0.08f, 0.35f, 0.18f);
        Counts = EnsureInts(Counts, 32);
        PerKeyFontSize = EnsureFloats(PerKeyFontSize, 32);
        PerKeyBackground = EnsureColorArray(PerKeyBackground, DefaultBackground);
        PerKeyBackgroundPressed = EnsureColorArray(PerKeyBackgroundPressed, DefaultBackgroundPressed);
        PerKeyOutline = EnsureColorArray(PerKeyOutline, DefaultOutline);
        PerKeyOutlinePressed = EnsureColorArray(PerKeyOutlinePressed, DefaultOutlinePressed);
        PerKeyText = EnsureColorArray(PerKeyText, DefaultText);
        PerKeyTextPressed = EnsureColorArray(PerKeyTextPressed, DefaultTextPressed);
        PerKeyRainColor = EnsureColorArray(PerKeyRainColor, DefaultRainColor);
        TotalCount = Math.Max(0, TotalCount);
        Scale = ClampFinite(Scale, 0.45f, 2f, 1f);
        KeyWidth = ClampFinite(KeyWidth, 24f, 160f, 50f);
        KeyHeight = ClampFinite(KeyHeight, 24f, 160f, 50f);
        KeyFontSize = ClampFinite(KeyFontSize, 8f, 72f, 20f);
        KpsFontSize = ClampFinite(KpsFontSize, 8f, 72f, 18f);
        TotalFontSize = ClampFinite(TotalFontSize, 8f, 72f, 18f);
        PositionX = ClampFinite(PositionX, 0f, 1f, 0.5f);
        PositionY = ClampFinite(PositionY, 0f, 1f, 0.03f);
        KeyGap = ClampFinite(KeyGap, 1f, 12f, 4f);
        RainSpeed = ClampFinite(RainSpeed, 20f, 1000f, 100f);
        RainHeight = ClampFinite(RainHeight, 20f, 600f, 275f);
        RainFadePixels = ClampFinite(RainFadePixels, 0f, 200f, 40f);
        RainLength = ClampFinite(RainLength, 0.1f, 3f, 1f);
        RainWidth = ClampFinite(RainWidth, 0.1f, 2f, 1f);
        KpsLabel = NormalizeLabel(KpsLabel, "KPS");
        TotalLabel = NormalizeLabel(TotalLabel, "Total");
        if (!Enum.IsDefined(Font)) Font = KeyViewerFont.MapleStory;
        CustomFontFile = NormalizeFontFile(CustomFontFile);
        Background = EnsureColor(Background, new[] { 0.56f, 0.235f, 1f, 0.20f });
        BackgroundPressed = EnsureColor(BackgroundPressed, new[] { 1f, 1f, 1f, 0.90f });
        Outline = EnsureColor(Outline, new[] { 0.55f, 0.24f, 1f, 1f });
        OutlinePressed = EnsureColor(OutlinePressed, new[] { 1f, 1f, 1f, 1f });
        Text = EnsureColor(Text, new[] { 1f, 1f, 1f, 1f });
        TextPressed = EnsureColor(TextPressed, new[] { 0f, 0f, 0f, 1f });
        RainColor = EnsureColor(RainColor, new[] { 0.51f, 0.13f, 0.86f, 0.80f });
        KpsBackground = EnsureColor(KpsBackground, DefaultBackground);
        KpsOutline = EnsureColor(KpsOutline, DefaultOutline);
        KpsText = EnsureColor(KpsText, DefaultText);
        TotalBackground = EnsureColor(TotalBackground, DefaultBackground);
        TotalOutline = EnsureColor(TotalOutline, DefaultOutline);
        TotalText = EnsureColor(TotalText, DefaultText);
    }

    private static string[] EnsureStrings(string[]? source, int count, IReadOnlyList<string> fallback)
    {
        if (source == null || source.Length != count)
        {
            var result = new string[count];
            for (int i = 0; i < count; i++)
                result[i] = i < fallback.Count ? fallback[i] : string.Empty;
            return result;
        }

        for (int i = 0; i < source.Length; i++)
        {
            source[i] = source[i]?.Trim() ?? string.Empty;
            if (source[i].Length == 0 && i < fallback.Count)
                source[i] = fallback[i];
        }
        return source;
    }

    private static float[] EnsureColor(float[]? source, float[] fallback)
    {
        if (source == null || source.Length < 4)
            return fallback;
        for (int i = 0; i < 4; i++)
            source[i] = float.IsFinite(source[i]) ? Math.Clamp(source[i], 0f, 1f) : fallback[i];
        return source;
    }

    private static int[] EnsureInts(int[]? source, int count)
    {
        if (source == null || source.Length != count)
        {
            var result = new int[count];
            if (source != null)
                Array.Copy(source, result, Math.Min(source.Length, result.Length));
            return result;
        }
        for (int i = 0; i < source.Length; i++)
            source[i] = Math.Max(0, source[i]);
        return source;
    }

    private static float[] EnsureFloats(float[]? source, int count)
    {
        if (source == null || source.Length != count)
        {
            var result = new float[count];
            if (source != null)
                Array.Copy(source, result, Math.Min(source.Length, result.Length));
            return result;
        }
        for (int i = 0; i < source.Length; i++)
            source[i] = float.IsFinite(source[i]) ? Math.Clamp(source[i], 0f, 72f) : 0f;
        return source;
    }

    private static float[][] EnsureColorArray(float[][]? source, float[] fallback)
    {
        if (source is { Length: 32 })
        {
            for (int i = 0; i < source.Length; i++)
                source[i] = EnsureColor(source[i], fallback.ToArray());
            return source;
        }

        var result = new float[32][];
        for (int i = 0; i < result.Length; i++)
        {
            float[]? value = source != null && i < source.Length ? source[i] : null;
            result[i] = EnsureColor(value, fallback.ToArray());
        }
        return result;
    }

    private static string NormalizeLabel(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim()[..Math.Min(32, value.Trim().Length)];

    private static string NormalizeFontFile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string file = Path.GetFileName(value.Trim());
        string extension = Path.GetExtension(file);
        return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            ? file : string.Empty;
    }

    private static float ClampFinite(float value, float min, float max, float fallback)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static float[][] CreateColorArray(float[] value)
    {
        var result = new float[32][];
        for (int i = 0; i < result.Length; i++) result[i] = value.ToArray();
        return result;
    }
}

internal static class Defaults
{
    internal static readonly string[] FootBindings = { "F8", "F3", "F7", "F2", "F6", "F1", "F5", "F4" };

    private static readonly string[] AllBindings =
    {
        "Tab", "_1", "_2", "E", "P", "Equal", "Backspace", "Backslash",
        "Space", "C", "Comma", "Period", "CapsLock", "LeftShift", "Enter", "H",
        "LeftCtrl", "D", "RightShift", "Semicolon", "Q", "Z", "X", "V", "B",
    };

    internal static int Count(KeyLayout layout) => layout switch
    {
        KeyLayout.Key8 => 8,
        KeyLayout.Key10 => 10,
        KeyLayout.Key12 => 12,
        KeyLayout.Key14 => 14,
        KeyLayout.Key16 => 16,
        KeyLayout.Key20 => 20,
        KeyLayout.Key24 => 24,
        _ => 16,
    };

    internal static string[] ForLayout(KeyLayout layout)
    {
        int count = Count(layout);
        var result = new string[count];
        Array.Copy(AllBindings, result, count);
        return result;
    }
}

internal sealed class SettingsStore
{
    private readonly string _path;
    private readonly string _countsPath;
    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions CountOptions = new()
    {
        IncludeFields = true,
    };

    private sealed class CountSnapshot
    {
        public int[] Counts = Array.Empty<int>();
        public int TotalCount;
    }

    internal SettingsStore(string directory)
    {
        _path = Path.Combine(directory, "settings.json");
        _countsPath = Path.Combine(directory, "counts.json");
    }

    internal KeyViewerSettings Load()
    {
        KeyViewerSettings settings;
        if (!File.Exists(_path))
        {
            settings = new KeyViewerSettings();
        }
        else
        {
            try
            {
                settings = JsonSerializer.Deserialize<KeyViewerSettings>(File.ReadAllText(_path), Options)
                    ?? new KeyViewerSettings();
            }
            catch (Exception exception)
            {
                PluginLog.Warn($"Settings load failed: {exception.Message}");
                settings = new KeyViewerSettings();
            }
        }

        settings.Normalize();
        LoadCounts(settings);
        return settings;
    }

    internal bool Save(KeyViewerSettings settings)
    {
        bool settingsSaved = true;
        try
        {
            settings.Normalize();
            string directory = Path.GetDirectoryName(_path) ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(directory);
            WriteAtomic(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception exception)
        {
            PluginLog.Warn($"Settings save failed: {exception.Message}");
            settingsSaved = false;
        }

        return settingsSaved && SaveCounts(settings);
    }

    internal bool SaveCounts(KeyViewerSettings settings)
    {
        try
        {
            settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(_countsPath) ?? AppContext.BaseDirectory);
            var snapshot = new CountSnapshot
            {
                Counts = (int[])settings.Counts.Clone(),
                TotalCount = settings.TotalCount,
            };
            WriteAtomic(_countsPath, JsonSerializer.Serialize(snapshot, CountOptions));
            return true;
        }
        catch (Exception exception)
        {
            PluginLog.Warn($"Count snapshot save failed: {exception.Message}");
            return false;
        }
    }

    private void LoadCounts(KeyViewerSettings settings)
    {
        if (!File.Exists(_countsPath)) return;
        try
        {
            CountSnapshot? snapshot = JsonSerializer.Deserialize<CountSnapshot>(
                File.ReadAllText(_countsPath), CountOptions);
            if (snapshot?.Counts == null || snapshot.Counts.Length == 0) return;

            Array.Clear(settings.Counts);
            Array.Copy(snapshot.Counts, settings.Counts, Math.Min(snapshot.Counts.Length, settings.Counts.Length));
            for (int i = 0; i < settings.Counts.Length; i++)
                settings.Counts[i] = Math.Max(0, settings.Counts[i]);
            settings.TotalCount = Math.Max(0, snapshot.TotalCount);
        }
        catch (Exception exception)
        {
            PluginLog.Warn($"Count snapshot load failed: {exception.Message}");
        }
    }

    private static void WriteAtomic(string path, string contents)
    {
        string temporary = path + ".tmp";
        byte[] bytes = Encoding.UTF8.GetBytes(contents);
        try
        {
            using var stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.WriteThrough);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
        catch
        {
            File.WriteAllBytes(temporary, bytes);
        }
        File.Move(temporary, path, true);
    }
}
