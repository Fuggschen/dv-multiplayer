using MPAPI.Interfaces.Packets;
using MPAPI.Types;
using System;
using System.Collections.Generic;

namespace MPAPI.Interfaces;

/// <summary>
/// Interface for interacting with Multiplayer mod client instances
/// </summary>
public interface IClient
{
    /// <summary>
    /// Event fired when a player connects.
    /// </summary>
    /// <returns>IPlayer object for the connected player</returns>
    event Action<IPlayer> OnPlayerConnected;

    /// <summary>
    /// Event fired when a player disconnects, but before the IPlayer object is destroyed
    /// </summary>
    /// <returns>IPlayer object for the disconnected player</returns>
    event Action<IPlayer> OnPlayerDisconnected;

    /// <summary>
    /// Gets Player Id of the local player
    /// </summary>
    /// <remarks>
    /// The local player does not have an IPlayer object
    /// </remarks>
    byte PlayerId { get; }

    /// <summary>
    /// Gets IPlayer objects for all players connected to the server
    /// </summary>
    /// <returns>Read-only collection of IPlayer objects</returns>
    IReadOnlyCollection<IPlayer> Players { get; }

    /// <summary>
    /// Gets number of players currently connected to the server
    /// </summary>
    /// <returns>Positive integer representing the number of connected players</returns>
    int PlayerCount { get; }

    /// <summary>
    /// Gets IPlayer for player by Id
    /// </summary>
    /// <returns>IPlayer object if found, otherwise null</returns>
    IPlayer GetPlayer(byte playerId);

    /// <summary>
    /// Gets connection state for the client
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets ping for the client
    /// </summary>
    int Ping { get; }

    #region Packet API
    /// <summary>
    /// Register a packet type that uses automatic serialization
    /// </summary>
    /// <typeparam name="T">Packet type implementing IPacket</typeparam>
    /// <param name="handler">Handler to call when packet is received</param>
    void RegisterPacket<T>(ClientPacketHandler<T> handler) where T : class, IPacket, new();

    /// <summary>
    /// Register a packet type that uses manual serialization
    /// </summary>
    /// <typeparam name="T">Packet type implementing ISerializablePacket</typeparam>
    /// <param name="handler">Handler to call when packet is received</param>
    void RegisterSerializablePacket<T>(ClientPacketHandler<T> handler) where T : class, ISerializablePacket, new();


    /// <summary>
    /// Send a packet to the server
    /// </summary>
    /// <typeparam name="T">Packet type</typeparam>
    /// <param name="packet">Packet to send</param>
    /// <param name="reliable">Whether to send reliably</param>
    void SendPacketToServer<T>(T packet, bool reliable = true) where T : class, IPacket, new();

    /// <summary>
    /// Send a packet to all connected players
    /// </summary>
    /// <typeparam name="T">Packet type</typeparam>
    /// <param name="packet">Packet to send</param>
    /// <param name="reliable">Whether to send reliably</param>
    void SendSerializablePacketToServer<T>(T packet, bool reliable = true) where T : class, ISerializablePacket, new();

    #endregion
}
