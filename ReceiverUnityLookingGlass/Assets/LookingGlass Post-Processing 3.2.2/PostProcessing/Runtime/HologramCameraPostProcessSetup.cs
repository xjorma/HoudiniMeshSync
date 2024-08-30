using System;
using System.Collections.Generic;
using UnityEngine;
using LookingGlass;

#if UNITY_EDITOR
using UnityEditor;
#endif

//TODO: Possibly use a custom namespace for this custom code?
namespace UnityEngine.Rendering.PostProcessing {
    /// <summary>
    /// Contains extensions to <see cref="HologramCamera"/> to support post-processing, by implementing callbacks during onto OnEnable and OnDisable.
    /// </summary>
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    internal static class HologramCameraPostProcessSetup {
#if UNITY_EDITOR
        static HologramCameraPostProcessSetup() {
            RegisterCallbacks();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterCallbacks() {
#if UNITY_POST_PROCESSING_STACK_V2
            HologramCamera.UsePostProcessing = DetermineIfShouldUsePostProcessing;
#endif
        }

        private static bool DetermineIfShouldUsePostProcessing(HologramCamera hologramCamera) =>
            hologramCamera.TryGetComponent(out PostProcessLayer layer) && layer.enabled;
    }
}
