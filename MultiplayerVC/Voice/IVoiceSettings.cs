using System;
using UnityEngine;

namespace MultiplayerVC.Voice
{
    /// <summary>
    /// Interface for providing voice chat settings to the MultiplayerVC module.
    /// This allows the main mod to provide settings without creating a direct dependency.
    /// </summary>
    public interface IVoiceSettings
    {
        bool VoiceEnabled { get; }
        KeyCode VoicePTTKey { get; }
        bool VoiceOpenMic { get; }
        float VoiceInputGain { get; }
        int VoiceVadThresholdDb { get; }
        float VoiceVolume { get; }
        string VoiceDeviceName { get; }
        
        /// <summary>
        /// Event raised when any voice setting changes
        /// </summary>
        event Action<IVoiceSettings> SettingsChanged;
    }
}
