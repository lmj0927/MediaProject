using Fusion;
using UnityEngine;

/// <summary>
/// 네트워크 상자 등 잡기 가능한 Prop.
/// free: Host WheelRider(판 목표 포즈 기반 속도 carry) + NetPosition.
/// Host 시각: _visualRoot(메시 자식)만 틱 사이 보간 — 루트 RB는 건드리지 않음.
/// Client 시각: RidingPlatform일 때 판 로컬 LateUpdate (parent 없음).
/// held: HoldPoint 스냅. NetworkTransform 없음.
/// _despawnBelowY 아래로 떨어지면 Host가 Despawn.
/// </summary>
[DefaultExecutionOrder(50)]
[RequireComponent(typeof(NetworkObject))]
public class HoldableProp : NetworkBehaviour, IHoldable, IAfterTick
{
    [Tooltip("던질 때 속도에 곱하는 배율")]
    [SerializeField] private float _thrownMassScale = 1f;

    [Tooltip("던질 때 홀더와 겹치지 않도록 앞으로 밀어내는 거리")]
    [SerializeField] private float _throwSeparation = 0.75f;

    [Tooltip("메시만 담은 자식(콜라이더 없음). Host에서 틱 사이 보간.")]
    [SerializeField] private Transform _visualRoot;

    [SerializeField] private float _visualSnapDistance = 2f;

    [Tooltip("이 월드 Y 아래로 떨어지면 Host가 Despawn")]
    [SerializeField] private float _despawnBelowY = -30f;

    private Rigidbody _rigidbody;
    private Collider[] _colliders;
    private TickVisualInterpolator _visual;

    private bool _ignoringHolderCollision;
    private NetworkId _ignoredHolderId;
    private Collider[] _ignoredHolderColliders;

    private WheelRider _wheelRider;
    private WeightSource _weightSource;

    [Networked] private NetworkBool IsHeldNet { get; set; }
    [Networked] private NetworkId HeldById { get; set; }

    [Networked] private Vector3 NetPosition { get; set; }
    [Networked] private Quaternion NetRotation { get; set; }

    [Networked] private NetworkId LastHolderId { get; set; }

    [Networked] private NetworkBool RidingPlatform { get; set; }
    [Networked] private NetworkId PlatformObjectId { get; set; }
    [Networked] private Vector3 PlatformLocalPosition { get; set; }

