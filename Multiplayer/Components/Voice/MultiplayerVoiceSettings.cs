using System;
using MultiplayerVC.Voice;
using UnityEngine;

namespace Multiplayer.Components.Voice
{
    /// <summary>
    /// Bridges the main Multiplayer mod Settings to the IVoiceSettings interface for the voice chat module.
    /// This allows the voice chat to react to settings changes without creating a direct dependency.
    /// </summary>
    public sealed class MultiplayerVoiceSettings : IVoiceSettings
    {
        public bool VoiceEnabled => Multiplayer.Settings.VoiceEnabled;
        public KeyCode VoicePTTKey => Multiplayer.Settings.VoicePTTKey;
        public bool VoiceOpenMic => Multiplayer.Settings.VoiceOpenMic;
        public float VoiceInputGain => Multiplayer.Settings.VoiceInputGain;
        public int VoiceVadThresholdDb => Multiplayer.Settings.VoiceVadThresholdDb;
        public float VoiceVolume => Multiplayer.Settings.VoiceVolume;
        public string VoiceDeviceName => Multiplayer.Settings.VoiceDeviceName ?? string.Empty;

        public event Action<IVoiceSettings> SettingsChanged;

        public MultiplayerVoiceSettings()
        {
            // Subscribe to the main settings update event
            Settings.OnSettingsUpdated += OnMainSettingsUpdated;
        }

        private void OnMainSettingsUpdated(Settings settings)
        {
            // Notify voice components that settings have changed
            SettingsChanged?.Invoke(this);
        }

        /// <summary>
        /// Unsubscribe from settings events
        /// </summary>
        public void Dispose()
        {
            Settings.OnSettingsUpdated -= OnMainSettingsUpdated;
        }
    }
}
