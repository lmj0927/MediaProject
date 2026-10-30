using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HUD용 코스 경로. 자식 오브젝트(지점)들을 하이어라키 순서대로 부드러운 곡선으로 이은 것.
/// 곡선은 모든 지점을 정확히 지나감(centripetal Catmull-Rom). sharpCorner가 켜진 지점에서는 각지게 꺾임.
/// 맵 작업: 프리팹을 씬에 놓고 자식 지점을 옮기거나 Ctrl+D로 복제해서 늘리면 됨. 씬 뷰에 곡선이 그대로 보임.
/// 휠 위치는 높이까지 포함해서 곡선 위로 찾고, 진행 거리는 수평 거리로 잼.
/// </summary>
[DisallowMultipleComponent]
public class CourseRoute : MonoBehaviour
{
    /// <summary>한 피어(HUD)가 코스 위 어디쯤 있는지 기억하는 값. 프레임마다 Track으로 갱신함.</summary>
    public class Tracker
    {
        /// <summary>지금 위치(출발점부터 경로를 따라 잰 m). 뒤로 가면 줄어듦.</summary>
        public float along;
        /// <summary>지금까지 가장 멀리 간 위치(m). 지난 체크포인트(진행바 하한) 판정에 씀.</summary>
        public float best;
        /// <summary>경로에서 수평으로 떨어진 거리(m). 점프로 뜬 높이는 넣지 않음.</summary>
        public float offRoute;
        public bool initialized;
        public float reacquireTimer;
        public int version = -1;
    }

    [Tooltip("곡선을 잘게 나누는 간격(m). 작을수록 정확하지만 계산이 늘어남.")]
    [SerializeField] private float sampleSpacing = 1f;

    [Tooltip("휠 위치를 찾을 때 지금 위치에서 경로를 따라 뒤로 살펴볼 거리(m). 길이 붙어 있는 곳에서 엉뚱한 구간으로 튀지 않게 함.")]
    [SerializeField] private float searchBehind = 10f;

    [Tooltip("휠 위치를 찾을 때 지금 위치에서 경로를 따라 앞으로 살펴볼 거리(m).")]
    [SerializeField] private float searchAhead = 25f;

    [Tooltip("살펴본 범위에서 찾은 위치가 수평으로 이보다 멀면(m) 경로 전체에서 다시 찾아봄(리셋·리스폰 대응). 점프로 높이 뜬 건 치지 않음.")]
    [SerializeField] private float reacquireDistance = 12f;

    [Tooltip("경로 전체에서 찾은 쪽이 이 시간(초) 동안 계속 훨씬 가까워야 그쪽으로 옮김.")]
    [SerializeField] private float reacquireTime = 1f;

    [Tooltip("경로 위 가장 가까운 곳을 고를 때 높이 차이를 얼마나 쳐줄지(0~1). 위아래로 겹친 길을 구분하는 데 씀. 0이면 겹친 길을 구분 못 함.")]
    [Range(0f, 1f)]
    [SerializeField] private float verticalWeight = 0.5f;

    [Tooltip("결승점까지 남은 거리가 이보다 작으면(m) 도착으로 봄.")]
    [SerializeField] private float finishRadius = 8f;

    private readonly List<Vector3> samples = new();
    private readonly List<float> lengths = new();
    private readonly List<float> checkpointLengths = new();
    private readonly List<Transform> points = new();
    private int builtChildCount = -1;
    private int version;

    public int PointCount { get { EnsureBuilt(); return points.Count; } }
    public bool IsValid { get { EnsureBuilt(); return samples.Count >= 2; } }
    public float TotalLength { get { EnsureBuilt(); return lengths.Count > 0 ? lengths[lengths.Count - 1] : 0f; } }
    public float FinishRadius => finishRadius;

    /// <summary>체크포인트 위치(출발점부터 m). 출발점은 빠지고 결승점은 항상 마지막에 들어감.</summary>
    public IReadOnlyList<float> CheckpointLengths { get { EnsureBuilt(); return checkpointLengths; } }

    private void Awake() => Rebuild();

    /// <summary>지점이 바뀌었으면 곡선을 다시 만듦. 실행 중에는 자식 개수가 바뀔 때만 다시 만듦.</summary>
    private void EnsureBuilt()
    {
        if (builtChildCount != transform.childCount) Rebuild();
    }

