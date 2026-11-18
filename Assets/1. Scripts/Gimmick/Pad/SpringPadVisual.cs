using UnityEngine;

/// <summary>
/// 스프링 점프 패드의 판 움직임. 휠이 패드에 닿아 날아가면 판이 쏘는 쪽 가장자리를 축으로 젖혀지며 솟았다가 돌아옴.
/// 기둥이 튀어 오르는 것은 JumpPad의 애니메이션이 하고, 여기서는 그 기둥이 휠을 뚫고 올라가지 않게 높이만 제한함.
/// 모든 피어가 휠 위치만 보고 스스로 재생함.
/// </summary>
[DefaultExecutionOrder(200)]
public class SpringPadVisual : MonoBehaviour
{
    [Tooltip("젖혀질 판 모델.")]
    [SerializeField] private Transform plate;

    [Tooltip("튀어 오르는 기둥 모델. 휠에 닿지 않게 높이를 제한함.")]
    [SerializeField] private Transform column;

    [Tooltip("기둥 기준점에서 기둥 윗면까지 길이(기둥의 로컬 Y 단위).")]
    [Min(0f)][SerializeField] private float columnLength = 4.44f;

    [Tooltip("기둥 너비의 절반(기둥의 로컬 단위).")]
    [Min(0f)][SerializeField] private float columnHalfWidth = 0.93f;

    [Tooltip("기둥이 내려가 있을 때의 로컬 Y.")]
    [SerializeField] private float columnRestY = -4.1f;

    [Tooltip("기둥 윗면과 휠 사이에 남기는 틈(m).")]
    [Min(0f)][SerializeField] private float columnGap = 0.6f;

    [Tooltip("판이 젖혀지는 각도(도).")]
    [SerializeField] private float flipAngle = 18f;

    [Tooltip("젖혀지면서 판이 위로 솟는 높이(m).")]
    [Min(0f)][SerializeField] private float riseHeight = 1.2f;

    [Tooltip("판이 다 젖혀지는 데 걸리는 시간(초).")]
    [Min(0.01f)][SerializeField] private float flipTime = 0.08f;

    [Tooltip("판이 제자리로 돌아오는 데 걸리는 시간(초).")]
    [Min(0.01f)][SerializeField] private float returnTime = 0.9f;

    [Tooltip("판 중심에서 축이 되는 가장자리까지 거리(m).")]
    [Min(0f)][SerializeField] private float hingeOffset = 4.4f;

    [Tooltip("휠 중심이 패드에서 이 거리(m) 안에 있고 쏘는 쪽으로 움직이면 재생함.")]
    [Min(0f)][SerializeField] private float triggerRange = 8f;

    private Transform wheel;
    private float wheelRadius;
    private Vector3 lastWheelPosition;
    private Vector3 plateRest;
    private Quaternion plateRestRotation;
    private float playTime = -100f;
    private float nextSearch;

    private void Awake()
    {
        if (plate == null) return;
        plateRest = plate.position - transform.position;
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
        if (Time.time - playTime > flipTime + returnTime && toWheel.magnitude < triggerRange && moved.y / dt > 3f && Vector3.Dot(moved, launch) / dt > 3f)
            playTime = Time.time;

        float t = Time.time - playTime;
        float k = t < flipTime ? t / flipTime : 1f - Mathf.SmoothStep(0f, 1f, (t - flipTime) / returnTime);
        k = Mathf.Clamp01(k);

        // 쏘는 쪽 가장자리를 축으로 판을 들어 올림
        Vector3 axis = Vector3.Cross(Vector3.up, launch);
        Quaternion tilt = Quaternion.AngleAxis(flipAngle * k, axis);
        Vector3 hinge = rest + launch * hingeOffset;
        plate.SetPositionAndRotation(hinge + tilt * (rest - hinge) + Vector3.up * (riseHeight * k), tilt * plateRestRotation);

        LimitColumn();
    }

    /// <summary>애니메이션이 올린 기둥이 휠 안으로 들어가 있으면 휠 겉면 바로 아래까지 내림.</summary>
    private void LimitColumn()
    {
        if (column == null || wheelRadius <= 0f) return;
        Vector3 local = column.localPosition;
        float wanted = local.y;
        if (wanted <= columnRestY) return;

        float low = columnRestY, high = wanted;
        float limit = wheelRadius + columnGap;
        if (TopDistance(local, high) >= limit) return;
        for (int i = 0; i < 8; i++)
        {
            float mid = (low + high) * 0.5f;
            if (TopDistance(local, mid) >= limit) low = mid;
            else high = mid;
        }
        column.localPosition = new Vector3(local.x, low, local.z);
    }

    /// <summary>기둥을 로컬 Y가 y인 자리에 뒀을 때 기둥 윗면(네모)에서 휠 중심에 가장 가까운 점까지 거리.</summary>
    private float TopDistance(Vector3 local, float y)
    {
        Transform parent = column.parent;
        Vector3 top = parent.TransformPoint(new Vector3(local.x, y, local.z) + column.localRotation * Vector3.up * columnLength);
        Vector3 right = parent.TransformVector(column.localRotation * Vector3.right * columnHalfWidth);
        Vector3 forward = parent.TransformVector(column.localRotation * Vector3.forward * columnHalfWidth);
        Vector3 d = wheel.position - top;
        Vector3 nearest = top
            + right.normalized * Mathf.Clamp(Vector3.Dot(d, right.normalized), -right.magnitude, right.magnitude)
            + forward.normalized * Mathf.Clamp(Vector3.Dot(d, forward.normalized), -forward.magnitude, forward.magnitude);
        return Vector3.Distance(nearest, wheel.position);
    }
}
