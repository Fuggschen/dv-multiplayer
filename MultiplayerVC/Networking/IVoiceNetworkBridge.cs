using System;
using System.Collections.Generic;

namespace MultiplayerVC.Networking
{
    // Implement this in the host mod to forward voice payloads over its networking.
    public interface IVoiceNetworkBridge
    {
        // True if this node is the authoritative server/host.
        bool IsServer { get; }

        // Local unique id (e.g., SteamID or netId) to tag outgoing frames.
        ulong SelfId { get; }

        // Client -> Server
        void SendClientToServer(byte[] payload);

        // Server -> Client (targeted send to one peer)
        void SendServerToClient(ulong targetPeerId, byte[] payload);

        // Server -> Clients (broadcast with optional exclusions)
        void SendServerToClients(IEnumerable<ulong> targetPeerIds, byte[] payload);

        // Raised when a client payload arrives at server.
        event Action<ulong /*fromPeerId*/, byte[] /*payload*/> OnClientPayload;

        // Raised when a server payload arrives at client.
        event Action<ulong /*fromPeerId*/, byte[] /*payload*/> OnServerPayload;
    }
}
