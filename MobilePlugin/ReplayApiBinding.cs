using System.Reflection;
using StArray.ModManager.Android.Native;
using StArray.ModManager.Manager;

namespace JipperKeyViewer.Mobile;

/// <summary>Optional reflection binding so Replay remains a non-required Mod.</summary>
internal sealed class ReplayApiBinding
{
    private const string ApiTypeName = "Replay.Mobile.ReplayKeyViewerApi";

    private PropertyInfo? _activeProperty;
    private EventInfo? _startedEvent;
    private EventInfo? _endedEvent;
    private EventInfo? _touchEvent;
    private Delegate? _startedHandler;
    private Delegate? _endedHandler;
    private Delegate? _touchHandler;
    private long _nextProbeTick;
    private bool _bound;

    internal bool IsPlaybackActive
    {
        get
        {
            TryBind();
            if (!_bound || _activeProperty == null)
                return false;
            try { return _activeProperty.GetValue(null) is true; }
            catch { return false; }
        }
    }

    internal void TryBind()
    {
        if (_bound || Environment.TickCount64 < _nextProbeTick)
            return;
        _nextProbeTick = Environment.TickCount64 + 500;

        try
        {
            Type? api = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                api = assembly.GetType(ApiTypeName, throwOnError: false);
                if (api != null) break;
            }
            if (api == null)
                return;

            _activeProperty = api.GetProperty("IsPlaybackActive", BindingFlags.Public | BindingFlags.Static);
            _startedEvent = api.GetEvent("PlaybackStarted", BindingFlags.Public | BindingFlags.Static);
            _endedEvent = api.GetEvent("PlaybackEnded", BindingFlags.Public | BindingFlags.Static);
            _touchEvent = api.GetEvent("ReplayTouch", BindingFlags.Public | BindingFlags.Static);
            if (_activeProperty == null || _startedEvent?.EventHandlerType == null
                || _endedEvent?.EventHandlerType == null || _touchEvent?.EventHandlerType == null)
                return;

            _startedHandler = Delegate.CreateDelegate(
                _startedEvent.EventHandlerType, this, nameof(OnReplayStarted));
            _endedHandler = Delegate.CreateDelegate(
                _endedEvent.EventHandlerType, this, nameof(OnReplayEnded));
            _touchHandler = Delegate.CreateDelegate(
                _touchEvent.EventHandlerType, this, nameof(OnReplayTouch));
            _startedEvent.AddEventHandler(null, _startedHandler);
            _endedEvent.AddEventHandler(null, _endedHandler);
            _touchEvent.AddEventHandler(null, _touchHandler);
            _bound = true;

            if (IsActiveWithoutProbe())
                KeyViewerRuntime.OnReplayStarted();
        }
        catch (Exception exception)
        {
            Unbind();
            PluginLog.Debug("Replay API binding deferred: " + exception.Message);
        }
    }

    internal void Dispose() => Unbind();

    private bool IsActiveWithoutProbe()
    {
        try { return _activeProperty?.GetValue(null) is true; }
        catch { return false; }
    }

    private void OnReplayStarted() => KeyViewerRuntime.OnReplayStarted();

    private void OnReplayEnded() => KeyViewerRuntime.OnReplayEnded();

    private void OnReplayTouch(
        int action,
        int pointerId,
        float x,
        float y,
        float sourceWidth,
        float sourceHeight)
    {
        KeyViewerRuntime.EnqueueReplayTouch(
            (AndroidInput.MotionAction)action,
            pointerId,
            x,
            y,
            sourceWidth,
            sourceHeight);
    }

    private void Unbind()
    {
        try
        {
            if (_startedEvent != null && _startedHandler != null)
                _startedEvent.RemoveEventHandler(null, _startedHandler);
            if (_endedEvent != null && _endedHandler != null)
                _endedEvent.RemoveEventHandler(null, _endedHandler);
            if (_touchEvent != null && _touchHandler != null)
                _touchEvent.RemoveEventHandler(null, _touchHandler);
        }
        catch { }

        _activeProperty = null;
        _startedEvent = null;
        _endedEvent = null;
        _touchEvent = null;
        _startedHandler = null;
        _endedHandler = null;
        _touchHandler = null;
        _bound = false;
    }
}