    public bool CanBeHeld => !IsHeldNet;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>();
        _wheelRider = GetComponent<WheelRider>();
        _weightSource = GetComponent<WeightSource>();
        _visual = new TickVisualInterpolator(transform, _visualRoot);
    }

    public override void Spawned()
    {
        NetPosition = transform.position;
        NetRotation = transform.rotation;
        SyncHolderCollisionIgnore(canWriteState: true);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ClearHolderCollisionIgnore();
    }

    public override void FixedUpdateNetwork()
    {
        SyncHolderCollisionIgnore(canWriteState: Object.HasStateAuthority);

        if (!Object.HasStateAuthority)
        {
            if (!IsHeldNet && !RidingPlatform)
                transform.SetPositionAndRotation(NetPosition, NetRotation);
            return;
        }

        if (IsHeldNet)
        {
            ClearPlatformRideState();
            return;
        }

        Vector3 bodyPosition = _rigidbody != null ? _rigidbody.position : transform.position;
        if (bodyPosition.y < _despawnBelowY)
        {
            Runner.Despawn(Object);
            return;
        }

        _wheelRider?.Simulate(Runner.DeltaTime);
        SyncPlatformRideState();

        NetPosition = _rigidbody != null ? _rigidbody.position : transform.position;
        NetRotation = _rigidbody != null ? _rigidbody.rotation : transform.rotation;

        if (_weightSource != null)
            _weightSource.SetSimulatedPosition(NetPosition);
    }

    void IAfterTick.AfterTick()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
            return;

        // held 중엔 손 위치로 계속 리셋 → 놓는 순간 손에서부터 이어서 보간
        if (IsHeldNet || _rigidbody == null)
            _visual.ResetTo(transform.position, transform.rotation);
        else
            _visual.Capture(_rigidbody.position, _rigidbody.rotation, _visualSnapDistance);
    }

    public override void Render()
    {
        SyncHolderCollisionIgnore(canWriteState: false);

        if (IsHeldNet)
            return;

        // Host RB는 FUN delta만. Client만 판 로컬 시각 보정 (Player와 동일 패턴).
        if (!Object.HasStateAuthority && !RidingPlatform)
            transform.SetPositionAndRotation(NetPosition, NetRotation);
    }

    public void SnapVisualToHoldPoint(Transform holdPoint)
    {
        if (holdPoint == null)
            return;

        transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
    }

    private void LateUpdate()
    {
        if (Object == null || !Object.IsValid)
            return;

        if (IsHeldNet)
        {
            _visual.RestoreLocal();
            SnapVisualToHolderHoldPoint();
            return;
        }

        // Host: 루트 월드 스냅은 RB 시뮬과 싸우므로 시각 자식만 보간.
        if (Object.HasStateAuthority)
        {
            _visual.Apply(Runner.LocalAlpha);
            return;
        }

        _visual.RestoreLocal();
        ApplyPlatformRenderCorrection();
    }

    private void SnapVisualToHolderHoldPoint()
    {
        if (!HeldById.IsValid || Runner == null)
            return;

        if (!Runner.TryFindObject(HeldById, out var holderObject))
            return;

        var holder = holderObject.GetComponent<Player>();
        if (holder == null || holder.HoldPoint == null)
            return;

        SnapVisualToHoldPoint(holder.HoldPoint);
    }

    private void ApplyPlatformRenderCorrection()
    {
        if (!RidingPlatform || !PlatformObjectId.IsValid || Runner == null)
            return;

        if (!Runner.TryFindObject(PlatformObjectId, out var platformObject))
            return;

        if (platformObject.TryGetComponent<PlatformTilt>(out var tilt))
        {
            tilt.GetRenderPose(out var platformPosition, out var platformRotation);
            transform.position = platformPosition + platformRotation * PlatformLocalPosition;
            return;
        }

        var platform = platformObject.transform;
        transform.position = platform.position + platform.rotation * PlatformLocalPosition;
    }

    private void SyncPlatformRideState()
    {
        if (_wheelRider == null ||
            !_wheelRider.TryGetRideLocal(out var platformObject, out var local))
        {
            ClearPlatformRideState();
            return;
        }

        RidingPlatform = true;
        PlatformObjectId = platformObject.Id;
        PlatformLocalPosition = local;
    }

    private void ClearPlatformRideState()
    {
        RidingPlatform = false;
        PlatformObjectId = default;
    }

    public void SnapToHoldPoint(Transform holdPoint)
    {
        if (!Object.HasStateAuthority || holdPoint == null)
            return;

        transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
        NetPosition = transform.position;
        NetRotation = transform.rotation;

        if (_weightSource != null)
            _weightSource.SetSimulatedPosition(transform.position);
    }

    public void OnPickedUp(NetworkObject holder)
    {
        if (!Object.HasStateAuthority)
            return;

        IsHeldNet = true;
        HeldById = holder.Id;
        LastHolderId = default;
        ClearPlatformRideState();
        _wheelRider?.NotifyPickedUp();
        SetPhysicsHeld(true);

        if (_weightSource != null)
            _weightSource.SetSupportOverride(holder.GetComponent<WeightSource>());

        SyncHolderCollisionIgnore(canWriteState: true);
    }

    public void OnReleased()
    {
        if (!Object.HasStateAuthority)
            return;

        BeginReleaseOverlapIgnore();

        IsHeldNet = false;
        HeldById = default;
        SetPhysicsHeld(false);

        if (_weightSource != null)
            _weightSource.SetSupportOverride(null);

        SyncHolderCollisionIgnore(canWriteState: true);

        if (_wheelRider != null)
            _wheelRider.SoftRelease();
        else if (_rigidbody != null && !_rigidbody.isKinematic)
            _rigidbody.linearVelocity = Vector3.zero;

        SyncPlatformRideState();
        NetPosition = transform.position;
        NetRotation = transform.rotation;
    }

    public void OnThrown(Vector3 worldVelocity)
    {
        if (!Object.HasStateAuthority)
            return;

        BeginReleaseOverlapIgnore();
        ApplyThrowSeparation(worldVelocity);

        IsHeldNet = false;
        HeldById = default;
        SetPhysicsHeld(false);

        if (_weightSource != null)
            _weightSource.SetSupportOverride(null);

        SyncHolderCollisionIgnore(canWriteState: true);

        if (_wheelRider != null)
            _wheelRider.BeginThrownFlight(worldVelocity * _thrownMassScale);
        else if (_rigidbody != null && !_rigidbody.isKinematic)
            _rigidbody.linearVelocity = worldVelocity * _thrownMassScale;

        if (_rigidbody != null)
            _rigidbody.angularVelocity = Vector3.zero;

        ClearPlatformRideState();
        NetPosition = transform.position;
        NetRotation = transform.rotation;
    }

    private void BeginReleaseOverlapIgnore()
    {
        LastHolderId = HeldById;
    }

    private void ApplyThrowSeparation(Vector3 worldVelocity)
    {
        var push = worldVelocity;
        push.y = 0f;
        if (push.sqrMagnitude < 0.0001f)
            return;

        transform.position += push.normalized * _throwSeparation;
        if (_rigidbody != null)
            _rigidbody.position = transform.position;
    }

    private void SetPhysicsHeld(bool held)
    {
        if (_rigidbody == null)
            return;

        _rigidbody.isKinematic = held;
        _rigidbody.useGravity = !held;
    }

    private NetworkId GetIgnoreTarget(bool canWriteState)
    {
        if (IsHeldNet && HeldById.IsValid)
            return HeldById;

        if (!LastHolderId.IsValid)
            return default;

        if (IsOverlapping(LastHolderId))
            return LastHolderId;

        if (canWriteState)
            LastHolderId = default;
        return default;
    }

    private bool IsOverlapping(NetworkId holderId)
    {
        if (Runner == null || !Runner.TryFindObject(holderId, out var holderObject))
            return false;

        var holderColliders = _ignoringHolderCollision && _ignoredHolderId == holderId && _ignoredHolderColliders != null
            ? _ignoredHolderColliders
            : holderObject.GetComponentsInChildren<Collider>();

        foreach (var mine in _colliders)
        {
            if (mine == null || mine.isTrigger) continue;
            foreach (var theirs in holderColliders)
            {
                if (theirs == null || theirs.isTrigger) continue;
                if (mine.bounds.Intersects(theirs.bounds)) return true;
            }
        }
        return false;
    }

    private void SyncHolderCollisionIgnore(bool canWriteState)
    {
        var target = GetIgnoreTarget(canWriteState);

        if (target.IsValid)
        {
            if (_ignoringHolderCollision && _ignoredHolderId == target)
                return;

            ClearHolderCollisionIgnore();

            if (Runner == null || !Runner.TryFindObject(target, out var holderObject))
                return;

            HoldCollisionUtility.SetIgnoreCollisions(_colliders, holderObject.gameObject, ignore: true);
            _ignoringHolderCollision = true;
            _ignoredHolderId = target;
            _ignoredHolderColliders = holderObject.GetComponentsInChildren<Collider>();
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
        _ignoredHolderColliders = null;
    }
}
