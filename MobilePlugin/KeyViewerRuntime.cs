using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using ImGuiNET;
using StArray.ModManager.Android.Native;

namespace JipperKeyViewer.Mobile;

internal static class KeyViewerRuntime
{
    private const int MaxMainKeys = 24;
    private const int MaxFootKeys = 8;
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
    private static readonly int[] FrontSequence = { 0, 1, 2, 3, 4, 5, 6, 7 };
    private static readonly int[] FootSequence = { 0, 1, 2, 3, 4, 5, 6, 7 };
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
        DrainTouchEvents();
        DrainReplayTouchEvents();
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
        ActivePointers.Clear();
        ReplayPointers.Clear();
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
        DrainTouchEvents();
        DrainReplayTouchEvents();
        ActivePointers.Clear();
        ReplayPointers.Clear();
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

    internal static void OnReplayStarted() => ResetInputState();

    internal static void OnReplayEnded() => ResetInputState();

    internal static void Update(JipperKeyViewerPlugin plugin, float delta, bool replayPlayback)
    {
        KeyViewerSettings settings = plugin.Settings;
        settings.Normalize();
        bool settingsPanelVisible = plugin.ConsumeSettingsPanelVisibility();
        _settingsPanelVisible = settingsPanelVisible;
        Vector2 display = ImGui.GetIO().DisplaySize;
        if (display.X <= 1f || display.Y <= 1f) return;

        if (_layout != (int)settings.Layout || _footCount != settings.FootKeyCount)
        {
            ResetInputState();
            _layout = (int)settings.Layout;
            _footCount = settings.FootKeyCount;
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
            Array.Clear(KeyboardMainPressed);
            Array.Clear(KeyboardFootPressed);
        }
        else
        {
            ClearReplayTouchInputState(clearRain: false);
            if (settings.TouchInputEnabled)
                ProcessTouchEvents(settings, display);
            else
                ClearTouchInputState(clearRain: false);

            PollKeyboard(settings);
        }

        ProcessKeyStates(settings);
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
        ImFontPtr font = ImGui.GetFont();
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
        int footCount = settings.FootKeyCount;
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
            KeyboardMainPressed[i] = IsKeyDown(settings.KeyBindings[i]);
        for (int i = 0; i < settings.FootKeyCount; i++)
            KeyboardFootPressed[i] = IsKeyDown(settings.FootBindings[i]);
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
        for (int i = 0; i < settings.FootKeyCount; i++)
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

            // Keep the part inside RainHeight solid. Only the overflow above that
            // boundary receives the vertical fade, otherwise the whole rain segment
            // would be interpolated from transparent to opaque.
            float solidFar = Math.Min(farDistance, maxHeight);
            if (solidFar - nearDistance > 1f)
            {
                Vector2 solidMin = new(x, rect.Min.Y - solidFar);
                Vector2 solidMax = new(x + width, rect.Min.Y - nearDistance);
                drawList.AddRectFilled(solidMin, solidMax, ColorU32(settings.RainColor, 1f));
            }

            float fadeNear = Math.Max(nearDistance, maxHeight);
            float fadeFar = Math.Min(farDistance, maxHeight + fadePixels);
            if (fadeFar - fadeNear <= 1f) continue;
            float topAlpha = FadeBeyondRainHeight(fadeFar, maxHeight, fadePixels);
            float bottomAlpha = FadeBeyondRainHeight(fadeNear, maxHeight, fadePixels);
            Vector2 fadeMin = new(x, rect.Min.Y - fadeFar);
            Vector2 fadeMax = new(x + width, rect.Min.Y - fadeNear);
            drawList.AddRectFilledMultiColor(
                fadeMin,
                fadeMax,
                ColorU32(settings.RainColor, topAlpha),
                ColorU32(settings.RainColor, topAlpha),
                ColorU32(settings.RainColor, bottomAlpha),
                ColorU32(settings.RainColor, bottomAlpha));
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
        uint background = ColorU32(pressed ? settings.BackgroundPressed : settings.Background, 1f);
        uint outline = ColorU32(pressed ? settings.OutlinePressed : settings.Outline, 1f);
        uint textColor = ColorU32(pressed ? settings.TextPressed : settings.Text, 1f);
        float rounding = Math.Min(6f, (rect.Max.Y - rect.Min.Y) * 0.12f);
        drawList.AddRectFilled(rect.Min, rect.Max, background, rounding);
        drawList.AddRect(rect.Min, rect.Max, outline, rounding, ImDrawFlags.None, pressed ? 2f : 1.5f);

        string label = GetLabel(settings, rect);
        float height = rect.Max.Y - rect.Min.Y;
        float width = rect.Max.X - rect.Min.X;
        float fontSize = Math.Clamp(height * 0.38f, 12f, 30f);
        float textWidth = Math.Max(8f, width - 4f);
        fontSize = FitFontSize(font, label, fontSize, textWidth, 8f);
        label = FitText(font, label, fontSize, textWidth);
        Vector2 labelSize = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, label);
        Vector2 labelPosition = new(
            rect.Min.X + (rect.Max.X - rect.Min.X - labelSize.X) * 0.5f,
            rect.Min.Y + (height - labelSize.Y) * (settings.ShowMainKeyCount ? 0.30f : 0.5f));
        AddTextShadow(drawList, font, fontSize, labelPosition, textColor, label);

