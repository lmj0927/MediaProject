using MoreMountains.Feedbacks;
using UnityEngine;

/// <summary>각 피어의 보간된 휠 이동으로 구동하는 속도 연출. 물리 속도에는 관여하지 않음.</summary>
[DefaultExecutionOrder(900)]
[RequireComponent(typeof(Camera))]
public class TransportSpeedFeel : MonoBehaviour
{
    [SerializeField] private SphereDriver transport;
    [Tooltip("속도 연출이 시작되는 수평 속도(m/s).")]
    [Min(0f)][SerializeField] private float startSpeed = 4f;
    [Tooltip("연출이 가장 강해지는 수평 속도(m/s). 실제 최고속도를 바꾸지는 않음.")]
    [Min(0.1f)][SerializeField] private float fullSpeed = 12f;
    [Tooltip("최대 속도에서 더 넓어지는 시야각(도).")]
    [Range(0f, 25f)][SerializeField] private float extraFieldOfView = 10f;
    [Tooltip("가속과 감속을 부드럽게 연결하는 Feel 스프링.")]
    [SerializeField] private MMSpringFloat speedSpring = new MMSpringFloat { Damping = 1f, Frequency = 2f };

    private Camera view;
    private Transform wheelVisual;
    private Canvas canvas;
    private SpeedStreaksGraphic streaks;
    private Vector3 previousPosition;
    private bool hasPreviousPosition;
    private float baseFieldOfView;
    private float nextSearch;

    public float HorizontalSpeed { get; private set; }
    public float Intensity { get; private set; }
    public float FieldOfViewOffset { get; private set; }

    private void Awake()
    {
        view = GetComponent<Camera>();
        baseFieldOfView = view.fieldOfView;
        var go = new GameObject("Speed Feel Overlay", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40; // 기존 HUD(50) 뒤에만 그림
        var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var graphic = new GameObject("Peripheral Speed Streaks", typeof(RectTransform), typeof(CanvasRenderer), typeof(SpeedStreaksGraphic));
        graphic.transform.SetParent(go.transform, false);
        var rect = graphic.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        streaks = graphic.GetComponent<SpeedStreaksGraphic>();
        streaks.raycastTarget = false;
    }

    private void LateUpdate()
    {
        if (transport == null && Time.unscaledTime >= nextSearch)
        {
            transport = FindFirstObjectByType<SphereDriver>();
            nextSearch = Time.unscaledTime + 0.5f;
        }
        bool active = view.enabled && transport != null && transport.Object != null && transport.Object.IsValid;
        canvas.gameObject.SetActive(active);
        if (!active) { ResetEffect(); return; }
        if (Time.deltaTime <= 0f) return;
        if (wheelVisual == null)
        {
            var visual = transport.transform.Find("Visual");
            wheelVisual = visual != null ? visual : transport.transform;
        }
        Vector3 position = wheelVisual.position;
        Vector3 delta = position - previousPosition;
        previousPosition = position;
        bool hadPosition = hasPreviousPosition;
        hasPreviousPosition = true;
        // 리스폰/순간이동은 가속으로 보이지 않게 즉시 복구함
        if (!hadPosition || delta.magnitude > 8f || delta.magnitude / Time.deltaTime > 60f)
        {
            ResetEffect();
            hasPreviousPosition = true;
            return;
        }
        delta.y = 0f;
        HorizontalSpeed = delta.magnitude / Time.deltaTime;
        float target = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(startSpeed, Mathf.Max(startSpeed + 0.1f, fullSpeed), HorizontalSpeed));
        speedSpring.MoveTo(target);
        speedSpring.UpdateSpringValue(Mathf.Min(Time.deltaTime, 0.05f));
        Intensity = Mathf.Clamp01(speedSpring.CurrentValue);
        FieldOfViewOffset = extraFieldOfView * Intensity;
        view.fieldOfView = baseFieldOfView + FieldOfViewOffset;
        streaks.SetSpeed(Intensity, Time.deltaTime);
    }

    private void ResetEffect()
    {
        HorizontalSpeed = Intensity = FieldOfViewOffset = 0f;
        hasPreviousPosition = false;
        speedSpring.MoveToInstant(0f);
        view.fieldOfView = baseFieldOfView;
        streaks.SetSpeed(0f, 0f);
    }

    private void OnDisable()
    {
        if (view == null) return;
        ResetEffect();
        canvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }
}
