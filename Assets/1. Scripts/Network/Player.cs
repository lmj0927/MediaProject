using Fusion;
using UnityEngine;

public class Player : NetworkBehaviour, IHoldable
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int IsHoldingHash = Animator.StringToHash("IsHolding");
    private static readonly int ThrowHash = Animator.StringToHash("Throw");

    [Header("Hold")]
    [SerializeField] private Transform _holdPoint;
    [SerializeField] private float _holdRadius = 1.2f;
    [SerializeField] private LayerMask _holdMask = ~0;
    [SerializeField] private float _throwForce = 8f;
    [SerializeField] private float _throwUpForce = 3f;
    [SerializeField] private float _throwSeparation = 0.75f;

    private NetworkCharacterController _cc;
    private CharacterController _unityCc;
    private Collider[] _colliders;
    private Animator _animator;
    private NetworkButtons _previousButtons;
    private bool _wasGrounded = true;
    private bool _wasHolding;
    private int _shownJumpCount;
    private int _shownThrowCount;
    private bool _ignoringHolderCollision;
    private NetworkId _ignoredHolderId;

    // 휠 연동. 둘 다 없어도 동작함(휠이 없는 씬 등).
    private WheelCarrier _carrier;
    private WeightSource _weightSource;

    [Networked] private int JumpCount { get; set; }
    [Networked] private int ThrowCount { get; set; }
    [Networked] private NetworkId HeldObjectId { get; set; }
    [Networked] private NetworkBool IsHeldNet { get; set; }
    [Networked] private NetworkId HeldById { get; set; }

    public Transform HoldPoint => _holdPoint;
    public bool CanBeHeld => !IsHeldNet;
    public bool IsHolding => HeldObjectId.IsValid;

    private void Awake()
    {
        _cc = GetComponent<NetworkCharacterController>();
        _unityCc = GetComponent<CharacterController>();
        _colliders = GetComponentsInChildren<Collider>();
        _animator = GetComponentInChildren<Animator>();
        _carrier = GetComponent<WheelCarrier>();
        _weightSource = GetComponent<WeightSource>();
        EnsureHoldPoint();
    }

    public override void Spawned()
    {
        EnsureHoldPoint();
        _shownJumpCount = JumpCount;
        _shownThrowCount = ThrowCount;
        _wasGrounded = _cc != null && _cc.Grounded;
        _wasHolding = IsHolding;
        SyncHeldControllerAndCollision();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ClearHolderCollisionIgnore();
    }

    public override void FixedUpdateNetwork()
    {
        SyncHeldControllerAndCollision();

        // Held on any peer: skip input/Move. Holder teleports us; CC is disabled while held.
        if (IsHeldNet)
            return;

        if (!GetInput(out NetworkInputData data))
        {
            if (Object.HasStateAuthority)
                UpdateCarriedObject();
            return;
        }

        var pressed = data.buttons.GetPressed(_previousButtons);
        _previousButtons = data.buttons;

        if (Object.HasStateAuthority)
        {
            if (pressed.IsSet(PlayerInputButton.Hold))
                HandleHoldPressed();

            if (pressed.IsSet(PlayerInputButton.Throw))
                HandleThrowPressed();
        }

        var direction = data.direction;
        direction.y = 0f;

        if (pressed.IsSet(PlayerInputButton.Jump) && _cc.Grounded)
        {
            _cc.Jump();
            JumpCount++;
        }

        // 휠에 실려 가는 이동을 자체 이동보다 먼저 적용함.
        // NetworkCharacterController는 Move 전후 위치 차이로 속도를 계산하므로,
        // 먼저 끝내 두면 애니메이터 Speed에 휠 이동이 섞이지 않음.
        ApplyWheelCarry();

        _cc.Move(direction);

        // 이번 틱의 최종 위치를 기록함. 다음 틱은 이 지점이 판과 함께 어디로 갔는지를 기준으로 옮김.
        if (_carrier != null)
            _carrier.EndTick(transform.position);

        // 틱 안의 실제 위치를 무게 계산에 알려줌.
        // 틱 밖에서는 transform이 화면용 보간 위치라 무게중심이 흔들림.
        if (_weightSource != null)
            _weightSource.SetSimulatedPosition(transform.position);

        if (Object.HasStateAuthority)
            UpdateCarriedObject();
    }

    public override void Render()
    {
        SyncHeldControllerAndCollision();
        UpdateAnimator();
    }

    /// <summary>
    /// 휠 위에 있으면 휠과 함께 옮김.
    /// CharacterController는 마찰로 끌려가지 않으므로 직접 옮겨야 함.
    /// 착지 중이고 점프 중이 아니면 바닥 쪽으로 살짝 눌러 착지 판정이 끊기지 않게 함.
    /// </summary>
    private void ApplyWheelCarry()
    {
        if (_carrier == null || _unityCc == null || !_unityCc.enabled)
            return;

        bool snap = _cc.Grounded && _cc.Velocity.y <= 0f;
        var delta = _carrier.GetCarryDelta(transform.position, _cc.Grounded, snap);
        if (delta.sqrMagnitude > 0f)
            _unityCc.Move(delta);
    }

    public void SnapToHoldPoint(Transform holdPoint)
    {
        if (!Object.HasStateAuthority || holdPoint == null || _cc == null)
            return;

        _cc.Teleport(holdPoint.position, holdPoint.rotation);
    }

    /// <summary>
    /// Snaps this player's held object (and nested carries) to HoldPoint.
    /// </summary>
    public void UpdateCarriedObject()
    {
        if (!Object.HasStateAuthority || !HeldObjectId.IsValid || _holdPoint == null)
            return;

        if (!Runner.TryFindObject(HeldObjectId, out var heldObject))
        {
            HeldObjectId = default;
            return;
        }

        var heldPlayer = heldObject.GetComponent<Player>();
        if (heldPlayer != null)
        {
            heldPlayer.SnapToHoldPoint(_holdPoint);
            heldPlayer.UpdateCarriedObject();
            return;
        }

        var heldProp = heldObject.GetComponent<HoldableProp>();
        heldProp?.SnapToHoldPoint(_holdPoint);
    }

    public void OnPickedUp(NetworkObject holder)
    {
        if (!Object.HasStateAuthority)
            return;

        IsHeldNet = true;
        HeldById = holder.Id;

        // 들린 동안에는 발밑 검사로 지지 대상을 찾을 수 없으므로,
        // 들고 있는 쪽 무게에 합산되도록 직접 연결함.
        if (_weightSource != null)
            _weightSource.SetSupportOverride(holder.GetComponent<WeightSource>());

        SyncHeldControllerAndCollision();
    }

    public void OnReleased()
    {
        if (!Object.HasStateAuthority)
            return;

        IsHeldNet = false;
        HeldById = default;

        if (_weightSource != null)
            _weightSource.SetSupportOverride(null);

        SyncHeldControllerAndCollision();

        if (_cc != null)
            _cc.Velocity = Vector3.zero;
    }

    public void OnThrown(Vector3 worldVelocity)
    {
        if (!Object.HasStateAuthority)
            return;

        ApplyThrowSeparation(worldVelocity);

        IsHeldNet = false;
        HeldById = default;

        if (_weightSource != null)
            _weightSource.SetSupportOverride(null);

        SyncHeldControllerAndCollision();

        if (_cc != null)
            _cc.Velocity = worldVelocity;
    }

    private void ApplyThrowSeparation(Vector3 worldVelocity)
    {
        var push = worldVelocity;
        push.y = 0f;
        if (push.sqrMagnitude < 0.0001f || _cc == null)
            return;

        var separated = transform.position + push.normalized * _throwSeparation;
        _cc.Teleport(separated, transform.rotation);
    }

    private void SyncHeldControllerAndCollision()
    {
        if (_unityCc != null)
            _unityCc.enabled = !IsHeldNet;

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

    private void HandleHoldPressed()
    {
        if (IsHolding)
        {
            ReleaseHeld(Vector3.zero, thrown: false);
            return;
        }

        if (!_cc.Grounded)
            return;

        var target = FindBestHoldable();
        if (target == null)
            return;

        HeldObjectId = target.Object.Id;
        target.OnPickedUp(Object);
    }

    private void HandleThrowPressed()
    {
        if (!IsHolding)
            return;

        var velocity = transform.forward * _throwForce + Vector3.up * _throwUpForce;
        ReleaseHeld(velocity, thrown: true);
        ThrowCount++;
    }

    private void ReleaseHeld(Vector3 throwVelocity, bool thrown)
    {
        if (!HeldObjectId.IsValid)
            return;

        if (Runner.TryFindObject(HeldObjectId, out var heldObject))
        {
            var holdable = heldObject.GetComponent<IHoldable>();
            if (holdable != null)
            {
                if (thrown)
                    holdable.OnThrown(throwVelocity);
                else
                    holdable.OnReleased();
            }
        }

        HeldObjectId = default;
    }

    private IHoldable FindBestHoldable()
    {
        var hits = Physics.OverlapSphere(transform.position, _holdRadius, _holdMask, QueryTriggerInteraction.Ignore);
        IHoldable best = null;
        var bestFacing = float.NegativeInfinity;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < hits.Length; i++)
        {
            var holdable = hits[i].GetComponentInParent<IHoldable>();
            if (holdable == null || !holdable.CanBeHeld)
                continue;

            if (holdable.Object == null || holdable.Object == Object)
                continue;

            var toTarget = holdable.Object.transform.position - transform.position;
            toTarget.y = 0f;
            var distance = toTarget.magnitude;
            var facing = distance > 0.001f
                ? Vector3.Dot(transform.forward, toTarget.normalized)
                : 1f;

            if (facing > bestFacing + 0.001f ||
                (Mathf.Abs(facing - bestFacing) <= 0.001f && distance < bestDistance))
            {
                best = holdable;
                bestFacing = facing;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void EnsureHoldPoint()
    {
        if (_holdPoint != null)
            return;

        var existing = transform.Find("HoldPoint");
        if (existing != null)
        {
            _holdPoint = existing;
            return;
        }

        var holdPointObject = new GameObject("HoldPoint");
        holdPointObject.transform.SetParent(transform, worldPositionStays: false);
        holdPointObject.transform.localPosition = new Vector3(0f, 1.2f, 0.6f);
        holdPointObject.transform.localRotation = Quaternion.identity;
        _holdPoint = holdPointObject.transform;
    }

    private void UpdateAnimator()
    {
        if (_animator == null || _cc == null)
            return;

        var velocity = _cc.Velocity;
        velocity.y = 0f;
        _animator.SetFloat(SpeedHash, velocity.magnitude);
        _animator.SetBool(GroundedHash, _cc.Grounded);

        var throwFired = ThrowCount != _shownThrowCount;
        if (throwFired)
        {
            _shownThrowCount = ThrowCount;
            // Keep IsHolding true this frame so Holding/HoldRun → Throw can consume the trigger
            // before !IsHolding exits to Idle/Running (which would leave Throw queued).
            _animator.SetBool(IsHoldingHash, true);
            _animator.ResetTrigger(ThrowHash);
            _animator.SetTrigger(ThrowHash);
        }
        else
        {
            _animator.SetBool(IsHoldingHash, IsHolding);
        }

        // Clear any leftover Throw when starting a new grab.
        if (IsHolding && !_wasHolding)
            _animator.ResetTrigger(ThrowHash);

        if (JumpCount != _shownJumpCount)
        {
            _shownJumpCount = JumpCount;
            _animator.ResetTrigger(JumpHash);
            _animator.SetTrigger(JumpHash);
        }
        else if (_cc.Grounded && !_wasGrounded)
        {
            _animator.ResetTrigger(JumpHash);
        }

        _wasGrounded = _cc.Grounded;
        _wasHolding = IsHolding;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, _holdRadius);
    }
#endif
}