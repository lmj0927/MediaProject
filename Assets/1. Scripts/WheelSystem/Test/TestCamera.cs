using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 테스트용 카메라. 로컬 플레이어와 휠을 번갈아 따라감.
/// 로컬 플레이어를 찾기 전에는 휠을 따라감.
///
/// Player LateUpdate(판 로컬 스냅)보다 늦게 돌아야 Client 카메라 떨림이 줄어듦.
/// 플레이어 모드는 네트워크 보간 타겟을 SmoothDamp로 한 번 더 필터하지 않음.
///
/// 조작
///   토글 키(기본 Tab): 플레이어 / 휠 전환
///   마우스 우클릭 드래그: 궤도 회전 (휠 모드 전용)
///   마우스 휠: 거리 조절
/// </summary>
[DefaultExecutionOrder(1000)]
public class TestCamera : MonoBehaviour
{
    public enum FollowMode { LocalPlayer, Wheel }

    [Header("Targets")]
    [Tooltip("휠 모드에서 따라갈 대상. 보통 Wheel(구체).")]
    [SerializeField] private Transform wheelTarget;

    [Tooltip("시작 모드. 로컬 플레이어를 못 찾으면 휠을 따라감.")]
    [SerializeField] private FollowMode mode = FollowMode.LocalPlayer;

    [Header("Player Framing")]
    [Tooltip("플레이어 모드의 거리.")]
    [SerializeField] private float playerDistance = 9f;

    [Tooltip("플레이어 모드의 수평 방향(도). 0이면 W가 화면 위쪽.")]
    [SerializeField] private float playerYaw = 0f;

    [Tooltip("플레이어 모드의 내려다보는 각도(도).")]
    [Range(5f, 85f)][SerializeField] private float playerPitch = 35f;

    [Header("Wheel Framing")]
    [Tooltip("휠 모드의 거리. 판 전체와 주변이 보이도록 멀게.")]
    [SerializeField] private float wheelDistance = 16f;

    [Tooltip("휠 모드의 시작 내려다보는 각도(도).")]
    [Range(5f, 85f)][SerializeField] private float wheelPitch = 35f;

    [Header("Common")]
    [Tooltip("대상보다 이 높이를 바라봄.")]
    [SerializeField] private float lookHeight = 1.2f;

    [Tooltip("휠 모드에서만 쓰는 따라가기 부드러움. 0이면 즉시. 플레이어 모드는 항상 즉시 추종.")]
    [SerializeField] private float followSmoothTime = 0.12f;

    [Header("Speed Framing")]
    [Tooltip("빠르게 달리는 동안 카메라를 조금 뒤로 빼고 진행 방향 쪽을 더 비춤(플레이어 모드).\n" +
             "회전은 하지 않고, 평균 속도로 평상/고속 두 단계만 천천히 오가서 멀미를 줄임.")]
    [SerializeField] private bool speedFraming = true;

    [Tooltip("휠 속도를 평균 내는 시간(초). 길수록 잠깐의 가속·제동에는 반응하지 않음.")]
    [Min(0.1f)][SerializeField] private float speedAverageTime = 2.5f;

    [Tooltip("평균 속도(m/s)가 이 값을 넘으면 고속 화면으로 바꿈.")]
    [SerializeField] private float fastEnterSpeed = 7f;

    [Tooltip("평균 속도(m/s)가 이 값 아래로 내려가야 평상 화면으로 돌아옴. 경계에서 왔다 갔다 하지 않도록 들어갈 때보다 낮게 둠.")]
    [SerializeField] private float fastExitSpeed = 4f;

    [Tooltip("화면이 바뀌는 데 걸리는 대략적인 시간(초).")]
    [Min(0.1f)][SerializeField] private float framingBlendTime = 1.5f;

    [Tooltip("고속 화면에서 더 멀어지는 거리(m).")]
    [SerializeField] private float fastExtraDistance = 3f;

    [Tooltip("고속 화면에서 더 내려다보는 각도(도).")]
    [SerializeField] private float fastExtraPitch = 5f;

    [Tooltip("고속 화면에서 진행 방향으로 내다보는 거리(m).")]
    [SerializeField] private float fastLookAhead = 4f;

    [Header("Control")]
    [Tooltip("모드 전환 키.")]
#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key toggleKey = Key.Tab;
#else
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
#endif

    [SerializeField] private float orbitSensitivity = 0.2f;
    [SerializeField] private float zoomStep = 1.5f;
    [SerializeField] private Vector2 distanceRange = new Vector2(3f, 40f);

    private Transform localPlayer;
    private float nextSearchTime;
    private float wheelYaw;
    private float playerZoom;
    private float wheelZoom;
    private Vector3 followVelocity;

    // 속도에 따른 화면 단계
    private Vector3 lastWheelPosition;
    private bool hasLastWheelPosition;
    private Vector3 averageVelocity;
    private bool fastFraming;
    private float framingBlend;
    private float framingBlendVelocity;

    /// <summary>평균 낸 휠 수평 속도. HUD 등에서 같이 쓸 수 있음.</summary>
    public Vector3 AverageWheelVelocity => averageVelocity;

    /// <summary>현재 카메라의 수평 회전(도). 입력을 카메라 기준으로 돌릴 때 사용.</summary>
    public static float Yaw { get; private set; }

