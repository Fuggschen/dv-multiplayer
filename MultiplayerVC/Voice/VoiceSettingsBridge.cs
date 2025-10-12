using UnityEngine;

namespace MultiplayerVC.Voice
{
    /// <summary>
    /// Bridges voice settings to the voice chat components.
    /// Applies settings from an IVoiceSettings provider to voice capture/playback components.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceSettingsBridge : MonoBehaviour
    {
        public VoiceCaptureBehaviour? Capture { get; set; }
        public VoicePlaybackBehaviour? Playback { get; set; }
        
        private IVoiceSettings? _settings;
        private bool _subscribed;

        /// <summary>
        /// Set the voice settings provider and apply current settings
        /// </summary>
        public void SetSettingsProvider(IVoiceSettings? settings)
        {
            // Unsubscribe from previous provider
            if (_settings != null && _subscribed)
            {
                _settings.SettingsChanged -= OnSettingsChanged;
                _subscribed = false;
            }

            _settings = settings;

            // Subscribe to new provider and apply current settings
            if (_settings != null)
            {
                _settings.SettingsChanged += OnSettingsChanged;
                _subscribed = true;
                ApplySettings(_settings);
                Logger.Log("[VC] VoiceSettingsBridge: Settings provider updated and applied");
            }
        }

        void OnDestroy()
        {
            if (_settings != null && _subscribed)
            {
                _settings.SettingsChanged -= OnSettingsChanged;
            }
        }

        private void OnSettingsChanged(IVoiceSettings settings)
        {
            ApplySettings(settings);
        }

        private void ApplySettings(IVoiceSettings settings)
        {
            try
            {
                Logger.Log($"[VC] VoiceSettingsBridge: Applying settings - VoiceEnabled={settings.VoiceEnabled}, PTTKey={settings.VoicePTTKey}, OpenMic={settings.VoiceOpenMic}, InputGain={settings.VoiceInputGain}, VADThreshold={settings.VoiceVadThresholdDb}, Volume={settings.VoiceVolume}, Device='{settings.VoiceDeviceName}'");

                // Apply voice enabled setting to both components
                ApplyVoiceEnabledSetting(settings);

                // Apply capture-specific settings
                if (Capture != null)
                {
                    ApplyCaptureSettings(settings);
                }

                // Apply playback-specific settings
                if (Playback != null)
                {
                    ApplyPlaybackSettings(settings);
                }

                Logger.Log("[VC] VoiceSettingsBridge: Successfully applied all settings to voice components");
            }
            catch (System.Exception ex)
            {
                Logger.Log($"[VC] VoiceSettingsBridge: Failed to apply settings: {ex}");
            }
        }

        private void ApplyVoiceEnabledSetting(IVoiceSettings settings)
        {
            bool voiceEnabled = settings.VoiceEnabled;
            
            // Enable/disable the entire voice components based on VoiceEnabled setting
            if (Capture != null) Capture.enabled = voiceEnabled;
            if (Playback != null) Playback.enabled = voiceEnabled;
            
            Logger.Log($"[VC] VoiceSettingsBridge: Voice chat enabled = {voiceEnabled}");
        }

        private void ApplyCaptureSettings(IVoiceSettings settings)
        {
            if (Capture == null) return;

            // Push-to-talk key
            var oldPTT = Capture.pushToTalk;
            Capture.pushToTalk = settings.VoicePTTKey;
            Logger.Log($"[VC] VoiceSettingsBridge: PTT key changed from {oldPTT} to {settings.VoicePTTKey}");

            // Open mic setting
            var oldOpenMic = Capture.openMic;
            Capture.openMic = settings.VoiceOpenMic;
            Logger.Log($"[VC] VoiceSettingsBridge: Open mic changed from {oldOpenMic} to {settings.VoiceOpenMic}");

            // Input gain
            var oldGain = Capture.inputGain;
            Capture.inputGain = Mathf.Clamp(settings.VoiceInputGain, 0.5f, 5f);
            Logger.Log($"[VC] VoiceSettingsBridge: Input gain changed from {oldGain} to {Capture.inputGain}");

            // VAD threshold
            var oldThreshold = Capture.vadThresholdDb;
            Capture.vadThresholdDb = Mathf.Clamp(settings.VoiceVadThresholdDb, -80, 0);
            Logger.Log($"[VC] VoiceSettingsBridge: VAD threshold changed from {oldThreshold} to {Capture.vadThresholdDb}");

            // Microphone device
            string deviceName = settings.VoiceDeviceName;
            Logger.Log($"[VC] VoiceSettingsBridge: Setting microphone device to '{deviceName}'");
            Capture.SetDevice(string.IsNullOrEmpty(deviceName) ? null : deviceName);
        }

        private void ApplyPlaybackSettings(IVoiceSettings settings)
        {
            if (Playback == null) return;

            // Voice volume
            var oldVolume = Playback.volume;
            Playback.volume = Mathf.Clamp01(settings.VoiceVolume);
            Logger.Log($"[VC] VoiceSettingsBridge: Voice volume changed from {oldVolume} to {Playback.volume}");
        }
    }
}
