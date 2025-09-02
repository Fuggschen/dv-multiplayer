using DV.Logic.Job;
using MPAPI.Types;
using System;

namespace MPAPI.Interfaces;

/// <summary>
/// Main interface for interacting with the Multiplayer mod
/// </summary>
public interface IMultiplayerAPI
{
    /// <summary>
    /// Gets the version of the Multiplayer API that the Multiplayer mod supports.
    /// </summary>
    public string SupportedApiVersion { get; }

    /// <summary>
    /// Gets the version of the Multiplayer mod itself.
    /// </summary>
    public string MultiplayerVersion { get; }

    /// <summary>
    /// Gets whether the multiplayer mod is currently loaded and active
    /// </summary>
    bool IsMultiplayerLoaded { get; }

    /// <summary>Sets the mod's compatibility requirements</summary>
    /// <param name="modId">String representing the your mod's Id (`ModEntry.Info.Id`)</param>
    /// <param name="compatibility">ModCompatibility flags representing installation host/client requirements</param>
    void SetModCompatibility(string modId, MultiplayerCompatibility compatibility);

    /// <summary>
    /// Returns true if either a host or client exist
    /// </summary>
    bool IsConnected { get; }
 
    /// <summary>
    /// Gets whether this instance is host
    /// </summary>
    bool IsHost { get; }

    /// <summary>
    /// Gets whether this instance is a dedicated server
    /// </summary>
    bool IsDedicatedServer { get; }

    /// <summary>
    /// Gets whether this current session is single player
    /// </summary>
    bool IsSinglePlayer { get; }

    /// <summary>
    /// Event fired when a game/network tick occurs
    /// Ticks occur at a fixed interval (TICK_INTERVAL = 1/TICK_RATE) and are useful for synchronisation, batching, and processing changes.
    ///
    /// The tick parameter can be used to determine if non-reliable packets have been dropped and to sequence actions for rollbacks or preventing stale data from being processed.
    /// 
    /// Example: In Multiplayer's TrainCar simulation sync, small changes are cached when they occur but sent as a single packet per TrainCar when OnTick fires, reducing network overhead.
    /// </summary>
    /// <remarks>The parameter represents the current tick number</remarks>
    event Action<uint> OnTick;

    /// <summary>
    /// The number of ticks per second (currently 24).
    /// Used to calculate the fixed tick interval: TICK_INTERVAL = 1.0f / TICK_RATE.
    /// </summary>
    uint TICK_RATE { get; }

    /// <summary>
    /// The current game tick.
    /// </summary>
    uint CurrentTick { get; }

    /// <summary>
    /// Gets the NetId for an object
    /// </summary>
    /// <param name="obj">The object you want the NetId for</param>
    /// <param name="netId">When this method returns, contains the NetId associated with the specified object, if found; otherwise, 0</param>
    /// <returns>True if a NetId for the object was found; otherwise, false</returns>
    bool TryGetNetId<T>(T obj, out ushort netId) where T : class;

    /// <summary>
    /// Gets the object for a NetId
    /// </summary>
    /// <param name="netId">The non-zero NetId for the object</param>
    /// <param name="obj">When this method returns, contains the object associated with the NetId, if found; otherwise null</param>
    /// <returns>True if the object was found; otherwise, false</returns>
    bool TryGetObjectFromNetId<T>(ushort netId, out T obj) where T : class;

    /// <summary>
    /// Registers a PaintTheme and returns its ID
    /// </summary>
    /// <param name="assetName">The string representing the `PaintTheme.AssetName`</param>
    /// <returns>Non-zero, unique Id if the theme was successfully registered, otherwise 0</returns>
    /// <remarks>PaintThemes must be registered each time the client or server starts, registration is not persistent across sessions.</remarks>
    uint RegisterPaintTheme(string assetName);

    /// <summary>
    /// Unregisters a PaintTheme
    /// </summary>
    /// <param name="themeId">The Id of the PaintTheme to be unregistered</param>
    void UnregisterPaintTheme(uint themeId);

    /// <summary>
    /// Registers a serialiser and deserialiser for a custom <see cref="DV.Logic.Job.Task"/> type for multiplayer synchronization.
    /// </summary>
    /// <typeparam name="TGameTask">The concrete <see cref="DV.Logic.Job.Task"/> type to register.</typeparam>
    /// <param name="taskType">The <see cref="DV.Logic.Job.TaskType"/> enum value</param>
    /// <param name="converter">
    /// A function that takes an instance of <typeparamref name="TGameTask"/> and returns a corresponding <see cref="TaskNetworkData"/> object
    /// for serialisation.
    /// </param>
    /// <param name="emptyCreator">
    /// A function that takes a <see cref="DV.Logic.Job.TaskType"/> and returns an empty <see cref="TaskNetworkData"/> instance for deserialisation.
    /// </param>
    /// <returns>
    /// <c>true</c> if the task type was successfully registered; <c>false</c> if the task type was already registered or registration failed.
    /// </returns>
    /// <remarks>
    /// This method allows the Multiplayer mod to correctly serialise and deserialise custom or extended task types.
    /// </remarks>
    bool RegisterTaskType<TGameTask>(TaskType taskType, Func<TGameTask, TaskNetworkData> converter, Func<TaskType, TaskNetworkData> emptyCreator)
        where TGameTask : Task;

    /// <summary>
    /// Unregisters a previously registered custom <see cref="DV.Logic.Job.Task"/> type.
    /// </summary>
    /// <typeparam name="TGameTask">The concrete <see cref="DV.Logic.Job.Task"/> type to unregister.</typeparam>
    /// <param name="taskType">The <see cref="TaskType"/> enum value associated with the task type to unregister.</param>
    /// <returns>
    /// <c>true</c> if the task type was successfully unregistered; <c>false</c> if the task type is a base game task type.
    /// </returns>
    /// <remarks>
    /// This method allows removal of custom or extended task types from the multiplayer system. 
    /// Base task types cannot be unregistered.
    /// </remarks>
    bool UnRegisterTaskType<TGameTask>(TaskType taskType) where TGameTask : Task;

}
