using System.Runtime.InteropServices;
using StArray.ModManager.Hooks;

namespace JipperKeyViewer.Mobile;

/// <summary>
/// Optional direct key-event fallback for Android builds where Unity's legacy
/// Input API does not expose an attached hardware keyboard.
/// </summary>
public static partial class KeyViewerNativeKeyboardKeyEventHook
{
    private static bool _installed;

    internal static bool Install()
    {
        if (_installed) return true;
        try
        {
            bool installed = InstallHooks();
            _installed = installed;
            KeyViewerKeyboardDiagnostics.RecordInstall(installed);
            if (installed)
                PluginLog.Info("Direct initializeKeyEvent keyboard hook installed");
            else
            {
                PluginLog.Debug("Direct initializeKeyEvent keyboard hook unavailable");
            }
            return installed;
        }
        catch (Exception exception)
        {
            KeyViewerKeyboardDiagnostics.RecordInstall(installed: false);
            PluginLog.Debug($"Direct key-event hook installation deferred: {exception.Message}");
            return false;
        }
    }

    internal static void Uninstall()
    {
        if (!_installed) return;
        try { UninstallHooks(); }
        catch (Exception exception) { PluginLog.Debug($"Direct key-event hook unload failed: {exception.Message}"); }
        finally
        {
            _installed = false;
        }
    }

    [NativeHook(
        "libinput.so",
        "_ZN7android13InputConsumer18initializeKeyEventEPNS_8KeyEventEPKNS_12InputMessageE",
        Convention = CallingConvention.Cdecl)]
    public unsafe static void OnInitializeKeyEvent(
        void* consumer,
        void* keyEvent,
        void* message)
    {
        KeyViewerKeyboardDiagnostics.RecordCallback();
        try
        {
            OnInitializeKeyEventOriginal(consumer, keyEvent, message);
        }
        catch (Exception exception)
        {
            PluginLog.Error($"InputConsumer.initializeKeyEvent original failed: {exception}");
            return;
        }
        if (keyEvent != null)
            KeyViewerKeyboardInput.CaptureNativeEvent(new IntPtr(keyEvent));
    }
}
