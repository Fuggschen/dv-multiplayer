using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using UnityEngine;
using MPAPI;

namespace MultiplayerVC.Networking
{
    // Voice payload packet for MultiplayerVC (MPAPI compatible)
    public sealed class VoicePayloadPacket : ISerializablePacket
    {
        public byte SenderId;
        public byte[] Data = Array.Empty<byte>();

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(SenderId);
            int len = Data?.Length ?? 0;
            writer.Write(len);
            if (len > 0)
                writer.Write(Data);
        }

        public void Deserialize(BinaryReader reader)
        {
            SenderId = reader.ReadByte();
            int len = reader.ReadInt32();
            if (len is < 0 or > 1_000_000)
            {
                Data = Array.Empty<byte>();
                return;
            }
            Data = reader.ReadBytes(len);
        }
    }

    // Simple bridge using only MPAPI
    public sealed class MpApiVoiceBridge : IVoiceNetworkBridge
    {
        public bool IsServer => MultiplayerAPI.Server != null;
        public bool IsClient => MultiplayerAPI.Client != null;
        public byte SelfId
        {
            get
            {
                var client = MultiplayerAPI.Client;
                var senderID = client.PlayerId;
                return !client.IsConnected ? (byte)0 : senderID;
            }
        }

        public event Action<byte, byte[]>? OnPayload;

        private readonly IClient? _client;
        private readonly IServer? _server;

        public MpApiVoiceBridge()
        {
            _client = MultiplayerAPI.Client;
            _server = MultiplayerAPI.Server;
            Debug.Log($"[VC] VoiceBridge: created (IsServer={IsServer}, IsClient={IsClient})");

            // Register voice packet handlers via MPAPI only
            RegisterVoicePackets();
        }

        private void RegisterVoicePackets()
        {
            if (_client != null)
            {
                Debug.Log($"[VC] VoiceBridge: Registering client VoicePayloadPacket handler via MPAPI RegisterSerializablePacket");
                _client.RegisterSerializablePacket<VoicePayloadPacket>(packet =>
                {
                    Debug.Log($"[VC] VoiceBridge(Client): received voice payload from sender {packet.SenderId}, {packet.Data?.Length ?? 0} bytes");
                    if (packet.Data is { Length: > 0 })
                        OnPayload?.Invoke(packet.SenderId, packet.Data);
                });
            }
            else
            {
                Debug.Log($"[VC] VoiceBridge: No client instance available for RegisterSerializablePacket");
            }

            if (_server != null)
            {
                Debug.Log($"[VC] VoiceBridge: Registering server VoicePayloadPacket handler via MPAPI RegisterSerializablePacket");
                _server.RegisterSerializablePacket<VoicePayloadPacket>((packet, sender) =>
                {
                    var data = packet.Data ?? Array.Empty<byte>();
                    int len = data.Length;
                    var senderId = sender?.PlayerId ?? (byte)0;
                    Debug.Log($"[VC] VoiceBridge(Server): received voice payload {len} bytes from player {senderId}");
                    if (len == 0) return;

                    Debug.Log($"[VC] VoiceBridge(Server): forwarding voice packet to all except sender {senderId}");
                    var forwardPacket = new VoicePayloadPacket { SenderId = senderId, Data = data };
                    _server.SendSerializablePacketToAll(forwardPacket, reliable: false, excludePlayer: sender);

                    // Notify local server listeners (for local playback if needed)
                    OnPayload?.Invoke(senderId, data);
                });
            }
            else
            {
                Debug.Log($"[VC] VoiceBridge: No server instance available for RegisterSerializablePacket");
            }
        }

        public void SendToAll(byte[] payload)
        {
            Debug.Log($"[VC] VoiceBridge: SendToAll called with {payload?.Length ?? 0} bytes, IsServer={IsServer}, IsClient={IsClient}");
            if (payload == null) return;

            var packet = new VoicePayloadPacket { SenderId = SelfId, Data = payload };

            if (IsServer && IsClient && _server != null)
            {
                // Get ID so we don't send to ourselves'
                var sender = _server.GetPlayer(SelfId);
                // Host case: We are both server and client, directly broadcast to all other clients
                Debug.Log($"[VC] VoiceBridge: Host sending voice packet directly to all clients (sender {SelfId})");
                _server.SendSerializablePacketToAll(packet, reliable: false, sender);
                Debug.Log($"[VC] VoiceBridge: Host voice packet broadcasted to all clients");
            }
            else if (_client != null)
            {
                // Regular client case: Send to server (server will handle forwarding)
                Debug.Log($"[VC] VoiceBridge: Client sending voice packet to server (sender {SelfId})");
                _client.SendSerializablePacketToServer(packet, reliable: false);
                Debug.Log($"[VC] VoiceBridge: Client voice packet sent to server");
            }
            else
            {
                Debug.LogWarning($"[VC] VoiceBridge: SendToAll called but no valid client or server instance available");
            }
        }
    }
}
