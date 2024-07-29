using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LookingGlass.Toolkit;

namespace LookingGlass {
    /// <summary>
    /// Represents a sequence of rendering commands that will be mixed together, including hologramCamera realtime 3D renders, quilts, and generic render textures.
    /// </summary>
    [Serializable]
    public class RenderStack : IEnumerable<RenderStep> {
        [SerializeField] private List<RenderStep> steps = new List<RenderStep>();

        private RenderTexture quiltMix;
        private Material alphaBlendMaterial;

        [NonSerialized] private RenderStep defaultStep;

        public event Action onQuiltChanged;
        public event Action onRendered;

        public int Count => steps.Count;
        public RenderStep this[int index] {
            get { return steps[index]; }
        }

        public RenderTexture QuiltMix => quiltMix;

        public void Add(RenderStep step) {
            if (step == null)
                throw new ArgumentNullException(nameof(step));
            steps.Add(step);
        }

        public bool Remove(RenderStep step) => steps.Remove(step);
        public void RemoveAt(int index) => steps.RemoveAt(index);

        public IEnumerator<RenderStep> GetEnumerator() => steps.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<RenderStep>) this).GetEnumerator();

        public void Clear() => steps.Clear();

        public void ResetToDefault() {
            steps.Clear();
            steps.Add(new RenderStep(LookingGlass.RenderStep.Type.CurrentHologramCamera));
        }

        private bool SetupQuiltIfNeeded(HologramCamera hologramCamera) {
            QuiltSettings quiltSettings = hologramCamera.QuiltSettings;
            if (quiltMix == null || quiltMix.width != quiltSettings.quiltWidth || quiltMix.height != quiltSettings.quiltHeight) {
                if (quiltMix != null)
                    quiltMix.Release();
                RenderTexture quilt = hologramCamera.QuiltTexture;
                quiltMix = new RenderTexture(quiltSettings.quiltWidth, quiltSettings.quiltHeight, 0, quilt.format);
                quiltMix.name = "Quilt Mix (" + quiltSettings.quiltWidth + "x" + quiltSettings.quiltHeight + ")";
                quiltMix.filterMode = FilterMode.Point;
                try {
                    onQuiltChanged?.Invoke();
                } catch (Exception e) {
                    Debug.LogException(e);
                }
                return true;
            }
            return false;
        }

        public RenderTexture RenderToQuilt(HologramCamera hologramCamera) {
            try {
                SetupQuiltIfNeeded(hologramCamera);
                MultiViewRendering.Clear(quiltMix, CameraClearFlags.SolidColor, Color.clear);

                if (steps.Count <= 0) {
                    if (defaultStep == null)
                        defaultStep = new RenderStep(LookingGlass.RenderStep.Type.CurrentHologramCamera);
                    RenderStep(defaultStep, hologramCamera, quiltMix);
                } else {
                    if (defaultStep != null)
                        defaultStep = null;

                    foreach (RenderStep step in steps)
                        if (step.IsEnabled)
                            RenderStep(step, hologramCamera, quiltMix);
                }
                try {
                    onRendered?.Invoke();
                } catch (Exception e) {
                    Debug.LogException(e);
                }
                return quiltMix;
            } finally {
                RenderTexture.active = null;
            }
        }

        private void RenderStep(RenderStep step, HologramCamera hologramCamera, RenderTexture mix) {
            QuiltSettings hologramCameraRenderSettings = hologramCamera.QuiltSettings;
            Camera postProcessCamera = step.PostProcessCamera;
            switch (step.RenderType) {
                case LookingGlass.RenderStep.Type.CurrentHologramCamera:
                    if (alphaBlendMaterial == null)
                        alphaBlendMaterial = new Material(Util.FindShader("LookingGlass/Alpha Blend"));

                    if (hologramCamera.Preview2D) {
                        hologramCamera.RenderPreview2D(false, true);
                        MultiViewRendering.CopyViewToAllQuiltTiles(hologramCameraRenderSettings, hologramCamera.Preview2DRT, mix);
                    } else {
                        hologramCamera.RenderQuiltLayer(false, false);
                        Graphics.Blit(hologramCamera.QuiltTexture, mix, alphaBlendMaterial);
                    }
                    break;
                case LookingGlass.RenderStep.Type.Quilt:
                    Texture quilt = step.QuiltTexture;
                    if (quilt != null) {
                        RenderTexture temp = RenderTexture.GetTemporary(quilt.width, quilt.height);
                        Graphics.Blit(quilt, temp);

                        int minViews = Mathf.Min(hologramCameraRenderSettings.tileCount, step.QuiltSettings.tileCount);

                        //TODO: Account for the quilt's renderAspect.
                        //Currently, setting it does nothing.
                        for (int v = 0; v < minViews; v++)
                            MultiViewRendering.CopyViewBetweenQuilts(step.QuiltSettings, v, temp, hologramCameraRenderSettings, v, mix);

                        if (postProcessCamera != null && postProcessCamera.gameObject.activeInHierarchy) {
                            RenderTexture tempDepthTex = CreateTemporaryBlankDepthTexture();
                            MultiViewRendering.RunPostProcess(hologramCamera, mix, tempDepthTex, postProcessCamera);
                            RenderTexture.ReleaseTemporary(tempDepthTex);
                        }
                        RenderTexture.ReleaseTemporary(temp);
                    }
                    break;
                case LookingGlass.RenderStep.Type.GenericTexture:
                    Texture texture = step.Texture;
                    if (texture != null) {
                        bool usedTemporary = false;
                        if (!(texture is RenderTexture renderTex)) {
                            usedTemporary = true;
                            renderTex = RenderTexture.GetTemporary(texture.width, texture.height);
                            Graphics.Blit(texture, renderTex);
                        }
                        MultiViewRendering.CopyViewToAllQuiltTiles(hologramCameraRenderSettings, renderTex, mix);

                        //TODO: Copy depth texture from camera's RenderTexture targetTexture, and use it for post-processing accurately instead of below with the blank depth texture:
                        //if (postProcessCamera != null && postProcessCamera.gameObject.activeInHierarchy) {
                        //    RenderTexture tempDepthTex = CreateTemporaryBlankDepthTexture();
                        //    MultiViewRendering.RunPostProcess(hologramCamera, mix, tempDepthTex, postProcessCamera);
                        //    RenderTexture.ReleaseTemporary(tempDepthTex);
                        //}
                        if (usedTemporary)
                            RenderTexture.ReleaseTemporary(renderTex);
                    }
                    break;
            }
        }

        private RenderTexture CreateTemporaryBlankDepthTexture() {
            RenderTexture depth = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.RFloat);
            MultiViewRendering.Clear(depth, CameraClearFlags.SolidColor, Color.clear);
            return depth;
        }
    }
}
