using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MultiplayerVC.Voice;
using UnityEngine;
using MPAPI;
using MPAPI.Interfaces.Packets;

namespace MultiplayerVC.Networking
{
    public sealed class BridgeVoiceTransport : IVoiceTransport, IDisposable
    {
        public event Action<VoiceFrame>? OnVoiceFrame;

        private readonly IVoiceNetworkBridge _bridge;
        private readonly IMuteProvider? _mute;
        private ushort _seq;

        public BridgeVoiceTransport(IVoiceNetworkBridge bridge, IMuteProvider? mute = null)
        {
            _bridge = bridge;
            _mute = mute;
            _bridge.OnPayload += OnPayload;
            Debug.Log($"[VC] BridgeVoiceTransport: created (IsServer={_bridge.IsServer}, bridge type={_bridge.GetType().Name})");
            Debug.Log($"[VC] BridgeVoiceTransport: OnPayload event subscribed to bridge");
        }

        public void Dispose()
        {
            _bridge.OnPayload -= OnPayload;
        }

        public VoiceTransportStats? Statistics => null;

        public void SendVoiceFrame(VoiceFrame frame)
        {
            frame.SenderId = _bridge.SelfId;
            frame.Sequence = ++_seq;
            frame.Timestamp = (uint)Environment.TickCount;

            // Serialize using MPAPI binary contract
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            frame.Serialize(bw);
            var bytes = ms.ToArray();

            // Broadcast to all connected clients
            var client = MultiplayerAPI.Client;
            var targetsList = new List<byte>();
            if (client?.Players == null) return;
            targetsList.AddRange(from p in client.Players where p.PlayerId != frame.SenderId select p.PlayerId);
            Debug.Log($"[VC] Transport(Sender): broadcasting frame seq={frame.Sequence} to {targetsList.Count} targets (sender={frame.SenderId})");
            _bridge.SendToAll(bytes);
        }


        private void OnPayload(byte fromPlayerId, byte[] payload)
        {
            Debug.Log($"[VC] Transport(Receiver): OnPayload called with fromPlayerId={fromPlayerId}, payload length={payload?.Length ?? 0}");
            
            // Clients receive this from the sender
            if (_mute != null && _mute.IsMutedLocal(fromPlayerId))
            {
                Debug.Log($"[VC] Transport(Receiver): drop muted remote {fromPlayerId}");
                return;
            }

            // Deserialize
            VoiceFrame frame;
            try
            {
                using var ms = new MemoryStream(payload);
                using var br = new BinaryReader(ms);
                frame = new VoiceFrame();
                frame.Deserialize(br);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VC] Transport(Receiver): failed to deserialize server payload: {ex.Message}");
                return;
            }

            if (fromPlayerId != 0) frame.SenderId = fromPlayerId;
            Debug.Log($"[VC] Transport(Receiver): received frame from {frame.SenderId}, seq={frame.Sequence}");
            OnVoiceFrame?.Invoke(frame);
        }
    }
}
