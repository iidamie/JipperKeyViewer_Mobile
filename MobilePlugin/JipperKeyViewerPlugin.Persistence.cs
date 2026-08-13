using StArray.ModManager.Inspector;

namespace JipperKeyViewer.Mobile;

// ModManager's generic Save button persists explicitly marked members on the
// plugin instance. Keep these proxies flat so its settings.json matches the
// format written by SettingsStore.Save(Settings).
public sealed partial class JipperKeyViewerPlugin
{
    [ModSetting] private KeyLayout Layout { get => Settings.Layout; set => Settings.Layout = value; }
    [ModSetting] private bool Enabled { get => Settings.Enabled; set => Settings.Enabled = value; }
    [ModSetting] private bool ShowOnlyInGameplay { get => Settings.ShowOnlyInGameplay; set => Settings.ShowOnlyInGameplay = value; }
    [ModSetting] private bool TouchInputEnabled { get => Settings.TouchInputEnabled; set => Settings.TouchInputEnabled = value; }
    [ModSetting] private bool KeyboardInputEnabled { get => Settings.KeyboardInputEnabled; set => Settings.KeyboardInputEnabled = value; }
    [ModSetting] private bool ShowTouchRegions { get => Settings.ShowTouchRegions; set => Settings.ShowTouchRegions = value; }
    [ModSetting] private bool TouchFootAreaEnabled { get => Settings.TouchFootAreaEnabled; set => Settings.TouchFootAreaEnabled = value; }
    [ModSetting] private float TouchFootAreaHeight { get => Settings.TouchFootAreaHeight; set => Settings.TouchFootAreaHeight = value; }
    [ModSetting] private bool ShowKps { get => Settings.ShowKps; set => Settings.ShowKps = value; }
    [ModSetting] private bool ShowTotal { get => Settings.ShowTotal; set => Settings.ShowTotal = value; }
    [ModSetting] private bool StreamerMode { get => Settings.StreamerMode; set => Settings.StreamerMode = value; }
    [ModSetting] private bool ShowMainKeyCount { get => Settings.ShowMainKeyCount; set => Settings.ShowMainKeyCount = value; }
    [ModSetting] private bool ShowPerKeyKps { get => Settings.ShowPerKeyKps; set => Settings.ShowPerKeyKps = value; }
    [ModSetting] private bool EnableCountFormatting { get => Settings.EnableCountFormatting; set => Settings.EnableCountFormatting = value; }
    [ModSetting] private string KpsLabel { get => Settings.KpsLabel; set => Settings.KpsLabel = value ?? "KPS"; }
    [ModSetting] private string TotalLabel { get => Settings.TotalLabel; set => Settings.TotalLabel = value ?? "Total"; }
    [ModSetting] private bool HideKpsTotalLabel { get => Settings.HideKpsTotalLabel; set => Settings.HideKpsTotalLabel = value; }
    [ModSetting] private bool KpsTotalCentered { get => Settings.KpsTotalCentered; set => Settings.KpsTotalCentered = value; }
    [ModSetting] private bool KpsTotalStacked { get => Settings.KpsTotalStacked; set => Settings.KpsTotalStacked = value; }
    [ModSetting] private bool EnableRain { get => Settings.EnableRain; set => Settings.EnableRain = value; }
    [ModSetting] private float RainSpeed { get => Settings.RainSpeed; set => Settings.RainSpeed = value; }
    [ModSetting] private float RainHeight { get => Settings.RainHeight; set => Settings.RainHeight = value; }
    [ModSetting] private float RainFadePixels { get => Settings.RainFadePixels; set => Settings.RainFadePixels = value; }
    [ModSetting] private float RainLength { get => Settings.RainLength; set => Settings.RainLength = value; }
    [ModSetting] private float RainWidth { get => Settings.RainWidth; set => Settings.RainWidth = value; }
    [ModSetting] private float Scale { get => Settings.Scale; set => Settings.Scale = value; }
    [ModSetting] private float KeyWidth { get => Settings.KeyWidth; set => Settings.KeyWidth = value; }
    [ModSetting] private float KeyHeight { get => Settings.KeyHeight; set => Settings.KeyHeight = value; }
    [ModSetting] private float KeyFontSize { get => Settings.KeyFontSize; set => Settings.KeyFontSize = value; }
    [ModSetting] private float KpsFontSize { get => Settings.KpsFontSize; set => Settings.KpsFontSize = value; }
    [ModSetting] private float TotalFontSize { get => Settings.TotalFontSize; set => Settings.TotalFontSize = value; }
    [ModSetting] private bool EnablePerKeyTextSize { get => Settings.EnablePerKeyTextSize; set => Settings.EnablePerKeyTextSize = value; }
    [ModSetting] private float[] PerKeyFontSize { get => Settings.PerKeyFontSize; set => Settings.PerKeyFontSize = value ?? Array.Empty<float>(); }
    [ModSetting] private float PositionX { get => Settings.PositionX; set => Settings.PositionX = value; }
    [ModSetting] private float PositionY { get => Settings.PositionY; set => Settings.PositionY = value; }
    [ModSetting] private float KeyGap { get => Settings.KeyGap; set => Settings.KeyGap = value; }
    [ModSetting] private int FootKeyCount { get => Settings.FootKeyCount; set => Settings.FootKeyCount = value; }
    [ModSetting] private string[] KeyBindings { get => Settings.KeyBindings; set => Settings.KeyBindings = value ?? Array.Empty<string>(); }
    [ModSetting] private string[] KeyLabels { get => Settings.KeyLabels; set => Settings.KeyLabels = value ?? Array.Empty<string>(); }
    [ModSetting] private string[] FootBindings { get => Settings.FootBindings; set => Settings.FootBindings = value ?? Array.Empty<string>(); }
    [ModSetting] private string[] FootLabels { get => Settings.FootLabels; set => Settings.FootLabels = value ?? Array.Empty<string>(); }
    [ModSetting] private int[] Counts { get => Settings.Counts; set => Settings.Counts = value ?? Array.Empty<int>(); }
    [ModSetting] private int TotalCount { get => Settings.TotalCount; set => Settings.TotalCount = value; }
    [ModSetting] private float[] Background { get => Settings.Background; set => Settings.Background = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] BackgroundPressed { get => Settings.BackgroundPressed; set => Settings.BackgroundPressed = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] Outline { get => Settings.Outline; set => Settings.Outline = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] OutlinePressed { get => Settings.OutlinePressed; set => Settings.OutlinePressed = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] Text { get => Settings.Text; set => Settings.Text = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] TextPressed { get => Settings.TextPressed; set => Settings.TextPressed = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] RainColor { get => Settings.RainColor; set => Settings.RainColor = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] KpsBackground { get => Settings.KpsBackground; set => Settings.KpsBackground = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] KpsOutline { get => Settings.KpsOutline; set => Settings.KpsOutline = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] KpsText { get => Settings.KpsText; set => Settings.KpsText = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] TotalBackground { get => Settings.TotalBackground; set => Settings.TotalBackground = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] TotalOutline { get => Settings.TotalOutline; set => Settings.TotalOutline = value ?? Array.Empty<float>(); }
    [ModSetting] private float[] TotalText { get => Settings.TotalText; set => Settings.TotalText = value ?? Array.Empty<float>(); }
    [ModSetting] private bool EnablePerKeyColors { get => Settings.EnablePerKeyColors; set => Settings.EnablePerKeyColors = value; }
    [ModSetting] private float[][] PerKeyBackground { get => Settings.PerKeyBackground; set => Settings.PerKeyBackground = value ?? Array.Empty<float[]>(); }
    [ModSetting] private float[][] PerKeyBackgroundPressed { get => Settings.PerKeyBackgroundPressed; set => Settings.PerKeyBackgroundPressed = value ?? Array.Empty<float[]>(); }
    [ModSetting] private float[][] PerKeyOutline { get => Settings.PerKeyOutline; set => Settings.PerKeyOutline = value ?? Array.Empty<float[]>(); }
    [ModSetting] private float[][] PerKeyOutlinePressed { get => Settings.PerKeyOutlinePressed; set => Settings.PerKeyOutlinePressed = value ?? Array.Empty<float[]>(); }
    [ModSetting] private float[][] PerKeyText { get => Settings.PerKeyText; set => Settings.PerKeyText = value ?? Array.Empty<float[]>(); }
    [ModSetting] private float[][] PerKeyTextPressed { get => Settings.PerKeyTextPressed; set => Settings.PerKeyTextPressed = value ?? Array.Empty<float[]>(); }
    [ModSetting] private float[][] PerKeyRainColor { get => Settings.PerKeyRainColor; set => Settings.PerKeyRainColor = value ?? Array.Empty<float[]>(); }
    [ModSetting] private KeyViewerFont Font { get => Settings.Font; set => Settings.Font = value; }
    [ModSetting] private string CustomFontFile { get => Settings.CustomFontFile; set => Settings.CustomFontFile = value ?? string.Empty; }
}
