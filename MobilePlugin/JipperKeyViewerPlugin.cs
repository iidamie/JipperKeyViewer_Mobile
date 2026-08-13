using System.Reflection;
using System.Numerics;
using ImGuiNET;
using StArray.ModManager.Android.Native;
using StArray.ModManager.Behaviours;
using StArray.ModManager.Manager;
using StArray.ModManager.Runtime;

[assembly: ModEntryPoint(typeof(JipperKeyViewer.Mobile.JipperKeyViewerPlugin))]

namespace JipperKeyViewer.Mobile;

public sealed partial class JipperKeyViewerPlugin : IModPlugin, IModSettings
{
    private const string LogTag = "JipperKeyViewer";
    private readonly SettingsStore _settingsStore;
    private readonly string _modDirectory;
    private readonly ReplayApiBinding _replayApi = new();
    private long _lastFrameTicks;
    private long _nextGameProbeTicks;
    private GameApi? _game;
    private GitHubUpdateService? _updateService;
    private bool _loaded;
    private bool _settingsPanelDrawnThisFrame;
    private int _lastSavedTotalCount;
    private long _nextCountSaveTicks;
    private string _notice = string.Empty;
    private DateTime _noticeUntilUtc;
    private int _appearanceKeyIndex;
    private bool _appearanceKeyFoot;
    private string[] _customFonts = Array.Empty<string>();

    public JipperKeyViewerPlugin()
    {
        _modDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? AppContext.BaseDirectory;
        _settingsStore = new SettingsStore(_modDirectory);
        Settings = _settingsStore.Load();
    }

    public KeyViewerSettings Settings { get; private set; }
    internal string ModDirectory => _modDirectory;
    internal GameApi? Game => _game;
    internal bool IsLoaded => _loaded;

    public string Id => "JipperKeyViewer";
    public string Name => "Jipper Key Viewer Mobile";
    public string Version => "1.7.0-mobile.19";
    public string Author => "HitMargin / mobile port";
    public string Description => "Jipper Key Viewer touch and keyboard overlay for ADOFAI Android";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    public void OnLoad()
    {
        _loaded = true;
        Settings ??= new KeyViewerSettings();
        Settings.Normalize();
        _replayApi.TryBind();
        KeyViewerFontRuntime.Reset();
        KeyViewerRuntime.Reset(Settings);
        _settingsPanelDrawnThisFrame = false;
        _lastSavedTotalCount = Settings.TotalCount;
        _nextCountSaveTicks = 0;
        _updateService = new GitHubUpdateService(_modDirectory, Version);
        _customFonts = KeyViewerFontRuntime.ListCustomFonts(_modDirectory);
        if (Settings.Font == KeyViewerFont.Custom && string.IsNullOrWhiteSpace(Settings.CustomFontFile)
            && _customFonts.Length > 0)
            Settings.CustomFontFile = _customFonts[0];
        _updateService.StartAutomaticCheck();
        InputEvents.OnTouch += OnTouch;
        TryResolveGame();
        PluginLog.Info("Loaded mobile ImGui overlay");
    }

    public void OnUnload()
    {
        _loaded = false;
        _replayApi.Dispose();
        InputEvents.OnTouch -= OnTouch;
        _updateService?.Dispose();
        _updateService = null;
        KeyViewerFontRuntime.Reset();
        KeyViewerRuntime.Reset(Settings);
        _game = null;
        _settingsPanelDrawnThisFrame = false;
        _settingsStore.Save(Settings);
        PluginLog.Info("Unloaded");
    }

    internal void TryResolveGame()
    {
        if (!_loaded || _game != null) return;
        GameApi? game = GameApi.Create();
        if (game == null) return;
        _game = game;
        PluginLog.Info("ADOFAI runtime binding is ready");
    }

    internal bool IsGameplayActive()
        // The game facade is optional for a key viewer. If a target build changes
        // its controller metadata, input and the overlay must still remain usable.
        => _game?.IsGameplayActive() ?? true;

    internal bool ConsumeSettingsPanelVisibility()
    {
        bool captured = _settingsPanelDrawnThisFrame;
        _settingsPanelDrawnThisFrame = false;
        return captured;
    }

