using ImGuiNET;
using StArray.ModManager.Android.Native;

namespace JipperKeyViewer.Mobile;

/// <summary>
/// Keyboard-only diagnostics for internal testing. Nothing here is called from
/// the touch input path, and logging happens only when explicitly requested.
/// </summary>
internal static class KeyViewerKeyboardDiagnostics
{
    private static long _installAttempts;
    private static long _installSuccesses;
    private static long _installFailures;
    private static long _hookCallbacks;
    private static long _keyEvents;
    private static long _readFailures;
    private static long _mappedKeys;
    private static long _unmappedKeys;
    private static long _enqueuedEvents;
    private static long _droppedEvents;
    private static long _captureStarts;
    private static long _captureAccepts;
    private static long _fallbackAccepts;

    private static int _lastKeyCode = -1;
    private static int _lastAction = -1;
    private static int _lastRepeat = -1;
    private static int _lastMeta;
    private static int _lastMappedKey;

    internal static void RecordInstall(bool installed)
    {
        Interlocked.Increment(ref _installAttempts);
        if (installed) Interlocked.Increment(ref _installSuccesses);
        else Interlocked.Increment(ref _installFailures);
        PluginLog.Debug($"Keyboard hook InitializeKeyEvent: installed={installed}");
    }

    internal static void RecordCallback()
        => Interlocked.Increment(ref _hookCallbacks);

    internal static void RecordInputEvent(IntPtr inputEvent, AndroidInput.EventType type)
    {
        if (inputEvent == IntPtr.Zero || type != AndroidInput.EventType.Key)
        {
            Interlocked.Increment(ref _readFailures);
            return;
        }

        Interlocked.Increment(ref _keyEvents);
    }

    internal static void RecordReadFailure()
        => Interlocked.Increment(ref _readFailures);

    internal static void RecordKey(
        int keyCode,
        AndroidInput.KeyAction action,
        int repeatCount,
        AndroidInput.MetaState metaState,
        bool mapped,
        ImGuiKey key)
    {
        if (mapped) Interlocked.Increment(ref _mappedKeys);
        else Interlocked.Increment(ref _unmappedKeys);
        Volatile.Write(ref _lastKeyCode, keyCode);
        Volatile.Write(ref _lastAction, (int)action);
        Volatile.Write(ref _lastRepeat, repeatCount);
        Volatile.Write(ref _lastMeta, (int)metaState);
        Volatile.Write(ref _lastMappedKey, (int)key);
    }

    internal static void RecordEnqueued()
        => Interlocked.Increment(ref _enqueuedEvents);

    internal static void RecordDropped()
        => Interlocked.Increment(ref _droppedEvents);

    internal static void RecordCaptureStarted()
    {
        Interlocked.Increment(ref _captureStarts);
        PluginLog.Debug("Keyboard binding capture started; waiting for next key-down");
    }

    internal static void RecordCaptureAccepted(bool fallback)
    {
        if (fallback) Interlocked.Increment(ref _fallbackAccepts);
        else Interlocked.Increment(ref _captureAccepts);
        PluginLog.Debug($"Keyboard binding capture accepted (fallback={fallback})");
    }

    internal static string GetStatusText(bool captureMode, int pendingEvents, int pendingPresses)
    {
        int keyCode = Volatile.Read(ref _lastKeyCode);
        string last = keyCode < 0
            ? "none"
            : $"code={keyCode}/action={Volatile.Read(ref _lastAction)}/"
                + $"repeat={Volatile.Read(ref _lastRepeat)}/"
                + $"imgui={FormatKey(Volatile.Read(ref _lastMappedKey))}";
        return $"Keyboard diag: key hook {_installSuccesses}/{_installAttempts}, "
            + $"callbacks {_hookCallbacks}, key events {_keyEvents}, last {last}, "
            + $"queue {pendingEvents}/{pendingPresses}, capture={(captureMode ? "on" : "off")}";
    }

    internal static void ResetForTest()
    {
        Interlocked.Exchange(ref _installAttempts, 0);
        Interlocked.Exchange(ref _installSuccesses, 0);
        Interlocked.Exchange(ref _installFailures, 0);
        Interlocked.Exchange(ref _hookCallbacks, 0);
        Interlocked.Exchange(ref _keyEvents, 0);
        Interlocked.Exchange(ref _readFailures, 0);
        Interlocked.Exchange(ref _mappedKeys, 0);
        Interlocked.Exchange(ref _unmappedKeys, 0);
        Interlocked.Exchange(ref _enqueuedEvents, 0);
        Interlocked.Exchange(ref _droppedEvents, 0);
        Interlocked.Exchange(ref _captureStarts, 0);
        Interlocked.Exchange(ref _captureAccepts, 0);
        Interlocked.Exchange(ref _fallbackAccepts, 0);
        Volatile.Write(ref _lastKeyCode, -1);
        Volatile.Write(ref _lastAction, -1);
        Volatile.Write(ref _lastRepeat, -1);
        Volatile.Write(ref _lastMeta, 0);
        Volatile.Write(ref _lastMappedKey, 0);
    }

    internal static void FlushPeriodic(bool force = false)
    {
        if (force)
            PluginLog.Info(BuildLogMessage());
    }

    private static string BuildLogMessage()
        => $"Keyboard diagnostics: key hook={_installSuccesses}/{_installAttempts} "
            + $"(fail {_installFailures}); callbacks={_hookCallbacks}; key events={_keyEvents}; "
            + $"mapped={_mappedKeys}, unmapped={_unmappedKeys}, queued={_enqueuedEvents}, "
            + $"dropped={_droppedEvents}; capture starts={_captureStarts}, "
            + $"native accepts={_captureAccepts}, ImGui fallback accepts={_fallbackAccepts}; "
            + $"last keyCode={Volatile.Read(ref _lastKeyCode)}, "
            + $"action={Volatile.Read(ref _lastAction)}, repeat={Volatile.Read(ref _lastRepeat)}, "
            + $"meta=0x{Volatile.Read(ref _lastMeta):X}, "
            + $"imguiKey={FormatKey(Volatile.Read(ref _lastMappedKey))}; "
            + KeyViewerUnityKeyboardInput.GetDiagnosticsStatus();

    private static string FormatKey(int value)
        => value < 0 ? "-" : ((ImGuiKey)value).ToString();
}
