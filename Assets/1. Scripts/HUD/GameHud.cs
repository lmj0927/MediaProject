using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 HUD. 실행하면 화면 캔버스를 만들고 매 프레임 판·휠·플레이어 상태를 읽어 그림.
/// 각 피어가 자기 화면에서만 그림.
///   왼쪽 아래: balanceWidget로 고름. Travel(기본): 진행 방향 원 + 속도 막대·숫자 + 수평계(켜고 끔). Disc: 판 축소판(기울기, 진행 방향, 판 위 플레이어·몬스터·폭탄)
///   가운데 위: 코스 진행도 + 길 방향 나침반·다음 체크포인트까지 거리 (씬에 CourseRoute가 있을 때만)
///   왼쪽 위: 팀원 상태(판 밖에 있으면 회색 + OFF)
///   화면 가장자리: 판 위에 있지만 화면 밖에 있는 폭탄·몬스터 쪽을 가리킴
/// 원판과 나침반은 카메라가 보는 방향을 화면 위쪽으로 삼음.
/// 코스와 카메라는 비워 두면 씬에서 찾음(코스: CourseRoute, 카메라: 메인 카메라).
/// </summary>
[DefaultExecutionOrder(1100)]
public class GameHud : MonoBehaviour
{
    public enum BalanceWidget { Disc, Travel }
    public enum SpeedUnit { MetersPerSecond, KilometersPerHour }

    [Header("Balance Widget")]
    [Tooltip("좌하단 표시. Disc: 판 축소판(판 위 사람·몬스터 표시 포함), Travel: 진행 방향 + 속도 (+ 수평계).")]
    [SerializeField] private BalanceWidget balanceWidget = BalanceWidget.Travel;

    [Tooltip("Travel: 수평계(판 기울기)를 보여줌.")]
    [SerializeField] private bool showTilt = true;

    [Tooltip("Travel: 켜면 구슬이 판의 낮은 쪽으로, 끄면 물방울처럼 높은 쪽으로 감.")]
    [SerializeField] private bool tiltShowsLowSide = true;

    [Tooltip("Travel: 켜면 방향 화살표가 원 중심을 지나 양쪽에 걸침. 끄면 중심에서 바깥으로만 뻗음.")]
    [SerializeField] private bool arrowThroughCenter = true;

    [Tooltip("Travel: 속도 숫자 단위.")]
    [SerializeField] private SpeedUnit speedUnit = SpeedUnit.MetersPerSecond;

    [Header("Sources")]
    [Tooltip("진행도·나침반용 코스. 비우면 씬에서 찾고, 씬에도 없으면 진행바·나침반·거리를 숨김.")]
    [SerializeField] private CourseRoute course;

    [Tooltip("화면 기준 카메라. 비우면 메인 카메라를 씀.")]
    [SerializeField] private Camera viewCamera;

    [Tooltip("나침반이 가리키는 곳: 경로를 따라 지금 위치에서 이만큼(m) 앞.")]
    [SerializeField] private float compassLookAhead = 15f;

    [Tooltip("휠 최고 속도(m/s). 속도 막대와 원판 화살표 길이의 기준.")]
    [SerializeField] private float topSpeed = 11.8f;

    [Tooltip("판 높이에서 위아래로 이 거리(m) 안에 있어야 판 위로 봄.")]
    [SerializeField] private float deckHeightRange = 4f;

    [Header("Smoothing")]
    [Tooltip("기울기 표시가 실제 기울기를 따라가는 빠르기(1/초). 클수록 바로 따라감.")]
    [SerializeField] private float leanFollow = 12f;

    [Tooltip("휠 속도를 재는 구간(초). 이 시간 전 위치와 지금 위치의 차이로 잼.")]
    [SerializeField] private float velocityWindow = 0.25f;

    [Tooltip("진행 방향 화살표와 속도 막대가 잰 속도를 따라가는 시간(초).")]
    [SerializeField] private float travelSmoothTime = 0.15f;

