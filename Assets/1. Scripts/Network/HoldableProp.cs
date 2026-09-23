using Fusion;
using UnityEngine;

/// <summary>
/// Networked prop (e.g. test crate) that can be held and thrown.
/// Add to a NetworkObject with a Collider; Rigidbody recommended for throws.
/// While held, collision with the holder is ignored (world / others still collide).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class HoldableProp : NetworkBehaviour, IHoldable
{
    [SerializeField] private float _thrownMassScale = 1f;
    [SerializeField] private float _throwSeparation = 0.75f;

    [Tooltip("놓거나 던진 뒤 들고 있던 사람과의 충돌 무시를 유지하는 최대 시간.\n" +
             "겹침이 먼저 풀리면 그 즉시 충돌이 돌아옴. " +
             "이게 없으면 손 위치에서 캡슐과 겹친 채로 충돌이 켜져, 물체와 사람이 서로 튕겨 나감.")]
    [SerializeField] private float _releaseIgnoreMaxSeconds = 1f;

    private Rigidbody _rigidbody;
    private Collider[] _colliders;
    private bool _ignoringHolderCollision;
    private NetworkId _ignoredHolderId;
    private Collider[] _ignoredHolderColliders;

    // 휠 연동. 둘 다 없어도 동작함.
    private WheelRider _wheelRider;
    private WeightSource _weightSource;

    [Networked] private NetworkBool IsHeldNet { get; set; }
    [Networked] private NetworkId HeldById { get; set; }
    [Networked] private Vector3 NetPosition { get; set; }
    [Networked] private Quaternion NetRotation { get; set; }

    // 놓은 직후 겹침이 풀릴 때까지 충돌을 무시할 대상
    [Networked] private NetworkId LastHolderId { get; set; }
    [Networked] private TickTimer ReleaseIgnoreTimer { get; set; }

    public bool CanBeHeld => !IsHeldNet;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>();
        _wheelRider = GetComponent<WheelRider>();
        _weightSource = GetComponent<WeightSource>();
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
            transform.SetPositionAndRotation(NetPosition, NetRotation);
            return;
        }

        if (!IsHeldNet)
        {
            NetPosition = transform.position;
            NetRotation = transform.rotation;
        }
    }

    public override void Render()
    {
        SyncHolderCollisionIgnore(canWriteState: false);

        if (!Object.HasStateAuthority)
            transform.SetPositionAndRotation(NetPosition, NetRotation);
    }

    /// <summary>
    /// 들려 있으면 화면에 그리기 직전에 손 위치로 옮김.
    /// 플레이어는 Fusion이 틱 사이를 보간한 위치에 그려지는데, 물체는 틱 위치에 머물러 있으면
    /// 둘이 다른 박자로 그려져 손 위에서 물체가 떨림.
    /// Render 순서는 보장되지 않으므로 모든 Render가 끝난 LateUpdate에서 처리함.
    /// 다음 틱에서 들고 있는 쪽이 다시 틱 위치로 옮기므로 시뮬레이션에는 영향 없음.
    /// </summary>
    private void LateUpdate()
    {
        if (Object == null || !Object.IsValid || !IsHeldNet || !HeldById.IsValid || Runner == null)
            return;

        if (!Runner.TryFindObject(HeldById, out var holderObject))
            return;

        var holder = holderObject.GetComponent<Player>();
        if (holder == null || holder.HoldPoint == null)
            return;

        transform.SetPositionAndRotation(holder.HoldPoint.position, holder.HoldPoint.rotation);
    }

    public void SnapToHoldPoint(Transform holdPoint)
    {
        if (!Object.HasStateAuthority || holdPoint == null)
            return;

        transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
        NetPosition = transform.position;
        NetRotation = transform.rotation;

        // 틱 안의 실제 위치를 무게 계산에 알려줌. 틱 밖에서는 화면용 위치로 바뀌어 있음.
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
        ReleaseIgnoreTimer = TickTimer.None;
        SetPhysicsHeld(true);

        // 들린 동안에는 들고 있는 쪽 무게에 합산되도록 직접 연결함.
        if (_weightSource != null)
            _weightSource.SetSupportOverride(holder.GetComponent<WeightSource>());

        SyncHolderCollisionIgnore(canWriteState: true);
    }

    public void OnReleased()
    {
        if (!Object.HasStateAuthority)
            return;

        BeginReleaseIgnore();

        IsHeldNet = false;
        HeldById = default;
        SetPhysicsHeld(false);

        if (_weightSource != null)
            _weightSource.SetSupportOverride(null);

        SyncHolderCollisionIgnore(canWriteState: true);

        // 월드 기준으로 정지시키면 휠과 함께 달리던 플레이어에게 휠 속도로 부딪힘.
        SetReleaseVelocity(Vector3.zero);
    }

    public void OnThrown(Vector3 worldVelocity)
    {
        if (!Object.HasStateAuthority)
            return;

        BeginReleaseIgnore();
        ApplyThrowSeparation(worldVelocity);

        IsHeldNet = false;
        HeldById = default;
        SetPhysicsHeld(false);

        if (_weightSource != null)
            _weightSource.SetSupportOverride(null);

        SyncHolderCollisionIgnore(canWriteState: true);

        // 던지는 속도는 휠 속도 위에 더함. 그래야 달리는 휠 위에서 던져도 앞으로 날아감.
        SetReleaseVelocity(worldVelocity * _thrownMassScale);

        if (_rigidbody != null)
            _rigidbody.angularVelocity = Vector3.zero;

        NetPosition = transform.position;
        NetRotation = transform.rotation;
    }

    /// <summary>놓는 순간의 들고 있던 사람을 기억해 두고, 겹침이 풀릴 때까지 충돌을 계속 무시함.</summary>
    private void BeginReleaseIgnore()
    {
        LastHolderId = HeldById;
        ReleaseIgnoreTimer = TickTimer.CreateFromSeconds(Runner, _releaseIgnoreMaxSeconds);
    }

    /// <summary>
    /// 놓거나 던진 직후의 속도를 설정함.
    /// 휠 위라면 휠 속도를 기본으로 깔고 그 위에 추가 속도를 더함.
    /// </summary>
    private void SetReleaseVelocity(Vector3 extraVelocity)
    {
        if (_rigidbody == null || _rigidbody.isKinematic)
            return;

        if (_wheelRider != null)
            _wheelRider.InheritFrameVelocity(extraVelocity);
        else
            _rigidbody.linearVelocity = extraVelocity;
    }

    private void ApplyThrowSeparation(Vector3 worldVelocity)
    {
        var push = worldVelocity;
        push.y = 0f;
        if (push.sqrMagnitude < 0.0001f)
            return;

        transform.position += push.normalized * _throwSeparation;
    }

    private void SetPhysicsHeld(bool held)
    {
        if (_rigidbody == null)
            return;

        _rigidbody.isKinematic = held;
        _rigidbody.useGravity = !held;
    }

    /// <summary>
    /// 지금 충돌을 무시해야 할 대상을 구함.
    /// 들려 있으면 들고 있는 사람, 놓은 직후라면 겹침이 풀리기 전까지 직전에 들고 있던 사람.
    /// </summary>
    private NetworkId GetIgnoreTarget(bool canWriteState)
    {
        if (IsHeldNet && HeldById.IsValid)
            return HeldById;

        if (!LastHolderId.IsValid)
            return default;

        bool expired = ReleaseIgnoreTimer.ExpiredOrNotRunning(Runner);
        bool stillOverlapping = !expired && IsOverlapping(LastHolderId);

        if (stillOverlapping)
            return LastHolderId;

        // 겹침이 풀렸거나 시간이 다 됨. 상태 쓰기는 틱 안의 권한자만 가능함.
        if (canWriteState)
        {
            LastHolderId = default;
            ReleaseIgnoreTimer = TickTimer.None;
        }
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