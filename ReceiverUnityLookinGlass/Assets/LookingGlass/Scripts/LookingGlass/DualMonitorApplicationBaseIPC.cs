//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.

using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LookingGlass.DualMonitorApplication {
    [HelpURL("https://look.glass/unitydocs")]
    [RequireComponent(typeof(InterProcessCommunicator))]
    public class DualMonitorApplicationBaseIPC : MonoBehaviour {

        public DualMonitorApplicationDisplay display;
        [Header("InterProcess Communicator")]
        [Tooltip("Automatically handle the IPC. If you change this to false, you have to set the reference and configure the IPC yourself")]
        public bool automaticallyHandleIPC = true;
        [Tooltip("IPC being referenced. Don't worry about this if this if automaticallyHandleIPC is set to true")]
        public InterProcessCommunicator ipc;

        public virtual void Awake() {
#if !UNITY_2018_4_OR_NEWER && !UNITY_2019_1_OR_NEWER
        Debug.LogError("[LookingGlass] Dual Monitor Application requires Unity version 2018.4 or newer!");
# endif

            ipc = GetComponent<InterProcessCommunicator>();

            // automatically handle the ports
            if (automaticallyHandleIPC) {
                if (display == DualMonitorApplicationDisplay.Window2D) {
                    ipc.receiverPort = 8080;
                    ipc.senderPort = 8081;
                } else {
                    ipc.receiverPort = 8081;
                    ipc.senderPort = 8080;
                }
                ipc.role = InterProcessCommunicator.Role.Both;
            }

            // subscribe to the message received callback
            ipc.OnMessageReceived += ReceiveMessage;
        }

        public virtual void OnDestroy() {
            // unsubscribe when dead
            ipc.OnMessageReceived -= ReceiveMessage;
        }

        public virtual void Update() {
            bool esc = false;

#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = InputSystem.GetDevice<Keyboard>();
            if (keyboard != null) {
                if (keyboard.escapeKey.wasPressedThisFrame)
                    esc = true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
                esc = true;
#endif

            // basic quit command. will quit the non-focused application through IPC as well
            if (esc) {
                IPCQuit();
            }
        }

        // quits both the current application and any recipients
        public virtual void IPCQuit() {
            ipc.SendData("quit");
            Application.Quit();
        }

        public virtual void ReceiveMessage(string message) {
            // basic quit message receiver
            switch (message) {
                case "quit":
                    Application.Quit();
                    break;
            }
        }
    }
}