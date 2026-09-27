using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 판에 하중을 싣는 대상. 무게 정보와 접촉 관계를 함께 관리함.
///
/// 판정은 트리거 체적이 아니라 실제 지지 관계를 따름.
/// Host State Authority에서만 Probe/만료를 갱신한다 (CenterOfMassSolver가 TickAllSupport 호출).
/// </summary>
[DisallowMultipleComponent]
public class WeightSource : MonoBehaviour
{
    public enum SupportDetection { Auto, Collision, Probe }

    /// <summary>씬에 존재하는 모든 무게 소스. Solver가 탐색 시작점으로 사용함.</summary>
    public static readonly List<WeightSource> All = new();

    [Header("Weight")]
    [Tooltip("무게중심 계산에 쓰는 가중치. Rigidbody 질량과 별개로 게임플레이용으로 조절 가능.")]
    [SerializeField] private float weight = 1f;

    [Tooltip("활성화시 Rigidbody.mass를 weight 대신 사용.")]
    [SerializeField] private bool useRigidbodyMass = false;

    [Tooltip("무게중심 기준점. 비우면 이 오브젝트의 Transform을 사용.")]
    [SerializeField] private Transform measurePoint;

    [Tooltip("비활성화시 판 위에 있어도 무게중심에 영향을 주지 않음.")]
    [SerializeField] private bool contributes = true;

    [Header("Contact")]
    [Tooltip("지지 관계를 알아내는 방법.\n" +
             "Auto: 움직이는 Rigidbody면 Collision, 아니면 Probe.\n" +
             "CharacterController 플레이어는 Auto 또는 Probe.")]
    [SerializeField] private SupportDetection detection = SupportDetection.Auto;

    [Tooltip("접촉이 끊긴 뒤에도 이 시간 동안은 연결을 유지함.\n" +
             "0이면 점프할 때마다 무게가 즉시 빠져 판이 덜컥거림.")]
    [SerializeField] private float lingerTime = 0.2f;

    [Tooltip("Collision 방식 전용. 접촉면의 법선 Y가 이 값 이상일 때만 하중 전달로 인정.\n" +
             "-1이면 옆면에 스치기만 해도 인정. 난간에 기댄 상태를 무게로 칠지 결정함.")]
    [Range(-1f, 1f)][SerializeField] private float minSupportNormalY = -1f;

    [Header("Probe")]
    [Tooltip("Probe 방식 전용. 발밑에서 이 거리 안에 있는 것을 지지 대상으로 봄.")]
    [SerializeField] private float probeDistance = 0.15f;

    [Tooltip("Probe 방식 전용. 검사할 레이어.")]
    [SerializeField] private LayerMask probeMask = ~0;

    private const float ProbeSkin = 0.05f;

    private readonly Dictionary<WeightSource, float> neighborExpiry = new();
    private readonly List<WeightSource> scratch = new();
    private readonly RaycastHit[] probeHits = new RaycastHit[8];

    private Rigidbody cachedBody;
    private CharacterController characterController;
    private Collider ownCollider;
    private WeightSource supportOverride;
    private NetworkObject networkObject;

    private float platformExpiry = -1f;
    private float weightMultiplier = 1f;

    private Vector3 simulatedPosition;
    private float simulatedPositionTime = -1f;
    private const float SimulatedPositionLifetime = 0.1f;

    private float SimTime
    {
        get
        {
            var runner = NetworkRunner.GetRunnerForGameObject(gameObject);
            if (runner != null && runner.IsRunning)
                return (float)runner.SimulationTime;
            return Time.time;
        }
    }

    private bool HasStateAuthority
    {
        get
        {
            if (networkObject == null)
                networkObject = GetComponentInParent<NetworkObject>();
            return networkObject != null && networkObject.IsValid && networkObject.HasStateAuthority;
        }
    }

    public Vector3 WorldPosition =>
        (measurePoint != null ? measurePoint.position : transform.position) + PositionCorrection;

    private Vector3 PositionCorrection =>
        simulatedPositionTime >= 0f && SimTime - simulatedPositionTime <= SimulatedPositionLifetime
            ? simulatedPosition - transform.position
            : Vector3.zero;

