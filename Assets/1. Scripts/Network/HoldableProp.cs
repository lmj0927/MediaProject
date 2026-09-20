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

    private Rigidbody _rigidbody;
    private Collider[] _colliders;
    private bool _ignoringHolderCollision;
    private NetworkId _ignoredHolderId;

    [Networked] private NetworkBool IsHeldNet { get; set; }
    [Networked] private NetworkId HeldById { get; set; }
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
        SyncHolderCollisionIgnore();

        if (!Object.HasStateAuthority)
            transform.SetPositionAndRotation(NetPosition, NetRotation);
    }

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
