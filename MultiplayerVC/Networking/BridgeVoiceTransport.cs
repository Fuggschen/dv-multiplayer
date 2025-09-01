using System;
using System.Collections.Generic;
using System.Linq;
using MultiplayerVC.Voice;
using UnityEngine;

namespace MultiplayerVC.Networking
{
    public sealed class BridgeVoiceTransport : IVoiceTransport, IDisposable
    {
        public event Action<VoiceFrame>? OnVoiceFrame;

        private readonly IVoiceNetworkBridge _bridge;
        private readonly IProximityProvider? _proximity;
        private readonly IMuteProvider? _mute;
        private ushort _seq;

        public BridgeVoiceTransport(IVoiceNetworkBridge bridge, IProximityProvider? proximity = null, IMuteProvider? mute = null)
        {
            _bridge = bridge;
            _proximity = proximity;
            _mute = mute;
            _bridge.OnClientPayload += OnClientPayload;
            _bridge.OnServerPayload += OnServerPayload;
            Debug.Log($"[VC] BridgeVoiceTransport: created (IsServer={_bridge.IsServer})");
        }

        public void Dispose()
        {
            _bridge.OnClientPayload -= OnClientPayload;
            _bridge.OnServerPayload -= OnServerPayload;
        }

        public VoiceTransportStats? Statistics => null;

        public void SendVoiceFrame(VoiceFrame frame)
        {
            frame.SenderId = _bridge.SelfId;
            frame.Sequence = ++_seq;
            frame.Timestamp = (uint)Environment.TickCount;
            var bytes = VoiceFrameSerializer.Serialize(frame);

            if (_bridge.IsServer)
            {
                // Broadcast to all connected clients except the speaker; spatialization is handled client-side.
                var server = MPAPI.MultiplayerAPI.Server;
                var targetsList = new List<ulong>();
                if (server != null)
                {
                    byte speakerByte = (byte)frame.SenderId;
                    foreach (var p in server.Players)
                    {
                        if (p == null) continue;
                        if (p.Id == speakerByte) continue;
                        targetsList.Add(p.Id);
                    }
                }

                Debug.Log($"[VC] Transport(Server): broadcasting frame seq={frame.Sequence} to {targetsList.Count} targets (UnitySpatial)");
                _bridge.SendServerToClients(targetsList, bytes);
            }
            else
            {
                // Client sends to server; server will rebroadcast
                Debug.Log($"[VC] Transport(Client): sending frame seq={frame.Sequence} to server");
                _bridge.SendClientToServer(bytes);
            }
        }

        private void OnClientPayload(ulong fromPeerId, byte[] payload)
        {
            // Only server should receive this
            if (!_bridge.IsServer) return;
            if (_mute != null && _mute.IsMutedServer(fromPeerId)) { Debug.Log($"[VC] Transport(Server): drop muted client {fromPeerId}"); return; }
            if (!VoiceFrameSerializer.TryDeserialize(payload, 0, payload.Length, out var frame)) { Debug.LogWarning("[VC] Transport(Server): failed to deserialize client payload"); return; }
            // Use sender provided by network or embedded in payload; trust fromPeerId if non-zero
            if (fromPeerId != 0) frame.SenderId = fromPeerId;

            // Rebroadcast to all connected clients except the speaker; spatialization handled client-side.
            var server = MPAPI.MultiplayerAPI.Server;
            var targetsList = new List<ulong>();
            if (server != null)
            {
                byte speakerByte = (byte)fromPeerId;
                foreach (var p in server.Players)
                {
                    if (p == null) continue;
                    if (p.Id == speakerByte) continue;
                    targetsList.Add(p.Id);
                }
            }

            Debug.Log($"[VC] Transport(Server): received frame from {fromPeerId}, rebroadcast to {targetsList.Count} targets (UnitySpatial)");
            var bytes = VoiceFrameSerializer.Serialize(frame);
            _bridge.SendServerToClients(targetsList, bytes);
        }

        private void OnServerPayload(ulong fromPeerId, byte[] payload)
        {
            // Clients receive this from the server
            if (_bridge.IsServer) return;
            if (_mute != null && _mute.IsMutedLocal(fromPeerId)) { Debug.Log($"[VC] Transport(Client): drop muted remote {fromPeerId}"); return; }
            if (!VoiceFrameSerializer.TryDeserialize(payload, 0, payload.Length, out var frame)) { Debug.LogWarning("[VC] Transport(Client): failed to deserialize server payload"); return; }
            if (fromPeerId != 0) frame.SenderId = fromPeerId;
            Debug.Log($"[VC] Transport(Client): received frame from {frame.SenderId}, seq={frame.Sequence}");
            OnVoiceFrame?.Invoke(frame);
        }
    }
}
