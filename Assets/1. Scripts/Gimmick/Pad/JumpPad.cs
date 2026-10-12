using Fusion;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 점프대. 올라온 대상을 쏘아 올림.
/// 높이, 수평으로 미는 세기, 들어온 기세의 유지 정도를 각각 따로 조절함.
/// 패드를 기울이면 그쪽으로 밀고, 평평하면 본체 forward로 밂. 기울기는 방향만 정하고 세기와는 무관함.
///
/// 이미 쏘아 올려진 상태면 다시 쏘지 않음. 상태 없이 현재 속도만 보고 판단함.
/// 대상 탐색과 네트워크 처리는 WheelPadBase가 담당함.
/// </summary>
[DefaultExecutionOrder(110)]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(BoxCollider))]
public class JumpPad : WheelPadBase
{
    // 이보다 덜 기울었으면 평평한 것으로 봄
    private const float FlatThreshold = 1f;

    [Header("Jump")]
    [Tooltip("뛰어오르는 높이(m). 구체 중심 기준. 다른 값과 무관하게 항상 이 높이까지 뜀.")]
    [Min(0f)][SerializeField] private float jumpHeight = 6f;

    [Tooltip("패드가 수평으로 미는 속도(m/s). 높이와 무관함. 0이면 위로만 튐.")]
    [Min(0f)][SerializeField] private float horizontalSpeed = 0f;

    [Tooltip("들어온 수평 속도를 얼마나 남길지.\n" +
             "0: 진입 속도와 무관하게 항상 같은 곳에 떨어짐. 역방향 패드에 적합.\n" +
             "1: 굴러오던 기세가 그대로 더해짐. 앞으로 날려주는 도약대에 적합.")]
    [Range(0f, 1f)][SerializeField] private float momentumKeep = 1f;

    [Tooltip("발사 후 회전을 새 진행 방향에 맞춤.\n" +
             "비활성화시 원래 돌던 방향으로 계속 돌아, 착지하는 순간 원래 방향으로 다시 굴러감.")]
    [SerializeField] private bool matchSpin = true;

    [Tooltip("수직 속도가 발사 속도의 이 비율보다 낮을 때만 다시 쏨.\n" +
             "높이면 걸쳐 있는 동안 여러 번 쏠 수 있고, 낮추면 확실히 한 번만 쏨.")]
    [Range(0.1f, 0.95f)][SerializeField] private float rearmFraction = 0.5f;

    /// <summary>수평으로 미는 방향. 기울어져 있으면 기울어진 쪽, 평평하면 본체 forward.</summary>
    public Vector3 PushDirection
    {
        get
        {
            Vector3 up = transform.up;
            if (Vector3.Angle(Vector3.up, up) > FlatThreshold)
            {
                Vector3 flat = new Vector3(up.x, 0f, up.z);
                if (flat.sqrMagnitude > 1e-6f) return flat.normalized;
            }
            return PadForward;
        }
    }

    /// <summary>설정 높이에 도달하는 수직 발사 속도(m/s).</summary>
    public float VerticalSpeed => Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);

    protected override void Affect(Rigidbody rb, float dt)
    {
        float vy = VerticalSpeed;
        Vector3 v = rb.linearVelocity;

        // 이미 쏘아 올려진 상태면 무시함
        if (v.y >= vy * rearmFraction) return;

        Vector3 horizontal = new Vector3(v.x, 0f, v.z) * momentumKeep + PushDirection * horizontalSpeed;
        rb.linearVelocity = horizontal + Vector3.up * vy;

        if (!matchSpin) return;

        float radius = SphereRadiusOf(rb);
        if (radius <= 0.01f) return;

        // 구르는 회전을 새 수평 속도에 맞춤. SphereDriver와 같은 축 규칙을 따름.
        // 수직축 회전(제자리 돌기)은 유지함.
        Vector3 yaw = Vector3.Project(rb.angularVelocity, Vector3.up);
        rb.angularVelocity = Vector3.Cross(horizontal, Vector3.up) / radius + yaw;
    }

#if UNITY_EDITOR
    private static readonly Color GizmoColor = new Color(1f, 0.75f, 0.2f, 0.95f);

    /// <summary>가만히 올라섰을 때의 발사 속도. 시각화용.</summary>
    private Vector3 StandingLaunch => PushDirection * horizontalSpeed + Vector3.up * VerticalSpeed;

    private void OnDrawGizmos()
    {
        // 쏘는 방향. 항상 표시함.
        Vector3 dir = StandingLaunch;
        if (dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();

        Vector3 from = transform.position;
        Vector3 to = from + dir * 2.5f;

        Gizmos.color = GizmoColor;
        Gizmos.DrawLine(from, to);

        Vector3 a = Vector3.Cross(dir, Vector3.right);
        if (a.sqrMagnitude < 1e-4f) a = Vector3.Cross(dir, Vector3.forward);
        a = a.normalized * 0.35f;
        Vector3 b = Vector3.Cross(dir, a).normalized * 0.35f;

        Gizmos.DrawLine(to, to - dir * 0.6f + a);
        Gizmos.DrawLine(to, to - dir * 0.6f - a);
        Gizmos.DrawLine(to, to - dir * 0.6f + b);
        Gizmos.DrawLine(to, to - dir * 0.6f - b);
    }

    private void OnDrawGizmosSelected()
    {
        float g = Mathf.Abs(Physics.gravity.y);
        if (g < 1e-4f) return;

        Vector3 start = transform.position;
        Vector3 v0 = StandingLaunch;
        Vector3 horizontal = new Vector3(v0.x, 0f, v0.z);
        float apexTime = v0.y / g;

        Vector3 apex = start + horizontal * apexTime + Vector3.up * jumpHeight;
        float ringRadius = Mathf.Max(0.5f, Mathf.Abs(transform.lossyScale.x) * 0.5f);

        Gizmos.color = GizmoColor;
        Handles.color = GizmoColor;

        DrawRing(apex, ringRadius);
        Gizmos.DrawLine(start, start + Vector3.up * jumpHeight);
        Handles.Label(apex + Vector3.up * 0.4f, $"최고 높이 {jumpHeight:F1}m");

        if (horizontalSpeed > 0f)
        {
            Vector3 land = start + horizontal * (apexTime * 2f);
            DrawRing(land, ringRadius);
            Gizmos.DrawLine(start + Vector3.up * jumpHeight, apex);

            // 기세를 남기지 않으면 진입 속도와 무관하게 항상 이 지점에 떨어짐
            string note = momentumKeep <= 0f ? "" : " (정지 진입 시)";
            Handles.Label(land + Vector3.up * 0.4f, $"착지 {Vector3.Distance(start, land):F1}m{note}");
        }
    }

    private static void DrawRing(Vector3 center, float radius)
    {
        const int segments = 32;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif
}