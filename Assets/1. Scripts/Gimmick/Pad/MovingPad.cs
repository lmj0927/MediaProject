using Fusion;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 이동 발판. 휠이 발판 위에 올라와 일정 시간 머무르면 발판째로 도착 지점(A)까지 직선으로 이동.
/// 기본설정: 휠이 발판 위에서 자유롭게 굴러다님. 판 기울기는 그대로 동작.
///
/// 탑승 판정: 휠 중심이 발판 안쪽(boardMargin만큼 들어간 영역)에 있고, 휠 바닥이 발판 바닥에 닿아 있어야 함.
/// 휠이 거의 멈춰 있어야 하며(boardMaxSpeed), 이 상태가 boardTime 동안 끊기지 않아야 출발함.
///
/// 이동 중 휠이 떨어지면(returnOnFall) 그 자리에서 시작점으로 되돌아감.
/// 튕겨서 잠깐 뜨는 경우를 무시하도록 fallGraceTime 동안 계속 벗어나 있어야 떨어진 것으로 봄.
///
/// 도착 후 기본은 그 자리에 머무름.
/// returnWhenEmpty: 휠이 내리면 시작점으로 복귀함.
/// pingPong: 휠과 무관하게 두 지점 사이를 계속 왕복함. 이때 탑승 조건과 복귀 설정은 무시됨.
///
/// 구성
///   루트(이 오브젝트): NetworkObject + 이 스크립트. 움직이지 않음.
///   Pad(자식): Kinematic Rigidbody + 감지용 BoxCollider(트리거) + NetworkTransform. 실제로 움직임.
///     감지 상자의 바닥면을 발판 바닥 높이로 봄. 밟는 면과 메시는 Pad의 자식으로 둠.
///   Destination(자식): 도착 지점. 빈 오브젝트.
/// 경로는 루트 기준 로컬 좌표로 기억하므로, 루트를 옮기거나 돌리면 경로 전체가 따라감.
///
/// </summary>
[DefaultExecutionOrder(-60)]
[RequireComponent(typeof(NetworkObject))]
public class MovingPad : NetworkBehaviour, IAfterTick
{
    public enum Phase { AtStart, ToEnd, AtEnd, ToStart }

    [Header("References")]
    [Tooltip("실제로 움직이는 발판. Kinematic Rigidbody가 있어야 함.")]
    [SerializeField] private Rigidbody pad;

    [Tooltip("휠 감지 영역. 비우면 pad의 BoxCollider를 씀. 트리거로 둘 것.\n" +
             "상자의 바닥면을 발판 바닥 높이로 봄.")]
    [SerializeField] private BoxCollider detection;

    [Tooltip("도착 지점 A. 루트의 자식으로 둘 것.")]
    [SerializeField] private Transform destination;

    [Tooltip("메시만 담은 pad의 자식(콜라이더 없음). Host에서 틱 사이 보간.")]
    [SerializeField] private Transform visualRoot;

    [Header("Motion")]
    [Tooltip("평균 이동 속도(m/s). easeInOut 활성화시 중간 구간은 이보다 빠름.")]
    [Min(0.1f)][SerializeField] private float speed = 3f;

    [Tooltip("출발과 도착을 부드럽게 함. 판 위 사람과 화물이 덜 쏠림.")]
    [SerializeField] private bool easeInOut = true;

    [Header("Boarding")]
    [Tooltip("휠이 발판 위에 이 시간(초) 동안 끊기지 않고 머물러야 출발함.")]
    [Min(0f)][SerializeField] private float boardTime = 1f;

    [Tooltip("휠 중심이 발판 가장자리에서 이 거리(m) 이상 안쪽에 있어야 올라온 것으로 봄.\n" +
             "0이면 중심이 발판 위에만 있으면 됨. 클수록 가운데까지 들어와야 함.")]
    [Min(0f)][SerializeField] private float boardMargin = 1f;

    [Tooltip("휠 바닥과 발판 바닥 사이 허용 간격(m). 이보다 떠 있으면 올라온 것으로 보지 않음.")]
    [Min(0f)][SerializeField] private float groundTolerance = 0.3f;

