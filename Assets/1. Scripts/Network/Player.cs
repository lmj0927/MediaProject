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

        _cc.Move(direction);

        if (Object.HasStateAuthority)
            UpdateCarriedObject();
    }

    public override void Render()
    {
        SyncHeldControllerAndCollision();
        UpdateAnimator();
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
