using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 게임 카메라. 판(휠) 전체를 화면 아래쪽에 두고, 그 위로 진행 방향(앞쪽)이 넓게 보이게 비춤.
/// 판 반지름과 화면 비율에 맞춰 판 전체가 화면에 들어오는 거리를 매 프레임 계산함.
/// 수평 방향(yaw)은 고정. 빠르게 달릴 때는 평균 속도로 평상/고속 두 단계만 천천히 오가며 조금 멀어짐.
///
/// 조작
///   토글 키(기본 Tab): 판 / 내 캐릭터 중심 전환. 각도와 화면 배치는 같음.
/// </summary>
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(Camera))]
public class GameCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("따라갈 판. 비우면 씬에서 찾음.")]
    [SerializeField] private PlatformTilt platform;

    [Tooltip("판 중심 높이(휠 중심 기준, m). 0 이하면 판 위치로 자동 계산.")]
    [SerializeField] private float deckHeight = 0f;

    [Tooltip("판 반지름에 더하는 여유(m).")]
    [SerializeField] private float deckMargin = 1f;

    [Header("Framing")]
    [Tooltip("화면 위쪽이 가리키는 수평 방향(도). 0이면 +Z가 화면 위쪽.")]
    [SerializeField] private float yaw = 0f;

    [Tooltip("판을 내려다보는 각도(도). 낮을수록 앞이 멀리 보이고 판은 납작하게 보임.")]
    [Range(15f, 80f)][SerializeField] private float pitch = 41.5f;

    [Tooltip("판 중심이 놓일 화면 높이(0 = 아래, 1 = 위).")]
    [Range(0.1f, 0.5f)][SerializeField] private float deckScreenY = 0.31f;

    [Tooltip("휠(구체)도 화면 안에 들어오게 함.")]
    [SerializeField] private bool keepWheelOnScreen = true;

    [Tooltip("판이 화면 가장자리에서 떨어져 있어야 하는 여백(화면 비율).")]
    [Range(0f, 0.2f)][SerializeField] private float screenMargin = 0.03f;

    [Tooltip("판 전체가 딱 들어오는 거리에 곱하는 배율. 1보다 크면 더 멀어짐.")]
    [Min(1f)][SerializeField] private float distanceScale = 1.2f;

    [Tooltip("거리 범위(m). 판 전체가 들어오는 거리를 이 안에서 고름.")]
    [SerializeField] private Vector2 distanceRange = new Vector2(10f, 60f);

    [Header("Smoothing")]
    [Tooltip("수평으로 따라가는 부드러움(초). 0이면 즉시.")]
    [Min(0f)][SerializeField] private float horizontalSmoothTime = 0.05f;

    [Tooltip("위아래로 따라가는 부드러움(초).")]
    [Min(0f)][SerializeField] private float verticalSmoothTime = 0.25f;

    [Header("Speed Framing")]
    [Tooltip("빠르게 달리는 동안 조금 멀어지고 판을 더 아래로 내림. 평균 속도로 두 단계만 오감.")]
    [SerializeField] private bool speedFraming = true;

    [Tooltip("휠 속도를 평균 내는 시간(초).")]
    [Min(0.1f)][SerializeField] private float speedAverageTime = 2.5f;

    [Tooltip("평균 속도(m/s)가 이 값을 넘으면 고속 화면으로 바꿈.")]
    [SerializeField] private float fastEnterSpeed = 7f;

    [Tooltip("평균 속도(m/s)가 이 값 아래로 내려가야 평상 화면으로 돌아옴.")]
    [SerializeField] private float fastExitSpeed = 4f;

    [Tooltip("화면이 바뀌는 데 걸리는 대략적인 시간(초).")]
    [Min(0.1f)][SerializeField] private float framingBlendTime = 1.5f;

    [Tooltip("고속 화면에서 더 멀어지는 거리(m).")]
    [SerializeField] private float fastExtraDistance = 3f;

    [Tooltip("고속 화면에서 판 중심을 더 내리는 화면 높이.")]
    [Range(0f, 0.2f)][SerializeField] private float fastLowerScreenY = 0.04f;

    [Header("Control")]
