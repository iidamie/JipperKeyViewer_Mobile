using System.Collections.Concurrent;
using ImGuiNET;
using StArray.ModManager.Android.Native;

namespace JipperKeyViewer.Mobile;

internal readonly record struct KeyboardPress(ImGuiKey Key, int AndroidKeyCode);

/// <summary>
/// Owns the keyboard state used by the overlay. Native callbacks only enqueue
/// immutable values; all state changes happen on the render/settings thread.
/// </summary>
internal static class KeyViewerKeyboardInput
{
    private const int QueueCapacity = 1024;
    private static readonly ConcurrentQueue<NativeKeyEvent> Events = new();
    private static readonly ConcurrentQueue<KeyboardPress> Presses = new();
    private static readonly HashSet<ImGuiKey> DownKeys = new();
    private static readonly HashSet<ImGuiKey> NativeObservedKeys = new();
    private static readonly HashSet<ImGuiKey> PressedKeys = new();
    private static readonly object StateLock = new();
    private static int CaptureMode;
    private static int NativeInputObserved;
    private static int FramePressesPending;

    private readonly record struct NativeKeyEvent(
        int KeyCode,
        AndroidInput.KeyAction Action,
        int RepeatCount,
        AndroidInput.MetaState MetaState);

    internal static void CaptureNativeEvent(IntPtr inputEvent)
    {
        if (inputEvent == IntPtr.Zero)
        {
            KeyViewerKeyboardDiagnostics.RecordReadFailure();
            return;
        }
        try
        {
            AndroidInput.EventType type = AndroidInput.AInputEvent_getType(inputEvent);
            KeyViewerKeyboardDiagnostics.RecordInputEvent(inputEvent, type);
            if (type != AndroidInput.EventType.Key)
                return;

            int keyCode = AndroidInput.AKeyEvent_getKeyCode(inputEvent);
            AndroidInput.KeyAction action = AndroidInput.AKeyEvent_getAction(inputEvent);
            int repeatCount = AndroidInput.AKeyEvent_getRepeatCount(inputEvent);
            AndroidInput.MetaState metaState = AndroidInput.AKeyEvent_getMetaState(inputEvent);
            bool mapped = KeyViewerKeyMap.TryMapAndroidKeyCode(keyCode, out ImGuiKey key);
            KeyViewerKeyboardDiagnostics.RecordKey(
                keyCode, action, repeatCount, metaState, mapped, key);
            if (mapped)
            {
                Enqueue(new NativeKeyEvent(keyCode, action, repeatCount, metaState));
                KeyViewerKeyboardDiagnostics.RecordEnqueued();
            }
        }
        catch (Exception exception)
        {
            KeyViewerKeyboardDiagnostics.RecordReadFailure();
            PluginLog.Debug($"Native keyboard event read failed: {exception.Message}");
        }
    }

    internal static void SetCaptureMode(bool enabled)
    {
        Volatile.Write(ref CaptureMode, enabled ? 1 : 0);
        if (!enabled)
            while (Presses.TryDequeue(out _)) { }
    }

    internal static void Update()
    {
        if (KeyViewerUnityKeyboardInput.IsPollingEnabled)
            KeyViewerUnityKeyboardInput.BeginFrame(captureMode: false);
        while (Events.TryDequeue(out NativeKeyEvent input))
        {
            if (!KeyViewerKeyMap.TryMapAndroidKeyCode(input.KeyCode, out ImGuiKey key))
                continue;

            lock (StateLock)
            {
                NativeObservedKeys.Add(key);
                Volatile.Write(ref NativeInputObserved, 1);
                switch (input.Action)
                {
                    case AndroidInput.KeyAction.Down:
                        if (DownKeys.Add(key)
                            && input.RepeatCount <= 0)
                        {
                            PressedKeys.Add(key);
                            Volatile.Write(ref FramePressesPending, 1);
                            if (Volatile.Read(ref CaptureMode) != 0)
                                Presses.Enqueue(new KeyboardPress(key, input.KeyCode));
                        }
                        break;
                    case AndroidInput.KeyAction.Up:
                        DownKeys.Remove(key);
                        break;
                }
            }
        }
    }

