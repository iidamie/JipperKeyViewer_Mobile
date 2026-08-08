using StArray.ModManager.RuntimeAbstractions;

namespace JipperKeyViewer.Mobile;

internal sealed unsafe class GameApi
{
    private readonly IRuntimeClass _controllerClass;
    private readonly IRuntimeField? _controllerInstance;
    private readonly IRuntimeField? _gameWorld;
    private readonly IRuntimeField? _paused;
    private readonly IRuntimeMethod? _getController;

    private GameApi(IRuntimeAssembly assembly)
    {
        _controllerClass = assembly.GetClass(string.Empty, "scrController")
            ?? throw new InvalidOperationException("scrController was not found");
        _controllerInstance = FindField(_controllerClass, "_instance", "instance");
        _gameWorld = FindField(_controllerClass, "gameworld", "isGameWorld", "isGameworld");
        _paused = FindField(_controllerClass, "_paused", "paused");
        IRuntimeClass? adoBase = assembly.GetClass(string.Empty, "ADOBase");
        _getController = adoBase?.GetMethod("get_controller", 0)
            ?? _controllerClass.GetMethod("get_instance", 0);
    }

    internal static GameApi? Create()
    {
        try
        {
            IAppDomain? domain = RuntimeManager.GetDomain();
            if (domain == null) return null;
            IRuntimeAssembly? assembly = domain.OpenAssembly("Assembly-CSharp.dll")
                ?? domain.OpenAssembly("Assembly-CSharp");
            return assembly == null ? null : new GameApi(assembly);
        }
        catch (Exception exception)
        {
            PluginLog.Debug($"Game runtime is not ready: {exception.Message}");
            return null;
        }
    }

    internal bool? IsGameplayActive()
    {
        nint controller = GetController();
        if (controller == 0) return null;
        bool gameWorld = _gameWorld == null || Read(_gameWorld, controller, (byte)1) != 0;
        bool paused = Read(_paused, controller, (byte)0) != 0;
        return gameWorld && !paused;
    }

    private nint GetController()
    {
        try
        {
            nint controller = _getController?.InvokeStatic() ?? 0;
            if (controller != 0) return controller;
        }
        catch { }
        return Read(_controllerInstance, 0, nint.Zero);
    }

    private static IRuntimeField? FindField(IRuntimeClass type, params string[] names)
    {
        foreach (string name in names)
        {
            IRuntimeField? field = type.GetField(name);
            if (field != null) return field;
        }
        return null;
    }

    private static T Read<T>(IRuntimeField? field, nint instance, T fallback) where T : unmanaged
    {
        if (field == null || (!field.IsStatic && instance == 0)) return fallback;
        try { return field.GetValue<T>(instance); }
        catch { return fallback; }
    }
}
