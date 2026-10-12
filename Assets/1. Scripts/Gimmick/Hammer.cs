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
    [SerializeField] private float _hitRadius = 1f;
    [SerializeField] private LayerMask _hitMask = ~0;
    [Tooltip("헤드 진행 방향 수평 속도 변화량 (m/s).")]
    [SerializeField] private float _launchSpeed = 12f;
    [Tooltip("위쪽 속도 변화량 (m/s).")]
    [SerializeField] private float _launchUpSpeed = 4f;
    [SerializeField] private float _hitCooldown = 0.5f;

    private readonly Collider[] _overlapBuffer = new Collider[16];
    private Quaternion _armRestLocalRotation;
    private Vector3 _headOffset;
    private TickVisualInterpolator _visual;
    private TickTimer _hitCooldownTimer;

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
        if (!_hitCooldownTimer.ExpiredOrNotRunning(Runner))
            return;

        Vector3 pivot = _arm.position;
        Vector3 headPosition = pivot + armRotation * _headOffset;

        int count = Physics.OverlapSphereNonAlloc(
            headPosition, _hitRadius, _overlapBuffer, _hitMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = _overlapBuffer[i];
            SphereDriver sphere = hit.GetComponentInParent<SphereDriver>();
            if (sphere == null)
                continue;

            Rigidbody body = hit.attachedRigidbody;
            if (body == null)
                continue;

            Vector3 velocityChange = GetSwingDirection(time, headPosition - pivot) * _launchSpeed
                                     + Vector3.up * _launchUpSpeed;
            sphere.AddImpulse(velocityChange * body.mass);
            _hitCooldownTimer = TickTimer.CreateFromSeconds(Runner, _hitCooldown);
            return;
        }
    }

    /// <summary>헤드의 현재 진행 방향(수평). 진자 끝이라 정확히 0이 될 일은 드묾.</summary>
    private Vector3 GetSwingDirection(float time, Vector3 headFromPivot)
    {
        Vector3 axisWorld = transform.rotation * _swingAxis.normalized;
        float angularSign = Mathf.Sign(Mathf.Cos(GetPhaseRadians(time)));
        Vector3 tangent = Vector3.Cross(axisWorld, headFromPivot) * angularSign;
        tangent.y = 0f;
        return tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.zero;
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
        Gizmos.DrawWireSphere(_head.position, _hitRadius);
    }
}
