// Much of this file is based on the Hexa.NET.ImGui example code. See the relevant example here:
// https://github.com/HexaEngine/Hexa.NET.ImGui/blob/main/Examples/ExampleGLFWOpenGL3/Program.cs

using Hexa.NET.GLFW;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using GLFWwindowPtr = Hexa.NET.GLFW.GLFWwindowPtr;

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Numerics;
using System.Diagnostics;
using ETS2LA.Shared;

using ETS2LA.Logging;
using ETS2LA.Controls;
using ETS2LA.Overlay.Window;
using ETS2LA.Overlay.AR;
using ETS2LA.ML;
using ETS2LA.ML.Vision;
using ETS2LA.Game.Telemetry;
using ETS2LA.State;
using ETS2LA.Translations;
using static ETS2LA.Translations.T;

namespace ETS2LA.Overlay;

#if WINDOWS
    public static class NativeMethods
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        public static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        public static extern uint TimeEndPeriod(uint uMilliseconds);
    }
#endif

public enum FontStyle
{
    Regular,
    Medium,
    SemiBold,
    Bold
}

public class OverlayHandler
{
    private static readonly Lazy<OverlayHandler> _instance = new(() => new OverlayHandler());
    public static OverlayHandler Current => _instance.Value;

    public ControlDefinition Interact = new ControlDefinition
    {   
        Id = "ETS2LA.Overlay.Interact",
        Name = _("Overlay Interaction"),
        Description = _("When this key is held, the overlay will receive mouse input and allow you to interact with it. NOTE: Interaction with items below the overlay is not possible during this time."),
        DefaultKeybind = "RightAlt",
        Type = ControlType.Boolean
    };

    public ARRenderer AR;
    private OverlaySettings overlaySettings;

    private bool isInteracting = false;
    private float bgOpacityTarget = 0.0f;
    private bool shutdown = false;
    private List<float> frameTimes = new List<float>();
    private List<double> remainingMs = new List<double>();
    
    private List<InternalWindow> windows = new();
    public Dictionary<FontStyle, ImFontPtr> Fonts = new Dictionary<FontStyle, ImFontPtr>();
    public const float DefaultFontSize = 18f;

    public bool IsOverlayFocused => isInteracting;
    public float AverageFrameTime => frameTimes.Count > 0 ? frameTimes.Average() : 0f;
    public float AverageRemainingTime => remainingMs.Count > 0 ? (float)remainingMs.Average() : 0f;

    public float OverlayWidth => GLFW.GetVideoMode(GLFW.GetPrimaryMonitor()).Width;
    public float OverlayHeight => GLFW.GetVideoMode(GLFW.GetPrimaryMonitor()).Height;
    public float OverlayMonitorRefreshRate => GLFW.GetVideoMode(GLFW.GetPrimaryMonitor()).RefreshRate;

    private string glslVersion = "#version 150";
    private GLFWwindowPtr glfwWindow;
    private ImGuiContextPtr imGuiContext;
    private ImPlotContextPtr ImPlotContext;
    private ImGuiIOPtr io;
    private GL gl;

    public OverlayHandler()
    {
        ControlsBackend.Current.RegisterControl(Interact);
        ControlsBackend.Current.On(Interact.Id, HandleInput);
        overlaySettings = OverlaySettingsHandler.Current.GetSettings();
        OverlaySettingsHandler.Current.OnSettingsUpdated += OnOverlaySettingsUpdated;

        Task.Factory.StartNew(RenderLoop, TaskCreationOptions.LongRunning);
        
        windows.Add(new ConsoleWindow());
        windows.Add(new DemoWindow());
        windows.Add(new StateWindow());

        if (MLSettings.Current.RenderVisionCameras)
            windows.Add(new VisionCamerasWindow());
    }

    private void OnOverlaySettingsUpdated(OverlaySettings newSettings)
    {
        overlaySettings = newSettings;
    }