    [Tooltip("휠의 수평 속도가 이 값(m/s) 이하여야 올라온 것으로 봄. 굴러오던 기세가 남은 채 출발하는 것을 막음.\n" +
             "0이면 속도를 보지 않음.")]
    [Min(0f)][SerializeField] private float boardMaxSpeed = 1f;

    [Header("Ride")]
    [Tooltip("이동 중 휠이 판 기울기로 굴러가는 것을 억제함. 무게 조종은 약하게 남음.\n" +
             "비활성화시 이동 중에도 평소처럼 굴러가며, 판 중앙에 서지 않으면 발판 밖으로 밀려날 수 있음.")]
    [SerializeField] private bool holdWhileMoving = true;

    [Tooltip("억제 세기(1/s). 클수록 덜 굴러감.\n" +
             "기울기로 굴러가는 속도가 대략 torqueGain / (torqueGain + 이 값) 배로 줄어듦.\n" +
             "torqueGain 1 기준 30이면 약 1/30. 0이면 억제하지 않음.\n" +
             "높이면 폭탄 같은 충격으로 밀리는 거리도 함께 줄어듦.")]
    [Min(0f)][SerializeField] private float holdStrength = 30f;

    [Header("Fall")]
    [Tooltip("이동 중 휠이 발판에서 떨어지면 그 자리에서 시작점으로 되돌아감.\n" +
             "비활성화시 휠이 없어도 도착 지점까지 계속 감.")]
    [SerializeField] private bool returnOnFall = true;

    [Tooltip("휠이 이 시간(초) 이상 계속 벗어나 있어야 떨어진 것으로 봄. 튕겨서 잠깐 뜨는 경우를 무시함.")]
    [Min(0f)][SerializeField] private float fallGraceTime = 0.3f;

    [Header("Wheel")]
    [Tooltip("이동 중 휠을 밟은 자리에 고정함. 현재는 쓰지 않는 예비 옵션.\n" +
             "활성화시 휠이 물리적으로 떨어질 수 없으므로 returnOnFall이 동작하지 않음.")]
    [SerializeField] private bool lockWheel = false;

    [Header("After Arrival")]
    [Tooltip("도착 후 휠이 내리면 returnDelay 뒤 시작점으로 돌아감. 비활성화시 도착 지점에 머무름.")]
    [SerializeField] private bool returnWhenEmpty = false;

    [Tooltip("휠이 내린 뒤 복귀를 시작하기까지의 시간(초).")]
    [Min(0f)][SerializeField] private float returnDelay = 2f;

    [Tooltip("휠과 무관하게 두 지점 사이를 계속 왕복함. 활성화시 탑승 조건과 복귀 설정은 무시됨.")]
    [SerializeField] private bool pingPong = false;

    [Tooltip("왕복 시 각 끝에서 기다리는 시간(초).")]
    [Min(0f)][SerializeField] private float endWait = 1.5f;

    [Header("Detection")]
    [Tooltip("휠 감지에 쓸 레이어.")]
    [SerializeField] private LayerMask detectionMask = ~0;

    [SerializeField] private float visualSnapDistance = 3f;

    private TickVisualInterpolator visual;
    private readonly Collider[] hits = new Collider[16];

    // 루트 기준 로컬 좌표
    private Vector3 startLocal;
    private Vector3 endLocal;
    private bool routeCached;

    private Vector3 padVelocity;

    private Rigidbody lockedWheel;
    private Vector3 lockedOffset;

    [Networked] private Phase PhaseNet { get; set; }
    [Networked] private float Progress { get; set; }
    [Networked] private TickTimer BoardTimer { get; set; }
    [Networked] private TickTimer WaitTimer { get; set; }
    [Networked] private TickTimer FallTimer { get; set; }

    /// <summary>휠을 태우고 출발한 이동인지. 떨어짐 판정용.</summary>
    [Networked] private NetworkBool Carrying { get; set; }

    /// <summary>현재 단계. 연출이나 UI에 사용.</summary>
    public Phase CurrentPhase => PhaseNet;