    private void OnTouch(TouchEventInfo info)
    {
        if (_loaded && !_replayApi.IsPlaybackActive)
            KeyViewerRuntime.EnqueueTouch(info);
    }

    public void OnForegroundGUI(ImDrawListPtr drawList)
    {
        _updateService?.DrawForegroundNotification();
        RenderFrame(drawList);
    }

    private void RenderFrame(ImDrawListPtr drawList)
    {
        if (!_loaded) return;
        if (!Settings.Enabled)
        {
            KeyViewerRuntime.ResetInputState();
            return;
        }
        long now = Environment.TickCount64;
        if (_game == null && now >= _nextGameProbeTicks)
        {
            _nextGameProbeTicks = now + 1000;
            TryResolveGame();
        }
        if (_lastFrameTicks == 0) _lastFrameTicks = now;
        float delta = Math.Clamp((now - _lastFrameTicks) / 1000f, 0.001f, 0.25f);
        _lastFrameTicks = now;
        try
        {
            _replayApi.TryBind();
            KeyViewerRuntime.Update(this, delta, _replayApi.IsPlaybackActive);
            AutoSaveCounts(now);
            KeyViewerRuntime.Render(this, drawList);
        }
        catch (Exception exception)
        {
            PluginLog.Error($"Overlay update failed: {exception}");
        }
    }