        if (!settings.ShowMainKeyCount) return;
        int slot = SlotIndex(rect.Index, rect.Foot);
        string value = settings.ShowPerKeyKps
            ? SlotPressTimes[slot].Count.ToString(CultureInfo.InvariantCulture)
            : FormatCount(settings.Counts[slot], settings.EnableCountFormatting);
        float valueSize = Math.Clamp(fontSize * 0.54f, 9f, 16f);
        valueSize = FitFontSize(font, value, valueSize, textWidth, 7f);
        value = FitText(font, value, valueSize, textWidth);
        Vector2 valueMeasure = font.CalcTextSizeA(valueSize, float.MaxValue, 0f, value);
        Vector2 valuePosition = new(
            rect.Min.X + (rect.Max.X - rect.Min.X - valueMeasure.X) * 0.5f,
            rect.Max.Y - valueMeasure.Y - Math.Max(3f, height * 0.08f));
        drawList.AddText(font, valueSize, valuePosition, textColor, value);
    }

    private static void DrawStatusKey(
        KeyViewerSettings settings,
        StatusRect rect,
        ImFontPtr font,
        ImDrawListPtr drawList)
    {
        uint background = ColorU32(settings.Background, 1f);
        uint outline = ColorU32(settings.Outline, 1f);
        uint textColor = ColorU32(settings.Text, 1f);
        float height = rect.Max.Y - rect.Min.Y;
        float width = rect.Max.X - rect.Min.X;
        float rounding = Math.Min(6f, height * 0.12f);
        drawList.AddRectFilled(rect.Min, rect.Max, background, rounding);
        drawList.AddRect(rect.Min, rect.Max, outline, rounding, ImDrawFlags.None, 1.5f);

        string label = rect.Total ? "Total" : "KPS";
        string value = rect.Total
            ? FormatCount(settings.TotalCount, settings.EnableCountFormatting)
            : _kps.ToString(CultureInfo.InvariantCulture);
        float padding = Math.Max(4f, height * 0.12f);
        float textSize = Math.Clamp(height * 0.32f, 10f, 24f);
        float labelWidth = Math.Max(8f, width * 0.45f - padding);
        float valueWidth = Math.Max(8f, width * 0.52f - padding);
        float labelSize = FitFontSize(font, label, textSize, labelWidth, 8f);
        float valueSize = FitFontSize(font, value, textSize, valueWidth, 8f);
        label = FitText(font, label, labelSize, labelWidth);
        value = FitText(font, value, valueSize, valueWidth);
        Vector2 labelMeasure = font.CalcTextSizeA(labelSize, float.MaxValue, 0f, label);
        Vector2 valueMeasure = font.CalcTextSizeA(valueSize, float.MaxValue, 0f, value);
        float labelY = rect.Min.Y + (height - labelMeasure.Y) * 0.5f;
        float valueY = rect.Min.Y + (height - valueMeasure.Y) * 0.5f;
        AddTextShadow(
            drawList,
            font,
            labelSize,
            new Vector2(rect.Min.X + padding, labelY),
            textColor,
            label);
        AddTextShadow(
            drawList,
            font,
            valueSize,
            new Vector2(rect.Max.X - padding - valueMeasure.X, valueY),
            textColor,
            value);
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
        int footCount = settings.TouchFootAreaEnabled ? settings.FootKeyCount : 0;
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
        drawList.AddText(new Vector2(12f, 12f), 0xF0FFFFFF, status);
    }

    private static string GetLabel(KeyViewerSettings settings, KeyRect rect)
    {
        string custom = rect.Foot ? settings.FootLabels[rect.Index] : settings.KeyLabels[rect.Index];
        if (!string.IsNullOrWhiteSpace(custom)) return custom;
        string binding = rect.Foot ? settings.FootBindings[rect.Index] : settings.KeyBindings[rect.Index];
        return PrettyBinding(binding);
    }

    private static bool IsKeyDown(string binding)
    {
        if (!TryMapKey(binding, out ImGuiKey key)) return false;
        try { return ImGui.IsKeyDown(key); }
        catch { return false; }
    }

    private static bool TryMapKey(string? name, out ImGuiKey key)
    {
        key = ImGuiKey.None;
        if (string.IsNullOrWhiteSpace(name)) return false;
        string value = name.Trim();
        value = value switch
        {
            "Alpha0" => "_0",
            "Alpha1" => "_1",
            "Alpha2" => "_2",
            "Alpha3" => "_3",
            "Alpha4" => "_4",
            "Alpha5" => "_5",
            "Alpha6" => "_6",
            "Alpha7" => "_7",
            "Alpha8" => "_8",
            "Alpha9" => "_9",
            "Equals" => "Equal",
            "Return" => "Enter",
            "LeftControl" => "LeftCtrl",
            "RightControl" => "RightCtrl",
            "BackQuote" => "GraveAccent",
            "LBracket" => "LeftBracket",
            "RBracket" => "RightBracket",
            _ => value,
        };
        if (!Enum.TryParse(value, true, out key)) return false;
        return key != ImGuiKey.None;
    }

    private static string PrettyBinding(string binding)
        => binding switch
        {
            "Backspace" => "Back",
            "CapsLock" => "Caps",
            "Backslash" => "\\",
            "Equal" or "Equals" => "=",
            "Comma" => ",",
            "Period" => ".",
            "Semicolon" => ";",
            "Space" => "Space",
            "LeftShift" => "LShift",
            "RightShift" => "RShift",
            "LeftControl" => "LCtrl",
            "RightControl" => "RCtrl",
            "LeftCtrl" => "LCtrl",
            "RightCtrl" => "RCtrl",
            "GraveAccent" or "BackQuote" => "`",
            _ when binding.StartsWith("_") && binding.Length == 2 => binding[1..],
            _ => string.IsNullOrWhiteSpace(binding) ? "-" : binding,
        };

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

    private static void AddTextShadow(ImDrawListPtr drawList, ImFontPtr font, float size, Vector2 position, uint color, string text)
    {
        drawList.AddText(font, size, position + new Vector2(1f, 1f), 0xB0000000, text);
        drawList.AddText(font, size, position, color, text);
    }

    private static uint ColorU32(float[] color, float alpha)
    {
        float r = color.Length > 0 ? color[0] : 1f;
        float g = color.Length > 1 ? color[1] : 1f;
        float b = color.Length > 2 ? color[2] : 1f;
        float a = (color.Length > 3 ? color[3] : 1f) * alpha;
        return ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, Math.Clamp(a, 0f, 1f)));
    }

    private static string FormatCount(int value, bool format)
        => format ? value.ToString("N0", CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    private static int SlotIndex(int index, bool foot) => foot ? MaxMainKeys + index : index;

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
}