    /// <summary>현재 이동 속도(m/s). Host 기준.</summary>
    public Vector3 Velocity => padVelocity;

    private Vector3 StartWorld => transform.TransformPoint(startLocal);
    private Vector3 EndWorld => transform.TransformPoint(endLocal);

    private void Reset()
    {
        pad = GetComponentInChildren<Rigidbody>();
        if (pad != null) detection = pad.GetComponent<BoxCollider>();

        var found = transform.Find("Destination");
        if (found != null) destination = found;
    }

    private void Awake()
    {
        if (pad == null || destination == null)
        {
            Debug.LogError($"{name}: pad 또는 destination이 비어 있습니다.", this);
            enabled = false;
            return;
        }

        pad.isKinematic = true;
        pad.interpolation = RigidbodyInterpolation.None;

        if (detection == null) detection = pad.GetComponent<BoxCollider>();
        if (detection != null) detection.isTrigger = true;

        CacheRoute();
        visual = new TickVisualInterpolator(pad.transform, visualRoot);
    }

    /// <summary>시작 위치와 도착 위치를 루트 기준으로 기억.</summary>
    private void CacheRoute()
    {
        if (routeCached) return;

        startLocal = transform.InverseTransformPoint(pad.position);
        endLocal = transform.InverseTransformPoint(destination.position);
        routeCached = true;
    }

    public override void Spawned()
    {
        if (!Object.HasStateAuthority) return;

        PhaseNet = Phase.AtStart;
        Progress = 0f;
        Carrying = false;

        if (pingPong)
            WaitTimer = TickTimer.CreateFromSeconds(Runner, endWait);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ReleaseWheel();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        float dt = Runner.DeltaTime;
        var wheel = FindWheel();
        bool onBoard = wheel != null && IsOnBoard(wheel, boardMargin);
        bool onPad = wheel != null && IsOnBoard(wheel, 0f);

        switch (PhaseNet)
        {
            case Phase.AtStart:
                if (pingPong) WaitThenDepart(Phase.ToEnd, wheel);
                else HandleBoarding(wheel, onBoard);
                break;

            case Phase.AtEnd:
                if (pingPong) WaitThenDepart(Phase.ToStart, wheel);
                else if (returnWhenEmpty) HandleReturn(onBoard);
                break;

            case Phase.ToEnd:
                HandleFall(onPad);
                Advance(dt);
                break;

            case Phase.ToStart:
                Advance(dt);
                break;
        }

        MovePad(dt, wheel, onPad);

        if (onPad && (PhaseNet == Phase.ToEnd || PhaseNet == Phase.ToStart))
            HoldWheel(wheel, dt);
    }

    void IAfterTick.AfterTick()
    {
        if (Object.HasStateAuthority)
            visual.Capture(pad.position, pad.rotation, visualSnapDistance);
    }

    private void LateUpdate()
    {
        if (visual == null) return;

        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            visual.Apply(Runner.LocalAlpha);
        else
            visual.RestoreLocal();
    }

    // ---- 단계 진행 ----

    /// <summary>휠이 발판 위에 boardTime 동안 끊기지 않고 머물면 출발함.</summary>
    private void HandleBoarding(Rigidbody wheel, bool onBoard)
    {
        if (!onBoard || !IsSettled(wheel))
        {
            BoardTimer = TickTimer.None;
            return;
        }

        if (!BoardTimer.IsRunning)
            BoardTimer = TickTimer.CreateFromSeconds(Runner, boardTime);

        if (BoardTimer.Expired(Runner))
        {
            BoardTimer = TickTimer.None;
            Carrying = true;
            Depart(Phase.ToEnd, wheel);
        }
    }

