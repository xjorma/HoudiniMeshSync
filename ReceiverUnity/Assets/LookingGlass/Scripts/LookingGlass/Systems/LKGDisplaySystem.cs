using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using LookingGlass.Toolkit;
using LookingGlass.Toolkit.Bridge;
using LookingGlass.Toolkit;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if HAS_NEWTONSOFT_JSON
using Newtonsoft.Json.Linq;
#endif

using ToolkitDisplay = LookingGlass.Toolkit.Display;

namespace LookingGlass {
    /// <summary>
    /// A callback for receiving notifications when calibrations are refreshed from the underlying system.
    /// </summary>
    public delegate void CalibrationRefreshEvent();

    /// <summary>
    /// <para>Contains access to Looking Glass display calibration data for all currently-connected displays to the system.</para>
    /// <para>Note that this class is NOT thread safe, and is expected to only be accessed on the Unity main thread.</para>
    /// </summary>
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public static class LKGDisplaySystem {
        private static BridgeConnectionHTTP bridgeConnection;
        //TODO: Add these to BridgeConnectionHTTP as public-getters:
        private static int port;
        private static int websocketPort;
        private static bool connected = false;

        private static bool isLoaded;
        private static ToolkitDisplay[] lkgDisplays;
        private static TaskCompletionSource<bool> connectTcs;
        private static TaskCompletionSource<bool> calibrationsTcs;
        private static bool disposed = false;
        private static bool isTesting = false;

        public static event CalibrationRefreshEvent onReload;

        public static bool IsLoaded => isLoaded;
        public static bool IsLoading => calibrationsTcs != null && !calibrationsTcs.Task.IsCompleted;

        /// <summary>
        /// Gets a LKG display in the array of connected displays that were found.
        /// </summary>
        /// <param name="index">Note that this is an arbitrary index, and should NOT be relied on for persistence.</param>
        public static ToolkitDisplay Get(int index) => new ToolkitDisplay(lkgDisplays[index]);
        public static int LKGDisplayCount => lkgDisplays?.Length ?? 0;
        public static IEnumerable<ToolkitDisplay> LKGDisplays {
            get {
                if (lkgDisplays != null)
                    foreach (ToolkitDisplay display in lkgDisplays)
                        yield return new ToolkitDisplay(display);
            }
        }

        public static Task WaitForConnected() {
            if (connectTcs != null)
                return connectTcs.Task;
            return Task.CompletedTask;
        }

        public static Task WaitForCalibrations() {
            if (calibrationsTcs != null)
                return calibrationsTcs.Task;
            return Task.CompletedTask;
        }

#if UNITY_EDITOR
        [Serializable]
        private class EditorPreloadedData {
            //NOTE: Turn this off to debug/visualize how long it takes for us to actually get calibration data.
            public const bool Enabled = true;

            public static string PersistentPath => Path.Combine(Application.persistentDataPath, "Editor Preload Data.json").Replace('\\', '/');
            public ToolkitDisplay[] allLKGDisplays;

            public void SetAsCurrrent() {
                LKGDisplaySystem.lkgDisplays = allLKGDisplays;
            }

            public static EditorPreloadedData GetCurrent() {
                EditorPreloadedData data = new();
                data.allLKGDisplays = LKGDisplaySystem.lkgDisplays;
                return data;
            }

            public static void SaveCurrentState() {
                string json = JsonUtility.ToJson(EditorPreloadedData.GetCurrent(), true);
                File.WriteAllText(EditorPreloadedData.PersistentPath, json);
            }
        }


        private static void OnPlayModeStateChange(PlayModeStateChange state) {
            switch (state) {
                case PlayModeStateChange.ExitingEditMode:
                    EditorPreloadedData.SaveCurrentState();
                    break;
            }
        }
#endif

        //NOTE: This runs:
        //  - Upon opening the Unity project/Unity editor
        //  - Upon entering playmode
        //NOT:
        //  - Upon re-entering edit mode
        static LKGDisplaySystem() {
#if UNITY_EDITOR
            //NOTE: Delay because we can't access Application.persistentDataPath during constructors (static constructors included)
            EditorApplication.delayCall += () => {
                if (EditorPreloadedData.Enabled) {
                    string path = EditorPreloadedData.PersistentPath;
                    if (File.Exists(path)) {
                        string json = File.ReadAllText(path);
                        EditorPreloadedData data = JsonUtility.FromJson<EditorPreloadedData>(json);
                        data.SetAsCurrrent();
                    }
                }
            };
#endif

            isLoaded = false;
            _ = Reconnect();

            Application.quitting += Dispose;
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeStateChange;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
#endif
        }

        public static void Dispose() {
            disposed = true;
            isLoaded = false;
            connected = false;
            if (bridgeConnection != null) {
                bridgeConnection.Dispose();
                bridgeConnection = null;
            }
            isTesting = false;

            //TODO: Hmm... considerations..
            ServiceLocator.Instance.Dispose();
            ServiceLocator.Instance = null;
        }

        private static async Task<bool> ForceReconnect() {
            connectTcs = new TaskCompletionSource<bool>();
            connected = false;
            if (bridgeConnection != null) {
                bridgeConnection.Dispose();
                bridgeConnection = null;
            }

            GetLKGBridgePorts(out port, out websocketPort);
            bridgeConnection = new BridgeConnectionHTTP(
                LookingGlass.Toolkit.ServiceLocator.Instance.GetSystem<LookingGlass.Toolkit.ILogger>(),
                LookingGlass.Toolkit.ServiceLocator.Instance.GetSystem<IHttpSender>(),
                "localhost",
                port,
                websocketPort
            );

            bool result = await Task.Run(() => {
                bool success = false;

                //WARNING: Not sure why this fails the first time upon opening the Unity project after a program restart...
                //      For now, let's just immediately retry. It works well for some reason:
                int maxRetries = 2;
                for (int i = 0; i < maxRetries; i++) {
                    success = bridgeConnection.Connect();
                    if (success)
                        break;
                }
                if (!success) {
                    Debug.LogError("Failed to connect to Looking Glass Bridge. Ensure LKG Bridge is running and retry by clicking LookingGlass → Retry LKG Bridge Connection.\n" +
                        "    LKG Bridge rest port: " + port + "\n" +
                        "    LKG Bridge websockets port: " + websocketPort);
                    return false;
                }
                connected = true;
                return true;
            });

            connectTcs.SetResult(result);
            return result;
        }

        /// <summary>
        /// <para>
        /// Gets the file path of the Looking Glass Bridge <c>settings.json</c> file.
        /// See the Bridge C++ source code for the source of truth of where this logic is based off of.
        /// </para>
        /// <remarks>
        /// (Specifically, search for the <c>wstring Settings::default_settings_folder()</c> function.
        /// As of the date of writing this, they can be found here:
        /// <list type="bullet">
        /// <item><a href="https://github.com/Looking-Glass/LookingGlassBridge/blob/master/service_engine/windows/Settings.cpp">Windows</a></item>
        /// <item><a href="https://github.com/Looking-Glass/LookingGlassBridge/blob/master/service_engine/macintosh/Settings.cpp">MacOS</a></item>
        /// <item><a href="https://github.com/Looking-Glass/LookingGlassBridge/blob/master/service_engine/linux/Settings.cpp">Linux</a></item>
        /// </list>
        /// </remarks>
        /// </summary>
        /// <returns>The file path of LKG Bridge's <c>settings.json</c> file, or <c>null</c> if not on implemented for the current platform.</returns>
        private static string GetLKGBridgeSettingsFilePath() {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Looking Glass/Bridge/settings.json").Replace('\\', '/');
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Looking Glass/Bridge/settings.json");
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lgf/Bridge/settings.json");
#else
            Debug.LogWarning("Unsupported platform for retriev LKG Bridge's settings.json file for configurable ports!");
            return null;
#endif
        }

        private static void GetLKGBridgePorts(out int restPort, out int websocketPort) {
            restPort = BridgeConnectionHTTP.DefaultPort;
            websocketPort = BridgeConnectionHTTP.DefaultWebSocketPort;

#if !HAS_NEWTONSOFT_JSON
            return;
#else
            string filePath = GetLKGBridgeSettingsFilePath();
            if (filePath != null) {
                if (File.Exists(filePath)) {
                    try {
                        string json = File.ReadAllText(filePath);

                        try {
                            JObject j = JObject.Parse(File.ReadAllText(filePath));
                            int value = j["rest_port"].Value<int>();
                            restPort = value;
                        } catch { }

                        try {
                            JObject j = JObject.Parse(File.ReadAllText(filePath));
                            int value = j["websocket_port"].Value<int>();
                            websocketPort = value;
                        } catch { }
                    } catch (Exception e) {
                        Debug.LogException(e);
                    }
                } else {
                    Debug.LogWarning("Unable to find LKG Bridge settings.json at file path: " + filePath);
                }
            }
#endif
        }

        public static async Task<bool> Reconnect() {
            try {
#if !HAS_NEWTONSOFT_JSON
                return false;
#else

                if ((connectTcs != null && !connectTcs.Task.IsCompleted) ||
                    (calibrationsTcs != null && !calibrationsTcs.Task.IsCompleted))
                    throw new InvalidOperationException("The calibration system is already trying to connect! Check " + nameof(IsLoading) + " first before trying to reconnect.");

                calibrationsTcs = new TaskCompletionSource<bool>();
                DateTime startTime = DateTime.Now;
                Debug.Log("Attempting to connect to LKG Bridge at " + startTime + "...");

                bool result = true;
                if (!connected)
                    result = await ForceReconnect();

                if (!result || disposed) {
                    calibrationsTcs.SetResult(false);
                    goto End;
                }

                result = ((Func<bool>) (() => {
                    try {
                        string versionMessage = "Connected to LKG Bridge v";
                        string response = bridgeConnection.TrySendMessage("bridge_version", "{}");
                        if (response != null)
                            versionMessage += JObject.Parse(response)["payload"]["value"].Value<string>();
                        else
                            versionMessage += "???";
                        if (bridgeConnection != null) {
                            versionMessage += ", using API v";
                            response = bridgeConnection.TrySendMessage("api_version", "{}");
                            if (response != null)
                                versionMessage += JObject.Parse(response)["payload"]["value"].Value<string>();
                            else
                                versionMessage += "???";
                        }
                        versionMessage += " at " + DateTime.UtcNow.ToLongTimeString() + " UTC";
                        versionMessage += "\n    Using LKG Bridge " + (port == BridgeConnectionHTTP.DefaultPort ? "" : "custom ") + "rest port: " + port;
                        versionMessage += "\n    Using LKG Bridge " + (websocketPort == BridgeConnectionHTTP.DefaultWebSocketPort ? "" : "custom ") + "websocket port: " + websocketPort;
                        versionMessage += "\n";

                        Debug.Log(versionMessage);
                    } catch (Exception e) {
                        Debug.LogWarning("Failed to retrieve LKG Bridge version and API version.");
                        Debug.LogException(e);
                    }
                    return true;
                }))();

                if (!result || disposed)
                    goto End;
                result = bridgeConnection.TryEnterOrchestration();

                if (!result || disposed) {
                    Debug.LogWarning("Failed to enter LKG Bridge orchestration.");
                    calibrationsTcs.SetResult(false);
                    goto End;
                }

                //[GSE-718]: Add support for LKG Bridge events, when they work!
                await Task.Run(() => {
                    bridgeConnection.AddListener("Monitor Connect", (string payload) => {
                        Debug.LogWarning("EVENT! " + payload);
                    });
                    bridgeConnection.AddListener("Monitor Disconnect", (string payload) => {
                        Debug.LogWarning("EVENT! " + payload);
                    });
                    bridgeConnection.AddListener("", (string payload) => {
                        Debug.LogWarning("EVENT! " + payload);
                    });
                });

                result = ((Func<bool>) (() => {
                    if (!bridgeConnection.TryUpdateDevices()) {
                        Debug.LogWarning("Failed to update LKG devices.");
                        return false;
                    }
                    return true;
                }))();

                if (!result || disposed) {
                    calibrationsTcs.SetResult(false);
                    goto End;
                }

                lkgDisplays = bridgeConnection.GetLKGDisplays().ToArray();
                isLoaded = true;
                HologramCamera.UpdateAllCalibrations();
#if UNITY_EDITOR
                EditorPreloadedData.SaveCurrentState();
#endif
                calibrationsTcs.SetResult(true);

                onReload?.Invoke();

                if (!isTesting) {
                    isTesting = true;

                    CoroutineRunner runner = new GameObject("Coroutines").AddComponent<CoroutineRunner>();
                    if (Application.isPlaying)
                        GameObject.DontDestroyOnLoad(runner.gameObject);
                    runner.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    runner.StartCoroutine(WaitForEventsCoroutine(() => {
                        GameObject.DestroyImmediate(runner.gameObject);
                    }));
                }

End:
                return result;
#endif
            } catch (Exception e) {
                Debug.LogException(e);
                return false;
            }
        }

        public static Task ReloadCalibrations() {
            try {
                calibrationsTcs = new TaskCompletionSource<bool>();
                //WARNING: Copied from above, to avoid nesting Task.Run(...) calls:
                lkgDisplays = bridgeConnection.GetLKGDisplays().ToArray();
                isLoaded = true;
                HologramCamera.UpdateAllCalibrations();
#if UNITY_EDITOR
                EditorPreloadedData.SaveCurrentState();
#endif
                calibrationsTcs.SetResult(true);
                onReload?.Invoke();
            } catch (Exception e) {
                Debug.LogException(e);
            }
            return Task.CompletedTask;
        }

        private class CoroutineRunner : MonoBehaviour { }

        private static IEnumerator WaitForEventsCoroutine(Action onDone) {
            IEnumerator Wait(float duration) {
#if UNITY_EDITOR
                if (!Application.isPlaying) {
                    TaskCompletionSource<bool> tcs = new();
                    Task.Run(() => {
                        Task.Delay((int) (1000 * duration)).Wait();
                        EditorApplication.delayCall += () => {
                            tcs.SetResult(true);
                            EditorApplication.QueuePlayerLoopUpdate();
                        };
                    });
                    while (!tcs.Task.IsCompleted)
                        yield return null;
                } else {
#endif
                    yield return new WaitForSeconds(duration);
#if UNITY_EDITOR
                }
#endif
            }

            while (!disposed) {
                if (bridgeConnection.TryUpdateDevices()) {
                    calibrationsTcs = new TaskCompletionSource<bool>();
                    //WARNING: Copied from above, to avoid nesting Task.Run(...) calls:
                    ToolkitDisplay[] nextDisplays = bridgeConnection.GetLKGDisplays().ToArray();

                    bool CheckThatAllExist<T>(T[] from, T[] to, Func<T, T, bool> match) {
                        if (from == null)
                            return true;
                        if (to == null)
                            return from.Length <= 0;

                        for (int i = 0; i < from.Length; i++) {
                            bool foundSame = false;
                            for (int j = 0; j < to.Length; j++) {
                                if (match(from[i], to[j])) {
                                    foundSame = true;
                                    break;
                                }
                            }
                            if (!foundSame)
                                return false;
                        }
                        return true;
                    }

                    bool changed = false;
                    if (!CheckThatAllExist(lkgDisplays, nextDisplays, (lhs, rhs) => lhs.Equals(rhs))
                        || !CheckThatAllExist(nextDisplays, lkgDisplays, (lhs, rhs) => lhs.Equals(rhs)))
                        changed = true;

                    if (changed) {
                        lkgDisplays = nextDisplays;
                        isLoaded = true;
                        HologramCamera.UpdateAllCalibrations();
#if UNITY_EDITOR
                        EditorPreloadedData.SaveCurrentState();
#endif
                        calibrationsTcs.SetResult(true);
                        onReload?.Invoke();
                    } else {
                        calibrationsTcs.SetResult(true);
                    }
                    yield return Wait(2);
                } else {
                    yield return Wait(3);
                }
            }
            onDone();
        }
    }
}