    [Header("Layout (1920x1080 기준)")]
    [SerializeField] private float discSize = 330f;
    [SerializeField] private float margin = 36f;

    [Tooltip("화면 위쪽 진행바의 위 여백(px).")]
    [SerializeField] private float topMargin = 16f;

    [Tooltip("진행바 아래 나침반을 보여줌. 끄면 거리 표시가 진행바 바로 아래로 올라감.")]
    [SerializeField] private bool showCompass = true;
    [SerializeField] private float edgeInset = 70f;

    private RectTransform root;
    private BalanceDiscGraphic disc;
    private TravelDialGraphic travelDial;
    private SpeedBarGraphic speedBar;
    private LevelGraphic level;
    private TextMeshProUGUI speedText;
    private ProgressBarGraphic progressBar;
    private CompassGraphic compass;
    private TextMeshProUGUI distanceText;
    private readonly List<(ChipGraphic chip, TextMeshProUGUI name, TextMeshProUGUI state)> chips = new();
    private readonly List<EdgeArrowGraphic> edgeArrows = new();

    private PlatformTilt platform;
    private Transform wheel;
    private float nextSearch;
    private Player[] players = new Player[0];
    private Monster[] monsters = new Monster[0];
    private Bomb[] bombs = new Bomb[0];

    private Vector2 shownLean;
    private readonly Queue<(float time, Vector3 position)> wheelSamples = new();
    private Vector3 wheelVelocity;
    private Vector3 wheelVelocitySmoothing;
    private readonly CourseRoute.Tracker courseTracker = new();
    private Vector3 viewForward = Vector3.forward;
    private Vector3 viewRight = Vector3.right;