    /// <summary>
    /// 휠을 태우고 가는 중 휠이 fallGraceTime 이상 벗어나 있으면 그 자리에서 시작점으로 되돌아감.
    /// </summary>
    private void HandleFall(bool onPad)
    {
        if (!returnOnFall || !Carrying || lockedWheel != null) return;

        if (onPad)
        {
            FallTimer = TickTimer.None;
            return;
        }

        if (!FallTimer.IsRunning)
            FallTimer = TickTimer.CreateFromSeconds(Runner, fallGraceTime);

        if (FallTimer.Expired(Runner))
        {
            FallTimer = TickTimer.None;
            Carrying = false;
            PhaseNet = Phase.ToStart;
        }
    }

    /// <summary>도착 후 휠이 내리고 일정 시간이 지나면 복귀함. 다시 올라타면 대기를 취소함.</summary>
    private void HandleReturn(bool onBoard)
    {
        if (onBoard)
        {
            WaitTimer = TickTimer.None;
            return;
        }

        if (!WaitTimer.IsRunning)
            WaitTimer = TickTimer.CreateFromSeconds(Runner, returnDelay);

        if (WaitTimer.Expired(Runner))
        {
            WaitTimer = TickTimer.None;
            Depart(Phase.ToStart, null);
        }
    }

    /// <summary>왕복 모드. 끝에서 기다린 뒤 반대쪽으로 출발함.</summary>
    private void WaitThenDepart(Phase next, Rigidbody wheel)
    {
        if (!WaitTimer.IsRunning)
            WaitTimer = TickTimer.CreateFromSeconds(Runner, endWait);

        if (WaitTimer.Expired(Runner))
        {
            WaitTimer = TickTimer.None;
            Depart(next, wheel);
        }
    }

    private void Depart(Phase phase, Rigidbody wheel)
    {
        if (lockWheel && wheel != null)
            LockWheel(wheel);

        FallTimer = TickTimer.None;
        PhaseNet = phase;
    }

    /// <summary>진행도를 올리고 끝에 닿으면 도착 처리함.</summary>
    private void Advance(float dt)
    {
        float distance = Vector3.Distance(StartWorld, EndWorld);
        float step = distance > 0.001f ? speed * dt / distance : 1f;

        if (PhaseNet == Phase.ToEnd)
        {
            Progress = Mathf.Min(1f, Progress + step);
            if (Progress >= 1f) Arrive(Phase.AtEnd);
        }
        else
        {
            Progress = Mathf.Max(0f, Progress - step);
            if (Progress <= 0f) Arrive(Phase.AtStart);
        }
    }

    private void Arrive(Phase phase)
    {
        PhaseNet = phase;
        Carrying = false;
        FallTimer = TickTimer.None;
        ReleaseWheel();

        if (pingPong)
            WaitTimer = TickTimer.CreateFromSeconds(Runner, endWait);
    }

    // ---- 이동 ----

    private Vector3 PositionAt(float progress)
    {
        float t = easeInOut ? progress * progress * (3f - 2f * progress) : progress;
        return Vector3.Lerp(StartWorld, EndWorld, t);
    }

    /// <summary>
    /// 발판과 그 위의 휠을 같은 이동량만큼 위치로 옮김.
    /// 휠은 발판 위에 가만히 놓인 것과 똑같이 시뮬되어 마찰, 구름, 기울기 조종이 평소대로 동작함.
    /// </summary>
    private void MovePad(float dt, Rigidbody wheel, bool onPad)
    {
        Vector3 target = PositionAt(Progress);
        Vector3 delta = target - pad.position;

        padVelocity = dt > 0f ? delta / dt : Vector3.zero;

        pad.position = target;

        if (lockedWheel != null)
        {
            lockedWheel.MovePosition(target + lockedOffset);
            return;
        }

        if (onPad && wheel != null && !wheel.isKinematic && delta.sqrMagnitude > 1e-10f)
            wheel.position += delta;
    }

