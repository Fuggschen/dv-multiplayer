using System;
using System.IO;
using UnityEngine;

namespace MultiplayerVC.Voice
{
    public static class VoiceFrameSerializer
    {
    private const byte Version = 1;

        public static byte[] Serialize(VoiceFrame frame)
        {
            using (var ms = new MemoryStream(12 + (frame.Payload?.Length ?? 0)))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Version);
                bw.Write((byte)frame.Codec);
                bw.Write(frame.Channels);
                bw.Write(frame.SampleRate);
                bw.Write(frame.Sequence);
                bw.Write(frame.Timestamp);
                bw.Write(frame.SenderId);
                int len = frame.Payload?.Length ?? 0;
                bw.Write(len);
                if (len > 0) bw.Write(frame.Payload);
                return ms.ToArray();
            }
        }

        public static bool TryDeserialize(byte[] data, int offset, int length, out VoiceFrame frame)
        {
            frame = new VoiceFrame();
            try
            {
                using (var ms = new MemoryStream(data, offset, length))
                using (var br = new BinaryReader(ms))
                {
                    byte ver = br.ReadByte();
                    if (ver != Version) return false;
                    frame.Codec = (VoiceCodecId)br.ReadByte();
                    frame.Channels = br.ReadByte();
                    frame.SampleRate = br.ReadInt32();
                    frame.Sequence = br.ReadUInt16();
                    frame.Timestamp = br.ReadUInt32();
                    frame.SenderId = br.ReadUInt64();
                    int len = br.ReadInt32();
                    if (len < 0 || len > 1_000_000) return false;
                    frame.Payload = br.ReadBytes(len);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VC] VoiceFrameSerializer: exception during deserialize: {ex.Message}");
                frame = null!;
                return false;
            }
        }
    }
}
