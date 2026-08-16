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
    private EventInfo? _keyboardEvent;
    private Delegate? _startedHandler;
    private Delegate? _endedHandler;
    private Delegate? _touchHandler;
    private Delegate? _keyboardHandler;
    private long _nextProbeTick;
    private bool _bound;
    private volatile bool _receiveTouch;
    private volatile bool _receiveKeyboard;
    private bool _touchSubscribed;
    private bool _keyboardSubscribed;

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
            _keyboardEvent = api.GetEvent("ReplayKeyboard", BindingFlags.Public | BindingFlags.Static);
            if (_activeProperty == null || _startedEvent?.EventHandlerType == null
                || _endedEvent?.EventHandlerType == null || _touchEvent?.EventHandlerType == null)
                return;

            _startedHandler = Delegate.CreateDelegate(
                _startedEvent.EventHandlerType, this, nameof(OnReplayStarted));
            _endedHandler = Delegate.CreateDelegate(
                _endedEvent.EventHandlerType, this, nameof(OnReplayEnded));
            _touchHandler = Delegate.CreateDelegate(
                _touchEvent.EventHandlerType, this, nameof(OnReplayTouch));
            if (_keyboardEvent?.EventHandlerType != null)
            {
                _keyboardHandler = Delegate.CreateDelegate(
                    _keyboardEvent.EventHandlerType, this, nameof(OnReplayKeyboard));
            }
            _startedEvent.AddEventHandler(null, _startedHandler);
            _endedEvent.AddEventHandler(null, _endedHandler);
            _bound = true;
            UpdateInputSubscriptions();

            if (IsActiveWithoutProbe())
                KeyViewerRuntime.OnReplayStarted();
        }
        catch (Exception exception)
        {
            Unbind();
            PluginLog.Debug("Replay API binding deferred: " + exception.Message);
        }
    }

    internal void ConfigureInputSubscriptions(bool receiveTouch, bool receiveKeyboard)
    {
        _receiveTouch = receiveTouch;
        _receiveKeyboard = receiveKeyboard;
        UpdateInputSubscriptions();
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
        if (!_receiveTouch)
            return;
        KeyViewerRuntime.EnqueueReplayTouch(
            (AndroidInput.MotionAction)action,
            pointerId,
            x,
            y,
            sourceWidth,
            sourceHeight);
    }

    private void OnReplayKeyboard(string binding, int action, int repeat)
    {
        if (_receiveKeyboard)
            KeyViewerRuntime.EnqueueReplayKeyboard(binding, action, repeat);
    }

    private void UpdateInputSubscriptions()
    {
        if (!_bound)
            return;
        try
        {
            UpdateSubscription(_touchEvent, _touchHandler, _receiveTouch, ref _touchSubscribed);
            UpdateSubscription(_keyboardEvent, _keyboardHandler, _receiveKeyboard, ref _keyboardSubscribed);
        }
        catch (Exception exception)
        {
            PluginLog.Debug("Replay input subscription update deferred: " + exception.Message);
        }
    }

    private static void UpdateSubscription(
        EventInfo? @event,
        Delegate? handler,
        bool shouldSubscribe,
        ref bool subscribed)
    {
        if (@event == null || handler == null || subscribed == shouldSubscribe)
            return;
        if (shouldSubscribe)
            @event.AddEventHandler(null, handler);
        else
            @event.RemoveEventHandler(null, handler);
        subscribed = shouldSubscribe;
    }

    private void Unbind()
    {
        try
        {
            if (_startedEvent != null && _startedHandler != null)
                _startedEvent.RemoveEventHandler(null, _startedHandler);
            if (_endedEvent != null && _endedHandler != null)
                _endedEvent.RemoveEventHandler(null, _endedHandler);
            if (_touchSubscribed && _touchEvent != null && _touchHandler != null)
                _touchEvent.RemoveEventHandler(null, _touchHandler);
            if (_keyboardSubscribed && _keyboardEvent != null && _keyboardHandler != null)
                _keyboardEvent.RemoveEventHandler(null, _keyboardHandler);
        }
        catch { }

        _activeProperty = null;
        _startedEvent = null;
        _endedEvent = null;
        _touchEvent = null;
        _keyboardEvent = null;
        _startedHandler = null;
        _endedHandler = null;
        _touchHandler = null;
        _keyboardHandler = null;
        _bound = false;
        _touchSubscribed = false;
        _keyboardSubscribed = false;
    }
}
