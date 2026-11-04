using Fusion;
using UnityEngine;

/// <summary>
/// 회전 막대. 기둥을 축으로 막대가 수평으로 돌고, 닿은 휠을 막대가 움직이는 쪽으로 밀어냄.
/// 각도는 시뮬레이션 시간으로 정함. 모든 피어가 같은 각도를 봄.
/// 판정과 밀어내기는 Host State Authority의 FixedUpdateNetwork에서 함. 막대 모델에는 콜라이더를 두지 않음.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Sweeper : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("도는 부분(메시만). 이 오브젝트의 자식.")]
    [SerializeField] private Transform arm;

    [Header("Rotation")]
    [Tooltip("회전 속도(도/초). 음수면 반대로 돎.")]
    [SerializeField] private float speed = 60f;

    [Tooltip("시작 각도(도). 여러 막대의 박자를 엇갈리게 할 때.")]
    [SerializeField] private float phase = 0f;

    [Header("Shape")]
    [Tooltip("축에서 막대 끝까지 거리(m).")]
    [Min(0.1f)][SerializeField] private float length = 12f;

    [Tooltip("막대 굵기의 절반(m).")]
    [Min(0f)][SerializeField] private float halfThickness = 1.3f;

    [Tooltip("막대 윗면 높이(m). 이 오브젝트 위치 기준.")]
    [Min(0f)][SerializeField] private float height = 5f;

    [Tooltip("축 양쪽으로 뻗은 막대.")]
    [SerializeField] private bool twoSided = true;

    [Header("Push")]
    [Tooltip("튕겨 나가는 정도. 0이면 막대 속도만큼만 밀리고, 1이면 그 두 배로 튕김.")]
    [Range(0f, 1f)][SerializeField] private float restitution = 0.3f;

    [Tooltip("막대 속도와 무관하게 밀어내는 최소 속도(m/s).")]
    [Min(0f)][SerializeField] private float minPush = 3f;

    [Tooltip("밀어낸 뒤 회전을 새 진행 방향에 맞춤.")]
    [SerializeField] private bool matchSpin = true;

    [SerializeField] private LayerMask detectionMask = ~0;

    private Quaternion armBase;
    private readonly Collider[] hits = new Collider[16];

    public float Speed => speed;

    private float AngleAt(float time) => phase + speed * time;

    /// <summary>막대 방향(수평).</summary>
    private Vector3 Direction(float time) => transform.rotation * (Quaternion.AngleAxis(AngleAt(time), Vector3.up) * Vector3.right);

    public override void Spawned()
    {
        if (arm != null) armBase = arm.localRotation;
    }

    public override void FixedUpdateNetwork()
    {
        float time = (float)Runner.SimulationTime;
        Pose(time);
        if (!Object.HasStateAuthority) return;

        Vector3 center = transform.position;
        Vector3 dir = Direction(time);
        int count = Physics.OverlapSphereNonAlloc(center + Vector3.up * height * 0.5f, length + 8f, hits, detectionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var rb = hits[i].attachedRigidbody;
            if (rb == null || rb.isKinematic || !rb.TryGetComponent<SphereDriver>(out _)) continue;
            if (!rb.TryGetComponent<SphereCollider>(out var sphere)) continue;
            Push(rb, sphere, center, dir);
        }
    }

    public override void Render()
    {
        Pose((float)Runner.SimulationTime + Runner.DeltaTime * Runner.LocalAlpha);
    }

    private void Pose(float time)
    {
        if (arm != null) arm.localRotation = Quaternion.AngleAxis(AngleAt(time), Vector3.up) * armBase;
    }

    /// <summary>휠이 막대에 닿았으면 막대가 움직이는 쪽으로 밀어냄.</summary>
    private void Push(Rigidbody rb, SphereCollider sphere, Vector3 center, Vector3 dir)
    {
        float radius = sphere.radius * Mathf.Max(rb.transform.lossyScale.x, rb.transform.lossyScale.y, rb.transform.lossyScale.z);
        Vector3 c = sphere.transform.TransformPoint(sphere.center);

        // 높이: 막대 높이 범위와 구가 겹치는지
        if (c.y - radius > center.y + height || c.y + radius < center.y) return;

        // 수평면에서 막대(선분)와 구 중심의 가장 가까운 점
        Vector3 rel = c - center; rel.y = 0f;
        float t = Vector3.Dot(rel, dir);
        t = twoSided ? Mathf.Clamp(t, -length, length) : Mathf.Clamp(t, 0f, length);
        Vector3 closest = dir * t;
        Vector3 away = rel - closest;
        float distance = away.magnitude;

        // 막대 높이에서 구의 단면 반지름
        float dy = Mathf.Clamp(c.y, center.y, center.y + height) - c.y;
        float slice = Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
        if (distance >= slice + halfThickness) return;

        Vector3 n = distance > 1e-4f ? away / distance : Vector3.Cross(Vector3.up, dir) * Mathf.Sign(speed);
        // 닿은 지점의 막대 속도
        Vector3 barVelocity = Vector3.Cross(Vector3.up * (speed * Mathf.Deg2Rad), closest);
        float approach = Vector3.Dot(barVelocity, n);
        if (approach <= 0f) { approach = 0f; }

        Vector3 v = rb.linearVelocity;
        float current = Vector3.Dot(v, n);
        float target = Mathf.Max(approach * (1f + restitution), minPush);
        if (current < target) v += n * (target - current);
        rb.linearVelocity = v;
        rb.position += n * (slice + halfThickness - distance);

        if (!matchSpin) return;
        Vector3 horizontal = new Vector3(v.x, 0f, v.z);
        Vector3 yaw = Vector3.Project(rb.angularVelocity, Vector3.up);
        rb.angularVelocity = Vector3.Cross(Vector3.up, horizontal) / radius + yaw;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);
        Vector3 center = transform.position + Vector3.up * height;
        Vector3 dir = Direction(0f);
        Gizmos.DrawLine(twoSided ? center - dir * length : center, center + dir * length);
        UnityEditor.Handles.color = Gizmos.color;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, length);
    }
#endif
}
