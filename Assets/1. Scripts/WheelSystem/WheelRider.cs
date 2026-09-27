using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Rigidbody용 휠 캐리. WheelCarrier와 같은 delta 방식:
///   1) 지난 틱 판 위 지점이 지금 어디로 갔는지 → delta
///   2) MovePosition(pos + delta)
///   3) EndTick으로 앵커 갱신
/// 착지: 판 로컬(회전 포함). 공중: 판 수평 이동만 (Y는 Rigidbody 중력).
/// Parent / kinematic 승객 / 절대 스냅 없음. Host SA만 시뮬.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(WeightSource))]
public class WheelRider : MonoBehaviour
{
    [Header("References")]
    [Tooltip("따라갈 휠의 판. 비우면 볼륨으로 자동 탐색.")]
    [SerializeField] private PlatformTilt platform;

    [Header("Volume")]
    [Range(0.5f, 1.5f)]
    [SerializeField] private float radiusScale = 1.05f;

    [Tooltip("데크 위로 이 높이까지 승객. 홀드/점프보다 높게.")]
    [SerializeField] private float heightAbove = 4f;

    [SerializeField] private float heightBelow = 0.6f;

    [Header("Carry")]
    [Tooltip("착지 중 판 회전까지 따라감. WheelCarrier와 동일.")]
    [SerializeField] private bool carryRotationWhenGrounded = true;

    [Tooltip("이 거리 이상 순간 이동이면 앵커 버림 (들기/던지기).")]
    [SerializeField] private float teleportThreshold = 0.5f;

    [Header("Detection")]
    [Range(0f, 1f)]
    [SerializeField] private float groundNormalThreshold = 0.4f;

    private static readonly List<PlatformTilt> platforms = new();
    private static readonly Dictionary<PlatformTilt, Rigidbody> platformBodies = new();
    private static float nextRefreshTime;
    private const float RefreshInterval = 1f;

    private Rigidbody body;
    private NetworkObject networkObject;
    private bool contactGrounded;

    private bool hasAnchor;
    private PlatformTilt anchorPlatform;
    private Vector3 anchorLocal;
    private Vector3 anchorPlatformPosition;
    private Vector3 lastFinalPosition;

    /// <summary>던지기 비행. 착지 전까지 carry 없음.</summary>
    private bool thrownFlight;

    private bool HasStateAuthority
    {
        get
        {
            if (networkObject == null)
                networkObject = GetComponentInParent<NetworkObject>();
            if (networkObject == null || !networkObject.IsValid)
                return true;
            return networkObject.HasStateAuthority;
        }
    }

    private bool IsNetworked =>
        networkObject != null && networkObject.IsValid;

    public bool IsRiding => !thrownFlight && FindContainingPlatform(BodyPosition) != null;

    public bool IsOnPlatform { get; private set; }

    public bool IsThrownFlight => thrownFlight;

    public PlatformTilt CurrentPlatform { get; private set; }