    public float Weight
    {
        get
        {
            float baseWeight = useRigidbodyMass && cachedBody != null ? cachedBody.mass : weight;
            return baseWeight * weightMultiplier;
        }
    }

    public bool Contributes
    {
        get => contributes && isActiveAndEnabled;
        set => contributes = value;
    }

    public CenterOfMassSolver DirectPlatform { get; private set; }

    public IReadOnlyCollection<WeightSource> Neighbors => neighborExpiry.Keys;

    public bool UsesProbe => detection switch
    {
        SupportDetection.Probe => true,
        SupportDetection.Collision => false,
        _ => cachedBody == null || cachedBody.isKinematic
    };

    /// <summary>Host COM 틱 직전에 모든 SA WeightSource의 지지 그래프를 갱신함.</summary>
    public static void TickAllSupport()
    {
        for (int i = 0; i < All.Count; i++)
        {
            var source = All[i];
            if (source != null && source.isActiveAndEnabled)
                source.TickSupport();
        }
    }

    public void TickSupport()
    {
        if (!HasStateAuthority)
            return;

        ExpireStaleLinks();

        if (supportOverride != null && supportOverride.isActiveAndEnabled)
        {
            Link(supportOverride, persistent: false);
            return;
        }

        if (UsesProbe)
            Probe();
    }

    public void SetWeight(float value)
    {
        weight = Mathf.Max(0f, value);
        useRigidbodyMass = false;
    }

    public void SetWeightMultiplier(float value)
    {
        weightMultiplier = Mathf.Max(0f, value);
    }

    public float WeightMultiplier => weightMultiplier;

    public void SetSimulatedPosition(Vector3 position)
    {
        simulatedPosition = position;
        simulatedPositionTime = SimTime;
    }

    public void SetSupportOverride(WeightSource supporter)
    {
        supportOverride = supporter == this ? null : supporter;
    }

    private void Reset()
    {
        measurePoint = transform;
    }

    private void CacheComponents()
    {
        cachedBody = GetComponent<Rigidbody>();
        characterController = GetComponent<CharacterController>();
        ownCollider = GetComponent<Collider>();
        networkObject = GetComponentInParent<NetworkObject>();
    }

    private void Awake()
    {
        CacheComponents();

        if (cachedBody != null && !cachedBody.isKinematic)
            cachedBody.sleepThreshold = 0f;

        if (cachedBody != null && !cachedBody.isKinematic &&
            cachedBody.interpolation == RigidbodyInterpolation.None)
        {
            cachedBody.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void OnEnable() => All.Add(this);

    private void OnDisable()
    {
        All.Remove(this);
        neighborExpiry.Clear();
        DirectPlatform = null;
        platformExpiry = -1f;
    }

    private void ExpireStaleLinks()
    {
        float now = SimTime;

        if (platformExpiry >= 0f && now >= platformExpiry)
        {
            DirectPlatform = null;
            platformExpiry = -1f;
        }

        if (neighborExpiry.Count == 0) return;

        scratch.Clear();
        foreach (var kvp in neighborExpiry)
        {
            if (kvp.Key == null || (kvp.Value >= 0f && now >= kvp.Value))
                scratch.Add(kvp.Key);
        }
        foreach (var key in scratch) neighborExpiry.Remove(key);
    }

    private void Probe()
    {
        GetProbeShape(out Vector3 origin, out float radius, out float distance);

        int count = Physics.SphereCastNonAlloc(
            origin, radius, Vector3.down, probeHits, distance, probeMask, QueryTriggerInteraction.Ignore);

        Collider best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            var col = probeHits[i].collider;
            if (col == null || col.transform.IsChildOf(transform)) continue;

            var carried = col.GetComponentInParent<WeightSource>();
            if (carried != null && carried.supportOverride == this) continue;

            if (probeHits[i].distance < bestDistance)
            {
                bestDistance = probeHits[i].distance;
                best = col;
            }
        }

        if (best == null) return;

        var solver = best.GetComponentInParent<CenterOfMassSolver>();
        if (solver != null)
        {
            SetPlatform(solver, persistent: false);
            return;
        }

        var other = best.GetComponentInParent<WeightSource>();
        if (other != null && other != this)
            Link(other, persistent: false);
    }

    private void GetProbeShape(out Vector3 origin, out float radius, out float distance)
    {
        Vector3 bottom;

        if (characterController != null)
        {
            Vector3 s = transform.lossyScale;
            float horizontalScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));

            radius = characterController.radius * horizontalScale * 0.9f;
            Vector3 center = transform.TransformPoint(characterController.center);
            bottom = center + Vector3.down * (characterController.height * 0.5f * Mathf.Abs(s.y));
            distance = probeDistance + ProbeSkin + characterController.skinWidth;
        }
        else if (ownCollider != null)
        {
            Bounds b = ownCollider.bounds;
            radius = Mathf.Min(b.extents.x, b.extents.z) * 0.9f;
            bottom = b.center + Vector3.down * b.extents.y;
            distance = probeDistance + ProbeSkin;
        }
        else
        {
            radius = 0.2f;
            bottom = transform.position;
            distance = probeDistance + ProbeSkin;
        }