    private void Awake() => BuildCanvas();

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextSearch) Search();

        var cam = viewCamera != null ? viewCamera : Camera.main;
        bool active = platform != null && wheel != null && cam != null;
        if (root.gameObject.activeSelf != active) root.gameObject.SetActive(active);
        if (!active) return;

        viewForward = cam.transform.forward;
        viewForward.y = 0f;
        viewForward = viewForward.sqrMagnitude > 1e-4f ? viewForward.normalized : Vector3.forward;
        viewRight = new Vector3(viewForward.z, 0f, -viewForward.x);

        UpdateWheelVelocity(Time.deltaTime);
        UpdateLean(Time.deltaTime);
        bool useDisc = balanceWidget == BalanceWidget.Disc;
        SetActive(disc, useDisc);
        SetActive(travelDial, !useDisc);
        SetActive(speedBar, !useDisc);
        SetActive(speedText, !useDisc);
        SetActive(level, !useDisc && showTilt);
        if (useDisc) UpdateDisc(); else UpdateTravel();
        UpdateCourse(Time.deltaTime);
        UpdateTeam();
        UpdateEdgeArrows(cam);
    }

    // ---- 읽기 ----

    private void Search()
    {
        nextSearch = Time.unscaledTime + 0.5f;
        if (platform == null) platform = FindFirstObjectByType<PlatformTilt>();
        if (course == null) course = FindFirstObjectByType<CourseRoute>();
        wheel = platform != null ? platform.Sphere : null;
        players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        monsters = FindObjectsByType<Monster>(FindObjectsSortMode.None);
        bombs = FindObjectsByType<Bomb>(FindObjectsSortMode.None);
    }

    /// <summary>월드 수평 벡터를 화면 기준(x 오른쪽, y 위쪽)으로 바꿈.</summary>
    private Vector2 ToView(Vector3 world) => new Vector2(Vector3.Dot(world, viewRight), Vector3.Dot(world, viewForward));

    /// <summary>판 위에 있는지. 있으면 원판 좌표(반지름 기준 -1~1)를 돌려줌.</summary>
    private bool OnDeck(Vector3 position, out Vector2 discPosition)
    {
        Vector3 offset = position - platform.transform.position;
        float radius = Mathf.Max(0.01f, platform.PlatformRadius);
        discPosition = ToView(offset) / radius;
        return Mathf.Abs(offset.y) < deckHeightRange && new Vector2(offset.x, offset.z).magnitude <= radius * 1.1f;
    }

    private static bool Alive(Fusion.NetworkBehaviour behaviour) =>
        behaviour != null && behaviour.Object != null && behaviour.Object.IsValid;

    /// <summary>
    /// 휠 속도. velocityWindow 전 위치와 지금 위치의 차이로 잰 뒤 부드럽게 따라감.
    /// </summary>
    private void UpdateWheelVelocity(float dt)
    {
        if (dt <= 0f) return;
        float now = Time.time;
        Vector3 position = wheel.position;
        wheelSamples.Enqueue((now, position));
        while (wheelSamples.Count > 2 && now - wheelSamples.Peek().time > velocityWindow) wheelSamples.Dequeue();

        var oldest = wheelSamples.Peek();
        float span = now - oldest.time;
        if (span < 1e-3f) return;
        Vector3 measured = (position - oldest.position) / span;
        // 리셋 등으로 순간이동하면 버림
        if (measured.sqrMagnitude > 60f * 60f) { wheelSamples.Clear(); return; }
        wheelVelocity = Vector3.SmoothDamp(wheelVelocity, measured, ref wheelVelocitySmoothing, Mathf.Max(0.01f, travelSmoothTime), Mathf.Infinity, dt);
    }

    // ---- 균형 원판 ----

    private static void SetActive(Component c, bool on)
    {
        if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
    }

    private void UpdateLean(float dt)
    {
        Vector2 targetLean = ToView(platform.DownhillDirection) * platform.NormalizedTilt;
        shownLean = Vector2.Lerp(shownLean, targetLean, 1f - Mathf.Exp(-leanFollow * dt));
    }

    /// <summary>휠 수평 속도를 화면 기준으로, 최고 속도를 1로 나눈 값.</summary>
    private Vector2 TravelRatio()
    {
        Vector3 flat = wheelVelocity;
        flat.y = 0f;
        return ToView(flat) / Mathf.Max(0.1f, topSpeed);
    }

    // ---- 진행 방향·속도 (새 좌하단) ----

    private void UpdateTravel()
    {
        Vector2 travel = TravelRatio();
        travelDial.travel = travel;
        travelDial.throughCenter = arrowThroughCenter;
        travelDial.Redraw();

        speedBar.ratio = Mathf.Clamp01(travel.magnitude);
        speedBar.Redraw();

        float speed = travel.magnitude * Mathf.Max(0.1f, topSpeed);
        speedText.text = speedUnit == SpeedUnit.KilometersPerHour ? $"{speed * 3.6f:0} km/h" : $"{speed:0.0} m/s";

        if (showTilt)
        {
            level.lean = shownLean;
            level.showLowSide = tiltShowsLowSide;
            level.Redraw();
        }
    }

    private void UpdateDisc()
    {
        disc.lean = shownLean;
        disc.travel = TravelRatio();

        disc.markers.Clear();
        foreach (var p in players)
        {
            if (!Alive(p) || !OnDeck(p.transform.position, out var at)) continue;
            disc.markers.Add(new BalanceDiscGraphic.Marker
            {
                position = at, kind = BalanceDiscGraphic.MarkerKind.Player,
                color = HudColors.ForCharacter(p.name), local = p.Object.HasInputAuthority
            });
        }
        foreach (var m in monsters)
            if (Alive(m) && OnDeck(m.transform.position, out var at))
                disc.markers.Add(new BalanceDiscGraphic.Marker { position = at, kind = BalanceDiscGraphic.MarkerKind.Monster });
        foreach (var b in bombs)
            if (Alive(b) && OnDeck(b.transform.position, out var at))
                disc.markers.Add(new BalanceDiscGraphic.Marker { position = at, kind = BalanceDiscGraphic.MarkerKind.Bomb });

        disc.Redraw();
    }

    // ---- 진행도·나침반 ----

    private void UpdateCourse(float dt)
    {
        bool hasCourse = course != null && course.IsValid;
        progressBar.gameObject.SetActive(hasCourse);
        compass.gameObject.SetActive(hasCourse && showCompass);
        distanceText.gameObject.SetActive(hasCourse);
        if (!hasCourse) return;
        distanceText.rectTransform.anchoredPosition = new Vector2(0f, showCompass ? -topMargin - 154f : -topMargin - 56f);

        course.Track(wheel.position, dt, courseTracker);
        float total = Mathf.Max(0.01f, course.TotalLength);

        var checkpoints = course.CheckpointLengths;
        if (progressBar.checkpoints.Length != checkpoints.Count) progressBar.checkpoints = new float[checkpoints.Count];
        for (int i = 0; i < checkpoints.Count; i++) progressBar.checkpoints[i] = checkpoints[i] / total;
        // 진행바는 지금 위치를 따라 뒤로도 줄지만, 마지막으로 지난 체크포인트 아래로는 안 내려감
        progressBar.progress = Mathf.Max(courseTracker.along, course.PassedCheckpoint(courseTracker)) / total;
        progressBar.reached = courseTracker.best / total;
        progressBar.Redraw();

        // 나침반: 경로를 따라 조금 앞의 곳을 가리킴(길 밖에 있으면 길로 돌아가는 쪽이 됨)
        Vector3 to = course.PointAtLength(courseTracker.along + compassLookAhead) - wheel.position;
        to.y = 0f;
        Vector2 view = ToView(to);
        if (view.sqrMagnitude > 1e-4f) compass.angle = Mathf.Atan2(view.x, view.y) * Mathf.Rad2Deg;
        compass.Redraw();

        // 거리: 다음 체크포인트(다 지났으면 결승점)까지 경로를 따라 잰 거리
        float remaining = Mathf.Max(0f, course.NextCheckpoint(courseTracker) - courseTracker.along);
        distanceText.text = course.IsFinished(courseTracker) ? "GOAL" : $"{Mathf.RoundToInt(remaining)} m";
    }

    // ---- 팀원 상태 ----

    private readonly List<Player> sortedPlayers = new();

    private void UpdateTeam()
    {
        sortedPlayers.Clear();
        foreach (var p in players) if (Alive(p)) sortedPlayers.Add(p);
        sortedPlayers.Sort((a, b) =>
        {
            int local = b.Object.HasInputAuthority.CompareTo(a.Object.HasInputAuthority);
            return local != 0 ? local : string.CompareOrdinal(a.name, b.name);
        });

        while (chips.Count < sortedPlayers.Count) chips.Add(CreateChip(chips.Count));
        for (int i = 0; i < chips.Count; i++)
        {
            var (chip, nameText, stateText) = chips[i];
            bool used = i < sortedPlayers.Count;
            chip.gameObject.SetActive(used);
            if (!used) continue;

            var p = sortedPlayers[i];
            bool onDeck = OnDeck(p.transform.position, out _);
            chip.characterColor = HudColors.ForCharacter(p.name);
            chip.dim = !onDeck;
            chip.Redraw();
            nameText.text = p.Object.HasInputAuthority ? "You" : HudColors.NameForCharacter(p.name);
            nameText.color = onDeck ? Color.white : new Color(0.7f, 0.7f, 0.75f);
            stateText.text = onDeck ? "" : "OFF";
        }
    }

    // ---- 화면 밖 위험 ----

    private readonly List<(Vector3 position, BalanceDiscGraphic.MarkerKind kind)> threats = new();

    private void UpdateEdgeArrows(Camera cam)
    {
        threats.Clear();
        foreach (var b in bombs)
            if (Alive(b) && OnDeck(b.transform.position, out _)) threats.Add((b.transform.position, BalanceDiscGraphic.MarkerKind.Bomb));
        foreach (var m in monsters)
            if (Alive(m) && OnDeck(m.transform.position, out _)) threats.Add((m.transform.position, BalanceDiscGraphic.MarkerKind.Monster));

        Vector2 half = root.rect.size * 0.5f - new Vector2(edgeInset, edgeInset);
        int used = 0;
        foreach (var (position, kind) in threats)
        {
            Vector3 vp = cam.WorldToViewportPoint(position);
            bool onScreen = vp.z > 0f && vp.x > 0.03f && vp.x < 0.97f && vp.y > 0.03f && vp.y < 0.97f;
            if (onScreen) continue;

            // 화면 가운데에서 대상 쪽 방향(카메라 뒤에 있으면 뒤집음)을 캔버스 단위로 구해 가장자리에 붙임
            Vector2 dir = new Vector2((vp.x - 0.5f) * root.rect.width, (vp.y - 0.5f) * root.rect.height);
            if (vp.z < 0f) dir = -dir;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.down;
            float k = Mathf.Min(half.x / Mathf.Max(1e-4f, Mathf.Abs(dir.x)), half.y / Mathf.Max(1e-4f, Mathf.Abs(dir.y)));

            if (used >= edgeArrows.Count) edgeArrows.Add(Create<EdgeArrowGraphic>("Edge Arrow", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90f, 90f)));
            var arrow = edgeArrows[used++];
            arrow.gameObject.SetActive(true);
            arrow.rectTransform.anchoredPosition = dir * k;
            arrow.angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            arrow.kind = kind;
            arrow.Redraw();
        }
        for (int i = used; i < edgeArrows.Count; i++) edgeArrows[i].gameObject.SetActive(false);
    }

    // ---- 만들기 ----

    private void BuildCanvas()
    {
        var go = new GameObject("Game HUD", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root = go.GetComponent<RectTransform>();

        disc = Create<BalanceDiscGraphic>("Balance Disc", Vector2.zero, Vector2.zero, new Vector2(margin, margin), new Vector2(discSize, discSize));

        // 새 좌하단: 방향 원(왼쪽 위) + 속도 막대와 숫자(아래) + 수평계(오른쪽)
        Vector2 dialCenter = new Vector2(margin + 174f, margin + 189f);
        travelDial = Create<TravelDialGraphic>("Travel Dial", Vector2.zero, new Vector2(0.5f, 0.5f), dialCenter, new Vector2(200f, 200f));
        speedBar = Create<SpeedBarGraphic>("Speed Bar", Vector2.zero, new Vector2(0.5f, 0.5f), dialCenter + new Vector2(0f, -132f), new Vector2(210f, 22f));
        speedText = CreateText("Speed", root, Vector2.zero, new Vector2(0.5f, 0.5f), dialCenter + new Vector2(0f, -170f), new Vector2(220f, 40f), 28f, TextAlignmentOptions.Center);
        level = Create<LevelGraphic>("Tilt Level", Vector2.zero, new Vector2(0.5f, 0.5f), dialCenter + new Vector2(168f, 0f), new Vector2(130f, 130f));
        progressBar = Create<ProgressBarGraphic>("Course Progress", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -topMargin), new Vector2(640f, 46f));
        compass = Create<CompassGraphic>("Next Target", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -topMargin - 64f), new Vector2(86f, 86f));
        distanceText = CreateText("Next Distance", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -topMargin - 154f), new Vector2(240f, 40f), 30f, TextAlignmentOptions.Center);
    }

    private (ChipGraphic, TextMeshProUGUI, TextMeshProUGUI) CreateChip(int index)
    {
        var chip = Create<ChipGraphic>($"Team {index}", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(margin, -margin - index * 58f), new Vector2(220f, 48f));
        var name = CreateText("Name", chip.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(58f, 0f), new Vector2(120f, 40f), 24f, TextAlignmentOptions.Left);
        var state = CreateText("State", chip.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(60f, 40f), 22f, TextAlignmentOptions.Right);
        state.color = new Color(1f, 0.8f, 0.2f);
        return (chip, name, state);
    }

    private T Create<T>(string objectName, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size) where T : Graphic
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(root, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        var graphic = go.AddComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private static TextMeshProUGUI CreateText(string objectName, RectTransform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.outlineWidth = 0.25f;
        text.outlineColor = HudColors.Navy;
        return text;
    }
}