    public void OnGui()
    {
        _settingsPanelDrawnThisFrame = true;
        Settings.Normalize();
        ImGui.TextUnformatted("Jipper Key Viewer Mobile");
        ImGui.TextDisabled(_game == null ? "waiting for ADOFAI runtime" : "runtime ready");
        ImGui.Separator();

        if (ImGui.Button("Clear counts##jipper-actions-clear"))
        {
            KeyViewerRuntime.ClearCounts(Settings);
            SaveSettingsNow();
            _notice = "Counts cleared";
            _noticeUntilUtc = DateTime.UtcNow.AddSeconds(3);
        }
        ImGui.SameLine();
        if (ImGui.Button("Save settings##jipper-actions-save"))
        {
            SaveSettingsNow();
            _notice = "Settings saved";
            _noticeUntilUtc = DateTime.UtcNow.AddSeconds(3);
        }
        if (DateTime.UtcNow < _noticeUntilUtc)
            ImGui.TextDisabled(_notice);

        float footerReserve = ImGui.GetFrameHeightWithSpacing() * 2f;
        if (ImGui.BeginChild(
                "##jipper-settings-scroll",
                new Vector2(0f, -footerReserve),
                ImGuiChildFlags.Borders,
                ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (ImGui.BeginTabBar("##jipper-settings-tabs"))
            {
                DrawGeneralSettingsTab();
                DrawLayoutSettingsTab();
                DrawRainSettingsTab();
                DrawAppearanceSettingsTab();
                DrawColorSettingsTab();
                DrawKeyBindingsTab();
                DrawUpdateTab();
                ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();
    }

    private void DrawGeneralSettingsTab()
    {
        if (!ImGui.BeginTabItem("General"))
            return;

        string[] layoutNames = { "8K", "10K", "12K", "14K", "16K", "20K", "24K" };
        int layout = (int)Settings.Layout;
        if (ImGui.Combo("Layout", ref layout, layoutNames, layoutNames.Length))
        {
            Settings.Layout = (KeyLayout)layout;
            Settings.Normalize();
            KeyViewerRuntime.ResetInputState();
        }

        ImGui.Checkbox("Enabled", ref Settings.Enabled);
        ImGui.Checkbox("Show only in gameplay", ref Settings.ShowOnlyInGameplay);
        ImGui.Checkbox("Enable touch mapping", ref Settings.TouchInputEnabled);
        ImGui.Checkbox("Show touch regions", ref Settings.ShowTouchRegions);
        ImGui.Checkbox("Touch foot area", ref Settings.TouchFootAreaEnabled);
        if (Settings.TouchFootAreaEnabled)
            ImGui.SliderFloat("Touch foot height", ref Settings.TouchFootAreaHeight, 0.08f, 0.35f, "%.2f");
        ImGui.Checkbox("Keyboard input", ref Settings.KeyboardInputEnabled);
        ImGui.Checkbox("Show KPS", ref Settings.ShowKps);
        ImGui.Checkbox("Show total", ref Settings.ShowTotal);
        ImGui.Checkbox("Streamer mode", ref Settings.StreamerMode);
        ImGui.Checkbox("Per-key KPS", ref Settings.ShowPerKeyKps);
        ImGui.Checkbox("Count formatting", ref Settings.EnableCountFormatting);
        ImGui.EndTabItem();
    }

    private void DrawLayoutSettingsTab()
    {
        if (!ImGui.BeginTabItem("Layout"))
            return;

        ImGui.Separator();
        ImGui.SliderFloat("Scale", ref Settings.Scale, 0.45f, 2f, "%.2f");
        ImGui.SliderFloat("Key width", ref Settings.KeyWidth, 24f, 160f, "%.1f px");
        ImGui.SliderFloat("Key height", ref Settings.KeyHeight, 24f, 160f, "%.1f px");
        ImGui.SliderFloat("Horizontal position", ref Settings.PositionX, 0f, 1f, "%.2f");
        ImGui.SliderFloat("Vertical position", ref Settings.PositionY, 0f, 1f, "%.2f");
        ImGui.SliderFloat("Key gap", ref Settings.KeyGap, 1f, 12f, "%.1f");
        int footCount = Settings.FootKeyCount;
        if (ImGui.SliderInt("Foot keys", ref footCount, 0, 8))
        {
            Settings.FootKeyCount = footCount;
            Settings.Normalize();
            KeyViewerRuntime.ResetInputState();
        }
        ImGui.EndTabItem();
    }

    private void DrawRainSettingsTab()
    {
        if (!ImGui.BeginTabItem("Rain"))
            return;

        ImGui.Checkbox("Rain effect", ref Settings.EnableRain);
        if (Settings.EnableRain)
        {
            ImGui.SliderFloat("Rain speed", ref Settings.RainSpeed, 20f, 1000f, "%.0f");
            ImGui.SliderFloat("Rain height", ref Settings.RainHeight, 20f, 600f, "%.0f");
            ImGui.SliderFloat("Rain edge fade", ref Settings.RainFadePixels, 0f, 200f, "%.0f px");
            ImGui.SliderFloat("Rain length", ref Settings.RainLength, 0.1f, 3f, "%.2fx");
            ImGui.SliderFloat("Rain width", ref Settings.RainWidth, 0.1f, 2f, "%.2fx key");
        }
        ImGui.EndTabItem();
    }

    private void DrawAppearanceSettingsTab()
    {
        if (!ImGui.BeginTabItem("Appearance"))
            return;

        string[] fontNames = { "MapleStory", "ImGui default", "Custom file" };
        int font = (int)Settings.Font;
        if (ImGui.Combo("Font", ref font, fontNames, fontNames.Length))
        {
            Settings.Font = (KeyViewerFont)font;
            KeyViewerFontRuntime.Reset();
        }

        if (Settings.Font == KeyViewerFont.Custom)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Refresh##jipper-font-refresh"))
            {
                _customFonts = KeyViewerFontRuntime.ListCustomFonts(_modDirectory);
                KeyViewerFontRuntime.Reset();
            }

            if (_customFonts.Length == 0)
            {
                ImGui.TextDisabled("No .ttf/.otf files in CustomFont");
            }
            else
            {
                int selected = Array.FindIndex(_customFonts, value =>
                    value.Equals(Settings.CustomFontFile, StringComparison.OrdinalIgnoreCase));
                if (selected < 0) selected = 0;
                string preview = _customFonts[selected];
                if (ImGui.BeginCombo("Custom font file", preview))
                {
                    for (int i = 0; i < _customFonts.Length; i++)
                    {
                        bool isSelected = i == selected;
                        if (ImGui.Selectable(_customFonts[i], isSelected))
                        {
                            Settings.CustomFontFile = _customFonts[i];
                            KeyViewerFontRuntime.Reset();
                        }
                        if (isSelected) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            if (_customFonts.Length > 0 && string.IsNullOrWhiteSpace(Settings.CustomFontFile))
            {
                Settings.CustomFontFile = _customFonts[0];
                KeyViewerFontRuntime.Reset();
            }
        }

        ImGui.SliderFloat("Key label size", ref Settings.KeyFontSize, 8f, 72f, "%.1f px");
        ImGui.SliderFloat("KPS label/value size", ref Settings.KpsFontSize, 8f, 72f, "%.1f px");
        ImGui.SliderFloat("Total label/value size", ref Settings.TotalFontSize, 8f, 72f, "%.1f px");
        ImGui.Checkbox("Per-key label size", ref Settings.EnablePerKeyTextSize);
        if (Settings.EnablePerKeyTextSize)
        {
            DrawAppearanceKeySelector();
            int slot = AppearanceSlot();
            if (slot >= 0 && slot < Settings.PerKeyFontSize.Length)
            {
                float value = Settings.PerKeyFontSize[slot];
                if (ImGui.SliderFloat("Selected key size (0 = global)", ref value, 0f, 72f, "%.1f px"))
                    Settings.PerKeyFontSize[slot] = value;
            }
        }

        ImGui.InputText("KPS label", ref Settings.KpsLabel, 32);
        ImGui.InputText("Total label", ref Settings.TotalLabel, 32);
        ImGui.Checkbox("Hide KPS/Total labels", ref Settings.HideKpsTotalLabel);
        if (!Settings.HideKpsTotalLabel)
        {
            ImGui.Checkbox("Center KPS/Total text", ref Settings.KpsTotalCentered);
            if (Settings.KpsTotalCentered)
                ImGui.Checkbox("Stack label above value", ref Settings.KpsTotalStacked);
        }
        ImGui.EndTabItem();
    }

    private void DrawColorSettingsTab()
    {
        if (!ImGui.BeginTabItem("Colors"))
            return;

        DrawColor("Key background", Settings.Background);
        DrawColor("Key background (pressed)", Settings.BackgroundPressed);
        DrawColor("Key outline", Settings.Outline);
        DrawColor("Key outline (pressed)", Settings.OutlinePressed);
        DrawColor("Key text", Settings.Text);
        DrawColor("Key text (pressed)", Settings.TextPressed);
        DrawColor("Rain", Settings.RainColor);

        ImGui.Separator();
        ImGui.TextUnformatted("KPS");
        DrawColor("KPS background", Settings.KpsBackground);
        DrawColor("KPS outline", Settings.KpsOutline);
        DrawColor("KPS text", Settings.KpsText);
        ImGui.TextUnformatted("Total");
        DrawColor("Total background", Settings.TotalBackground);
        DrawColor("Total outline", Settings.TotalOutline);
        DrawColor("Total text", Settings.TotalText);

        ImGui.Separator();
        ImGui.Checkbox("Per-key colors", ref Settings.EnablePerKeyColors);
        if (Settings.EnablePerKeyColors)
        {
            DrawAppearanceKeySelector();
            int slot = AppearanceSlot();
            if (slot >= 0 && slot < Settings.PerKeyBackground.Length)
            {
                DrawColor("Selected background", Settings.PerKeyBackground[slot]);
                DrawColor("Selected background (pressed)", Settings.PerKeyBackgroundPressed[slot]);
                DrawColor("Selected outline", Settings.PerKeyOutline[slot]);
                DrawColor("Selected outline (pressed)", Settings.PerKeyOutlinePressed[slot]);
                DrawColor("Selected text", Settings.PerKeyText[slot]);
                DrawColor("Selected text (pressed)", Settings.PerKeyTextPressed[slot]);
                DrawColor("Selected rain", Settings.PerKeyRainColor[slot]);
            }
        }
        ImGui.EndTabItem();
    }

    private void DrawAppearanceKeySelector()
    {
        string[] names = Settings.CurrentBindings;
        int count = _appearanceKeyFoot ? Settings.FootBindings.Length : names.Length;
        if (count == 0)
        {
            ImGui.TextDisabled("No foot keys in this layout");
            ImGui.SameLine();
            if (ImGui.SmallButton("Main keys##appearance-key-kind"))
            {
                _appearanceKeyFoot = false;
                _appearanceKeyIndex = 0;
            }
            return;
        }
        _appearanceKeyIndex = Math.Clamp(_appearanceKeyIndex, 0, Math.Max(0, count - 1));
        string selected = _appearanceKeyFoot
            ? $"Foot {Settings.FootBindings[_appearanceKeyIndex]}"
            : PrettyBindingName(names[_appearanceKeyIndex]);
        if (ImGui.BeginCombo("Selected key", selected))
        {
            for (int i = 0; i < count; i++)
            {
                string label = _appearanceKeyFoot
                    ? $"Foot {Settings.FootBindings[i]}"
                    : PrettyBindingName(names[i]);
                bool isSelected = i == _appearanceKeyIndex;
                if (ImGui.Selectable(label, isSelected)) _appearanceKeyIndex = i;
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(_appearanceKeyFoot ? "Main keys##appearance-key-kind" : "Foot keys##appearance-key-kind"))
        {
            _appearanceKeyFoot = !_appearanceKeyFoot;
            _appearanceKeyIndex = 0;
        }
    }

    private int AppearanceSlot()
        => _appearanceKeyFoot && Settings.FootBindings.Length == 0
            ? -1
            : _appearanceKeyFoot ? 24 + _appearanceKeyIndex : _appearanceKeyIndex;

    private static string PrettyBindingName(string binding)
        => string.IsNullOrWhiteSpace(binding) ? "-" : binding switch
        {
            "Backspace" => "Back",
            "CapsLock" => "Caps",
            "LeftShift" => "LShift",
            "RightShift" => "RShift",
            "LeftCtrl" => "LCtrl",
            "RightCtrl" => "RCtrl",
            "Equal" => "=",
            "Comma" => ",",
            "Period" => ".",
            _ when binding.StartsWith('_') && binding.Length == 2 => binding[1..],
            _ => binding,
        };

    private static void DrawColor(string label, float[] value)
    {
        if (value == null || value.Length < 4) return;
        var color = new Vector4(value[0], value[1], value[2], value[3]);
        if (!ImGui.ColorEdit4(label, ref color, ImGuiColorEditFlags.AlphaBar)) return;
        value[0] = Math.Clamp(color.X, 0f, 1f);
        value[1] = Math.Clamp(color.Y, 0f, 1f);
        value[2] = Math.Clamp(color.Z, 0f, 1f);
        value[3] = Math.Clamp(color.W, 0f, 1f);
    }

    private void DrawKeyBindingsTab()
    {
        if (!ImGui.BeginTabItem("Keys"))
            return;

        DrawBindingFields(Settings.CurrentBindings, Settings.CurrentLabels, false);
        if (Settings.FootKeyCount > 0)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Foot bindings");
            DrawBindingFields(Settings.FootBindings, Settings.FootLabels, true);
        }
        ImGui.EndTabItem();
    }

    private void DrawUpdateTab()
    {
        if (!ImGui.BeginTabItem("Updates"))
            return;

        _updateService?.DrawGui();
        ImGui.EndTabItem();
    }

    private void AutoSaveCounts(long now)
    {
        if (Settings.TotalCount == _lastSavedTotalCount || now < _nextCountSaveTicks)
            return;
        if (_settingsStore.SaveCounts(Settings))
        {
            _lastSavedTotalCount = Settings.TotalCount;
            _nextCountSaveTicks = now + 250;
        }
    }

    private void SaveSettingsNow()
    {
        if (_settingsStore.Save(Settings))
            _lastSavedTotalCount = Settings.TotalCount;
    }

    private static void DrawBindingFields(string[] bindings, string[] labels, bool foot)
    {
        for (int i = 0; i < bindings.Length; i++)
        {
            ImGui.PushID((foot ? "foot" : "main") + i.ToString());
            string binding = bindings[i];
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText($"Key {i + 1} binding", ref binding, 32))
                bindings[i] = binding;
            string label = labels[i];
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("Label (optional)", ref label, 32))
                labels[i] = label;
            ImGui.PopID();
        }
    }
}

internal static class PluginLog
{
    internal static void Info(string message) => Logger.Info("JipperKeyViewer", message);
    internal static void Debug(string message) => Logger.Debug("JipperKeyViewer", message);
    internal static void Warn(string message) => Logger.Warn("JipperKeyViewer", message);
    internal static void Error(string message) => Logger.Error("JipperKeyViewer", message);
}
