using Fusion;
using UnityEngine;

/// <summary>
/// 네트워크 상자 등 잡기 가능한 Prop.
/// NetworkObject + Collider 필요. Rigidbody가 있으면 던지기에 사용.
/// 잡혀 있는 동안 홀더와의 충돌만 무시한다.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class HoldableProp : NetworkBehaviour, IHoldable
{
    [Tooltip("던질 때 속도에 곱하는 배율")]
    [SerializeField] private float _thrownMassScale = 1f;

    [Tooltip("던질 때 홀더와 겹치지 않도록 앞으로 밀어내는 거리")]
    [SerializeField] private float _throwSeparation = 0.75f;

    private Rigidbody _rigidbody;
    private Collider[] _colliders;

    /// <summary>현재 홀더와 IgnoreCollision 중인지.</summary>
    private bool _ignoringHolderCollision;

    /// <summary>Ignore를 건 홀더 NetworkId (해제 시 사용).</summary>
    private NetworkId _ignoredHolderId;

    [Networked] private NetworkBool IsHeldNet { get; set; }
    [Networked] private NetworkId HeldById { get; set; }

    /// <summary>프록시용 위치/회전 복제 상태.</summary>
    [Networked] private Vector3 NetPosition { get; set; }
    [Networked] private Quaternion NetRotation { get; set; }

    public bool CanBeHeld => !IsHeldNet;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>();
    }

    public override void Spawned()
    {
        NetPosition = transform.position;
        NetRotation = transform.rotation;
        SyncHolderCollisionIgnore();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ClearHolderCollisionIgnore();
    }

    public override void FixedUpdateNetwork()
    {
        SyncHolderCollisionIgnore();

        // 프록시는 네트워크 포즈만 적용
        if (!Object.HasStateAuthority)
        {
            transform.SetPositionAndRotation(NetPosition, NetRotation);
            return;
        }

        // 잡혀 있지 않을 때만 물리 결과를 네트워크 포즈에 기록
        // (잡혀 있으면 Player.SnapToHoldPoint가 NetPosition을 갱신)
        if (!IsHeldNet)
        {
            NetPosition = transform.position;
            NetRotation = transform.rotation;
        }
    }

    public override void Render()
    {
        SyncHolderCollisionIgnore();

        if (!Object.HasStateAuthority)
            transform.SetPositionAndRotation(NetPosition, NetRotation);
    }

    /// <summary>홀더 HoldPoint에 붙인다 (State Authority만).</summary>
    public void SnapToHoldPoint(Transform holdPoint)
    {
        if (!Object.HasStateAuthority || holdPoint == null)
            return;

        transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
        NetPosition = transform.position;
        NetRotation = transform.rotation;
    }

    public void OnPickedUp(NetworkObject holder)
    {
        if (!Object.HasStateAuthority)
            return;

        IsHeldNet = true;
        HeldById = holder.Id;
        SetPhysicsHeld(true);
        SyncHolderCollisionIgnore();
    }

    public void OnReleased()
    {
        if (!Object.HasStateAuthority)
            return;

        IsHeldNet = false;
        HeldById = default;
        SetPhysicsHeld(false);
        SyncHolderCollisionIgnore();
        if (_rigidbody != null)
            _rigidbody.linearVelocity = Vector3.zero;
    }

    public void OnThrown(Vector3 worldVelocity)
    {
        if (!Object.HasStateAuthority)
            return;

        ApplyThrowSeparation(worldVelocity);

        IsHeldNet = false;
        HeldById = default;
        SetPhysicsHeld(false);
        SyncHolderCollisionIgnore();

        if (_rigidbody != null)
        {
            _rigidbody.linearVelocity = worldVelocity * _thrownMassScale;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        NetPosition = transform.position;
        NetRotation = transform.rotation;
    }

    /// <summary>던지는 방향으로 살짝 밀어 홀더와 즉시 재충돌하는 것을 줄인다.</summary>
    private void ApplyThrowSeparation(Vector3 worldVelocity)
    {
        var push = worldVelocity;
        push.y = 0f;
        if (push.sqrMagnitude < 0.0001f)
            return;

        transform.position += push.normalized * _throwSeparation;
    }

    /// <summary>잡힌 동안 kinematic, 놓이면 중력/물리 복구.</summary>
    private void SetPhysicsHeld(bool held)
    {
        if (_rigidbody == null)
            return;

        _rigidbody.isKinematic = held;
        _rigidbody.useGravity = !held;
    }

    /// <summary>IsHeldNet에 맞춰 홀더와의 IgnoreCollision을 켜거나 끈다.</summary>
    private void SyncHolderCollisionIgnore()
    {
        if (IsHeldNet && HeldById.IsValid)
        {
            if (_ignoringHolderCollision && _ignoredHolderId == HeldById)
                return;

            ClearHolderCollisionIgnore();

            if (Runner == null || !Runner.TryFindObject(HeldById, out var holderObject))
                return;

            HoldCollisionUtility.SetIgnoreCollisions(_colliders, holderObject.gameObject, ignore: true);
            _ignoringHolderCollision = true;
            _ignoredHolderId = HeldById;
            return;
        }

        ClearHolderCollisionIgnore();
    }

    private void ClearHolderCollisionIgnore()
    {
        if (!_ignoringHolderCollision)
            return;

        if (_ignoredHolderId.IsValid && Runner != null &&
            Runner.TryFindObject(_ignoredHolderId, out var holderObject))
        {
            HoldCollisionUtility.SetIgnoreCollisions(_colliders, holderObject.gameObject, ignore: false);
        }

        _ignoringHolderCollision = false;
        _ignoredHolderId = default;
    }
}
