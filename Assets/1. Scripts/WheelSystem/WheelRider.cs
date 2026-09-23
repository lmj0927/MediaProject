using UnityEngine;

/// <summary>
/// 휠을 하나의 "움직이는 기준계"로 보고, 그 기준계의 속도 변화량을 매 스텝 그대로 물체에 더함.
///
/// 바닥으로 누르는 힘을 전혀 쓰지 않으므로 수직항력이 늘지 않고, 따라서 마찰에 영향이 없음.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(WeightSource))]
public class WheelRider : MonoBehaviour
{
    [Header("References")]
    [Tooltip("따라갈 휠의 판. 비우면 트리거에 처음 닿을 때 자동으로 찾는다.")]
    [SerializeField] private PlatformTilt platform;

    [Header("Carry")]
    [Tooltip("공중에 있는 동안 휠의 속도 변화를 따라간다. 점프해도 판 밖으로 밀려나지 않는다.")]
    [SerializeField] private bool carryWhileAirborne = true;

    [Tooltip("따라가는 정도. 1이면 완전히 동기화, 0이면 비동기화.\n" +
             "0.7 정도로 낮추면 점프 시 약간 뒤로 밀리지만 회복은 가능한 절충이 된다.")]
    [Range(0f, 1f)][SerializeField] private float carryFactor = 1f;

    [Tooltip("수직 성분도 따라갈지 여부. 보통은 끄는 쪽이 자연스럽다.")]
    [SerializeField] private bool carryVertical = false;

    [Header("Drift correction")]
    [Tooltip("누적 오차를 천천히 보정하는 세기. 0이면 보정하지 않음.\n" +
             "물리 오차로 조금씩 밀려나는 것을 막지만, 크면 공중 조작이 뻣뻣해진다.")]
    [Range(0f, 6f)][SerializeField] private float driftCorrection = 1.5f;

    [Header("Detection")]
    [Tooltip("접촉면의 법선 Y가 이 값보다 크면 판을 밟고 있는 것으로 친다.")]
    [Range(0f, 1f)][SerializeField] private float groundNormalThreshold = 0.4f;

    [Tooltip("판에서 벗어난 뒤에도 이 시간 동안은 계속 실어 준다. " +
             "가장자리에서 점프할 때 뚝 끊기는 느낌을 막는다.")]
    [SerializeField] private float carryGracePeriod = 0.4f;

    private Rigidbody body;
    private bool insideVolume;
    private bool contactGrounded;
    private float lastInsideTime = -99f;

    private Vector3 lastFrameVelocity;
    private bool hasFrameSample;

    /// <summary>지금 휠에 실려 가는 중인지.</summary>
    public bool IsRiding => platform != null &&
                            (insideVolume || Time.time - lastInsideTime <= carryGracePeriod);

    /// <summary>판 표면에 직접 닿아 있는지.</summary>
    public bool IsOnPlatform { get; private set; }

    /// <summary>
    /// 지정한 판에 즉시 연결함. 판 위 공중에 새로 생성한 물체에 사용.
    /// 트리거 감지는 다음 물리 스텝에야 오므로, 그 전에 속도를 물려주려면 직접 연결해야 함.
    /// </summary>
    public void AttachTo(PlatformTilt target)
    {
        if (body == null) body = GetComponent<Rigidbody>();

        platform = target;
        lastInsideTime = Time.time;
        lastFrameVelocity = target != null ? target.FrameVelocity : Vector3.zero;
        hasFrameSample = target != null;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        IsOnPlatform = contactGrounded;
        contactGrounded = false;

        if (platform == null)
        {
            hasFrameSample = false;
            return;
        }

        // 공중에서는 판의 이동만 따라감.
        Vector3 frameVelocity = platform.FrameVelocity;

        // 첫 샘플에서는 변화량을 알 수 없으므로 건너뛴다
        if (!hasFrameSample)
        {
            lastFrameVelocity = frameVelocity;
            hasFrameSample = true;
            return;
        }

        // 들려 있는 등 Kinematic 상태면 속도를 설정할 수 없음.
        // 기준값만 갱신해 두어야 풀려난 직후 엉뚱한 변화량이 한꺼번에 들어가지 않음.
        if (body.isKinematic)
        {
            lastFrameVelocity = frameVelocity;
            return;
        }

        if (IsOnPlatform || !carryWhileAirborne || !IsRiding)
        {
            lastFrameVelocity = frameVelocity;
            return;
        }

        ApplyFrameCarry(frameVelocity);
        ApplyDriftCorrection(frameVelocity);

        lastFrameVelocity = frameVelocity;
    }

