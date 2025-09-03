using System.Collections.Generic;

namespace MultiplayerVC.Networking
{
    public interface IMuteProvider
    {
        bool IsMutedLocal(byte remoteId);    // client-side mute
        bool IsMutedServer(byte remoteId);    // server-side moderation
    }
}
