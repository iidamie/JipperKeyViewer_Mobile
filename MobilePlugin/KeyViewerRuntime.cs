using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using ImGuiNET;
using StArray.ModManager.Android.Native;

namespace JipperKeyViewer.Mobile;

internal static unsafe class KeyViewerRuntime
{
    private const int MaxMainKeys = 24;
    private const int MaxFootKeys = 16;
    private const int MaxSlots = MaxMainKeys + MaxFootKeys;
    private const int TouchQueueCapacity = 1024;

    private readonly record struct TouchBinding(int Index, bool Foot);
    private readonly record struct ReplayTouchEvent(
        AndroidInput.MotionAction Action,
        int PointerId,
        float X,
        float Y,
        float SourceWidth,
        float SourceHeight);
    private readonly record struct ReplayKeyboardEvent(string Binding, int Action, int Repeat);

    private sealed class RainDrop
    {
        internal int Index;
        internal bool Foot;
        internal float Started;
        internal float? Released;
        internal float ReleaseTravel;
    }

    private static readonly ConcurrentQueue<TouchEventInfo> TouchEvents = new();
    private static readonly ConcurrentQueue<ReplayTouchEvent> ReplayTouchEvents = new();
    private static readonly ConcurrentQueue<ReplayKeyboardEvent> ReplayKeyboardEvents = new();
    private static readonly HashSet<ImGuiKey> ReplayDownKeys = new();
    private static readonly Dictionary<int, TouchBinding> ActivePointers = new();
    private static readonly Dictionary<int, TouchBinding> ReplayPointers = new();
    private static readonly int[] TouchMainCounts = new int[MaxMainKeys];
    private static readonly int[] TouchFootCounts = new int[MaxFootKeys];
    private static readonly bool[] KeyboardMainPressed = new bool[MaxMainKeys];
    private static readonly bool[] KeyboardFootPressed = new bool[MaxFootKeys];
    private static readonly bool[] MainPressed = new bool[MaxMainKeys];
    private static readonly bool[] FootPressed = new bool[MaxFootKeys];
    private static readonly int[] ReplayMainCounts = new int[MaxMainKeys];
    private static readonly int[] ReplayFootCounts = new int[MaxFootKeys];
    private static readonly bool[] ReplayMainPressed = new bool[MaxMainKeys];
    private static readonly bool[] ReplayFootPressed = new bool[MaxFootKeys];
    private static readonly string?[] CachedBindingValues = new string?[MaxSlots];
    private static readonly string?[] CachedCustomLabels = new string?[MaxSlots];
    private static readonly ImGuiKey[] CachedBindingKeys = new ImGuiKey[MaxSlots];
    private static readonly string[] CachedDisplayLabels = new string[MaxSlots];
    private static readonly bool[] BindingCacheInitialized = new bool[MaxSlots];
    private static readonly int[] FrontSequence = { 0, 1, 2, 3, 4, 5, 6, 7 };
    private static readonly int[] FootSequence =
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
    };
    private static readonly Queue<float> PressTimes = new(256);
    private static readonly Queue<float>[] SlotPressTimes = CreateSlotQueues();
    private static readonly List<KeyRect> Geometry = new(32);
    private static readonly List<StatusRect> StatusGeometry = new(2);
    private static readonly List<RainDrop> RainDrops = new(64);

    private static Vector2 _boundsMin;
    private static Vector2 _boundsMax;
    private static float _time;
    private static int _kps;
    private static int _layout = -1;
    private static int _footCount = -1;
    private static bool _active;
    private static bool _settingsPanelVisible;

    internal static void Reset(KeyViewerSettings settings)
    {
        KeyViewerKeyboardInput.Reset();
        DrainTouchEvents();
        DrainReplayTouchEvents();
        DrainReplayKeyboardEvents();
        Array.Clear(TouchMainCounts);
        Array.Clear(TouchFootCounts);
        Array.Clear(KeyboardMainPressed);
        Array.Clear(KeyboardFootPressed);
        Array.Clear(MainPressed);
        Array.Clear(FootPressed);
        Array.Clear(ReplayMainCounts);
        Array.Clear(ReplayFootCounts);
        Array.Clear(ReplayMainPressed);
        Array.Clear(ReplayFootPressed);
        Array.Clear(BindingCacheInitialized);
        Array.Clear(CachedBindingKeys);
        Array.Clear(CachedDisplayLabels);
        ActivePointers.Clear();
        ReplayPointers.Clear();
        ReplayDownKeys.Clear();
        PressTimes.Clear();
        foreach (Queue<float> queue in SlotPressTimes) queue.Clear();
        RainDrops.Clear();
        Geometry.Clear();
        StatusGeometry.Clear();
        _boundsMin = Vector2.Zero;
        _boundsMax = Vector2.Zero;
        _time = 0f;
        _kps = 0;
        _layout = (int)settings.Layout;
        _footCount = settings.FootKeyCount;
        _active = false;
        _settingsPanelVisible = false;
    }

    internal static void ResetInputState()
    {
        KeyViewerKeyboardInput.ResetDownState();
        DrainTouchEvents();
        DrainReplayTouchEvents();
        DrainReplayKeyboardEvents();
        ActivePointers.Clear();
        ReplayPointers.Clear();
        ReplayDownKeys.Clear();
        Array.Clear(TouchMainCounts);
        Array.Clear(TouchFootCounts);
        Array.Clear(KeyboardMainPressed);
        Array.Clear(KeyboardFootPressed);
        Array.Clear(MainPressed);
        Array.Clear(FootPressed);
        Array.Clear(ReplayMainCounts);
        Array.Clear(ReplayFootCounts);
        Array.Clear(ReplayMainPressed);
        Array.Clear(ReplayFootPressed);
        RainDrops.Clear();
        PressTimes.Clear();
        foreach (Queue<float> queue in SlotPressTimes) queue.Clear();
        _kps = 0;
    }

    internal static void ClearCounts(KeyViewerSettings settings)
    {
        Array.Clear(settings.Counts);
        settings.TotalCount = 0;
        PressTimes.Clear();
        foreach (Queue<float> queue in SlotPressTimes) queue.Clear();
        _kps = 0;
    }

    internal static void EnqueueTouch(TouchEventInfo info)
    {
        while (TouchEvents.Count >= TouchQueueCapacity && TouchEvents.TryDequeue(out _)) { }
        TouchEvents.Enqueue(info);
    }

    internal static void EnqueueReplayTouch(
        AndroidInput.MotionAction action,
        int pointerId,
        float x,
        float y,
        float sourceWidth,
        float sourceHeight)
    {
        while (ReplayTouchEvents.Count >= TouchQueueCapacity && ReplayTouchEvents.TryDequeue(out _)) { }
        ReplayTouchEvents.Enqueue(new ReplayTouchEvent(
            action, pointerId, x, y, sourceWidth, sourceHeight));
    }

    internal static void EnqueueReplayKeyboard(string binding, int action, int repeat)
    {
        if (string.IsNullOrWhiteSpace(binding))
            return;
        while (ReplayKeyboardEvents.Count >= TouchQueueCapacity
            && ReplayKeyboardEvents.TryDequeue(out _)) { }
        ReplayKeyboardEvents.Enqueue(new ReplayKeyboardEvent(binding, action, repeat));
    }

    internal static void OnReplayStarted() => ResetInputState();

    internal static void OnReplayEnded() => ResetInputState();

    internal static void Update(JipperKeyViewerPlugin plugin, float delta, bool replayPlayback)
    {
        KeyViewerSettings settings = plugin.Settings;
        bool settingsPanelVisible = plugin.ConsumeSettingsPanelVisibility();
        _settingsPanelVisible = settingsPanelVisible;
        bool physicalKeyboardInput = !replayPlayback && settings.KeyboardInputEnabled;
        if (physicalKeyboardInput)
            KeyViewerKeyboardInput.Update();
        Vector2 display = ImGui.GetIO().DisplaySize;
        if (display.X <= 1f || display.Y <= 1f) return;

        int footCount = Math.Clamp(settings.FootKeyCount, 0, MaxFootKeys);
        if (_layout != (int)settings.Layout || _footCount != footCount)
        {
            ResetInputState();
            _layout = (int)settings.Layout;
            _footCount = footCount;
        }

        KeyViewerLayout.Build(
            settings,
            display,
            Geometry,
            StatusGeometry,
            out _boundsMin,
            out _boundsMax);
        _time += Math.Clamp(delta, 0f, 0.25f);
        bool active = settings.Enabled && (_settingsPanelVisible
            || !settings.ShowOnlyInGameplay
            || plugin.IsGameplayActive());
        if (!active)
        {
            if (_active) ResetInputState();
            _active = false;
            TrimRain(settings);
            return;
        }

        if (!_active)
            ResetInputState();
        _active = true;

        if (replayPlayback)
        {
            ClearPhysicalTouchInputState();
            ProcessReplayTouchEvents(settings, display);
            ProcessReplayKeyboardEvents();
            ApplyReplayKeyboardState(settings);
        }
        else
        {
            ClearReplayTouchInputState(clearRain: false);
            ClearReplayKeyboardInputState();
            if (settings.TouchInputEnabled)
                ProcessTouchEvents(settings, display);
            else
                ClearTouchInputState(clearRain: false);

            if (settings.KeyboardInputEnabled)
                PollKeyboard(settings);
            else
                ClearKeyboardInputState();
        }

        ProcessKeyStates(settings);
        if (physicalKeyboardInput)
            KeyViewerKeyboardInput.ClearFramePresses();
        TrimPressTimes(settings);
        TrimRain(settings);
    }

    internal static void Render(JipperKeyViewerPlugin plugin, ImDrawListPtr drawList)
    {
        KeyViewerSettings settings = plugin.Settings;
        if (!settings.Enabled) return;

        Vector2 display = ImGui.GetIO().DisplaySize;
        bool gameplayVisible = _settingsPanelVisible
            || !settings.ShowOnlyInGameplay
            || plugin.IsGameplayActive();
        bool previewOnly = settings.ShowTouchRegions && (!_active || !gameplayVisible);
        if (!_active || !gameplayVisible)
        {
            if (previewOnly)
            {
                DrawTouchRegions(settings, display, drawList);
                DrawPreviewStatus(plugin, drawList);
            }
            return;
        }
        if (Geometry.Count == 0) return;

        DrawRain(settings, drawList);
        ImFontPtr font = KeyViewerFontRuntime.GetFont(settings, plugin.ModDirectory);
        foreach (KeyRect rect in Geometry)
            DrawKey(settings, rect, font, drawList);

        if (settings.ShowTouchRegions)
            DrawTouchRegions(settings, display, drawList);

        if (!settings.StreamerMode)
        {
            foreach (StatusRect rect in StatusGeometry)
                DrawStatusKey(settings, rect, font, drawList);
        }
    }

    private static void ProcessTouchEvents(KeyViewerSettings settings, Vector2 display)
    {
        while (TouchEvents.TryDequeue(out TouchEventInfo info))
        {
            switch (info.Action)
            {
                case AndroidInput.MotionAction.Down:
                case AndroidInput.MotionAction.PointerDown:
                    PressPointer(settings, display, info.PointerId, info.X, info.Y);
                    break;
                case AndroidInput.MotionAction.Up:
                case AndroidInput.MotionAction.PointerUp:
                    ReleasePointer(info.PointerId);
                    break;
                case AndroidInput.MotionAction.Cancel:
                    ActivePointers.Clear();
                    Array.Clear(TouchMainCounts);
                    Array.Clear(TouchFootCounts);
                    break;
            }
        }
    }

    private static void ClearPhysicalTouchInputState()
    {
        ClearTouchInputState(clearRain: false);
        Array.Clear(KeyboardMainPressed);
        Array.Clear(KeyboardFootPressed);
    }

    private static void ClearReplayTouchInputState(bool clearRain)
    {
        DrainReplayTouchEvents();
        ReplayPointers.Clear();
        Array.Clear(ReplayMainCounts);
        Array.Clear(ReplayFootCounts);
        Array.Clear(ReplayMainPressed);
        Array.Clear(ReplayFootPressed);
        if (clearRain)
            RainDrops.Clear();
    }

    private static void ClearReplayKeyboardInputState()
    {
        DrainReplayKeyboardEvents();
        ReplayDownKeys.Clear();
        Array.Clear(KeyboardMainPressed);
        Array.Clear(KeyboardFootPressed);
    }

    private static void ClearKeyboardInputState()
    {
        Array.Clear(KeyboardMainPressed);
        Array.Clear(KeyboardFootPressed);
    }

    private static void ProcessReplayKeyboardEvents()
    {
        while (ReplayKeyboardEvents.TryDequeue(out ReplayKeyboardEvent input))
        {
            if (!KeyViewerKeyMap.TryParse(input.Binding, out ImGuiKey key))
                continue;
            if (input.Action == 1)
                ReplayDownKeys.Remove(key);
            else
                ReplayDownKeys.Add(key);
        }
    }

    private static void ApplyReplayKeyboardState(KeyViewerSettings settings)
    {
        Array.Clear(KeyboardMainPressed);
        Array.Clear(KeyboardFootPressed);
        int mainCount = Defaults.Count(settings.Layout);
        for (int i = 0; i < mainCount; i++)
        {
            ImGuiKey key = GetCachedBindingKey(settings, i, foot: false);
            KeyboardMainPressed[i] = key != ImGuiKey.None && ReplayDownKeys.Contains(key);
        }
        int footCount = Math.Clamp(settings.FootKeyCount, 0, MaxFootKeys);
        for (int i = 0; i < footCount; i++)
        {
            ImGuiKey key = GetCachedBindingKey(settings, i, foot: true);
            KeyboardFootPressed[i] = key != ImGuiKey.None && ReplayDownKeys.Contains(key);
        }
    }

    private static void ProcessReplayTouchEvents(KeyViewerSettings settings, Vector2 display)
    {
        while (ReplayTouchEvents.TryDequeue(out ReplayTouchEvent info))
        {
            switch (info.Action)
            {
                case AndroidInput.MotionAction.Down:
                case AndroidInput.MotionAction.PointerDown:
                    float x = info.SourceWidth > 0f
                        ? info.X / info.SourceWidth * display.X
                        : info.X;
                    float y = info.SourceHeight > 0f
                        ? info.Y / info.SourceHeight * display.Y
                        : info.Y;
                    PressPointer(settings, display, info.PointerId, x, y, replay: true);
                    break;
                case AndroidInput.MotionAction.Up:
                case AndroidInput.MotionAction.PointerUp:
                    ReleasePointer(info.PointerId, replay: true);
                    break;
                case AndroidInput.MotionAction.Cancel:
                    ClearReplayTouchInputState(clearRain: false);
                    break;
            }
        }
    }

    private static void ClearTouchInputState(bool clearRain)
    {
        DrainTouchEvents();
        ActivePointers.Clear();
        Array.Clear(TouchMainCounts);
        Array.Clear(TouchFootCounts);
        if (clearRain)
            RainDrops.Clear();
    }

    private static void PressPointer(
        KeyViewerSettings settings,
        Vector2 display,
        int pointerId,
        float x,
        float y,
        bool replay = false)
    {
        ReleasePointer(pointerId, replay);

        float normalizedY = Math.Clamp(y / Math.Max(1f, display.Y), 0f, 0.999999f);
        int footCount = Math.Clamp(settings.FootKeyCount, 0, MaxFootKeys);
        float footHeight = settings.TouchFootAreaEnabled && footCount > 0
            ? Math.Clamp(settings.TouchFootAreaHeight, 0.08f, 0.35f) : 0f;
        float handHeight = 1f - footHeight;

        if (footHeight > 0f && normalizedY >= handHeight)
        {
            int foot = SelectNearestTouchKey(
                FootSequence,
                footCount,
                true,
                x,
                display.X,
                replay ? ReplayPointers : ActivePointers);
            int[] footCounts = replay ? ReplayFootCounts : TouchFootCounts;
            footCounts[foot]++;
            (replay ? ReplayPointers : ActivePointers)[pointerId] = new TouchBinding(foot, true);
            return;
        }

        int mainRows = KeyViewerLayout.MainRows(settings.Layout);
        int row = Math.Clamp((int)(normalizedY / Math.Max(0.0001f, handHeight) * mainRows), 0, mainRows - 1);
        IReadOnlyList<int> rowKeys = TouchRow(settings.Layout, row);
        if (rowKeys.Count == 0) return;
        int key = SelectNearestTouchKey(
            rowKeys,
            rowKeys.Count,
            false,
            x,
            display.X,
            replay ? ReplayPointers : ActivePointers);
        int[] mainCounts = replay ? ReplayMainCounts : TouchMainCounts;
        mainCounts[key]++;
        (replay ? ReplayPointers : ActivePointers)[pointerId] = new TouchBinding(key, false);
    }

    private static int SelectNearestTouchKey(
        IReadOnlyList<int> candidates,
        int candidateCount,
        bool foot,
        float touchX,
        float displayWidth,
        Dictionary<int, TouchBinding> pointers)
    {
        int nearest = candidates[0];
        int nearestAvailable = -1;
        float nearestDistance = float.MaxValue;
        float nearestAvailableDistance = float.MaxValue;

        // Touch down events are dequeued chronologically. The first finger claims
        // the closest key; later fingers skip already claimed keys and take the
        // closest remaining key in the same touch row/area.
        for (int position = 0; position < candidateCount; position++)
        {
            int key = candidates[position];
            float center = TouchKeyCenter(position, candidateCount, displayWidth);
            float distance = MathF.Abs(touchX - center);
            if (distance < nearestDistance)
            {
                nearest = key;
                nearestDistance = distance;
            }

            if (IsTouchKeyOccupied(key, foot, pointers))
                continue;
            if (distance < nearestAvailableDistance)
            {
                nearestAvailable = key;
                nearestAvailableDistance = distance;
            }
        }

        return nearestAvailable >= 0 ? nearestAvailable : nearest;
    }

    private static float TouchKeyCenter(
        int position,
        int candidateCount,
        float displayWidth)
        => displayWidth * (position + 0.5f) / Math.Max(1, candidateCount);

    private static bool IsTouchKeyOccupied(
        int index,
        bool foot,
        Dictionary<int, TouchBinding> pointers)
    {
        foreach (TouchBinding binding in pointers.Values)
        {
            if (binding.Index == index && binding.Foot == foot)
                return true;
        }
        return false;
    }

    private static void ReleasePointer(int pointerId, bool replay = false)
    {
        Dictionary<int, TouchBinding> pointers = replay ? ReplayPointers : ActivePointers;
        if (!pointers.Remove(pointerId, out TouchBinding binding)) return;
        if (binding.Foot)
        {
            int[] counts = replay ? ReplayFootCounts : TouchFootCounts;
            counts[binding.Index] = Math.Max(0, counts[binding.Index] - 1);
            return;
        }
        int[] mainCounts = replay ? ReplayMainCounts : TouchMainCounts;
        mainCounts[binding.Index] = Math.Max(0, mainCounts[binding.Index] - 1);
    }

    private static void PollKeyboard(KeyViewerSettings settings)
    {
        Array.Clear(KeyboardMainPressed);
        Array.Clear(KeyboardFootPressed);
        if (!settings.KeyboardInputEnabled) return;

        for (int i = 0; i < Defaults.Count(settings.Layout); i++)
        {
            ImGuiKey key = GetCachedBindingKey(settings, i, foot: false);
            KeyboardMainPressed[i] = key != ImGuiKey.None
                && (KeyViewerKeyboardInput.IsDown(key) || KeyViewerKeyboardInput.WasPressed(key));
        }
        int footCount = Math.Clamp(settings.FootKeyCount, 0, MaxFootKeys);
        for (int i = 0; i < footCount; i++)
        {
            ImGuiKey key = GetCachedBindingKey(settings, i, foot: true);
            KeyboardFootPressed[i] = key != ImGuiKey.None
                && (KeyViewerKeyboardInput.IsDown(key) || KeyViewerKeyboardInput.WasPressed(key));
        }
    }

    private static void ProcessKeyStates(KeyViewerSettings settings)
    {
        int mainCount = Defaults.Count(settings.Layout);
        for (int i = 0; i < mainCount; i++)
            ProcessSlot(
                settings,
                i,
                false,
                TouchMainCounts[i] > 0 || KeyboardMainPressed[i] || ReplayMainCounts[i] > 0);
        int footCount = Math.Clamp(settings.FootKeyCount, 0, MaxFootKeys);
        for (int i = 0; i < footCount; i++)
            ProcessSlot(
                settings,
                i,
                true,
                TouchFootCounts[i] > 0 || KeyboardFootPressed[i] || ReplayFootCounts[i] > 0);
    }

    private static void ProcessSlot(KeyViewerSettings settings, int index, bool foot, bool current)
    {
        bool[] state = foot ? FootPressed : MainPressed;
        if (state[index] == current) return;
        state[index] = current;
        int slot = SlotIndex(index, foot);
        if (current)
        {
            settings.Counts[slot] = Math.Max(0, settings.Counts[slot]) + 1;
            settings.TotalCount = Math.Max(0, settings.TotalCount) + 1;
            PressTimes.Enqueue(_time);
            SlotPressTimes[slot].Enqueue(_time);
            if (settings.EnableRain)
            {
                KeyRect? rect = FindRect(index, foot);
                if (rect.HasValue)
                {
                    KeyRect value = rect.Value;
                    RainDrops.Add(new RainDrop
                    {
                        Index = index,
                        Foot = foot,
                        Started = _time,
                    });
                }
            }
        }
        else
        {
            for (int i = RainDrops.Count - 1; i >= 0; i--)
            {
                RainDrop drop = RainDrops[i];
                if (!drop.Released.HasValue && IsSameRect(drop, index, foot))
                {
                    drop.Released = _time;
                    float speed = Math.Max(20f, settings.RainSpeed);
                    drop.ReleaseTravel = Math.Max(0f, (_time - drop.Started) * speed);
                    break;
                }
            }
        }
    }

    private static KeyRect? FindRect(int index, bool foot)
    {
        foreach (KeyRect rect in Geometry)
            if (rect.Index == index && rect.Foot == foot)
                return rect;
        return null;
    }

    private static bool IsSameRect(RainDrop drop, int index, bool foot)
        => drop.Index == index && drop.Foot == foot;

    private static void TrimPressTimes(KeyViewerSettings settings)
    {
        while (PressTimes.Count > 0 && _time - PressTimes.Peek() > 1f)
            PressTimes.Dequeue();
        _kps = PressTimes.Count;
        for (int i = 0; i < MaxSlots; i++)
        {
            Queue<float> queue = SlotPressTimes[i];
            while (queue.Count > 0 && _time - queue.Peek() > 1f)
                queue.Dequeue();
        }
    }

    private static void TrimRain(KeyViewerSettings settings)
    {
        float speed = Math.Max(20f, settings.RainSpeed);
        float maxHeight = Math.Max(20f, settings.RainHeight);
        float fadePixels = Math.Clamp(settings.RainFadePixels, 0f, maxHeight);
        float fadeEnd = maxHeight + fadePixels;
        for (int i = RainDrops.Count - 1; i >= 0; i--)
        {
            RainDrop drop = RainDrops[i];
            if (drop.Released.HasValue
                && (_time - drop.Released.Value) * speed >= fadeEnd)
                RainDrops.RemoveAt(i);
        }
    }

    private static void DrawRain(KeyViewerSettings settings, ImDrawListPtr drawList)
    {
        if (!settings.EnableRain) return;
        float speed = Math.Max(20f, settings.RainSpeed);
        float maxHeight = Math.Max(20f, settings.RainHeight);
        float fadePixels = Math.Clamp(settings.RainFadePixels, 0f, maxHeight);
        float lengthScale = Math.Clamp(settings.RainLength, 0.1f, 3f);
        float widthScale = Math.Clamp(settings.RainWidth, 0.1f, 2f);
        foreach (RainDrop drop in RainDrops)
        {
            KeyRect? currentRect = FindRect(drop.Index, drop.Foot);
            if (!currentRect.HasValue) continue;
            KeyRect rect = currentRect.Value;
            float travel = Math.Max(0f, (_time - drop.Started) * speed);
            float nearDistance;
            float farDistance;
            if (drop.Released.HasValue)
            {
                float afterRelease = Math.Max(0f, (_time - drop.Released.Value) * speed);
                nearDistance = afterRelease;
                farDistance = afterRelease + drop.ReleaseTravel * lengthScale;
            }
            else
            {
                // While held, the rain always grows from the key's top edge.
                nearDistance = 0f;
                farDistance = travel * lengthScale;
            }
            farDistance = Math.Min(farDistance, maxHeight + fadePixels);
            if (farDistance - nearDistance <= 1f) continue;

            float keyWidth = rect.Max.X - rect.Min.X;
            float width = Math.Max(2f, keyWidth * widthScale);
            float x = (rect.Min.X + rect.Max.X - width) * 0.5f;
            float[] rainColor = GetRainColor(settings, drop);
            KeyViewerColorGradient? rainGradient = GetRainGradient(settings, drop);

            // Keep the part inside RainHeight solid. Only the overflow above that
            // boundary receives the vertical fade, otherwise the whole rain segment
            // would be interpolated from transparent to opaque.
            float solidFar = Math.Min(farDistance, maxHeight);
            if (solidFar - nearDistance > 1f)
            {
                Vector2 solidMin = new(x, rect.Min.Y - solidFar);
                Vector2 solidMax = new(x + width, rect.Min.Y - nearDistance);
                AddGradientRect(drawList, solidMin, solidMax, rainColor, rainGradient, 1f, 1f);
            }

            float fadeNear = Math.Max(nearDistance, maxHeight);
            float fadeFar = Math.Min(farDistance, maxHeight + fadePixels);
            if (fadeFar - fadeNear <= 1f) continue;
            float topAlpha = FadeBeyondRainHeight(fadeFar, maxHeight, fadePixels);
            float bottomAlpha = FadeBeyondRainHeight(fadeNear, maxHeight, fadePixels);
            Vector2 fadeMin = new(x, rect.Min.Y - fadeFar);
            Vector2 fadeMax = new(x + width, rect.Min.Y - fadeNear);
            AddGradientRect(drawList, fadeMin, fadeMax, rainColor, rainGradient, topAlpha, bottomAlpha);
        }
    }

    private static float FadeBeyondRainHeight(float distance, float height, float fadePixels)
    {
        if (distance <= height) return 1f;
        if (fadePixels <= 0f) return 0f;
        return distance >= height + fadePixels
            ? 0f
            : Math.Clamp(1f - (distance - height) / fadePixels, 0f, 1f);
    }

    private static void DrawKey(KeyViewerSettings settings, KeyRect rect, ImFontPtr font, ImDrawListPtr drawList)
    {
        bool pressed = rect.Foot ? FootPressed[rect.Index] : MainPressed[rect.Index];
        int slot = SlotIndex(rect.Index, rect.Foot);
        float[] backgroundColor = GetKeyColor(settings, slot, pressed, ColorRole.Background);
        float[] outlineColor = GetKeyColor(settings, slot, pressed, ColorRole.Outline);
        KeyViewerColorGradient? backgroundGradient = GetKeyGradient(
            settings, slot, pressed, ColorRole.Background);
        KeyViewerColorGradient? outlineGradient = GetKeyGradient(
            settings, slot, pressed, ColorRole.Outline);
        KeyViewerColorGradient? textGradient = GetKeyGradient(
            settings, slot, pressed, ColorRole.Text);
        float[] textColorArray = GetKeyColor(settings, slot, pressed, ColorRole.Text);
        uint textColor = ColorU32(ResolveColor(textColorArray, textGradient), 1f);
        float rounding = Math.Min(6f, (rect.Max.Y - rect.Min.Y) * 0.12f);
        DrawKeyBox(
            drawList,
            rect.Min,
            rect.Max,
            backgroundColor,
            backgroundGradient,
            outlineColor,
            outlineGradient,
            rounding,
            pressed ? 2f : 1.5f);

        string label = GetLabel(settings, rect);
        float height = rect.Max.Y - rect.Min.Y;
        float width = rect.Max.X - rect.Min.X;
        float fontSize = settings.KeyFontSize;
        if (settings.EnablePerKeyTextSize && slot < settings.PerKeyFontSize.Length
            && settings.PerKeyFontSize[slot] > 0f)
            fontSize = settings.PerKeyFontSize[slot];
        fontSize = Math.Clamp(fontSize, 8f, 72f);
        float textWidth = Math.Max(8f, width - 4f);
        fontSize = FitFontSize(font, label, fontSize, textWidth, 8f);
        label = FitText(font, label, fontSize, textWidth);
        Vector2 labelSize = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, label);
        Vector2 labelPosition = new(
            rect.Min.X + (rect.Max.X - rect.Min.X - labelSize.X) * 0.5f,
            rect.Min.Y + (height - labelSize.Y) * (settings.ShowMainKeyCount ? 0.30f : 0.5f));
        AddTextShadow(drawList, font, fontSize, labelPosition, textColor, label, textGradient);

        if (!settings.ShowMainKeyCount) return;
        string value = settings.ShowPerKeyKps
            ? SlotPressTimes[slot].Count.ToString(CultureInfo.InvariantCulture)
            : FormatCount(settings.Counts[slot], settings.EnableCountFormatting);
        float valueSize = Math.Clamp(fontSize * 0.54f, 8f, 40f);
        valueSize = FitFontSize(font, value, valueSize, textWidth, 7f);
        value = FitText(font, value, valueSize, textWidth);
        Vector2 valueMeasure = font.CalcTextSizeA(valueSize, float.MaxValue, 0f, value);
        Vector2 valuePosition = new(
            rect.Min.X + (rect.Max.X - rect.Min.X - valueMeasure.X) * 0.5f,
            rect.Max.Y - valueMeasure.Y - Math.Max(3f, height * 0.08f));
        AddColorText(drawList, font, valueSize, valuePosition, textColor, value, textGradient);
    }

    private static void DrawStatusKey(
        KeyViewerSettings settings,
        StatusRect rect,
        ImFontPtr font,
        ImDrawListPtr drawList)
    {
        float[] backgroundColor = rect.Total ? settings.TotalBackground : settings.KpsBackground;
        float[] outlineColor = rect.Total ? settings.TotalOutline : settings.KpsOutline;
        float[] textColorArray = rect.Total ? settings.TotalText : settings.KpsText;
        KeyViewerColorGradient? backgroundGradient = GetStatusGradient(settings, rect.Total, ColorRole.Background);
        KeyViewerColorGradient? outlineGradient = GetStatusGradient(settings, rect.Total, ColorRole.Outline);
        KeyViewerColorGradient? textGradient = GetStatusGradient(settings, rect.Total, ColorRole.Text);
        uint textColor = ColorU32(ResolveColor(textColorArray, textGradient), 1f);
        float height = rect.Max.Y - rect.Min.Y;
        float width = rect.Max.X - rect.Min.X;
        float rounding = Math.Min(6f, height * 0.12f);
        DrawKeyBox(
            drawList,
            rect.Min,
            rect.Max,
            backgroundColor,
            backgroundGradient,
            outlineColor,
            outlineGradient,
            rounding,
            1.5f);

        string label = rect.Total ? settings.TotalLabel : settings.KpsLabel;
        string value = rect.Total
            ? FormatCount(settings.TotalCount, settings.EnableCountFormatting)
            : _kps.ToString(CultureInfo.InvariantCulture);
        float padding = Math.Max(4f, height * 0.12f);
        float textSize = Math.Clamp(rect.Total ? settings.TotalFontSize : settings.KpsFontSize, 8f, 72f);
        if (settings.HideKpsTotalLabel)
        {
            float hiddenValueWidth = Math.Max(8f, width - padding * 2f);
            float hiddenValueSize = FitFontSize(font, value, textSize, hiddenValueWidth, 8f);
            value = FitText(font, value, hiddenValueSize, hiddenValueWidth);
            Vector2 hiddenValueMeasure = font.CalcTextSizeA(hiddenValueSize, float.MaxValue, 0f, value);
            Vector2 hiddenValuePosition = new(
                rect.Min.X + (width - hiddenValueMeasure.X) * 0.5f,
                rect.Min.Y + (height - hiddenValueMeasure.Y) * 0.5f);
            AddTextShadow(
                drawList,
                font,
                hiddenValueSize,
                hiddenValuePosition,
                textColor,
                value,
                textGradient);
            return;
        }

        bool stacked = settings.KpsTotalCentered && settings.KpsTotalStacked;
        bool centered = settings.KpsTotalCentered;
        float labelWidth = centered ? Math.Max(8f, width - padding * 2f) : Math.Max(8f, width * 0.45f - padding);
        float valueWidth = centered ? Math.Max(8f, width - padding * 2f) : Math.Max(8f, width * 0.52f - padding);
        float labelSize = FitFontSize(font, label, textSize, labelWidth, 8f);
        float valueSize = FitFontSize(font, value, textSize, valueWidth, 8f);
        label = FitText(font, label, labelSize, labelWidth);
        value = FitText(font, value, valueSize, valueWidth);
        Vector2 labelMeasure = font.CalcTextSizeA(labelSize, float.MaxValue, 0f, label);
        Vector2 valueMeasure = font.CalcTextSizeA(valueSize, float.MaxValue, 0f, value);
        float labelY = rect.Min.Y + (height - labelMeasure.Y) * 0.5f;
        float valueY = rect.Min.Y + (height - valueMeasure.Y) * 0.5f;
        Vector2 labelPosition;
        Vector2 valuePosition;
        if (stacked)
        {
            float gap = Math.Max(1f, height * 0.04f);
            float combinedHeight = labelMeasure.Y + gap + valueMeasure.Y;
            float top = rect.Min.Y + (height - combinedHeight) * 0.5f;
            labelPosition = new Vector2(rect.Min.X + (width - labelMeasure.X) * 0.5f, top);
            valuePosition = new Vector2(rect.Min.X + (width - valueMeasure.X) * 0.5f, top + labelMeasure.Y + gap);
        }
        else if (centered)
        {
            float combinedWidth = labelMeasure.X + Math.Max(4f, width * 0.04f) + valueMeasure.X;
            float left = rect.Min.X + (width - combinedWidth) * 0.5f;
            labelPosition = new Vector2(left, labelY);
            valuePosition = new Vector2(left + labelMeasure.X + Math.Max(4f, width * 0.04f), valueY);
        }
        else
        {
            labelPosition = new Vector2(rect.Min.X + padding, labelY);
            valuePosition = new Vector2(rect.Max.X - padding - valueMeasure.X, valueY);
        }
        AddTextShadow(
            drawList,
            font,
            labelSize,
            labelPosition,
            textColor,
            label,
            textGradient);
        AddTextShadow(
            drawList,
            font,
            valueSize,
            valuePosition,
            textColor,
            value,
            textGradient);
    }

    private static IReadOnlyList<int> TouchRow(KeyLayout layout, int rowFromTop)
    {
        int rows = KeyViewerLayout.MainRows(layout);
        if (rows == 1)
            return FrontSequence;

        if (rows == 2)
            return rowFromTop == 0
                ? KeyViewerLayout.BackSequence(layout)
                : FrontSequence;

        return rowFromTop switch
        {
            0 => KeyViewerLayout.ThirdSequence(layout),
            1 => KeyViewerLayout.BackSequence(layout),
            _ => FrontSequence,
        };
    }

    private static void DrawTouchRegions(KeyViewerSettings settings, Vector2 display, ImDrawListPtr drawList)
    {
        int mainRows = KeyViewerLayout.MainRows(settings.Layout);
        int footCount = settings.TouchFootAreaEnabled
            ? Math.Clamp(settings.FootKeyCount, 0, MaxFootKeys)
            : 0;
        float footHeight = footCount > 0
            ? Math.Clamp(settings.TouchFootAreaHeight, 0.08f, 0.35f)
            : 0f;
        float handBottom = display.Y * (1f - footHeight);
        uint handColor = 0xA8FFFFFFu;
        uint handBoundaryColor = 0xD8FFFFFFu;
        uint footColor = 0xB8FFCC66u;
        uint footBoundaryColor = 0xE8FFCC66u;

        for (int row = 0; row < mainRows; row++)
        {
            float top = handBottom * row / mainRows;
            float bottom = handBottom * (row + 1) / mainRows;
            IReadOnlyList<int> rowKeys = TouchRow(settings.Layout, row);
            for (int column = 0; column < rowKeys.Count - 1; column++)
            {
                float leftCenter = TouchKeyCenter(
                    column,
                    rowKeys.Count,
                    display.X);
                float rightCenter = TouchKeyCenter(
                    column + 1,
                    rowKeys.Count,
                    display.X);
                float x = (leftCenter + rightCenter) * 0.5f;
                drawList.AddLine(new Vector2(x, top), new Vector2(x, bottom), handColor, 2f);
            }

            drawList.AddLine(new Vector2(0f, top), new Vector2(display.X, top), handBoundaryColor, 2f);
        }

        drawList.AddLine(
            new Vector2(0f, handBottom),
            new Vector2(display.X, handBottom),
            footCount > 0 ? footBoundaryColor : handBoundaryColor,
            3f);

        if (footCount == 0) return;
        for (int column = 0; column < footCount - 1; column++)
        {
            float leftCenter = TouchKeyCenter(
                column,
                footCount,
                display.X);
            float rightCenter = TouchKeyCenter(
                column + 1,
                footCount,
                display.X);
            float x = (leftCenter + rightCenter) * 0.5f;
            drawList.AddLine(new Vector2(x, handBottom), new Vector2(x, display.Y), footColor, 2f);
        }
    }

    private static void DrawPreviewStatus(JipperKeyViewerPlugin plugin, ImDrawListPtr drawList)
    {
        string status = plugin.Game == null
            ? "Touch preview: waiting for ADOFAI runtime"
            : "Touch preview: enter gameplay to enable input";
        ImFontPtr font = KeyViewerFontRuntime.GetFont(plugin.Settings, plugin.ModDirectory);
        KeyViewerFontRuntime.AddText(
            drawList,
            font,
            Math.Clamp(ImGui.GetFontSize(), 12f, 24f),
            new Vector2(12f, 12f),
            0xF0FFFFFF,
            status);
    }

    private static string GetLabel(KeyViewerSettings settings, KeyRect rect)
    {
        EnsureBindingCache(settings, rect.Index, rect.Foot);
        return CachedDisplayLabels[SlotIndex(rect.Index, rect.Foot)];
    }

    private static ImGuiKey GetCachedBindingKey(KeyViewerSettings settings, int index, bool foot)
    {
        EnsureBindingCache(settings, index, foot);
        return CachedBindingKeys[SlotIndex(index, foot)];
    }

    private static void EnsureBindingCache(KeyViewerSettings settings, int index, bool foot)
    {
        int slot = SlotIndex(index, foot);
        string binding = foot ? settings.FootBindings[index] : settings.KeyBindings[index];
        string customLabel = foot ? settings.FootLabels[index] : settings.KeyLabels[index];
        binding ??= string.Empty;
        customLabel ??= string.Empty;

        if (BindingCacheInitialized[slot]
            && string.Equals(CachedBindingValues[slot], binding, StringComparison.Ordinal)
            && string.Equals(CachedCustomLabels[slot], customLabel, StringComparison.Ordinal))
            return;

        BindingCacheInitialized[slot] = true;
        CachedBindingValues[slot] = binding;
        CachedCustomLabels[slot] = customLabel;
        CachedBindingKeys[slot] = KeyViewerKeyMap.TryParse(binding, out ImGuiKey key)
            ? key
            : ImGuiKey.None;
        CachedDisplayLabels[slot] = string.IsNullOrWhiteSpace(customLabel)
            ? KeyViewerKeyMap.GetDisplayName(binding)
            : customLabel;
    }

    private static float FitFontSize(ImFontPtr font, string text, float preferred, float maxWidth, float minimum)
    {
        Vector2 measure = font.CalcTextSizeA(preferred, float.MaxValue, 0f, text);
        if (measure.X <= maxWidth || measure.X <= 0f) return preferred;
        return Math.Max(minimum, preferred * maxWidth / measure.X);
    }

    private static string FitText(ImFontPtr font, string text, float size, float maxWidth)
    {
        if (maxWidth <= 0f) return string.Empty;
        if (font.CalcTextSizeA(size, float.MaxValue, 0f, text).X <= maxWidth)
            return text;

        const string suffix = "...";
        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text[..length] + suffix;
            if (font.CalcTextSizeA(size, float.MaxValue, 0f, candidate).X <= maxWidth)
                return candidate;
        }

        if (font.CalcTextSizeA(size, float.MaxValue, 0f, suffix).X <= maxWidth)
            return suffix;
        for (int length = text.Length; length > 0; length--)
        {
            string candidate = text[..length];
            if (font.CalcTextSizeA(size, float.MaxValue, 0f, candidate).X <= maxWidth)
                return candidate;
        }
        return string.Empty;
    }

    private static void AddTextShadow(
        ImDrawListPtr drawList,
        ImFontPtr font,
        float size,
        Vector2 position,
        uint color,
        string text,
        KeyViewerColorGradient? gradient = null)
    {
        KeyViewerFontRuntime.AddText(
            drawList,
            font,
            size,
            position + new Vector2(1f, 1f),
            0xB0000000,
            text);
        AddColorText(drawList, font, size, position, color, text, gradient);
    }

    private static void AddColorText(
        ImDrawListPtr drawList,
        ImFontPtr font,
        float size,
        Vector2 position,
        uint color,
        string text,
        KeyViewerColorGradient? gradient)
    {
        if (gradient is not { Enabled: true })
        {
            KeyViewerFontRuntime.AddText(drawList, font, size, position, color, text);
            return;
        }

        if (string.IsNullOrEmpty(text)) return;
        if (font.NativePtr == null)
        {
            KeyViewerFontRuntime.AddText(drawList, font, size, position, color, text);
            return;
        }
        IntPtr textureId = font.ContainerAtlas.TexID;
        if (textureId == IntPtr.Zero)
        {
            KeyViewerFontRuntime.AddText(drawList, font, size, position, color, text);
            return;
        }

        Vector2 measure = font.CalcTextSizeA(size, float.MaxValue, 0f, text);
        float scale = size / Math.Max(1f, font.FontSize);
        Vector2 cursor = position;
        drawList.PushTextureID(textureId);
        try
        {
            foreach (char character in text)
            {
                if (character == '\n')
                {
                    cursor.X = position.X;
                    cursor.Y += font.FontSize * scale;
                    continue;
                }

                ImFontGlyphPtr glyph = font.FindGlyph(character);
                if (glyph.NativePtr == null)
                {
                    cursor.X += font.GetCharAdvance(character) * scale;
                    continue;
                }

                Vector2 p0 = new(cursor.X + glyph.X0 * scale, position.Y + glyph.Y0 * scale);
                Vector2 p1 = new(cursor.X + glyph.X1 * scale, position.Y + glyph.Y0 * scale);
                Vector2 p2 = new(cursor.X + glyph.X1 * scale, position.Y + glyph.Y1 * scale);
                Vector2 p3 = new(cursor.X + glyph.X0 * scale, position.Y + glyph.Y1 * scale);
                float x = Math.Clamp(
                    (cursor.X - position.X + glyph.AdvanceX * scale * 0.5f)
                        / Math.Max(1f, measure.X),
                    0f,
                    1f);
                uint glyphColor = GradientColorU32(gradient, x, 0.5f, 1f);
                drawList.AddImageQuad(
                    textureId,
                    p0,
                    p1,
                    p2,
                    p3,
                    new Vector2(glyph.U0, glyph.V0),
                    new Vector2(glyph.U1, glyph.V0),
                    new Vector2(glyph.U1, glyph.V1),
                    new Vector2(glyph.U0, glyph.V1),
                    glyphColor);
                cursor.X += glyph.AdvanceX * scale;
            }
        }
        finally
        {
            drawList.PopTextureID();
        }
    }

    private static uint ColorU32(float[] color, float alpha)
    {
        float r = color.Length > 0 ? color[0] : 1f;
        float g = color.Length > 1 ? color[1] : 1f;
        float b = color.Length > 2 ? color[2] : 1f;
        float a = (color.Length > 3 ? color[3] : 1f) * alpha;
        return ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, Math.Clamp(a, 0f, 1f)));
    }

    private static uint ColorU32(Vector4 color, float alpha = 1f)
    {
        color.W = Math.Clamp(color.W * alpha, 0f, 1f);
        return ImGui.ColorConvertFloat4ToU32(color);
    }

    private static string FormatCount(int value, bool format)
        => format ? value.ToString("N0", CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    private static int SlotIndex(int index, bool foot) => foot ? MaxMainKeys + index : index;

    private enum ColorRole
    {
        Background,
        Outline,
        Text,
    }

    private static void DrawKeyBox(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float[] background,
        KeyViewerColorGradient? backgroundGradient,
        float[] outline,
        KeyViewerColorGradient? outlineGradient,
        float rounding,
        float outlineThickness)
    {
        if (backgroundGradient is not { Enabled: true }
            && outlineGradient is not { Enabled: true })
        {
            drawList.AddRectFilled(min, max, ColorU32(background, 1f), rounding);
            drawList.AddRect(
                min,
                max,
                ColorU32(outline, 1f),
                rounding,
                ImDrawFlags.None,
                outlineThickness);
            return;
        }

        AddGradientRect(drawList, min, max, outline, outlineGradient, 1f, 1f);
        float inset = Math.Clamp(outlineThickness, 0.5f, Math.Min(max.X - min.X, max.Y - min.Y) * 0.5f);
        Vector2 innerMin = min + new Vector2(inset);
        Vector2 innerMax = max - new Vector2(inset);
        if (innerMax.X > innerMin.X && innerMax.Y > innerMin.Y)
            AddGradientRect(drawList, innerMin, innerMax, background, backgroundGradient, 1f, 1f);
    }

    private static void AddGradientRect(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float[] flatColor,
        KeyViewerColorGradient? gradient,
        float topAlpha,
        float bottomAlpha)
    {
        if (gradient is not { Enabled: true })
        {
            if (MathF.Abs(topAlpha - bottomAlpha) <= 0.001f)
                drawList.AddRectFilled(min, max, ColorU32(flatColor, topAlpha));
            else
                drawList.AddRectFilledMultiColor(
                    min,
                    max,
                    ColorU32(flatColor, topAlpha),
                    ColorU32(flatColor, topAlpha),
                    ColorU32(flatColor, bottomAlpha),
                    ColorU32(flatColor, bottomAlpha));
            return;
        }

        drawList.AddRectFilledMultiColor(
            min,
            max,
            GradientColorU32(gradient, 0f, 0f, topAlpha),
            GradientColorU32(gradient, 1f, 0f, topAlpha),
            GradientColorU32(gradient, 1f, 1f, bottomAlpha),
            GradientColorU32(gradient, 0f, 1f, bottomAlpha));
    }

    private static uint GradientColorU32(
        KeyViewerColorGradient gradient,
        float x,
        float y,
        float alpha)
    {
        x = Math.Clamp(x, 0f, 1f);
        y = Math.Clamp(y, 0f, 1f);
        float r = Bilinear(gradient, 0, x, y);
        float g = Bilinear(gradient, 1, x, y);
        float b = Bilinear(gradient, 2, x, y);
        float a = Bilinear(gradient, 3, x, y);
        return ColorU32(new Vector4(r, g, b, a), alpha);
    }

    private static float Bilinear(KeyViewerColorGradient gradient, int channel, float x, float y)
    {
        float topLeft = GradientChannel(gradient.TopLeft, channel);
        float topRight = GradientChannel(gradient.TopRight, channel);
        float bottomLeft = GradientChannel(gradient.BottomLeft, channel);
        float bottomRight = GradientChannel(gradient.BottomRight, channel);
        float top = topLeft + (topRight - topLeft) * x;
        float bottom = bottomLeft + (bottomRight - bottomLeft) * x;
        return top + (bottom - top) * y;
    }

    private static float GradientChannel(float[] color, int channel)
        => color.Length > channel ? Math.Clamp(color[channel], 0f, 1f) : 1f;

    private static float[] ResolveColor(float[] fallback, KeyViewerColorGradient? gradient)
        => gradient is { Enabled: true } && gradient.TopLeft.Length >= 4
            ? gradient.TopLeft
            : fallback;

    private static float[] GetKeyColor(KeyViewerSettings settings, int slot, bool pressed, ColorRole role)
    {
        if (settings.EnablePerKeyColors && slot >= 0 && slot < settings.PerKeyBackground.Length)
        {
            float[][] values = role switch
            {
                ColorRole.Background => pressed ? settings.PerKeyBackgroundPressed : settings.PerKeyBackground,
                ColorRole.Outline => pressed ? settings.PerKeyOutlinePressed : settings.PerKeyOutline,
                _ => pressed ? settings.PerKeyTextPressed : settings.PerKeyText,
            };
            if (slot < values.Length && values[slot] is { Length: >= 4 } value)
                return value;
        }

        return role switch
        {
            ColorRole.Background => pressed ? settings.BackgroundPressed : settings.Background,
            ColorRole.Outline => pressed ? settings.OutlinePressed : settings.Outline,
            _ => pressed ? settings.TextPressed : settings.Text,
        };
    }

    private static KeyViewerColorGradient? GetKeyGradient(
        KeyViewerSettings settings,
        int slot,
        bool pressed,
        ColorRole role)
    {
        if (settings.EnablePerKeyColors && slot >= 0 && slot < settings.PerKeyBackgroundGradients.Length)
        {
            KeyViewerColorGradient[] values = role switch
            {
                ColorRole.Background => pressed
                    ? settings.PerKeyBackgroundPressedGradients
                    : settings.PerKeyBackgroundGradients,
                ColorRole.Outline => pressed
                    ? settings.PerKeyOutlinePressedGradients
                    : settings.PerKeyOutlineGradients,
                _ => pressed
                    ? settings.PerKeyTextPressedGradients
                    : settings.PerKeyTextGradients,
            };
            if (slot < values.Length && values[slot] is { Enabled: true } value)
                return value;
        }

        KeyViewerColorGradient? global = role switch
        {
            ColorRole.Background => pressed ? settings.BackgroundPressedGradient : settings.BackgroundGradient,
            ColorRole.Outline => pressed ? settings.OutlinePressedGradient : settings.OutlineGradient,
            _ => pressed ? settings.TextPressedGradient : settings.TextGradient,
        };
        return global is { Enabled: true } ? global : null;
    }

    private static KeyViewerColorGradient? GetStatusGradient(
        KeyViewerSettings settings,
        bool total,
        ColorRole role)
    {
        KeyViewerColorGradient global = total
            ? role switch
            {
                ColorRole.Background => settings.TotalBackgroundGradient,
                ColorRole.Outline => settings.TotalOutlineGradient,
                _ => settings.TotalTextGradient,
            }
            : role switch
            {
                ColorRole.Background => settings.KpsBackgroundGradient,
                ColorRole.Outline => settings.KpsOutlineGradient,
                _ => settings.KpsTextGradient,
            };
        return global is { Enabled: true } ? global : null;
    }

    private static float[] GetRainColor(KeyViewerSettings settings, RainDrop drop)
    {
        int slot = SlotIndex(drop.Index, drop.Foot);
        return settings.EnablePerKeyColors && slot >= 0 && slot < settings.PerKeyRainColor.Length
            && settings.PerKeyRainColor[slot] is { Length: >= 4 } value
            ? value : settings.RainColor;
    }

    private static KeyViewerColorGradient? GetRainGradient(KeyViewerSettings settings, RainDrop drop)
    {
        int slot = SlotIndex(drop.Index, drop.Foot);
        if (settings.EnablePerKeyColors
            && slot >= 0
            && slot < settings.PerKeyRainGradients.Length
            && settings.PerKeyRainGradients[slot] is { Enabled: true } value)
            return value;
        return settings.RainGradient is { Enabled: true } ? settings.RainGradient : null;
    }

    private static Queue<float>[] CreateSlotQueues()
    {
        var result = new Queue<float>[MaxSlots];
        for (int i = 0; i < result.Length; i++) result[i] = new Queue<float>(32);
        return result;
    }

    private static void DrainTouchEvents()
    {
        while (TouchEvents.TryDequeue(out _)) { }
    }

    private static void DrainReplayTouchEvents()
    {
        while (ReplayTouchEvents.TryDequeue(out _)) { }
    }

    private static void DrainReplayKeyboardEvents()
    {
        while (ReplayKeyboardEvents.TryDequeue(out _)) { }
    }
}
