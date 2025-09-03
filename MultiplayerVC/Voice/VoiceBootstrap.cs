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

        public void Initialize(IVoiceNetworkBridge bridge, IMuteProvider? mute = null)
        {
            Debug.Log($"[VC] VoiceBootstrap.Initialize: IsServer={bridge.IsServer}, mute={(mute?.GetType().Name ?? "null")}");
            var transport = new BridgeVoiceTransport(bridge, mute);

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

            Capture.Transport = transport;
            Playback.Transport = transport;
            Debug.Log("[VC] VoiceBootstrap.Initialize completed: capture and playback bound to transport");
        }
    }

    // Centralized runtime integration with the Multiplayer API.
    internal static class VoiceRuntime
    {
        private static bool s_subscribed;
        private static bool s_initialized;

        // Ensures we subscribe to MPAPI events once and initialize immediately if already active.
        public static void EnsureSubscribed()
        {
            if (s_subscribed) return;
            MPAPI.MultiplayerAPI.ClientStarted += OnClientStarted;
            s_subscribed = true;
            Debug.Log("[Info] VoiceRuntime: subscribed to MPAPI events");
            // If already connected before we subscribed, initialize immediately.
            if (!s_initialized && MPAPI.MultiplayerAPI.Client != null)
            {
                InitializeRuntime();
            }
        }

        public static void Unsubscribe()
        {
            if (!s_subscribed) return;
            MPAPI.MultiplayerAPI.ClientStarted -= OnClientStarted;
            s_subscribed = false;
        }

        private static void OnClientStarted(IClient _)
        {
            if (!s_initialized) InitializeRuntime();
        }

        private static void InitializeRuntime()
        {
            s_initialized = true;
            Debug.Log("[VC] VoiceRuntime: initializing voice via MPAPI");

            var root = new GameObject("[VC] Runtime");
            Object.DontDestroyOnLoad(root);

            var bootstrap = root.AddComponent<VoiceBootstrap>();

            // No proximity provider; broadcast to all clients and let Unity handle spatialization.
            IMuteProvider mute = new LocalMuteProvider();

            var bridge = new MpApiVoiceBridge();
            bootstrap.Initialize(bridge, mute);
        }
    }
    // Auto-initialize VoiceRuntime after scene load if not already initialized by the host mod.
    internal sealed class VoiceAutoInitializer : MonoBehaviour
    {
        private static bool s_spawned;

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
            VoiceRuntime.EnsureSubscribed();
        }

        private void OnDisable()
        {
            VoiceRuntime.Unsubscribe();
        }
    }

    // Public entrypoint for explicit initialization from the host mod.
    public static class VoiceEntrypoint
    {
        // Call this from Multiplayer.cs after registering the MPAPI provider.
        public static void Initialize()
        {
            VoiceRuntime.EnsureSubscribed();
        }
    }
}
