using ImGuiNET;
using StArray.ModManager.Runtime;
using StArray.ModManager.RuntimeAbstractions;

namespace JipperKeyViewer.Mobile;

/// <summary>
/// Reads hardware keyboard state through the game's legacy Unity Input API.
/// This stays on the overlay thread and never hooks Android's all-input path.
/// </summary>
internal static unsafe class KeyViewerUnityKeyboardInput
{
    private const long BindRetryMilliseconds = 1000L;

    private static readonly Dictionary<ImGuiKey, bool> DownStates = new();
    private static readonly Dictionary<ImGuiKey, bool> PressedStates = new();
    private static readonly HashSet<ImGuiKey> ObservedDownKeys = new();
    private static readonly nint[] KeyArgument = new nint[1];

    private static IRuntimeMethod? _getKey;
    private static IRuntimeMethod? _getKeyDown;
    private static IRuntimeMethod? _getAnyKey;
    private static IRuntimeMethod? _getAnyKeyDown;
    private static long _nextBindTicks;
    private static long _lastFrameTicks;
    private static long _lastCaptureCheckTicks;
    private static bool _pollingEnabled;
    private static bool _anyKey;
    private static bool _anyKeyDown;
    private static int _observedPresses;
    private static ImGuiKey _lastObservedKey = ImGuiKey.None;
    private static string _status = "waiting for Unity Input";

    internal static bool IsPollingEnabled => _pollingEnabled;

    internal static void BeginFrame(bool captureMode)
    {
        if (!_pollingEnabled && !captureMode)
            return;

        long now = Environment.TickCount64;
        if (_lastFrameTicks != now)
        {
            _lastFrameTicks = now;
            DownStates.Clear();
            PressedStates.Clear();
            _anyKey = false;
            _anyKeyDown = false;

            if (!TryBind(now))
                return;

            try
            {
                _anyKey = InvokeNoArgument(_getAnyKey!);
                if (!_anyKey)
                    ObservedDownKeys.Clear();
            }
            catch (Exception exception)
            {
                ResetBinding($"Unity Input call failed: {exception.Message}", now);
                return;
            }
        }

        if (!captureMode || _lastCaptureCheckTicks == now || _getAnyKeyDown == null)
            return;

        _lastCaptureCheckTicks = now;
        try
        {
            _anyKeyDown = InvokeNoArgument(_getAnyKeyDown);
        }
        catch (Exception exception)
        {
            ResetBinding($"Unity Input key-down call failed: {exception.Message}", now);
        }
    }

    internal static bool IsDown(ImGuiKey key)
    {
        if (!_pollingEnabled || !_anyKey
            || !KeyViewerKeyMap.TryMapUnityKeyCode(key, out int unityKeyCode))
            return false;
        if (DownStates.TryGetValue(key, out bool down))
            return down;

        try
        {
            down = InvokeKey(_getKey!, unityKeyCode);
            DownStates[key] = down;
            RecordObservedKey(key, down);
            return down;
        }
        catch (Exception exception)
        {
            ResetBinding($"Unity GetKey failed: {exception.Message}", Environment.TickCount64);
            return false;
        }
    }

    internal static bool TryCapturePressed(out KeyboardPress press)
    {
        BeginFrame(captureMode: true);
        if (!_anyKeyDown)
        {
            press = default;
            return false;
        }

        foreach (ImGuiKey key in KeyViewerKeyMap.CapturableKeys())
        {
            if (!KeyViewerKeyMap.TryMapUnityKeyCode(key, out int unityKeyCode))
                continue;
            if (!TryGetKeyDown(key, unityKeyCode))
                continue;

            _pollingEnabled = true;
            press = new KeyboardPress(key, unityKeyCode);
            return true;
        }

        press = default;
        return false;
    }

    internal static string GetDiagnosticsStatus()
        => $"Unity input: {_status}, polling={(_pollingEnabled ? "on" : "standby")}, "
            + $"anyKey={_anyKey}, anyKeyDown={_anyKeyDown}, "
            + $"observed={_observedPresses}, last="
            + (_lastObservedKey == ImGuiKey.None
                ? "none"
                : KeyViewerKeyMap.GetDisplayName(KeyViewerKeyMap.GetBindingName(_lastObservedKey)));

    internal static void Reset()
    {
        _getKey = null;
        _getKeyDown = null;
        _getAnyKey = null;
        _getAnyKeyDown = null;
        _nextBindTicks = 0;
        _lastFrameTicks = 0;
        _lastCaptureCheckTicks = 0;
        _pollingEnabled = false;
        _anyKey = false;
        _anyKeyDown = false;
        _observedPresses = 0;
        _lastObservedKey = ImGuiKey.None;
        _status = "waiting for Unity Input";
        DownStates.Clear();
        PressedStates.Clear();
        ObservedDownKeys.Clear();
    }

