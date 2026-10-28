using Fusion;
using UnityEngine;

/// <summary>
/// 천장/기둥에 매달린 진자 망치. 루트(이 오브젝트) 위치가 손잡이 끝 회전축이다.
/// 각도는 시간의 사인 함수라 모든 피어가 같은 식으로 재구성한다 (복제 필드 없음).
/// Host SA: FUN에서 kinematic 팔을 MoveRotation, 헤드 주변을 매 틱 쿼리해 휠 구에 충격.
/// Client: Render에서 RemoteRenderTime 기준으로 팔 루트를 배치 (NT로 보간되는 구와 시점 일치).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Hammer : NetworkBehaviour, IAfterTick
{
    [Header("References")]
    [Tooltip("루트 직속 자식, 루트와 같은 위치(손잡이 끝). kinematic Rigidbody + 손잡이/헤드 콜라이더.")]
    [SerializeField] private Rigidbody _arm;

    [Tooltip("헤드 중심 위치 표시용 Transform (_arm 하위).")]
    [SerializeField] private Transform _head;

    [Tooltip("메시만 담은 _arm 하위 자식(콜라이더 없음). Host에서 틱 사이 보간.")]
    [SerializeField] private Transform _visualRoot;

    [Header("Swing")]
    [Tooltip("루트 로컬 기준 회전축. 기본 forward = 로컬 X 방향 좌우로 흔들림.")]
    [SerializeField] private Vector3 _swingAxis = Vector3.forward;
    [SerializeField] private float _amplitude = 60f;
    [SerializeField] private float _period = 3f;
    [Tooltip("0~1. 여러 망치의 박자를 엇갈리게 할 때.")]
    [Range(0f, 1f)]
    [SerializeField] private float _phaseOffset;

    [Header("Hit")]
    [Tooltip("머리 판정 구의 반지름. _head에 캡슐 콜라이더가 있으면 대신 그 모양(꺼져 있어도)으로 판정함.")]
    [SerializeField] private float _hitRadius = 1f;

    [Tooltip("손잡이 굵기(반지름). 머리뿐 아니라 손잡이가 판이나 공에 닿아도 맞은 것으로 봄.")]
    [SerializeField] private float _handleRadius = 0.4f;
    [SerializeField] private LayerMask _hitMask = ~0;

    [Tooltip("튕겨 나가는 정도. 0이면 닿은 지점의 해머 속도만큼만 밀려 해머에 붙어 가고, 1이면 그 두 배로 튕김.\n" +
             "해머는 휠보다 훨씬 무거운 것으로 봄. 휠은 해머가 그 지점에서 움직이는 속도 이상으로 밀려나 해머가 휠을 뚫고 지나가지 않음.")]
    [Range(0f, 1f)][SerializeField] private float _restitution = 0.3f;

    private readonly Collider[] _overlapBuffer = new Collider[32];
    private Quaternion _armRestLocalRotation;
    private Vector3 _headOffset;

    // 머리 캡슐의 양 끝(팔 기준)과 반지름. 보이는 머리 크기로 판정하기 위함.
    private bool _hasHeadCapsule;
    private Vector3 _headEndA;
    private Vector3 _headEndB;
    private float _headCapsuleRadius;
    private TickVisualInterpolator _visual;

    private void Awake()
    {
        if (_arm == null)
        {
            Debug.LogWarning($"{name}: Hammer._arm이 비어 있어 동작하지 않습니다.", this);
            enabled = false;
            return;
        }

        _arm.isKinematic = true;
        _arm.interpolation = RigidbodyInterpolation.None;
        _armRestLocalRotation = _arm.transform.localRotation;

        Transform head = _head != null ? _head : _arm.transform;
        _headOffset = Quaternion.Inverse(_arm.rotation) * (head.position - _arm.position);

        // 머리 중심 구만으로 판정하면 머리 면이 휠에 박힌 뒤 중심이 닿아서야 날아감
        if (head.TryGetComponent<CapsuleCollider>(out var capsule))
        {
            GetCapsuleWorld(capsule, out Vector3 a, out Vector3 b, out _headCapsuleRadius);
            Quaternion toArm = Quaternion.Inverse(_arm.rotation);
            _headEndA = toArm * (a - _arm.position);
            _headEndB = toArm * (b - _arm.position);
            _hasHeadCapsule = true;
        }

        _visual = new TickVisualInterpolator(_arm.transform, _visualRoot);
    }

    public override void Spawned()
    {
        if (!enabled)
            return;

        Quaternion rotation = GetArmRotation(GetAngle((float)Runner.SimulationTime));
        _arm.transform.rotation = rotation;
        _arm.rotation = rotation;
        _visual.ResetTo(_arm.position, rotation);
    }

    public override void FixedUpdateNetwork()
    {
        if (!enabled || !Object.HasStateAuthority)
            return;

        float time = (float)Runner.SimulationTime;
        float angle = GetAngle(time);
        Quaternion rotation = GetArmRotation(angle);
        _arm.MoveRotation(rotation);

        TryHit(time, rotation);
    }

    void IAfterTick.AfterTick()
    {
        if (enabled && Object.HasStateAuthority)
            _visual.Capture(_arm.position, _arm.rotation, float.MaxValue);
    }

    public override void Render()
    {
        if (!enabled || Object.HasStateAuthority)
            return;

        _arm.transform.rotation = GetArmRotation(GetAngle((float)Runner.RemoteRenderTime));
    }

    private void LateUpdate()
    {
        if (_visual == null)
            return;

        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            _visual.Apply(Runner.LocalAlpha);
        else
            _visual.RestoreLocal();
    }

    private void TryHit(float time, Quaternion armRotation)
    {
        Vector3 pivot = _arm.position;
        Vector3 headPosition = pivot + armRotation * _headOffset;

        // 보이는 모양 그대로 판정함: 손잡이(축→머리)와 머리. 판(데크)이든 공이든 닿으면 휠을 밈.
        // 팔 콜라이더는 꺼 둠. 켜면 키네마틱 팔이 머리 속도 그대로 휠을 쳐서 수십 m/s로 날려 버림.
        int count = Physics.OverlapCapsuleNonAlloc(
            pivot, headPosition, _handleRadius, _overlapBuffer, _hitMask, QueryTriggerInteraction.Ignore);
        if (TryHitWheel(count, time, pivot, pivot, headPosition))
            return;

        if (_hasHeadCapsule)
        {
            Vector3 a = pivot + armRotation * _headEndA;
            Vector3 b = pivot + armRotation * _headEndB;
            count = Physics.OverlapCapsuleNonAlloc(a, b, _headCapsuleRadius, _overlapBuffer, _hitMask, QueryTriggerInteraction.Ignore);
            TryHitWheel(count, time, pivot, a, b);
        }
        else
        {
            count = Physics.OverlapSphereNonAlloc(
                headPosition, _hitRadius, _overlapBuffer, _hitMask, QueryTriggerInteraction.Ignore);
            TryHitWheel(count, time, pivot, headPosition, headPosition);
        }
    }

    /// <summary>캡슐 콜라이더의 월드 기준 양 끝 중심과 반지름. 스케일 반영.</summary>
    private static void GetCapsuleWorld(CapsuleCollider capsule, out Vector3 a, out Vector3 b, out float radius)
    {
        Transform t = capsule.transform;
        Vector3 s = t.lossyScale;
        Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;

        float axisScale = Mathf.Abs(capsule.direction == 0 ? s.x : capsule.direction == 1 ? s.y : s.z);
        float sideScale = capsule.direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))
                        : capsule.direction == 1 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z))
                        : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));

        radius = capsule.radius * sideScale;
        float half = Mathf.Max(0f, capsule.height * 0.5f * axisScale - radius);

        Vector3 center = t.TransformPoint(capsule.center);
        Vector3 dir = t.TransformDirection(axis).normalized;
        a = center + dir * half;
        b = center - dir * half;
    }

    /// <summary>
    /// 겹친 콜라이더 중 휠(공 또는 판)이 있으면 해머의 그 지점 속도에 맞춰 밂. 밀었으면 true.
    /// segA→segB는 겹침 검사에 쓴 손잡이 또는 머리의 중심선.
    /// </summary>
    private bool TryHitWheel(int count, float time, Vector3 pivot, Vector3 segA, Vector3 segB)
    {
        for (int i = 0; i < count; i++)
        {
            SphereDriver sphere = FindWheel(_overlapBuffer[i], out bool hitBall);
            if (sphere == null)
                continue;

            Rigidbody body = sphere.GetComponent<Rigidbody>();
            if (body == null)
                continue;

            // 해머 중심선에서 휠 쪽으로 향하는 방향을 밀어내는 방향으로 봄
            Vector3 center = body.worldCenterOfMass;
            Vector3 onSegment = ClosestOnSegment(segA, segB, center);
            Vector3 normal = center - onSegment;

            // 판만 닿았으면(손잡이가 데크를 침) 옆으로만 밂. 공 중심은 데크 아래라 위아래 성분은 의미가 없음.
            if (!hitBall)
                normal.y = 0f;

            if (normal.sqrMagnitude < 1e-6f)
                continue;
            normal.Normalize();

            // 해머가 그 지점에서 움직이는 속도. 진자이므로 축 기준 회전 속도 × 거리.
            Vector3 hammerVelocity = Vector3.Cross(GetAngularVelocity(time), onSegment - pivot);

            // 해머는 휠보다 훨씬 무거움: 다가오는 성분만큼 되튕김. 이미 해머보다 빨리 멀어지는 중이면 그대로 둠.
            float approach = Vector3.Dot(body.linearVelocity - hammerVelocity, normal);
            if (approach >= 0f)
                continue;

            sphere.ApplyKnockback(-(1f + _restitution) * approach * normal);
            return true;
        }
        return false;
    }

    private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        if (lengthSq < 1e-8f)
            return a;
        return a + ab * Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq);
    }

    /// <summary>팔의 현재 회전 속도(rad/s, 월드 축). 각도가 사인 함수이므로 그 미분.</summary>
    private Vector3 GetAngularVelocity(float time)
    {
        float period = Mathf.Max(0.01f, _period);
        float degreesPerSecond = _amplitude * Mathf.Cos(GetPhaseRadians(time)) * 2f * Mathf.PI / period;
        Vector3 axisWorld = transform.rotation * _swingAxis.normalized;
        return axisWorld * (degreesPerSecond * Mathf.Deg2Rad);
    }

    /// <summary>공 콜라이더면 그 휠, 판(데크·울타리) 콜라이더면 판이 따라가는 휠을 찾음.</summary>
    private static SphereDriver FindWheel(Collider hit, out bool hitBall)
    {
        SphereDriver sphere = hit.GetComponentInParent<SphereDriver>();
        hitBall = sphere != null;
        if (sphere != null)
            return sphere;

        PlatformTilt platform = hit.GetComponentInParent<PlatformTilt>();
        return platform != null && platform.Sphere != null
            ? platform.Sphere.GetComponent<SphereDriver>()
            : null;
    }

    private float GetPhaseRadians(float time)
    {
        float period = Mathf.Max(0.01f, _period);
        return 2f * Mathf.PI * (time / period + _phaseOffset);
    }

    private float GetAngle(float time) => _amplitude * Mathf.Sin(GetPhaseRadians(time));

    private Quaternion GetArmRotation(float angle)
    {
        return transform.rotation * Quaternion.AngleAxis(angle, _swingAxis.normalized) * _armRestLocalRotation;
    }

    private void OnDrawGizmosSelected()
    {
        if (_head == null)
            return;

        Gizmos.color = Color.red;
        if (_head.TryGetComponent<CapsuleCollider>(out var capsule))
        {
            GetCapsuleWorld(capsule, out Vector3 a, out Vector3 b, out float r);
            Gizmos.DrawWireSphere(a, r);
            Gizmos.DrawWireSphere(b, r);
            Gizmos.DrawLine(a, b);
        }
        else
        {
            Gizmos.DrawWireSphere(_head.position, _hitRadius);
        }
    }
}
