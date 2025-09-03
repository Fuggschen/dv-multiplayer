using System.Collections.Generic;

namespace MultiplayerVC.Networking
{
    // Simple local-only mute provider with a public API to modify mutes
    public sealed class LocalMuteProvider : IMuteProvider
    {
        private readonly HashSet<byte> _localMutes = new HashSet<byte>();
        private readonly HashSet<byte> _serverMutes = new HashSet<byte>();

        public bool IsMutedLocal(byte remoteId) => _localMutes.Contains(remoteId);
        public bool IsMutedServer(byte remoteId) => _serverMutes.Contains(remoteId);

        public void MuteLocal(byte id) => _localMutes.Add(id);
        public void UnmuteLocal(byte id) => _localMutes.Remove(id);

        public void MuteServer(byte id) => _serverMutes.Add(id);
        public void UnmuteServer(byte id) => _serverMutes.Remove(id);
        public void Clear() { _localMutes.Clear(); _serverMutes.Clear(); }
    }
}
