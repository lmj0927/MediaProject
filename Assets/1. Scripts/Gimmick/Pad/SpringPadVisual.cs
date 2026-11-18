using UnityEngine;

/// <summary>
/// 스프링 점프 패드의 판과 기둥 움직임. 휠이 패드에 닿아 날아가면 기둥이 솟으면서 판을 쏘는 쪽 가장자리를 축으로 젖혀 올렸다가 돌아옴.
/// 판과 기둥은 휠 아랫면보다 높이 올라가지 않음. 모든 피어가 휠 위치만 보고 스스로 재생함.
/// </summary>
public class SpringPadVisual : MonoBehaviour
{
    [Tooltip("젖혀질 판 모델.")]
    [SerializeField] private Transform plate;

    [Tooltip("판을 밀어 올리는 기둥 모델. 판의 자식이어도 됨.")]
    [SerializeField] private Transform column;

    [Tooltip("판이 젖혀지는 각도(도).")]
    [SerializeField] private float flipAngle = 50f;

    [Tooltip("판이 다 젖혀지는 데 걸리는 시간(초).")]
    [Min(0.01f)][SerializeField] private float flipTime = 0.12f;

    [Tooltip("다 젖혀진 채로 머무는 시간(초).")]
    [Min(0f)][SerializeField] private float holdTime = 0.35f;

    [Tooltip("판이 제자리로 돌아오는 데 걸리는 시간(초).")]
    [Min(0.01f)][SerializeField] private float returnTime = 0.8f;

    [Tooltip("판 중심에서 축이 되는 가장자리까지 거리(m).")]
    [Min(0f)][SerializeField] private float hingeOffset = 4.4f;

    [Tooltip("판 기준점에서 판 윗면까지 높이(m).")]
    [Min(0f)][SerializeField] private float plateThickness = 1.5f;

    [Tooltip("휠 중심이 패드에서 이 거리(m) 안에 있고 쏘는 쪽으로 움직이면 재생함.")]
    [Min(0f)][SerializeField] private float triggerRange = 8f;

    private Transform wheel;
    private float wheelRadius;
    private Vector3 lastWheelPosition;
    private Vector3 plateRest;
    private Quaternion plateRestRotation;
    private Vector3 columnRest;
    private Quaternion columnRestRotation;
    private float playTime = -100f;
    private float nextSearch;

    private void Awake()
    {
        if (plate == null) return;
        plateRest = plate.position - transform.position;
        plateRestRotation = plate.rotation;
        if (column == null) return;
        columnRest = column.position - transform.position;
        columnRestRotation = column.rotation;
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
            var sphere = driver.GetComponent<SphereCollider>();
            wheelRadius = sphere != null ? sphere.radius * driver.transform.lossyScale.x : 0f;
            lastWheelPosition = wheel.position;
        }

        // 쏘는 방향(패드의 Y축)의 수평 성분
        Vector3 launch = transform.up;
        launch.y = 0f;
        launch = launch.sqrMagnitude > 1e-4f ? launch.normalized : Vector3.forward;

        Vector3 moved = wheel.position - lastWheelPosition;
        lastWheelPosition = wheel.position;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 rest = transform.position + plateRest;
        Vector3 toWheel = wheel.position - rest;
        toWheel.y = 0f;
        float total = flipTime + holdTime + returnTime;
        if (Time.time - playTime > total && toWheel.magnitude < triggerRange && moved.y / dt > 3f && Vector3.Dot(moved, launch) / dt > 3f)
            playTime = Time.time;

        float t = Time.time - playTime;
        float k = t < flipTime ? t / flipTime : t < flipTime + holdTime ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - flipTime - holdTime) / returnTime);
        k = Mathf.Clamp01(k);

        // 판의 들린 가장자리와 가운데가 휠 아랫면에 닿지 않는 각도까지만 젖힘
        Vector3 hinge = rest + launch * hingeOffset;
        float angle = flipAngle * k;
        for (int i = 0; i < 6 && angle > 0.01f; i++)
        {
            float rad = angle * Mathf.Deg2Rad;
            bool edgeClear = Clear(hinge - launch * (2f * hingeOffset * Mathf.Cos(rad)), rest.y + plateThickness + 2f * hingeOffset * Mathf.Sin(rad));
            bool centerClear = Clear(hinge - launch * (hingeOffset * Mathf.Cos(rad)), rest.y + plateThickness + hingeOffset * Mathf.Sin(rad));
            if (edgeClear && centerClear) break;
            angle *= 0.6f;
            if (i == 5) angle = 0f;
        }

        Vector3 axis = Vector3.Cross(Vector3.up, launch);
        Quaternion tilt = Quaternion.AngleAxis(angle, axis);
        plate.SetPositionAndRotation(hinge + tilt * (rest - hinge), tilt * plateRestRotation);

        // 기둥은 똑바로 선 채 판 가운데를 받쳐 올림
        if (column != null)
            column.SetPositionAndRotation(transform.position + columnRest + Vector3.up * (hingeOffset * Mathf.Sin(angle * Mathf.Deg2Rad)), columnRestRotation);
    }

    /// <summary>수평 위치 point에서 높이 height가 휠 아랫면보다 아래인지.</summary>
    private bool Clear(Vector3 point, float height)
    {
        Vector3 d = wheel.position - point;
        float flat = new Vector2(d.x, d.z).magnitude;
        if (flat >= wheelRadius) return true;
        return height <= wheel.position.y - Mathf.Sqrt(wheelRadius * wheelRadius - flat * flat);
    }
}
