using System.Collections.Generic;

namespace MultiplayerVC.Networking
{
    public interface IProximityProvider
    {
        // Return peer ids that should hear 'speakerId'. Host-only.
        IEnumerable<ulong> GetRecipientsFor(ulong speakerId);
    }

    public interface IMuteProvider
    {
        bool IsMutedLocal(ulong remoteId);    // client-side mute
        bool IsMutedServer(ulong remoteId);    // server-side moderation
    }
}
