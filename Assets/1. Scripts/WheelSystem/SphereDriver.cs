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
    private float knockbackTimer;

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
        // 조작 속도 제한은 토크 목표치가 맡음. 여기서 막으면 빠르게 밀릴 때 회전이 못 따라가 미끄러짐.
        body.maxAngularVelocity = Mathf.Max(config.maxSpinSpeed, config.maxAngularSpeed * 1.5f);
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

        float dt = Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;
        if (knockbackTimer > 0f)
        {
            knockbackTimer -= dt;
            DampKnockback(dt);
        }

        if (platform == null) return;

        Vector3 downhill = platform.DownhillDirection;
        float tilt = platform.NormalizedTilt;

        if (downhill == Vector3.zero || tilt <= 0.001f)
        {
            ApplyIdleDamping();
            return;
        }

        // 진행 방향 d로 미끄러짐 없이 구르는 회전축은 up × d
        Vector3 rollAxis = Vector3.Cross(Vector3.up, downhill).normalized;
        Vector3 targetAngular = rollAxis * (tilt * config.maxAngularSpeed);

        Vector3 error = targetAngular - body.angularVelocity;
        float gain = IsGrounded ? config.torqueGain : config.torqueGain * config.airControlFactor;
        gain *= ControlFactor;

        body.AddTorque(error * gain, ForceMode.Acceleration);
    }

    /// <summary>밀려나는 수평 속도를 줄임. 구름도 같은 비율로 줄여 미끄러지지 않게 함.</summary>
    private void DampKnockback(float dt)
    {
        if (config.knockbackDrag <= 0f) return;

        float keep = Mathf.Exp(-config.knockbackDrag * dt);
        Vector3 v = body.linearVelocity;
        body.linearVelocity = new Vector3(v.x * keep, v.y, v.z * keep);

        Vector3 w = body.angularVelocity;
        Vector3 yaw = Vector3.Project(w, Vector3.up);
        body.angularVelocity = yaw + (w - yaw) * keep;
    }

    private void ApplyIdleDamping()
    {
        if (!IsGrounded || config.idleDamping <= 0f) return;
        body.AddTorque(-body.angularVelocity * (config.idleDamping * ControlFactor), ForceMode.Acceleration);
    }

    private readonly RaycastHit[] groundHits = new RaycastHit[32];

    /// <summary>
    /// 구체 바로 아래가 땅에 닿았는지. 중심에서 아래로 작은 구를 쏴서 바닥 아래 groundCheckPadding 안에 무언가 있으면 닿은 것으로 봄.
    /// 자기 자신, 판(구체를 감싸고 있어 항상 겹침), 판 위의 캐릭터와 물건은 땅으로 치지 않음.
    /// 겹침 검사는 아주 큰 평면 메시에서 가끔 놓쳐서 쓰지 않음.
    /// </summary>
    private bool CheckGrounded()
    {
        float radius = Radius;
        float castRadius = radius * 0.5f;
        float distance = radius - castRadius + groundCheckPadding;

        int count = Physics.SphereCastNonAlloc(
            transform.position, castRadius, Vector3.down, groundHits, distance, groundMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (IsGround(groundHits[i].collider)) return true;
        }
        return false;
    }

    private bool IsGround(Collider col)
    {
        if (col == null) return false;
        Transform t = col.transform;
        if (t.IsChildOf(transform)) return false;
        if (platform != null && t.IsChildOf(platform.transform)) return false;
        if (col.GetComponentInParent<WeightSource>() != null) return false;
        return true;
    }

    /// <summary>폭발, 범프, 부스터 같은 외부 충격용. Host SA에서만.</summary>
    public void AddImpulse(Vector3 force)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            return;

        body.AddForce(force, ForceMode.Impulse);
    }

    /// <summary>강한 충격 직후면 조작이 약해짐. 1이면 평소.</summary>
    private float ControlFactor => knockbackTimer > 0f ? config.knockbackControl : 1f;

    /// <summary>넉백(해머 등)으로 조작이 약해진 상태인지.</summary>
    public bool IsKnockedBack => knockbackTimer > 0f;

    /// <summary>
    /// 해머 같은 강한 충격. Host SA에서만.
    /// 속도를 바로 바꾸고 회전을 새 진행 방향에 맞춘 뒤(미끄러지지 않고 굴러감), 잠시 조작을 약하게 함.
    /// 조작이 그대로면 땅을 잡는 힘 때문에 옆으로 밀린 속도가 바로 사라짐.
    /// </summary>
    public void ApplyKnockback(Vector3 velocityChange)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            return;

        body.linearVelocity += velocityChange;

        Vector3 horizontal = body.linearVelocity;
        horizontal.y = 0f;
        Vector3 yaw = Vector3.Project(body.angularVelocity, Vector3.up);
        body.angularVelocity = Vector3.Cross(Vector3.up, horizontal) / Radius + yaw;

        knockbackTimer = config.knockbackRecoverTime;
    }

    /// <summary>
    /// 내려찍기 반동. 찍은 쪽으로 굴러가는 속도를 바로 더함. Host SA에서만.
    /// 선속도와 각속도를 같이 바꿔 미끄러지지 않고 구르는 상태로 들어감.
    /// strength는 판 중심에서의 거리 비율(0~1). sinceLast는 같은 사람이 직전에 찍은 뒤 지난 시간(초).
    /// </summary>
    public void ApplyStampKick(Vector3 direction, float strength, float sinceLast = float.PositiveInfinity)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            return;

        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-6f || config.stampKickSpeed <= 0f)
            return;

        float speed = config.stampKickSpeed * Mathf.Clamp01(strength);
        if (config.stampRecoverTime > 0f) speed *= Mathf.Clamp01(sinceLast / config.stampRecoverTime);
        if (!IsGrounded) speed *= config.stampAirFactor;
        if (speed <= 0f) return;

        direction.Normalize();
        body.linearVelocity += direction * speed;
        body.angularVelocity += Vector3.Cross(Vector3.up, direction) * (speed / Radius);
    }

    /// <summary>스케일이 반영된 구체 반지름.</summary>
    private float Radius => sphereCollider != null
        ? sphereCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y)
        : 0.5f;
}
