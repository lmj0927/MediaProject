using UnityEngine;

/// <summary>
/// 스프링 점프 패드의 판 움직임. 휠이 패드에 닿아 날아가면 판이 쏘는 쪽 가장자리를 축으로 젖혀지며 솟았다가 돌아옴.
/// 기둥이 튀어 오르는 것은 JumpPad의 애니메이션이 함. 판과 기둥이 휠 안으로 들어가지 않게 매 프레임 그리기 직전에 높이를 제한함.
/// 모든 피어가 휠 위치만 보고 스스로 재생함.
/// </summary>
public class SpringPadVisual : MonoBehaviour
{
    [Tooltip("젖혀질 판 모델.")]
    [SerializeField] private Transform plate;

    [Tooltip("튀어 오르는 기둥 모델.")]
    [SerializeField] private Transform column;

    [Tooltip("기둥이 내려가 있을 때의 로컬 Y.")]
    [SerializeField] private float columnRestY = -4.1f;

    [Tooltip("판과 기둥이 휠 겉면 안으로 들어가도 되는 깊이(m). 가만히 있을 때 판이 휠에 묻혀 있는 만큼.")]
    [Min(0f)][SerializeField] private float allowedOverlap = 0.4f;

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
    private Bounds plateBounds;
    private Bounds columnBounds;
    private float playTime = -100f;
    private float nextSearch;

    private void Awake()
    {
        if (plate == null) return;
        plateRest = plate.position - transform.position;
        plateRestRotation = plate.rotation;
        plateBounds = MeshBounds(plate);
        if (column != null) columnBounds = MeshBounds(column);
    }

    private static Bounds MeshBounds(Transform t)
    {
        var filter = t.GetComponent<MeshFilter>();
        return filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
    }

    private void OnEnable() => Application.onBeforeRender += Pose;

    private void OnDisable() => Application.onBeforeRender -= Pose;

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

        Vector3 moved = wheel.position - lastWheelPosition;
        lastWheelPosition = wheel.position;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 toWheel = wheel.position - (transform.position + plateRest);
        toWheel.y = 0f;
        if (Time.time - playTime > flipTime + returnTime && toWheel.magnitude < triggerRange && moved.y / dt > 3f && Vector3.Dot(moved, LaunchDirection()) / dt > 3f)
            playTime = Time.time;
    }

    /// <summary>쏘는 방향(패드의 Y축)의 수평 성분.</summary>
    private Vector3 LaunchDirection()
    {
        Vector3 launch = transform.up;
        launch.y = 0f;
        return launch.sqrMagnitude > 1e-4f ? launch.normalized : Vector3.forward;
    }

    /// <summary>판을 젖힌 정도 k(0~1)에 맞는 자리에 둠.</summary>
    private void PlacePlate(float k)
    {
        Vector3 launch = LaunchDirection();
        Vector3 rest = transform.position + plateRest;
        Quaternion tilt = Quaternion.AngleAxis(flipAngle * k, Vector3.Cross(Vector3.up, launch));
        Vector3 hinge = rest + launch * hingeOffset;
        plate.SetPositionAndRotation(hinge + tilt * (rest - hinge) + Vector3.up * (riseHeight * k), tilt * plateRestRotation);
    }

    /// <summary>모델 상자가 휠 겉면 안으로 들어간 깊이(m). 안 닿으면 0 이하.</summary>
    private float Overlap(Transform model, Bounds bounds)
    {
        Vector3 local = model.InverseTransformPoint(wheel.position);
        Vector3 nearest = model.TransformPoint(bounds.ClosestPoint(local));
        return wheelRadius - Vector3.Distance(nearest, wheel.position);
    }

    /// <summary>그리기 직전: 판을 젖히고, 판과 기둥이 휠 안으로 들어가 있으면 닿지 않는 데까지 낮춤.</summary>
    private void Pose()
    {
        if (plate == null) return;
        float t = Time.time - playTime;
        float k = t < flipTime ? t / flipTime : 1f - Mathf.SmoothStep(0f, 1f, (t - flipTime) / returnTime);
        k = Mathf.Clamp01(k);

        // 기둥이 판과 같이 움직이므로, 판을 먼저 정하기 위해 기둥은 잠시 내려 둠
        float wantedColumn = columnRestY;
        if (column != null)
        {
            wantedColumn = column.localPosition.y;
            SetColumn(columnRestY);
        }

        PlacePlate(k);
        if (wheel != null && wheelRadius > 0f && k > 0f && Overlap(plate, plateBounds) > allowedOverlap)
        {
            float low = 0f, high = k;
            for (int i = 0; i < 8; i++)
            {
                float mid = (low + high) * 0.5f;
                PlacePlate(mid);
                if (Overlap(plate, plateBounds) > allowedOverlap) high = mid;
                else low = mid;
            }
            PlacePlate(low);
        }

        if (column == null) return;
        SetColumn(wantedColumn);
        if (wheel == null || wheelRadius <= 0f || wantedColumn <= columnRestY || Overlap(column, columnBounds) <= allowedOverlap) return;
        float down = columnRestY, up = wantedColumn;
        for (int i = 0; i < 8; i++)
        {
            float mid = (down + up) * 0.5f;
            SetColumn(mid);
            if (Overlap(column, columnBounds) > allowedOverlap) up = mid;
            else down = mid;
        }
        SetColumn(down);
    }

    private void SetColumn(float y)
    {
        Vector3 local = column.localPosition;
        column.localPosition = new Vector3(local.x, y, local.z);
    }
}
