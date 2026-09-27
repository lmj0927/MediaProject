using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가속 장판. 올라가 있는 동안 장판의 forward 방향으로 계속 밀어줌.
/// 화살표 방향으로 들어가면 가속, 반대로 들어가면 감속, 옆으로 지나가면 경로가 틀어짐.
///
/// 트리거 콜라이더가 필요함. 크기는 부모 X/Z 스케일로 조절하고, 콜라이더는 가로세로 1을 유지할 것.
/// 표면 흐름은 PadSurface가 담당함.
/// 장판을 벗어나면 SphereDriver가 원래 목표 속도로 되돌리므로 부스트 효과는 서서히 사라짐.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SpeedBoostPad : MonoBehaviour
{
    [Header("Boost")]
    [Tooltip("장판 방향으로 가하는 가속도(m/s²).")]
    [SerializeField] private float acceleration = 25f;

    [Tooltip("장판 방향 속도가 이 값에 도달하면 더 밀지 않음(m/s). 0이면 제한 없음.\n" +
             "긴 장판에서 속도가 끝없이 붙는 것을 막음.")]
    [SerializeField] private float maxBoostSpeed = 30f;

    [Tooltip("미는 만큼 구름도 맞춰 줌. 비활성화시 밀리기만 하고 미끄러짐.")]
    [SerializeField] private bool matchRolling = true;

    [Tooltip("휠 외의 Rigidbody도 밀어냄. 땅에 떨어진 화물 등.")]
    [SerializeField] private bool affectOtherBodies = false;

    private readonly HashSet<Rigidbody> bodies = new();
    private readonly List<Rigidbody> scratch = new();

    /// <summary>미는 방향. 장판의 forward를 수평으로 눕힌 값.</summary>
    public Vector3 BoostDirection
    {
        get
        {
            Vector3 d = transform.forward;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.zero;
        }
    }

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;

        if (col is BoxCollider box)
        {
            // 부모 스케일에 따라 늘어나므로 가로세로는 1로 둠. 높이만 직접 지정.
            box.size = new Vector3(1f, 2f, 1f);
            box.center = new Vector3(0f, 1f, 0f);
        }
    }

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (IsTarget(rb)) bodies.Add(rb);
    }

    private void OnTriggerExit(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb != null) bodies.Remove(rb);
    }

    private bool IsTarget(Rigidbody rb)
    {
        if (rb == null || rb.isKinematic) return false;
        return affectOtherBodies || rb.GetComponent<SphereDriver>() != null;
    }

    private void FixedUpdate()
    {
        if (bodies.Count == 0) return;

        Vector3 dir = BoostDirection;
        if (dir == Vector3.zero) return;

        float dt = Time.fixedDeltaTime;

        scratch.Clear();
        scratch.AddRange(bodies);

        foreach (var rb in scratch)
        {
            // Exit 신호 없이 사라지거나 비활성화된 대상 정리
            if (rb == null || !rb.gameObject.activeInHierarchy || rb.isKinematic)
            {
                bodies.Remove(rb);
                continue;
            }

            Push(rb, dir, dt);
        }
    }

    private void Push(Rigidbody rb, Vector3 dir, float dt)
    {
        float accel = acceleration;

        if (maxBoostSpeed > 0f)
        {
            float along = Vector3.Dot(rb.linearVelocity, dir);
            if (along >= maxBoostSpeed) return;

            // 한 스텝에 상한을 넘지 않게 자름
            accel = Mathf.Min(accel, (maxBoostSpeed - along) / dt);
        }

        rb.AddForce(dir * accel, ForceMode.Acceleration);

        if (matchRolling && rb.TryGetComponent<SphereCollider>(out var sphere))
        {
            Vector3 s = rb.transform.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

            // 구르는 방향에 수직인 수평축. 가속도를 반지름으로 나누면 미끄러짐 없이 구름.
            if (radius > 0.01f)
                rb.AddTorque(Vector3.Cross(Vector3.up, dir) * (accel / radius), ForceMode.Acceleration);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Vector3 dir = BoostDirection;
        if (dir == Vector3.zero) return;

        Vector3 from = transform.position + Vector3.up * 0.1f;
        Vector3 to = from + dir * 2f;

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Gizmos.DrawLine(from, to);

        Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.4f;
        Gizmos.DrawLine(to, to - dir * 0.6f + side);
        Gizmos.DrawLine(to, to - dir * 0.6f - side);
    }
#endif
}