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

    [Tooltip("���ų� ���� �� ��� �ִ� ������� �浹 ���ø� �����ϴ� �ִ� �ð�.\n" +
             "��ħ�� ���� Ǯ���� �� ��� �浹�� ���ƿ�. " +
             "�̰� ������ �� ��ġ���� ĸ���� ��ģ ä�� �浹�� ����, ��ü�� ����� ���� ƨ�� ����.")]
    [SerializeField] private float _releaseIgnoreMaxSeconds = 1f;

    private Rigidbody _rigidbody;
    private Collider[] _colliders;

    /// <summary>현재 홀더와 IgnoreCollision 중인지.</summary>
    private bool _ignoringHolderCollision;

    /// <summary>Ignore를 건 홀더 NetworkId (해제 시 사용).</summary>
    private NetworkId _ignoredHolderId;
    private Collider[] _ignoredHolderColliders;

    // �� ����. �� �� ��� ������.
    private WheelRider _wheelRider;
    private WeightSource _weightSource;

    [Networked] private NetworkBool IsHeldNet { get; set; }
    [Networked] private NetworkId HeldById { get; set; }

    /// <summary>프록시용 위치/회전 복제 상태.</summary>
    [Networked] private Vector3 NetPosition { get; set; }
    [Networked] private Quaternion NetRotation { get; set; }

    // ���� ���� ��ħ�� Ǯ�� ������ �浹�� ������ ���
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
        SyncHolderCollisionIgnore(canWriteState: false);

        if (!Object.HasStateAuthority)
            transform.SetPositionAndRotation(NetPosition, NetRotation);
    }

    /// <summary>
    /// ��� ������ ȭ�鿡 �׸��� ������ �� ��ġ�� �ű�.
    /// �÷��̾�� Fusion�� ƽ ���̸� ������ ��ġ�� �׷����µ�, ��ü�� ƽ ��ġ�� �ӹ��� ������
    /// ���� �ٸ� ���ڷ� �׷��� �� ������ ��ü�� ����.
    /// Render ������ ������� �����Ƿ� ��� Render�� ���� LateUpdate���� ó����.
    /// ���� ƽ���� ��� �ִ� ���� �ٽ� ƽ ��ġ�� �ű�Ƿ� �ùķ��̼ǿ��� ���� ����.
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

        // ƽ ���� ���� ��ġ�� ���� ��꿡 �˷���. ƽ �ۿ����� ȭ��� ��ġ�� �ٲ�� ����.
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

        // �鸰 ���ȿ��� ��� �ִ� �� ���Կ� �ջ�ǵ��� ���� ������.
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

        // ���� �������� ������Ű�� �ٰ� �Բ� �޸��� �÷��̾�� �� �ӵ��� �ε���.
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

        // ������ �ӵ��� �� �ӵ� ���� ����. �׷��� �޸��� �� ������ ������ ������ ���ư�.
        SetReleaseVelocity(worldVelocity * _thrownMassScale);

        if (_rigidbody != null)
            _rigidbody.angularVelocity = Vector3.zero;

        NetPosition = transform.position;
        NetRotation = transform.rotation;
    }

    /// <summary>���� ������ ��� �ִ� ����� ����� �ΰ�, ��ħ�� Ǯ�� ������ �浹�� ��� ������.</summary>
    private void BeginReleaseIgnore()
    {
        LastHolderId = HeldById;
        ReleaseIgnoreTimer = TickTimer.CreateFromSeconds(Runner, _releaseIgnoreMaxSeconds);
    }

    /// <summary>
    /// ���ų� ���� ������ �ӵ��� ������.
    /// �� ����� �� �ӵ��� �⺻���� ��� �� ���� �߰� �ӵ��� ����.
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

    /// <summary>잡힌 동안 kinematic, 놓이면 중력/물리 복구.</summary>
    private void SetPhysicsHeld(bool held)
    {
        if (_rigidbody == null)
            return;

        _rigidbody.isKinematic = held;
        _rigidbody.useGravity = !held;
    }

    /// <summary>
    /// ���� �浹�� �����ؾ� �� ����� ����.
    /// ��� ������ ��� �ִ� ���, ���� ���Ķ�� ��ħ�� Ǯ���� ������ ������ ��� �ִ� ���.
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

        // ��ħ�� Ǯ�Ȱų� �ð��� �� ��. ���� ����� ƽ ���� �����ڸ� ������.
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