    internal static void ResetDownState()
    {
        lock (StateLock)
        {
            DownKeys.Clear();
            NativeObservedKeys.Clear();
            PressedKeys.Clear();
        }
        Volatile.Write(ref NativeInputObserved, 0);
        Volatile.Write(ref FramePressesPending, 0);
    }

    internal static bool WasPressed(ImGuiKey key)
    {
        if (key == ImGuiKey.None || Volatile.Read(ref FramePressesPending) == 0)
            return false;
        lock (StateLock) return PressedKeys.Contains(key);
    }

    internal static void ClearFramePresses()
    {
        if (Volatile.Read(ref FramePressesPending) == 0)
            return;
        lock (StateLock) PressedKeys.Clear();
        Volatile.Write(ref FramePressesPending, 0);
    }

    internal static bool IsDown(ImGuiKey key)
    {
        if (key == ImGuiKey.None)
            return false;
        if (Volatile.Read(ref NativeInputObserved) != 0)
        {
            lock (StateLock)
            {
                if (NativeObservedKeys.Contains(key))
                    return DownKeys.Contains(key);
            }
        }

        if (KeyViewerUnityKeyboardInput.IsPollingEnabled
            && KeyViewerUnityKeyboardInput.IsDown(key))
            return true;

        try { return ImGui.IsKeyDown(key); }
        catch { return false; }
    }

    internal static bool TryTakePressed(out KeyboardPress press)
    {
        if (Volatile.Read(ref CaptureMode) == 0)
        {
            press = default;
            return false;
        }
        if (Presses.TryDequeue(out press))
        {
            KeyViewerKeyboardDiagnostics.RecordCaptureAccepted(fallback: false);
            return true;
        }

        if (KeyViewerUnityKeyboardInput.TryCapturePressed(out press))
        {
            KeyViewerKeyboardDiagnostics.RecordCaptureAccepted(fallback: true);
            return true;
        }

        // Some manager builds also feed ImGui key events. Keep capture usable
        // there when neither native nor Unity input sees the key.
        foreach (ImGuiKey key in KeyViewerKeyMap.CapturableKeys())
        {
            try
            {
                if (ImGui.IsKeyPressed(key, false))
                {
                    press = new KeyboardPress(key, -1);
                    KeyViewerKeyboardDiagnostics.RecordCaptureAccepted(fallback: true);
                    return true;
                }
            }
            catch
            {
                break;
            }
        }

        press = default;
        return false;
    }

    internal static void ClearPendingPresses()
    {
        while (Presses.TryDequeue(out _)) { }
        while (Events.TryDequeue(out _)) { }
    }

    internal static void Reset()
    {
        SetCaptureMode(false);
        ClearPendingPresses();
        lock (StateLock)
        {
            DownKeys.Clear();
            NativeObservedKeys.Clear();
            PressedKeys.Clear();
        }
        Volatile.Write(ref NativeInputObserved, 0);
        Volatile.Write(ref FramePressesPending, 0);
        KeyViewerUnityKeyboardInput.Reset();
    }

    private static void Enqueue(NativeKeyEvent input)
    {
        while (Events.Count >= QueueCapacity && Events.TryDequeue(out NativeKeyEvent dropped))
            KeyViewerKeyboardDiagnostics.RecordDropped();
        Events.Enqueue(input);
    }

    internal static string GetDiagnosticsStatus()
        => KeyViewerKeyboardDiagnostics.GetStatusText(
            Volatile.Read(ref CaptureMode) != 0,
            Events.Count,
            Presses.Count)
            + " | " + KeyViewerUnityKeyboardInput.GetDiagnosticsStatus();
}
