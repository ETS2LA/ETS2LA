using ETS2LA.Game.SDK;
using ETS2LA.Logging;
using ETS2LA.Game.Telemetry;
using ETS2LA.Overlay.Shaders;

using System.Numerics;
using TruckLib;

using Hexa.NET.ImGui;
using Hexa.NET.OpenGL;
using Hexa.NET.ImGui.Backends.OpenGL3;

namespace ETS2LA.Overlay.AR;

public class ARRenderer
{
    private CameraData cameraData;
    private GameTelemetryData telemetryData;
    private List<ARRenderCallback> renderCallbacks = new();
    private Matrix4x4 thisFrameProjection;
    private Matrix4x4 thisFrameView;
    private Matrix4x4 thisFrameViewProjection;
    private int thisFrameOffsetX = 0;
    private int thisFrameOffsetZ = 0;
    private int thisFrameWidth = 0;
    private int thisFrameHeight = 0;

    private OverlaySettings overlaySettings;

    // These are all variables that are needed for
    // rendering ImGui windows in 3D space.
    private GL gl;
    private bool isWindowContextInitialized = false;
    private ImGuiContextPtr arWindowContext;
    private ARWindowBuffer currentWindowBuffer;
    private ImGuiContextPtr oldContext;

    // Shaders
    private LineWithGradient lineWithGradientShader;

    public ARRenderer(GL gl)
    {
        this.gl = gl;
        this.lineWithGradientShader = new LineWithGradient(gl);

        // The font atlas needs to be shared between the main context
        // and our AR context here.
        var mainContext = ImGui.GetCurrentContext();
        unsafe
        {
            arWindowContext = ImGui.CreateContext(mainContext.IO.Fonts);
        }

        // I've set the default size of the AR window buffer to 1280x720.
        // TODO: Dynamic? Needs changing? TBD
        ImGui.SetCurrentContext(arWindowContext);
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1280, 720);
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;

        currentWindowBuffer = new ARWindowBuffer(gl, 1280, 720);
        cameraData = CameraProvider.Current.GetCurrentData();
        telemetryData = GameTelemetry.Current.GetCurrentData();

        overlaySettings = OverlaySettingsHandler.Current.GetSettings();
        OverlaySettingsHandler.Current.OnSettingsUpdated += OnOverlaySettingsUpdated;

