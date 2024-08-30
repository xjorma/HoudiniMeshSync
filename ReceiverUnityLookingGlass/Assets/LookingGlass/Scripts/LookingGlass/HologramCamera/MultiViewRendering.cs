using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using LookingGlass.Toolkit;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LookingGlass {
    internal static class MultiViewRendering {
        [Serializable]
        private struct ViewInterpolationProperties {
            private bool initialized;
            public bool Initialized => initialized;

            public ShaderPropertyId result;
            public ShaderPropertyId resultDepth;
            public ShaderPropertyId nearClip;
            public ShaderPropertyId farClip;
            public ShaderPropertyId focalDist;
            public ShaderPropertyId perspw;
            public ShaderPropertyId viewSize;
            public ShaderPropertyId viewPositions;
            public ShaderPropertyId viewOffsets;
            public ShaderPropertyId baseViewPositions;
            public ShaderPropertyId spanSize;
            public ShaderPropertyId px;

            public void InitializeAll() {
                initialized = true;

                result = "Result";
                resultDepth = "ResultDepth";
                nearClip = "_NearClip";
                farClip = "_FarClip";
                focalDist = "focalDist";
                perspw = "perspw";
                viewSize = "viewSize";
                viewPositions = "viewPositions";
                viewOffsets = "viewOffsets";
                baseViewPositions = "baseViewPositions";
                spanSize = "spanSize";
                px = "px";

            }
        }
        [Serializable]
        private struct LenticularProperties {
            private bool initialized;
            public bool Initialized => initialized;

            public ShaderPropertyId pitch;
            public ShaderPropertyId slope;
            public ShaderPropertyId center;
            public ShaderPropertyId subpixelSize;
            public ShaderPropertyId screenW;
            public ShaderPropertyId screenH;
            public ShaderPropertyId tileCount;
            public ShaderPropertyId viewPortion;
            public ShaderPropertyId tile;

            public ShaderPropertyId subpixelCellCount;
            public ShaderPropertyId subpixelCells;
            public ShaderPropertyId filterMode;
            public ShaderPropertyId cellPatternType;

            public ShaderPropertyId filterEdge;
            public ShaderPropertyId filterEnd;
            public ShaderPropertyId filterSize;

            public ShaderPropertyId gaussianSigma;
            public ShaderPropertyId edgeThreshold;

            public ShaderPropertyId aspect;             //NOTE: CORRESPONDS TO QuiltSettings.renderAspet (the aspect of the renderer at the time of capture, NOT the tile's actual aspect, which may differ).

            public void InitializeAll() {
                initialized = true;

                pitch = "pitch";
                slope = "slope";
                center = "center";
                subpixelSize = "subpixelSize";
                screenW = "screenW";
                screenH = "screenH";
                tileCount = "tileCount";
                viewPortion = "viewPortion";
                tile = "tile";

                subpixelCellCount = "subpixelCellCount";
                subpixelCells = "subpixelCells";
                filterMode = "filterMode";
                cellPatternType = "cellPatternType";

                filterEdge = "filterEdge";
                filterEnd = "filterEnd";
                filterSize = "filterSize";

                gaussianSigma = "gaussianSigma";
                edgeThreshold = "edgeThreshold";

                aspect = "aspect";
            }
        }

        [Serializable]
        private struct RenderViewSharedData {
            public Camera singleViewCamera;
            public QuiltSettings quiltSettings;
            public float aspect;
            public Matrix4x4 centerViewMatrix;
            public Matrix4x4 centerProjMatrix;
            public float viewConeSweep;
            public float projModifier;
            public bool isNonDefaultDepthiness;
            public Matrix4x4 depthinessMatrix;
            public RenderTexture quilt;
            public RenderTexture quiltRTDepth;
            public bool fallbackCameraTargetTexture;
            public Action<int> onViewRender;
        }

        private static ComputeShader interpolationComputeShader;
        private static ViewInterpolationProperties interpolationProperties;
        private static LenticularProperties lenticularProperties;
        private static Material copyRGB_AMaterial;

        private static void GetClearFlagBooleans(CameraClearFlags clearFlags, out bool clearColor, out bool clearDepth) {
            clearColor = clearFlags == CameraClearFlags.SolidColor;
            clearDepth = clearColor || clearFlags == CameraClearFlags.Depth;
        }

        internal static void ClearBeforeRendering(RenderTexture target, HologramCamera hologramCamera) {
            HologramCameraProperties cameraData = hologramCamera.CameraProperties;
            Clear(target, cameraData.ClearFlags, cameraData.BackgroundColor);
        }

        internal static void Clear(RenderTexture renderTarget, CameraClearFlags clearFlags, Color color) {
            if (renderTarget == null)
                return;

            GetClearFlagBooleans(clearFlags, out bool clearColor, out bool clearDepth);
            if (!clearColor)
                color = Color.clear;

            if (clearDepth || clearColor)
                Clear(renderTarget, clearColor, clearDepth, color);
        }

        private static void Clear(RenderTexture renderTarget, bool clearColor, bool clearDepth, Color color) {
            try {
                RenderTexture.active = renderTarget;
                GL.Clear(
                    clearDepth,
                    clearColor,
                    color,
                    1
                );
            } finally {
                RenderTexture.active = null;
            }
        }

        internal static void RenderQuilt(HologramCamera hologramCamera, bool ignorePostProcessing, Action<int> onViewRender) {
            if (hologramCamera == null || !hologramCamera.Initialized) {
                Debug.LogError("The camera must be non-null and finished initializing before it can render.");
                return;
            }
            hologramCamera.UpdateLenticularMaterial();

            HologramCameraProperties cameraData = hologramCamera.CameraProperties;
            Camera singleViewCamera = hologramCamera.SingleViewCamera;
            Calibration cal = hologramCamera.Calibration;
            float focalPlane = cameraData.FocalPlane;
            float depthiness = cameraData.Depthiness;
            float size = cameraData.Size;
            float viewCone = cal.viewCone;
            //WARNING: This should not be an assertion for now, because it breaks the plugin in a devastating way
            if (viewCone == 0)
                Debug.LogError("The viewCone should be non-zero! When zero, all the single-views may render the same image. cal = " + JsonUtility.ToJson(cal, true));

            RenderViewSharedData data = new RenderViewSharedData() {
                singleViewCamera = singleViewCamera,
                quiltSettings = hologramCamera.QuiltSettings,
                centerViewMatrix = singleViewCamera.worldToCameraMatrix,
                centerProjMatrix = singleViewCamera.projectionMatrix,
                isNonDefaultDepthiness = depthiness != 1,
                quilt = hologramCamera.QuiltTexture,
                fallbackCameraTargetTexture = hologramCamera.Debugging.FallbackCameraTargetTexture,
                onViewRender = onViewRender
            };

            data.aspect = hologramCamera.QuiltSettings.renderAspect; //TODO: Is this quiltAspect or ScreenAspect?
            data.viewConeSweep = (-focalPlane * Mathf.Tan(viewCone * 0.5f * Mathf.Deg2Rad) * 2);
            data.projModifier = 1 / (size * data.aspect); //The projection matrices must be modified in terms of focal plane size
            if (data.viewConeSweep == 0)
                Debug.LogError("The viewConeSweep should be non-zero! When zero, all the single-views may render the same image.");

            int tileCount = data.quiltSettings.tileCount;

            RenderTexture depthQuiltTex = hologramCamera.DepthQuiltTexture;
            if (depthQuiltTex == null) {
                depthQuiltTex = CreateQuiltDepthTexture(data.quilt, false);
                hologramCamera.DepthQuiltTexture = data.quiltRTDepth = depthQuiltTex;
            } else {
                data.quiltRTDepth = depthQuiltTex;
            }

            //NOTE: This is important to do before rendering each frame
            //so that the view interpolation compute shaders work as intended!
            ClearDepthTexture(data.quiltRTDepth);

            if (data.isNonDefaultDepthiness) {
                Matrix4x4 transposeMatrix = Matrix4x4.Translate(new Vector3(0, 0, focalPlane));
                Matrix4x4 scaleMatrix = Matrix4x4.Scale(new Vector3(1, 1, cameraData.Depthiness));
                Matrix4x4 untransposeMatrix = Matrix4x4.Translate(new Vector3(0, 0, -focalPlane));

                data.depthinessMatrix = untransposeMatrix * scaleMatrix * transposeMatrix;
            }

#if UNITY_POST_PROCESSING_STACK_V2
            bool hasPPCamera = false;
            if (!ignorePostProcessing) {
                Camera postProcessCamera = hologramCamera.PostProcessCamera;
                hasPPCamera = postProcessCamera != null;
                if (hasPPCamera) {
                    //WARNING: Camera.CopyFrom(...) MAY ALSO COPY THE TRANSFORM SCALE!!!
                    Vector3 previousScale = postProcessCamera.transform.localScale;
                    postProcessCamera.CopyFrom(data.singleViewCamera);
                    postProcessCamera.transform.localScale = previousScale;
                }
            }
#endif
            //NOTE: This FOV trick is on purpose, to keep shadows from disappearing.

            //We use a large 135° FOV so that lights and shadows DON'T get culled out in our individual single-views!
            //But, this FOV is ignored when we actually render, because we modify the camera matrices.
            //So, we get the best of both worlds -- rendering correctly with no issues with culling.
            if (RenderPipelineUtil.IsBuiltIn)
                data.singleViewCamera.fieldOfView = 135;
            else
                data.singleViewCamera.fieldOfView += 35;
            data.singleViewCamera.aspect = data.aspect;

            HologramViewInterpolation viewInterpolation = hologramCamera.Optimization.ViewInterpolation;
            int onlyShowViewIndex = hologramCamera.Debugging.OnlyShowView;
            if (onlyShowViewIndex > -1) {
                bool copyViewToAllTiles = !hologramCamera.Debugging.OnlyRenderOneView;
                RenderView(onlyShowViewIndex, ref data, copyViewToAllTiles,
                    out RenderTexture viewRT,
                    out RenderTexture viewRTRFloat
                );

                if (copyViewToAllTiles) {
                    for (int i = 0; i < data.quiltSettings.tileCount; i++) {
                        if (i == onlyShowViewIndex)
                            continue;
                        //Instead of re-rendering the single-view so many times, we can just copy it across the quilt way faster!
                        CopyViewToQuilt(data.quiltSettings, i, viewRT, data.quilt);
                        CopyViewToQuilt(data.quiltSettings, i, viewRTRFloat, data.quiltRTDepth);
                    }

                    RenderTexture.ReleaseTemporary(viewRT);
                    RenderTexture.ReleaseTemporary(viewRTRFloat);
                }
            } else {
                for (int i = 0; i < tileCount; i++) {
                    if (viewInterpolation.IsInterpolatedView(i, tileCount))
                        continue;
                    RenderView(i, ref data);
                }
                // onViewRender final pass
                onViewRender?.Invoke(tileCount);
            }

            //Reset stuff back to what they were originally:
            //NOTE: We DON'T call these reset matrix methods, because our "default" matrices are customized
            //in LookingGlass.ResetCameras() to include things like the focalPlane and frustum shifting.
            //data.singleViewCamera.ResetWorldToCameraMatrix();
            //data.singleViewCamera.ResetProjectionMatrix();

            data.singleViewCamera.worldToCameraMatrix = data.centerViewMatrix;
            data.singleViewCamera.projectionMatrix = data.centerProjMatrix;
            data.singleViewCamera.fieldOfView = cameraData.FieldOfView;

            if (viewInterpolation != HologramViewInterpolation.None)
                InterpolateViewsOnQuilt(hologramCamera, data.quilt, data.quiltRTDepth);

#if UNITY_POST_PROCESSING_STACK_V2
            if (hasPPCamera && !ignorePostProcessing) {
#if !UNITY_2018_1_OR_NEWER
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 ||
                    SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12) {
                    FlipRenderTexture(data.quilt);
                }
#endif
                RunPostProcess(hologramCamera, data.quilt, data.quiltRTDepth);
            }
#endif
        }

        private static RenderTexture CreateQuiltDepthTexture(RenderTexture quilt, bool isTemporary) {
            RenderTextureDescriptor depthDescriptor = quilt.descriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.RFloat;
            RenderTexture quiltRTDepth = (isTemporary) ? RenderTexture.GetTemporary(depthDescriptor) : new RenderTexture(depthDescriptor);
            return quiltRTDepth;
        }

        private static void ClearDepthTexture(RenderTexture quiltRTDepth) {
            RenderTexture.active = quiltRTDepth;
            GL.Clear(true, true, Color.black, 1);
            RenderTexture.active = null;
        }

        private static void RenderView(int viewIndex, ref RenderViewSharedData data)
            => RenderView(viewIndex, ref data, false, out _, out _);
        private static void RenderView(int viewIndex, ref RenderViewSharedData data, bool persistViewTextures, out RenderTexture viewRT, out RenderTexture viewRTRFloat) {
            //TODO: Is there a reason we don't notify after the view has **finished** rendering? (below, at the bottom of this for loop block)
            data.onViewRender?.Invoke(viewIndex);

            viewRT = RenderTexture.GetTemporary(data.quiltSettings.TileWidth, data.quiltSettings.TileHeight, 24, data.quilt.format);
            
            //IMPORTANT: The single-view camera MUST clear before each render
            //TODO: Not sure how to fix Depthiness != 1 case of the background not clearing properly with Skybox clear flags...
            try {
                RenderTexture.active = viewRT;
                GL.Clear(true, true, Color.clear, 1);
            } finally {
                RenderTexture.active = null;
            }

            RenderTexture viewRTDepth = null;

            //TODO: [CRT-3174] Look into how to SetTargetBuffers (BOTH color + depth) in URP like we do in built-in below:
            //Looking into the following:
            //      - https://docs.unity3d.com/ScriptReference/Camera.SetTargetBuffers.html
            //      - https://forum.unity.com/threads/urp-is-camera-settargetbuffers-supposed-to-work-in-urp.1289651/
            //      - https://forum.unity.com/threads/how-to-access-the-camera-depth-texture-on-urp.1338239/
            //      - https://forum.unity.com/threads/urp-settargetbuffers-does-not-work-in-2020-2-1-and-2021-1-0b1.1029760/
            if (RenderPipelineUtil.IsBuiltIn) {
                viewRTDepth = RenderTexture.GetTemporary(data.quiltSettings.TileWidth, data.quiltSettings.TileHeight, 24, RenderTextureFormat.Depth);
                try {
                    RenderTexture.active = viewRTDepth;
                    GL.Clear(true, true, Color.clear, 1);
                } finally {
                    RenderTexture.active = null;
                }
            }

            if (data.fallbackCameraTargetTexture || !RenderPipelineUtil.IsBuiltIn) {
                data.singleViewCamera.targetTexture = viewRT;
            } else {
                data.singleViewCamera.SetTargetBuffers(viewRT.colorBuffer, viewRTDepth.depthBuffer);
            }

            Matrix4x4 viewMatrix = data.centerViewMatrix;
            Matrix4x4 projMatrix = data.centerProjMatrix;

            float currentViewLerp = 0; // if numviews is 1, take center view
            int tileCount = data.quiltSettings.tileCount;
            if (tileCount > 1)
                currentViewLerp = (float) viewIndex / (tileCount - 1) - 0.5f;

            //NOTE:
            //m03 is x shift        (m03 is 1st row, 4th column)
            //m13 is y shift        (m13 is 2st row, 4th column)
            //m23 is z shift        (m23 is 3rd row, 4th column)
            viewMatrix.m03 += currentViewLerp * data.viewConeSweep;

            projMatrix.m02 += currentViewLerp * data.viewConeSweep * data.projModifier;
            data.singleViewCamera.worldToCameraMatrix = viewMatrix;
            data.singleViewCamera.projectionMatrix = (data.isNonDefaultDepthiness) ? projMatrix * data.depthinessMatrix : projMatrix;

            data.singleViewCamera.Render();

            CopyViewToQuilt(data.quiltSettings, viewIndex, viewRT, data.quilt);
            data.singleViewCamera.targetTexture = null;

            switch (RenderPipelineUtil.GetRenderPipelineType()) {
                case RenderPipelineType.BuiltIn:
                    // gotta create a weird new viewRT now
                    RenderTextureDescriptor viewRTRFloatDesc = viewRT.descriptor;
                    viewRTRFloatDesc.colorFormat = RenderTextureFormat.RFloat;
                    viewRTRFloat = RenderTexture.GetTemporary(viewRTRFloatDesc);
                    Graphics.Blit(viewRTDepth, viewRTRFloat);
                    RenderTexture.ReleaseTemporary(viewRTDepth);

                    CopyViewToQuilt(data.quiltSettings, viewIndex, viewRTRFloat, data.quiltRTDepth);

                    if (!persistViewTextures) {
                        RenderTexture.ReleaseTemporary(viewRT);
                        RenderTexture.ReleaseTemporary(viewRTRFloat);
                        viewRT = null;
                        viewRTRFloat = null;
                    }

                    //NOTE: This helps 3D cursor ReadPixels faster
                    GL.Flush();
                    break;
                default:
                    RenderTexture.ReleaseTemporary(viewRT);
                    viewRT = null;
                    viewRTRFloat = null;
                    break;
            }
        }

        private static Rect GetViewRect(QuiltSettings quiltSettings, int viewIndex) {
            int reversedViewIndex = quiltSettings.columns * quiltSettings.rows - viewIndex - 1;

            int targetX = (viewIndex % quiltSettings.columns) * quiltSettings.TileWidth;
            int targetY = (reversedViewIndex / quiltSettings.columns) * quiltSettings.TileHeight + quiltSettings.PaddingVertical; //NOTE: Reversed here because Y is taken from the top

            return new Rect(targetX, targetY, quiltSettings.TileWidth, quiltSettings.TileHeight);
        }

        /// <summary>
        /// <para>Copies <paramref name="view"/> to every tile in the given <paramref name="quilt"/> texture.</para>
        /// <para>This is useful for copying a 2D view to a quilt, so they can render together with the lenticular shader.</para>
        /// </summary>
        /// <param name="quiltSettings">The quilt settings that correspond to <paramref name="quilt"/>.</param>
        /// <param name="view">The view that will be copied to every tile of the <paramref name="quilt"/>.</param>
        /// <param name="quilt">The target texture that will have the <paramref name="view"/> copied over all of its tiles.</param>
        public static void CopyViewToAllQuiltTiles(QuiltSettings quiltSettings, Texture view, RenderTexture quilt) {
            for (int v = 0; v < quiltSettings.tileCount; v++)
                MultiViewRendering.CopyViewToQuilt(quiltSettings, v, view, quilt);
        }

        /// <summary>
        /// Copies an entire texture into the single-view tile of a quilt.
        /// </summary>
        public static void CopyViewToQuilt(QuiltSettings quiltSettings, int viewIndex, Texture view, RenderTexture quilt) {
            //NOTE: not using Graphics.CopyTexture(...) because it's an exact per-pixel copy (100% overwrite, no alpha-blending support).
            Rect viewRect = GetViewRect(quiltSettings, viewIndex);

            Graphics.SetRenderTarget(quilt);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, quiltSettings.quiltWidth, quiltSettings.quiltHeight, 0);
            Graphics.DrawTexture(viewRect, view);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
        }

        /// <summary>
        /// Copies a single-view from one quilt to another quilt.
        /// </summary>
        public static void CopyViewBetweenQuilts(QuiltSettings fromRenderSettings, int fromView, RenderTexture fromQuilt,
            QuiltSettings toRenderSettings, int toView, RenderTexture toQuilt) {

            Rect fromRect = GetViewRect(fromRenderSettings, fromView);
            Rect toRect = GetViewRect(toRenderSettings, toView);

            //NOTE: I'm not sure why we have to manually flip our coordinates, I'm hoping this is expected by Unity when (SystemInfo.graphicsUVStartsAtTop == true)
            //Without this, our quilts were copying in reverse!
            if (SystemInfo.graphicsUVStartsAtTop)
                fromRect.y = (fromQuilt.height - fromRenderSettings.TileHeight) - fromRect.y;

            Rect normalizedFromRect = new Rect(
                fromRect.x / fromQuilt.width,
                fromRect.y / fromQuilt.height,
                fromRect.width / fromQuilt.width,
                fromRect.height / fromQuilt.height
            );

            Graphics.SetRenderTarget(toQuilt);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, toRenderSettings.quiltWidth, toRenderSettings.quiltHeight, 0);
            Graphics.DrawTexture(toRect, fromQuilt, normalizedFromRect, 0, 0, 0, 0);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
        }

        /// <summary>
        /// Copies a single-view from one quilt to the <paramref name="destination"/> texture.
        /// </summary>
        public static void CopyViewFromQuilt(QuiltSettings fromRenderSettings, int fromView, RenderTexture fromQuilt, RenderTexture destination) {
            if (fromQuilt == null)
                throw new ArgumentNullException(nameof(fromQuilt));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            Rect fromRect = GetViewRect(fromRenderSettings, fromView);
            Rect toRect = new Rect(0, 0, destination.width, destination.height);

            //NOTE: I'm not sure why we have to manually flip our coordinates, I'm hoping this is expected by Unity when (SystemInfo.graphicsUVStartsAtTop == true)
            //Without this, our quilts were copying in reverse!
            if (SystemInfo.graphicsUVStartsAtTop)
                fromRect.y = (fromQuilt.height - fromRenderSettings.TileHeight) - fromRect.y;

            Rect normalizedFromRect = new Rect(
                fromRect.x / fromQuilt.width,
                fromRect.y / fromQuilt.height,
                fromRect.width / fromQuilt.width,
                fromRect.height / fromQuilt.height
            );

            Graphics.SetRenderTarget(destination);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, destination.width, destination.height, 0);
            Graphics.DrawTexture(toRect, fromQuilt, normalizedFromRect, 0, 0, 0, 0);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
        }

        /// <summary>
        /// Applies post-processing effects to the <paramref name="target"/> texture.<br />
        /// Note that this method does NOT draw anything to the screen. It only writes into the <paramref name="target"/> render texture.
        /// </summary>
        /// <param name="hologramCamera">The instance associated with the post-processing. Its post-processing camera is used if <paramref name="postProcessCamera"/> is <c>null</c>.</param>
        /// <param name="target">The render texture to apply post-processing into.</param>
        /// <param name="depthTexture">The depth texture to use for post-processing effects. This is useful, because you can provide a custom depth texture instead of always using a single <see cref="HologramCamera"/>'s depth texture.</param>
        /// <param name="postProcessCamera">A custom post-processing camera, if any. When set to <c>null</c>, the <paramref name="hologramCamera"/>'s built-in post-processing camera is used.</param>
        public static void RunPostProcess(HologramCamera hologramCamera, RenderTexture target, Texture depthTexture, Camera postProcessCamera = null) {
            RenderTexture previousAlpha = null;
            RenderTexture rgbaMix = null;

            bool preserveAlpha = true;
            if (preserveAlpha) {
                previousAlpha = RenderTexture.GetTemporary(target.width, target.height);
                rgbaMix = RenderTexture.GetTemporary(target.width, target.height);
                Graphics.Blit(target, previousAlpha);
            }

            if (postProcessCamera == null)
                postProcessCamera = hologramCamera.PostProcessCamera;
            postProcessCamera.cullingMask = 0;
            postProcessCamera.clearFlags = CameraClearFlags.Nothing;
            postProcessCamera.targetTexture = target;

            Shader.SetGlobalTexture("_FAKEDepthTexture", depthTexture);
            postProcessCamera.Render();

            if (preserveAlpha) {
                if (copyRGB_AMaterial == null)
                    copyRGB_AMaterial = new Material(Util.FindShader("LookingGlass/Copy RGB-A"));
                copyRGB_AMaterial.SetTexture("_ColorTex", target);
                copyRGB_AMaterial.SetTexture("_AlphaTex", previousAlpha);
                Graphics.Blit(null, rgbaMix, copyRGB_AMaterial);
                Graphics.Blit(rgbaMix, target);
                RenderTexture.ReleaseTemporary(previousAlpha);
                RenderTexture.ReleaseTemporary(rgbaMix);
            }
        }

        internal static RenderTexture RenderPreview2D(HologramCamera hologramCamera, bool ignorePostProcessing = false) {
            if (hologramCamera == null || !hologramCamera.Initialized)
                throw new ArgumentException("The camera must be finished initializing before it can render.", nameof(hologramCamera));
            Profiler.BeginSample(nameof(RenderPreview2D), hologramCamera);
            try {
                Profiler.BeginSample("Create " + nameof(RenderTexture) + "s", hologramCamera);
                Calibration cal = hologramCamera.Calibration;
                int width = cal.screenW;
                int height = cal.screenH;
                RenderTexture preview2DRT = hologramCamera.Preview2DRT;
                Camera singleViewCamera = hologramCamera.SingleViewCamera;
                Camera postProcessCamera = hologramCamera.PostProcessCamera;

                if (preview2DRT == null
                    || preview2DRT.width != width
                    || preview2DRT.height != height) {

                    if (preview2DRT != null)
                        preview2DRT.Release();
                    preview2DRT = new RenderTexture(width, height, 24);
                    preview2DRT.name = "LookingGlass Preview 2D";
                }
                Profiler.EndSample();

                Profiler.BeginSample("Rendering", hologramCamera);
                ClearBeforeRendering(preview2DRT, hologramCamera);

                RenderTexture depth = null;
                try {
                    GetClearFlagBooleans(hologramCamera.CameraProperties.ClearFlags, out bool clearColor, out bool clearDepth);

                    if (RenderPipelineUtil.IsBuiltIn) {
                        depth = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.Depth);
                        if (clearDepth)
                            Clear(depth, clearColor, clearDepth, Color.black);
                    }

                    if (hologramCamera.Debugging.FallbackCameraTargetTexture || !RenderPipelineUtil.IsBuiltIn) {
                        singleViewCamera.targetTexture = preview2DRT;
                    } else {
                        singleViewCamera.SetTargetBuffers(preview2DRT.colorBuffer, depth.depthBuffer);
                    }

                    singleViewCamera.Render();

#if UNITY_POST_PROCESSING_STACK_V2
                    bool hasPPCam = postProcessCamera != null;
                    if (hasPPCam && !ignorePostProcessing) {
                        postProcessCamera.CopyFrom(singleViewCamera);
#if !UNITY_2018_1_OR_NEWER
                        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 ||
                            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12)
                            FlipRenderTexture(preview2DRT);
#endif
                        RunPostProcess(hologramCamera, preview2DRT, depth);
                    }
#endif
                } finally {
                    if (depth != null)
                        RenderTexture.ReleaseTemporary(depth);
                    Profiler.EndSample();
                }
                return preview2DRT;
            } finally {
                Profiler.EndSample();
            }
        }

        public static void FlipRenderTexture(RenderTexture texture) {
            RenderTexture rtTemp = RenderTexture.GetTemporary(texture.descriptor);
            rtTemp.Create();
            Graphics.CopyTexture(texture, rtTemp);
            Graphics.SetRenderTarget(texture);
            Rect rtRect = new Rect(0, 0, texture.width, texture.height);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, rtRect.width, 0, rtRect.height);
            Graphics.DrawTexture(rtRect, rtTemp);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
            RenderTexture.ReleaseTemporary(rtTemp);
        }

        public static void FlipGenericRenderTexture(RenderTexture texture) {
            RenderTexture rtTemp = RenderTexture.GetTemporary(texture.descriptor);
            rtTemp.Create();
            Graphics.CopyTexture(texture, rtTemp);
            Graphics.SetRenderTarget(texture);
            Rect rtRect = new Rect(0, 0, texture.width, texture.height);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, texture.width, 0, texture.height);
            Graphics.DrawTexture(rtRect, rtTemp);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
            RenderTexture.ReleaseTemporary(rtTemp);
        }

        public static void InterpolateViewsOnQuilt(HologramCamera hologramCamera, RenderTexture quilt, RenderTexture quiltRTDepth) {
            if (interpolationComputeShader == null)
                interpolationComputeShader = Resources.Load<ComputeShader>("ViewInterpolation");
            if (interpolationComputeShader == null) {
                Debug.Log(nameof(interpolationComputeShader) + " failed to load from Resources! Unable to interpolate views on quilt.");
                return;
            }

            if (!interpolationProperties.Initialized)
                interpolationProperties.InitializeAll();

            Calibration cal = hologramCamera.Calibration;
            Camera singleViewCamera = hologramCamera.SingleViewCamera;
            HologramCameraProperties cameraData = hologramCamera.CameraProperties;
            QuiltSettings quiltSettings = hologramCamera.QuiltSettings;
            OptimizationProperties optimization = hologramCamera.Optimization;
            int viewInterpolation = optimization.ViewInterpolation.GetViewIndexDivisor(quiltSettings.tileCount);

            int kernelFwd = interpolationComputeShader.FindKernel("QuiltInterpolationForward");
            int kernelBack = optimization.BlendViews ?
                interpolationComputeShader.FindKernel("QuiltInterpolationBackBlend") :
                interpolationComputeShader.FindKernel("QuiltInterpolationBack");
            int kernelFwdFlicker = interpolationComputeShader.FindKernel("QuiltInterpolationForwardFlicker");
            int kernelBackFlicker = optimization.BlendViews ?
                interpolationComputeShader.FindKernel("QuiltInterpolationBackBlendFlicker") :
                interpolationComputeShader.FindKernel("QuiltInterpolationBackFlicker");

            interpolationComputeShader.SetTexture(kernelFwd, interpolationProperties.result, quilt);
            interpolationComputeShader.SetTexture(kernelFwd, interpolationProperties.resultDepth, quiltRTDepth);
            interpolationComputeShader.SetTexture(kernelBack, interpolationProperties.result, quilt);
            interpolationComputeShader.SetTexture(kernelBack, interpolationProperties.resultDepth, quiltRTDepth);
            interpolationComputeShader.SetTexture(kernelFwdFlicker, interpolationProperties.result, quilt);
            interpolationComputeShader.SetTexture(kernelFwdFlicker, interpolationProperties.resultDepth, quiltRTDepth);
            interpolationComputeShader.SetTexture(kernelBackFlicker, interpolationProperties.result, quilt);
            interpolationComputeShader.SetTexture(kernelBackFlicker, interpolationProperties.resultDepth, quiltRTDepth);
            interpolationComputeShader.SetFloat(interpolationProperties.nearClip, singleViewCamera.nearClipPlane);
            interpolationComputeShader.SetFloat(interpolationProperties.farClip, singleViewCamera.farClipPlane);
            interpolationComputeShader.SetFloat(interpolationProperties.focalDist, hologramCamera.CameraProperties.FocalPlane);

            //Used for perspective w component:
            float aspectCorrectedFOV = Mathf.Atan(cal.ScreenAspect * Mathf.Tan(0.5f * cameraData.FieldOfView * Mathf.Deg2Rad));
            interpolationComputeShader.SetFloat(interpolationProperties.perspw, 2 * Mathf.Tan(aspectCorrectedFOV));
            interpolationComputeShader.SetVector(interpolationProperties.viewSize, new Vector4(
                quiltSettings.TileWidth,
                quiltSettings.TileHeight,
                1f / quiltSettings.TileWidth,
                1f / quiltSettings.TileHeight
            ));

            List<int> viewPositions = new List<int>();
            List<float> viewOffsets = new List<float>();
            List<int> baseViewPositions = new List<int>();
            int validViewIndex = -1;
            int currentInterp = 1;
            int tileCount = quiltSettings.tileCount;
            for (int i = 0; i < tileCount; i++) {
                int[] positions = {
                    i % quiltSettings.columns * quiltSettings.TileWidth,
                    i / quiltSettings.columns * quiltSettings.TileHeight,
                };
                if (i != 0 && i != tileCount - 1 && i % viewInterpolation != 0) {
                    viewPositions.AddRange(positions);
                    viewPositions.AddRange(new[] { validViewIndex, validViewIndex + 1 });
                    int div = Mathf.Min(viewInterpolation, tileCount - 1);
                    int divTotal = tileCount / div;
                    if (i > divTotal * viewInterpolation) {
                        div = tileCount - divTotal * viewInterpolation;
                    }

                    float viewCone = cal.viewCone;
                    float offset = div * Mathf.Tan(viewCone * Mathf.Deg2Rad) / (tileCount - 1f);
                    float lerp = (float) currentInterp / div;
                    currentInterp++;
                    viewOffsets.AddRange(new[] { offset, lerp });
                } else {
                    baseViewPositions.AddRange(positions);
                    validViewIndex++;
                    currentInterp = 1;
                }
            }

            int viewCount = viewPositions.Count / 4;
            ComputeBuffer viewPositionsBuffer = new ComputeBuffer(viewPositions.Count / 4, 4 * sizeof(int));
            ComputeBuffer viewOffsetsBuffer = new ComputeBuffer(viewOffsets.Count / 2, 2 * sizeof(float));
            ComputeBuffer baseViewPositionsBuffer = new ComputeBuffer(baseViewPositions.Count / 2, 2 * sizeof(int));
            viewPositionsBuffer.SetData(viewPositions);
            viewOffsetsBuffer.SetData(viewOffsets);
            baseViewPositionsBuffer.SetData(baseViewPositions);

            interpolationComputeShader.SetBuffer(kernelFwd, interpolationProperties.viewPositions, viewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelFwd, interpolationProperties.viewOffsets, viewOffsetsBuffer);
            interpolationComputeShader.SetBuffer(kernelFwd, interpolationProperties.baseViewPositions, baseViewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelBack, interpolationProperties.viewPositions, viewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelBack, interpolationProperties.viewOffsets, viewOffsetsBuffer);
            interpolationComputeShader.SetBuffer(kernelBack, interpolationProperties.baseViewPositions, baseViewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelFwdFlicker, interpolationProperties.viewPositions, viewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelFwdFlicker, interpolationProperties.viewOffsets, viewOffsetsBuffer);
            interpolationComputeShader.SetBuffer(kernelFwdFlicker, interpolationProperties.baseViewPositions, baseViewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelBackFlicker, interpolationProperties.viewPositions, viewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelBackFlicker, interpolationProperties.viewOffsets, viewOffsetsBuffer);
            interpolationComputeShader.SetBuffer(kernelBackFlicker, interpolationProperties.baseViewPositions, baseViewPositionsBuffer);

            uint blockX, blockY, blockZ;
            interpolationComputeShader.GetKernelThreadGroupSizes(kernelFwd, out blockX, out blockY, out blockZ);
            int computeX = quiltSettings.TileWidth / (int) blockX + Mathf.Min(quiltSettings.TileWidth % (int) blockX, 1);
            int computeY = quiltSettings.TileHeight / (int) blockY + Mathf.Min(quiltSettings.TileHeight % (int) blockY, 1);
            int computeZ = viewCount / (int) blockZ + Mathf.Min(viewCount % (int) blockZ, 1);

            if (optimization.ReduceFlicker) {
                int spanSize = 2 * viewInterpolation;
                interpolationComputeShader.SetInt(interpolationProperties.spanSize, spanSize);
                for (int i = 0; i < spanSize; i++) {
                    interpolationComputeShader.SetInt(interpolationProperties.px, i);
                    interpolationComputeShader.Dispatch(kernelFwd, quiltSettings.TileWidth / spanSize, computeY, computeZ);
                    interpolationComputeShader.Dispatch(kernelBack, quiltSettings.TileWidth / spanSize, computeY, computeZ);
                }
            } else {
                interpolationComputeShader.Dispatch(kernelFwdFlicker, computeX, computeY, computeZ);
                interpolationComputeShader.Dispatch(kernelBackFlicker, computeX, computeY, computeZ);
            }

            if (optimization.FillGaps) {
                var fillgapsKernel = interpolationComputeShader.FindKernel("FillGaps");
                interpolationComputeShader.SetTexture(fillgapsKernel, interpolationProperties.result, quilt);
                interpolationComputeShader.SetTexture(fillgapsKernel, interpolationProperties.resultDepth, quiltRTDepth);
                interpolationComputeShader.SetBuffer(fillgapsKernel, interpolationProperties.viewPositions, viewPositionsBuffer);
                interpolationComputeShader.Dispatch(fillgapsKernel, computeX, computeY, computeZ);
            }

            viewPositionsBuffer.Dispose();
            viewOffsetsBuffer.Dispose();
            baseViewPositionsBuffer.Dispose();
        }
        public static void SetLenticularMaterialSettings(HologramCamera hologramCamera, Material lenticularMaterial) {
            if (lenticularMaterial == null)
                throw new ArgumentNullException(nameof(lenticularMaterial));

            if (!lenticularProperties.Initialized)
                lenticularProperties.InitializeAll();

            Calibration cal = hologramCamera.Calibration;
            QuiltSettings quiltSettings = hologramCamera.QuiltSettings;
            ScreenRect lenticularRegion = hologramCamera.LenticularRegion;
            float aspect = hologramCamera.QuiltSettings.renderAspect;
            float tileCount = quiltSettings.columns * quiltSettings.rows;
            int screenW = lenticularRegion.Width;
            int screenH = lenticularRegion.Height;

            lenticularMaterial.SetFloat(lenticularProperties.screenW, screenW);
            lenticularMaterial.SetFloat(lenticularProperties.screenH, screenH);
            lenticularMaterial.SetFloat(lenticularProperties.tileCount, tileCount);
            lenticularMaterial.SetFloat(lenticularProperties.pitch, Calibration.ProcessPitch(lenticularRegion.Width, cal));

            lenticularMaterial.SetFloat(lenticularProperties.slope, Calibration.ProcessSlope(lenticularRegion.Width, lenticularRegion.Height, cal));
            lenticularMaterial.SetFloat(lenticularProperties.center, cal.center
#if UNITY_EDITOR
                + hologramCamera.CameraProperties.CenterOffset
#endif
            );
            lenticularMaterial.SetFloat(lenticularProperties.subpixelSize, (float) 1 / (3 * screenW) * (cal.flipImageX >= 0.5f ? -1 : 1));

            lenticularMaterial.SetVector(lenticularProperties.tile, new Vector4(
                quiltSettings.columns,
                quiltSettings.rows,
                quiltSettings.tileCount,
                tileCount
            ));
            lenticularMaterial.SetVector(lenticularProperties.viewPortion, new Vector4(
                quiltSettings.ViewPortionHorizontal,
                quiltSettings.ViewPortionVertical
            ));

            lenticularMaterial.SetVector(lenticularProperties.aspect, new Vector4(
                aspect,
                aspect
            ));

            // Only dim the edge views for 32 and 65 inch when NOT using Preview2D
            LKGDeviceType deviceType = cal.GetDeviceType();
            bool shouldDimEdgeViews =
                (deviceType == LKGDeviceType._32inGen2 || deviceType == LKGDeviceType._65inLandscapeGen2) &&
                !hologramCamera.Preview2D;

            lenticularMaterial.SetInt(lenticularProperties.filterMode, 1);
            lenticularMaterial.SetInt(lenticularProperties.cellPatternType, cal.cellPatternMode);

            lenticularMaterial.SetInt(lenticularProperties.filterEdge, shouldDimEdgeViews ? 1 : 0);
            lenticularMaterial.SetFloat(lenticularProperties.filterEnd, 0.05f);
            lenticularMaterial.SetFloat(lenticularProperties.filterSize, 0.15f);
            lenticularMaterial.SetFloat(lenticularProperties.gaussianSigma, 0.01f);
            lenticularMaterial.SetFloat(lenticularProperties.edgeThreshold, 0.01f);

            int subpixelCellCount = cal.subpixelCells?.Length ?? 0;
            ComputeBuffer subpixelCellBuffer = hologramCamera.subpixelCellBuffer;
            SubpixelCell[] normalizedSubpixelCells = hologramCamera.normalizedSubpixelCells;
            try {
                lenticularMaterial.SetInt(lenticularProperties.subpixelCellCount, subpixelCellCount);
                if (subpixelCellBuffer != null) {
                    if (subpixelCellBuffer.count != subpixelCellCount) {
                        subpixelCellBuffer.Dispose();
                        subpixelCellBuffer = null;
                    }
                }
                bool needsRecreation = subpixelCellBuffer == null && subpixelCellCount > 0;
                if (needsRecreation)
                    subpixelCellBuffer = new ComputeBuffer(cal.subpixelCells.Length, SubpixelCell.Stride, ComputeBufferType.Structured);

                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal) {
                    //NOTE: On Metal/MacOS, you'll get this error if you try to SetBuffer(...) with null (or don't SetBuffer(...) at all):
                    //  Metal: Vertex or Fragment Shader "LookingGlass/Lenticular" requires a ComputeBuffer at index 1 to be bound, but none provided. Skipping draw calls to avoid crashing.

                    //Also NOTE:
                    //  Trying to create a ComputeBuffer with 0 length will result in an ArgumentException, so we'll put dummy data in:
                    if (subpixelCellBuffer == null)
                        subpixelCellBuffer = new ComputeBuffer(Calibration.MaxSubpixelPatterns, SubpixelCell.Stride, ComputeBufferType.Structured);
                }

                if (subpixelCellCount > 0) {
                    if (normalizedSubpixelCells == null || normalizedSubpixelCells.Length != cal.subpixelCells.Length) {
                        normalizedSubpixelCells = new SubpixelCell[cal.subpixelCells.Length];
                    }
                    for (int i = 0; i < cal.subpixelCells.Length; i++) {
                        normalizedSubpixelCells[i] = cal.subpixelCells[i];
                        normalizedSubpixelCells[i].Normalize(screenW, screenH);
                    }
                    subpixelCellBuffer.SetData(normalizedSubpixelCells);
                    SubpixelCell[] retrieved = new SubpixelCell[subpixelCellBuffer.count];
                    subpixelCellBuffer.GetData(retrieved);
                } else {
                    normalizedSubpixelCells = null;
                }

                lenticularMaterial.SetBuffer(lenticularProperties.subpixelCells, subpixelCellBuffer);
            } finally {
                hologramCamera.subpixelCellBuffer = subpixelCellBuffer;
                hologramCamera.normalizedSubpixelCells = normalizedSubpixelCells;
            }
        }
    }
}
