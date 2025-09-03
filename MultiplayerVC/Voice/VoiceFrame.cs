using System;
using System.IO;
using UnityEngine;
using MPAPI.Interfaces.Packets;

namespace MultiplayerVC.Voice
{
    // Packet carrying one encoded voice frame. Serializable via MPAPI BinaryWriter/BinaryReader.
    public struct VoiceFrame : ISerializablePacket
    {
        public byte SenderId;
        public ushort Sequence;
        public uint Timestamp;
        public int SampleRate;
        public byte Channels;
        public VoiceCodecId Codec;
        public byte[] Payload;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)Codec);
            writer.Write(Channels);
            writer.Write(SampleRate);
            writer.Write(Sequence);
            writer.Write(Timestamp);
            writer.Write(SenderId);
            int len = Payload?.Length ?? 0;
            writer.Write(len);
            if (len > 0)
            {
                writer.Write(Payload);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            Codec = (VoiceCodecId)reader.ReadByte();
            Channels = reader.ReadByte();
            SampleRate = reader.ReadInt32();
            Sequence = reader.ReadUInt16();
            Timestamp = reader.ReadUInt32();
            SenderId = reader.ReadByte();
            int len = reader.ReadInt32();
            if (len is < 0 or > 1_000_000)
            {
                Payload = Array.Empty<byte>();
            }
            else
            {
                Payload = reader.ReadBytes(len);
            }
        }
    }
}