        ImGui.SetCurrentContext(mainContext);

    }

    private void OnOverlaySettingsUpdated(OverlaySettings newSettings)
    {
        overlaySettings = newSettings;
    }

    /// <summary>
    ///  Renders all registered AR callbacks. This should be called every frame
    ///  from the main overlay rendering loop. <br/> 
    ///  Note: 3rd parties should not call this function.
    /// </summary>
    public void Render()
    {
        thisFrameProjection = default;
        thisFrameView = default;
        thisFrameViewProjection = default;
        GetViewMatrix();

        thisFrameWidth = (int)OverlayHandler.Current.OverlayWidth;
        thisFrameHeight = (int)OverlayHandler.Current.OverlayHeight;

        foreach (var callback in renderCallbacks)
        {
            try { callback.Render3D(); }
            catch (Exception ex)
            {
                Logger.Error($"Exception in AR render callback '{callback.Definition.Name}': {ex}");
            }
        }
    }

    /// <summary>
    ///  Renders all shaders for this frame.
    /// </summary>
    public void RenderShaders()
    {
        lineWithGradientShader.RenderPass();
    }

    /// <summary>
    ///  Register a new AR rendering callback. The provided callback will be called
    ///  every frame.
    /// </summary>
    /// <param name="callback">Callback definition / instance</param>
    public void RegisterRenderCallback(ARRenderCallback callback)
    {
        renderCallbacks.Add(callback);
    }

    /// <summary>
    ///  Unregister a previously registered callback.
    /// </summary>
    /// <param name="name">Name of the callback to remove</param>
    public void UnregisterRenderCallback(string name)
    {
        renderCallbacks.RemoveAll(callback => callback.Definition.Name == name);
    }

    /// <summary>
    ///  Get the current camera's view matrix. <br/>
    ///  NOTE: This matrix does not take into account ETS2 world wrapping.
    ///        You'll need to negate 512 * cameraData.cx from X and 512 * cameraData.cy from Z 
    ///        of other objects to get the relative position to the camera right. This is already
    ///        applied in all functions of this class.
    /// </summary>
    /// <returns>View matrix computed from camera data.</returns>
    public Matrix4x4 GetViewMatrix()
    {
        if (thisFrameView != default)
            return thisFrameView;
        

        thisFrameOffsetX = 512 * cameraData.cx;
        thisFrameOffsetZ = 512 * cameraData.cy;

        Vector3 camPos = new Vector3(
            cameraData.position.X, 
            cameraData.position.Y, 
            cameraData.position.Z
        );

        Quaternion camRot = new Quaternion(
            cameraData.rotation.X,
            cameraData.rotation.Y,
            cameraData.rotation.Z,
            cameraData.rotation.W
        );

        // The game's output is a bit weird, so we need to do adjustments to get it
        // to match what System.Numerics expects. There might be multiple inversions here?
        // TODO: Could be simplified?
        Quaternion invQuat = Quaternion.Conjugate(camRot);
        Vector3 euler = invQuat.ToEuler();
        Quaternion filteredRot = Quaternion.CreateFromYawPitchRoll(-euler.Y + (float)Math.PI, -euler.Z + (float)Math.PI, -euler.X);

        Matrix4x4 cameraWorldMatrix = Matrix4x4.CreateFromQuaternion(filteredRot) * 
                                      Matrix4x4.CreateTranslation(camPos);

        Matrix4x4.Invert(cameraWorldMatrix, out Matrix4x4 viewMatrix);
        thisFrameView = viewMatrix;
        return viewMatrix;
    }

    /// <summary>
    ///  Get the camera's projection matrix.
    /// </summary>
    /// <returns>Projection matrix returned by the game.</returns>
    public Matrix4x4 GetProjectionMatrix()
    {
        if (thisFrameProjection != default)
            return thisFrameProjection;
        
        // Game uses 0-1 for depth, but System.Numerics expects -1 to 1.
        // Simple transpose is enough to convert.
        thisFrameProjection = Matrix4x4.Transpose(cameraData.projection);
        return thisFrameProjection;
    }

    /// <summary>
    ///  Convert a world position into a screen coordinate. This takes all current
    ///  camera variables into account.
    /// </summary>
    /// <param name="worldPos">Target world position.</param>
    /// <param name="destinationWidth">Width of the destination viewport.</param>
    /// <param name="destinationHeight">Height of the destination viewport.</param>
    /// <returns>Screen coordinate if the world position is visible, otherwise null.</returns>
    public Vector2? WorldToScreen(Vector3 worldPos, int destinationWidth, int destinationHeight)
    {
        if (thisFrameViewProjection == default)
            thisFrameViewProjection = GetViewMatrix() * GetProjectionMatrix();

        Vector4 clipSpacePos = Vector4.Transform(new Vector4(worldPos, 1.0f), thisFrameViewProjection);
        if (clipSpacePos.W <= 0.1f) return null; // behind the camera

        // perspective divide to normalize coordinates
        // so that means x,y is -1 to 1 where 0,0 is the center
        Vector3 ndc = new Vector3(clipSpacePos.X, clipSpacePos.Y, clipSpacePos.Z) / clipSpacePos.W;
        // now -1 to 1 to screen coordinates
        float x = (ndc.X + 1.0f) * 0.5f * destinationWidth;
        float y = (1.0f - ndc.Y) * 0.5f * destinationHeight;

        return new Vector2(x, y);
    }

    /// <summary>
    ///  Converts a world position to -1 to 1 normalized device coordinates.
    ///  Used by shaders.
    /// </summary>
    public Vector2? WorldToNDC(Vector3 worldPos)
    {
        if (thisFrameViewProjection == default)
            thisFrameViewProjection = GetViewMatrix() * GetProjectionMatrix();

        Vector4 clipSpacePos = Vector4.Transform(new Vector4(worldPos, 1.0f), thisFrameViewProjection);
        if (clipSpacePos.W <= 0.1f) return null; // behind

        return new Vector2(clipSpacePos.X / clipSpacePos.W, clipSpacePos.Y / clipSpacePos.W);
    }

    /// <summary>
    ///  This function will return the camera space coordinates of the provided ARCoordinate
    ///  while also taking into account the coordinate center.
    /// </summary>
    /// <param name="coord">ARCoordinate to convert.</param>
    /// <returns>camera space coordinates.</returns>
    /// <exception cref="ArgumentException"></exception>
    public Vector3 ARCoordinateToVector3(ARCoordinate coord)
    {
        switch (coord.Center)
        {
            case ARCoordinateCenter.World:
                return new Vector3(
                    coord.OffsetX - thisFrameOffsetX, 
                    coord.OffsetY, 
                    coord.OffsetZ - thisFrameOffsetZ
                );
            case ARCoordinateCenter.Truck:
                Vector3 offset = coord.OffsetToVector3();
                return new Vector3(
                    offset.X + cameraData.truckPosition.X - thisFrameOffsetX,
                    offset.Y + cameraData.truckPosition.Y,
                    offset.Z + cameraData.truckPosition.Z - thisFrameOffsetZ
                );
            case ARCoordinateCenter.Camera:
                return coord.OffsetToVector3() + cameraData.position;
            default:
                throw new ArgumentException("Invalid ARCoordinateCenter");
        }
    }

    private uint ConvertColor(uint rgba)
    {
        // ImGui uses ABGR, but we have ARGB, so we need to convert it.
        return
            ((rgba & 0xFF000000) >> 24) |
            ((rgba & 0x00FF0000) >> 8)  |
            ((rgba & 0x0000FF00) << 8)  |
            ((rgba & 0x000000FF) << 24); 
    }

    private bool AllPointsOutsideRenderDistance(ARCoordinate[] points)
    {
        float maxDistance = overlaySettings.MaxARDistance;
        Vector3 cameraPos = cameraData.position;

        foreach (var point in points)
        {
            Vector3 worldPos = ARCoordinateToVector3(point);
            float distance = Vector3.Distance(worldPos, cameraPos);
            if (distance <= maxDistance)
                return false;
        }

        return true;
    }

    /// <summary>
    ///  Draw a line in 3D space. The line will be transformed and projected
    ///  onto the AR overlay.
    /// </summary>
    /// <param name="start">Start position of the line.</param>
    /// <param name="end">End position of the line.</param>
    /// <param name="color">Color of the line.</param>
    /// <param name="thickness">Thickness of the line in pixels.</param>
    public void Draw3DLine(ARCoordinate start, ARCoordinate end, UInt32 color, float thickness = 1.0f)
    {
        if (AllPointsOutsideRenderDistance(new ARCoordinate[] { start, end }))
            return;
        
        Vector2? p1 = WorldToScreen(ARCoordinateToVector3(start), thisFrameWidth, thisFrameHeight);
        Vector2? p2 = WorldToScreen(ARCoordinateToVector3(end), thisFrameWidth, thisFrameHeight);

        if (!p1.HasValue || !p2.HasValue) return;

        ImGui.GetBackgroundDrawList().AddLine(
            p1.Value, p2.Value, 
            ConvertColor(color), thickness
        );
    }

    /// <summary>
    ///   Renders a HUD guide line from a single list of centerline points.
    ///   WARNING: This function has partly been created by Gemini to support the shader it made.
    ///            This function has however been modified extensively, unlike the shader itself.
    /// </summary>
    /// <param name="points">Ordered path coordinates (from closest to farthest point)</param>
    /// <param name="color">RGBA uint color format</param>
    /// <param name="glowWidth">Width of the glow bleed in world units</param>
    /// <param name="isLeftLine">True if this is the left boundary line, False if right boundary</param>
    public void Draw3DLineWithGradient(IReadOnlyList<ARCoordinate> leftPoints, IReadOnlyList<ARCoordinate> rightPoints, uint color, float transparentValue = 0.0f, uint secondaryColor = 0x00000000, float secondaryColorDist = -1f)
    {
        if (leftPoints == null || rightPoints == null)
            return;

        int count = Math.Min(leftPoints.Count, rightPoints.Count);
        if (count < 2)
            return;

        Vector3 camPos = cameraData.position;
        float nearFadeStart = 30f;
        float nearFadeEnd = 10f;
        float farFadeStart = 60f;
        float farFadeEnd = 150f;
        float secondaryColorFadeStart = secondaryColorDist - 10f;
        float secondaryColorFadeEnd = secondaryColorDist + 10f;

        Vector4 colVec = new Vector4(
            ((color & 0xFF000000) >> 24) / 255.0f,
            ((color & 0x00FF0000) >> 16) / 255.0f,
            ((color & 0x0000FF00) >> 8) / 255.0f,
            (color & 0x000000FF) / 255.0f
        );

        Vector4 secondaryColorVec = new Vector4(
            ((secondaryColor & 0xFF000000) >> 24) / 255.0f,
            ((secondaryColor & 0x00FF0000) >> 16) / 255.0f,
            ((secondaryColor & 0x0000FF00) >> 8) / 255.0f,
            (secondaryColor & 0x000000FF) / 255.0f
        );

        float GetProgressFloat(float distance)
        {
            if (distance < nearFadeEnd)
                return 1.0f;
            if (distance < nearFadeStart)
                return (distance - nearFadeStart) / (nearFadeEnd - nearFadeStart);
            if (distance > farFadeStart)
                return (distance - farFadeStart) / (farFadeEnd - farFadeStart);
            if (distance > farFadeEnd)
                return 1.0f;
            return 0.0f;
        }

        Vector4 GetColor(float distance)
        {
            if (secondaryColorDist > 0f && distance >= secondaryColorFadeStart && distance <= secondaryColorFadeEnd)
            {
                float t = (distance - secondaryColorFadeStart) / (secondaryColorFadeEnd - secondaryColorFadeStart);
                return Vector4.Lerp(colVec, secondaryColorVec, t);
            }
            else if (secondaryColorDist > 0f && distance >= secondaryColorFadeEnd)
            {
                return secondaryColorVec;
            }
            return colVec;
        }

        for (int i = 0; i < count - 1; i++)
        {
            ARCoordinate outStart = leftPoints[i];
            ARCoordinate inStart  = rightPoints[i];
            ARCoordinate outEnd   = leftPoints[i + 1];
            ARCoordinate inEnd    = rightPoints[i + 1];

            if (AllPointsOutsideRenderDistance(new[] { outStart, inStart, outEnd, inEnd }))
                continue;

            Vector2? ndcOutStart = WorldToNDC(ARCoordinateToVector3(outStart));
            Vector2? ndcInStart  = WorldToNDC(ARCoordinateToVector3(inStart));
            Vector2? ndcInEnd    = WorldToNDC(ARCoordinateToVector3(inEnd));
            Vector2? ndcOutEnd   = WorldToNDC(ARCoordinateToVector3(outEnd));

            if (!ndcOutStart.HasValue || !ndcInStart.HasValue || !ndcInEnd.HasValue || !ndcOutEnd.HasValue)
                continue;

            float distanceStart = Vector3.Distance(ARCoordinateToVector3(outStart), camPos);
            float distanceEnd = Vector3.Distance(ARCoordinateToVector3(outEnd), camPos);

            

            lineWithGradientShader.AddGlowQuad(
                ndcOutStart.Value,
                ndcInStart.Value,
                ndcInEnd.Value,
                ndcOutEnd.Value,
                GetColor(distanceStart),
                GetColor(distanceEnd),
                GetProgressFloat(distanceStart),
                GetProgressFloat(distanceEnd),
                transparentValue
            );
        }
    }

    /// <summary>
    ///  Draw a 3D circle in world space.
    /// </summary>
    /// <param name="center">The center of the circle.</param>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="color">The color of the circle.</param>
    /// <param name="filled">Whether the circle should be filled.</param>
    /// <param name="thickness">The thickness of the circle outline.</param>
    public void Draw3DCircle(ARCoordinate center, float radius, UInt32 color, bool filled = false, float thickness = 1)
    {
        if (AllPointsOutsideRenderDistance(new ARCoordinate[] { center }))
            return;

        Vector2? centerScreen = WorldToScreen(ARCoordinateToVector3(center), thisFrameWidth, thisFrameHeight);
        if (!centerScreen.HasValue) 
            return;

        if (filled) {
            ImGui.GetBackgroundDrawList().AddCircleFilled(
                centerScreen.Value, radius, ConvertColor(color)
            );
        }
        else {
            ImGui.GetBackgroundDrawList().AddCircle(
                centerScreen.Value, radius, ConvertColor(color), thickness
            );
        }
    }

    /// <summary>
    ///  Draw a 3D polygon in world space.
    /// </summary>
    /// <param name="points">List of points to draw, automatically closed.</param>
    /// <param name="color">The color of the polygon.</param>
    /// <param name="filled">Whether the polygon should be filled.</param>
    /// <param name="thickness">The thickness of the polygon outline.</param>
    public void Draw3DPolygon(ARCoordinate[] points, UInt32 color, bool filled = false, float thickness = 1)
    {
        if (points == null || points.Length < 3)
            return;

        if (AllPointsOutsideRenderDistance(points))
            return;

        Vector2[] screenPoints = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            Vector2? screenPos = WorldToScreen(ARCoordinateToVector3(points[i]), thisFrameWidth, thisFrameHeight);
            if (!screenPos.HasValue)
                return;

            screenPoints[i] = screenPos.Value;
        }

        if (filled)
        {
            unsafe
            {
                fixed (Vector2* p = &screenPoints[0])
                {
                    ImGui.GetBackgroundDrawList().AddConvexPolyFilled(p, screenPoints.Length, ConvertColor(color));
                }
            }
        }
        else
        {
            for (int i = 0; i < screenPoints.Length; i++)
            {
                ImGui.GetBackgroundDrawList().AddLine(
                    screenPoints[i], screenPoints[(i + 1) % screenPoints.Length], ConvertColor(color), thickness
                );
            }
        }
    }

    /// <summary>
    ///  Draw a 3D quad in world space.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <param name="p3"></param>
    /// <param name="p4"></param>
    /// <param name="color">Color of the quad.</param>
    /// <param name="filled">Whether this quad should be filled.</param>
    /// <param name="thickness">The thickness of a non filled quad.</param>
    public void Draw3DQuad(ARCoordinate p1, ARCoordinate p2, ARCoordinate p3, ARCoordinate p4, UInt32 color, bool filled = false, float thickness = 1)
    {
        if (AllPointsOutsideRenderDistance(new ARCoordinate[] { p1, p2, p3, p4 }))
            return;
        
        Vector2? p1s = WorldToScreen(ARCoordinateToVector3(p1), thisFrameWidth, thisFrameHeight);
        Vector2? p2s = WorldToScreen(ARCoordinateToVector3(p2), thisFrameWidth, thisFrameHeight);
        Vector2? p3s = WorldToScreen(ARCoordinateToVector3(p3), thisFrameWidth, thisFrameHeight);
        Vector2? p4s = WorldToScreen(ARCoordinateToVector3(p4), thisFrameWidth, thisFrameHeight);

        if (!p1s.HasValue || !p2s.HasValue || !p3s.HasValue || !p4s.HasValue)
            return;

        if (filled)
        {
            ImGui.GetBackgroundDrawList().AddQuadFilled(
                p1s.Value, p2s.Value, p3s.Value, p4s.Value, 
                ConvertColor(color)
            );
        }
        else
        {            
            ImGui.GetBackgroundDrawList().AddQuad(
                p1s.Value, p2s.Value, p3s.Value, p4s.Value, 
                ConvertColor(color), thickness
            );
        }
    }

    /// <summary>
    ///  Draw a triangle in world space.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <param name="p3"></param>
    /// <param name="color">The color of the triangle.</param>
    /// <param name="filled">Whether this triangle should be filled.</param>
    /// <param name="thickness">The thickness of a non filled triangle.</param>
    public void Draw3DTriangle(ARCoordinate p1, ARCoordinate p2, ARCoordinate p3, UInt32 color, bool filled = false, float thickness = 1)
    {
        if (AllPointsOutsideRenderDistance(new ARCoordinate[] { p1, p2, p3 }))
            return;

        Vector2? p1s = WorldToScreen(ARCoordinateToVector3(p1), thisFrameWidth, thisFrameHeight);
        Vector2? p2s = WorldToScreen(ARCoordinateToVector3(p2), thisFrameWidth, thisFrameHeight);
        Vector2? p3s = WorldToScreen(ARCoordinateToVector3(p3), thisFrameWidth, thisFrameHeight);

        if (!p1s.HasValue || !p2s.HasValue || !p3s.HasValue)
            return;

        if (filled)
        {
            ImGui.GetBackgroundDrawList().AddTriangleFilled(
                p1s.Value, p2s.Value, p3s.Value, 
                ConvertColor(color)
            );
        }
        else
        {
            ImGui.GetBackgroundDrawList().AddTriangle(
                p1s.Value, p2s.Value, p3s.Value, 
                ConvertColor(color), thickness
            );
        }
    }

    /// <summary>
    ///  Draw text in world space. The text will be transformed and projected
    ///  onto the AR overlay. The position of the text is based on the top left
    ///  corner of the text's bounding box.
    /// </summary>
    /// <param name="position"></param>
    /// <param name="text"></param>
    /// <param name="color"></param>
    /// <param name="centerX">Whether to center the text horizontally on the position.</param>
    /// <param name="centerY">Whether to center the text vertically on the position.</param>
    public void Draw3DText(ARCoordinate position, string text, UInt32 color, float xFactor = 0f, float yFactor = 0f, UInt32? bgColor = null, UInt32? bgBorderColor = null, float bgBorderThickness = 1f, float bgPaddingX = 0f, float bgPaddingY = 0f, float bgRounding = 0f)
    {
        if (AllPointsOutsideRenderDistance(new ARCoordinate[] { position }))
            return;

        Vector2? screenPos = WorldToScreen(ARCoordinateToVector3(position), thisFrameWidth, thisFrameHeight);
        if (!screenPos.HasValue) return;

        Vector2 textSize = ImGui.CalcTextSize(text);
        Vector2 drawPos = screenPos.Value;
        drawPos.X -= textSize.X * xFactor;
        drawPos.Y -= textSize.Y * yFactor;

        if (bgColor.HasValue)
        {
            ImGui.GetBackgroundDrawList().AddRectFilled(
                new Vector2(drawPos.X - bgPaddingX, drawPos.Y - bgPaddingY),
                new Vector2(drawPos.X + textSize.X + bgPaddingX, drawPos.Y + textSize.Y + bgPaddingY),
                ConvertColor(bgColor.Value),
                bgRounding
            );
        }

        if (bgBorderColor.HasValue)
        {
            ImGui.GetBackgroundDrawList().AddRect(
                new Vector2(drawPos.X - bgPaddingX, drawPos.Y - bgPaddingY),
                new Vector2(drawPos.X + textSize.X + bgPaddingX, drawPos.Y + textSize.Y + bgPaddingY),
                ConvertColor(bgBorderColor.Value),
                bgRounding,
                bgBorderThickness
            );
        }

        ImGui.GetBackgroundDrawList().AddText(
            drawPos, ConvertColor(color), text
        );
    }

    /// <summary>
    ///  Begin rendering an ImGui window in the AR overlay.
    /// </summary>
    /// <param name="id">Id of the window.</param>
    /// <param name="flags">Flags for the window.</param>
    /// <param name="forceWidth">Forced width of the window.</param>
    /// <param name="forceHeight">Forced height of the window.</param>
    /// <param name="bgOpacity">Background opacity of the window.</param>
    public void BeginWindow(
        string id, 
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysAutoResize,
        int forceWidth = 0,
        int forceHeight = 0,
        float bgOpacity = 1.0f
    )
    {
        if (isWindowContextInitialized) {
            Logger.Warn("AR window context already initialized. You probably called BeginWindow without a matching EndWindow.");
            return;
        }

        oldContext = ImGui.GetCurrentContext();
        ImGui.SetCurrentContext(arWindowContext);

        ImGuiImplOpenGL3.NewFrame();
        ImGui.NewFrame();

        ImGui.SetNextWindowBgAlpha(bgOpacity);
        ImGui.SetNextWindowPos(new Vector2(0, 0));
        if (forceWidth > 0 && forceHeight > 0)
            ImGui.SetNextWindowSize(new Vector2(forceWidth, forceHeight));
        else
            ImGui.SetNextWindowSizeConstraints(new Vector2(0, 0), new Vector2(currentWindowBuffer.Width, currentWindowBuffer.Height));

        ImGui.Begin(id, flags);
        isWindowContextInitialized = true;
    }

    /// <summary>
    ///  End the rendering of an ImGui window. This will render the window into a texture
    ///  and then draw that texture onto the AR overlay.
    /// </summary>
    /// <param name="center">Center position of the window in world coordinates.</param>
    /// <param name="rotation">Rotation of the window in world coordinates.</param>
    /// <param name="width">Width of the window in world coordinates.</param>
    public void EndWindow(ARCoordinate center, Quaternion rotation, float width, bool invertY = false)
    {
        var windowSize = ImGui.GetWindowSize();
        var windowPos = ImGui.GetWindowPos(); 

        ImGui.End(); 
        ImGui.Render();

        // Here we clear the FBO and then render ImGui's
        // data into it.
        gl.BindFramebuffer(GLFramebufferTarget.Framebuffer, currentWindowBuffer.Fbo);
        gl.Viewport(0, 0, currentWindowBuffer.Width, currentWindowBuffer.Height);
        gl.ClearColor(0, 0, 0, 0);
        gl.Clear(GLClearBufferMask.ColorBufferBit);

        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
        gl.BindFramebuffer(GLFramebufferTarget.Framebuffer, 0);

        // Back to the main context, as we want to draw the texture
        // onto the main overlay background.
        ImGui.SetCurrentContext(oldContext);

        if (AllPointsOutsideRenderDistance(new ARCoordinate[] { center }))
        {
            isWindowContextInitialized = false;
            return;
        }

        float windowAspectRatio = windowSize.Y / windowSize.X;
        float height = width * windowAspectRatio;

        float halfW = width / 2f;
        float halfH = height / 2f;

        // Mirroring the same quaternion adjustments as in GetViewMatrix.
        // Again, if anyone has a better understanding they can fix this.
        Quaternion invQuat = Quaternion.Conjugate(rotation);
        Vector3 euler = invQuat.ToEuler();
        Quaternion correctedRot = Quaternion.CreateFromYawPitchRoll(
            -euler.Y + (float)Math.PI, 
            -euler.Z + (float)Math.PI, 
            -euler.X
        );

        // Local space
        Vector3 localTL = new Vector3(-halfW,  halfH, 0);
        Vector3 localTR = new Vector3( halfW,  halfH, 0);
        Vector3 localBR = new Vector3( halfW, -halfH, 0);
        Vector3 localBL = new Vector3(-halfW, -halfH, 0);

        // Into world space
        Matrix4x4 modelMatrix = Matrix4x4.CreateFromQuaternion(correctedRot) 
                              * Matrix4x4.CreateTranslation(ARCoordinateToVector3(center));
        
        Vector3 pTL = Vector3.Transform(localTL, modelMatrix);
        Vector3 pTR = Vector3.Transform(localTR, modelMatrix);
        Vector3 pBR = Vector3.Transform(localBR, modelMatrix);
        Vector3 pBL = Vector3.Transform(localBL, modelMatrix);

        // And then projection into screen space
        Vector2? s1 = WorldToScreen(pTL, thisFrameWidth, thisFrameHeight);
        Vector2? s2 = WorldToScreen(pTR, thisFrameWidth, thisFrameHeight);
        Vector2? s3 = WorldToScreen(pBR, thisFrameWidth, thisFrameHeight);
        Vector2? s4 = WorldToScreen(pBL, thisFrameWidth, thisFrameHeight);

        if (s1.HasValue && s2.HasValue && s3.HasValue && s4.HasValue)
        {
            // Top/Bottom has to be 1f- to match properly.
            float uvLeft   = windowPos.X / currentWindowBuffer.Width;
            float uvTop    = 1.0f - ((windowPos.Y + windowSize.Y) / currentWindowBuffer.Height);
            float uvRight  = (windowPos.X + windowSize.X) / currentWindowBuffer.Width;
            float uvBottom = 1.0f - (windowPos.Y / currentWindowBuffer.Height);

            ImTextureID texID = (ImTextureID)currentWindowBuffer.Texture;
            ImTextureRef texRef;
            unsafe { texRef = new ImTextureRef(null, texID); }

            ImGui.GetBackgroundDrawList().AddImageQuad(
                texRef,
                s1.Value, s2.Value, s3.Value, s4.Value,
                invertY ? new Vector2(uvLeft, uvBottom) : new Vector2(uvLeft,  uvTop),
                invertY ? new Vector2(uvRight, uvBottom) : new Vector2(uvRight,  uvTop),
                invertY ? new Vector2(uvRight, uvTop)    : new Vector2(uvRight, uvBottom),
                invertY ? new Vector2(uvLeft, uvTop)      : new Vector2(uvLeft, uvBottom),
                0xFFFFFFFF
            );
        }

        isWindowContextInitialized = false;
    }
}