        origin = bottom + Vector3.up * (radius + ProbeSkin) + PositionCorrection;
        distance += radius;
    }

    private void OnCollisionEnter(Collision collision) => Connect(collision);

    private void OnCollisionStay(Collision collision) => Connect(collision);

    private void OnCollisionExit(Collision collision)
    {
        if (!HasStateAuthority) return;

        var solver = collision.transform.GetComponentInParent<CenterOfMassSolver>();
        if (solver != null && solver == DirectPlatform)
        {
            platformExpiry = SimTime + lingerTime;
            return;
        }

        var other = collision.transform.GetComponentInParent<WeightSource>();
        if (other != null)
        {
            BeginExpire(other);
            other.BeginExpire(this);
        }
    }

    private void Connect(Collision collision)
    {
        if (!HasStateAuthority) return;
        if (UsesProbe || supportOverride != null) return;
        if (!IsSupporting(collision)) return;

        var solver = collision.transform.GetComponentInParent<CenterOfMassSolver>();
        if (solver != null)
        {
            SetPlatform(solver, persistent: false);
            return;
        }

        var other = collision.transform.GetComponentInParent<WeightSource>();
        if (other != null && other != this)
            Link(other, persistent: false);
    }

    private bool IsSupporting(Collision collision)
    {
        if (minSupportNormalY <= -1f) return true;

        int count = collision.contactCount;
        for (int i = 0; i < count; i++)
        {
            if (collision.GetContact(i).normal.y >= minSupportNormalY) return true;
        }
        return false;
    }

    private void SetPlatform(CenterOfMassSolver solver, bool persistent)
    {
        float now = SimTime;
        if (DirectPlatform != solver)
        {
            DirectPlatform = solver;
            platformExpiry = persistent ? -1f : now + lingerTime;
            return;
        }

        if (persistent) platformExpiry = -1f;
        else if (platformExpiry >= 0f) platformExpiry = now + lingerTime;
    }

    private void Link(WeightSource other, bool persistent)
    {
        Touch(other, persistent);
        other.Touch(this, persistent);
    }

    private void Touch(WeightSource other, bool persistent)
    {
        if (persistent)
        {
            neighborExpiry[other] = -1f;
            return;
        }

        if (neighborExpiry.TryGetValue(other, out float current) && current < 0f) return;
        neighborExpiry[other] = SimTime + lingerTime;
    }

    private void BeginExpire(WeightSource other)
    {
        if (neighborExpiry.TryGetValue(other, out float current) && current < 0f)
            neighborExpiry[other] = SimTime + lingerTime;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!UsesProbe && Application.isPlaying) return;

        if (!Application.isPlaying) CacheComponents();
        GetProbeShape(out Vector3 origin, out float radius, out float distance);
        Gizmos.color = DirectPlatform != null ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(origin, radius);
        Gizmos.DrawWireSphere(origin + Vector3.down * distance, radius);
    }
#endif
}