    private void HandleInput(object sender, ControlChangeEventArgs e)
    {
        bool b = (bool)e.NewValue;
        if (b == isInteracting) { return; }
        isInteracting = b;
    }

    private void RenderLoop()
    {
        if(!InitGLFW())
        {
            Logger.Error(_("Failed to initialize overlay, OpenGL initialization failed."));
            return;
        }
        GLFW.MakeContextCurrent(glfwWindow);
        gl = new GL(new BindingsContext(glfwWindow));

        if (MLSettings.Current.RenderVisionCameras)
            VisionHandler.Current.Initialize(gl);
        
        if (!InitImGui())
        {
            Logger.Error(_("Failed to initialize overlay, ImGui initialization failed."));
            return;
        }

        #if WINDOWS
            // 1ms sleep accuracy on windows
            // Linux is fine from the get go
            NativeMethods.TimeBeginPeriod(1);
        #endif

        Stopwatch fs = Stopwatch.StartNew();
        int targetFramerate = (int)OverlayMonitorRefreshRate;
        double interval = 1000.0 / targetFramerate;
        double next = fs.Elapsed.TotalMilliseconds;
        double start = fs.Elapsed.TotalMilliseconds;

        while (GLFW.WindowShouldClose(glfwWindow) == 0 && !shutdown)
        {
            if (overlaySettings.LimitFramerate)
                interval = 1000.0 / overlaySettings.MaxFramerate;
            else
                interval = 1000.0 / targetFramerate;

            start = fs.Elapsed.TotalMilliseconds;
            if (next < start)
                next = start;
            
            next += interval;

            if (!isInteracting) { 
                // This has to be called each frame to properly update the flags.
                // For whatever reason they are set back to default. Shouldn't affect
                // performance, it's just weird...
                ImGui.GetPlatformIO().Viewports[0].Flags |= ImGuiViewportFlags.NoInputs;
                bgOpacityTarget = 0.0f;

                # if LINUX
                    GLFW.SetWindowAttrib(glfwWindow, GLFW.GLFW_MOUSE_PASSTHROUGH, 1);
                # endif
            }
            else  {
                bgOpacityTarget = 0.5f;
                # if LINUX
                    GLFW.SetWindowAttrib(glfwWindow, GLFW.GLFW_MOUSE_PASSTHROUGH, 0);
                # endif
            }

            GLFW.PollEvents();

            // Skip rendering if we're minimized, though this should actually
            // never happen for the overlay.
            if (GLFW.GetWindowAttrib(glfwWindow, GLFW.GLFW_ICONIFIED) != 0)
            {
                ImGuiImplGLFW.Sleep(10);
                continue;
            }

            GLFW.MakeContextCurrent(glfwWindow);

            ImGuiImplOpenGL3.NewFrame();
            ImGuiImplGLFW.NewFrame();
            ImGui.NewFrame();

            // This renders the virtual cameras into their respective framebuffers.
            if (MLSettings.Current.RenderVisionCameras)
                VisionHandler.Current.Render();

            gl.Viewport(0, 0, (int)OverlayWidth, (int)OverlayHeight);

            // The actual rendering is happening here,
            // all other calls are just setup.
            try { 
                if (AR == null) 
                {
                    AR = new ARRenderer(gl);

                    if (MLSettings.Current.RenderVisionCameras)
                    {
                        VisionCamerasARRenderer renderer = new VisionCamerasARRenderer();
                        renderer.Register();
                    }
                }
                bool paused = GameTelemetry.Current.GetCurrentData().paused;
                if (overlaySettings.RenderAR && (!overlaySettings.DontRenderWhenPaused || !paused)) AR.Render(); 
            }
            catch (Exception ex) {
                Logger.Error(_("Error in AR rendering: {0}", ex.Message));
            }

            try { OnUIRender(); }
            catch (Exception ex) {
                Logger.Error(_("Error rendering overlay: {0}", ex.Message));
            }
            // ---

            ImGui.Render();

            gl.ClearColor(0f, 0f, 0f, bgOpacityTarget);
            gl.Clear(GLClearBufferMask.ColorBufferBit);

            try { AR.RenderShaders(); } 
            catch (Exception ex) {
                Logger.Error(_("Error in AR OpenGL pass: {0}", ex.Message));
            }
            
            ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());

            if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
            {
                ImGui.UpdatePlatformWindows();
                ImGui.RenderPlatformWindowsDefault();
            }

            GLFW.SwapInterval(0); // disable vsync
            GLFW.SwapBuffers(glfwWindow);

            double remaining = next - fs.Elapsed.TotalMilliseconds;
            if (remaining > 1.0)
                Thread.Sleep((int)(remaining - 1));
            while (fs.Elapsed.TotalMilliseconds < next)
                Thread.SpinWait(10);
            
            frameTimes.Add((float)(fs.Elapsed.TotalMilliseconds - start));
            if (frameTimes.Count > targetFramerate) { frameTimes.RemoveAt(0); }
            
            remainingMs.Add(remaining);
            if (remainingMs.Count > targetFramerate) { remainingMs.RemoveAt(0); }
        }

