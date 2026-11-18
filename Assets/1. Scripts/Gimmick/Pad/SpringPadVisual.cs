using UnityEngine;

/// <summary>
/// 스프링 점프 패드의 판 움직임. 휠이 패드에 닿아 날아가면 판이 쏘는 방향 반대쪽 가장자리를 축으로 젖혀졌다가 돌아옴.
/// 모든 피어가 휠 위치만 보고 스스로 재생함.
/// </summary>
public class SpringPadVisual : MonoBehaviour
{
    [Tooltip("젖혀질 판 모델.")]
    [SerializeField] private Transform plate;

    [Tooltip("판이 젖혀지는 각도(도).")]
    [SerializeField] private float flipAngle = 50f;

    [Tooltip("판이 다 젖혀지는 데 걸리는 시간(초).")]
    [Min(0.01f)][SerializeField] private float flipTime = 0.08f;

    [Tooltip("판이 제자리로 돌아오는 데 걸리는 시간(초).")]
    [Min(0.01f)][SerializeField] private float returnTime = 0.9f;

    [Tooltip("판 중심에서 축이 되는 가장자리까지 거리(m).")]
    [Min(0f)][SerializeField] private float hingeOffset = 4.2f;

    [Tooltip("휠 중심이 패드에서 이 거리(m) 안에 있고 쏘는 쪽으로 움직이면 재생함.")]
    [Min(0f)][SerializeField] private float triggerRange = 8f;

    private Transform wheel;
    private Vector3 lastWheelPosition;
    private Vector3 plateRestPosition;
    private Quaternion plateRestRotation;
    private float playTime = -100f;
    private float nextSearch;

    private void Awake()
    {
        if (plate == null) return;
        plateRestPosition = plate.position - transform.position;
        plateRestRotation = plate.rotation;
    }

    private void LateUpdate()
    {
        if (plate == null) return;
        if (wheel == null)
        {
            if (Time.unscaledTime < nextSearch) return;
            nextSearch = Time.unscaledTime + 1f;
            var driver = FindFirstObjectByType<SphereDriver>();
            if (driver == null) return;
            wheel = driver.transform;
            lastWheelPosition = wheel.position;
        }

        // 쏘는 방향(패드의 Y축)의 수평 성분
        Vector3 launch = transform.up;
        launch.y = 0f;
        launch = launch.sqrMagnitude > 1e-4f ? launch.normalized : Vector3.forward;

        Vector3 moved = wheel.position - lastWheelPosition;
        lastWheelPosition = wheel.position;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 toWheel = wheel.position - transform.position;
        toWheel.y = 0f;
        if (Time.time - playTime > flipTime + returnTime && toWheel.magnitude < triggerRange && moved.y / dt > 3f && Vector3.Dot(moved, launch) / dt > 3f)
            playTime = Time.time;

        float t = Time.time - playTime;
        float k = t < flipTime ? t / flipTime : 1f - Mathf.SmoothStep(0f, 1f, (t - flipTime) / returnTime);
        k = Mathf.Clamp01(k);

        // 쏘는 쪽 반대 가장자리를 축으로 판을 들어 올림
        Vector3 axis = Vector3.Cross(Vector3.up, launch);
        Quaternion tilt = Quaternion.AngleAxis(flipAngle * k, axis);
        Vector3 hinge = transform.position + plateRestPosition + launch * hingeOffset;
        Vector3 rest = transform.position + plateRestPosition;
        plate.SetPositionAndRotation(hinge + tilt * (rest - hinge), tilt * plateRestRotation);
    }
}