    /// <summary>
    /// 현재 위치의 휠 속도를 즉시 입히고, 그 위에 추가 속도를 더함.
    /// 들고 있던 물체를 놓거나 던진 직후 호출.
    ///
    /// Kinematic을 푼 뒤에 호출해야 함.
    /// </summary>
    /// <param name="extraVelocity">휠 속도 위에 더할 속도. 놓기는 zero, 던지기는 던지는 속도.</param>
    public void InheritFrameVelocity(Vector3 extraVelocity)
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (body == null || body.isKinematic) return;

        Vector3 frame = Vector3.zero;
        if (platform != null && IsRiding)
        {
            frame = platform.FrameVelocity;
            if (!carryVertical) frame.y = 0f;
            frame *= carryFactor;
        }

        body.linearVelocity = frame + extraVelocity;
        lastFrameVelocity = platform != null ? platform.FrameVelocity : Vector3.zero;
        hasFrameSample = platform != null;
    }

    /// <summary>기준계의 속도 변화량을 그대로 더함. 상대 속도는 보존됨.</summary>
    private void ApplyFrameCarry(Vector3 frameVelocity)
    {
        Vector3 delta = (frameVelocity - lastFrameVelocity) * carryFactor;
        if (!carryVertical) delta.y = 0f;

        if (delta.sqrMagnitude > 1e-8f)
            body.linearVelocity += delta;
    }

    /// <summary>
    /// 충돌이나 공기 저항으로 인한 오차 보정
    /// </summary>
    private void ApplyDriftCorrection(Vector3 frameVelocity)
    {
        if (driftCorrection <= 0f) return;

        Vector3 diff = frameVelocity - body.linearVelocity;
        diff.y = 0f;

        body.AddForce(diff * (driftCorrection * carryFactor), ForceMode.Acceleration);
    }

    // ---- 감지 ----

    private void OnTriggerEnter(Collider other)
    {
        var found = other.GetComponentInParent<PlatformTilt>();
        if (found == null) return;

        if (platform == null) platform = found;
        if (found != platform) return;

        insideVolume = true;
        lastInsideTime = Time.time;
    }

    private void OnTriggerStay(Collider other)
    {
        if (platform == null) return;
        if (other.GetComponentInParent<PlatformTilt>() != platform) return;

        insideVolume = true;
        lastInsideTime = Time.time;
    }

    private void OnTriggerExit(Collider other)
    {
        if (platform == null) return;
        if (other.GetComponentInParent<PlatformTilt>() != platform) return;

        insideVolume = false;
    }

    private void OnCollisionStay(Collision collision) => EvaluateContacts(collision);
    private void OnCollisionEnter(Collision collision) => EvaluateContacts(collision);

    private void EvaluateContacts(Collision collision)
    {
        if (platform == null) return;
        if (collision.transform.GetComponentInParent<PlatformTilt>() != platform) return;

        int count = collision.contactCount;
        for (int i = 0; i < count; i++)
        {
            if (collision.GetContact(i).normal.y > groundNormalThreshold)
            {
                contactGrounded = true;
                lastInsideTime = Time.time;
                return;
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || platform == null) return;

        Gizmos.color = IsOnPlatform ? Color.green : (IsRiding ? Color.cyan : Color.gray);
        Gizmos.DrawRay(transform.position, platform.PointVelocity(transform.position) * 0.3f);
    }
#endif
}