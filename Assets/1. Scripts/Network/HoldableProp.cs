using Fusion;
using UnityEngine;

/// <summary>
/// 네트워크 상자 등 잡기 가능한 Prop.
/// free: Host WheelRider(판 목표 포즈 기반 속도 carry) + NetPosition.
/// Host 시각: _visualRoot(메시 자식)만 틱 사이 보간 — 루트 RB는 건드리지 않음.
/// Client 시각: 스냅샷(from/to) 보간한 Net 포즈 / 판 로컬 좌표. RidingPlatform이면 판 Render 포즈 + 로컬로 LateUpdate (parent 없음).
/// held: HoldPoint 스냅. NetworkTransform 없음.
/// _despawnBelowY 아래로 떨어지면 Host가 Despawn.
/// </summary>
[DefaultExecutionOrder(50)]
[RequireComponent(typeof(NetworkObject))]
public class HoldableProp : NetworkBehaviour, IHoldable, ILaunchable, IAfterTick
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

    private PropertyReader<Vector3> _netPositionReader;
    private PropertyReader<Quaternion> _netRotationReader;
    private PropertyReader<NetworkBool> _ridingReader;
    private PropertyReader<NetworkId> _platformIdReader;
    private PropertyReader<Vector3> _platformLocalReader;

    // Client Render에서 샘플한 표시 포즈 (LateUpdate 판 보정이 같은 값을 씀)
    private bool _renderRiding;
    private NetworkId _renderPlatformId;
    private Vector3 _renderLocal;

    [Networked] private NetworkBool IsHeldNet { get; set; }
    [Networked] private NetworkId HeldById { get; set; }

    [Networked] private Vector3 NetPosition { get; set; }
    [Networked] private Quaternion NetRotation { get; set; }

    [Networked] private NetworkId LastHolderId { get; set; }

    [Networked] private NetworkBool RidingPlatform { get; set; }
    [Networked] private NetworkId PlatformObjectId { get; set; }
    [Networked] private Vector3 PlatformLocalPosition { get; set; }

    public bool CanBeHeld => !IsHeldNet;
    public bool IsHeld => IsHeldNet;

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

        _netPositionReader = GetPropertyReader<Vector3>(nameof(NetPosition));
        _netRotationReader = GetPropertyReader<Quaternion>(nameof(NetRotation));
        _ridingReader = GetPropertyReader<NetworkBool>(nameof(RidingPlatform));
        _platformIdReader = GetPropertyReader<NetworkId>(nameof(PlatformObjectId));
        _platformLocalReader = GetPropertyReader<Vector3>(nameof(PlatformLocalPosition));
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

        // Host RB는 FUN delta만. Client만 보간 포즈 적용 (판 위면 위치는 LateUpdate 판 보정).
        if (IsHeldNet || Object.HasStateAuthority)
            return;

        SampleClientPose(out var position, out var rotation);
        if (_renderRiding)
            transform.rotation = rotation;
        else
            transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>
    /// Client 표시용 포즈를 from/to 스냅샷 사이에서 보간 (구체 NT·판 Render와 같은 시간대).
    /// 판 탑승이 끊기거나 판이 바뀐 구간은 로컬 좌표를 보간하지 않고 to 값을 씀.
    /// 스냅샷이 없으면 최신 복제 값.
    /// </summary>
    private void SampleClientPose(out Vector3 position, out Quaternion rotation)
    {
        position = NetPosition;
        rotation = NetRotation;
        _renderRiding = RidingPlatform;
        _renderPlatformId = PlatformObjectId;
        _renderLocal = PlatformLocalPosition;

        if (!TryGetSnapshotsBuffers(out var from, out var to, out float alpha))
            return;

        var (fromPosition, toPosition) = _netPositionReader.Read(from, to);
        var (fromRotation, toRotation) = _netRotationReader.Read(from, to);
        var (fromRiding, toRiding) = _ridingReader.Read(from, to);
        var (fromPlatform, toPlatform) = _platformIdReader.Read(from, to);
        var (fromLocal, toLocal) = _platformLocalReader.Read(from, to);

        position = LerpOrSnap(fromPosition, toPosition, alpha);
        rotation = SlerpOrSnap(fromRotation, toRotation, alpha);

        _renderRiding = toRiding;
        _renderPlatformId = toPlatform;
        _renderLocal = fromRiding && toRiding && fromPlatform == toPlatform
            ? LerpOrSnap(fromLocal, toLocal, alpha)
            : toLocal;
    }

    private Vector3 LerpOrSnap(Vector3 from, Vector3 to, float alpha) =>
        (to - from).sqrMagnitude > _visualSnapDistance * _visualSnapDistance
            ? to
            : Vector3.LerpUnclamped(from, to, alpha);

    /// <summary>초기화 전 스냅샷(0,0,0,0)이 섞이면 Slerp가 NaN이 되므로 to로 끊음.</summary>
    private static Quaternion SlerpOrSnap(Quaternion from, Quaternion to, float alpha)
    {
        if (Quaternion.Dot(from, from) < 0.5f)
            return to;
        if (Quaternion.Dot(to, to) < 0.5f)
            return from;
        return Quaternion.Slerp(from, to, alpha);
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

    /// <summary>Client: Render에서 샘플한 보간 로컬 좌표를 판 Render 포즈에 붙임.</summary>
    private void ApplyPlatformRenderCorrection()
    {
        if (!_renderRiding || !_renderPlatformId.IsValid || Runner == null)
            return;

        if (!Runner.TryFindObject(_renderPlatformId, out var platformObject))
            return;

        if (platformObject.TryGetComponent<PlatformTilt>(out var tilt))
        {
            tilt.GetRenderPose(out var platformPosition, out var platformRotation);
            transform.position = platformPosition + platformRotation * _renderLocal;
            return;
        }

        var platform = platformObject.transform;
        transform.position = platform.position + platform.rotation * _renderLocal;
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

    /// <summary>
    /// 폭발 등 외부 충격. 들려 있으면 홀더 손에서 풀린 뒤 날아간다.
    /// 판 위 착지 carry가 수평 속도를 덮어쓰지 않도록 던지기 비행으로 전환.
    /// </summary>
    public void Launch(Vector3 worldVelocity)
    {
        if (!Object.HasStateAuthority)
            return;

        if (IsHeldNet)
            ReleaseFromHolder();

        if (_wheelRider != null)
            _wheelRider.BeginThrownFlight(worldVelocity);
        else if (_rigidbody != null && !_rigidbody.isKinematic)
            _rigidbody.linearVelocity = worldVelocity;

        if (_rigidbody != null && !_rigidbody.isKinematic)
            _rigidbody.angularVelocity = Vector3.zero;

        ClearPlatformRideState();
        NetPosition = transform.position;
        NetRotation = transform.rotation;
    }

    /// <summary>홀더가 놓치게 함. 홀더를 못 찾으면 스스로 놓인 상태로 전환.</summary>
    private void ReleaseFromHolder()
    {
        if (HeldById.IsValid && Runner.TryFindObject(HeldById, out var holderObject) &&
            holderObject.TryGetComponent<Player>(out var holder))
        {
            holder.DropHeld();
        }

        if (IsHeldNet)
            OnReleased();
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