    /// <summary>
    /// 이동 중 휠의 구름을 억제함. 수평 속도와 수평축 회전을 매 틱 일정 비율로 줄임.
    /// </summary>
    private void HoldWheel(Rigidbody wheel, float dt)
    {
        if (!holdWhileMoving || holdStrength <= 0f) return;
        if (wheel == null || wheel == lockedWheel || wheel.isKinematic) return;

        float keep = Mathf.Exp(-holdStrength * dt);

        // 발판 이동은 위치로 따로 옮기므로 휠의 속도는 그대로 발판 기준 속도
        Vector3 v = wheel.linearVelocity;
        wheel.linearVelocity = new Vector3(v.x * keep, v.y, v.z * keep);

        Vector3 w = wheel.angularVelocity;
        Vector3 yaw = Vector3.Project(w, Vector3.up);
        wheel.angularVelocity = yaw + (w - yaw) * keep;
    }

    /// <summary>휠이 발판 기준으로 거의 멈춰 있는지. boardMaxSpeed가 0이면 항상 참.</summary>
    private bool IsSettled(Rigidbody wheel)
    {
        if (boardMaxSpeed <= 0f || wheel == null) return true;

        Vector3 v = wheel.linearVelocity;
        v.y = 0f;
        return v.sqrMagnitude <= boardMaxSpeed * boardMaxSpeed;
    }

    // ---- 휠 고정 (예비) ----

    /// <summary>휠을 밟은 자리에 고정함. 이동 동안 물리 대신 발판을 따라 옮겨짐.</summary>
    private void LockWheel(Rigidbody wheel)
    {
        if (lockedWheel == wheel) return;
        ReleaseWheel();

        lockedWheel = wheel;
        lockedOffset = wheel.position - pad.position;

        wheel.linearVelocity = Vector3.zero;
        wheel.angularVelocity = Vector3.zero;
        wheel.isKinematic = true;
    }

    /// <summary>고정을 풀고 발판의 현재 속도를 물려줌. 도착 지점에서는 거의 0.</summary>
    private void ReleaseWheel()
    {
        if (lockedWheel == null) return;

        lockedWheel.isKinematic = false;
        lockedWheel.linearVelocity = padVelocity;
        lockedWheel.angularVelocity = Vector3.zero;
        lockedWheel = null;
    }

    // ---- 감지 ----

    /// <summary>감지 상자에 닿은 휠을 찾음. 없으면 null. 올라왔는지는 IsOnBoard로 따로 판정함.</summary>
    private Rigidbody FindWheel()
    {
        if (lockedWheel != null) return lockedWheel;
        if (detection == null) return null;

        GetDetectionBox(detection, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);

        int count = Physics.OverlapBoxNonAlloc(
            center, halfExtents, hits, rotation, detectionMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            var rb = hits[i].attachedRigidbody;
            if (rb == null || rb == pad) continue;
            if (rb.TryGetComponent<SphereDriver>(out _)) return rb;
        }
        return null;
    }

    /// <summary>
    /// 휠이 발판 위에 올라와 있는지.
    /// 중심이 가장자리에서 margin 이상 안쪽이고, 바닥이 발판 바닥에서 groundTolerance 이내여야 함.
    /// </summary>
    private bool IsOnBoard(Rigidbody wheel, float margin)
    {
        if (wheel == lockedWheel) return true;
        if (detection == null) return false;

        var t = detection.transform;
        Vector3 s = t.lossyScale;
        Vector3 scale = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

        Vector3 local = Vector3.Scale(t.InverseTransformPoint(wheel.position) - detection.center, scale);
        Vector3 half = Vector3.Scale(detection.size, scale) * 0.5f;

        float limitX = Mathf.Max(0f, half.x - margin);
        float limitZ = Mathf.Max(0f, half.z - margin);
        if (Mathf.Abs(local.x) > limitX || Mathf.Abs(local.z) > limitZ) return false;

        // 상자의 바닥면 = 발판 바닥
        float bottom = local.y - WheelRadius(wheel);
        float gap = bottom - (-half.y);
        return gap <= groundTolerance && gap >= -groundTolerance - 0.5f;
    }

    private static float WheelRadius(Rigidbody wheel)
    {
        if (!wheel.TryGetComponent<SphereCollider>(out var sphere)) return 0f;

        Vector3 s = wheel.transform.lossyScale;
        return sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    private static void GetDetectionBox(BoxCollider box, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        var t = box.transform;
        Vector3 s = t.lossyScale;
        center = t.TransformPoint(box.center);
        halfExtents = Vector3.Scale(box.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z))) * 0.5f;
        rotation = t.rotation;
    }

