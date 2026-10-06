using Fusion;
using UnityEngine;

/// <summary>
/// 가속 장판. 올라가 있는 동안 장판의 forward 방향으로 계속 밀어줌.
/// 화살표 방향으로 들어가면 가속, 반대로 들어가면 감속, 옆으로 지나가면 경로가 틀어짐.
/// </summary>
[DefaultExecutionOrder(110)]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(BoxCollider))]
public class SpeedBoostPad : WheelPadBase
{
    [Header("Boost")]
    [Tooltip("장판 방향으로 가하는 가속도(m/s²).")]
    [SerializeField] private float acceleration = 25f;

    [Tooltip("장판 방향 속도가 이 값에 도달하면 더 밀지 않음(m/s). 0이면 제한 없음.\n" +
             "긴 장판에서 속도가 끝없이 붙는 것을 막음.")]
    [SerializeField] private float maxBoostSpeed = 30f;

    [Tooltip("미는 만큼 구름도 맞춰 줌. 비활성화시 밀리기만 하고 미끄러짐.")]
    [SerializeField] private bool matchRolling = true;

    /// <summary>미는 방향.</summary>
    public Vector3 BoostDirection => PadForward;

    protected override void Affect(Rigidbody rb, float dt)
    {
        Vector3 dir = PadForward;
        if (dir == Vector3.zero) return;

        float accel = acceleration;

        if (maxBoostSpeed > 0f)
        {
            float along = Vector3.Dot(rb.linearVelocity, dir);
            if (along >= maxBoostSpeed) return;

            accel = Mathf.Min(accel, (maxBoostSpeed - along) / dt);
        }

        rb.AddForce(dir * accel, ForceMode.Acceleration);

        if (!matchRolling) return;

        // SphereDriver와 같은 축 규칙을 따름. 가속도를 반지름으로 나누면 미끄러짐 없이 구름.
        float radius = SphereRadiusOf(rb);
        if (radius > 0.01f)
            rb.AddTorque(Vector3.Cross(dir, Vector3.up) * (accel / radius), ForceMode.Acceleration);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos() => DrawDirectionGizmo(new Color(0.2f, 0.9f, 1f, 0.9f));
#endif
}