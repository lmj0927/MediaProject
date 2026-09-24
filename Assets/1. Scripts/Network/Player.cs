using Fusion;
using UnityEngine;

/// <summary>
/// Host 권한 플레이어: 이동/점프/Stamp, 잡기·들기·던지기, 애니 파라미터 구동.
/// </summary>
public class Player : NetworkBehaviour, IHoldable
{
    // Animator 파라미터 해시 (문자열 조회 비용 절약)
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int IsHoldingHash = Animator.StringToHash("IsHolding");
    private static readonly int ThrowHash = Animator.StringToHash("Throw");
    private static readonly int StampHash = Animator.StringToHash("Stamp");

    [Header("Hold")]
    [Tooltip("잡은 물체를 붙일 위치 (없으면 런타임 생성)")]
    [SerializeField] private Transform _holdPoint;

    [Tooltip("잡기 탐지 구 반경")]
    [SerializeField] private float _holdRadius = 1.2f;

    [SerializeField] private LayerMask _holdMask = ~0;

    [SerializeField] private float _throwForce = 8f;
    [SerializeField] private float _throwUpForce = 3f;

    [Tooltip("던질 때 대상/자신을 앞으로 밀어 홀더와 겹침을 줄이는 거리")]
    [SerializeField] private float _throwSeparation = 0.75f;

    [Header("Stamp")]
    [Tooltip("Stamp 중 아래 속도 크기 (임시 튜닝값)")]
    [SerializeField] private float _stampDownSpeed = 25f;

    [Tooltip("자기 캐릭터 화면에서만 표시할 이름표")]
    [SerializeField] private GameObject _nameTag;

    private NetworkCharacterController _cc;
    private CharacterController _unityCc;
    private Collider[] _colliders;
    private Animator _animator;

    /// <summary>버튼 엣지(누른 순간) 판별용 이전 틱 버튼 상태.</summary>
    private NetworkButtons _previousButtons;

    private bool _wasGrounded = true;
    private bool _wasHolding;

    /// <summary>로컬에서 이미 재생한 Jump/Throw/Stamp 카운트 (트리거 중복 방지).</summary>
    private int _shownJumpCount;
    private int _shownThrowCount;
    private int _shownStampCount;

    private bool _ignoringHolderCollision;
    private NetworkId _ignoredHolderId;

    /// <summary>점프 성공 시 증가. 원격도 Render에서 Trigger 재생.</summary>
    [Networked] private int JumpCount { get; set; }

    /// <summary>던지기 성공 시 증가.</summary>
    [Networked] private int ThrowCount { get; set; }

    /// <summary>Stamp 성공 시 증가.</summary>
    [Networked] private int StampCount { get; set; }

    /// <summary>공중 Stamp 중(착지까지). Host 시뮬 상태.</summary>
    [Networked] private NetworkBool IsStamping { get; set; }

    /// <summary>현재 들고 있는 오브젝트. 유효하지 않으면 비어 있음.</summary>
    [Networked] private NetworkId HeldObjectId { get; set; }

    /// <summary>이 플레이어가 누군가에게 잡혀 있는지.</summary>
    [Networked] private NetworkBool IsHeldNet { get; set; }

