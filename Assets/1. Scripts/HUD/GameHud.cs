using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 HUD. 실행하면 화면 캔버스를 만들고 매 프레임 판·휠·플레이어 상태를 읽어 그림.
/// 각 피어가 자기 화면에서만 그림(네트워크로 보내는 것 없음).
///   왼쪽 아래: 균형 원판(기울기, 진행 방향, 판 위 플레이어·몬스터·폭탄)
///   가운데 위: 코스 진행도 + 길 방향 나침반·다음 체크포인트까지 거리 (씬에 CourseRoute가 있을 때만)
///   왼쪽 위: 팀원 상태(판 밖에 있으면 회색 + OFF)
///   화면 가장자리: 판 위에 있지만 화면 밖에 있는 폭탄·몬스터 쪽을 가리킴
/// 원판과 나침반은 카메라가 보는 방향을 화면 위쪽으로 삼음.
/// 씬에 넣기만 하면 됨. 코스와 카메라는 비워 두면 씬에서 알아서 찾음(코스: CourseRoute, 카메라: 메인 카메라).
/// </summary>
[DefaultExecutionOrder(1100)]
public class GameHud : MonoBehaviour
{
    [Header("Sources")]
    [Tooltip("진행도·나침반용 코스. 비우면 씬에서 찾고, 씬에도 없으면 진행바·나침반·거리를 숨김.")]
    [SerializeField] private CourseRoute course;

    [Tooltip("화면 기준 카메라. 비우면 메인 카메라를 씀.")]
    [SerializeField] private Camera viewCamera;

    [Tooltip("나침반이 가리키는 곳: 경로를 따라 지금 위치에서 이만큼(m) 앞.")]
    [SerializeField] private float compassLookAhead = 15f;

    [Tooltip("휠 최고 속도(m/s). 진행 방향 화살표 길이의 기준.")]
    [SerializeField] private float topSpeed = 11.8f;

    [Tooltip("판 높이에서 위아래로 이 거리(m) 안에 있어야 판 위로 봄.")]
    [SerializeField] private float deckHeightRange = 4f;

    [Header("Smoothing")]
    [Tooltip("기울기 표시가 실제 기울기를 따라가는 빠르기(1/초). 클수록 바로 따라감.")]
    [SerializeField] private float leanFollow = 12f;

    [Tooltip("진행 방향 화살표에 쓰는 휠 속도를 평균 내는 시간(초).")]
    [SerializeField] private float travelSmoothTime = 0.15f;

    [Header("Layout (1920x1080 기준)")]
    [SerializeField] private float discSize = 330f;
    [SerializeField] private float margin = 36f;
    [SerializeField] private float edgeInset = 70f;

    private RectTransform root;
    private BalanceDiscGraphic disc;
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
    private Vector3 lastWheelPosition;
    private bool hasLastWheel;
    private Vector3 wheelVelocity;
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
        UpdateDisc(Time.deltaTime);
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

    private void UpdateWheelVelocity(float dt)
    {
        if (dt <= 0f) return;
        Vector3 position = wheel.position;
        if (hasLastWheel)
        {
            Vector3 velocity = (position - lastWheelPosition) / dt;
            // 리셋 등으로 순간이동하면 버림
            if (velocity.sqrMagnitude < 60f * 60f)
                wheelVelocity = Vector3.Lerp(wheelVelocity, velocity, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, travelSmoothTime)));
        }
        lastWheelPosition = position;
        hasLastWheel = true;
    }

    // ---- 균형 원판 ----

    private void UpdateDisc(float dt)
    {
        Vector2 targetLean = ToView(platform.DownhillDirection) * platform.NormalizedTilt;
        shownLean = Vector2.Lerp(shownLean, targetLean, 1f - Mathf.Exp(-leanFollow * dt));
        disc.lean = shownLean;

        Vector3 flat = wheelVelocity;
        flat.y = 0f;
        disc.travel = ToView(flat) / Mathf.Max(0.1f, topSpeed);

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
        compass.gameObject.SetActive(hasCourse);
        distanceText.gameObject.SetActive(hasCourse);
        if (!hasCourse) return;

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
        progressBar = Create<ProgressBarGraphic>("Course Progress", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -margin), new Vector2(640f, 46f));
        compass = Create<CompassGraphic>("Next Target", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -margin - 64f), new Vector2(86f, 86f));
        distanceText = CreateText("Next Distance", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -margin - 154f), new Vector2(240f, 40f), 30f, TextAlignmentOptions.Center);
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
