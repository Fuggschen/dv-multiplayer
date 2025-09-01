using System.Collections;
using UnityEngine;
using MultiplayerVC.Networking;
using MPAPI.Interfaces;

namespace MultiplayerVC.Voice
{
    // Create this from the host mod at runtime and assign the bridge/policies.
    [DisallowMultipleComponent]
    public sealed class VoiceBootstrap : MonoBehaviour
    {
        [Header("Attach or create components")]
        public VoiceCaptureBehaviour? Capture;
        public VoicePlaybackBehaviour? Playback;

        public void Initialize(IVoiceNetworkBridge bridge, IProximityProvider? proximity = null, IMuteProvider? mute = null)
        {
            Debug.Log($"[VC] VoiceBootstrap.Initialize: bridge.IsServer={bridge.IsServer}, proximity={(proximity?.GetType().Name ?? "null")}, mute={(mute?.GetType().Name ?? "null")}");
            var transport = new BridgeVoiceTransport(bridge, proximity, mute);

            if (Capture == null)
            {
                Debug.Log("[VC] Adding VoiceCaptureBehaviour to GameObject");
                Capture = gameObject.AddComponent<VoiceCaptureBehaviour>();
            }
            if (Playback == null)
            {
                Debug.Log("[VC] Adding VoicePlaybackBehaviour to GameObject");
                Playback = gameObject.AddComponent<VoicePlaybackBehaviour>();
            }

            // disable capture loopback when bridged
            Capture.loopback = false;
            Capture.Transport = transport;
            Playback.Transport = transport;
            Debug.Log("[VC] VoiceBootstrap.Initialize completed: capture and playback bound to transport");
        }
    }

    // Simple runtime auto-integration with the Multiplayer API.
    // Initializes once MPAPI reports a server or client has started, or immediately if already active.
    internal sealed class VoiceAutoInitializer : MonoBehaviour
    {
        private static bool s_spawned;
        private static bool s_initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (s_spawned) return;
            var go = new GameObject("[VC] Bootstrapper");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<VoiceAutoInitializer>();
            s_spawned = true;
            Debug.Log("[VC] AutoInitializer: created bootstrapper");
        }

        private void OnEnable()
        {
            MPAPI.MultiplayerAPI.ServerStarted += OnServerStarted;
            MPAPI.MultiplayerAPI.ClientStarted += OnClientStarted;

            // If already connected before we subscribed, initialize immediately.
            if (!s_initialized && (MPAPI.MultiplayerAPI.Server != null || MPAPI.MultiplayerAPI.Client != null))
            {
                InitializeRuntime();
            }
        }

        private void OnDisable()
        {
            MPAPI.MultiplayerAPI.ServerStarted -= OnServerStarted;
            MPAPI.MultiplayerAPI.ClientStarted -= OnClientStarted;
        }

        private void OnServerStarted(IServer _)
        {
            if (!s_initialized) InitializeRuntime();
        }

        private void OnClientStarted(IClient _)
        {
            if (!s_initialized) InitializeRuntime();
        }

        private void InitializeRuntime()
        {
            s_initialized = true;
            Debug.Log("[VC] AutoInitializer: initializing voice via MPAPI");

            var root = new GameObject("[VC] Runtime");
            Object.DontDestroyOnLoad(root);

            var bootstrap = root.AddComponent<VoiceBootstrap>();

            // No proximity provider; broadcast to all clients and let Unity handle spatialization.
            IMuteProvider mute = new LocalMuteProvider();

            var bridge = new MpApiVoiceBridge();
            bootstrap.Initialize(bridge, null, mute);
        }
    }
}
