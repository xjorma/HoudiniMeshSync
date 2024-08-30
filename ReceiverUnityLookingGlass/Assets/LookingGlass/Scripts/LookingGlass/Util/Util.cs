﻿using System;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LookingGlass {
    internal static class Util {
        private static Texture2D opaqueBlackTexture;

        public static Texture2D OpaqueBlackTexture {
            get {
                if (opaqueBlackTexture == null) {
                    opaqueBlackTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    Color32[] blackPixels = new Color32[16];
                    for (int i = 0; i < blackPixels.Length; i++)
                        blackPixels[i] = new Color32(0, 0, 0, 255);
                    opaqueBlackTexture.SetPixels32(blackPixels);
                    opaqueBlackTexture.Apply();

#if UNITY_EDITOR
                    AssemblyReloadEvents.beforeAssemblyReload += DestroyBlackTexture;
#endif
                }
                return opaqueBlackTexture;
            }
        }

#if UNITY_EDITOR
        private static bool alreadyAttemptedReimport = false;

        private static void DestroyBlackTexture() {
            if (opaqueBlackTexture != null)
                Texture2D.DestroyImmediate(opaqueBlackTexture);
        }
#endif

        //NOTE: This is a more robust replacement for calling Shader.Find(string), that detects when Unity accidentally returns null.
        //This may occur when downgrading the Unity project, and perhaps in other unidentified scenarios.
        //Instead of leaving the developer to be scratching their head why the Lenticular shader is null, we just reimport the Resources folder that contains it and automatically re-reference it.
        //This speeds up our workflow!
        public static Shader FindShader(string name) {
            Shader shader = Shader.Find(name);

#if UNITY_EDITOR
            if (shader == null && !alreadyAttemptedReimport) {
                alreadyAttemptedReimport = true;
                try {
                    string resourcesFolderGuid = "4d49f158eb8fe48a89f792f8dd9c09af";
                    Debug.Log("Forcing reimport of the resources folder (GUID = " + resourcesFolderGuid + ") because " + nameof(Shader) + "." + nameof(Shader.Find) + "(string) was returning null.");
                    string resourcesFolderPath = AssetDatabase.GUIDToAssetPath(resourcesFolderGuid);
                    AssetDatabase.ImportAsset(resourcesFolderPath, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);

                    shader = Shader.Find(name);
                } catch (Exception e) {
                    Debug.LogException(e);
                    return null;
                }
            }
#endif

            return shader;
        }

        /// <summary>
        /// Copies the <paramref name="source"/> texture to the CPU and encodes it to PNG bytes.<br />
        /// This is the minimum amount of work that requires the Unity main thread.
        /// </summary>
        internal static void EncodeToPNGBytes(RenderTexture source, out Texture2D cpuTexture, out byte[] bytes) {
            cpuTexture = ReadTextureToCPU(source);
            bytes = cpuTexture.EncodeToPNG();
        }

        internal static Texture2D SaveAsPNGScreenshotAt(RenderTexture source, string filePath) {
            EncodeToPNGBytes(source, out Texture2D cpuTexture, out byte[] bytes);
            
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllBytes(filePath, bytes);

            Debug.Log("Took screenshot to:    " + filePath + "!");
            return cpuTexture;
        }

        internal static Texture2D ReadTextureToCPU(RenderTexture source) {
            Texture2D result = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);

            try {
                RenderTexture.active = source;
                result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                result.Apply();
            } finally {
                RenderTexture.active = null;
            }

            return result;
        }
    }
}