    /// <summary>나를 잡고 있는 홀더 NetworkId.</summary>
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
        EnsureHoldPoint();
    }

    public override void Spawned()
    {
        EnsureHoldPoint();
        _shownJumpCount = JumpCount;
        _shownThrowCount = ThrowCount;
        _shownStampCount = StampCount;
        _wasGrounded = _cc != null && _cc.Grounded;
        _wasHolding = IsHolding;
        SyncHeldControllerAndCollision();
        UpdateNameTagVisibility();
    }

    /// <summary>
    /// 로컬에서 이 아바타의 Input Authority일 때만 이름표를 켠다.
    /// (Host/Join 각각 자기 캐릭터만 보임. 네트워크 동기화 불필요)
    /// </summary>
    private void UpdateNameTagVisibility()
    {
        if (_nameTag == null)
            return;

        _nameTag.SetActive(Object.HasInputAuthority);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ClearHolderCollisionIgnore();
    }

    public override void FixedUpdateNetwork()
    {
        SyncHeldControllerAndCollision();

        // 잡혀 있으면 모든 피어에서 입력/Move 스킵 (비활성 CC에 Move 호출 방지)
        // 위치는 홀더의 UpdateCarriedObject → SnapToHoldPoint가 맞춤
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

        // 잡기/던지기는 Host(State Authority)만 확정
        if (Object.HasStateAuthority)
        {
            if (pressed.IsSet(PlayerInputButton.Hold))
                HandleHoldPressed();

            if (pressed.IsSet(PlayerInputButton.Throw))
                HandleThrowPressed();
        }

        // Stamp: 공중 + 미홀드 + 미Stamp 상태에서 엣지 한 번 → 착지까지 유지
        if (pressed.IsSet(PlayerInputButton.Stamp) &&
            !IsStamping &&
            !IsHolding &&
            !_cc.Grounded)
        {
            BeginStamp();
        }

        if (IsStamping)
        {
            ApplyStampVelocity();
            _cc.Move(Vector3.zero);

            if (_cc.Grounded)
                IsStamping = false;
        }
        else
        {
            var direction = data.direction;
            direction.y = 0f;

            if (pressed.IsSet(PlayerInputButton.Jump) && _cc.Grounded)
            {
                _cc.Jump();
                JumpCount++;
            }

            _cc.Move(direction);
        }

        if (Object.HasStateAuthority)
            UpdateCarriedObject();
    }

    /// <summary>Stamp 진입: 수평 속도 제거 + 아래 속도 부여 + 애니 카운트.</summary>
    private void BeginStamp()
    {
        IsStamping = true;
        StampCount++;
        ApplyStampVelocity();
    }

    /// <summary>Stamp 동안 수평 정지 + 아래 속도 유지.</summary>
    private void ApplyStampVelocity()
    {
        if (_cc == null)
            return;

        var velocity = _cc.Velocity;
        velocity.x = 0f;
        velocity.z = 0f;
        velocity.y = -Mathf.Abs(_stampDownSpeed);
        _cc.Velocity = velocity;
    }

    public override void Render()
    {
        SyncHeldControllerAndCollision();
        UpdateAnimator();
    }

    /// <summary>홀더 HoldPoint로 텔레포트 (잡힌 플레이어용).</summary>
    public void SnapToHoldPoint(Transform holdPoint)
    {
        if (!Object.HasStateAuthority || holdPoint == null || _cc == null)
            return;

        _cc.Teleport(holdPoint.position, holdPoint.rotation);
    }

    /// <summary>
    /// 들고 있는 대상을 HoldPoint에 붙인다.
    /// 체인(A가 B를 든 채 C가 A를 듦)은 재귀로 처리한다.
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

        IsStamping = false;
        IsHeldNet = true;
        HeldById = holder.Id;
        SyncHeldControllerAndCollision();
    }

    public void OnReleased()
    {
        if (!Object.HasStateAuthority)
            return;

        IsHeldNet = false;
        HeldById = default;
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
        SyncHeldControllerAndCollision();

        if (_cc != null)
            _cc.Velocity = worldVelocity;
    }

    /// <summary>던지는 방향으로 살짝 밀어 홀더와 즉시 재충돌하는 것을 줄인다.</summary>
    private void ApplyThrowSeparation(Vector3 worldVelocity)
    {
        var push = worldVelocity;
        push.y = 0f;
        if (push.sqrMagnitude < 0.0001f || _cc == null)
            return;

        var separated = transform.position + push.normalized * _throwSeparation;
        _cc.Teleport(separated, transform.rotation);
    }

    /// <summary>
    /// 잡힌 상태면 CC 비활성 + 홀더와 IgnoreCollision.
    /// 모든 피어에서 IsHeldNet을 보고 동기화한다.
    /// </summary>
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

    /// <summary>E: 들고 있으면 놓기, 아니면 지면에서 대상 잡기.</summary>
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

    /// <summary>Q: 정면(+위)으로 던지기.</summary>
    private void HandleThrowPressed()
    {
        if (!IsHolding)
            return;

        var velocity = transform.forward * _throwForce + Vector3.up * _throwUpForce;
        ReleaseHeld(velocity, thrown: true);
        ThrowCount++;
    }

    /// <summary>들고 있던 대상에 놓기 또는 던지기 콜백을 보내고 HeldObjectId를 비운다.</summary>
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

    /// <summary>
    /// 반경 안 IHoldable 중 정면(dot) 우선, 동점이면 가까운 대상을 고른다.
    /// 이미 잡힌 대상·자기 자신은 제외.
    /// </summary>
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

    /// <summary>HoldPoint가 없으면 자식 Empty를 기본 오프셋으로 만든다.</summary>
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

    /// <summary>
    /// 복제된 시뮬 상태로 애니를 구동한다 (로컬·원격 공통).
    /// Jump/Throw/Stamp는 네트워크 카운트 증가 시에만 Trigger.
    /// </summary>
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
            // 같은 프레임에 IsHolding=false면 Idle로 먼저 빠져 Throw Trigger가 남을 수 있음
            // → 한 프레임 IsHolding을 유지해 Holding→Throw가 Trigger를 소비하게 함
            _animator.SetBool(IsHoldingHash, true);
            _animator.ResetTrigger(ThrowHash);
            _animator.SetTrigger(ThrowHash);
        }
        else
        {
            _animator.SetBool(IsHoldingHash, IsHolding);
        }

        // 새로 잡을 때 잔여 Throw Trigger 제거
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

        if (StampCount != _shownStampCount)
        {
            _shownStampCount = StampCount;
            _animator.ResetTrigger(JumpHash);
            _animator.ResetTrigger(StampHash);
            _animator.SetTrigger(StampHash);
        }
        else if (_cc.Grounded && !_wasGrounded)
        {
            _animator.ResetTrigger(StampHash);
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
