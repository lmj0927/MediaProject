using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 테스트용 카메라. 로컬 플레이어와 휠을 번갈아 따라감.
/// 로컬 플레이어를 찾기 전에는 휠을 따라감.
///
/// 조작
///   토글 키(기본 Tab): 플레이어 / 휠 전환
///   마우스 우클릭 드래그: 궤도 회전 (휠 모드 전용)
///   마우스 휠: 거리 조절
///
/// 플레이어 모드는 방향이 고정됨. 이동 입력이 월드 기준이라 카메라가 돌면 조작 방향이 어긋남.
/// </summary>
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

    [Tooltip("따라가는 부드러움. 0이면 즉시 따라감.")]
    [SerializeField] private float followSmoothTime = 0.12f;

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

    /// <summary>현재 카메라의 수평 회전(도). 입력을 카메라 기준으로 돌릴 때 사용.</summary>
    public static float Yaw { get; private set; }

    /// <summary>지금 실제로 따라가고 있는 대상.</summary>
    public Transform CurrentTarget =>
        mode == FollowMode.LocalPlayer && localPlayer != null ? localPlayer : wheelTarget;

    // 로컬 플레이어를 못 찾아 휠을 따라가는 중에도 휠 모드로 취급함
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

        float yaw = IsWheelView ? wheelYaw : playerYaw;
        float pitch = IsWheelView ? wheelPitch : playerPitch;
        float distance = IsWheelView ? wheelZoom : playerZoom;

        Vector3 focus = target.position + Vector3.up * lookHeight;
        Vector3 desired = focus + Quaternion.Euler(pitch, yaw, 0f) * (Vector3.back * distance);

        transform.position = followSmoothTime > 0f
            ? Vector3.SmoothDamp(transform.position, desired, ref followVelocity, followSmoothTime)
            : desired;

        transform.LookAt(focus);
        Yaw = yaw;
    }

    /// <summary>
    /// 입력 권한을 가진 플레이어를 찾음. 호스트에서는 호스트 본인의 캐릭터.
    /// 세션 시작 전이나 재접속 중에는 없으므로 주기적으로 다시 찾음.
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

        // 궤도 회전은 휠 모드에서만 허용함
        if (IsWheelView && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            Orbit(delta.x * orbitSensitivity, delta.y * orbitSensitivity);
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f) Zoom(-Mathf.Sign(scroll) * zoomStep);
#else
        if (Input.GetKeyDown(toggleKey)) ToggleMode();

        // 궤도 회전은 휠 모드에서만 허용함
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