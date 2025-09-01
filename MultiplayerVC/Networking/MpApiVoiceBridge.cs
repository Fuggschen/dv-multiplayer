using System;
using System.Collections.Generic;
using System.IO;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using UnityEngine;

namespace MultiplayerVC.Networking
{
    // Packet type for raw voice payload forwarding (manual serialization)
    internal sealed class VoicePayloadPacket : ISerializablePacket
    {
        public byte[]? Data = Array.Empty<byte>();

        public void Serialize(BinaryWriter writer)
        {
            if (Data == null) return;
            writer.Write(Data.Length);
            writer.Write(Data);
        }

        public void Deserialize(BinaryReader reader)
        {
            int len = reader.ReadInt32();
            if (len < 0 || len > 1_000_000) { Data = Array.Empty<byte>(); return; }
            Data = reader.ReadBytes(len);
        }
    }

    // Concrete bridge using the public MultiplayerAPI (no edits to main mod required)
    public sealed class MpApiVoiceBridge : IVoiceNetworkBridge, IDisposable
    {
        public bool IsServer => MPAPI.MultiplayerAPI.Server != null;
        public ulong SelfId
        {
            get
            {
                var client = MPAPI.MultiplayerAPI.Client;
                if (client != null)
                {
                    // There is no explicit "self player" accessor; we use Id=0 which is typically reserved for self in this mod.
                    var p = client.GetPlayer(0);
                    if (p != null) return p.Id;
                }
                return 0;
            }
        }

        public event Action<ulong, byte[]>? OnClientPayload;
        public event Action<ulong, byte[]>? OnServerPayload;

        private readonly IServer? _server;
        private readonly IClient? _client;

        public MpApiVoiceBridge()
        {
            _server = MPAPI.MultiplayerAPI.Server;
            _client = MPAPI.MultiplayerAPI.Client;
            Debug.Log($"[VC] VoiceBridge: created (IsServer={IsServer})");

            _server?.RegisterSerializablePacket<VoicePayloadPacket>((packet, sender) =>
            {
                Debug.Log($"[VC] VoiceBridge(Server): received client payload {packet.Data?.Length ?? 0} bytes from {sender.Id}");
                if (packet.Data != null) OnClientPayload?.Invoke(sender.Id, packet.Data);
            });

            _client?.RegisterSerializablePacket<VoicePayloadPacket>(packet =>
            {
                Debug.Log($"[VC] VoiceBridge(Client): received server payload {packet.Data?.Length ?? 0} bytes");
                // Sender is embedded in payload by serializer; the bridge will extract it downstream
                if (packet.Data != null) OnServerPayload?.Invoke(0, packet.Data);
            });
        }

        public void Dispose()
        {
            // API does not expose unregister; rely on lifecycle disposal
        }

        public void SendClientToServer(byte[] payload)
        {
            Debug.Log($"[VC] VoiceBridge(Client): send {payload?.Length ?? 0} bytes to server");
            _client?.SendSerializablePacketToServer(new VoicePayloadPacket { Data = payload }, reliable: false);
        }

        public void SendServerToClient(ulong targetPeerId, byte[] payload)
        {
            if (_server == null) return;
            var player = _server.GetPlayer((byte)targetPeerId);
            if (player != null)
            {
                Debug.Log($"[VC] VoiceBridge(Server): send {payload?.Length ?? 0} bytes to {targetPeerId}");
                _server.SendSerializablePacketToPlayer(new VoicePayloadPacket { Data = payload }, player, reliable: false);
            }
        }

        public void SendServerToClients(IEnumerable<ulong> targetPeerIds, byte[] payload)
        {
            if (_server == null) return;
            int count = 0;
            foreach (var id in targetPeerIds) { count++; }
            Debug.Log($"[VC] VoiceBridge(Server): broadcast {payload?.Length ?? 0} bytes to {count} targets");
            foreach (var id in targetPeerIds)
            {
                if (payload != null) SendServerToClient(id, payload);
            }
        }
    }
}
