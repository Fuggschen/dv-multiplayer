using System;

namespace MultiplayerVC.Networking
{
    // Implement this in the host mod to forward voice payloads over its networking.
    public interface IVoiceNetworkBridge
    {
        // True if this node is the authoritative server/host.
        bool IsServer { get; }

        // Local MPAPI player ID to tag outgoing frames.
        byte SelfId { get; }

        // Send from Sender to all recipients.
        void SendToAll(byte[] payload);

        // Raised when a payload arrives at client.
        event Action<byte, byte[]> OnPayload;
    }
}
