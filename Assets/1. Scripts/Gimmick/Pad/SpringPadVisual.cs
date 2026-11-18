using UnityEngine;

/// <summary>
/// 스프링 점프 패드의 판 움직임. 휠이 패드에 닿아 날아가면 판이 쏘는 방향 반대쪽 가장자리를 축으로 젖혀졌다가 돌아옴.
/// 모든 피어가 휠 위치만 보고 스스로 재생함.
/// </summary>
public class SpringPadVisual : MonoBehaviour
{
    [Tooltip("젖혀질 판 모델.")]
    [SerializeField] private Transform plate;

    [Tooltip("튀어 오르는 기둥 모델(판의 자식). 판 윗면과 휠 아랫면 사이 거리보다 높이 솟지 않게 함.")]
    [SerializeField] private Transform column;

    [Tooltip("판이 젖혀지는 각도(도).")]
    [SerializeField] private float flipAngle = 50f;

    [Tooltip("젖혀지면서 판이 위로 솟는 높이(m).")]
    [Min(0f)][SerializeField] private float riseHeight = 1.2f;

    [Tooltip("판이 다 젖혀지는 데 걸리는 시간(초).")]
    [Min(0.01f)][SerializeField] private float flipTime = 0.08f;

    [Tooltip("판이 제자리로 돌아오는 데 걸리는 시간(초).")]
    [Min(0.01f)][SerializeField] private float returnTime = 0.9f;

    [Tooltip("판 중심에서 축이 되는 가장자리까지 거리(m).")]
    [Min(0f)][SerializeField] private float hingeOffset = 4.2f;

    [Tooltip("휠 중심이 패드에서 이 거리(m) 안에 있고 쏘는 쪽으로 움직이면 재생함.")]
    [Min(0f)][SerializeField] private float triggerRange = 8f;

    private Transform wheel;
    private float wheelRadius;
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
        plate.SetPositionAndRotation(hinge + tilt * (rest - hinge) + Vector3.up * (riseHeight * k), tilt * plateRestRotation);
        LimitColumn();
    }

    /// <summary>기둥이 판 윗면 위로 솟는 높이를 판 윗면과 휠 아랫면 사이의 최단 거리까지로 제한함.</summary>
    private void LimitColumn()
    {
        if (column == null || wheelRadius <= 0f) return;
        var plateMesh = plate.GetComponent<MeshFilter>();
        var columnMesh = column.GetComponent<MeshFilter>();
        if (plateMesh == null || columnMesh == null) return;
        float plateTop = plateMesh.sharedMesh.bounds.max.y;
        float columnTop = columnMesh.sharedMesh.bounds.max.y;

        // 판 윗면(평면)에서 휠 겉면까지의 최단 거리
        Vector3 normal = plate.up;
        float gap = Mathf.Max(0f, Vector3.Dot(wheel.position - plate.TransformPoint(new Vector3(0f, plateTop, 0f)), normal) - wheelRadius);

        Vector3 local = column.localPosition;
        float highest = plateTop - columnTop + gap / Mathf.Max(plate.lossyScale.y, 1e-4f);
        if (local.y > highest) column.localPosition = new Vector3(local.x, highest, local.z);
    }
}
