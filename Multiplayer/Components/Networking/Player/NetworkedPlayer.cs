using Multiplayer.Components.Networking.Train;
using Multiplayer.Editor.Components.Player;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

public class NetworkedPlayer : MonoBehaviour
{
    private const float LERP_SPEED = 5.0f;

    public byte PlayerId { get; set; }

    private AnimationHandler animationHandler;
    private NameTag nameTag;
    private int ping;

    private string username;

    public string Username
    {
        get => username;
        set
        {
            username = value;
            nameTag.SetUsername(value);
        }
    }

    internal bool IsOnCar { get; private set; }
    internal TrainCar OccupiedCar { get; private set; }

    private Transform selfTransform;
    private Vector3 targetPos;
    private Quaternion targetRotation;
    private Vector2 moveDir;
    private Vector2 targetMoveDir;

    protected void Awake()
    {
        animationHandler = GetComponent<AnimationHandler>();

        nameTag = GetComponent<NameTag>();
        nameTag.LookTarget = PlayerManager.ActiveCamera.transform;
        PlayerManager.CameraChanged += () => nameTag.LookTarget = PlayerManager.ActiveCamera.transform;

        OnSettingsUpdated(Multiplayer.Settings);
        Settings.OnSettingsUpdated += OnSettingsUpdated;

        selfTransform = transform;
        targetPos = selfTransform.position;
        targetRotation = selfTransform.rotation;
        moveDir = Vector2.zero;
        targetMoveDir = Vector2.zero;
    }

    private void OnSettingsUpdated(Settings settings)
    {
        nameTag.ShowUsername(settings.ShowNameTags);
        nameTag.ShowPing(settings.ShowNameTags && settings.ShowPingInNameTags);
    }

    public void SetPing(int ping)
    {
        nameTag.SetPing(ping);
        this.ping = ping;
    }

    public int GetPing()
    {
        return ping;
    }

    protected void Update()
    {
        float t = Time.deltaTime * LERP_SPEED;

        Vector3 position = Vector3.Lerp(IsOnCar ? selfTransform.localPosition : selfTransform.position, IsOnCar ? targetPos : targetPos + WorldMover.currentMove, t);
        
        moveDir = Vector2.Lerp(moveDir, targetMoveDir, t);
        animationHandler.SetMoveDir(moveDir);

        if (IsOnCar && OccupiedCar != null)
        {
            selfTransform.localPosition = position;

            // Calculate a world-up-respecting rotation
            // This creates a rotation where Y points up in world space
            // but the forward direction aligns with the car's forward projected onto the horizontal plane
            Vector3 carForward = OccupiedCar.transform.forward;
            Vector3 worldUp = Vector3.up;

            // Project car's forward onto the horizontal plane
            Vector3 horizontalForward = Vector3.ProjectOnPlane(carForward, worldUp).normalized;
            if (horizontalForward.sqrMagnitude < 0.001f)
                horizontalForward = Vector3.ProjectOnPlane(OccupiedCar.transform.right, worldUp).normalized;

            // Create base orientation aligned with world up but facing car's forward direction
            Quaternion baseRotation = Quaternion.LookRotation(horizontalForward, worldUp);

            // Apply the desired Y rotation (player's facing direction) on top of this base rotation
            Quaternion targetWorldRotation = baseRotation * Quaternion.Euler(0, targetRotation.eulerAngles.y, 0);

            // Apply rotation in world space despite being a child transform
            selfTransform.rotation = Quaternion.Lerp(selfTransform.rotation, targetWorldRotation, t);
        }
        else
        {
            selfTransform.position = position;
            selfTransform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, t);
        }
    }

    public void UpdatePosition(Vector3 position, Vector2 moveDir, float rotationY, bool isJumping, bool movePacketIsOnCar)
    {
        targetPos = position;
        targetMoveDir = moveDir;

        animationHandler.SetIsJumping(isJumping);

        if (IsOnCar != movePacketIsOnCar)
            return;

        targetRotation = Quaternion.Euler(0, rotationY, 0);
    }

    public void UpdateCar(ushort netId)
    {
        IsOnCar = NetworkedTrainCar.TryGet(netId, out TrainCar trainCar);
        OccupiedCar = trainCar;

        if (IsOnCar)
            selfTransform.SetParent(OccupiedCar.transform, true);
        else
            selfTransform.SetParent(null, true);
    }
}
