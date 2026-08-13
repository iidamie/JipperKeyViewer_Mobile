using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace JipperKeyViewer.Mobile;

/// <summary>
/// Owns the keyboard-only MapleStory font atlas. The manager's shared atlas is
/// already locked by the time a mod draws its foreground overlay, so changing
/// it here would invalidate the manager's font texture.
/// </summary>
internal static unsafe class KeyViewerFontRuntime
{
    private const string ResourceName =
        "JipperKeyViewer.Mobile.Assets.MAPLESTORY_OTF_BOLD.OTF";
    private const float RasterSize = 48f;

    private static ImFontAtlasPtr _atlas;
    private static ImFontPtr _font;
    private static IntPtr _context;
    private static IntPtr _failedContext;
    private static uint _texture;
    private static bool _ready;

    internal static ImFontPtr GetFont()
    {
        ImFontPtr fallback = ImGui.GetFont();
        IntPtr context;
        try { context = ImGui.GetCurrentContext(); }
        catch { return fallback; }
        if (context == IntPtr.Zero) return fallback;

        if (_ready && _context == context && _font.NativePtr != null)
            return _font;

        if (_ready)
        {
            // A changed ImGui context can belong to a replaced EGL context.
            // Do not delete a stale texture name from the new GL context.
            Release(deleteTexture: false);
        }

        if (_failedContext == context)
            return fallback;

        if (TryCreate(context, out string? error))
            return _font;

        _failedContext = context;
        PluginLog.Warn($"MapleStory keyboard font unavailable; using ImGui fallback: {error}");
        return fallback;
    }

    internal static void AddText(
        ImDrawListPtr drawList,
        ImFontPtr font,
        float size,
        System.Numerics.Vector2 position,
        uint color,
        string text)
    {
        if (_ready && font.NativePtr == _font.NativePtr && _texture != 0)
        {
            drawList.PushTextureID(_atlas.TexID);
            try
            {
                drawList.AddText(font, size, position, color, text);
            }
            finally
            {
                drawList.PopTextureID();
            }
            return;
        }

        drawList.AddText(font, size, position, color, text);
    }

    internal static void Reset()
    {
        Release(deleteTexture: true);
        _failedContext = IntPtr.Zero;
    }

    private static bool TryCreate(IntPtr context, out string? error)
    {
        ImFontAtlasPtr atlas = default;
        ImFontPtr font = default;
        uint texture = 0;
        try
        {
            byte[] fontBytes = ReadFontBytes();
            atlas = new ImFontAtlasPtr(ImGuiNative.ImFontAtlas_ImFontAtlas());
            if (atlas.NativePtr == null)
                throw new InvalidOperationException("ImFontAtlas allocation failed");

            IntPtr fontData = Marshal.AllocHGlobal(fontBytes.Length);
            try
            {
                Marshal.Copy(fontBytes, 0, fontData, fontBytes.Length);
                ImFontConfigPtr config = new(ImGuiNative.ImFontConfig_ImFontConfig());
                try
                {
                    // AddFont copies non-owned input data into this atlas.
                    config.FontDataOwnedByAtlas = false;
                    font = atlas.AddFontFromMemoryTTF(
                        fontData,
                        fontBytes.Length,
                        RasterSize,
                        config,
                        atlas.GetGlyphRangesDefault());
                    if (font.NativePtr == null || !atlas.Build())
                        throw new InvalidOperationException("ImGui could not build the MapleStory font atlas");
                }
                finally
                {
                    config.Destroy();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(fontData);
            }

            atlas.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);
            if (pixels == IntPtr.Zero || width <= 0 || height <= 0 || bytesPerPixel != 4)
                throw new InvalidOperationException("ImGui returned an invalid MapleStory font texture");

            texture = UploadTexture(pixels, width, height);
            atlas.SetTexID(new IntPtr((long)texture));
            atlas.ClearInputData();
            atlas.ClearTexData();

            _atlas = atlas;
            _font = font;
            _texture = texture;
            _context = context;
            _ready = true;
            _failedContext = IntPtr.Zero;
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            if (texture != 0)
                TryDeleteTexture(texture);
            if (atlas.NativePtr != null)
                atlas.Destroy();
            error = exception.Message;
            return false;
        }
    }