    /// <summary>자식 지점들로 곡선을 다시 만듦.</summary>
    public void Rebuild()
    {
        points.Clear();
        foreach (Transform child in transform)
            if (child.gameObject.activeSelf) points.Add(child);
        builtChildCount = transform.childCount;
        version++;

        samples.Clear();
        lengths.Clear();
        checkpointLengths.Clear();
        if (points.Count < 2) return;

        samples.Add(points[0].position);
        lengths.Add(0f);
        float step = Mathf.Max(0.2f, sampleSpacing);

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 p1 = points[i].position, p2 = points[i + 1].position;
            // 끝점이거나 각진 코너면 바깥쪽 조절점을 구간 반대편으로 둬서 그 지점에서 곡선이 꺾이게 함
            Vector3 p0 = i > 0 && !IsSharp(i) ? points[i - 1].position : p1 + (p1 - p2);
            Vector3 p3 = i + 2 < points.Count && !IsSharp(i + 1) ? points[i + 2].position : p2 + (p2 - p1);

            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(p1, p2) / step));
            for (int s = 1; s <= steps; s++)
            {
                Vector3 position = s == steps ? p2 : CatmullRom(p0, p1, p2, p3, s / (float)steps);
                lengths.Add(lengths[lengths.Count - 1] + Flat(position - samples[samples.Count - 1]).magnitude);
                samples.Add(position);
            }

            bool last = i + 1 == points.Count - 1;
            if (last || IsCheckpoint(i + 1)) checkpointLengths.Add(lengths[lengths.Count - 1]);
        }
    }

    private bool IsSharp(int index) => points[index].TryGetComponent(out CoursePoint p) && p.sharpCorner;
    private bool IsCheckpoint(int index) => points[index].TryGetComponent(out CoursePoint p) && p.checkpoint;

    /// <summary>centripetal Catmull-Rom. t는 p1(0)~p2(1). 지점 간격이 들쭉날쭉해도 곡선이 덜 튀어나감.</summary>
    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t0 = 0f;
        float t1 = t0 + Mathf.Max(1e-3f, Mathf.Sqrt(Vector3.Distance(p0, p1)));
        float t2 = t1 + Mathf.Max(1e-3f, Mathf.Sqrt(Vector3.Distance(p1, p2)));
        float t3 = t2 + Mathf.Max(1e-3f, Mathf.Sqrt(Vector3.Distance(p2, p3)));
        float u = Mathf.Lerp(t1, t2, t);

        Vector3 a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
        Vector3 a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
        Vector3 a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;
        Vector3 b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2;
        Vector3 b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3;
        return (t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2;
    }

    /// <summary>
    /// 위치를 경로 위로 찾아 tracker를 갱신함.
    /// 평소에는 지금 위치 앞뒤 일정 범위 안에서만 찾고, 거기서 너무 멀어진 상태가 reacquireTime 동안 이어지면 경로 전체에서 다시 찾음.
    /// </summary>
    public void Track(Vector3 position, float dt, Tracker tracker)
    {
        EnsureBuilt();
        if (samples.Count < 2) return;

        if (!tracker.initialized || tracker.version != version)
        {
            tracker.along = tracker.best = Nearest(position, 0f, TotalLength, out _, out tracker.offRoute);
            tracker.initialized = true;
            tracker.reacquireTimer = 0f;
            tracker.version = version;
            return;
        }

        float along = Nearest(position, tracker.along - searchBehind, tracker.along + searchAhead, out float distance, out float flatDistance);
        bool jumped = false;
        // 벗어났는지는 수평 거리로만 봄. 점프로 높이 뜬 동안 위치가 멈춰서 나침반이 뒤를 가리키던 문제를 막음
        if (flatDistance > reacquireDistance)
        {
            float anywhere = Nearest(position, 0f, TotalLength, out float anywhereDistance, out float anywhereFlat);
            if (anywhereDistance < distance * 0.5f)
            {
                tracker.reacquireTimer += dt;
                if (tracker.reacquireTimer >= reacquireTime)
                {
                    along = anywhere;
                    flatDistance = anywhereFlat;
                    jumped = true;
                    tracker.reacquireTimer = 0f;
                }
            }
            else tracker.reacquireTimer = 0f;

            // 길에서 멀리 벗어난 동안은 범위 안 가장 가까운 곳이 프레임마다 끌려다니므로 위치를 그대로 둠
            if (!jumped) along = tracker.along;
        }
        else tracker.reacquireTimer = 0f;

        tracker.along = along;
        tracker.offRoute = flatDistance;
        // 가장 멀리 간 곳은 줄지 않음. 단 다른 곳으로 옮겨졌을 때(리셋·리스폰)는 그 자리부터 다시 셈
        tracker.best = jumped ? along : Mathf.Max(tracker.best, along);
    }

    /// <summary>
    /// 경로를 따라 [from, to] 범위 안에서 위치와 가장 가까운 곳을 찾아 그 경로 거리를 돌려줌.
    /// distance는 높이를 verticalWeight만큼 친 거리(고르는 기준), flatDistance는 그곳까지의 수평 거리.
    /// </summary>
    private float Nearest(Vector3 position, float from, float to, out float distance, out float flatDistance)
    {
        flatDistance = 0f;
        int first = Mathf.Max(0, IndexAt(from));
        int last = Mathf.Min(samples.Count - 2, IndexAt(to));
        distance = float.MaxValue;
        float result = from;
        for (int i = first; i <= last; i++)
        {
            Vector3 a = samples[i], ab = samples[i + 1] - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(position - a, ab) / ab.sqrMagnitude);
            Vector3 delta = position - (a + ab * t);
            delta.y *= verticalWeight;
            float d = delta.sqrMagnitude;
            if (d < distance)
            {
                distance = d;
                flatDistance = Flat(position - (a + ab * t)).magnitude;
                result = Mathf.Lerp(lengths[i], lengths[i + 1], t);
            }
        }
        distance = Mathf.Sqrt(distance);
        return result;
    }

    /// <summary>경로 거리 s가 들어 있는 샘플 구간의 시작 번호.</summary>
    private int IndexAt(float s)
    {
        int lo = 0, hi = lengths.Count - 1;
        if (s <= 0f) return 0;
        if (s >= lengths[hi]) return hi - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (lengths[mid] <= s) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>출발점부터 경로를 따라 s(m) 간 곳의 위치.</summary>
    public Vector3 PointAtLength(float s)
    {
        EnsureBuilt();
        if (samples.Count == 0) return transform.position;
        if (samples.Count == 1) return samples[0];
        s = Mathf.Clamp(s, 0f, TotalLength);
        int i = IndexAt(s);
        float span = lengths[i + 1] - lengths[i];
        // 높이만 바뀌는 구간(엘리베이터 등)은 수평 길이가 0이라 그 끝을 돌려줌
        return span < 1e-4f ? samples[i + 1] : Vector3.Lerp(samples[i], samples[i + 1], (s - lengths[i]) / span);
    }

    /// <summary>tracker 기준 다음 체크포인트의 경로 거리. 다 지났으면 결승점.</summary>
    public float NextCheckpoint(Tracker tracker)
    {
        EnsureBuilt();
        foreach (float c in checkpointLengths)
            if (c > tracker.best + 0.01f) return c;
        return TotalLength;
    }

    /// <summary>tracker 기준 마지막으로 지난 체크포인트의 경로 거리. 아직 없으면 0(출발점). 진행바의 하한으로 씀.</summary>
    public float PassedCheckpoint(Tracker tracker)
    {
        EnsureBuilt();
        float passed = 0f;
        foreach (float c in checkpointLengths)
            if (c <= tracker.best + 0.01f) passed = c;
        return passed;
    }

    public bool IsFinished(Tracker tracker) => tracker.initialized && tracker.best >= TotalLength - finishRadius;

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    [ContextMenu("끝에 지점 추가")]
    private void AddPointAtEnd()
    {
        Vector3 at = transform.position, dir = Vector3.forward;
        int count = transform.childCount;
        if (count > 0) at = transform.GetChild(count - 1).position;
        if (count > 1) dir = Flat(at - transform.GetChild(count - 2).position).normalized;
        var go = new GameObject($"Point {count}", typeof(CoursePoint));
#if UNITY_EDITOR
        UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Add Course Point");
#endif
        go.transform.SetParent(transform, false);
        go.transform.position = at + dir * 10f;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // 편집 중에는 지점을 옮기면 바로 보이게 매번 다시 만듦
        if (!Application.isPlaying) Rebuild();
        if (samples.Count < 2) return;

        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        for (int i = 1; i < samples.Count; i++) Gizmos.DrawLine(samples[i - 1], samples[i]);

        var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel) { normal = { textColor = Color.white } };
        for (int i = 0; i < points.Count; i++)
        {
            string label = i == 0 ? "출발" : i == points.Count - 1 ? "결승" : IsCheckpoint(i) ? $"{i} 체크" : $"{i}";
            UnityEditor.Handles.Label(points[i].position + Vector3.up * 2.5f, label, style);
        }
    }
#endif
}
