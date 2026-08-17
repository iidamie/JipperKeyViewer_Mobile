using System.Reflection;
using StArray.ModManager.Android.Native;
using StArray.ModManager.Interop;

namespace JipperKeyViewer.Mobile;

/// <summary>VirtualInput V2 consumer with the private Replay V1 reflection protocol as fallback.</summary>
internal sealed class ReplayApiBinding
{
    private const string ApiTypeName = "Replay.Mobile.ReplayKeyViewerApi";

    private PropertyInfo? _activeProperty;
    private PropertyInfo? _v2ActiveProperty;
    private EventInfo? _startedEvent;
    private EventInfo? _endedEvent;
    private EventInfo? _touchEvent;
    private EventInfo? _keyboardEvent;
    private Delegate? _startedHandler;
    private Delegate? _endedHandler;
    private Delegate? _touchHandler;
    private Delegate? _keyboardHandler;
    private ModInteropSubscription? _v2Subscription;
    private long _nextProbeTick;
    private long _v2SessionGeneration;
    private bool _bound;
    private bool _v2Active;
    private bool _suppressV1ForCurrentPlayback;
    private volatile bool _receiveTouch;
    private volatile bool _receiveKeyboard;
    private bool _touchSubscribed;
    private bool _keyboardSubscribed;

    internal bool IsV2PlaybackActive => Volatile.Read(ref _v2Active);

    internal bool IsPlaybackActive
    {
        get
        {
            TryBind();
            if (IsV2PlaybackActive)
                return true;
            if (!_bound || _activeProperty == null)
                return false;
            try { return _activeProperty.GetValue(null) is true; }
            catch { return false; }
        }
    }

    internal void TryBind()
    {
        TryBindV2();
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
            _v2ActiveProperty = api.GetProperty(
                "IsVirtualInputV2Active",
                BindingFlags.Public | BindingFlags.Static);
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
            {
                var suppress = IsV2PlaybackActive || (CanReceiveV2() && IsRemoteV2Active());
                Volatile.Write(ref _suppressV1ForCurrentPlayback, suppress);
                if (!suppress)
                    KeyViewerRuntime.OnReplayStarted();
            }
        }
        catch (Exception exception)
        {
            UnbindV1();
            PluginLog.Debug("Replay V1 API binding deferred: " + exception.Message);
        }
    }

    internal void ConfigureInputSubscriptions(bool receiveTouch, bool receiveKeyboard)
    {
        _receiveTouch = receiveTouch;
        _receiveKeyboard = receiveKeyboard;
        UpdateInputSubscriptions();
    }

    internal void AcknowledgeV2Ended(long sessionGeneration)
    {
        if (Interlocked.Read(ref _v2SessionGeneration) != sessionGeneration)
            return;
        Volatile.Write(ref _v2Active, false);
        Volatile.Write(ref _suppressV1ForCurrentPlayback, false);
        Interlocked.Exchange(ref _v2SessionGeneration, 0);
    }

    internal void Dispose()
    {
        var generation = Interlocked.Exchange(ref _v2SessionGeneration, 0);
        if (Volatile.Read(ref _v2Active))
            KeyViewerRuntime.EnqueueReplayV2Batch(
                new VirtualInputBatch(VirtualInputBatchKind.Ended, Math.Max(1, generation)));
        Volatile.Write(ref _v2Active, false);
        Volatile.Write(ref _suppressV1ForCurrentPlayback, false);
        Interlocked.Exchange(ref _v2Subscription, null)?.Dispose();
        UnbindV1();
    }

    private void TryBindV2()
    {
        if (_v2Subscription is { IsRetired: false })
            return;
        if (!ModInterop.TrySubscribe(
                new InteropSubscriptionRequest(ModInteropConstants.VirtualInputPlaybackV2, 2)
                {
                    QueueCapacity = ModInteropConstants.VirtualInputQueueCapacity,
                    DispatchContext = InteropDispatchContext.SerializedWorker
                },
                OnVirtualInput,
                out var subscription,
                out var error))
        {
            if (error.Code is not (InteropErrorCode.RuntimeUnavailable or
                InteropErrorCode.GenerationMismatch))
                PluginLog.Debug("VirtualInput V2 binding deferred: " + error);
            return;
        }
        _v2Subscription = subscription;
    }

    private void OnVirtualInput(InteropMessage message)
    {
        var batch = message.VirtualInput;
        if (batch == null)
            return;
        if (message.IsCancellation)
        {
            EndV2(batch.SessionGeneration);
            return;
        }
        if (batch.Kind == VirtualInputBatchKind.Started)
        {
            var previous = Interlocked.Exchange(
                ref _v2SessionGeneration,
                batch.SessionGeneration);
            if (previous > 0 && previous != batch.SessionGeneration)
                KeyViewerRuntime.EnqueueReplayV2Batch(
                    new VirtualInputBatch(VirtualInputBatchKind.Ended, previous));
            Volatile.Write(ref _v2Active, true);
            Volatile.Write(ref _suppressV1ForCurrentPlayback, true);
            KeyViewerRuntime.EnqueueReplayV2Batch(batch);
            return;
        }
        if (!Volatile.Read(ref _v2Active) ||
            Interlocked.Read(ref _v2SessionGeneration) != batch.SessionGeneration)
            return;
        if (!KeyViewerRuntime.EnqueueReplayV2Batch(batch))
            KeyViewerRuntime.ForceEndReplayV2(batch.SessionGeneration);
    }

    private void EndV2(long sessionGeneration)
    {
        if (Interlocked.Read(ref _v2SessionGeneration) != sessionGeneration)
            return;
        KeyViewerRuntime.ForceEndReplayV2(sessionGeneration);
    }

    private bool IsActiveWithoutProbe()
    {
        try { return _activeProperty?.GetValue(null) is true; }
        catch { return false; }
    }

    private bool IsRemoteV2Active()
    {
        try { return _v2ActiveProperty?.GetValue(null) is true; }
        catch { return false; }
    }

    private bool CanReceiveV2()
        => _v2Subscription is { IsRetired: false };

    private bool ShouldSuppressV1()
        => IsV2PlaybackActive || Volatile.Read(ref _suppressV1ForCurrentPlayback);

    private void OnReplayStarted()
    {
        var suppress = IsV2PlaybackActive || (CanReceiveV2() && IsRemoteV2Active());
        Volatile.Write(ref _suppressV1ForCurrentPlayback, suppress);
        if (!suppress)
            KeyViewerRuntime.OnReplayStarted();
    }

    private void OnReplayEnded()
    {
        var suppressed = ShouldSuppressV1();
        Volatile.Write(ref _suppressV1ForCurrentPlayback, false);
        if (!suppressed)
            KeyViewerRuntime.OnReplayEnded();
    }

    private void OnReplayTouch(
        int action,
        int pointerId,
        float x,
        float y,
        float sourceWidth,
        float sourceHeight)
    {
        if (!_receiveTouch || ShouldSuppressV1())
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
        if (_receiveKeyboard && !ShouldSuppressV1())
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

    private void UnbindV1()
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
        _v2ActiveProperty = null;
        _startedEvent = null;
        _endedEvent = null;
        _touchEvent = null;
        _keyboardEvent = null;
        _startedHandler = null;
        _endedHandler = null;
        _touchHandler = null;
        _keyboardHandler = null;
        _bound = false;
        Volatile.Write(ref _suppressV1ForCurrentPlayback, false);
        _touchSubscribed = false;
        _keyboardSubscribed = false;
    }
}
