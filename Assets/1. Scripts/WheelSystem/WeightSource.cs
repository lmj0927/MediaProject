using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 판에 하중을 싣는 대상. 무게 정보와 접촉 관계를 함께 관리함.
///
/// 판정은 트리거 체적이 아니라 실제 지지 관계를 따름.
/// 체적으로 재면 공중에 떠 있는 대상까지 무게로 잡힘.
///
/// 지지 관계를 알아내는 방법은 두 가지.
/// - Collision: 충돌 콜백. 물리로 움직이는 Rigidbody 대상용.
/// - Probe: 발밑 SphereCast. CharacterController처럼 충돌 콜백이 오지 않는 대상용.
/// Auto로 두면 움직이는 Rigidbody가 있으면 Collision, 없으면 Probe를 씀.
///
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

    private float platformExpiry = -1f;
    private float weightMultiplier = 1f;

    private Vector3 simulatedPosition;
    private float simulatedPositionTime = -1f;
    private const float SimulatedPositionLifetime = 0.1f;

    // ---- 무게 정보 ----

    public Vector3 WorldPosition =>
        (measurePoint != null ? measurePoint.position : transform.position) + PositionCorrection;

    /// <summary>
    /// 화면용 보간 위치와 실제 시뮬레이션 위치의 차이.
    /// </summary>
    private Vector3 PositionCorrection =>
        simulatedPositionTime >= 0f && Time.time - simulatedPositionTime <= SimulatedPositionLifetime
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

    // ---- 접촉 정보 ----

    /// <summary>판에 직접 닿아 있는 경우 그 솔버. 아니면 null.</summary>
    public CenterOfMassSolver DirectPlatform { get; private set; }

    /// <summary>현재 지지 관계로 이어진 다른 무게 소스들.</summary>
    public IReadOnlyCollection<WeightSource> Neighbors => neighborExpiry.Keys;

    /// <summary>현재 발밑 검사 방식을 쓰는지.</summary>
    public bool UsesProbe => detection switch
    {
        SupportDetection.Probe => true,
        SupportDetection.Collision => false,
        _ => cachedBody == null || cachedBody.isKinematic
    };

    // ---- 무게 조절 API ----

    /// <summary>기본 무게를 바꿈. 아이템 획득 등 영구적인 변화에 사용.</summary>
    public void SetWeight(float value)
    {
        weight = Mathf.Max(0f, value);
        useRigidbodyMass = false;
    }

    /// <summary>일시적인 무게 배율. 내리찍기 같은 순간적인 변화에 사용.</summary>
    public void SetWeightMultiplier(float value)
    {
        weightMultiplier = Mathf.Max(0f, value);
    }

    /// <summary>현재 적용 중인 배율.</summary>
    public float WeightMultiplier => weightMultiplier;

    /// <summary>
    /// 시뮬레이션 기준 위치를 알려줌.
    /// 네트워크로 움직이는 캐릭터는 매 틱 이동이 끝난 직후 호출할 것.
    /// 호출이 끊기면 잠시 후 자동으로 transform 위치로 돌아감.
    /// </summary>
    public void SetSimulatedPosition(Vector3 position)
    {
        simulatedPosition = position;
        simulatedPositionTime = Time.time;
    }

    /// <summary>
    /// 들려 있는 동안 지지 대상을 강제로 지정함. null이면 해제.
    /// </summary>
    public void SetSupportOverride(WeightSource supporter)
    {
        supportOverride = supporter == this ? null : supporter;
    }

    // ---- 수명 ----

    private void Reset()
    {
        measurePoint = transform;
    }

    private void CacheComponents()
    {
        cachedBody = GetComponent<Rigidbody>();
        characterController = GetComponent<CharacterController>();
        ownCollider = GetComponent<Collider>();
    }

    private void Awake()
    {
        CacheComponents();

        // 잠들면 OnCollisionStay가 끊겨 연결이 만료됨
        if (cachedBody != null && !cachedBody.isKinematic)
            cachedBody.sleepThreshold = 0f;

        // 판의 보간과 어긋나면 떨려 보임
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

    private void FixedUpdate()
    {
        // 들려 있으면 지지 대상이 정해져 있으므로 발밑 검사를 하지 않음
        if (supportOverride != null && supportOverride.isActiveAndEnabled)
        {
            Link(supportOverride, persistent: false);
            return;
        }

        if (UsesProbe) Probe();
    }

    private void Update()
    {
        // 유예가 끝난 연결 정리
        if (platformExpiry >= 0f && Time.time >= platformExpiry)
        {
            DirectPlatform = null;
            platformExpiry = -1f;
        }

        if (neighborExpiry.Count == 0) return;

        scratch.Clear();
        foreach (var kvp in neighborExpiry)
        {
            if (kvp.Key == null || (kvp.Value >= 0f && Time.time >= kvp.Value))
                scratch.Add(kvp.Key);
        }
        foreach (var key in scratch) neighborExpiry.Remove(key);
    }

    // ---- Probe 방식 ----

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

            // 내가 들고 있는 대상은 바닥이 될 수 없음. 발밑과 겹쳐 서로를 바닥으로 삼는 섬이 생김.
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

    /// <summary>발밑 검사에 쓸 구체의 시작점, 반지름, 거리를 구함.</summary>
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

        // 바닥에서 반지름만큼 올린 지점에서 쏴야 시작부터 바닥에 파묻히지 않음
        origin = bottom + Vector3.up * (radius + ProbeSkin) + PositionCorrection;
        distance += radius;
    }

    // ---- Collision 방식 ----

    private void OnCollisionEnter(Collision collision) => Connect(collision);

    // 접촉이 유지되는 동안 계속 연결을 갱신함
    private void OnCollisionStay(Collision collision) => Connect(collision);

    private void OnCollisionExit(Collision collision)
    {
        // 방식과 무관하게 처리함. 들어 올리면 Probe로 전환되어 신호가 버려짐.
        var solver = collision.transform.GetComponentInParent<CenterOfMassSolver>();
        if (solver != null && solver == DirectPlatform)
        {
            platformExpiry = Time.time + lingerTime;
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
        if (UsesProbe || supportOverride != null) return;
        if (!IsSupporting(collision)) return;

        // 매 스텝 갱신함. Exit 신호가 누락되어도 lingerTime 뒤에 풀림.
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
            // 법선은 상대 쪽에서 이쪽을 향함. 아래에서 받치고 있으면 Y가 양수
            if (collision.GetContact(i).normal.y >= minSupportNormalY) return true;
        }
        return false;
    }

    // ---- 연결 관리 ----

    /// <summary>
    /// 판과의 직접 연결을 기록함.
    /// persistent는 Exit가 올 때까지 유지(충돌 방식), 아니면 매 스텝 갱신해야 유지(검사 방식).
    /// </summary>
    private void SetPlatform(CenterOfMassSolver solver, bool persistent)
    {
        if (DirectPlatform != solver)
        {
            DirectPlatform = solver;
            platformExpiry = persistent ? -1f : Time.time + lingerTime;
            return;
        }

        if (persistent) platformExpiry = -1f;
        else if (platformExpiry >= 0f) platformExpiry = Time.time + lingerTime;
    }

    /// <summary>양방향으로 연결함. 한쪽만 기록하면 탐색에서 누락됨.</summary>
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

        // 이미 충돌 방식으로 고정된 연결은 덮어쓰지 않음
        if (neighborExpiry.TryGetValue(other, out float current) && current < 0f) return;
        neighborExpiry[other] = Time.time + lingerTime;
    }

    private void BeginExpire(WeightSource other)
    {
        if (neighborExpiry.TryGetValue(other, out float current) && current < 0f)
            neighborExpiry[other] = Time.time + lingerTime;
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