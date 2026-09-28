using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Rigidbody용 휠 캐리. 텔레포트(MovePosition) 없이 속도만 맞춰 PhysX가 판과 같은 스텝에 옮기게 한다.
///   착지: 현재 판 로컬 위치를 이번 틱 판 목표 포즈로 옮긴 이동량 / dt → 수평 속도 고정 (판에 붙음)
///   공중(볼륨 안): 판 수평 이동 변화분만 더함 (판 기준 상대 속도 유지, Y는 중력)
///   던지기: 판 착지 전까지 carry 없음
/// 동적 RB에 텔레포트+속도를 같이 주면 한 스텝에 두 번 이동해 틱마다 부호가 바뀌는 떨림이 생긴다.
/// 착지 판정은 틱마다 아래 BoxCast 쿼리. Host SA만 시뮬 (HostPhysicsStepper로 틱당 물리 1회 전제).
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
    [Tooltip("착지 중 판 회전(기울기 변화)까지 따라감. 끄면 판 이동만.")]
    [SerializeField] private bool carryRotationWhenGrounded = true;

    [Header("Detection")]
    [Range(0f, 1f)]
    [SerializeField] private float groundNormalThreshold = 0.4f;

    [Tooltip("콜라이더 바닥 아래로 이 거리 안에 판 표면이 있으면 착지로 봄.")]
    [SerializeField] private float groundProbeDistance = 0.08f;

    [SerializeField] private LayerMask groundMask = ~0;

    private static readonly List<PlatformTilt> platforms = new();
    private static readonly Dictionary<PlatformTilt, Rigidbody> platformBodies = new();
    private static readonly RaycastHit[] probeHits = new RaycastHit[8];
    private static float nextRefreshTime;
    private const float RefreshInterval = 1f;

    private Rigidbody body;
    private NetworkObject networkObject;
    private Collider[] ownColliders;
    private Collider probeCollider;

    /// <summary>직전 틱에 준 판 수평 속도. 공중에서 판 가속분만 더할 때 사용.</summary>
    private Vector3 lastCarryVelocity;
    private bool hasCarryVelocity;

    private bool hasRideLocal;
    private Vector3 rideLocal;

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

        ownColliders = GetComponentsInChildren<Collider>();
        foreach (var c in ownColliders)
        {
            if (c != null && !c.isTrigger)
            {
                probeCollider = c;
                break;
            }
        }
    }

    private void FixedUpdate()
    {
        if (IsNetworked)
            return;
        if (!HasStateAuthority)
            return;
        Simulate(Time.fixedDeltaTime);
    }

    public void AttachTo(PlatformTilt target)
    {
        if (body == null) body = GetComponent<Rigidbody>();
        platform = target;
        thrownFlight = false;
        ClearCarry();
    }

    public void NotifyPickedUp()
    {
        thrownFlight = false;
        IsOnPlatform = false;
        CurrentPlatform = null;
        ClearCarry();
    }

    /// <summary>스폰 직후 판 속도 상속.</summary>
    public void BindToPlatformAtSpawn(PlatformTilt target)
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (body == null || target == null || body.isKinematic) return;

        AttachTo(target);
        CurrentPlatform = target;

        Vector3 v = target.FrameVelocity;
        v.y = 0f;
        body.linearVelocity = v;
        body.angularVelocity = Vector3.zero;
        SetCarryVelocity(v);
    }

    /// <summary>놓기. 판 수평 속도를 물려받고 공중 carry로 이어짐.</summary>
    public void SoftRelease()
    {
        if (!HasStateAuthority) return;
        if (body == null) body = GetComponent<Rigidbody>();
        if (body == null || body.isKinematic) return;

        thrownFlight = false;
        ClearCarry();

        var plat = FindContainingPlatform(BodyPosition);
        CurrentPlatform = plat;
        if (plat != null)
        {
            Vector3 frame = plat.FrameVelocity;
            frame.y = 0f;
            body.linearVelocity = frame;
            SetCarryVelocity(frame);
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
        IsOnPlatform = false;
        CurrentPlatform = null;
        ClearCarry();

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

    public void ResetAnchor() => ClearCarry();

    /// <summary>Client 시각 보정용 판 로컬 위치 (틱 시작 시점, NetPosition과 같은 시점).</summary>
    public bool TryGetRideLocal(out NetworkObject platformObject, out Vector3 localPosition)
    {
        platformObject = null;
        localPosition = default;

        if (thrownFlight || !hasRideLocal || CurrentPlatform == null)
            return false;
        if (CurrentPlatform.Object == null || !CurrentPlatform.Object.IsValid)
            return false;

        platformObject = CurrentPlatform.Object;
        localPosition = rideLocal;
        return true;
    }

    /// <summary>Host SA 한 틱. HoldableProp.FUN에서 호출 (PlatformTilt FUN 이후, Physics.Simulate 이전).</summary>
    public void Simulate(float deltaTime)
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (body == null || body.isKinematic || deltaTime <= 0f)
            return;

        hasRideLocal = false;
        IsOnPlatform = ProbePlatformGround();

        if (thrownFlight)
        {
            if (IsOnPlatform && FindContainingPlatform(BodyPosition) != null)
            {
                thrownFlight = false;
                ClearCarry();
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

        // 볼륨 밖: 월드 속도 그대로 (판 속도를 품은 채 떨어져 나감)
        if (plat == null)
        {
            ClearCarry();
            return;
        }

        platform = plat;
        var platBody = GetBody(plat);
        if (platBody == null)
        {
            ClearCarry();
            return;
        }

        Vector3 platPosition = platBody.position;
        Quaternion platRotation = platBody.rotation;
        plat.GetStepTargetPose(out var nextPosition, out var nextRotation);

        Vector3 local = Quaternion.Inverse(platRotation) * (position - platPosition);
        rideLocal = local;
        hasRideLocal = true;

        Vector3 v = body.linearVelocity;

        if (IsOnPlatform)
        {
            // 판에 붙음: 이번 스텝 뒤 같은 판 로컬 위치에 오도록 수평 속도 고정
            Vector3 carried = carryRotationWhenGrounded
                ? nextPosition + nextRotation * local
                : position + (nextPosition - platPosition);
            Vector3 surface = (carried - position) / deltaTime;
            surface.y = 0f;

            v.x = surface.x;
            v.z = surface.z;
            SetCarryVelocity(surface);
        }
        else
        {
            // 공중: 판 기준 상대 수평 속도 유지 + 판 가속분만 반영
            Vector3 frame = (nextPosition - platPosition) / deltaTime;
            frame.y = 0f;

            if (hasCarryVelocity)
            {
                Vector3 change = frame - lastCarryVelocity;
                v.x += change.x;
                v.z += change.z;
            }
            SetCarryVelocity(frame);
        }

        body.linearVelocity = v;
    }

    private void SetCarryVelocity(Vector3 horizontal)
    {
        lastCarryVelocity = horizontal;
        hasCarryVelocity = true;
    }

    private void ClearCarry()
    {
        hasCarryVelocity = false;
        hasRideLocal = false;
    }

    /// <summary>콜라이더 중심에서 아래로 얇은 박스를 쏴 판 윗면이 바로 밑에 있는지 확인.</summary>
    private bool ProbePlatformGround()
    {
        if (probeCollider == null || !probeCollider.enabled)
            return false;

        Bounds b = probeCollider.bounds;
        float half = Mathf.Max(0.01f, Mathf.Min(b.extents.x, b.extents.z) * 0.5f);
        var halfExtents = new Vector3(half, 0.01f, half);
        float distance = b.extents.y + groundProbeDistance;

        int count = Physics.BoxCastNonAlloc(
            b.center, halfExtents, Vector3.down, probeHits, Quaternion.identity,
            distance, groundMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            var hit = probeHits[i];
            if (hit.collider == null || IsOwnCollider(hit.collider))
                continue;
            if (hit.distance <= 0f)
                continue;
            if (hit.normal.y <= groundNormalThreshold)
                continue;

            var plat = hit.collider.GetComponentInParent<PlatformTilt>();
            if (plat == null)
                continue;

            if (platform == null) platform = plat;
            return true;
        }

        return false;
    }

    private bool IsOwnCollider(Collider c)
    {
        for (int i = 0; i < ownColliders.Length; i++)
        {
            if (ownColliders[i] == c)
                return true;
        }
        return false;
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

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !hasRideLocal || CurrentPlatform == null) return;
        var platBody = GetBody(CurrentPlatform);
        if (platBody == null) return;

        Gizmos.color = IsOnPlatform ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(platBody.position + platBody.rotation * rideLocal, 0.12f);
    }
#endif
}
