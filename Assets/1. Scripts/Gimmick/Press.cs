using Fusion;
using UnityEngine;

/// <summary>
/// 프레스. 머리가 주기적으로 내려찍고, 내려오는 동안 아래에 있는 휠을 가까운 쪽으로 밀어냄(기본은 앞뒤).
/// 깔려서 끝나는 판정은 없음. 머리 높이는 시뮬레이션 시간으로 정함. 모든 피어가 같은 높이를 봄.
/// 판정과 밀어내기는 Host State Authority의 FixedUpdateNetwork에서 함. 머리 모델에는 콜라이더를 두지 않음.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Press : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("움직이는 머리(메시만). 이 오브젝트의 자식.")]
    [SerializeField] private Transform head;

    [Header("Cycle")]
    [Tooltip("한 번 찍고 돌아오는 데 걸리는 시간(초).")]
    [Min(0.5f)][SerializeField] private float period = 4f;

    [Tooltip("0~1. 여러 프레스의 박자를 엇갈리게 할 때.")]
    [Range(0f, 1f)][SerializeField] private float phaseOffset = 0f;

    [Tooltip("주기 중 위에서 기다리는 비율.")]
    [Range(0f, 1f)][SerializeField] private float holdUp = 0.45f;

    [Tooltip("주기 중 내려찍는 비율.")]
    [Range(0.01f, 1f)][SerializeField] private float slam = 0.1f;

    [Tooltip("주기 중 아래에서 머무는 비율. 나머지는 올라가는 시간.")]
    [Range(0f, 1f)][SerializeField] private float holdDown = 0.15f;

    [Tooltip("머리가 내려가는 거리(m).")]
    [Min(0f)][SerializeField] private float travel = 14f;

    [Header("Push")]
    [Tooltip("머리 아래 판정 범위(가로, 앞뒤)의 절반(m). 이 오브젝트의 X, Z 방향.")]
    [SerializeField] private Vector2 halfArea = new Vector2(10f, 6f);

    [Tooltip("판정 범위 중심(m). 이 오브젝트 기준 X, Z. 머리가 몸체 옆으로 나와 있는 프레스용.")]
    [SerializeField] private Vector2 areaOffset = Vector2.zero;

    [Tooltip("이 오브젝트의 X 방향(앞뒤 대신 좌우)으로 밀어냄.")]
    [SerializeField] private bool pushAlongX = false;

    [Tooltip("올라가 있을 때 머리 아랫면 높이(m). 이 오브젝트 위치 기준.")]
    [SerializeField] private float headBottom = 16f;

    [Tooltip("휠 윗면 위로 이만큼(m) 안에 머리가 내려오면 밀어냄. 판과 사람 높이.")]
    [Min(0f)][SerializeField] private float clearance = 8f;

    [Tooltip("밀어내는 수평 속도(m/s).")]
    [Min(0f)][SerializeField] private float pushSpeed = 10f;

    [Tooltip("밀어낼 때 위로 띄우는 속도(m/s).")]
    [Min(0f)][SerializeField] private float lift = 2f;

    [SerializeField] private LayerMask detectionMask = ~0;

    private Vector3 headBase;
    private readonly Collider[] hits = new Collider[16];

    /// <summary>0이면 위, 1이면 끝까지 내려온 상태.</summary>
    public float Down(float time)
    {
        float u = Mathf.Repeat(time / period + phaseOffset, 1f);
        if (u < holdUp) return 0f;
        u -= holdUp;
        if (u < slam) { float k = u / slam; return k * k; }
        u -= slam;
        if (u < holdDown) return 1f;
        u -= holdDown;
        float rise = Mathf.Max(0.01f, 1f - holdUp - slam - holdDown);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / rise));
    }

    public override void Spawned()
    {
        if (head != null) headBase = head.localPosition;
    }

    public override void FixedUpdateNetwork()
    {
        float time = (float)Runner.SimulationTime;
        float down = Down(time);
        Pose(down);
        if (!Object.HasStateAuthority || down <= 0f) return;

        float bottom = transform.position.y + headBottom - travel * down;
        Vector3 center = transform.TransformPoint(new Vector3(areaOffset.x, 0f, areaOffset.y)) + Vector3.up * (bottom - transform.position.y) * 0.5f;
        Vector3 half = new Vector3(halfArea.x + 6f, Mathf.Max(1f, (bottom - transform.position.y) * 0.5f + 6f), halfArea.y + 6f);
        int count = Physics.OverlapBoxNonAlloc(center, half, hits, transform.rotation, detectionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var rb = hits[i].attachedRigidbody;
            if (rb == null || rb.isKinematic || !rb.TryGetComponent<SphereDriver>(out _)) continue;
            if (!rb.TryGetComponent<SphereCollider>(out var sphere)) continue;

            float radius = sphere.radius * Mathf.Max(rb.transform.lossyScale.x, rb.transform.lossyScale.y, rb.transform.lossyScale.z);
            Vector3 c = sphere.transform.TransformPoint(sphere.center);
            if (bottom > c.y + radius + clearance) continue;

            Vector3 local = transform.InverseTransformPoint(c) - new Vector3(areaOffset.x, 0f, areaOffset.y);
            if (Mathf.Abs(local.x) > halfArea.x + (pushAlongX ? radius * 0.5f : 0f) || Mathf.Abs(local.z) > halfArea.y + (pushAlongX ? 0f : radius * 0.5f)) continue;

            // 머리 아래에서 가까운 쪽으로 밀어냄
            Vector3 axis = pushAlongX ? transform.right : transform.forward; axis.y = 0f; axis.Normalize();
            Vector3 dir = (pushAlongX ? local.x : local.z) >= 0f ? axis : -axis;
            Vector3 v = rb.linearVelocity;
            float along = Vector3.Dot(v, dir);
            if (along < pushSpeed) v += dir * (pushSpeed - along);
            if (v.y < lift) v.y = lift;
            rb.linearVelocity = v;
            Vector3 horizontal = new Vector3(v.x, 0f, v.z);
            Vector3 yaw = Vector3.Project(rb.angularVelocity, Vector3.up);
            rb.angularVelocity = Vector3.Cross(Vector3.up, horizontal) / radius + yaw;
        }
    }

    public override void Render()
    {
        Pose(Down((float)Runner.SimulationTime + Runner.DeltaTime * Runner.LocalAlpha));
    }

    private void Pose(float down)
    {
        if (head == null || head.parent == null) return;
        head.localPosition = headBase + head.parent.InverseTransformVector(Vector3.down * (travel * down));
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(new Vector3(areaOffset.x, 0f, areaOffset.y)) + transform.up * headBottom, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfArea.x * 2f, 0.2f, halfArea.y * 2f));
        Gizmos.DrawWireCube(Vector3.down * travel, new Vector3(halfArea.x * 2f, 0.2f, halfArea.y * 2f));
    }
#endif
}