#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key toggleKey = Key.Tab;
#else
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
#endif

    private Camera cam;
    private TransportSpeedFeel speedFeel;
    private Transform wheelVisual;
    private Transform localPlayer;
    private bool followPlayer;
    private float nextSearch;

    private Vector3 focus;
    private float focusHeight;
    private bool hasFocus;
    private Vector3 focusVelocity;

    private Vector3 lastWheelPosition;
    private bool hasLastWheel;
    private Vector3 averageVelocity;
    private bool fastFraming;
    private float framingBlend;
    private float framingBlendVelocity;

    /// <summary>평균 낸 휠 수평 속도.</summary>
    public Vector3 AverageWheelVelocity => averageVelocity;

    /// <summary>지금 계산된 카메라 거리(m).</summary>
    public float CurrentDistance { get; private set; }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        speedFeel = GetComponent<TransportSpeedFeel>();
    }

    // 속도 FOV를 거리 자동 맞춤이 상쇄하지 않게 평상시 시야각으로 배치함.
    private float FramingFieldOfView => cam.fieldOfView - (speedFeel != null ? speedFeel.FieldOfViewOffset : 0f);

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextSearch) Search();
        HandleInput();
        if (platform == null || wheelVisual == null) return;

        float dt = Time.deltaTime;
        UpdateSpeedFraming(dt);

        Vector3 target = TargetPoint();
        if (!hasFocus) { focus = target; hasFocus = true; }
        focus = new Vector3(
            Smooth(focus.x, target.x, ref focusVelocity.x, horizontalSmoothTime, dt),
            Smooth(focus.y, target.y, ref focusVelocity.y, verticalSmoothTime, dt),
            Smooth(focus.z, target.z, ref focusVelocity.z, horizontalSmoothTime, dt));

        float screenY = deckScreenY - fastLowerScreenY * framingBlend;
        float radius = platform.PlatformRadius + deckMargin;
        float distance = Mathf.Min(FitDistance(focus, radius, screenY) * distanceScale, distanceRange.y) + fastExtraDistance * framingBlend;
        CurrentDistance = distance;

        Pose(focus, distance, screenY, out var position, out var rotation);
        transform.SetPositionAndRotation(position, rotation);
    }

    private static float Smooth(float current, float target, ref float velocity, float time, float dt)
    {
        if (time <= 0f || dt <= 0f) { velocity = 0f; return target; }
        return Mathf.SmoothDamp(current, target, ref velocity, time, Mathf.Infinity, dt);
    }

    /// <summary>화면 배치의 기준점. 판 모드는 판 중심, 캐릭터 모드는 내 캐릭터.</summary>
    private Vector3 TargetPoint()
    {
        if (followPlayer && localPlayer != null) return localPlayer.position + Vector3.up * 1f;
        float height = deckHeight > 0f ? deckHeight : platform.transform.position.y - platform.Sphere.position.y + 1f;
        focusHeight = height;
        return wheelVisual.position + Vector3.up * height;
    }

    /// <summary>
    /// 기준점을 화면 높이 screenY에 두는 카메라 위치와 회전.
    /// </summary>
    private void Pose(Vector3 center, float distance, float screenY, out Vector3 position, out Quaternion rotation)
    {
        float tanHalf = Mathf.Tan(FramingFieldOfView * 0.5f * Mathf.Deg2Rad);
        float raise = Mathf.Atan((0.5f - screenY) * 2f * tanHalf) * Mathf.Rad2Deg;
        position = center + Quaternion.Euler(pitch, yaw, 0f) * (Vector3.back * distance);
        rotation = Quaternion.Euler(pitch - raise, yaw, 0f);
    }

    /// <summary>판 둘레(반지름 radius)가 화면 여백 안에 모두 들어오는 가장 가까운 거리.</summary>
    private float FitDistance(Vector3 center, float radius, float screenY)
    {
        float lo = distanceRange.x, hi = distanceRange.y;
        if (Fits(center, radius, screenY, lo)) return lo;
        for (int i = 0; i < 14; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Fits(center, radius, screenY, mid)) hi = mid; else lo = mid;
        }
        return hi;
    }

    private bool Fits(Vector3 center, float radius, float screenY, float distance)
    {
        Pose(center, distance, screenY, out var position, out var rotation);
        Quaternion inverse = Quaternion.Inverse(rotation);
        float tanHalf = Mathf.Tan(FramingFieldOfView * 0.5f * Mathf.Deg2Rad);
        float aspect = Mathf.Max(0.1f, cam.aspect);
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f;
            Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            Vector3 local = inverse * (p - position);
            if (local.z <= 0.1f) return false;
            float vx = 0.5f + local.x / (local.z * 2f * tanHalf * aspect);
            float vy = 0.5f + local.y / (local.z * 2f * tanHalf);
            if (vx < screenMargin || vx > 1f - screenMargin || vy < screenMargin || vy > 1f - screenMargin) return false;
        }
        if (keepWheelOnScreen && !followPlayer)
        {
            // 휠 아래쪽 반구에서 화면 아래로 가장 튀어나오는 점들
            float r = platform.SphereRadius;
            Vector3 wheel = center - Vector3.up * focusHeight;
            Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            for (int i = 0; i <= 4; i++)
            {
                float a = i * 22.5f * Mathf.Deg2Rad;
                Vector3 p = wheel + (Vector3.down * Mathf.Cos(a) + back * Mathf.Sin(a)) * r;
                Vector3 local = inverse * (p - position);
                if (local.z <= 0.1f) return false;
                if (0.5f + local.y / (local.z * 2f * tanHalf) < screenMargin * 0.5f) return false;
            }
        }
        return true;
    }

    private void UpdateSpeedFraming(float dt)
    {
        if (dt <= 0f) return;
        Vector3 position = wheelVisual.position;
        if (hasLastWheel)
        {
            Vector3 velocity = (position - lastWheelPosition) / dt;
            // 리셋 등으로 순간이동하면 버림
            if (velocity.sqrMagnitude < 60f * 60f)
                averageVelocity = Vector3.Lerp(averageVelocity, velocity, 1f - Mathf.Exp(-dt / speedAverageTime));
        }
        lastWheelPosition = position;
        hasLastWheel = true;

        Vector3 flat = averageVelocity;
        flat.y = 0f;
        float speed = flat.magnitude;
        if (!fastFraming && speed > fastEnterSpeed) fastFraming = true;
        else if (fastFraming && speed < fastExitSpeed) fastFraming = false;

        float target = speedFraming && fastFraming ? 1f : 0f;
        framingBlend = Mathf.SmoothDamp(framingBlend, target, ref framingBlendVelocity, framingBlendTime * 0.5f);
    }

    private void Search()
    {
        nextSearch = Time.unscaledTime + 0.5f;
        if (platform == null) platform = FindFirstObjectByType<PlatformTilt>();
        if (platform != null && wheelVisual == null && platform.Sphere != null)
        {
            var visual = platform.Sphere.Find("Visual");
            wheelVisual = visual != null ? visual : platform.Sphere;
        }
        if (localPlayer == null)
        {
            foreach (var p in FindObjectsByType<Player>(FindObjectsSortMode.None))
                if (p.Object != null && p.Object.IsValid && p.Object.HasInputAuthority) { localPlayer = p.transform; break; }
        }
    }

    private void HandleInput()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) followPlayer = !followPlayer;
#else
        if (Input.GetKeyDown(toggleKey)) followPlayer = !followPlayer;
#endif
    }
}
