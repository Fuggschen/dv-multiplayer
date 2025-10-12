using System.Collections;
using UnityEngine;
using MultiplayerVC.Networking;
using MPAPI.Interfaces;
using UnityModManagerNet;
using System.Runtime.InteropServices;

namespace MultiplayerVC.Voice
{
    // Create this from the host mod at runtime and assign the bridge/policies.
    [DisallowMultipleComponent]
    public sealed class VoiceBootstrap : MonoBehaviour
    {
        [Header("Attach or create components")]
        public VoiceCaptureBehaviour? Capture;
        public VoicePlaybackBehaviour? Playback;
        public VoiceSettingsBridge? SettingsBridge;

        public void Initialize(IVoiceNetworkBridge bridge, IMuteProvider? mute = null, IVoiceSettings? settings = null)
        {
            Logger.Log($"[VC] VoiceBootstrap.Initialize: IsServer={bridge.IsServer}, mute={(mute?.GetType().Name ?? "null")}, settings={(settings?.GetType().Name ?? "null")}");
            var transport = new BridgeVoiceTransport(bridge, mute);

            if (Capture == null)
            {
                Logger.Log("[VC] Adding VoiceCaptureBehaviour to GameObject");
                Capture = gameObject.AddComponent<VoiceCaptureBehaviour>();
            }
            if (Playback == null)
            {
                Logger.Log("[VC] Adding VoicePlaybackBehaviour to GameObject");
                Playback = gameObject.AddComponent<VoicePlaybackBehaviour>();
            }
            if (SettingsBridge == null)
            {
                Logger.Log("[VC] Adding VoiceSettingsBridge to GameObject");
                SettingsBridge = gameObject.AddComponent<VoiceSettingsBridge>();
            }

            Capture.Transport = transport;
            Playback.Transport = transport;

            // Connect settings bridge to voice components
            SettingsBridge.Capture = Capture;
            SettingsBridge.Playback = Playback;

            // Apply settings if provided
            if (settings != null)
            {
                SettingsBridge.SetSettingsProvider(settings);
            }

            Logger.Log("[VC] VoiceBootstrap.Initialize completed: capture and playback bound to transport, settings bridge configured");
        }
    }

    // Centralized runtime integration with the Multiplayer API.
    internal static class VoiceRuntime
    {
        private static bool s_subscribed;
        private static bool s_initialized;
        private static VoiceBootstrap? s_bootstrap;

        public static bool IsInitialized => s_initialized;

        // Ensures we subscribe to MPAPI events once and initialize immediately if already active.
        public static void EnsureSubscribed()
        {
            if (s_subscribed) return;
            MPAPI.MultiplayerAPI.ClientStarted += OnClientStarted;
            s_subscribed = true;
            Logger.Log("[Info] VoiceRuntime: subscribed to MPAPI events");
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

        public static void ApplySettings(IVoiceSettings? settings)
        {
            if (s_bootstrap?.SettingsBridge != null)
            {
                s_bootstrap.SettingsBridge.SetSettingsProvider(settings);
            }
        }

        private static void OnClientStarted(IClient _)
        {
            if (!s_initialized) InitializeRuntime();
        }

        private static void InitializeRuntime()
        {
            s_initialized = true;
            Logger.Log("[VC] VoiceRuntime: initializing voice via MPAPI");

            var root = new GameObject("[VC] Runtime");
            Object.DontDestroyOnLoad(root);

            s_bootstrap = root.AddComponent<VoiceBootstrap>();

            // No proximity provider; broadcast to all clients and let Unity handle spatialization.
            IMuteProvider mute = new LocalMuteProvider();

            var bridge = new MpApiVoiceBridge();

            // Get settings from entrypoint
            var settings = VoiceEntrypoint.GetSettingsProvider();

            s_bootstrap.Initialize(bridge, mute, settings);
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
            Logger.Log("[VC] AutoInitializer: created bootstrapper");
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
        private static IVoiceSettings? _settingsProvider;

        // Call this from Multiplayer.cs after registering the MPAPI provider.
        public static void Initialize()
        {
            VoiceRuntime.EnsureSubscribed();
        }

        // Call this to provide a settings provider for voice chat
        public static void SetSettingsProvider(IVoiceSettings? settings)
        {
            _settingsProvider = settings;
            // If runtime is already initialized, apply settings immediately
            if (VoiceRuntime.IsInitialized)
            {
                VoiceRuntime.ApplySettings(settings);
            }
        }

        internal static IVoiceSettings? GetSettingsProvider()
        {
            return _settingsProvider;
        }
    }

    public static class Logger {
        public static void Log(string message) {
            UnityModManager.Logger.Log(message);
        }
    }
}