    private Vector3 BodyPosition =>
        body != null ? body.position : transform.position;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        networkObject = GetComponentInParent<NetworkObject>();
        if (body != null)
            body.interpolation = RigidbodyInterpolation.None;
    }

    private void FixedUpdate()
    {
        if (IsNetworked)
            return;
        if (!HasStateAuthority)
            return;
        Simulate();
    }

    public void AttachTo(PlatformTilt target)
    {
        if (body == null) body = GetComponent<Rigidbody>();
        platform = target;
        hasAnchor = false;
        thrownFlight = false;
    }

    public void NotifyPickedUp()
    {
        thrownFlight = false;
        hasAnchor = false;
        contactGrounded = false;
        IsOnPlatform = false;
        CurrentPlatform = null;
    }

    /// <summary>스폰 직후 판 속도 상속 + 앵커 시작.</summary>
    public void BindToPlatformAtSpawn(PlatformTilt target)
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (body == null || target == null || body.isKinematic) return;

        AttachTo(target);
        CurrentPlatform = target;
        EndTick(BodyPosition, target);

        Vector3 v = target.FrameVelocity;
        v.y = 0f;
        body.linearVelocity = v;
        body.angularVelocity = Vector3.zero;
    }

    /// <summary>놓기. Carrier 공중 경로로 이어짐 (특수 SoftDrop 없음).</summary>
    public void SoftRelease()
    {
        if (!HasStateAuthority) return;
        if (body == null) body = GetComponent<Rigidbody>();
        if (body == null || body.isKinematic) return;

        thrownFlight = false;

        var plat = FindContainingPlatform(BodyPosition);
        CurrentPlatform = plat;
        if (plat != null)
        {
            EndTick(BodyPosition, plat);
            Vector3 frame = plat.FrameVelocity;
            frame.y = 0f;
            body.linearVelocity = frame;
        }

        body.angularVelocity = Vector3.zero;
    }

    /// <summary>던지기. carry OFF until 판 착지.</summary>
    public void BeginThrownFlight(Vector3 worldVelocity)
    {
        if (!HasStateAuthority) return;
        if (body == null) body = GetComponent<Rigidbody>();
        if (body == null || body.isKinematic) return;

        thrownFlight = true;
        hasAnchor = false;
        contactGrounded = false;
        IsOnPlatform = false;
        CurrentPlatform = null;

        Vector3 v = worldVelocity;
        var plat = FindContainingPlatform(BodyPosition);
        if (plat != null)
        {
            platform = plat;
            Vector3 frame = plat.FrameVelocity;
            frame.y = 0f;
            v += frame;
        }

        body.linearVelocity = v;
        body.angularVelocity = Vector3.zero;
    }

    public void ResetAnchor() => hasAnchor = false;

    public bool TryGetRideLocal(out NetworkObject platformObject, out Vector3 localPosition)
    {
        platformObject = null;
        localPosition = default;

        if (thrownFlight || !hasAnchor || CurrentPlatform == null)
            return false;
        if (CurrentPlatform.Object == null || !CurrentPlatform.Object.IsValid)
            return false;

        platformObject = CurrentPlatform.Object;
        localPosition = anchorLocal;
        return true;
    }

    /// <summary>Host SA 한 틱. HoldableProp.FUN에서 호출.</summary>
    public void Simulate()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (body == null || body.isKinematic)
            return;

        IsOnPlatform = contactGrounded;
        contactGrounded = false;

        if (thrownFlight)
        {
            if (IsOnPlatform && FindContainingPlatform(BodyPosition) != null)
            {
                thrownFlight = false;
                hasAnchor = false;
            }
            else
            {
                CurrentPlatform = null;
                return;
            }
        }

        Vector3 position = BodyPosition;
        var plat = FindContainingPlatform(position);
        CurrentPlatform = plat;

        if (plat == null)
        {
            hasAnchor = false;
            return;
        }

        platform = plat;
        var platBody = GetBody(plat);
        if (platBody == null)
        {
            hasAnchor = false;
            return;
        }

        Vector3 delta = GetCarryDelta(position, plat, platBody, IsOnPlatform);
        if (delta.sqrMagnitude > 1e-10f)
            body.MovePosition(position + delta);

        // 착지 중에만 수평 속도를 판에 맞춤. 공중은 Carrier처럼 속도 강제 없음.
        if (IsOnPlatform)
        {
            Vector3 pv = plat.PointVelocity(body.position);
            Vector3 v = body.linearVelocity;
            v.x = pv.x;
            v.z = pv.z;
            body.linearVelocity = v;
        }

        EndTick(BodyPosition, plat);
    }

    private Vector3 GetCarryDelta(
        Vector3 position,
        PlatformTilt plat,
        Rigidbody platBody,
        bool grounded)
    {
        bool teleported = hasAnchor &&
            (position - lastFinalPosition).sqrMagnitude > teleportThreshold * teleportThreshold;

        if (!hasAnchor || anchorPlatform != plat || teleported || platBody == null)
            return Vector3.zero;

        if (grounded && carryRotationWhenGrounded)
        {
            Vector3 carried = platBody.position + platBody.rotation * anchorLocal;
            return carried - position;
        }

        // 공중: 판의 이동만 (Y 제외) — WheelCarrier와 동일
        Vector3 delta = platBody.position - anchorPlatformPosition;
        delta.y = 0f;
        return delta;
    }

    private void EndTick(Vector3 finalPosition, PlatformTilt plat)
    {
        lastFinalPosition = finalPosition;

        var platBody = GetBody(plat);
        if (platBody == null)
        {
            hasAnchor = false;
            return;
        }

        anchorPlatform = plat;
        anchorLocal = Quaternion.Inverse(platBody.rotation) * (finalPosition - platBody.position);
        anchorPlatformPosition = platBody.position;
        hasAnchor = true;
    }

    private PlatformTilt FindContainingPlatform(Vector3 position)
    {
        if (platform != null &&
            platform.isActiveAndEnabled &&
            platform.ContainsCarryPoint(position, radiusScale, heightAbove, heightBelow))
            return platform;

        RefreshPlatformsIfNeeded();

        PlatformTilt best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < platforms.Count; i++)
        {
            var p = platforms[i];
            if (p == null || !p.isActiveAndEnabled) continue;
            if (!p.ContainsCarryPoint(position, radiusScale, heightAbove, heightBelow))
                continue;

            var rb = GetBody(p);
            Vector3 deck = rb != null ? rb.position : p.transform.position;
            Vector3 flat = position - deck;
            flat.y = 0f;
            float d = flat.sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = p;
            }
        }

        return best;
    }

    private static Rigidbody GetBody(PlatformTilt plat)
    {
        if (platformBodies.TryGetValue(plat, out var rb) && rb != null)
            return rb;
        rb = plat.GetComponent<Rigidbody>();
        platformBodies[plat] = rb;
        return rb;
    }

    private static void RefreshPlatformsIfNeeded()
    {
        bool hasMissing = false;
        for (int i = 0; i < platforms.Count; i++)
        {
            if (platforms[i] == null)
            {
                hasMissing = true;
                break;
            }
        }

        if (!hasMissing && platforms.Count > 0 && Time.unscaledTime < nextRefreshTime)
            return;

        platforms.Clear();
        platforms.AddRange(Object.FindObjectsByType<PlatformTilt>(FindObjectsSortMode.None));
        platformBodies.Clear();
        nextRefreshTime = Time.unscaledTime + RefreshInterval;
    }

    private void OnCollisionStay(Collision collision) => EvaluateContacts(collision);
    private void OnCollisionEnter(Collision collision) => EvaluateContacts(collision);

    private void EvaluateContacts(Collision collision)
    {
        if (IsNetworked && !HasStateAuthority) return;

        var plat = collision.transform.GetComponentInParent<PlatformTilt>();
        if (plat == null) return;

        int count = collision.contactCount;
        for (int i = 0; i < count; i++)
        {
            if (collision.GetContact(i).normal.y > groundNormalThreshold)
            {
                contactGrounded = true;
                if (platform == null) platform = plat;
                return;
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !hasAnchor || anchorPlatform == null) return;
        var platBody = GetBody(anchorPlatform);
        if (platBody == null) return;

        Gizmos.color = IsOnPlatform ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(platBody.position + platBody.rotation * anchorLocal, 0.12f);
    }
#endif
}