    /// <summary>지금 실제로 따라가고 있는 대상.</summary>
    public Transform CurrentTarget =>
        mode == FollowMode.LocalPlayer && localPlayer != null ? localPlayer : wheelTarget;

    private bool IsWheelView => CurrentTarget == wheelTarget;

    private void Awake()
    {
        playerZoom = playerDistance;
        wheelZoom = wheelDistance;
    }

    private void LateUpdate()
    {
        FindLocalPlayerIfNeeded();
        HandleInput();

        var target = CurrentTarget;
        if (target == null) return;

        UpdateSpeedFraming(Time.deltaTime);

        float yaw = IsWheelView ? wheelYaw : playerYaw;
        float pitch = IsWheelView ? wheelPitch : playerPitch;
        float distance = IsWheelView ? wheelZoom : playerZoom;

        Vector3 focus = target.position + Vector3.up * lookHeight;

        if (!IsWheelView && framingBlend > 0.001f)
        {
            distance += fastExtraDistance * framingBlend;
            pitch = Mathf.Clamp(pitch + fastExtraPitch * framingBlend, 5f, 85f);
            Vector3 travel = averageVelocity;
            travel.y = 0f;
            if (travel.sqrMagnitude > 0.25f) focus += travel.normalized * (fastLookAhead * framingBlend);
        }
        Vector3 desired = focus + Quaternion.Euler(pitch, yaw, 0f) * (Vector3.back * distance);

        // 플레이어: NCC/판 LateUpdate 스냅 직후 좌표를 즉시 사용 (이중 SmoothDamp 떨림 방지)
        // 휠: 기존처럼 부드럽게 따라가도 됨
        if (!IsWheelView || followSmoothTime <= 0f)
        {
            followVelocity = Vector3.zero;
            transform.position = desired;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(
                transform.position, desired, ref followVelocity, followSmoothTime);
        }

        transform.LookAt(focus);
        Yaw = yaw;
    }

    /// <summary>
    /// 휠의 평균 속도로 평상/고속 화면을 고르고, 그 사이를 천천히 섞음.
    /// 위치 변화로 속도를 재서 호스트와 클라이언트가 같게 동작함.
    /// </summary>
    private void UpdateSpeedFraming(float dt)
    {
        if (wheelTarget == null || dt <= 0f) return;

        Vector3 position = wheelTarget.position;
        if (hasLastWheelPosition)
        {
            Vector3 velocity = (position - lastWheelPosition) / dt;
            // 리셋 등으로 순간이동하면 평균을 흐트러뜨리지 않게 버림
            if (velocity.sqrMagnitude < 60f * 60f)
                averageVelocity = Vector3.Lerp(averageVelocity, velocity, 1f - Mathf.Exp(-dt / speedAverageTime));
        }
        lastWheelPosition = position;
        hasLastWheelPosition = true;

        Vector3 flat = averageVelocity;
        flat.y = 0f;
        float speed = flat.magnitude;
        if (!fastFraming && speed > fastEnterSpeed) fastFraming = true;
        else if (fastFraming && speed < fastExitSpeed) fastFraming = false;

        float target = speedFraming && fastFraming ? 1f : 0f;
        framingBlend = Mathf.SmoothDamp(framingBlend, target, ref framingBlendVelocity, framingBlendTime * 0.5f);
    }

    /// <summary>
    /// 입력 권한을 가진 플레이어를 찾음. 호스트에서는 호스트 본인의 캐릭터.
    /// </summary>
    private void FindLocalPlayerIfNeeded()
    {
        if (localPlayer != null || Time.time < nextSearchTime) return;
        nextSearchTime = Time.time + 0.5f;

        var players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        foreach (var p in players)
        {
            if (p.Object != null && p.Object.IsValid && p.Object.HasInputAuthority)
            {
                localPlayer = p.transform;
                followVelocity = Vector3.zero;
                return;
            }
        }
    }

    private void HandleInput()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
            ToggleMode();

        if (mouse == null) return;

        if (IsWheelView && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            Orbit(delta.x * orbitSensitivity, delta.y * orbitSensitivity);
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f) Zoom(-Mathf.Sign(scroll) * zoomStep);
#else
        if (Input.GetKeyDown(toggleKey)) ToggleMode();

        if (IsWheelView && Input.GetMouseButton(1))
        {
            Orbit(Input.GetAxis("Mouse X") * orbitSensitivity * 10f,
                  Input.GetAxis("Mouse Y") * orbitSensitivity * 10f);
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f) Zoom(-Mathf.Sign(scroll) * zoomStep);
#endif
    }

    private void Orbit(float yawDelta, float pitchDelta)
    {
        wheelYaw += yawDelta;
        wheelPitch = Mathf.Clamp(wheelPitch - pitchDelta, 5f, 85f);
    }

    private void ToggleMode()
    {
        mode = mode == FollowMode.LocalPlayer ? FollowMode.Wheel : FollowMode.LocalPlayer;
        followVelocity = Vector3.zero;
    }

    private void Zoom(float amount)
    {
        if (IsWheelView)
            wheelZoom = Mathf.Clamp(wheelZoom + amount, distanceRange.x, distanceRange.y);
        else
            playerZoom = Mathf.Clamp(playerZoom + amount, distanceRange.x, distanceRange.y);
    }
}