    private static bool TryBind(long now)
    {
        if (_getKey != null && _getKeyDown != null
            && _getAnyKey != null && _getAnyKeyDown != null)
            return true;
        if (now < _nextBindTicks)
            return false;

        _nextBindTicks = now + BindRetryMilliseconds;
        _pollingEnabled = false;
        try
        {
            IAppDomain? domain = RuntimeManager.GetDomain();
            IRuntimeClass? inputClass = FindInputClass(domain);
            if (inputClass == null)
            {
                _status = "Unity Input class unavailable";
                return false;
            }

            IRuntimeMethod? getKey = FindKeyMethod(inputClass, "GetKeyInt", "GetKey");
            IRuntimeMethod? getKeyDown = FindKeyMethod(inputClass, "GetKeyDownInt", "GetKeyDown");
            IRuntimeMethod? getAnyKey = inputClass.GetMethod("get_anyKey", 0);
            IRuntimeMethod? getAnyKeyDown = inputClass.GetMethod("get_anyKeyDown", 0);
            if (getKey == null || getKeyDown == null || getAnyKey == null || getAnyKeyDown == null)
            {
                _status = "Unity Input methods unavailable";
                return false;
            }

            _getKey = getKey;
            _getKeyDown = getKeyDown;
            _getAnyKey = getAnyKey;
            _getAnyKeyDown = getAnyKeyDown;
            _status = "ready";
            PluginLog.Info("Unity legacy keyboard input is ready");
            return true;
        }
        catch (Exception exception)
        {
            _status = $"Unity Input bind failed: {exception.Message}";
            return false;
        }
    }

    private static IRuntimeClass? FindInputClass(IAppDomain? domain)
    {
        if (domain == null)
            return null;

        foreach (IRuntimeAssembly assembly in domain.GetAssemblies())
        {
            try
            {
                IRuntimeClass? inputClass = assembly.GetClass("UnityEngine", "Input");
                if (inputClass != null)
                    return inputClass;
            }
            catch
            {
                // A stale assembly entry must not prevent the remaining lookup.
            }
        }
        return null;
    }

    private static IRuntimeMethod? FindKeyMethod(
        IRuntimeClass inputClass,
        string internalName,
        string publicName)
    {
        return inputClass.GetMethod(internalName, "UnityEngine.KeyCode")
            ?? inputClass.GetMethod(internalName, "KeyCode")
            ?? inputClass.GetMethod(internalName, 1)
            ?? inputClass.GetMethod(publicName, "UnityEngine.KeyCode")
            ?? inputClass.GetMethod(publicName, "KeyCode")
            ?? inputClass.GetMethod(publicName, 1);
    }

    private static bool TryGetKeyDown(ImGuiKey key, int unityKeyCode)
    {
        if (PressedStates.TryGetValue(key, out bool pressed))
            return pressed;

        try
        {
            pressed = InvokeKey(_getKeyDown!, unityKeyCode);
            PressedStates[key] = pressed;
            if (pressed)
                RecordObservedKey(key, down: true);
            return pressed;
        }
        catch (Exception exception)
        {
            ResetBinding($"Unity GetKeyDown failed: {exception.Message}", Environment.TickCount64);
            return false;
        }
    }

    private static bool InvokeNoArgument(IRuntimeMethod method)
        => method.InvokeStaticUnbox<bool>();

    private static bool InvokeKey(IRuntimeMethod method, int unityKeyCode)
    {
        int keyCode = unityKeyCode;
        KeyArgument[0] = (nint)(&keyCode);
        return method.InvokeStaticUnbox<bool>(KeyArgument);
    }

    private static void RecordObservedKey(ImGuiKey key, bool down)
    {
        if (down)
        {
            if (ObservedDownKeys.Add(key))
            {
                _observedPresses++;
                _lastObservedKey = key;
            }
            return;
        }

        ObservedDownKeys.Remove(key);
    }

    private static void ResetBinding(string status, long now)
    {
        _getKey = null;
        _getKeyDown = null;
        _getAnyKey = null;
        _getAnyKeyDown = null;
        _nextBindTicks = now + BindRetryMilliseconds;
        _pollingEnabled = false;
        _status = status;
        DownStates.Clear();
        PressedStates.Clear();
        ObservedDownKeys.Clear();
    }
}
