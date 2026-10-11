using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 감속 구역. 방향과 무관하게 안에 있는 동안 속도를 제한함.
///
/// 구역 안의 최고 속도를 절대값으로 정함. 휠의 원래 속도 설정과 무관하게 "진흙에선 최대 몇"으로 동작함.
/// 표면 흐름은 PadSurface가 담당함.
/// 트리거 콜라이더가 필요함. 크기는 부모 X/Z 스케일로 조절하고, 콜라이더는 가로세로 1을 유지할 것.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SlowZone : MonoBehaviour
{
    [Header("Slow")]
    [Tooltip("구역 안에서의 수평 속도 상한(m/s).")]
    [SerializeField] private float maxSpeedInside = 6f;

    [Tooltip("상한까지 줄어드는 빠르기. 클수록 급정거에 가까움.")]
    [SerializeField] private float brakeRate = 3f;

    [Tooltip("상한 아래에서도 적용되는 저항. 0이면 상한 아래에선 영향 없음.")]
    [SerializeField] private float extraDrag = 0.5f;

    [Tooltip("휠 외의 Rigidbody도 감속함. 땅에 떨어진 화물 등.")]
    [SerializeField] private bool affectOtherBodies = false;

    private readonly HashSet<Rigidbody> bodies = new();
    private readonly List<Rigidbody> scratch = new();

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

            Slow(rb, dt);
        }
    }

    private void Slow(Rigidbody rb, float dt)
    {
        Vector3 v = rb.linearVelocity;
        float speed = new Vector2(v.x, v.z).magnitude;
        if (speed < 1e-4f) return;

        float target = speed;

        if (speed > maxSpeedInside)
            target = Mathf.Lerp(speed, maxSpeedInside, 1f - Mathf.Exp(-brakeRate * dt));

        if (extraDrag > 0f)
            target *= Mathf.Exp(-extraDrag * dt);

        if (target >= speed) return;

        float scale = target / speed;
        rb.linearVelocity = new Vector3(v.x * scale, v.y, v.z * scale);

        rb.angularVelocity *= scale;
    }
}