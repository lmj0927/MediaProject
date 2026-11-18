using Fusion;
using UnityEngine;

/// <summary>
/// 측면 피스톤. 길 옆 받침에서 기둥이 주기적으로 튀어나와 길을 막고, 튀어나오는 기둥에 걸린 휠을 기둥이 나가는 쪽으로 날림.
/// 기둥 위치는 세션 시각으로 계산해서 모든 피어가 같은 박자를 봄. 휠을 날리는 것은 Host에서만 함.
/// 기둥은 이 오브젝트의 Z 방향으로 나감.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class SidePiston : MonoBehaviour
{
    [Header("References")]
    [Tooltip("튀어나오는 기둥 마디들. 받침에서 가까운 쪽부터 차례로 밀려 나옴. 각 마디는 Z 길이가 segmentLength인 모델.")]
    [SerializeField] private Transform[] segments = System.Array.Empty<Transform>();

    [Tooltip("기둥 마디 하나의 길이(m).")]
    [Min(0.1f)][SerializeField] private float segmentLength = 12f;

    [Tooltip("나온 기둥만큼 늘어나는 충돌 상자.")]
    [SerializeField] private BoxCollider body;

    [Header("Cycle")]
    [Tooltip("한 번 나왔다 들어가는 주기(초).")]
    [Min(0.5f)][SerializeField] private float period = 6f;

    [Tooltip("주기 시작 위치(0~1). 여러 피스톤의 박자를 엇갈리게 할 때.")]
    [Range(0f, 1f)][SerializeField] private float phaseOffset;

    [Tooltip("튀어나오기 전에 기둥 끝이 살짝 나오며 예고하는 시간(초).")]
    [Min(0f)][SerializeField] private float warnTime = 0.7f;

    [Tooltip("튀어나오는 데 걸리는 시간(초).")]
    [Min(0.05f)][SerializeField] private float extendTime = 1.1f;

    [Tooltip("끝까지 나온 채로 머무는 시간(초).")]
    [Min(0f)][SerializeField] private float holdTime = 0.8f;

    [Tooltip("들어가는 데 걸리는 시간(초).")]
    [Min(0.05f)][SerializeField] private float retractTime = 1.6f;

    [Header("Shape")]
    [Tooltip("기둥이 나가는 거리(m).")]
    [Min(0f)][SerializeField] private float travel = 44f;

    [Tooltip("예고할 때 기둥 끝이 나오는 거리(m).")]
    [Min(0f)][SerializeField] private float warnPeek = 1.5f;

    [Tooltip("기둥 너비의 절반(m). 이 오브젝트의 X 방향.")]
    [Min(0.1f)][SerializeField] private float halfWidth = 6f;

    [Header("Push")]
    [Tooltip("기둥에 걸린 휠을 날리는 최소 속도(m/s). 기둥이 나가는 속도가 더 빠르면 그 속도로 밀림.")]
    [Min(0f)][SerializeField] private float pushSpeed = 26f;

    [Tooltip("날릴 때 위로 띄우는 속도(m/s).")]
    [Min(0f)][SerializeField] private float lift = 4f;

    private NetworkObject networkObject;
    private SphereDriver wheel;
    private Rigidbody wheelBody;
    private SphereCollider wheelSphere;
    private float nextWheelSearch;
    private PlatformTilt platform;
    private float lastExtension;
    private bool soundPlayed;

    private void Awake()
    {
        networkObject = GetComponent<NetworkObject>();
    }

    /// <summary>기둥이 나온 정도(0~1)와 예고 정도(0~1), 나오는 중인지를 구함.</summary>
    private float Extension(float time, out float warn, out bool extending)
    {
        float t = Mathf.Repeat(time / period + phaseOffset, 1f) * period;
        warn = 0f;
        extending = false;
        if (t < extendTime) { extending = true; return t / extendTime; }
        t -= extendTime;
        if (t < holdTime) return 1f;
        t -= holdTime;
        if (t < retractTime) return 1f - Mathf.SmoothStep(0f, 1f, t / retractTime);
        float untilNext = period - extendTime - holdTime - t;
        if (untilNext < warnTime) warn = 1f - untilNext / Mathf.Max(warnTime, 0.01f);
        return 0f;
    }

    private bool TryGetTime(out float time)
    {
        time = 0f;
        if (networkObject == null || !networkObject.IsValid || networkObject.Runner == null) return false;
        time = (float)networkObject.Runner.SimulationTime;
        return true;
    }

    private void Update()
    {
        if (!TryGetTime(out float time)) return;
        Pose(Extension(time, out float warn, out _), warn);
    }

    private void Pose(float extension, float warn)
    {
        float along = travel * extension + warnPeek * warn;

        // 끝 마디부터 차례로 놓고, 받침 안에 남은 부분은 줄여서 감춤
        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment == null) continue;
            float front = along - i * segmentLength;
            bool shown = front > 0.01f;
            if (segment.gameObject.activeSelf != shown) segment.gameObject.SetActive(shown);
            if (!shown) continue;
            float back = Mathf.Max(0f, front - segmentLength);
            Vector3 p = segment.localPosition;
            segment.localPosition = new Vector3(p.x, p.y, (front + back) * 0.5f);
            segment.localScale = new Vector3(1f, 1f, (front - back) / segmentLength);
        }

        if (body == null) return;
        body.enabled = along > 0.2f;
        body.center = new Vector3(body.center.x, body.center.y, along * 0.5f);
        body.size = new Vector3(body.size.x, body.size.y, Mathf.Max(0.1f, along));
    }

    private void FixedUpdate()
    {
        if (!TryGetTime(out float time) || !networkObject.HasStateAuthority) return;
        float extension = Extension(time, out float warn, out bool extending);
        Pose(extension, warn);
        float faceSpeed = travel * Mathf.Max(0f, extension - lastExtension) / Mathf.Max(Time.fixedDeltaTime, 1e-4f);
        lastExtension = extension;
        if (!extending) { soundPlayed = false; return; }

        if (wheel == null)
        {
            if (Time.time < nextWheelSearch) return;
            nextWheelSearch = Time.time + 1f;
            wheel = FindFirstObjectByType<SphereDriver>();
            if (wheel == null) return;
            wheelBody = wheel.GetComponent<Rigidbody>();
            wheelSphere = wheel.GetComponent<SphereCollider>();
            platform = FindFirstObjectByType<PlatformTilt>();
        }

        // 기둥 끝이 판 가장자리에 닿는 것으로 봄. 휠은 기둥 끝보다 항상 앞에 있게 해서 기둥이 탈것을 뚫고 지나가지 않게 함
        float radius = wheelSphere.radius * wheel.transform.lossyScale.x;
        float reach = platform != null ? Mathf.Max(radius, platform.PlatformRadius) : radius;
        Vector3 local = transform.InverseTransformPoint(wheelSphere.transform.TransformPoint(wheelSphere.center));
        float face = travel * extension;
        if (Mathf.Abs(local.x) > halfWidth + reach * 0.6f || Mathf.Abs(local.y) > radius * 2f) return;
        if (local.z < -reach || local.z - reach > face) return;

        Vector3 dir = transform.forward;
        dir.y = 0f;
        dir.Normalize();
        float overlap = face - (local.z - reach);
        if (overlap > 0f) wheelBody.position += dir * overlap;

        Vector3 v = wheelBody.linearVelocity;
        Vector3 change = Vector3.zero;
        float target = Mathf.Max(faceSpeed, pushSpeed);
        float along = Vector3.Dot(v, dir);
        if (along < target) change += dir * (target - along);
        if (v.y < lift) change += Vector3.up * (lift - v.y);
        if (change.sqrMagnitude > 1e-4f) wheel.ApplyKnockback(change, false);
        if (!soundPlayed)
        {
            soundPlayed = true;
            wheel.ReportImpactSound(wheelBody.position - dir * radius, target);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, 4.5f, travel * 0.5f), new Vector3(halfWidth * 2f, 9f, travel));
    }
#endif
}
