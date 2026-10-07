using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 휠에 작용하는 장판의 부모.
/// Host State Authority의 FixedUpdateNetwork에서 매 틱 겹침 검사로 대상을 찾아 Affect를 호출함/
///
/// 물리 본체(이 오브젝트): BoxCollider + 장판 스크립트. 시각(자식): Quad + PadSurface.
/// 크기는 이 오브젝트의 X/Z 스케일로 조절하고, 콜라이더는 가로세로 1을 유지할 것.
/// </summary>
public abstract class WheelPadBase : NetworkBehaviour
{
    [Header("Detection")]
    [Tooltip("휠 외의 Rigidbody에도 작용함. 땅에 떨어진 화물 등.")]
    [SerializeField] private bool affectOtherBodies = false;

    [Tooltip("겹침 검사에 쓸 레이어.")]
    [SerializeField] private LayerMask detectionMask = ~0;

    private BoxCollider area;
    private readonly Collider[] hits = new Collider[16];
    private readonly HashSet<Rigidbody> affected = new();

    /// <summary>장판의 forward를 수평으로 눕힌 방향.</summary>
    public Vector3 PadForward
    {
        get
        {
            Vector3 d = transform.forward;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.zero;
        }
    }

    protected virtual void Reset()
    {
        var box = GetComponent<BoxCollider>();
        box.isTrigger = true;

        box.size = new Vector3(1f, 0.1f, 1f);
        box.center = new Vector3(0f, 0f, 0f);
    }

    protected virtual void Awake()
    {
        area = GetComponent<BoxCollider>();
        area.isTrigger = true;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        GetWorldBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);

        int count = Physics.OverlapBoxNonAlloc(
            center, halfExtents, hits, rotation, detectionMask, QueryTriggerInteraction.Ignore);

        affected.Clear();

        for (int i = 0; i < count; i++)
        {
            var rb = hits[i].attachedRigidbody;
            if (rb == null || rb.isKinematic || !affected.Add(rb)) continue;
            if (!affectOtherBodies && !rb.TryGetComponent<SphereDriver>(out _)) continue;

            Affect(rb, Runner.DeltaTime);
        }
    }

    /// <summary>장판 위에 있는 대상에 이번 틱의 효과를 적용함. Host SA에서만 호출됨.</summary>
    protected abstract void Affect(Rigidbody rb, float dt);

    /// <summary>BoxCollider의 월드 기준 중심, 반크기, 회전을 구함.</summary>
    private void GetWorldBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        Vector3 s = transform.lossyScale;
        center = transform.TransformPoint(area.center);
        halfExtents = Vector3.Scale(area.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z))) * 0.5f;
        rotation = transform.rotation;
    }

    /// <summary>대상의 SphereCollider 반지름. 구가 아니면 0.</summary>
    protected static float SphereRadiusOf(Rigidbody rb)
    {
        if (!rb.TryGetComponent<SphereCollider>(out var sphere)) return 0f;

        Vector3 s = rb.transform.lossyScale;
        return sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

#if UNITY_EDITOR
    /// <summary>장판 방향 화살표를 그림.</summary>
    protected void DrawDirectionGizmo(Color color)
    {
        Vector3 dir = PadForward;
        if (dir == Vector3.zero) return;

        Vector3 from = transform.position + Vector3.up * 0.1f;
        Vector3 to = from + dir * 2f;

        Gizmos.color = color;
        Gizmos.DrawLine(from, to);

        Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.4f;
        Gizmos.DrawLine(to, to - dir * 0.6f + side);
        Gizmos.DrawLine(to, to - dir * 0.6f - side);
    }
#endif
}
