using DV;
using DV.InventorySystem;
using DV.Logic.Job;
using DV.Scenarios.Common;
using DV.ServicePenalty;
using DV.ThingTypes;
using DV.WeatherSystem;
using Humanizer;
using LiteNetLib;
using LiteNetLib.Utils;
using MPAPI.Interfaces.Packets;
using MPAPI.Types;
using Multiplayer.API;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Train;
using Multiplayer.Networking.Packets.Clientbound;
using Multiplayer.Networking.Packets.Clientbound.Jobs;
using Multiplayer.Networking.Packets.Clientbound.SaveGame;
using Multiplayer.Networking.Packets.Clientbound.Train;
using Multiplayer.Networking.Packets.Clientbound.World;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Networking.Packets.Common.Train;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Networking.Packets.Serverbound.Train;
using Multiplayer.Networking.Packets.Unconnected;
using Multiplayer.Networking.TransportLayers;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using UnityEngine;

namespace Multiplayer.Networking.Managers.Server;

public class NetworkServer : NetworkManager
{
    public Action<ServerPlayer> PlayerConnected;
    public Action<ServerPlayer> PlayerDisconnected;
    public Action<ServerPlayer> PlayerReady;
    protected override string LogPrefix => "[Server]";

    private readonly Queue<ITransportPeer> joinQueue = new();   //Queue for players attempting to join while server is loading

    private readonly Dictionary<byte, ServerPlayer> serverPlayers = [];             //player Id to ServerPlayer mapping
    private readonly Dictionary<byte, ITransportPeer> peers = [];                   //player Id to peer mapping
    private readonly Dictionary<ITransportPeer, ServerPlayer> peerToPlayer = [];    //peer to ServerPlayer mapping

    private LobbyServerManager lobbyServerManager;
    public readonly bool IsSinglePlayer;
    public LobbyServerData ServerData;
    public RerailController rerailController;

    public IReadOnlyCollection<ServerPlayer> ServerPlayers => serverPlayers.Values;
    public int PlayerCount => ServerPlayers.Count;

    private ITransportPeer _selfPeer;
    public ITransportPeer SelfPeer
    {
        get
        {
            if (_selfPeer != null)
                return _selfPeer;

            peers.TryGetValue(SelfId, out _selfPeer);
            return _selfPeer;
        }
    }

    public byte SelfId => NetworkLifecycle.Instance.Client?.PlayerId ?? 0;

    public readonly IDifficulty Difficulty;
    private bool IsLoaded;

    private readonly ChatManager _chatManager = new();
    public ChatManager ChatManager => _chatManager;

    public NetworkServer(IDifficulty difficulty, Settings settings, bool singlePlayer, LobbyServerData serverData) : base(settings)
    {
        Log($"Server created for {(singlePlayer ? "single player" : "multiplayer")} game");

        IsSinglePlayer = singlePlayer;
        ServerData = serverData;
        Difficulty = difficulty;
    }

    public override bool Start(int port)
    {
        //setup paint theme lookup cache
        PaintThemeLookup.Instance.CheckInstance();

        WorldStreamingInit.LoadingFinished += OnLoaded;

        Multiplayer.Log($"Starting server...");
        //Try to get our static IPv6 Address we will need this for IPv6 NAT punching to be reliable
        if (IPAddress.TryParse(LobbyServerManager.GetStaticIPv6Address(), out IPAddress ipv6Address))
        {
            //start the connection, IPv4 messages can come from anywhere, IPv6 messages need to specifically come from the static IPv6
            return base.Start(IPAddress.Any, ipv6Address, port);

        }

        //we're not running IPv6, start as normal
        return base.Start(port);
    }

    public override void Stop()
    {
        WorldStreamingInit.LoadingFinished -= OnLoaded;

        if (lobbyServerManager != null)
        {
            lobbyServerManager.RemoveFromLobbyServer();
            UnityEngine.Object.Destroy(lobbyServerManager);
        }

        //Alert all clients (except host)
        var packet = WritePacket(new ClientboundDisconnectPacket());
        foreach (var peer in peers.Values)
        {
            if (peer != SelfPeer)
                peer?.Disconnect(packet);
        }

        //Reset player ID pool
        foreach (var player in serverPlayers.Values)
            player.Dispose();

        base.Stop();
    }

