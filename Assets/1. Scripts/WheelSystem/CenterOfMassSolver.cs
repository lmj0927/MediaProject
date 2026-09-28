using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 판에 하중을 싣고 있는 대상을 모아 무게중심을 구하고, 축(구체 중심) 기준 수평 오프셋을 냄.
/// Host State Authority의 FixedUpdateNetwork에서만 시뮬한다.
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(NetworkObject))]
public class CenterOfMassSolver : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("오프셋을 재는 기준점. 보통 구체(축)의 Transform.")]
    [SerializeField] private Transform pivot;

    [Header("Detection")]
    [Tooltip("간접 접촉까지 따라감. 비활성화시 판에 직접 닿은 대상만 계산.")]
    [SerializeField] private bool includeIndirectContacts = true;

    [Tooltip("접촉 연쇄를 따라갈 최대 단계. 사람 위에 사람이 올라탄 경우를 몇 단계까지 인정할지.")]
    [Range(1, 16)][SerializeField] private int maxContactDepth = 6;

    [Header("Influence")]
    [Tooltip("활성화시 중심에서 먼 대상일수록 영향력이 커짐(거리의 제곱에 비례).\n" +
                "비활성화시 단순 무게 평균.")]
    [SerializeField] private bool distanceSquaredInfluence = true;

    [Tooltip("중심에 있는 대상의 최소 영향력. 거리 비율(0~1)의 제곱에 더해짐.\n" +
                "0이면 중심의 무게는 기울기에 관여하지 않음. 클수록 단순 평균에 가까워짐.")]
    [Min(0f)][SerializeField] private float centerInfluence = 0.2f;

    [Header("Range Guard")]
    [Tooltip("판 반경에 곱한 거리보다 수평으로 멀리 있는 대상은 제외함.\n" +
                "연결이 남아 있어도 판 밖의 물체가 무게로 잡히지 않게 막음. 0이면 사용 안 함.")]
    [SerializeField] private float occupantRangeScale = 1.3f;

    [Tooltip("데크보다 이만큼 아래에 있는 대상은 제외함.")]
    [SerializeField] private float occupantBelowTolerance = 1f;

    [Header("Bias")]
    [Tooltip("무게중심을 인위적으로 밀어내는 값(아이템, 돌풍 등).")]
    [SerializeField] private Vector3 externalBias = Vector3.zero;

    [Header("Advanced")]
    [Tooltip("계산 결과를 매끄럽게 만드는 시간. " +
                "PlatformTilt.tiltResponsiveness와 역할이 겹치므로 둘 중 하나만 조절할 것. " +
                "기본값에서 건드리지 않는 쪽을 권장함.")]
    [SerializeField] private float smoothTime = 0.05f;

    private readonly List<WeightSource> occupants = new();
    private readonly Queue<WeightSource> frontier = new();
    private readonly HashSet<WeightSource> visited = new();

    private Vector3 smoothedOffset;
    private Vector3 smoothVelocity;
    private PlatformTilt platform;

    /// <summary>축 기준 수평 오프셋(월드 XZ). 길이가 0이면 균형 상태.</summary>
    public Vector3 WorldOffset => smoothedOffset;

    /// <summary>무게중심의 월드 위치. 디버그 표시용.</summary>
    public Vector3 WorldCenterOfMass => PivotPosition + smoothedOffset;

    /// <summary>현재 판 위 총 무게. 0이면 아무도 없는 상태.</summary>
    public float TotalWeight { get; private set; }

    public bool HasOccupants => TotalWeight > Mathf.Epsilon;

    /// <summary>현재 하중을 싣고 있는 것으로 판정된 대상들.</summary>
    public IReadOnlyList<WeightSource> Occupants => occupants;

    private Vector3 PivotPosition => pivot != null ? pivot.position : transform.position;

    private void Awake()
    {
        platform = GetComponent<PlatformTilt>();

        if (pivot == null)
            Debug.LogWarning($"{name}: pivot이 비어 있습니다. 구체의 Transform을 지정하세요.", this);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        WeightSource.TickAllSupport();
        Simulate(Runner.DeltaTime);
    }

    /// <summary>한 틱분 COM 갱신. Host SA에서만 호출.</summary>
    public void Simulate(float dt)
    {
        CollectOccupants();

        Vector3 target = CalculateRawOffset() + externalBias;

        if (smoothTime <= 0f)
        {
            smoothedOffset = target;
            smoothVelocity = Vector3.zero;
        }
        else
        {
            smoothedOffset = Vector3.SmoothDamp(
                smoothedOffset, target, ref smoothVelocity, smoothTime,
                Mathf.Infinity, dt);
        }
    }

    private void CollectOccupants()
    {
        occupants.Clear();
        visited.Clear();
        frontier.Clear();

        var all = WeightSource.All;
        for (int i = 0; i < all.Count; i++)
        {
            var source = all[i];
            if (source == null || source.DirectPlatform != this) continue;
            if (visited.Add(source)) frontier.Enqueue(source);
        }

        int depth = 0;
        while (frontier.Count > 0 && depth < maxContactDepth)
        {
            int levelCount = frontier.Count;
            for (int i = 0; i < levelCount; i++)
            {
                var source = frontier.Dequeue();
                if (source == null) continue;

                if (!IsInRange(source)) continue;

                occupants.Add(source);

                if (!includeIndirectContacts) continue;

                foreach (var n in source.Neighbors)
                {
                    if (n != null && visited.Add(n)) frontier.Enqueue(n);
                }
            }
            depth++;
        }
    }

    private bool IsInRange(WeightSource source)
    {
        if (occupantRangeScale <= 0f || platform == null) return true;

        Vector3 center = platform.transform.position;
        Vector3 p = source.WorldPosition;

        if (p.y < center.y - occupantBelowTolerance) return false;

        float range = platform.PlatformRadius * occupantRangeScale;
        float dx = p.x - center.x;
        float dz = p.z - center.z;
        return dx * dx + dz * dz <= range * range;
    }

    private Vector3 CalculateRawOffset()
    {
        Vector3 pivotPosition = PivotPosition;
        float radius = platform != null ? Mathf.Max(0.01f, platform.EffectiveOffsetRadius) : 1f;

        Vector3 weightedSum = Vector3.zero;
        float influenceSum = 0f;
        float total = 0f;

        for (int i = 0; i < occupants.Count; i++)
        {
            var source = occupants[i];
            if (source == null || !source.Contributes) continue;

            float w = source.Weight;
            if (w <= 0f) continue;

            Vector3 offset = source.WorldPosition - pivotPosition;
            offset.y = 0f;

            float influence = w;
            if (distanceSquaredInfluence)
            {
                float ratio = Mathf.Min(offset.magnitude / radius, 1f);
                influence = w * (ratio * ratio + centerInfluence);
            }

            weightedSum += offset * influence;
            influenceSum += influence;
            total += w;
        }

        TotalWeight = total;
        if (influenceSum <= Mathf.Epsilon) return Vector3.zero;

        return weightedSum / influenceSum;
    }

    /// <summary>외부 시스템이 무게중심을 밀어낼 때 사용.</summary>
    public void SetExternalBias(Vector3 bias)
    {
        bias.y = 0f;
        externalBias = bias;
    }

    private void OnDisable()
    {
        occupants.Clear();
        visited.Clear();
        frontier.Clear();
    }
}
