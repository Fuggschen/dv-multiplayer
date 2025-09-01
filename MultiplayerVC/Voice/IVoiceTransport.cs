using System;

namespace MultiplayerVC.Voice
{
    // Abstraction so this package doesn't depend on dv-multiplayer's NetworkManager.
    public interface IVoiceTransport
    {
        // Called by sender to push a compressed voice frame to the network layer.
        void SendVoiceFrame(VoiceFrame frame);

        // Subscribe to receive frames for local playback.
        event Action<VoiceFrame> OnVoiceFrame;

        // Optional statistics, useful for overlay.
        VoiceTransportStats? Statistics { get; }
    }

    public readonly struct VoiceTransportStats(int up, int down, int fps)
    {
        public readonly int UplinkBytesPerSecond = up;
        public readonly int DownlinkBytesPerSecond = down;
        public readonly int FramesPerSecond = fps;
    }
}
