using System;

namespace MultiplayerVC.Voice
{
    public enum VoiceCodecId : byte
    {
        Opus = 1,
        Steam = 2,
    }

    // Represents one compressed audio frame (e.g., 20 ms) with minimal metadata.
    public sealed class VoiceFrame
    {
        public ulong SenderId; // opaque player id (netId/steamId) per host integration
        public ushort Sequence; // per-sender sequence counter (wraps)
        public uint Timestamp; // tick or ms
        public VoiceCodecId Codec;
        public int SampleRate;
        public byte Channels; // 1 for mono
        public byte[] Payload = Array.Empty<byte>();
        public float InputRms; // optional: local level meter
    }
}
