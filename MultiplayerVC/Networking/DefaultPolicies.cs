using System.Collections.Generic;
using UnityEngine;
using MPAPI.Interfaces;

namespace MultiplayerVC.Networking
{
    // Proximity provider that returns all player ids except the speaker (server-side)
    public sealed class AllRecipientsProximityProvider : IProximityProvider
    {
        public IEnumerable<ulong> GetRecipientsFor(ulong speakerId)
        {
            var server = MPAPI.MultiplayerAPI.Server;
            if (server == null)
                yield break;
            foreach (var p in server.Players)
            {
                if (p == null) continue;
                if (p.Id == (byte)speakerId) continue;
                yield return p.Id;
            }
        }
    }

    // Distance-based proximity using MPAPI server players' positions
    public sealed class DistanceProximityProvider : IProximityProvider
    {
        private readonly float _radius;

        public DistanceProximityProvider(float radius)
        {
            _radius = Mathf.Max(0f, radius);
        }

        public IEnumerable<ulong> GetRecipientsFor(ulong speakerId)
        {
            var server = MPAPI.MultiplayerAPI.Server;
            if (server == null)
                yield break;

            var speaker = server.GetPlayer((byte)speakerId);
            if (speaker == null || !speaker.IsLoaded)
                yield break;

            var pos = speaker.Position;
            float rSqr = _radius * _radius;
            foreach (var p in server.Players)
            {
                if (p == null || !p.IsLoaded) continue;
                if (p.Id == speaker.Id) continue;
                var d = (p.Position - pos).sqrMagnitude;
                if (d <= rSqr)
                    yield return p.Id;
            }
        }
    }

    // Simple local-only mute provider with a public API to modify mutes
    public sealed class LocalMuteProvider : IMuteProvider
    {
        private readonly HashSet<ulong> _localMutes = new HashSet<ulong>();
        private readonly HashSet<ulong> _serverMutes = new HashSet<ulong>();

        public bool IsMutedLocal(ulong remoteId) => _localMutes.Contains(remoteId);
        public bool IsMutedServer(ulong remoteId) => _serverMutes.Contains(remoteId);

        public void MuteLocal(ulong id) => _localMutes.Add(id);
        public void UnmuteLocal(ulong id) => _localMutes.Remove(id);

        public void MuteServer(ulong id) => _serverMutes.Add(id);
        public void UnmuteServer(ulong id) => _serverMutes.Remove(id);
        public void Clear() { _localMutes.Clear(); _serverMutes.Clear(); }
    }
}