    private static byte[] ReadFontBytes()
    {
        Assembly assembly = typeof(KeyViewerFontRuntime).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("embedded MAPLESTORY_OTF_BOLD.OTF was not found");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static uint UploadTexture(IntPtr pixels, int width, int height)
    {
        uint texture = 0;
        int previousActiveTexture = unchecked((int)Gl.Texture0);
        int previousTexture = 0;
        int previousUnpackAlignment = 4;
        int previousUnpackRowLength = 0;
        bool stateCaptured = false;
        try
        {
            Gl.ClearErrors();
            Gl.GetIntegerv(Gl.ActiveTextureState, out previousActiveTexture);
            Gl.ActiveTexture(Gl.Texture0);
            Gl.GetIntegerv(Gl.TextureBinding2D, out previousTexture);
            Gl.GetIntegerv(Gl.UnpackAlignment, out previousUnpackAlignment);
            Gl.GetIntegerv(Gl.UnpackRowLength, out previousUnpackRowLength);
            stateCaptured = true;

            Gl.GenTextures(1, out texture);
            if (texture == 0)
                throw new InvalidOperationException("glGenTextures returned zero");

            Gl.BindTexture(Gl.Texture2D, texture);
            Gl.TexParameteri(Gl.Texture2D, Gl.TextureMinFilter, (int)Gl.Linear);
            Gl.TexParameteri(Gl.Texture2D, Gl.TextureMagFilter, (int)Gl.Linear);
            Gl.TexParameteri(Gl.Texture2D, Gl.TextureWrapS, (int)Gl.ClampToEdge);
            Gl.TexParameteri(Gl.Texture2D, Gl.TextureWrapT, (int)Gl.ClampToEdge);
            Gl.PixelStorei(Gl.UnpackAlignment, 1);
            Gl.PixelStorei(Gl.UnpackRowLength, 0);
            Gl.TexImage2D(
                Gl.Texture2D,
                0,
                (int)Gl.Rgba,
                width,
                height,
                0,
                Gl.Rgba,
                Gl.UnsignedByte,
                pixels);
            uint error = Gl.GetError();
            if (error != Gl.NoError)
                throw new InvalidOperationException($"OpenGL font texture upload failed: 0x{error:X}");
            return texture;
        }
        catch
        {
            if (texture != 0)
                TryDeleteTexture(texture);
            throw;
        }
        finally
        {
            if (stateCaptured)
            {
                try
                {
                    Gl.ActiveTexture(Gl.Texture0);
                    Gl.BindTexture(Gl.Texture2D, unchecked((uint)Math.Max(0, previousTexture)));
                    Gl.PixelStorei(Gl.UnpackAlignment, NormalizeUnpackAlignment(previousUnpackAlignment));
                    Gl.PixelStorei(Gl.UnpackRowLength, Math.Max(0, previousUnpackRowLength));
                    Gl.ActiveTexture(unchecked((uint)Math.Max((int)Gl.Texture0, previousActiveTexture)));
                }
                catch { }
            }
        }
    }

    private static void Release(bool deleteTexture)
    {
        if (deleteTexture && _texture != 0)
            TryDeleteTexture(_texture);
        if (_atlas.NativePtr != null)
            _atlas.Destroy();
        _atlas = default;
        _font = default;
        _context = IntPtr.Zero;
        _texture = 0;
        _ready = false;
    }

    private static void TryDeleteTexture(uint texture)
    {
        try { Gl.DeleteTextures(1, ref texture); }
        catch { }
    }

    private static int NormalizeUnpackAlignment(int value)
        => value is 1 or 2 or 4 or 8 ? value : 4;

    private static class Gl
    {
        internal const uint Texture0 = 0x84C0;
        internal const uint NoError = 0;
        internal const uint ActiveTextureState = 0x84E0;
        internal const uint Texture2D = 0x0DE1;
        internal const uint TextureBinding2D = 0x8069;
        internal const uint TextureMinFilter = 0x2801;
        internal const uint TextureMagFilter = 0x2800;
        internal const uint TextureWrapS = 0x2802;
        internal const uint TextureWrapT = 0x2803;
        internal const uint UnpackAlignment = 0x0CF5;
        internal const uint UnpackRowLength = 0x0CF2;
        internal const uint Linear = 0x2601;
        internal const uint ClampToEdge = 0x812F;
        internal const uint Rgba = 0x1908;
        internal const uint UnsignedByte = 0x1401;

        [DllImport("libGLESv3.so", EntryPoint = "glActiveTexture", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ActiveTexture(uint texture);

        [DllImport("libGLESv3.so", EntryPoint = "glBindTexture", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BindTexture(uint target, uint texture);

        [DllImport("libGLESv3.so", EntryPoint = "glDeleteTextures", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DeleteTextures(int count, ref uint textures);

        [DllImport("libGLESv3.so", EntryPoint = "glGenTextures", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void GenTextures(int count, out uint textures);

        [DllImport("libGLESv3.so", EntryPoint = "glGetError", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint GetError();

        [DllImport("libGLESv3.so", EntryPoint = "glGetIntegerv", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void GetIntegerv(uint name, out int value);

        [DllImport("libGLESv3.so", EntryPoint = "glPixelStorei", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PixelStorei(uint name, int value);

        [DllImport("libGLESv3.so", EntryPoint = "glTexImage2D", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void TexImage2D(
            uint target,
            int level,
            int internalFormat,
            int width,
            int height,
            int border,
            uint format,
            uint type,
            IntPtr pixels);

        [DllImport("libGLESv3.so", EntryPoint = "glTexParameteri", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void TexParameteri(uint target, uint name, int value);

        internal static void ClearErrors()
        {
            for (int i = 0; i < 8 && GetError() != NoError; i++) { }
        }
    }
}