    protected override void Subscribe()
    {
        //Client management
        netPacketProcessor.SubscribeReusable<ServerboundClientLoginPacket, IConnectionRequest>(OnServerboundClientLoginPacket);

        //World sync
        netPacketProcessor.SubscribeReusable<ServerboundClientReadyPacket, ITransportPeer>(OnServerboundClientReadyPacket);
        netPacketProcessor.SubscribeReusable<ServerboundSaveGameDataRequestPacket, ITransportPeer>(OnServerboundSaveGameDataRequestPacket);
        netPacketProcessor.SubscribeReusable<ServerboundTimeAdvancePacket, ITransportPeer>(OnServerboundTimeAdvancePacket);


        netPacketProcessor.SubscribeReusable<ServerboundPlayerPositionPacket, ITransportPeer>(OnServerboundPlayerPositionPacket);
        netPacketProcessor.SubscribeReusable<ServerboundTrainSyncRequestPacket>(OnServerboundTrainSyncRequestPacket);
        netPacketProcessor.SubscribeReusable<ServerboundTrainDeleteRequestPacket, ITransportPeer>(OnServerboundTrainDeleteRequestPacket);
        netPacketProcessor.SubscribeReusable<ServerboundTrainRerailRequestPacket, ITransportPeer>(OnServerboundTrainRerailRequestPacket);
        netPacketProcessor.SubscribeReusable<ServerboundLicensePurchaseRequestPacket, ITransportPeer>(OnServerboundLicensePurchaseRequestPacket);
        netPacketProcessor.SubscribeReusable<CommonChangeJunctionPacket, ITransportPeer>(OnCommonChangeJunctionPacket);
        netPacketProcessor.SubscribeReusable<CommonRotateTurntablePacket, ITransportPeer>(OnCommonRotateTurntablePacket);
        netPacketProcessor.SubscribeReusable<CommonCouplerInteractionPacket, ITransportPeer>(OnCommonCouplerInteractionPacket);
        //netPacketProcessor.SubscribeReusable<CommonTrainCouplePacket, ITransportPeer>(OnCommonTrainCouplePacket);
        netPacketProcessor.SubscribeReusable<CommonTrainUncouplePacket, ITransportPeer>(OnCommonTrainUncouplePacket);
        netPacketProcessor.SubscribeReusable<CommonHoseConnectedPacket, ITransportPeer>(OnCommonHoseConnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonHoseDisconnectedPacket, ITransportPeer>(OnCommonHoseDisconnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonMuConnectedPacket, ITransportPeer>(OnCommonMuConnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonMuDisconnectedPacket, ITransportPeer>(OnCommonMuDisconnectedPacket);
        netPacketProcessor.SubscribeReusable<CommonCockFiddlePacket, ITransportPeer>(OnCommonCockFiddlePacket);
        netPacketProcessor.SubscribeReusable<CommonBrakeCylinderReleasePacket, ITransportPeer>(OnCommonBrakeCylinderReleasePacket);
        netPacketProcessor.SubscribeReusable<CommonHandbrakePositionPacket, ITransportPeer>(OnCommonHandbrakePositionPacket);
        netPacketProcessor.SubscribeReusable<CommonPaintThemePacket, ITransportPeer>(OnCommonPaintThemePacket);
        netPacketProcessor.SubscribeReusable<ServerboundAddCoalPacket, ITransportPeer>(OnServerboundAddCoalPacket);
        netPacketProcessor.SubscribeReusable<ServerboundFireboxIgnitePacket, ITransportPeer>(OnServerboundFireboxIgnitePacket);
        netPacketProcessor.SubscribeReusable<CommonTrainPortsPacket, ITransportPeer>(OnCommonTrainPortsPacket);
        netPacketProcessor.SubscribeReusable<CommonTrainFusesPacket, ITransportPeer>(OnCommonTrainFusesPacket);
        netPacketProcessor.SubscribeReusable<ServerboundJobValidateRequestPacket, ITransportPeer>(OnServerboundJobValidateRequestPacket);
        netPacketProcessor.SubscribeReusable<CommonChatPacket, ITransportPeer>(OnCommonChatPacket);
        netPacketProcessor.SubscribeReusable<UnconnectedPingPacket, IPEndPoint>(OnUnconnectedPingPacket);
        netPacketProcessor.SubscribeNetSerializable<CommonItemChangePacket, ITransportPeer>(OnCommonItemChangePacket);
    }

    //allow mods to register their own packets
    public void RegisterExternalPacket<T>(ServerPacketHandler<T> handler) where T : class, IPacket, new()
    {
        netPacketProcessor.SubscribeReusable<T, ITransportPeer>((packet, peer) =>
        {
            var serverPlayer = TryGetServerPlayer(peer, out var player) ? new ServerPlayerWrapper(player) : null;
            handler(packet, serverPlayer);
        });
    }

    public void RegisterExternalSerializablePacket<T>(ServerPacketHandler<T> handler) where T : class, ISerializablePacket, new()
    {
        netPacketProcessor.SubscribeNetSerializable<ExternalSerializablePacketWrapper<T>, ITransportPeer>((wrapper, peer) =>
        {
            var serverPlayer = TryGetServerPlayer(peer, out var player) ? new ServerPlayerWrapper(player) : null;
            handler(wrapper.Packet, serverPlayer);
        },
        () => new ExternalSerializablePacketWrapper<T>()
        );
    }

    private void OnLoaded()
    {
        if (!IsSinglePlayer)
        {
            lobbyServerManager = NetworkLifecycle.Instance.GetOrAddComponent<LobbyServerManager>();
        }

        Log($"Server loaded, processing {joinQueue.Count} queued players");
        IsLoaded = true;

        while (joinQueue.Count > 0)
        {
            ITransportPeer peer = joinQueue.Dequeue();

            // Assuming the `peer.ConnectionState` property exists and is being checked
            if (peer.ConnectionState.Equals(TransportConnectionState.Connected))
            {
                System.Console.WriteLine("Connection is established.");
                OnServerboundClientReadyPacket(null, peer);
            }
            else
            {
                System.Console.WriteLine("Connection is not established.");
            }
        }
    }

    public bool TryGetServerPlayer(ITransportPeer peer, out ServerPlayer player)
    {
        return peerToPlayer.TryGetValue(peer, out player);
    }

    public bool TryGetServerPlayer(byte playerId, out ServerPlayer player)
    {
        return serverPlayers.TryGetValue(playerId, out player);
    }

    #region Net Events

    public override void OnPeerConnected(ITransportPeer peer)
    {
        LogDebug(() => $"OnPeerConnected({peer.Id})");
    }

    public override void OnPeerDisconnected(ITransportPeer peer, DisconnectReason disconnectReason)
    {
        LogDebug(() => $"OnPeerDisconnected({peer.Id})");
        if (!peerToPlayer.TryGetValue(peer, out ServerPlayer player))
        {
            LogWarning($"Peer {peer.GetType()}, peerId: {peer.Id} disconnected but no player found");
            return;
        }

        Log($"Player {player.Username} disconnected: {disconnectReason}");

        if (WorldStreamingInit.isLoaded)
            SaveGameManager.Instance.UpdateInternalData();

        serverPlayers.Remove(player.PlayerId);
        peers.Remove(player.PlayerId);
        peerToPlayer.Remove(peer);

        SendPacketToAll
            (
                new ClientboundPlayerDisconnectPacket
                {
                    PlayerId = player.PlayerId
                },
                DeliveryMethod.ReliableUnordered
            );

        PlayerDisconnected?.Invoke(player);

        player.Dispose();
    }

    public override void OnNetworkLatencyUpdate(ITransportPeer peer, int latency)
    {
        if (!TryGetServerPlayer(peer, out var player))
            return;

        ClientboundPingUpdatePacket clientboundPingUpdatePacket = new()
        {
            PlayerId = player.PlayerId,
            Ping = latency
        };

        SendPacketToAll(clientboundPingUpdatePacket, DeliveryMethod.ReliableUnordered, peer);

        if (latency > LATENCY_FLAG)
        {
            LogWarning($"High Ping Detected! Player: \"{player.Username}\", ping: {latency}ms");
        }

        // Ensure we don't send a TickSync packet to ourselves
        if (peer == SelfPeer)
            return;

        SendPacket(peer, new ClientboundTickSyncPacket
        {
            ServerTick = NetworkLifecycle.Instance.Tick
        }, DeliveryMethod.ReliableUnordered);
    }

    public override void OnConnectionRequest(NetDataReader requestData, IConnectionRequest request)
    {
        LogDebug(() => $"NetworkServer OnConnectionRequest");
        netPacketProcessor.ReadAllPackets(requestData, request);
    }

    #endregion

    #region Packet Senders

    private void SendPacketToAll<T>(T packet, DeliveryMethod deliveryMethod) where T : class, new()
    {
        NetDataWriter writer = WritePacket(packet);
        foreach (var peer in peers.Values)
            peer?.Send(writer, deliveryMethod);
    }

    private void SendPacketToAll<T>(T packet, DeliveryMethod deliveryMethod, ITransportPeer excludePeer) where T : class, new()
    {
        NetDataWriter writer = WritePacket(packet);
        foreach (var peer in peers.Values)
        {
            if (peer == excludePeer)
                continue;
            peer?.Send(writer, deliveryMethod);
        }
    }

    private void SendNetSerializablePacketToAll<T>(T packet, DeliveryMethod deliveryMethod) where T : INetSerializable, new()
    {
        NetDataWriter writer = WriteNetSerializablePacket(packet);
        foreach (var peer in peers.Values)
            peer?.Send(writer, deliveryMethod);
    }

    private void SendNetSerializablePacketToAll<T>(T packet, DeliveryMethod deliveryMethod, ITransportPeer excludePeer) where T : INetSerializable, new()
    {
        NetDataWriter writer = WriteNetSerializablePacket(packet);
        foreach (var peer in peers.Values)
        {
            if (peer == excludePeer)
                continue;
            peer?.Send(writer, deliveryMethod);
        }
    }

    #region Mod Packets
    public void SendExternalPacketToAll<T>(T packet, bool reliable) where T : class, IPacket, new()
    {
        var deliveryMethod = reliable ? DeliveryMethod.ReliableUnordered : DeliveryMethod.Unreliable;
        SendPacketToAll(packet, deliveryMethod);
    }

    public void SendExternalPacketToAll<T>(T packet, bool reliable, ITransportPeer excludePeer) where T : class, IPacket, new()
    {
        var deliveryMethod = reliable ? DeliveryMethod.ReliableUnordered : DeliveryMethod.Unreliable;

        if (excludePeer == null)
            SendPacketToAll(packet, deliveryMethod);
        else
            SendPacketToAll(packet, deliveryMethod, excludePeer);
    }

    public void SendExternalSerializablePacketToAll<T>(T packet, bool reliable) where T : class, ISerializablePacket, new()
    {
        var deliveryMethod = reliable ? DeliveryMethod.ReliableUnordered : DeliveryMethod.Unreliable;
        var wrapper = new ExternalSerializablePacketWrapper<T> { Packet = packet };
        SendNetSerializablePacketToAll(wrapper, deliveryMethod);
    }

    public void SendExternalSerializablePacketToAll<T>(T packet, bool reliable, ITransportPeer excludePeer) where T : class, ISerializablePacket, new()
    {
        var deliveryMethod = reliable ? DeliveryMethod.ReliableUnordered : DeliveryMethod.Unreliable;
        var wrapper = new ExternalSerializablePacketWrapper<T> { Packet = packet };

        if (excludePeer == null)
            SendNetSerializablePacketToAll(wrapper, deliveryMethod);
        else
            SendNetSerializablePacketToAll(wrapper, deliveryMethod, excludePeer);
    }

    public void SendExternalPacketToPlayer<T>(T packet, ITransportPeer peer, bool reliable) where T : class, IPacket, new()
    {
        var deliveryMethod = reliable ? DeliveryMethod.ReliableUnordered : DeliveryMethod.Unreliable;
        SendPacket(peer, packet, deliveryMethod);
    }

    public void SendExternalSerializablePacketToPlayer<T>(T packet, ITransportPeer peer, bool reliable) where T : class, ISerializablePacket, new()
    {
        var deliveryMethod = reliable ? DeliveryMethod.ReliableUnordered : DeliveryMethod.Unreliable;
        var wrapper = new ExternalSerializablePacketWrapper<T> { Packet = packet };

        SendNetSerializablePacket(peer, wrapper, deliveryMethod);
    }

    #endregion

    public void KickPlayer(ServerPlayer player)
    {
        if (player == null || player.Peer == null)
            return;

        player.Peer.Disconnect(WritePacket(new ClientboundDisconnectPacket { Kicked = true }));
    }
    public void SendGameParams(GameParams gameParams)
    {
        SendPacketToAll(ClientboundGameParamsPacket.FromGameParams(gameParams), DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendSpawnTrainset(List<TrainCar> set, bool autoCouple, bool sendToAll, ITransportPeer sendTo = null)
    {

        LogDebug(() =>
        {
            StringBuilder sb = new();

            sb.Append($"SendSpawnTrainSet() Sending trainset {set?.FirstOrDefault()?.GetNetId()} with {set?.Count} cars");

            TrainCar[] noNetId = set?.Where(car => car.GetNetId() == 0).ToArray();

            if (noNetId.Length > 0)
                sb.AppendLine($"Erroneous cars!: {string.Join(", ", noNetId.Select(car => $"{{{car?.ID}, {car?.CarGUID}, {car.logicCar != null}}}"))}");

            return sb.ToString();

        });

        var packet = ClientboundSpawnTrainSetPacket.FromTrainSet(set, autoCouple);

        if (!sendToAll)
        {
            if (sendTo == null)
                LogError($"SendSpawnTrainSet() Trying to send to null peer!");
            else
                SendPacket(sendTo, packet, DeliveryMethod.ReliableOrdered);
        }
        else
            SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendSpawnTrainCar(NetworkedTrainCar networkedTrainCar)
    {
        SendPacketToAll(ClientboundSpawnTrainCarPacket.FromTrainCar(networkedTrainCar), DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendDestroyTrainCar(NetworkedTrainCar netTrainCar, ITransportPeer peer = null)
    {
        //ushort netID = trainCar.GetNetId();
        Log($"Sending DestroyTrainCarPacket for [{netTrainCar.CurrentID} {netTrainCar.NetId}]");

        if (netTrainCar.NetId == 0)
        {
            Multiplayer.LogWarning($"SendDestroyTrainCar failed. [{netTrainCar.CurrentID} {netTrainCar.NetId}]");
            return;
        }

        var packet = new ClientboundDestroyTrainCarPacket { NetId = netTrainCar.NetId };

        if (peer == null)
            SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, SelfPeer);
        else
            SendPacket(peer, packet, DeliveryMethod.ReliableOrdered);
    }

    public void SendTrainsetPhysicsUpdate(ClientboundTrainsetPhysicsPacket packet, bool reliable)
    {
        //LogDebug(() => $"Sending Physics packet for netId: {packet.FirstNetId}, tick: {packet.Tick}");
        SendPacketToAll(packet, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable, SelfPeer);
    }

    public void SendBrakeState(ushort netId, float mainReservoirPressure, float brakePipePressure, float brakeCylinderPressure, float overheatPercent, float overheatReductionFactor, float temperature)
    {
        SendPacketToAll(new ClientboundBrakeStateUpdatePacket
        {
            NetId = netId,
            MainReservoirPressure = mainReservoirPressure,
            BrakePipePressure = brakePipePressure,
            BrakeCylinderPressure = brakeCylinderPressure,
            OverheatPercent = overheatPercent,
            OverheatReductionFactor = overheatReductionFactor,
            Temperature = temperature
        }, DeliveryMethod.ReliableOrdered, SelfPeer);

        //Multiplayer.LogDebug(()=> $"Sending Brake Pressures netId {netId}: {mainReservoirPressure}, {independentPipePressure}, {brakePipePressure}, {brakeCylinderPressure}");
    }

    public void SendFireboxState(ushort netId, float fireboxContents, bool fireboxOn)
    {
        SendPacketToAll(new ClientboundFireboxStatePacket
        {
            NetId = netId,
            Contents = fireboxContents,
            IsOn = fireboxOn
        }, DeliveryMethod.ReliableOrdered, SelfPeer);

        Multiplayer.LogDebug(() => $"Sending Firebox States netId {netId}: {fireboxContents}, {fireboxOn}");
    }

    public void SendCargoState(NetworkedTrainCar netTraincar, bool isLoading, byte cargoModelIndex)
    {
        Car logicCar = netTraincar?.TrainCar?.logicCar;

        if (logicCar == null)
        {
            LogWarning($"Attempted to send cargo state for {netTraincar?.CurrentID}, but logic car does not exist!");
            return;
        }

        CargoType cargoType = isLoading ? logicCar.CurrentCargoTypeInCar : logicCar.LastUnloadedCargoType;
        SendPacketToAll(new ClientboundCargoStatePacket
        {
            NetId = netTraincar.NetId,
            IsLoading = isLoading,
            CargoType = (ushort)cargoType,
            CargoAmount = logicCar.LoadedCargoAmount,
            CargoHealth = netTraincar.TrainCar.CargoDamage.HealthPercentage,
            CargoModelIndex = cargoModelIndex,
            WarehouseMachineId = logicCar.CargoOriginWarehouse?.ID
        }, DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendCargoHealthUpdate(ushort netId, float currentHealth)
    {
        SendPacketToAll(new ClientboundCargoHealthUpdatePacket
        {
            NetId = netId,
            CargoHealth = currentHealth,
        }, DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendCarHealthUpdate(ushort netId, TrainCarHealthData health)
    {
        SendPacketToAll(new ClientboundCarHealthUpdatePacket
        {
            NetId = netId,
            Health = health
        }, DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendRerailTrainCar(ushort netId, ushort rerailTrack, Vector3 worldPos, Vector3 forward)
    {
        SendPacketToAll(new ClientboundRerailTrainPacket
        {
            NetId = netId,
            TrackId = rerailTrack,
            Position = worldPos,
            Forward = forward
        }, DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendWindowsBroken(ushort netId, Vector3 forceDirection)
    {
        SendPacketToAll(new ClientboundWindowsBrokenPacket
        {
            NetId = netId,
            ForceDirection = forceDirection
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendWindowsRepaired(ushort netId)
    {
        SendPacketToAll(new ClientboundWindowsRepairedPacket
        {
            NetId = netId
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendMoney(float amount)
    {
        SendPacketToAll(new ClientboundMoneyPacket
        {
            Amount = amount
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendLicense(string id, bool isJobLicense)
    {
        SendPacketToAll(new ClientboundLicenseAcquiredPacket
        {
            Id = id,
            IsJobLicense = isJobLicense
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendGarage(string id)
    {
        SendPacketToAll(new ClientboundGarageUnlockPacket
        {
            Id = id
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendDebtStatus(bool hasDebt)
    {
        SendPacketToAll(new ClientboundDebtStatusPacket
        {
            HasDebt = hasDebt
        }, DeliveryMethod.ReliableUnordered, SelfPeer);
    }

    public void SendTrainUncouple(Coupler coupler, bool playAudio, bool dueToBrokenCouple, bool viaChainInteraction)
    {
        ushort couplerNetId = coupler.train.GetNetId();

        if (couplerNetId == 0)
        {
            LogWarning($"SendTrainUncouple failed. Coupler: {coupler.name} {couplerNetId}");
            return;
        }

        LogDebug(() => $"SendTrainUncouple({coupler.train.ID}, {coupler.isFrontCoupler}, {dueToBrokenCouple}, {viaChainInteraction})");

        SendPacketToAll(
            new CommonTrainUncouplePacket
            {
                NetId = couplerNetId,
                IsFrontCoupler = coupler.isFrontCoupler,
                PlayAudio = playAudio,
                ViaChainInteraction = viaChainInteraction,
                DueToBrokenCouple = dueToBrokenCouple,
            },
            DeliveryMethod.ReliableOrdered
        );
    }

    public void SendJobsCreatePacket(NetworkedStationController networkedStation, NetworkedJob[] jobs, ITransportPeer peer = null)
    {
        Multiplayer.Log($"Sending JobsCreatePacket for stationNetId {networkedStation.NetId} with {jobs.Count()} jobs");

        var packet = ClientboundJobsCreatePacket.FromNetworkedJobs(networkedStation, jobs);

        if (peer == null)
            SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, SelfPeer);
        else
            SendPacket(peer, packet, DeliveryMethod.ReliableOrdered);
    }

    public void SendJobsUpdatePacket(ushort stationNetId, NetworkedJob[] jobs)
    {
        Multiplayer.Log($"Sending JobsUpdatePacket for stationNetId {stationNetId} with {jobs.Count()} jobs");
        SendPacketToAll(ClientboundJobsUpdatePacket.FromNetworkedJobs(stationNetId, jobs), DeliveryMethod.ReliableOrdered, SelfPeer);
    }

    public void SendItemsChangePacket(List<ItemUpdateData> items, ServerPlayer player)
    {
        Multiplayer.Log($"Sending SendItemsChangePacket with {items.Count()} items to {player.Username}");

        if (player.Peer != null && player.Peer != SelfPeer)
        {
            SendNetSerializablePacket(player.Peer, new CommonItemChangePacket { Items = items },
                DeliveryMethod.ReliableOrdered);
        }
    }

    public void SendChat(string message, ServerPlayer exclude = null)
    {

        if (exclude != null)
        {
            NetworkLifecycle.Instance.Server.SendPacketToAll(new CommonChatPacket
            {
                message = message
            }, DeliveryMethod.ReliableUnordered, exclude.Peer);
        }
        else
        {
            NetworkLifecycle.Instance.Server.SendPacketToAll(new CommonChatPacket
            {
                message = message
            }, DeliveryMethod.ReliableUnordered);
        }
    }

    public void SendWhisper(string message, ServerPlayer recipient)
    {
        if (!string.IsNullOrEmpty(message) && recipient != null && recipient.Peer != null)
        {
            NetworkLifecycle.Instance.Server.SendPacket(recipient.Peer, new CommonChatPacket
            {
                message = message
            }, DeliveryMethod.ReliableUnordered);
        }

    }

    #endregion

    #region Listeners

    private void OnServerboundClientLoginPacket(ServerboundClientLoginPacket packet, IConnectionRequest request)
    {
        LogDebug(() => $"OnServerboundClientLoginPacket from {packet.Username}");

        // clean up username - remove leading/trailing white space, swap spaces for underscores and truncate
        packet.Username = packet.Username.Trim().Replace(' ', '_').Truncate(Settings.MAX_USERNAME_LENGTH);
        string overrideUsername = packet.Username;

        //ensure the username is unique
        int uniqueName = ServerPlayers.Where(player => player.OriginalUsername.ToLower() == packet.Username.ToLower()).Count();

        if (uniqueName > 0)
        {
            overrideUsername += uniqueName;
        }

        Guid guid;
        try
        {
            guid = new Guid(packet.Guid);
        }
        catch (ArgumentException)
        {
            // This can only happen if the sent GUID is tampered with, in which case, we aren't worried about showing a message.
            Log($"Invalid GUID from {packet.Username}{(Multiplayer.Settings.LogIps ? $" at {request.RemoteEndPoint.Address}" : "")}");
            request.Reject();
            return;
        }

        Log($"Processing login packet for {packet.Username} ({guid}){(Multiplayer.Settings.LogIps ? $" at {request.RemoteEndPoint.Address}" : "")}");

        if (Multiplayer.Settings.Password != packet.Password)
        {
            LogWarning("Denied login due to invalid password!");
            ClientboundLoginResponsePacket denyPacket = new()
            {
                ReasonKey = Locale.DISCONN_REASON__INVALID_PASSWORD_KEY
            };
            request.Reject(WritePacket(denyPacket));
            return;
        }

        if (packet.BuildVersion != Multiplayer.LocalBuildInfo)
        {
            LogWarning($"Denied login to incorrect game version! Got: {packet.BuildVersion}, expected: {Multiplayer.LocalBuildInfo}");
            ClientboundLoginResponsePacket denyPacket = new()
            {
                ReasonKey = Locale.DISCONN_REASON__GAME_VERSION_KEY,
                ReasonArgs = [Multiplayer.LocalBuildInfo, packet.BuildVersion.ToString()]
            };
            request.Reject(WritePacket(denyPacket));
            return;
        }

        if (PlayerCount >= Multiplayer.Settings.MaxPlayers || IsSinglePlayer && PlayerCount >= 1)
        {
            LogWarning("Denied login due to server being full!");
            ClientboundLoginResponsePacket denyPacket = new()
            {
                ReasonKey = Locale.DISCONN_REASON__FULL_SERVER_KEY
            };
            request.Reject(WritePacket(denyPacket));
            return;
        }

        var validation = ModCompatibilityManager.Instance.ValidateClientMods(packet.Mods);
        if (!validation.IsValid)
        {

            LogWarning($"Denied login due to mod mismatch! {validation.Missing.Count} missing, {validation.Extra} extra");
            LogDebug(() =>
            {
                StringBuilder sb = new("Mod mis-match:");
                sb.AppendLine("Server Mods:");
                foreach (ModInfo mod in ModCompatibilityManager.Instance.GetLocalMods())
                    sb.AppendLine($"\t{mod.Id} {mod.Version}, Status: {ModCompatibilityManager.Instance.GetCompatibility(mod)}");

                sb.AppendLine("\r\nClient Mods:");
                foreach (ModInfo mod in packet.Mods)
                    sb.AppendLine($"\t{mod.Id} {mod.Version}, Status (if known): {ModCompatibilityManager.Instance.GetCompatibility(mod)}");

                sb.AppendLine("\r\nMissing Mods:");
                foreach (ModInfo mod in validation.Missing)
                    sb.AppendLine($"\t{mod.Id} {mod.Version}, Status: {ModCompatibilityManager.Instance.GetCompatibility(mod)}");

                sb.AppendLine("\r\nExtra Mods:");
                foreach (ModInfo mod in validation.Extra)
                    sb.AppendLine($"\t{mod.Id} {mod.Version}, Status (if known): {ModCompatibilityManager.Instance.GetCompatibility(mod)}");

                return sb.ToString();
            });

            ClientboundLoginResponsePacket denyPacket = new()
            {
                ReasonKey = Locale.DISCONN_REASON__MODS_KEY,
                Missing = validation.Missing.ToArray(),
                Extra = validation.Extra.ToArray(),
            };
            request.Reject(WritePacket(denyPacket));
            return;
        }

        ITransportPeer peer = request.Accept();

        ServerPlayer serverPlayer = new
        (
            peer,
            overrideUsername,
            packet.Username,
            guid
        );

        serverPlayers.Add(serverPlayer.PlayerId, serverPlayer);
        peerToPlayer.Add(peer, serverPlayer);

        PlayerConnected?.Invoke(serverPlayer);

        ClientboundLoginResponsePacket acceptPacket = new()
        {
            Accepted = true,
            PlayerId = serverPlayer.PlayerId,
        };

        SendPacket(peer, acceptPacket, DeliveryMethod.ReliableUnordered);
    }

    private void OnServerboundSaveGameDataRequestPacket(ServerboundSaveGameDataRequestPacket packet, ITransportPeer peer)
    {
        LogDebug(() => $"OnServerboundSaveGameDataRequestPacket from peerId: {peer.Id}");

        if (!TryGetServerPlayer(peer, out ServerPlayer player))
        {
            LogError($"Save game data request received for {peer.GetType()}, peerId: {peer.Id}, but ServerPlayer not found");
            peer.Disconnect();
            return;
        }

        //if (peers.ContainsKey((byte)peer.Id))
        //{
        //    LogWarning("Denied save game data request from already connected peer!");
        //    return;
        //}

        SendPacket(peer, ClientboundGameParamsPacket.FromGameParams(Globals.G.GameParams), DeliveryMethod.ReliableOrdered);
        SendPacket(peer, ClientboundSaveGameDataPacket.CreatePacket(player), DeliveryMethod.ReliableOrdered);
    }

    private void OnServerboundClientReadyPacket(ServerboundClientReadyPacket packet, ITransportPeer peer)
    {
        LogDebug(() => $"OnServerboundClientReadyPacket from peerId: {peer.Id}");

        if (!peerToPlayer.TryGetValue(peer, out ServerPlayer serverPlayer))
        {
            LogError($"Ready packet received for {peer.GetType()}, peerId: {peer.Id}, but ServerPlayer not found");
            peer.Disconnect();
            return;
        }

        // Allow clients to connect before the server is fully loaded
        if (!IsLoaded)
        {
            joinQueue.Enqueue(peer);
            SendPacket(peer, new ClientboundServerLoadingPacket(), DeliveryMethod.ReliableOrdered);
            return;
        }

        // Unpause physics
        if (AppUtil.Instance.IsTimePaused)
            AppUtil.Instance.RequestSystemOnValueChanged(0.0f);

        // Allow the player to receive packets
        peers.Add(serverPlayer.PlayerId, peer);

        // Send the new player to all other players
        ClientboundPlayerJoinedPacket clientboundPlayerJoinedPacket = new()
        {
            PlayerId = serverPlayer.PlayerId,
            Username = serverPlayer.Username,
            //Guid = serverPlayer.Guid.ToByteArray()
        };
        SendPacketToAll(clientboundPlayerJoinedPacket, DeliveryMethod.ReliableOrdered, peer);

        LogDebug(() => $"Chatmanager");
        ChatManager.ServerMessage(serverPlayer.Username + " joined the game", null, serverPlayer);

        Log($"Client {peer.Id} is ready. Sending world state");

        // No need to sync the world state if the player is the host
        if (NetworkLifecycle.Instance.IsHost(serverPlayer))
        {
            SendPacket(peer, new ClientboundRemoveLoadingScreenPacket(), DeliveryMethod.ReliableOrdered);
            serverPlayer.IsLoaded = true;
            PlayerReady?.Invoke(serverPlayer);
            return;
        }

        SendPacket(peer, new ClientboundBeginWorldSyncPacket(), DeliveryMethod.ReliableOrdered);

        // Send weather state
        SendPacket(peer, WeatherDriver.Instance.GetSaveData(Globals.G.GameParams.WeatherEditorAlwaysAllowed).ToObject<ClientboundWeatherPacket>(), DeliveryMethod.ReliableOrdered);

        // Send junctions and turntables
        SendPacket(peer, new ClientboundRailwayStatePacket
        {
            SelectedJunctionBranches = NetworkedJunction.IndexedJunctions.Select(j => j.Junction.selectedBranch).ToArray(),
            TurntableRotations = NetworkedTurntable.IndexedTurntables.Select(j => j.TurntableRailTrack.currentYRotation).ToArray()
        }, DeliveryMethod.ReliableOrdered);

        // Send trains
        foreach (Trainset set in Trainset.allSets)
        {
            try
            {
                SendSpawnTrainset(set.cars, false, false, peer);
            }
            catch (Exception e)
            {
                LogWarning($"Exception when trying to send train set spawn data for [{set?.firstCar?.ID}, {set?.firstCar?.GetNetId()}]\r\n{e.Message}\r\n{e.StackTrace}");
            }
        }

        // Sync Stations (match NetIDs with StationIDs) - we could do this the same as junctions but juntions may need to be upgraded to work this way - future planning for mod integration
        SendPacket(peer, new ClientBoundStationControllerLookupPacket(NetworkedStationController.GetAll().ToArray()), DeliveryMethod.ReliableOrdered);

        //send jobs
        foreach (StationController station in StationController.allStations)
        {
            if (NetworkedStationController.GetFromStationController(station, out NetworkedStationController netStation))
            {
                //only send active jobs (available or in progress) - new clients don't need to know about old jobs
                NetworkedJob[] jobs = netStation.NetworkedJobs
                    .Where(j => j.Job.State == JobState.Available || j.Job.State == JobState.InProgress)
                    .ToArray();

                for (int i = 0; i < jobs.Length; i++)
                {
                    SendJobsCreatePacket(netStation, [jobs[i]], peer);
                }
            }
            else
            {
                LogError($"Sending job packets... Failed to get NetworkedStation from station");
            }
        }

        // Send existing players
        foreach (ServerPlayer player in ServerPlayers)
        {
            if (player.PlayerId == serverPlayer.PlayerId)
                continue;
            SendPacket(peer, new ClientboundPlayerJoinedPacket
            {
                PlayerId = player.PlayerId,
                Username = player.Username,
                //Guid = player.Guid.ToByteArray(),
                CarID = player.CarId,
                Position = player.RawPosition,
                Rotation = player.RawRotationY
            }, DeliveryMethod.ReliableOrdered);
        }

        // All data has been sent, allow the client to load into the world.
        Log($"Sending Remove Loading Screen to {serverPlayer.Username}");
        SendPacket(peer, new ClientboundRemoveLoadingScreenPacket(), DeliveryMethod.ReliableOrdered);

        serverPlayer.IsLoaded = true;

        PlayerReady?.Invoke(serverPlayer);
    }

    private void OnServerboundPlayerPositionPacket(ServerboundPlayerPositionPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
        {
            LogWarning($"Received Player Position from {peer.GetType()}, peerId: {peer.Id}, but could not find matching player.");
            return;
        }
 
        player.CarId = packet.CarID;
        player.RawPosition = packet.Position;
        player.RawRotationY = packet.RotationY;

        ClientboundPlayerPositionPacket clientboundPacket = new()
        {
            PlayerId = player.PlayerId,
            Position = packet.Position,
            MoveDir = packet.MoveDir,
            RotationY = packet.RotationY,
            IsJumpingIsOnCar = packet.IsJumpingIsOnCar,
            CarID = packet.CarID
        };

        SendPacketToAll(clientboundPacket, DeliveryMethod.Sequenced, peer);
    }

    private void OnServerboundTimeAdvancePacket(ServerboundTimeAdvancePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(new ClientboundTimeAdvancePacket
        {
            amountOfTimeToSkipInSeconds = packet.amountOfTimeToSkipInSeconds
        }, DeliveryMethod.ReliableUnordered, peer);
    }

    private void OnCommonChangeJunctionPacket(CommonChangeJunctionPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonRotateTurntablePacket(CommonRotateTurntablePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonCouplerInteractionPacket(CommonCouplerInteractionPacket packet, ITransportPeer peer)
    {
        if (!peerToPlayer.TryGetValue(peer, out var player))
        {
            LogWarning($"Received Coupler Interaction from {peer.GetType()}, peerId: {peer.Id}, but could not find matching player.");
            return;
        }

        //todo: add validation that to ensure the client is near the coupler - this packet may also be used for remote operations and may need to factor that in in the future
        if (NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar netTrainCar))
        {
            if (netTrainCar.Server_ValidateCouplerInteraction(packet, player))
            {
                //passed validation, send to all but the originator
                SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
            }
            else
            {
                LogDebug(() => $"OnCommonCouplerInteractionPacket([{packet.Flags}, {netTrainCar.CurrentID}, {packet.NetId}], {player.PlayerId}) Sending validation failure");
                //failed validation notify client
                SendPacket(
                            peer,
                            new CommonCouplerInteractionPacket
                            {
                                NetId = packet.NetId,
                                Flags = (ushort)CouplerInteractionType.NoAction,
                                IsFrontCoupler = packet.IsFrontCoupler,
                            }
                            , DeliveryMethod.ReliableOrdered
                          );
            }
        }
        else
        {
            LogDebug(() => $"OnCommonCouplerInteractionPacket([{packet.Flags}, {netTrainCar.CurrentID}, {packet.NetId}], {player.PlayerId}) Sending destroy");
            //Car doesn't exist, tell client to delete it
            SendDestroyTrainCar(netTrainCar, peer);
        }

    }
    //private void OnCommonTrainCouplePacket(CommonTrainCouplePacket packet, ITransportPeer peer)
    //{
    //    SendPacketToAll(packet, DeliveryMethod.ReliableUnordered, peer);
    //}

    private void OnCommonTrainUncouplePacket(CommonTrainUncouplePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonHoseConnectedPacket(CommonHoseConnectedPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonHoseDisconnectedPacket(CommonHoseDisconnectedPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonMuConnectedPacket(CommonMuConnectedPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonMuDisconnectedPacket(CommonMuDisconnectedPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonCockFiddlePacket(CommonCockFiddlePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonBrakeCylinderReleasePacket(CommonBrakeCylinderReleasePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonHandbrakePositionPacket(CommonHandbrakePositionPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonPaintThemePacket(CommonPaintThemePacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnServerboundAddCoalPacket(ServerboundAddCoalPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;

        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        //is value valid?
        if (float.IsNaN(packet.CoalMassDelta))
            return;

        if (!NetworkLifecycle.Instance.IsHost(player))
        {
            float carLength = CarSpawner.Instance.carLiveryToCarLength[networkedTrainCar.TrainCar.carLivery];

            //is player close enough to add coal?
            if ((player.WorldPosition - networkedTrainCar.transform.position).sqrMagnitude <= carLength * carLength)
                networkedTrainCar.firebox?.fireboxCoalControlPort.ExternalValueUpdate(packet.CoalMassDelta);
        }

    }

    private void OnServerboundFireboxIgnitePacket(ServerboundFireboxIgnitePacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;

        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        if (!NetworkLifecycle.Instance.IsHost(player))
        {
            //is player close enough to ignite firebox?
            float carLength = CarSpawner.Instance.carLiveryToCarLength[networkedTrainCar.TrainCar.carLivery];
            if ((player.WorldPosition - networkedTrainCar.transform.position).sqrMagnitude <= carLength * carLength)
                networkedTrainCar.firebox?.Ignite();
        }
    }

    private void OnCommonTrainPortsPacket(CommonTrainPortsPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        //if not the host && validation fails then ignore packet
        if (!NetworkLifecycle.Instance.IsHost(player))
        {
            bool flag = networkedTrainCar.Server_ValidateClientSimFlowPacket(player, packet);

            //LogDebug(() => $"OnCommonTrainPortsPacket from {player.Username}, Not host, valid: {flag}");
            if (!flag)
            {
                return;
            }
        }

        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnCommonTrainFusesPacket(CommonTrainFusesPacket packet, ITransportPeer peer)
    {
        SendPacketToAll(packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private void OnServerboundTrainSyncRequestPacket(ServerboundTrainSyncRequestPacket packet)
    {
        if (NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            networkedTrainCar.Server_DirtyAllState();
    }

    private void OnServerboundTrainDeleteRequestPacket(ServerboundTrainDeleteRequestPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;

        if (networkedTrainCar.HasPlayers)
        {
            LogWarning($"{player.Username} tried to delete a train with players in it!");
            return;
        }

        TrainCar trainCar = networkedTrainCar.TrainCar;
        float cost = trainCar.playerSpawnedCar ? 0.0f : Mathf.RoundToInt(Globals.G.GameParams.DeleteCarMaxPrice);
        if (!Inventory.Instance.RemoveMoney(cost))
        {
            LogWarning($"{player.Username} tried to delete a train without enough money to do so!");
            return;
        }

        Job job = JobsManager.Instance.GetJobOfCar(trainCar.logicCar);
        switch (job?.State)
        {
            case JobState.Available:
                job.ExpireJob();
                break;
            case JobState.InProgress:
                JobsManager.Instance.AbandonJob(job);
                break;
        }

        CarSpawner.Instance.DeleteCar(trainCar);
    }

    private void OnServerboundTrainRerailRequestPacket(ServerboundTrainRerailRequestPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;
        if (!NetworkedTrainCar.TryGet(packet.NetId, out NetworkedTrainCar networkedTrainCar))
            return;
        if (!NetworkedRailTrack.TryGet(packet.TrackId, out NetworkedRailTrack networkedRailTrack))
            return;

        TrainCar trainCar = networkedTrainCar.TrainCar;
        Vector3 position = packet.Position + WorldMover.currentMove;

        //Check if player is a Newbie (currently shared with all players)
        float cost = TutorialHelper.InRestrictedMode || rerailController != null && rerailController.isPlayerNewbie ? 0f :
            RerailController.CalculatePrice((networkedTrainCar.transform.position - position).magnitude, trainCar.carType, Globals.G.GameParams.RerailMaxPrice);

        if (!Inventory.Instance.RemoveMoney(cost))
        {
            LogWarning($"{player.Username} tried to rerail a train without enough money to do so!");
            return;
        }

        trainCar.Rerail(networkedRailTrack.RailTrack, position, packet.Forward);
    }

    private void OnServerboundLicensePurchaseRequestPacket(ServerboundLicensePurchaseRequestPacket packet, ITransportPeer peer)
    {
        if (!TryGetServerPlayer(peer, out ServerPlayer player))
            return;

        JobLicenseType_v2 jobLicense = null;
        GeneralLicenseType_v2 generalLicense = null;
        float? price = packet.IsJobLicense
            ? (jobLicense = Globals.G.Types.jobLicenses.Find(l => l.id == packet.Id))?.price
            : (generalLicense = Globals.G.Types.generalLicenses.Find(l => l.id == packet.Id))?.price;

        if (!price.HasValue)
        {
            LogWarning($"{player.Username} tried to purchase an invalid {(packet.IsJobLicense ? "job" : "general")} license with id {packet.Id}!");
            return;
        }

        CareerManagerDebtController.Instance.RefreshExistingDebtsState();
        if (CareerManagerDebtController.Instance.NumberOfNonZeroPricedDebts > 0)
        {
            LogWarning($"{player.Username} tried to purchase a {(packet.IsJobLicense ? "job" : "general")} license with id {packet.Id} while having existing debts!");
            return;
        }

        if (!Inventory.Instance.RemoveMoney(price.Value))
        {
            LogWarning($"{player.Username} tried to purchase a {(packet.IsJobLicense ? "job" : "general")} license with id {packet.Id} without enough money to do so!");
            return;
        }

        if (packet.IsJobLicense)
            LicenseManager.Instance.AcquireJobLicense(jobLicense);
        else
            LicenseManager.Instance.AcquireGeneralLicense(generalLicense);
    }


    private void OnServerboundJobValidateRequestPacket(ServerboundJobValidateRequestPacket packet, ITransportPeer peer)
    {
        Log($"OnServerboundJobValidateRequestPacket(): {packet.JobNetId}");

        if (!NetworkedJob.Get(packet.JobNetId, out NetworkedJob networkedJob))
        {
            LogWarning($"OnServerboundJobValidateRequestPacket() NetworkedJob not found: {packet.JobNetId}");

            SendPacket(peer, new ClientboundJobValidateResponsePacket { JobNetId = packet.JobNetId, Invalid = true }, DeliveryMethod.ReliableOrdered);
            return;
        }

        if (!TryGetServerPlayer(peer, out ServerPlayer player))
        {
            LogWarning($"OnServerboundJobValidateRequestPacket() ServerPlayer not found: {peer.Id}");
            return;
        }

        //Find the station and validator
        if (!NetworkedStationController.Get(packet.StationNetId, out NetworkedStationController networkedStationController) || networkedStationController.JobValidator == null)
        {
            LogWarning($"OnServerboundJobValidateRequestPacket() JobValidator not found. StationNetId: {packet.StationNetId}, StationController found: {networkedStationController != null}, JobValidator found: {networkedStationController?.JobValidator != null}");
            return;
        }

        LogDebug(() => $"OnServerboundJobValidateRequestPacket() Validating {packet.JobNetId}, Validation Type: {packet.validationType} overview: {networkedJob.JobOverview != null}, booklet: {networkedJob.JobBooklet != null}");
        switch (packet.validationType)
        {
            case ValidationType.JobOverview:
                networkedStationController.JobValidator.ProcessJobOverview(networkedJob.JobOverview.GetTrackedItem<JobOverview>());
                break;

            case ValidationType.JobBooklet:
                networkedStationController.JobValidator.ValidateJob(networkedJob.JobBooklet.GetTrackedItem<JobBooklet>());
                break;
        }

        //SendPacket(peer, new ClientboundJobValidateResponsePacket { JobNetId = packet.JobNetId, Invalid = false }, DeliveryMethod.ReliableUnordered);
    }

    private void OnCommonChatPacket(CommonChatPacket packet, ITransportPeer peer)
    {
        if (TryGetServerPlayer(peer, out ServerPlayer player))
            ChatManager.ProcessMessage(packet.message, player);
    }
    #endregion

    #region Unconnected Packet Handling
    private void OnUnconnectedPingPacket(UnconnectedPingPacket packet, IPEndPoint endPoint)
    {
        //Multiplayer.Log($"OnUnconnectedPingPacket({endPoint.Address})");
        //SendUnconnectedPacket(packet, endPoint.Address.ToString(), endPoint.Port);
    }

    private void OnCommonItemChangePacket(CommonItemChangePacket packet, ITransportPeer peer)
    {
        //if(!TryGetServerPlayer(peer, out var player))
        //    return;

        //LogDebug(()=>$"OnCommonItemChangePacket({packet?.Items?.Count}, {peer.Id} (\"{player.Username}\"))");

        //Multiplayer.LogDebug(() =>
        //{
        //    string debug = "";

        //    foreach (var item in packet?.Items)
        //    {
        //        debug += "UpdateType: " + item?.UpdateType + "\r\n";
        //        debug += "itemNetId: " + item?.ItemNetId + "\r\n";
        //        debug += "PrefabName: " + item?.PrefabName + "\r\n";
        //        debug += "Equipped: " + item?.ItemState + "\r\n";
        //        debug += "Position: " + item?.ItemPosition + "\r\n";
        //        debug += "Rotation: " + item?.ItemRotation + "\r\n";
        //        debug += "ThrowDirection: " + item?.ThrowDirection + "\r\n";
        //        debug += "Player: " + item?.Player + "\r\n";
        //        debug += "CarNetId: " + item?.CarNetId + "\r\n";
        //        debug += "AttachedFront: " + item?.AttachedFront + "\r\n";

        //        debug += "States:";

        //        if (item.States != null)
        //            foreach (var state in item?.States)
        //                debug += "\r\n\t" + state.Key + ": " + state.Value;
        //    }

        //    return debug;
        //}

        //);

        //NetworkedItemManager.Instance.ReceiveSnapshots(packet.Items, player);
    }
    #endregion
}