        ImGuiImplOpenGL3.Shutdown();
        ImGuiImplOpenGL3.SetCurrentContext(null);
        ImGuiImplGLFW.Shutdown();
        ImGuiImplGLFW.SetCurrentContext(null);
        ImGui.DestroyContext();
        gl.Dispose();

        // Clean up and terminate GLFW
        GLFW.DestroyWindow(glfwWindow);
        GLFW.Terminate();
    }

    private void OnUIRender()
    {
        if (isInteracting)
        {
            ImGui.Begin(_("Interaction Mode"), ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground);
            ImGui.SetWindowPos(new Vector2(OverlayWidth / 2 - 60, 10), ImGuiCond.Always);
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), _("Interaction Mode"));

            ImGui.Spacing();
            try
            {
                foreach (var window in windows) {
                    bool isOpen = window.IsWindowOpen;
                    Vector4 color = isOpen ? new Vector4(0.5f, 0.6f, 0.5f, 1f) : new Vector4(0.6f, 0.5f, 0.5f, 1f);

                    ImGui.TextColored(color, isOpen ? "[X]" : "[   ]");
                    ImGui.SameLine();
                    ImGui.TextColored(color, window.Definition.Title);
        
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(_("Click to {0} this window", isOpen ? _("hide") : _("show")));
                    }
                    if (ImGui.IsItemClicked())
                    {
                        window.IsWindowOpen = !window.IsWindowOpen;
                    }
                }
            } catch (Exception ex)
            {
                Logger.Error(_("Error rendering interaction mode window controls: {0}", ex.Message));
            }
            ImGui.End();
        }

        ImGui.SetNextWindowPos(new Vector2(OverlayWidth - 10, 10), ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.Begin(_("Performance Overlay"), ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground);

        var fps = (AverageFrameTime > 0) ? (int)(1 / (AverageFrameTime / 1000f)) : 0;
        int freePercentage;
        if (overlaySettings.LimitFramerate)
            freePercentage = (int)(AverageRemainingTime / (1000f / overlaySettings.MaxFramerate) * 100f);
        else
            freePercentage = (int)(AverageRemainingTime / (1000f / OverlayMonitorRefreshRate) * 100f);
        
        ImGui.TextColored(new Vector4(1f,1f,1f,0.5f), $"{(int)(1/(AverageFrameTime / 1000f))}");
        ImGui.TextColored(new Vector4(1f,1f,1f,0.5f), $"{freePercentage}%%");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(_("Overlay free CPU time percentage"));
        }

        ImGui.End();

        foreach (InternalWindow window in windows)
        {
            try
            {
                if (!window.IsWindowOpen) { continue; }
                if (window.Definition.NoWindow.GetValueOrDefault(false))
                {
                    window.Render();
                    continue;
                }

                // Plugin developer has set the width and height, these get applied
                // once each startup. Use ImGuiWindowFlags to disallow movement.
                if (window.Definition.Width.HasValue || window.Definition.Height.HasValue)
                {
                    var width = window.Definition.Width.GetValueOrDefault(480);
                    var height = window.Definition.Height.GetValueOrDefault(320);
                    if (width > 0 && height > 0)
                        ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Once);
                }

                // Plugin developer has set a sizing function, this is continuous
                // throughout the lifecycle of the window.
                if (window.Definition.SizingFunction.HasValue && window.Definition.SizingFunction.Value != null)
                {
                    var (width, height) = window.Definition.SizingFunction.Value();
                    ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Always);
                }

                ImGui.SetNextWindowBgAlpha(window.Definition.Alpha.GetValueOrDefault(0.9f));
                ImGui.Begin(window.Definition.Title, window.Definition.Flags.GetValueOrDefault(ImGuiWindowFlags.None));
                
                // Plugin developer has set X and Y values, these are applied
                // once each startup. Use ImGuiWindowFlags to disallow resizing.
                if (window.Definition.X.HasValue || window.Definition.Y.HasValue)
                {
                    int x = window.Definition.X.GetValueOrDefault((int)(OverlayWidth / 2));
                    int y = window.Definition.Y.GetValueOrDefault((int)(OverlayHeight / 2));
                    if (x >= 0 && y >= 0)
                    {
                        ImGui.SetWindowPos(new Vector2(x, y), ImGuiCond.Once);
                    }
                } 
                // If no X and Y are set, then for the first time we'll open this window
                // at the center of the screen.
                else
                {
                    int x = (int)OverlayWidth / 2;
                    int y = (int)OverlayHeight / 2;
                    if (x >= 0 && y >= 0)
                    {
                        ImGui.SetWindowPos(new Vector2(x, y), ImGuiCond.FirstUseEver);
                    }
                }

                // Plugin developer has set a location function, this is continuous
                // throughout the lifecycle of the window.
                if (window.Definition.LocationFunction.HasValue && window.Definition.LocationFunction.Value != null)
                {
                    var (x, y) = window.Definition.LocationFunction.Value();
                    ImGui.SetWindowPos(new Vector2(x, y), ImGuiCond.Always);
                }

                var isCollapsed = ImGui.IsWindowCollapsed();
                if (isCollapsed) {
                    ImGui.End(); 
                    continue; 
                }

                try
                {
                    RenderWindowContextMenu(window);
                } catch (Exception ex)
                {
                    Logger.Error(_("Error rendering context menu for window {0}: {1}", window.Definition.Title, ex.Message));
                }

                try
                {
                    window.Render();
                } catch (Exception ex)
                {
                    Logger.Error(_("Error rendering window {0}: {1}", window.Definition.Title, ex.Message));
                }

                ImGui.End();
            }
            catch (Exception ex)
            {
                try { ImGui.End(); }
                catch { }
                Logger.Error(_("Error rendering window {0}: {1}", window.Definition.Title, ex.Message));
            }
        }
    }

    private unsafe void RenderWindowContextMenu(InternalWindow window)
    {
        if (ImGui.BeginPopupContextWindow((byte*)0, ImGuiPopupFlags.MouseButtonRight))
        {
            window.RenderContextMenu();
            if (ImGui.MenuItem(_("Close")))
            {
                window.IsWindowOpen = false;
            }
            ImGui.EndPopup();
        }
    }

    private bool InitImGui()
    {
        imGuiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(imGuiContext);
        
        ImPlot.SetImGuiContext(imGuiContext);
        ImPlotContext = ImPlot.CreateContext();
        ImPlot.SetCurrentContext(ImPlotContext);

        io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;     // Enable Keyboard Controls
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad;      // Enable Gamepad Controls
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;         // Enable Docking
        if (OverlaySettingsHandler.Current.GetSettings().SupportMultipleViewports)
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;       // Enable Multi-Viewport / Platform Windows

        // Tweak our styling just a little
        ImGui.StyleColorsDark();
        var style = ImGui.GetStyle();
        style.WindowRounding = 3.0f;
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.1f, 0.1f, 0.1f, 1f);

        // And then we scale by the monitor DPI
        var mon = GLFW.GetPrimaryMonitor();
        float mainScale = ImGuiImplGLFW.GetContentScaleForMonitor(Unsafe.BitCast<Hexa.NET.GLFW.GLFWmonitorPtr, Hexa.NET.ImGui.Backends.GLFW.GLFWmonitorPtr>(mon));

        style.ScaleAllSizes(mainScale);
        style.FontScaleDpi = mainScale;
        io.ConfigDpiScaleFonts = true;
        io.ConfigDpiScaleViewports = true;

        Language language = StateSettingsHandler.Current.GetSettings().DisplayLanguage;
        string fontName = "Geist";

        switch (language.EnglishName)
        {
            case "Japanese":
                fontName = "NotoSansJP";
                break;
            case "Chinese (Simplified)":
                fontName = "NotoSansSC";
                break;
            case "Chinese (Traditional)":
                fontName = "NotoSansTC";
                break;
            case "Korean":
                fontName = "NotoSansKR";
                break;
            default:
                break;
        }

        List<Tuple<FontStyle, string>> fonts = new List<Tuple<FontStyle, string>>()
        {
            new Tuple<FontStyle, string>(FontStyle.Medium, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Fonts", $"{fontName}", $"{fontName}-Medium.ttf")),
            new Tuple<FontStyle, string>(FontStyle.Regular, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Fonts", $"{fontName}", $"{fontName}-Regular.ttf")),
            new Tuple<FontStyle, string>(FontStyle.SemiBold, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Fonts", $"{fontName}", $"{fontName}-SemiBold.ttf")),
            new Tuple<FontStyle, string>(FontStyle.Bold, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Fonts", $"{fontName}", $"{fontName}-Bold.ttf")),
        };

        // Set fonts
        unsafe
        {
            for (int i = 0; i < fonts.Count; i++)
            {
                string fontPath = fonts[i].Item2;
                if (!File.Exists(fontPath))
                {
                    Logger.Error(_("Font file not found at {0}", fontPath));
                    continue;
                }
                ImFont* font = io.Fonts.AddFontFromFileTTF(fontPath, DefaultFontSize);
                ImFontPtr fontPtr = new ImFontPtr(font);
                Fonts[fonts[i].Item1] = fontPtr;
            }
        }

        ImGuiImplGLFW.SetCurrentContext(imGuiContext);
        if (!ImGuiImplGLFW.InitForOpenGL(Unsafe.BitCast<GLFWwindowPtr, Hexa.NET.ImGui.Backends.GLFW.GLFWwindowPtr>(glfwWindow), true))
        {
            Logger.Error(_("Failed to init ImGui Impl GLFW"));
            GLFW.Terminate();
            return false;
        }

        ImGuiImplOpenGL3.SetCurrentContext(imGuiContext);
        if (!ImGuiImplOpenGL3.Init(glslVersion))
        {
            Logger.Error(_("Failed to init ImGui Impl OpenGL3"));
            GLFW.Terminate();
            return false;
        }

        gl.Enable(GLEnableCap.Blend);
        gl.BlendFunc(GLBlendingFactor.SrcAlpha, GLBlendingFactor.OneMinusSrcAlpha);
        gl.ClearColor(0f, 0f, 0f, 0f); // Transparent background
        return true;
    }

    private bool InitGLFW()
    {
        unsafe
        {
            GLFW.SetErrorCallback((error, description) =>
            {
                # if DEBUG
                Logger.Error(_("GLFW Error {0}: {1}", error, Utils.DecodeStringUTF8(description)));
                # endif
            });
        }

        unsafe
        {
            Logger.Info(_("Initializing GLFW Version: {0}", Utils.DecodeStringUTF8(GLFW.GetVersionString())));
        }

        // This code sets the platform to X11 instead of wayland. This only needs to be
        // done inside vscode for whatever reason. https://github.com/opentk/opentk/issues/1823
        string? sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        string? useWayland = Environment.GetEnvironmentVariable("GLFW_USE_WAYLAND");
        if (sessionType == "wayland" && useWayland == "0")
        {
            GLFW.InitHint(GLFW.GLFW_PLATFORM, GLFW.GLFW_PLATFORM_X11);
        }

        Logger.Info(_("Initializing GLFW..."));
        GLFW.Init();
        GLFW.WindowHint(GLFW.GLFW_CONTEXT_VERSION_MAJOR, 3);
        GLFW.WindowHint(GLFW.GLFW_CONTEXT_VERSION_MINOR, 2);
        GLFW.WindowHint(GLFW.GLFW_OPENGL_PROFILE, GLFW.GLFW_OPENGL_CORE_PROFILE);  // 3.2+ only

        # if LINUX
            GLFW.WindowHint(GLFW.GLFW_CONTEXT_CREATION_API, GLFW.GLFW_EGL_CONTEXT_API);
        # endif

        GLFW.WindowHint(GLFW.GLFW_TRANSPARENT_FRAMEBUFFER, 1);  // Transparent
        GLFW.WindowHint(GLFW.GLFW_DECORATED, 0);                // No window decorations
        GLFW.WindowHint(GLFW.GLFW_FLOATING, 1);                 // Always on top
        GLFW.WindowHint(GLFW.GLFW_FOCUSED, 0);                  // Start unfocused
        GLFW.WindowHint(GLFW.GLFW_FOCUS_ON_SHOW, 0);            // Start unfocused
        
        var mon = GLFW.GetPrimaryMonitor();
        int width, height;
        width = GLFW.GetVideoMode(mon).Width;
        height = GLFW.GetVideoMode(mon).Height;

        // NOTE: Width and height set to screen-2
        // If they are set to the screen size, windows does some optimizations that cause the window
        // to go full black when focused. Setting these to -2 seems to prevent that.
        glfwWindow = GLFW.CreateWindow(width - 2, height - 2, "ETS2LA Overlay", null, null);
        if (glfwWindow.IsNull)
        {
            Logger.Error(_("Failed to create GLFW window"));
            GLFW.Terminate();
            return false;
        }

        # if WINDOWS
        GLFW.SetWindowPos(glfwWindow, 1, 1);
        # endif

        return true;
    }

    public void RegisterWindow(WindowDefinition def, Action renderAction, Optional<Action> renderContextMenuAction = default)
    {
        foreach (var window in windows)
        {
            if (window.Definition.Title == def.Title)
            {
                window.Definition = def;
                window.Render = renderAction;
                window.RenderContextMenu = renderContextMenuAction.GetValueOrDefault(() => { });
                return;
            }
        }
        
        var newWindow = new ExternalWindow(def, renderAction, renderContextMenuAction.GetValueOrDefault(() => { }));
        if (def.Open.HasValue)
        {
            newWindow.IsWindowOpen = def.Open.Value;
        }

        windows.Add(newWindow);
    }

    public void UnregisterWindow(WindowDefinition def)
    {
        windows.RemoveAll(w => w.Definition.Title == def.Title);
    }

    public void OpenWindow(string windowName)
    {
        var window = windows.FirstOrDefault(w => w.Definition.Title == windowName);
        if (window != null)
        {
            window.IsWindowOpen = true;
        }
    }

    public void CloseWindow(string windowName)
    {
        var window = windows.FirstOrDefault(w => w.Definition.Title == windowName);
        if (window != null)
        {
            window.IsWindowOpen = false;
        }
    }

    public void Shutdown()
    {
        shutdown = true;
        VisionHandler.Current.Shutdown();
    }
}
