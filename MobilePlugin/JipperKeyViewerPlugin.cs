using System.Reflection;
using System.Numerics;
using ImGuiNET;
using StArray.ModManager.Android.Native;
using StArray.ModManager.Behaviours;
using StArray.ModManager.Manager;
using StArray.ModManager.Runtime;

[assembly: ModEntryPoint(typeof(JipperKeyViewer.Mobile.JipperKeyViewerPlugin))]

namespace JipperKeyViewer.Mobile;

public sealed class JipperKeyViewerPlugin : IModPlugin, IModSettings
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

    public JipperKeyViewerPlugin()
    {
        _modDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? AppContext.BaseDirectory;
        _settingsStore = new SettingsStore(_modDirectory);
        Settings = _settingsStore.Load();
    }

    public KeyViewerSettings Settings { get; private set; }
    internal GameApi? Game => _game;
    internal bool IsLoaded => _loaded;

    public string Id => "JipperKeyViewer";
    public string Name => "Jipper Key Viewer Mobile";
    public string Version => "1.6.5-mobile.17";
    public string Author => "HitMargin / mobile port";
    public string Description => "Jipper Key Viewer touch and keyboard overlay for ADOFAI Android";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    public void OnLoad()
    {
        _loaded = true;
        Settings ??= new KeyViewerSettings();
        Settings.Normalize();
        _replayApi.TryBind();
        KeyViewerRuntime.Reset(Settings);
        _settingsPanelDrawnThisFrame = false;
        _lastSavedTotalCount = Settings.TotalCount;
        _nextCountSaveTicks = 0;
        _updateService = new GitHubUpdateService(_modDirectory, Version);
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

        ImGui.Separator();
        ImGui.TextUnformatted("Layout");
        ImGui.SliderFloat("Scale", ref Settings.Scale, 0.45f, 2f, "%.2f");
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

        ImGui.Checkbox("Rain effect", ref Settings.EnableRain);
        if (Settings.EnableRain)
        {
            ImGui.SliderFloat("Rain speed", ref Settings.RainSpeed, 20f, 1000f, "%.0f");
            ImGui.SliderFloat("Rain height", ref Settings.RainHeight, 20f, 600f, "%.0f");
            ImGui.SliderFloat("Rain edge fade", ref Settings.RainFadePixels, 0f, 200f, "%.0f px");
            ImGui.SliderFloat("Rain length", ref Settings.RainLength, 0.1f, 3f, "%.2fx");
            ImGui.SliderFloat("Rain width", ref Settings.RainWidth, 0.1f, 2f, "%.2fx key");
        }

        if (ImGui.Button("Clear counts"))
        {
            KeyViewerRuntime.ClearCounts(Settings);
            SaveSettingsNow();
            _notice = "Counts cleared";
            _noticeUntilUtc = DateTime.UtcNow.AddSeconds(3);
        }

        if (ImGui.CollapsingHeader("Key bindings"))
        {
            DrawBindingFields(Settings.CurrentBindings, Settings.CurrentLabels, false);
            if (Settings.FootKeyCount > 0)
            {
                ImGui.Separator();
                ImGui.TextUnformatted("Foot bindings");
                DrawBindingFields(Settings.FootBindings, Settings.FootLabels, true);
            }
        }

        if (ImGui.Button("Save settings"))
        {
            SaveSettingsNow();
            _notice = "Settings saved";
            _noticeUntilUtc = DateTime.UtcNow.AddSeconds(3);
        }
        if (DateTime.UtcNow < _noticeUntilUtc)
            ImGui.SameLine();
        if (DateTime.UtcNow < _noticeUntilUtc)
            ImGui.TextDisabled(_notice);

        _updateService?.DrawGui();
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
