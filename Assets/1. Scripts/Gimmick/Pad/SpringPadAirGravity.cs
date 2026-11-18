using UnityEngine;

/// <summary>
/// 스프링 점프 패드가 쏜 휠에 공중에 떠 있는 동안만 중력을 더 걸어 줌. 빠르게 솟았다가 빠르게 떨어지게 함.
/// 패드가 휠을 쏘는 Host에서만 동작함.
/// </summary>
[RequireComponent(typeof(JumpPad))]
public class SpringPadAirGravity : MonoBehaviour
{
    [Tooltip("공중에 있는 동안의 중력 배율. 1이면 그대로.")]
    [Min(1f)][SerializeField] private float gravityScale = 3f;

    [Tooltip("쏜 뒤 이 시간(초)이 지나면 땅에 닿지 않았어도 그만둠.")]
    [Min(0.1f)][SerializeField] private float maxTime = 5f;

    private JumpPad pad;
    private Rigidbody body;
    private SphereDriver driver;
    private float launchTime;
    private float lastTime;
    private Vector3 lastPosition;

    private void Awake() => pad = GetComponent<JumpPad>();

    private void OnEnable() => pad.Launched += OnLaunched;

    private void OnDisable() => pad.Launched -= OnLaunched;

    private void OnLaunched(JumpPad source, Rigidbody launched)
    {
        if (!launched.TryGetComponent(out driver)) return;
        body = launched;
        launchTime = lastTime = Time.time;
        lastPosition = body.position;
    }

    private void FixedUpdate()
    {
        if (body == null) return;
        float now = Time.time;
        float elapsed = now - launchTime;

        // 땅에 닿았거나, 리스폰으로 순간 이동했거나, 너무 오래 지났으면 끝냄
        bool landed = elapsed > 0.3f && driver.IsGrounded;
        bool moved = (body.position - lastPosition).sqrMagnitude > 30f * 30f;
        if (landed || moved || elapsed > maxTime)
        {
            body = null;
            return;
        }

        body.linearVelocity += Physics.gravity * ((gravityScale - 1f) * (now - lastTime));
        lastTime = now;
        lastPosition = body.position;
    }
}