#if UNITY_EDITOR
    private static readonly Color RouteColor = new Color(0.3f, 1f, 0.5f, 0.95f);
    private static readonly Color GhostColor = new Color(0.3f, 1f, 0.5f, 0.35f);
    private static readonly Color BoardColor = new Color(1f, 0.85f, 0.2f, 0.9f);

    private void OnDrawGizmos()
    {
        if (pad == null || destination == null) return;

        // 플레이 중에는 기억한 경로
        Vector3 start = Application.isPlaying && routeCached ? StartWorld : pad.position;
        Vector3 end = Application.isPlaying && routeCached ? EndWorld : destination.position;
        Vector3 dir = end - start;
        float distance = dir.magnitude;

        // 경로와 진행 방향
        Gizmos.color = RouteColor;
        Gizmos.DrawLine(start, end);

        if (distance > 0.01f)
        {
            Vector3 n = dir / distance;
            Vector3 up = Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 a = Vector3.Cross(n, up).normalized * 0.45f;
            Vector3 b = Vector3.Cross(n, a).normalized * 0.45f;

            // 중간과 끝에 화살촉
            foreach (float k in new[] { 0.5f, 1f })
            {
                Vector3 tip = start + dir * k;
                Gizmos.DrawLine(tip, tip - n * 0.8f + a);
                Gizmos.DrawLine(tip, tip - n * 0.8f - a);
                Gizmos.DrawLine(tip, tip - n * 0.8f + b);
                Gizmos.DrawLine(tip, tip - n * 0.8f - b);
            }
        }

        // 시작과 도착 지점의 발판 윤곽, 탑승 영역
        var box = detection != null ? detection : pad.GetComponent<BoxCollider>();
        if (box != null)
        {
            DrawFootprint(box, start - pad.position, Application.isPlaying ? GhostColor : RouteColor, 0f);
            DrawFootprint(box, end - pad.position, RouteColor, 0f);
            DrawFootprint(box, start - pad.position, BoardColor, boardMargin);
        }

        Handles.color = RouteColor;
        float seconds = distance / Mathf.Max(0.1f, speed);
        Handles.Label(end + Vector3.up * 0.6f, $"A  {distance:F1}m · {seconds:F1}s");
        Handles.Label(start + Vector3.up * 0.6f, $"{ModeLabel} · {ArrivalLabel}");
    }

    private string ModeLabel =>
        lockWheel ? "휠 고정" :
        returnOnFall ? $"탑승 {boardTime:F1}s · 떨어지면 복귀" :
        $"탑승 {boardTime:F1}s";

    private string ArrivalLabel =>
        pingPong ? $"왕복 (끝에서 {endWait:F1}s)" :
        returnWhenEmpty ? $"비면 {returnDelay:F1}s 뒤 복귀" :
        "도착 후 머묾";

    /// <summary>감지 영역의 바닥 윤곽을 offset만큼 옮긴 자리에 그림. inset만큼 안쪽으로 줄여 그릴 수 있음.</summary>
    private static void DrawFootprint(BoxCollider box, Vector3 offset, Color color, float inset)
    {
        var t = box.transform;
        Vector3 s = t.lossyScale;
        Vector3 c = box.center;
        float bottom = c.y - box.size.y * 0.5f;

        // inset은 미터 단위이므로 로컬 크기로 환산함
        float sx = Mathf.Max(0f, box.size.x - 2f * inset / Mathf.Max(1e-4f, Mathf.Abs(s.x)));
        float sz = Mathf.Max(0f, box.size.z - 2f * inset / Mathf.Max(1e-4f, Mathf.Abs(s.z)));

        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.Translate(offset) * t.localToWorldMatrix;
        Gizmos.color = color;
        Gizmos.DrawWireCube(new Vector3(c.x, bottom, c.z), new Vector3(sx, 0.02f, sz));
        Gizmos.matrix = old;
    }
#endif
}
