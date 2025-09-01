using System;

namespace MultiplayerVC.Voice
{
    // Local-only transport to validate capture/encode/decode/playback without networking.
    public sealed class LoopbackTransport : IVoiceTransport
    {
        public event Action<VoiceFrame>? OnVoiceFrame;
        private readonly ulong _senderId;
        private ushort _seq;

        public LoopbackTransport(ulong senderId = 1)
        {
            _senderId = senderId;
        }

        public void SendVoiceFrame(VoiceFrame frame)
        {
            frame.SenderId = _senderId;
            frame.Sequence = ++_seq;
            frame.Timestamp = (uint)Environment.TickCount;
            OnVoiceFrame?.Invoke(frame);
        }

        public VoiceTransportStats? Statistics => null;
    }
}
