using Fusion;
using UnityEngine;

/// <summary>
/// 판의 기울기를 읽어 구체에 보정 토크를 넣음.
/// Host State Authority의 FixedUpdateNetwork에서만 시뮬한다.
/// 구체는 non-kinematic Rigidbody여야 충돌, 경사, 넉백이 물리로 처리됨.
/// Host 시각: visualRoot(메시 자식)만 틱 사이 보간. Client는 NetworkTransform이 루트를 보간.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class SphereDriver : NetworkBehaviour, IAfterTick
{
    [Header("References")]
    [SerializeField] private PlatformTilt platform;
    [SerializeField] private WheelConfig config;

    [Tooltip("메시만 담은 자식(콜라이더 없음). Host에서 틱 사이 보간.")]
    [SerializeField] private Transform visualRoot;

    [SerializeField] private float visualSnapDistance = 3f;

    [Header("Ground check")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckPadding = 0.15f;

    private Rigidbody body;
    private SphereCollider sphereCollider;
    private TickVisualInterpolator visual;

    public bool IsGrounded { get; private set; }

    /// <summary>현재 수평 속도 크기. UI나 카메라 연출에 사용함.</summary>
    public float HorizontalSpeed
    {
        get
        {
            Vector3 v = body.linearVelocity;
            v.y = 0f;
            return v.magnitude;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        sphereCollider = GetComponent<SphereCollider>();

        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (config == null)
        {
            config = ScriptableObject.CreateInstance<WheelConfig>();
            Debug.LogWarning($"{name}: WheelConfig가 없어 기본값으로 실행합니다.", this);
        }
        body.maxAngularVelocity = config.maxAngularSpeed * 1.5f;
        visual = new TickVisualInterpolator(transform, visualRoot);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        Simulate();
    }

    void IAfterTick.AfterTick()
    {
        if (Object.HasStateAuthority)
            visual.Capture(body.position, body.rotation, visualSnapDistance);
    }

    private void LateUpdate()
    {
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            visual.Apply(Runner.LocalAlpha);
        else
            visual.RestoreLocal();
    }

    /// <summary>한 틱분 토크. Host SA에서만 호출.</summary>
    public void Simulate()
    {
        IsGrounded = CheckGrounded();

        if (platform == null) return;

        Vector3 downhill = platform.DownhillDirection;
        float tilt = platform.NormalizedTilt;

        if (downhill == Vector3.zero || tilt <= 0.001f)
        {
            ApplyIdleDamping();
            return;
        }

        Vector3 rollAxis = Vector3.Cross(downhill, Vector3.up).normalized;
        Vector3 targetAngular = rollAxis * (tilt * config.maxAngularSpeed);

        Vector3 error = targetAngular - body.angularVelocity;
        float gain = IsGrounded ? config.torqueGain : config.torqueGain * config.airControlFactor;

        body.AddTorque(error * gain, ForceMode.Acceleration);
    }

    private void ApplyIdleDamping()
    {
        if (!IsGrounded || config.idleDamping <= 0f) return;
        body.AddTorque(-body.angularVelocity * config.idleDamping, ForceMode.Acceleration);
    }

    private bool CheckGrounded()
    {
        float radius = sphereCollider != null
            ? sphereCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y)
            : 0.5f;

        return Physics.CheckSphere(
            transform.position + Vector3.down * groundCheckPadding,
            radius * 0.95f,
            groundMask,
            QueryTriggerInteraction.Ignore);
    }

    /// <summary>폭발, 범프, 부스터 같은 외부 충격용. Host SA에서만.</summary>
    public void AddImpulse(Vector3 force)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            return;

        body.AddForce(force, ForceMode.Impulse);
    }
}
