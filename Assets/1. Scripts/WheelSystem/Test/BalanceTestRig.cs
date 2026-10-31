using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Fusion;
using UnityEngine;

/// <summary>
/// 테스트용. 혼자서 여러 명이 탄 상황을 재현하고 휠 상태를 기록함.
///
/// 더미: 실제 캐릭터 프리팹을 입력 없이 스폰한 것. 이동, 점프, 무게, 판 위 보정이 실제 플레이어와 같음.
///   입력 대신 이 스크립트가 틱마다 캐릭터의 이동 함수(NetworkCharacterController.Move)를 직접 호출함.
///   위치는 판 중심 기준 수평 좌표(월드 XZ 오프셋)로 지정함. 판이 움직여도 목표는 판을 따라감.
/// 전략 주행: 사각 코스를 만들고 더미 4명이 팀처럼 휠을 몰고 가게 함.
///   남은 거리로 원하는 속도를 정하고, 실제 속도와의 차이 방향으로 몰려가 가속·제동·방향 전환을 함.
///   각자 반응 시간과 위치 오차가 있어 사람이 하는 것처럼 조금씩 늦고 어긋남.
///   트롤 옵션: 1명이 반대편·옆쪽 가장자리로 다니며 방해함. 팀은 트롤을 모르고 평소처럼 판단함.
///   트롤은 반대편에 섰을 때 내려찍기를 반복함(trollStamps).
/// 내려찍기: 점프 → 정점에서 아래로 내리꽂기 → 착지 후 무게 배율. 수치는 캐릭터 프리팹의 Player 값을 그대로 씀.
/// 기록: Host에서 매 틱(물리 스텝 후) 휠 속도, 기울기, 무게중심 등을 CSV로 저장함.
///   저장 위치는 프로젝트의 Logs/BalanceTest 폴더. git에서 제외되는 폴더임.
///
/// 구성: 씬에 빈 오브젝트 하나를 두고 NetworkObject + 이 스크립트를 붙임.
/// 시나리오는 에디터에서 Instance의 메서드를 호출해 실행함. Host 세션에서만 동작함.
///
/// 실행 순서: 플레이어(50) 뒤, 물리 스텝(HostPhysicsStepper 1000) 전.
///   더미 이동은 실제 플레이어와 같은 시점에 해야 판 위 carry 기준점이 맞음.
///   물리 스텝 뒤에 옮기면 기준점이 이미 움직인 판에 맞춰져 매 틱 판의 이동만큼 뒤처짐.
/// </summary>
[DefaultExecutionOrder(60)]
[RequireComponent(typeof(NetworkObject))]
public class BalanceTestRig : NetworkBehaviour, IAfterTick
{
    public static BalanceTestRig Instance { get; private set; }

    [Header("References")]
    [Tooltip("대상 판. 비우면 씬에서 찾음.")]
    [SerializeField] private PlatformTilt platform;

    [Tooltip("대상 휠. 비우면 씬에서 찾음.")]
    [SerializeField] private SphereDriver wheel;

    [Header("Dummy")]
    [Tooltip("더미로 쓸 캐릭터 프리팹. 순서대로 돌아가며 씀. 로컬 플레이어와 다른 색을 넣을 것.")]
    [SerializeField] private NetworkObject[] dummyPrefabs;

    [Tooltip("목표 지점에 이 거리(m)까지 다가가면 멈춤.")]
    [Min(0.05f)][SerializeField] private float arriveDistance = 0.3f;

    [Header("Strategy")]
    [Tooltip("사각 코스 한 변의 길이(m).")]
    [Min(10f)][SerializeField] private float courseSize = 60f;

    [Tooltip("팀이 유지하려는 순항 속도(m/s).")]
    [Min(1f)][SerializeField] private float cruiseSpeed = 10f;

    [Tooltip("모퉁이(중간 웨이포인트)를 지날 때 허용하는 속도(m/s).")]
    [Min(0f)][SerializeField] private float cornerSpeed = 4f;

    [Tooltip("팀이 예상하는 제동 감속(m/s²). 남은 거리와 이 값으로 언제 제동할지 정함.\n" +
             "실제 휠보다 크게 잡으면 늦게 제동해 지나치고, 작게 잡으면 일찍 멈춤.")]
    [Min(0.1f)][SerializeField] private float expectedBrake = 2f;

    [Tooltip("원하는 속도와 실제 속도의 차이가 이 값(m/s)이면 판 가장자리까지 몰려감.")]
    [Min(0.5f)][SerializeField] private float fullLeanError = 8f;

    [Tooltip("판 가장자리까지 몰렸을 때 휠이 내는 최고 속도(m/s). 원하는 속도에 필요한 쏠림을 미리 계산하는 데 씀.")]
    [Min(1f)][SerializeField] private float fullLeanSpeed = 11.8f;

    [Tooltip("원하는 속도에 필요한 쏠림을 미리 더하는 비율. 0이면 차이만 보고 몰림.\n" +
             "차이만 보면 속도가 오를수록 덜 쏠려서 순항 속도에 못 미친 채 멈춰 있음.")]
    [Range(0f, 1f)][SerializeField] private float leanFeedForward = 1f;

    [Tooltip("원하는 속도가 이 값(m/s) 이하이면 미리 더하지 않음. 순항 속도에서 최대가 됨.")]
    [Min(0f)][SerializeField] private float feedForwardFrom = 5f;

    [Tooltip("가는 방향과 목표 방향이 어긋났을 때 낮추는 원하는 속도(m/s). 먼저 속도를 줄인 뒤 꺾음.\n" +
             "빙판처럼 옆으로 잘 꺾이지 않는 곳에서 속도를 유지한 채 꺾으려다 목표 주위를 맴도는 것을 막음.")]
    [Min(0f)][SerializeField] private float turnSpeed = 2f;

    [Tooltip("가는 방향과 목표 방향 사이 각의 코사인이 이 값 이상이면 속도를 줄이지 않음. 이 값과 turnSpeedFullAt 사이에서 서서히 줄임.")]
    [Range(-1f, 1f)][SerializeField] private float turnSpeedFreeAt = 0.9f;

    [Tooltip("코사인이 이 값 이하이면 turnSpeed까지 줄임.")]
    [Range(-1f, 1f)][SerializeField] private float turnSpeedFullAt = 0.3f;

    [Tooltip("경로선에서 벗어난 거리 1m당 되돌아가려는 옆 속도(m/s). 좁은 길에서 밀려도 중심선을 지키게 함.")]
    [Min(0f)][SerializeField] private float lineHold = 0.4f;

    [Tooltip("웨이포인트 도착 판정 반경(m).")]
    [Min(0.5f)][SerializeField] private float waypointRadius = 5f;

    [Tooltip("반응 시간 범위(초). 더미마다 매번 이 범위에서 무작위로 정함.")]
    [SerializeField] private Vector2 reactionTime = new Vector2(0.25f, 0.6f);

    [Tooltip("서는 위치 오차(m). 사람이 정확한 자리에 서지 못하는 정도.")]
    [Min(0f)][SerializeField] private float aimError = 0.5f;

    [Tooltip("나란히 설 때의 간격(m).")]
    [Min(0.5f)][SerializeField] private float spacing = 1.6f;

    [Header("Troll")]
    [Tooltip("트롤이 작전을 바꾸는 간격 범위(초).")]
    [SerializeField] private Vector2 trollTacticTime = new Vector2(3f, 6f);

    [Tooltip("트롤의 반응 시간 범위(초). 방해에 집중하므로 팀보다 약간 빠름.")]
    [SerializeField] private Vector2 trollReactionTime = new Vector2(0.2f, 0.4f);

    [Tooltip("트롤이 반대편에 섰을 때 내려찍기를 반복함.")]
    [SerializeField] private bool trollStamps = true;

    [Header("Hammer Gate")]
    [Tooltip("해머 회전축 앞 이 거리(m)에서 멈춰 타이밍을 봄. 사람이 해머를 보고 기다렸다 지나가는 것을 흉내 냄.")]
    [Min(5f)][SerializeField] private float gateHoldDistance = 35f;

    [Tooltip("예측 여유(m). 판·공 크기에 더해 예측이 조금 빗나가도 맞지 않게 함.")]
    [Min(0f)][SerializeField] private float gateMargin = 0.6f;

    [Tooltip("출발 시점을 찾을 때 내다보는 시간(초).")]
    [Min(1f)][SerializeField] private float gateSearchTime = 12f;

    [Header("Bomb")]
    [Tooltip("폭탄 반경에 더해 피하는 여유(m). 팀은 판 위 폭탄에서 이만큼 떨어진 자리를 고름.")]
    [Min(0f)][SerializeField] private float bombAvoidMargin = 1.5f;

    [Header("Gimmick Course")]
    [Tooltip("코스 출발점. 휠을 여기로 옮긴 뒤 출발함.")]
    [SerializeField] private Transform courseStart;

    [Tooltip("출발 곡선을 잴 곳. 비우면 courseStart. 해머 앞 출발이 빙판 위라면 빙판 위에 둬야 타이밍 예측이 맞음.")]
    [SerializeField] private Transform courseCalibrationPoint;

    [Tooltip("웨이포인트 차례. 마지막은 엘리베이터 발판 가운데(여기서 멈춤).")]
    [SerializeField] private Transform[] courseWaypoints;

    [Tooltip("마지막 웨이포인트의 엘리베이터. 도착(AtEnd)하면 결승으로 감. 비우면 바로 결승으로 감.")]
    [SerializeField] private MovingPad courseElevator;

    [Tooltip("결승점.")]
    [SerializeField] private Transform courseFinish;

    [Tooltip("타이밍을 맞춰 지날 해머들.")]
    [SerializeField] private Hammer[] courseHammers;

    [Tooltip("코스 중 판 위에 떨어뜨릴 폭탄 프리팹. 비우면 안 떨어뜨림.")]
    [SerializeField] private NetworkObject courseBombPrefab;

    [Tooltip("휠이 이 지점 반경 courseBombRadius 안에 들어오면 폭탄을 한 번 떨어뜨림.")]
    [SerializeField] private Transform courseBombZone;

    [Min(1f)][SerializeField] private float courseBombRadius = 20f;

    [Header("Hammer Loop")]
    [Tooltip("해머 반복 보기 출발점. 해머 쪽 점프대 앞 지면에 둠.")]
    [SerializeField] private Transform hammerLoopStart;

    [Tooltip("해머 반복 보기 끝점. 해머를 지나 외다리 너머.")]
    [SerializeField] private Transform hammerLoopEnd;

    [Min(1)][SerializeField] private int hammerLoopRounds = 5;

    [Header("Ice Chase")]
    [Tooltip("빙판 추격에 쓸 빙판. 이 빙판 안에서 목표가 무작위로 생김.")]
    [SerializeField] private IcePad iceChasePad;

    [Tooltip("보이는 목표 영역의 반지름(m).")]
    [Min(0.5f)][SerializeField] private float iceChaseTargetRadius = 4f;

    [Tooltip("이 반지름(m) 안에 휠이 들어오면 목표가 다른 곳으로 옮겨 감. 목표 영역보다 넓게 둬서 실제로는 도달하지 못하게 함.")]
    [Min(1f)][SerializeField] private float iceChaseTriggerRadius = 12f;

    [Tooltip("새 목표는 휠에서 이 거리(m) 이상 떨어진 곳에 생김.")]
    [Min(0f)][SerializeField] private float iceChaseMinJump = 40f;

    [Tooltip("빙판 가장자리에서 이만큼(m) 안쪽에만 목표를 둠.")]
    [Min(0f)][SerializeField] private float iceChaseEdgeMargin = 20f;

    [Header("Team Stamp")]
    [Tooltip("팀이 전술적으로 내려찍기를 씀. 가야 할 속도와 실제 속도 차이가 클 때, 기울일 방향 쪽 가장자리의 더미가 찍음.\n" +
             "해머 대기·출발 중에는 쓰지 않음(출발 시점 계산이 내려찍기 없이 잰 값이라 어긋남).")]
    [SerializeField] private bool teamStamps = true;

    [Tooltip("가야 할 속도와 실제 속도의 차이(m/s)가 이 값을 넘을 때만 찍음. 급가속·급제동·급선회 상황.")]
    [Min(0f)][SerializeField] private float teamStampError = 3f;

    [Tooltip("판 중심에서 기울일 방향으로 판 반경의 이 비율 이상 나가 있는 더미만 찍음. 중심 근처에서 찍으면 효과가 없음.")]
    [Range(0f, 1f)][SerializeField] private float teamStampReach = 0.45f;

    [Tooltip("팀 안에서 다음 사람이 찍기까지 최소 간격(초).")]
    [Min(0f)][SerializeField] private float teamStampInterval = 0.8f;

    [Tooltip("같은 더미가 다시 찍기까지 최소 간격(초). 휠 설정의 연타 방지 시간과 맞추면 매번 최대 효과.")]
    [Min(0f)][SerializeField] private float teamStampCooldown = 2.5f;

    [Header("Monster")]
    [Tooltip("판 위 몬스터를 더미가 잡아서 판 밖으로 던짐. 끄면 몬스터를 내버려 둠.")]
    [SerializeField] private bool handleMonsters = true;

    [Tooltip("몬스터가 판에 오른 뒤 이 시간(초)이 지나야 잡으러 감. 그동안은 몬스터 영향을 그대로 받음.")]
    [Min(0f)][SerializeField] private float monsterGrabDelay = 20f;

    [Tooltip("몬스터와 이 거리(m) 안이면 잡기를 시도함. 실제 잡기 반경(1.2m)보다 조금 작게 둠.")]
    [Min(0.1f)][SerializeField] private float monsterGrabReach = 0.9f;

    [Tooltip("잡은 몬스터를 판 반경의 이 비율 지점까지 들고 가서 바깥쪽으로 던짐.")]
    [Range(0.3f, 0.95f)][SerializeField] private float monsterThrowEdge = 0.85f;

    [Tooltip("잡으러 가거나 들고 가는 데 이 시간(초)을 넘기면 포기하고 다시 고름.")]
    [Min(1f)][SerializeField] private float monsterGiveUpTime = 12f;

    [Header("Stamp")]
    [Tooltip("자동 내려찍기 사이의 대기 시간 범위(초). 착지 후 다음 점프까지.")]
    [SerializeField] private Vector2 autoStampDelay = new Vector2(0.15f, 0.4f);

    [Tooltip("자동 내려찍기는 목표 자리에서 이 거리(m) 안에 왔거나 막혀서 멈춰 섰을 때만 함.\n" +
             "이동 중에 찍으면 점프하는 동안 멈춰 서서, 가운데를 지나다 제자리 점프만 반복하게 됨.")]
    [Min(0.1f)][SerializeField] private float autoStampArriveDistance = 1f;

    private class Dummy
    {
        public NetworkObject obj;
        public Player player;
        public NetworkCharacterController cc;
        public CharacterController controller;
        public WheelCarrier carrier;
        public WeightSource source;
        public Vector2 target;
        public bool moving;
        public bool jumpRequested;
        public float nextDecision;

        // 내려찍기: 0 없음, 1 점프 대기, 2 상승 중, 3 공중 정지, 4 내리꽂는 중
        public int stampPhase;
        public float stampPhaseTime;
        public float stampWeightEnd = -1f;
        public float stampRecoverEnd = -1f;
        public float lastTeamStamp = -100f;
        public bool stampHanging;
        public bool autoStamp;
        public float nextAutoStamp;

        public bool IsValid => obj != null && obj.IsValid && cc != null;
    }

    private readonly List<Dummy> dummies = new();
    private readonly RaycastHit[] hits = new RaycastHit[16];

    private Rigidbody wheelBody;
    private Rigidbody platformBody;
    private CenterOfMassSolver solver;

    private StringBuilder log;
    private string logName;
    private float logStartTime;

    private Vector3 wheelStartPosition;
    private Vector3 wheelOriginalPosition;
    private float wheelOriginalY;
    private Quaternion wheelStartRotation;
    private bool resetRequested;

    // 전략 주행 상태
    private readonly List<Vector3> waypoints = new();
    private readonly List<GameObject> markers = new();
    private int waypointIndex;
    private Vector3 routeStart;
    private bool strategyActive;
    private float legStartTime;
    private Vector3 plannedLean;
    private Vector3 plannedDirection;

    // 트롤. 팀은 트롤의 존재를 모르고 평소처럼 판단하며, 결과로 생긴 속도 오차를 더 몰려서 메움.
    private enum TrollTactic { Counter, Sideways, Center }
    private int trollIndex = -1;
    private TrollTactic trollTactic;
    private float trollSide = 1f;
    private float nextTacticTime;
    private readonly StringBuilder strategyReport = new();
    private int stampsThisTick;
    private int stampTotal;
    private readonly StringBuilder courseReport = new();

    // 해머 타이밍 대기
    private enum GatePhase { Off, Approach, Settle, Wait, Go }
    private readonly List<Hammer> gateHammers = new();
    private Hammer gateHammer;
    private GatePhase gatePhase;
    private Vector3 gateDirection;
    private float gatePhaseTime;
    private float gateGoTime;
    private float gateNearTime = -1f;

    // 출발 곡선: 모여 선 자세에서 출발 신호를 준 뒤 틱마다 진행한 거리(m)
    private readonly List<float> goProfile = new();
    private float goProfileStep;
    private int calibrationPhase = -1;
    private float calibrationTime;
    private Vector3 calibrationOrigin;
    private Vector3 calibrationDirection;

    /// <summary>전략 주행 중인지.</summary>
    public bool StrategyActive => strategyActive;

    /// <summary>구간별 소요 시간 등 전략 주행 결과.</summary>
    public string StrategyReport => strategyReport.ToString();

    /// <summary>판의 수평 반경. 시나리오 좌표를 정할 때 사용.</summary>
    public float DeckRadius => platform != null ? platform.PlatformRadius : 0f;

    /// <summary>전략 주행에서 중간 웨이포인트를 지날 때 허용하는 속도(m/s).</summary>
    public float CornerSpeed { get => cornerSpeed; set => cornerSpeed = Mathf.Max(0f, value); }

    /// <summary>출발 곡선을 재 두었는지.</summary>
    public bool HasGoProfile => goProfile.Count > 1;

    /// <summary>출발 곡선을 재는 중인지.</summary>
    public bool Calibrating => calibrationPhase >= 0;

    /// <summary>
    /// 전략 주행에서 타이밍을 맞춰 지날 해머들. 경로 앞에 나타나면 멈췄다가, 맞지 않는 시점에 출발함.
    /// 출발 시점은 출발 곡선(CalibrateGo)과 해머 각도로 미리 계산함.
    /// </summary>
    public void SetHammerGates(params Hammer[] hammers)
    {
        gateHammers.Clear();
        if (hammers != null)
            foreach (var h in hammers) if (h != null) gateHammers.Add(h);
        gatePhase = GatePhase.Off;
        gateHammer = null;
    }

    /// <summary>
    /// 출발 곡선을 잼. 평지에서 판 가운데로 모여 멈춘 뒤 direction 쪽으로 몰려 출발하고, 7초 동안의 진행 거리를 기록함.
    /// 해머 앞에서 기다렸다 출발할 때와 같은 동작이라 그 거리로 해머 통과 시점을 예측함.
    /// </summary>
    public void CalibrateGo(Vector3 direction)
    {
        StopStrategy();
        direction.y = 0f;
        calibrationDirection = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        calibrationPhase = 0;
    }

    /// <summary>
    /// 판 위 (x, z) 오프셋 위에 네트워크 프리팹을 생성함. 폭탄 등.
    /// 들 수 있는 물건은 생성 직후 물리 위치가 프리팹 위치에 남는 문제(HoldableProp)가 있어 여기서만 맞춰 줌.
    /// </summary>
    public NetworkObject SpawnOnDeck(NetworkObject prefab, float x, float z, float height = 2f)
    {
        if (prefab == null || Runner == null || platformBody == null) return null;

        Vector3 position = DeckPoint(x, z) + Vector3.up * height;
        var obj = Runner.Spawn(prefab, position, Quaternion.identity, null);
        if (obj == null) return null;

        if (obj.TryGetComponent<Rigidbody>(out var body))
        {
            body.position = position;
            body.rotation = obj.transform.rotation;
        }
        if (obj.TryGetComponent<WheelRider>(out var rider) && platform != null)
            rider.BindToPlatformAtSpawn(platform);
        return obj;
    }

    /// <summary>기믹 코스를 주행 중인지.</summary>
    public bool CourseRunning { get; private set; }

    /// <summary>기믹 코스 진행 상황과 결과.</summary>
    public string CourseReport => courseReport.ToString();

    /// <summary>
    /// 기믹 코스 한 바퀴: (출발 곡선이 없으면 측정) → 출발점 → 웨이포인트(해머는 타이밍 대기, 폭탄 구역에서 폭탄 투하)
    /// → 엘리베이터 도착 대기 → 결승. 기록 파일은 course_시각.csv.
    /// </summary>
    public void RunCourse(bool withTroll)
    {
        if (CourseRunning || courseStart == null || courseWaypoints == null || courseWaypoints.Length == 0 || courseFinish == null)
        {
            Debug.LogWarning($"{name}: 코스 설정이 비었거나 이미 주행 중입니다.", this);
            return;
        }
        StartCoroutine(CourseRoutine(withTroll));
    }

    /// <summary>전략 주행의 순항 속도(m/s).</summary>
    public float CruiseSpeed { get => cruiseSpeed; set => cruiseSpeed = Mathf.Max(1f, value); }

    /// <summary>트롤이 내려찍기를 쓰는지.</summary>
    public bool TrollStamps { get => trollStamps; set => trollStamps = value; }

    /// <summary>기록 시작 후 더미가 내려찍기로 착지한 횟수.</summary>
    public int StampTotal => stampTotal;

    /// <summary>현재 더미 수.</summary>
    public int DummyCount => dummies.Count;

    /// <summary>기록 중인지.</summary>
    public bool IsRecording => log != null;

    public override void Spawned()
    {
        Instance = this;

        if (platform == null) platform = FindFirstObjectByType<PlatformTilt>();
        if (wheel == null) wheel = FindFirstObjectByType<SphereDriver>();

        if (platform != null)
        {
            platformBody = platform.GetComponent<Rigidbody>();
            solver = platform.GetComponent<CenterOfMassSolver>();
        }
        if (wheel != null)
        {
            wheelBody = wheel.GetComponent<Rigidbody>();
            wheelStartPosition = wheelBody.position;
            wheelStartRotation = wheelBody.rotation;
            wheelOriginalPosition = wheelStartPosition;
            wheelOriginalY = wheelStartPosition.y;
        }
    }

    /// <summary>
    /// 휠을 시작 위치로 되돌리고 멈춤. 다음 틱에 처리함.
    /// 판과 더미는 휠을 따라오지만, 판 위의 실제 플레이어는 따라오지 않으므로 다시 올라가야 함.
    /// </summary>
    public void ResetWheel() => resetRequested = true;

    /// <summary>
    /// 휠을 지정한 수평 위치(높이는 처음 시작 높이)로 옮기고 멈춤. 이후 ResetWheel도 이 위치로 감.
    /// 기믹 레인마다 출발점을 바꿀 때 사용.
    /// </summary>
    public void ResetWheelAt(Vector3 position)
    {
        wheelStartPosition = new Vector3(position.x, wheelOriginalY, position.z);
        resetRequested = true;
    }

    /// <summary>휠 중심을 지정한 위치(높이 포함)로 옮기고 멈춤. 고지대 위 등 시작 높이와 다른 곳에 놓을 때.</summary>
    public void ResetWheelAtHeight(Vector3 center)
    {
        wheelStartPosition = center;
        resetRequested = true;
    }

    /// <summary>휠 출발점을 씬에 놓인 처음 위치로 되돌림.</summary>
    public void ResetWheelToOrigin()
    {
        wheelStartPosition = wheelOriginalPosition;
        resetRequested = true;
    }

    private void ApplyReset()
    {
        resetRequested = false;
        if (wheelBody == null) return;

        wheelBody.position = wheelStartPosition;
        wheelBody.rotation = wheelStartRotation;
        wheelBody.linearVelocity = Vector3.zero;
        wheelBody.angularVelocity = Vector3.zero;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        StopRecording();
        StopStrategy();
        ClearDummies();
        if (Instance == this) Instance = null;
    }

    // ---- 더미 ----

    /// <summary>더미 수를 맞춤. 늘어난 더미는 판 중앙 근처에 생성됨.</summary>
    public void SetDummyCount(int count)
    {
        count = Mathf.Max(0, count);
        while (dummies.Count < count)
        {
            // 판 중앙을 피해 둥글게 흩어 생성함. 한 자리에 겹쳐 생성되면 서로 밀어냄.
            float angle = dummies.Count * Mathf.PI * 0.5f;
            float r = DeckRadius * 0.4f;
            var d = CreateDummy(dummies.Count, Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
            if (d == null) return;
            dummies.Add(d);
        }
        while (dummies.Count > count)
        {
            var d = dummies[dummies.Count - 1];
            if (d.obj != null && d.obj.IsValid) Runner.Despawn(d.obj);
            dummies.RemoveAt(dummies.Count - 1);
        }
    }

    /// <summary>더미를 판 중심 기준 (x, z) 오프셋의 데크 위로 즉시 옮김. 필요하면 더미 수를 늘림.</summary>
    public void PlaceDummy(int index, float x, float z)
    {
        if (index < 0) return;
        if (index >= dummies.Count) SetDummyCount(index);
        if (index == dummies.Count)
        {
            var created = CreateDummy(index, x, z);
            if (created == null) return;
            dummies.Add(created);
        }
        if (index >= dummies.Count || !dummies[index].IsValid) return;

        var d = dummies[index];
        d.target = new Vector2(x, z);
        d.moving = false;

        d.cc.Teleport(DeckPoint(x, z) + Vector3.up * 0.1f);
        d.cc.Velocity = Vector3.zero;
        if (d.carrier != null) d.carrier.ResetAnchor();
    }

    /// <summary>더미를 (x, z)까지 걸어가게 함. 속도는 캐릭터 프리팹의 이동 속도를 따름.</summary>
    public void MoveDummy(int index, float x, float z)
    {
        if (index < 0) return;
        if (index >= dummies.Count) SetDummyCount(index + 1);
        if (index >= dummies.Count) return;

        var d = dummies[index];
        d.target = new Vector2(x, z);
        d.moving = true;
    }

    /// <summary>더미를 점프시킴. 착지 상태일 때 다음 틱에 뜀.</summary>
    public void JumpDummy(int index)
    {
        if (index < 0 || index >= dummies.Count) return;
        dummies[index].jumpRequested = true;
    }

    /// <summary>더미를 점프시킨 뒤 정점에서 내려찍게 함. 착지 상태일 때 시작함.</summary>
    public void StampDummy(int index)
    {
        if (index < 0 || index >= dummies.Count) return;
        var d = dummies[index];
        if (d.stampPhase == 0) SetStampPhase(d, 1);
    }

    /// <summary>더미가 착지할 때마다 내려찍기를 반복하게 함. 끄면 진행 중인 것만 마저 함.</summary>
    public void SetAutoStamp(int index, bool on)
    {
        if (index < 0 || index >= dummies.Count) return;
        dummies[index].autoStamp = on;
    }

    /// <summary>모든 더미의 자동 내려찍기를 끔.</summary>
    public void StopAllStamps()
    {
        for (int i = 0; i < dummies.Count; i++) dummies[i].autoStamp = false;
    }

    private void SetStampPhase(Dummy d, int phase)
    {
        d.stampPhase = phase;
        d.stampPhaseTime = Runner != null ? (float)Runner.SimulationTime : 0f;
    }

    private static readonly System.Reflection.BindingFlags PrivateField =
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    /// <summary>Player의 비공개 복제 상태를 씀. 더미 애니가 실제 플레이어와 같게 보이도록 함.</summary>
    private static void SetPlayerState(Player player, string property, object value)
    {
        if (player == null) return;
        typeof(Player).GetProperty(property, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(player, value);
    }

    /// <summary>더미 Stamp 애니 재생. 실제 플레이어처럼 StampCount를 올림.</summary>
    private static void PlayStampAnimation(Player player)
    {
        var info = typeof(Player).GetProperty("StampCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (player == null || info == null) return;
        info.SetValue(player, (int)info.GetValue(player) + 1);
    }

    /// <summary>Player의 비공개 튜닝값을 읽음. 못 읽으면 기본값.</summary>
    private static float PlayerValue(Player player, string field, float fallback)
    {
        if (player == null) return fallback;
        var info = typeof(Player).GetField(field, PrivateField);
        return info != null && info.GetValue(player) is float value ? value : fallback;
    }

    /// <summary>
    /// 내려찍기 한 틱. 실제 Player와 같은 흐름: 공중에서 수평 정지 + 아래 속도 → 착지하면 무게 배율.
    /// 내려찍기 중이면 true를 돌려 수평 이동을 막음.
    /// </summary>
    private bool UpdateStamp(Dummy d, float now, bool nearTarget)
    {
        d.stampHanging = false;
        if (d.stampWeightEnd >= 0f && now >= d.stampWeightEnd)
        {
            if (d.source != null) d.source.SetWeightMultiplier(1f);
            d.stampWeightEnd = -1f;
        }

        // 착지 자세 유지 중에는 움직이지 않음 (실제 Player와 같음)
        if (d.stampRecoverEnd >= 0f)
        {
            if (now < d.stampRecoverEnd) return true;
            d.stampRecoverEnd = -1f;
            SetPlayerState(d.player, "IsStampRecovering", (NetworkBool)false);
        }

        if (d.stampPhase == 0 && d.autoStamp && nearTarget && d.cc.Grounded && now >= d.nextAutoStamp)
            SetStampPhase(d, 1);

        switch (d.stampPhase)
        {
            case 1:
                if (!d.cc.Grounded) return false;
                var v = d.cc.Velocity;
                if (v.y < 0f) { v.y = 0f; d.cc.Velocity = v; }
                d.cc.Jump(ignoreGrounded: true);
                SetStampPhase(d, 2);
                return true;

            case 2:
                // 정점 근처에서 내리꽂기 시작. 점프가 안 됐으면 포기함.
                if (now - d.stampPhaseTime > 1.5f) { SetStampPhase(d, 0); return false; }
                if (!d.cc.Grounded && d.cc.Velocity.y <= 1f) { SetStampPhase(d, 3); PlayStampAnimation(d.player); }
                return true;

            case 3:
                // 공중 정지: 속도를 지우고 이동(중력)을 건너뜀
                if (now - d.stampPhaseTime < PlayerValue(d.player, "_stampHangTime", 0f))
                {
                    d.cc.Velocity = Vector3.zero;
                    d.stampHanging = true;
                    return true;
                }
                SetStampPhase(d, 4);
                goto case 4;

            case 4:
                if (now - d.stampPhaseTime > 2f) { SetStampPhase(d, 0); return false; }
                d.cc.Velocity = new Vector3(0f, -PlayerValue(d.player, "_stampDownSpeed", 25f), 0f);
                return true;
        }
        return false;
    }

    /// <summary>내리꽂는 중 착지했으면 무게 배율을 걺. Move 뒤에 호출.</summary>
    private void CheckStampLanding(Dummy d, float now)
    {
        if (d.stampPhase != 4 || !d.cc.Grounded) return;

        SetStampPhase(d, 0);
        float recover = PlayerValue(d.player, "_stampRecoverTime", 0f);
        if (recover > 0f)
        {
            d.stampRecoverEnd = now + recover;
            SetPlayerState(d.player, "IsStampRecovering", (NetworkBool)true);
        }
        d.nextAutoStamp = now + recover + Random.Range(autoStampDelay.x, autoStampDelay.y);
        stampsThisTick++;
        stampTotal++;

        float duration = PlayerValue(d.player, "_stampWeightDuration", 0.3f);
        if (d.source == null || duration <= 0f) return;
        d.source.SetWeightMultiplier(PlayerValue(d.player, "_stampWeightMultiplier", 3f));
        d.stampWeightEnd = now + duration;
    }

    /// <summary>모든 더미를 제거함.</summary>
    public void ClearDummies()
    {
        if (Runner == null) { dummies.Clear(); return; }
        SetDummyCount(0);
    }

    private Dummy CreateDummy(int index, float x, float z)
    {
        if (dummyPrefabs == null || dummyPrefabs.Length == 0)
        {
            Debug.LogWarning($"{name}: dummyPrefabs가 비어 있습니다. 캐릭터 프리팹을 넣으세요.", this);
            return null;
        }

        var prefab = dummyPrefabs[index % dummyPrefabs.Length];
        // 입력 권한 없이 생성함. Player는 입력이 없으면 휠 carry만 하고 이동은 이 스크립트가 맡음.
        var obj = Runner.Spawn(prefab, DeckPoint(x, z) + Vector3.up * 0.1f, Quaternion.identity, null);
        if (obj == null) return null;

        obj.name = $"Dummy {index} ({prefab.name})";
        return new Dummy
        {
            obj = obj,
            player = obj.GetComponent<Player>(),
            cc = obj.GetComponent<NetworkCharacterController>(),
            controller = obj.GetComponent<CharacterController>(),
            carrier = obj.GetComponent<WheelCarrier>(),
            source = obj.GetComponent<WeightSource>()
        };
    }

    private static readonly System.Reflection.PropertyInfo LaunchedProperty =
        typeof(Player).GetProperty("IsLaunched", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>폭발 등으로 날아가는 중인지. Player의 비공개 상태라 리플렉션으로 읽음. 못 읽으면 false.</summary>
    private static bool IsLaunched(Player player)
    {
        if (player == null || LaunchedProperty == null) return false;
        return LaunchedProperty.GetValue(player) is NetworkBool launched && launched;
    }

    /// <summary>판 중심 기준 (x, z) 오프셋 바로 아래의 데크 표면. 데크가 없으면 판 높이의 같은 수평 위치.</summary>
    private Vector3 DeckPoint(float x, float z)
    {
        Vector3 center = platformBody.position;
        Vector3 above = center + new Vector3(x, 10f, z);
        return TryFindDeck(above, out Vector3 surface) ? surface : new Vector3(above.x, center.y, above.z);
    }

    /// <summary>휠 리셋과 더미 이동. 플레이어(50) 뒤, 물리 스텝 전에 실행됨.</summary>
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || platformBody == null) return;

        if (resetRequested) ApplyReset();
        if (calibrationPhase >= 0) UpdateCalibration();
        else if (strategyActive) UpdateStrategy();
        if (handleMonsters) UpdateMonsters();
        DriveDummies();
    }

    /// <summary>
    /// 더미 한 틱 이동.
    /// Player는 입력이 없으면 휠 carry만 하고 끝나므로, 그 뒤에 실제 플레이어와 같은 순서로
    /// 이동(중력 포함) → carry 기준점 갱신 → 무게 위치 갱신을 이어서 함.
    /// </summary>
    private void DriveDummies()
    {
        float now = (float)Runner.SimulationTime;
        for (int i = 0; i < dummies.Count; i++)
        {
            var d = dummies[i];
            if (!d.IsValid) continue;

            // 들려 있으면 CharacterController가 꺼지고 든 쪽이 위치를 정함. 날아가는 중에는 Player가 탄도 이동을 직접 함.
            // 이때 이동을 호출하면 경고가 나거나 넉백 속도가 이동 속도로 잘려 사라짐.
            if (d.controller == null || !d.controller.enabled || IsLaunched(d.player)) continue;

            Vector3 position = d.obj.transform.position;
            Vector3 direction = Vector3.zero;
            Vector3 toTarget = platformBody.position + new Vector3(d.target.x, 0f, d.target.y) - position;
            toTarget.y = 0f;
            // 목표에 왔거나, 울타리 등에 막혀 더 못 가고 멈춰 서 있으면 그 자리에서 찍음
            Vector3 walk = d.cc.Velocity;
            walk.y = 0f;
            bool settled = toTarget.magnitude <= autoStampArriveDistance || walk.magnitude < 0.3f;
            bool stamping = UpdateStamp(d, now, settled);

            if (d.moving && !stamping)
            {
                Vector3 goal = platformBody.position + new Vector3(d.target.x, 0f, d.target.y);
                Vector3 flat = goal - position;
                flat.y = 0f;
                if (flat.magnitude <= arriveDistance) d.moving = false;
                else direction = flat.normalized;
            }

            if (d.jumpRequested && d.cc.Grounded)
            {
                var v = d.cc.Velocity;
                if (v.y < 0f) { v.y = 0f; d.cc.Velocity = v; }
                d.cc.Jump(ignoreGrounded: true);
                d.jumpRequested = false;
            }

            if (!d.stampHanging) d.cc.Move(direction);
            CheckStampLanding(d, now);

            position = d.obj.transform.position;
            if (d.carrier != null) d.carrier.EndTick(position);
            if (d.source != null) d.source.SetSimulatedPosition(position);
        }
    }

    /// <summary>위에서 아래로 쏘아 이 판의 데크 표면을 찾음. 트리거와 다른 물체는 무시함.</summary>
    private bool TryFindDeck(Vector3 from, out Vector3 point)
    {
        point = default;
        int count = Physics.RaycastNonAlloc(from, Vector3.down, hits, 30f, ~0, QueryTriggerInteraction.Ignore);

        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var col = hits[i].collider;
            if (col == null || col.GetComponentInParent<PlatformTilt>() != platform) continue;
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                point = hits[i].point;
            }
        }
        return best < float.PositiveInfinity;
    }

    // ---- 전략 주행 ----

    /// <summary>
    /// 휠의 현재 위치에서 시작하는 사각 코스를 만들고 4명이 팀으로 몰고 가게 함.
    /// 앞으로 한 변, 오른쪽으로 한 변, 뒤로 한 변, 왼쪽으로 한 변을 돌아 출발점에서 멈춤.
    /// withTroll 활성화시 무작위 1명이 목표 도달을 방해하는 트롤이 됨.
    /// </summary>
    public void StartCourse(bool withTroll = false)
    {
        if (wheelBody == null) return;

        Vector3 start = wheelBody.position;
        start.y = 0f;
        StartRoute(new[]
        {
            start + new Vector3(0f, 0f, courseSize),
            start + new Vector3(courseSize, 0f, courseSize),
            start + new Vector3(courseSize, 0f, 0f),
            start
        }, withTroll);
    }

    /// <summary>
    /// 지정한 웨이포인트(높이 무시)를 차례로 지나 마지막 점에서 멈추게 함. 기믹 위를 지나는 경로를 시험할 때 사용.
    /// 마지막을 뺀 점에서는 cornerSpeed까지 줄였다가 지나감.
    /// </summary>
    public void StartRoute(Vector3[] points, bool withTroll = false)
    {
        StopStrategy();
        if (wheelBody == null || points == null || points.Length == 0) return;

        if (dummies.Count < 4)
        {
            float r = DeckRadius * 0.5f;
            PlaceDummy(0, 0f, r);
            PlaceDummy(1, r, 0f);
            PlaceDummy(2, 0f, -r);
            PlaceDummy(3, -r, 0f);
        }

        foreach (var point in points) waypoints.Add(new Vector3(point.x, 0f, point.z));
        routeStart = new Vector3(wheelBody.position.x, 0f, wheelBody.position.z);

        for (int i = 0; i < waypoints.Count; i++)
            markers.Add(CreateMarker(waypoints[i], i == waypoints.Count - 1));

        waypointIndex = 0;
        legStartTime = (float)Runner.SimulationTime;
        strategyReport.Clear();
        strategyReport.AppendLine($"웨이포인트 {waypoints.Count}개, 순항 {cruiseSpeed:F1}m/s, 예상 제동 {expectedBrake:F1}m/s²");

        float now = (float)Runner.SimulationTime;
        for (int i = 0; i < dummies.Count; i++)
            dummies[i].nextDecision = now + Random.Range(reactionTime.x, reactionTime.y);

        trollIndex = withTroll && dummies.Count > 1 ? Random.Range(0, dummies.Count) : -1;
        if (trollIndex >= 0)
        {
            nextTacticTime = now;
            strategyReport.AppendLine($"트롤: {dummies[trollIndex].obj.name}");
        }

        strategyActive = true;
    }

    /// <summary>전략 주행을 멈춤. 더미는 그 자리에 섬.</summary>
    public void StopStrategy()
    {
        strategyActive = false;
        gatePhase = GatePhase.Off;
        gateHammer = null;
        if (trollIndex >= 0 && trollIndex < dummies.Count) dummies[trollIndex].autoStamp = false;
        trollIndex = -1;
        waypoints.Clear();
        foreach (var m in markers) if (m != null) Destroy(m);
        markers.Clear();
        for (int i = 0; i < dummies.Count; i++) dummies[i].moving = false;
    }

    /// <summary>
    /// 팀의 판단. 남은 거리로 지금 원하는 속도를 정하고, 실제 속도와의 차이만큼 판을 기울일 위치(lean)를 정함.
    /// 휠은 무게가 쏠린 쪽으로 가속하므로, 차이 방향으로 몰리면 가속·제동·방향 전환이 모두 됨.
    /// 더미는 각자의 반응 시간이 지날 때마다 그 순간의 판단을 보고 자리를 옮김.
    /// </summary>
    private void UpdateStrategy()
    {
        if (waypoints.Count == 0) { strategyActive = false; return; }

        float now = (float)Runner.SimulationTime;
        Vector3 position = wheelBody.position;
        Vector3 velocity = wheelBody.linearVelocity;
        position.y = 0f;
        velocity.y = 0f;

        bool last = waypointIndex == waypoints.Count - 1;
        Vector3 toGoal = waypoints[waypointIndex] - position;
        float distance = toGoal.magnitude;

        // 중간 웨이포인트는 옆으로 비껴 지나가도 다음 웨이포인트 쪽으로 넘어섰으면 지난 것으로 봄.
        // 안 그러면 밀려서 비껴간 뒤 되돌아가려다 떨어지는 등 사람이 안 할 행동을 함.
        bool passed = !last && Vector3.Distance(waypoints[waypointIndex + 1], position)
                               < Vector3.Distance(waypoints[waypointIndex + 1], waypoints[waypointIndex]);

        if ((distance < waypointRadius || passed) && (!last || velocity.magnitude < 1f))
        {
            strategyReport.AppendLine($"웨이포인트 {waypointIndex + 1} 도착: {now - legStartTime:F1}초, 통과 속도 {velocity.magnitude:F1}m/s, 거리 오차 {distance:F1}m" +
                                      (teamStamps ? $", 팀 내려찍기 {legTeamStamps}회" : ""));
            legTeamStamps = 0;
            legStartTime = now;

            if (last)
            {
                strategyReport.AppendLine("코스 완료");
                StopStrategy();
                SpreadToBalance();
                return;
            }

            waypointIndex++;
            toGoal = waypoints[waypointIndex] - position;
            distance = toGoal.magnitude;
            last = waypointIndex == waypoints.Count - 1;
        }

        if (gateHammers.Count > 0 && UpdateGate(now, position, velocity, ref toGoal, ref distance, ref last))
            return;

        // 남은 거리에서 예상 감속으로 멈추거나(마지막) 모퉁이 속도로 줄일 수 있는 속도
        float endSpeed = last ? 0f : cornerSpeed;
        float reachable = Mathf.Sqrt(endSpeed * endSpeed + 2f * expectedBrake * Mathf.Max(0f, distance - 1f));
        float desiredSpeed = Mathf.Min(cruiseSpeed, reachable);

        // 가는 방향이 목표에서 크게 어긋나 있으면 먼저 속도를 줄임
        float speedNow = velocity.magnitude;
        if (speedNow > 1f && distance > 0.01f)
        {
            float alignment = Vector3.Dot(velocity / speedNow, toGoal / distance);
            float keep = Mathf.InverseLerp(turnSpeedFullAt, turnSpeedFreeAt, alignment);
            desiredSpeed = Mathf.Min(desiredSpeed, Mathf.Lerp(turnSpeed, cruiseSpeed, keep));
        }

        Vector3 desired = distance > 0.01f ? toGoal / distance * desiredSpeed : Vector3.zero;

        // 경로선(이전 웨이포인트 → 지금 웨이포인트)에서 옆으로 벗어난 만큼 되돌아감
        // 마지막 정지 지점 근처에서는 끔. 멈추는 동안 옆으로 끌어당기면 목표 주위를 맴돎.
        if (lineHold > 0f && gatePhase == GatePhase.Off && !(last && distance < 15f))
        {
            Vector3 legStart = waypointIndex > 0 ? waypoints[waypointIndex - 1] : routeStart;
            Vector3 leg = waypoints[waypointIndex] - legStart;
            if (leg.sqrMagnitude > 1f)
            {
                Vector3 legDir = leg.normalized;
                Vector3 rel = position - legStart;
                Vector3 cross = rel - legDir * Vector3.Dot(rel, legDir);
                desired -= Vector3.ClampMagnitude(cross * lineHold, desiredSpeed + 2f);
            }
        }

        Vector3 error = desired - velocity;
        // 미리 더하는 쏠림은 빨리 달릴 때만 씀. 멈추거나 모퉁이를 돌 때 쓰면 제동이 약해져 목표 주위를 오감.
        float feedForward = leanFeedForward * Mathf.InverseLerp(feedForwardFrom, cruiseSpeed, desiredSpeed);
        Vector3 lean = desired / fullLeanSpeed * feedForward + error / fullLeanError;
        plannedLean = Vector3.ClampMagnitude(lean, 1f) * (DeckRadius * 0.8f);
        plannedDirection = distance > 0.01f ? toGoal / distance : Vector3.forward;

        if (teamStamps) UpdateTeamStamp(now, error);

        if (trollIndex >= 0 && now >= nextTacticTime) ChangeTrollTactic(now);

        // 트롤을 뺀 인원으로 줄을 섬
        int teamCount = trollIndex >= 0 ? dummies.Count - 1 : dummies.Count;
        int slot = 0;

        for (int i = 0; i < dummies.Count; i++)
        {
            var d = dummies[i];
            bool troll = i == trollIndex;
            int mySlot = troll ? -1 : slot++;
            if (!d.IsValid || now < d.nextDecision) continue;

            Vector2 spot;
            if (troll)
            {
                d.nextDecision = now + Random.Range(trollReactionTime.x, trollReactionTime.y);
                spot = AvoidBombs(TrollSpot());
                d.autoStamp = trollStamps && trollTactic == TrollTactic.Counter;
            }
            else
            {
                d.nextDecision = now + Random.Range(reactionTime.x, reactionTime.y);
                spot = AvoidBombs(SlotFor(mySlot, teamCount, plannedLean));
            }
            MoveDummy(i, spot.x, spot.y);
        }
    }

    // ---- 팀 내려찍기 ----

    private float nextTeamStamp;
    private int legTeamStamps;

    /// <summary>
    /// 팀의 전술 내려찍기. 가야 할 속도와 실제 속도 차이(error)가 크면 그 방향 쪽 가장자리에 선 더미 한 명이 찍음.
    /// 찍은 쪽으로 무게 배율·판 눌림·휠 반동이 들어가므로 가속, 제동, 방향 전환을 모두 앞당김.
    /// 실제 Player와 같은 흐름(점프 → 정점 근처에서 찍기)을 그대로 씀.
    /// </summary>
    private void UpdateTeamStamp(float now, Vector3 error)
    {
        if (now < nextTeamStamp || error.magnitude < teamStampError) return;

        Vector3 want = error.normalized;
        Vector3 center = platformBody.position;
        float reach = DeckRadius * teamStampReach;

        int best = -1;
        float bestAlong = reach;
        for (int i = 0; i < dummies.Count; i++)
        {
            var d = dummies[i];
            if (i == trollIndex || i == hunterIndex || !d.IsValid || d.stampPhase != 0 || d.stampRecoverEnd >= 0f) continue;
            if (!d.cc.Grounded || now - d.lastTeamStamp < teamStampCooldown || d.player == null || d.player.IsHolding) continue;
            Vector3 offset = d.obj.transform.position - center;
            offset.y = 0f;
            float along = Vector3.Dot(offset, want);
            // 옆으로 많이 비껴 선 더미는 엉뚱한 방향으로 밀게 되므로 뺌
            if (along < bestAlong || offset.sqrMagnitude < 1e-4f || along / offset.magnitude < 0.7f) continue;
            bestAlong = along;
            best = i;
        }
        if (best < 0) return;

        dummies[best].lastTeamStamp = now;
        SetStampPhase(dummies[best], 1);
        nextTeamStamp = now + teamStampInterval;
        legTeamStamps++;
    }

    // ---- 몬스터 처리 ----

    private readonly Dictionary<Monster, float> monsterOnDeckSince = new();
    private readonly StringBuilder monsterReport = new();
    private Monster[] monsterCache = new Monster[0];
    private float monsterCacheTime = -1f;

    // 몬스터 담당 더미: 0 없음, 1 잡으러 감, 2 들고 가장자리로 감
    private int hunterIndex = -1;
    private int hunterPhase;
    private float hunterPhaseTime;
    private Monster hunterTarget;
    private float hunterTargetSince;
    private Vector2 hunterThrowSpot;

    /// <summary>몬스터 처리 기록. 잡은 시점, 던진 시점.</summary>
    public string MonsterReport => monsterReport.ToString();

    /// <summary>
    /// 판 위에 monsterGrabDelay초 넘게 있던 몬스터를 가장 가까운 더미(트롤 제외)가 잡아서 가장자리에서 바깥으로 던짐.
    /// 실제 Player의 잡기·던지기 함수를 그대로 불러서 무게 이동, 애니, 던지는 힘이 실제와 같음.
    /// 나머지 더미는 평소처럼 판단함. 한 명이 빠진 만큼 기울이는 힘이 약해지는 것까지 그대로 드러남.
    /// </summary>
    private void UpdateMonsters()
    {
        float now = (float)Runner.SimulationTime;
        if (now - monsterCacheTime > 0.5f)
        {
            monsterCache = FindObjectsByType<Monster>(FindObjectsSortMode.None);
            monsterCacheTime = now;
        }

        // 판 위에 오른 시점 기록. 내려가거나 사라지면 지움
        foreach (var m in monsterCache)
        {
            if (m == null || !m.Object || !m.Object.IsValid) continue;
            bool onDeck = m.TryGetComponent<WheelRider>(out var rider) && rider.IsOnPlatform && rider.CurrentPlatform == platform;
            if (onDeck) { if (!monsterOnDeckSince.ContainsKey(m)) monsterOnDeckSince[m] = now; }
            else if (m != hunterTarget) monsterOnDeckSince.Remove(m);
        }
        scratchMonsters.Clear();
        foreach (var m in monsterOnDeckSince.Keys) if (m == null || !m.Object || !m.Object.IsValid) scratchMonsters.Add(m);
        foreach (var m in scratchMonsters) monsterOnDeckSince.Remove(m);

        if (hunterIndex >= dummies.Count || (hunterIndex >= 0 && !dummies[hunterIndex].IsValid)) EndHunt(now, null);
        if (hunterIndex < 0) PickHunter(now);
        if (hunterIndex < 0) return;

        var d = dummies[hunterIndex];
        d.nextDecision = now + 0.5f; // 팀 판단이 이 더미를 다른 자리로 보내지 않게 막음
        Vector3 center = platformBody.position;

        if (now - hunterPhaseTime > monsterGiveUpTime) { EndHunt(now, "포기(시간 초과)"); return; }

        if (hunterPhase == 1)
        {
            if (hunterTarget == null || !hunterTarget.Object || !hunterTarget.Object.IsValid || !monsterOnDeckSince.ContainsKey(hunterTarget))
            {
                EndHunt(now, null);
                return;
            }

            Vector3 to = hunterTarget.transform.position - d.obj.transform.position;
            to.y = 0f;
            Vector3 offset = hunterTarget.transform.position - center;
            MoveDummy(hunterIndex, offset.x, offset.z);

            if (to.magnitude <= monsterGrabReach && d.cc.Grounded && d.stampPhase == 0)
            {
                CallPlayer(d.player, "HandleHoldPressed");
                if (d.player.IsHolding)
                {
                    // 지금 선 방향 그대로 바깥으로 걸어가면 마지막에 바깥을 보고 서게 됨
                    Vector3 outward = d.obj.transform.position - center;
                    outward.y = 0f;
                    if (outward.magnitude < 0.5f) outward = d.obj.transform.forward;
                    outward.y = 0f;
                    outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward;
                    float r = DeckRadius * monsterThrowEdge;
                    hunterThrowSpot = new Vector2(outward.x * r, outward.z * r);
                    LogMonster($"{d.obj.name}: 몬스터 잡음 (판에 오른 지 {now - hunterTargetSince:F1}초, 잡으러 가는 데 {now - hunterPhaseTime:F1}초)");
                    hunterPhase = 2;
                    hunterPhaseTime = now;
                }
            }
            return;
        }

        // 2: 들고 가장자리로 가서 바깥을 보고 던짐
        if (!d.player.IsHolding) { EndHunt(now, "놓침"); return; }
        MoveDummy(hunterIndex, hunterThrowSpot.x, hunterThrowSpot.y);

        Vector3 here = d.obj.transform.position - center;
        here.y = 0f;
        Vector3 spot = new Vector3(hunterThrowSpot.x, 0f, hunterThrowSpot.y);
        Vector3 facing = d.obj.transform.forward;
        facing.y = 0f;
        bool arrived = (here - spot).magnitude <= arriveDistance + 0.3f || here.magnitude >= spot.magnitude;
        bool facingOut = facing.sqrMagnitude > 1e-4f && Vector3.Dot(facing.normalized, spot.normalized) > 0.7f;
        // 이미 바깥에 서 있는데 안쪽을 보고 있으면 조금 더 바깥으로 걸어가 몸을 돌림
        if (arrived && !facingOut && spot.sqrMagnitude > 1e-4f)
        {
            float r = Mathf.Min(DeckRadius * 0.95f, here.magnitude + 1f);
            Vector3 outward = spot.normalized;
            hunterThrowSpot = new Vector2(outward.x * r, outward.z * r);
        }

        if (arrived && facingOut && d.cc.Grounded)
        {
            CallPlayer(d.player, "HandleThrowPressed");
            EndHunt(now, $"{d.obj.name}: 몬스터 던짐 (판에 오른 지 {now - hunterTargetSince:F1}초)");
        }
    }

    private readonly List<Monster> scratchMonsters = new();

    /// <summary>오래 머문 몬스터 중 가장 오래된 것을 고르고, 그 몬스터와 가장 가까운 더미(트롤 제외)를 보냄.</summary>
    private void PickHunter(float now)
    {
        Monster target = null;
        float since = float.MaxValue;
        foreach (var pair in monsterOnDeckSince)
        {
            if (now - pair.Value < monsterGrabDelay || pair.Value >= since) continue;
            target = pair.Key;
            since = pair.Value;
        }
        if (target == null) return;

        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < dummies.Count; i++)
        {
            var d = dummies[i];
            if (i == trollIndex || !d.IsValid || d.stampPhase != 0 || d.player == null || d.player.IsHolding) continue;
            if (d.controller == null || !d.controller.enabled || IsLaunched(d.player)) continue;
            float distance = Vector3.Distance(d.obj.transform.position, target.transform.position);
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        if (best < 0) return;

        hunterIndex = best;
        hunterTarget = target;
        hunterTargetSince = since;
        hunterPhase = 1;
        hunterPhaseTime = now;
        dummies[best].autoStamp = false;
    }

    private void EndHunt(float now, string message)
    {
        if (message != null) LogMonster(message);
        if (hunterIndex >= 0 && hunterIndex < dummies.Count && dummies[hunterIndex].IsValid)
        {
            dummies[hunterIndex].nextDecision = now;
            dummies[hunterIndex].moving = false;
        }
        hunterIndex = -1;
        hunterPhase = 0;
        hunterTarget = null;
    }

    private void LogMonster(string message)
    {
        monsterReport.AppendLine(message);
        if (strategyActive) strategyReport.AppendLine(message);
        Debug.Log($"{name}: {message}", this);
    }

    /// <summary>Player의 비공개 입력 처리 함수를 부름. 더미는 입력이 없어서 잡기·던지기를 이렇게 대신 실행함.</summary>
    private static void CallPlayer(Player player, string method)
    {
        if (player == null) return;
        typeof(Player).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(player, null);
    }

    // ---- 기믹 코스 ----

    private System.Collections.IEnumerator CourseRoutine(bool withTroll)
    {
        CourseRunning = true;
        courseReport.Clear();
        float started = Time.time;

        Vector3 first = courseWaypoints[0].position - courseStart.position;
        Vector3 forward = Flat(first).sqrMagnitude > 0.01f ? Flat(first).normalized : Vector3.forward;

        // 출발 곡선: 출발점 뒤쪽(빈 땅)으로 한 번 잼
        if (!HasGoProfile)
        {
            courseReport.AppendLine("출발 곡선 측정");
            ClearDummies();
            ResetWheelAt(courseCalibrationPoint != null ? courseCalibrationPoint.position : courseStart.position);
            yield return new WaitForSeconds(0.6f);
            SetDummyCount(4);
            yield return new WaitForSeconds(0.5f);
            CalibrateGo(-forward);
            while (Calibrating) yield return null;
        }

        ClearDummies();
        ResetWheelAt(courseStart.position);
        yield return new WaitForSeconds(0.6f);
        courseLevel = wheelBody.position.y;

        SetHammerGates(courseHammers);
        StartRecording("course_" + System.DateTime.Now.ToString("HHmmss"));
        var points = new Vector3[courseWaypoints.Length];
        for (int i = 0; i < points.Length; i++) points[i] = courseWaypoints[i].position;
        StartRoute(points, withTroll);
        courseReport.AppendLine(withTroll ? "출발 (트롤 1명)" : "출발");

        bool bombDropped = courseBombPrefab == null || courseBombZone == null;
        while (strategyActive)
        {
            if (!bombDropped && Flat(wheelBody.position - courseBombZone.position).magnitude < courseBombRadius)
            {
                bombDropped = true;
                if (SpawnOnDeck(courseBombPrefab, -1.5f, 1.5f) != null) courseReport.AppendLine("폭탄 투하");
            }
            if (WheelFell()) break;
            yield return null;
        }

        if (!WheelFell() && courseElevator != null)
        {
            float wait = 0f;
            while (courseElevator.CurrentPhase != MovingPad.Phase.AtEnd && wait < 30f)
            {
                wait += Time.deltaTime;
                yield return null;
            }
            courseReport.AppendLine(courseElevator.CurrentPhase == MovingPad.Phase.AtEnd ? "엘리베이터 도착" : "엘리베이터 출발 안 함");
        }

        courseReport.Append(strategyReport);

        if (!WheelFell())
        {
            StartRoute(new[] { courseFinish.position }, false);
            while (strategyActive && !WheelFell()) yield return null;
        }

        StopStrategy();
        StopRecording();
        courseReport.Append(strategyReport);
        courseReport.AppendLine(WheelFell() ? "실패: 휠이 코스에서 떨어짐" : $"완주: {Time.time - started:F1}초");
        Debug.Log($"{name}: 기믹 코스 결과\n{courseReport}", this);
        CourseRunning = false;
    }

    private float courseLevel;

    // ---- 빙판 추격 ----

    private readonly StringBuilder iceChaseReport = new();
    private Vector3 iceChaseTarget;
    private GameObject iceChaseTargetDisc, iceChaseTriggerDisc;

    public bool IceChasing { get; private set; }
    public string IceChaseReport => iceChaseReport.ToString();

    /// <summary>
    /// 빙판 위 무작위 목표를 향해 계속 몰고 감. 휠이 목표 주변(트리거 반지름)에 들어오면 목표가 다른 곳으로 옮겨 가서
    /// 멈추지 못하고 계속 선회, 급감속, 급회전하게 됨. 빙판에서의 조작감을 보기 위함.
    /// </summary>
    public void StartIceChase()
    {
        if (IceChasing || CourseRunning || HammerLooping || iceChasePad == null) return;
        StartCoroutine(IceChaseRoutine());
    }

    /// <summary>빙판 추격을 멈춤. 휠과 더미는 그 자리에 둠.</summary>
    public void StopIceChase() => IceChasing = false;

    private System.Collections.IEnumerator IceChaseRoutine()
    {
        IceChasing = true;
        iceChaseReport.Clear();

        StopStrategy();
        ClearDummies();
        ResetWheelAt(iceChasePad.transform.position);
        yield return new WaitForSeconds(0.6f);

        iceChaseTriggerDisc = CreateDisc("Ice Chase Trigger", iceChaseTriggerRadius, new Color(0.25f, 0.45f, 0.9f), 0.03f);
        iceChaseTargetDisc = CreateDisc("Ice Chase Target", iceChaseTargetRadius, new Color(1f, 0.55f, 0.1f), 0.06f);

        StartRecording("icechase_" + System.DateTime.Now.ToString("HHmmss"));

        int legs = 0;
        float legStart = Time.time;
        MoveIceChaseTarget();
        StartRoute(new[] { iceChaseTarget }, false);

        while (IceChasing)
        {
            Vector3 flat = Flat(wheelBody.position - iceChaseTarget);
            bool reached = flat.magnitude < iceChaseTriggerRadius;

            // 트리거보다 먼저 멈춰 버렸어도(제동이 강해 목표 앞에서 섬) 다음 목표로 넘어감
            if (reached || !strategyActive)
            {
                legs++;
                Vector3 velocity = Flat(wheelBody.linearVelocity);
                Vector3 previous = iceChaseTarget;
                MoveIceChaseTarget();

                float turn = velocity.magnitude > 0.5f
                    ? Vector3.Angle(velocity, Flat(iceChaseTarget - wheelBody.position))
                    : 0f;
                iceChaseReport.AppendLine(
                    $"{legs}번째 목표: {Time.time - legStart:F1}초, {(reached ? "트리거 진입" : "트리거 앞에서 멈춤")} 속도 {velocity.magnitude:F1}m/s, " +
                    $"다음 목표까지 {Vector3.Distance(Flat(previous), Flat(iceChaseTarget)):F0}m, 꺾어야 할 각도 {turn:F0}°");

                legStart = Time.time;
                StartRoute(new[] { iceChaseTarget }, false);
            }
            yield return null;
        }

        StopStrategy();
        StopRecording();
        if (iceChaseTargetDisc != null) Destroy(iceChaseTargetDisc);
        if (iceChaseTriggerDisc != null) Destroy(iceChaseTriggerDisc);
        Debug.Log($"{name}: 빙판 추격 결과\n{iceChaseReport}", this);
    }

    /// <summary>빙판 안쪽에서 휠과 충분히 떨어진 무작위 지점으로 목표를 옮김.</summary>
    private void MoveIceChaseTarget()
    {
        Vector3 center = iceChasePad.transform.position;
        Vector3 half = iceChasePad.transform.lossyScale * 0.5f;
        float hx = Mathf.Max(0f, half.x - iceChaseEdgeMargin);
        float hz = Mathf.Max(0f, half.z - iceChaseEdgeMargin);

        Vector3 best = center;
        float bestDistance = -1f;
        for (int i = 0; i < 30; i++)
        {
            Vector3 candidate = center + new Vector3(Random.Range(-hx, hx), 0f, Random.Range(-hz, hz));
            float distance = Flat(candidate - wheelBody.position).magnitude;
            if (distance >= iceChaseMinJump) { best = candidate; break; }
            if (distance > bestDistance) { bestDistance = distance; best = candidate; }
        }

        iceChaseTarget = new Vector3(best.x, wheelBody.position.y, best.z);

        float groundY = wheelBody.position.y - SphereRadius;
        if (iceChaseTargetDisc != null) iceChaseTargetDisc.transform.position = new Vector3(best.x, groundY + 0.06f, best.z);
        if (iceChaseTriggerDisc != null) iceChaseTriggerDisc.transform.position = new Vector3(best.x, groundY + 0.03f, best.z);
    }

    private float SphereRadius => wheel.TryGetComponent<SphereCollider>(out var sc) ? sc.radius * wheel.transform.lossyScale.x : 5f;

    /// <summary>바닥에 까는 납작한 원판. 충돌 없음.</summary>
    private GameObject CreateDisc(string discName, float radius, Color color, float height)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = discName;
        Destroy(disc.GetComponent<Collider>());
        disc.transform.localScale = new Vector3(radius * 2f, height, radius * 2f);

        var renderer = disc.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null) renderer.material = new Material(shader);
        renderer.material.color = color;
        return disc;
    }

    // ---- 해머 반복 보기 ----

    private readonly StringBuilder hammerLoopReport = new();

    public bool HammerLooping { get; private set; }
    public string HammerLoopReport => hammerLoopReport.ToString();

    /// <summary>
    /// 출발점에서 점프대로 뛰어올라 해머 외다리를 타이밍 없이 직진하는 시도를 반복함. 해머에 맞는 모습을 여러 번 보기 위함.
    /// 매 회 휠을 출발점으로 되돌림.
    /// </summary>
    public void RunHammerLoop(int rounds)
    {
        if (HammerLooping || CourseRunning || hammerLoopStart == null || hammerLoopEnd == null) return;
        StartCoroutine(HammerLoopRoutine(rounds));
    }

    private System.Collections.IEnumerator HammerLoopRoutine(int rounds)
    {
        HammerLooping = true;
        hammerLoopReport.Clear();
        int hits = 0;

        for (int round = 1; round <= rounds; round++)
        {
            StopStrategy();
            ClearDummies();
            ResetWheelAt(hammerLoopStart.position);
            yield return new WaitForSeconds(0.6f);

            // 타이밍을 보지 않고 직진함
            SetHammerGates();
            StartRoute(new[] { hammerLoopEnd.position }, false);

            float level = float.NegativeInfinity;
            bool knocked = false;
            string result = "시간 초과";
            float started = Time.time;

            while (Time.time - started < 30f)
            {
                if (wheel.IsKnockedBack) knocked = true;

                // 땅에 닿아 있을 때 가장 높았던 층보다 3m 이상 낮은 곳에 닿으면 떨어진 것
                if (wheel.IsGrounded)
                {
                    float y = wheelBody.position.y;
                    if (y > level) level = y;
                    if (y < level - 3f)
                    {
                        result = knocked ? "맞고 떨어짐" : "맞지 않고 떨어짐";
                        break;
                    }
                }

                if (!strategyActive)
                {
                    result = knocked ? "맞았지만 버팀" : "맞지 않고 통과";
                    break;
                }
                yield return null;
            }

            if (knocked) hits++;
            hammerLoopReport.AppendLine($"{round}회차: {result}");
            yield return new WaitForSeconds(2f);
        }

        StopStrategy();
        hammerLoopReport.AppendLine($"{rounds}회 중 {hits}회 맞음");
        Debug.Log($"{name}: 해머 반복 보기 결과\n{hammerLoopReport}", this);
        HammerLooping = false;
    }

    /// <summary>
    /// 휠이 코스에서 떨어졌는지. 땅에 닿아 있을 때의 높이 중 가장 높았던 층보다 3m 이상 낮은 곳에 닿았으면 떨어진 것.
    /// 고지대에 올라섰다가 아래 바닥으로 떨어지는 경우를 잡음.
    /// </summary>
    private bool WheelFell()
    {
        if (!wheel.IsGrounded) return false;
        float y = wheelBody.position.y;
        if (y > courseLevel) courseLevel = y;
        return y < courseLevel - 3f;
    }

    // ---- 해머 타이밍 대기 ----

    /// <summary>
    /// 해머 앞에서 멈췄다 타이밍을 봐서 지나감. 이번 틱 판단을 여기서 끝냈으면 true.
    /// Approach: 목표를 멈출 자리로 바꿔 평소 판단으로 멈춤. Settle: 판 가운데로 모여 멈춤.
    /// Wait: 계산한 출발 시점까지 기다림. Go: 앞으로 몰려 해머를 지나감.
    /// </summary>
    private bool UpdateGate(float now, Vector3 position, Vector3 velocity, ref Vector3 toGoal, ref float distance, ref bool last)
    {
        if (gatePhase == GatePhase.Off)
        {
            if (distance < 0.01f) return false;
            Vector3 dir = toGoal / distance;
            foreach (var h in gateHammers)
            {
                Vector3 rel = Flat(h.transform.position) - position;
                float along = Vector3.Dot(rel, dir);
                float across = (rel - dir * along).magnitude;
                if (along > gateHoldDistance && along < gateHoldDistance + 80f && across < 12f)
                {
                    gateHammer = h;
                    gateDirection = dir;
                    gatePhase = GatePhase.Approach;
                    gatePhaseTime = now;
                    break;
                }
            }
            if (gatePhase == GatePhase.Off) return false;
        }

        float planeDistance = Vector3.Dot(Flat(gateHammer.transform.position) - position, gateDirection);

        // 트롤은 대기·출발 중에도 계속 방해함. 팀만 모였다 출발함.
        if (gatePhase != GatePhase.Approach) UpdateTrollDuringGate(now);

        switch (gatePhase)
        {
            case GatePhase.Approach:
            {
                Vector3 hold = Flat(gateHammer.transform.position) - gateDirection * gateHoldDistance;
                float toHold = (hold - position).magnitude;
                if (toHold < 3f) { if (gateNearTime < 0f) gateNearTime = now; }
                else gateNearTime = -1f;

                // 거의 멈췄거나, 트롤 등으로 못 멈춰도 근처에서 2초 지나면 대기 자세로 감
                bool stopped = toHold < 1.5f && velocity.magnitude < 0.6f;
                bool nearLong = gateNearTime >= 0f && now - gateNearTime > 2f;
                if (stopped || nearLong)
                {
                    gateNearTime = -1f;
                    PlaceStance(gateDirection);
                    gatePhase = GatePhase.Settle;
                    gatePhaseTime = now;
                    return true;
                }
                toGoal = hold - position;
                distance = toGoal.magnitude;
                last = true;
                return false;
            }

            case GatePhase.Settle:
                if (trollIndex >= 0) HoldTeam(now, position, velocity);
                // 다 멈췄거나, 방해 때문에 완전히 못 멈춰도 3초 지나면 그 상태에서 출발 시점을 계산함
                if ((now - gatePhaseTime > 1f && Settled()) || now - gatePhaseTime > 3f)
                {
                    gateGoTime = HasGoProfile ? FindGoTime(now, planeDistance) : now;
                    strategyReport.AppendLine($"해머 앞 대기: 멈춘 뒤 {gateGoTime - now:F1}초 기다렸다 출발");
                    gatePhase = GatePhase.Wait;
                }
                return true;

            case GatePhase.Wait:
                if (trollIndex >= 0) HoldTeam(now, position, velocity);
                if (now >= gateGoTime)
                {
                    GoLean(gateDirection);
                    gatePhase = GatePhase.Go;
                    gatePhaseTime = now;
                }
                return true;

            case GatePhase.Go:
                // 출발 뒤에도 해머 선을 지킴. 옆으로 흐르면 예측한 경로와 달라져 타이밍이 어긋남.
                GoLean(gateDirection, now, position);
                if (planeDistance < -15f || now - gatePhaseTime > 12f)
                {
                    gateHammers.Remove(gateHammer);
                    gateHammer = null;
                    gatePhase = GatePhase.Off;
                    for (int i = 0; i < dummies.Count; i++) dummies[i].nextDecision = now;
                    return false;
                }
                return true;
        }
        return false;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static Vector3 SideOf(Vector3 dir) => new Vector3(-dir.z, 0f, dir.x);

    /// <summary>진행 방향에 수직으로 판 가운데 한 줄로 섬. 앞뒤 균형이라 휠이 멈춤. 내려찍기는 멈춤.</summary>
    private void PlaceStance(Vector3 dir)
    {
        Vector3 side = SideOf(dir);
        int count = TeamCount;

        // 지금 서 있는 옆 방향 순서대로 자리를 줌. 서로 가로질러 가다 몸이 막혀 줄을 못 서는 일을 막음.
        var order = new List<int>();
        for (int i = 0; i < dummies.Count; i++)
            if (i != trollIndex && dummies[i].IsValid) order.Add(i);
        order.Sort((a, b) => SideCoord(a, side).CompareTo(SideCoord(b, side)));

        for (int slot = 0; slot < order.Count; slot++)
        {
            int i = order[slot];
            Vector3 p = side * ((slot - (count - 1) * 0.5f) * spacing);
            dummies[i].autoStamp = false;
            MoveDummy(i, p.x, p.z);
        }
    }

    /// <summary>더미가 판 중심에서 옆 방향으로 얼마나 떨어져 있는지.</summary>
    private float SideCoord(int index, Vector3 side) =>
        Vector3.Dot(dummies[index].obj.transform.position - platformBody.position, side);

    private int TeamCount => trollIndex >= 0 ? dummies.Count - 1 : dummies.Count;

    /// <summary>해머 앞 대기·출발 중 트롤 행동. 팀이 기울이려는 쪽(출발 중이면 진행 방향)을 기준으로 방해함.</summary>
    private void UpdateTrollDuringGate(float now)
    {
        if (trollIndex < 0 || trollIndex >= dummies.Count) return;
        var d = dummies[trollIndex];
        if (!d.IsValid || now < d.nextDecision) return;

        if (now >= nextTacticTime) ChangeTrollTactic(now);
        plannedDirection = gateDirection;
        plannedLean = gatePhase == GatePhase.Go ? gateDirection * (DeckRadius * 0.8f) : Vector3.zero;

        d.nextDecision = now + Random.Range(trollReactionTime.x, trollReactionTime.y);
        Vector2 spot = AvoidBombs(TrollSpot());
        d.autoStamp = trollStamps && trollTactic == TrollTactic.Counter;
        MoveDummy(trollIndex, spot.x, spot.y);
    }

    /// <summary>모두 동시에 진행 방향 가장자리로 몰려 최대로 기울임. 반응 시간 없이 바로 움직임.</summary>
    /// <summary>
    /// 트롤이 있을 때 해머 앞 대기 중 팀 행동. 모여 서기만 하면 트롤 혼자 판을 기울여 굴러가 버리므로,
    /// 멈출 자리를 지키도록 평소처럼 버팀(반응 시간 포함).
    /// </summary>
    private void HoldTeam(float now, Vector3 position, Vector3 velocity)
    {
        Vector3 hold = Flat(gateHammer.transform.position) - gateDirection * gateHoldDistance;
        Vector3 desired = Vector3.ClampMagnitude((hold - position) * 0.8f, 3f);
        plannedLean = Vector3.ClampMagnitude((desired - velocity) / fullLeanError, 1f) * (DeckRadius * 0.8f);

        int count = TeamCount;
        int slot = 0;
        for (int i = 0; i < dummies.Count; i++)
        {
            if (i == trollIndex) continue;
            int mySlot = slot++;
            var d = dummies[i];
            if (!d.IsValid || now < d.nextDecision) continue;
            d.nextDecision = now + Random.Range(reactionTime.x, reactionTime.y);
            Vector2 spot = AvoidBombs(SlotFor(mySlot, count, plannedLean));
            MoveDummy(i, spot.x, spot.y);
        }
    }

    /// <summary>
    /// 출발 중 트롤이 옆으로 밀 때: 앞으로 몰린 채 해머 선(해머를 지나는 진행 방향 직선)에서 벗어난 만큼 옆으로 버팀.
    /// </summary>
    private void GoLean(Vector3 dir, float now, Vector3 position)
    {
        Vector3 rel = position - Flat(gateHammer.transform.position);
        Vector3 cross = rel - dir * Vector3.Dot(rel, dir);
        Vector3 shift = Vector3.ClampMagnitude(-cross * 0.6f, DeckRadius * 0.4f);

        Vector3 side = SideOf(dir);
        int count = TeamCount;
        int slot = 0;
        for (int i = 0; i < dummies.Count; i++)
        {
            if (i == trollIndex) continue;
            int mySlot = slot++;
            var d = dummies[i];
            if (!d.IsValid || now < d.nextDecision) continue;
            d.nextDecision = now + Random.Range(reactionTime.x, reactionTime.y);
            Vector3 p = side * ((mySlot - (count - 1) * 0.5f) * spacing) + dir * (DeckRadius * 0.8f) + shift;
            Vector2 spot = Vector2.ClampMagnitude(new Vector2(p.x, p.z), DeckRadius * 0.85f);
            MoveDummy(i, spot.x, spot.y);
        }
    }

    private void GoLean(Vector3 dir)
    {
        Vector3 side = SideOf(dir);
        int count = TeamCount;
        int slot = 0;
        for (int i = 0; i < dummies.Count; i++)
        {
            if (i == trollIndex) continue;
            Vector3 p = side * ((slot++ - (count - 1) * 0.5f) * spacing) + dir * (DeckRadius * 0.8f);
            MoveDummy(i, p.x, p.z);
        }
    }

    /// <summary>휠이 멈췄고 더미가 모두 자리에 섰는지.</summary>
    private bool Settled()
    {
        if (Flat(wheelBody.linearVelocity).magnitude > 0.25f) return false;
        for (int i = 0; i < dummies.Count; i++)
            if (i != trollIndex && dummies[i].IsValid && dummies[i].moving) return false;
        return true;
    }

    /// <summary>출발 곡선 측정. 모여 서기 → 멈추면 출발 → 7초 기록.</summary>
    private void UpdateCalibration()
    {
        float now = (float)Runner.SimulationTime;
        Vector3 position = Flat(wheelBody.position);

        switch (calibrationPhase)
        {
            case 0:
                PlaceStance(calibrationDirection);
                calibrationTime = now;
                calibrationPhase = 1;
                break;

            case 1:
                if (now - calibrationTime > 1f && Settled())
                {
                    GoLean(calibrationDirection);
                    goProfile.Clear();
                    goProfileStep = Runner.DeltaTime;
                    calibrationOrigin = position;
                    calibrationTime = now;
                    calibrationPhase = 2;
                }
                break;

            case 2:
                goProfile.Add(Vector3.Dot(position - calibrationOrigin, calibrationDirection));
                if (now - calibrationTime >= 7f)
                {
                    calibrationPhase = -1;
                    SpreadToBalance();
                    Debug.Log($"{name}: 출발 곡선 측정 완료. 1초 {GoDistance(1f):F1}m, 2초 {GoDistance(2f):F1}m, 3초 {GoDistance(3f):F1}m, 4초 {GoDistance(4f):F1}m", this);
                }
                break;
        }
    }

    /// <summary>출발 신호 뒤 t초에 간 거리. 기록이 끝난 뒤는 마지막 속도로 이어 감.</summary>
    private float GoDistance(float t)
    {
        if (goProfile.Count < 2 || t <= 0f) return 0f;
        float index = t / goProfileStep;
        int i = Mathf.FloorToInt(index);
        if (i >= goProfile.Count - 1)
        {
            int n = goProfile.Count;
            float speed = (goProfile[n - 1] - goProfile[n - 2]) / goProfileStep;
            return goProfile[n - 1] + speed * (t - (n - 1) * goProfileStep);
        }
        return Mathf.Lerp(goProfile[i], goProfile[i + 1], index - i);
    }

    /// <summary>
    /// 지금부터 내다보며 맞지 않는 출발 시점을 찾음. 처음 나오는 안전한 구간의 가운데를 고름(예측 오차에 강함).
    /// 못 찾으면 가장 덜 위험한 시점.
    /// </summary>
    private float FindGoTime(float now, float planeDistance)
    {
        const float step = 0.05f;
        float firstSafe = -1f;
        float bestTime = now;
        float bestGap = float.NegativeInfinity;

        for (float t0 = now + 0.1f; t0 < now + gateSearchTime; t0 += step)
        {
            float gap = PredictGap(t0, planeDistance);
            if (gap > bestGap) { bestGap = gap; bestTime = t0; }

            if (gap > 0f)
            {
                if (firstSafe < 0f) firstSafe = t0;
            }
            else if (firstSafe >= 0f)
            {
                return (firstSafe + t0 - step) * 0.5f;
            }
        }
        return firstSafe >= 0f ? firstSafe : bestTime;
    }

    /// <summary>t0에 출발하면 해머와 가장 가까워질 때의 틈(m). 음수면 맞음.</summary>
    private float PredictGap(float t0, float planeDistance)
    {
        var shape = HammerShape.Of(gateHammer);
        if (shape == null) return 1f;

        Vector3 start = wheelBody.position;
        Vector3 plane = gateHammer.transform.position;
        float deckHeight = platformBody.position.y - wheelBody.position.y;
        float ballRadius = wheel.TryGetComponent<SphereCollider>(out var sc) ? sc.radius * wheel.transform.lossyScale.x : 5f;
        float deckRadius = platform.PlatformRadius;
        const float deckHalf = 0.7f;

        float minGap = float.PositiveInfinity;
        float step = 2f / 64f;
        for (float dt = 0f; dt < 10f; dt += step)
        {
            float s = GoDistance(dt);
            Vector3 c = start + gateDirection * s;
            float along = Vector3.Dot(Flat(c - plane), gateDirection);
            if (along < -25f) continue;
            if (along > 25f) break;

            shape.Pose(t0 + dt, out Vector3 pivot, out Vector3 head, out Vector3 endA, out Vector3 endB);
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(pivot, head)));
            int m = shape.hasHeadCapsule ? Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(endA, endB))) : 0;
            for (int k = 0; k <= n + m + 1; k++)
            {
                // 손잡이(축→머리 중심), 이어서 머리 캡슐(한쪽 끝→다른 끝)을 따라 점을 찍음
                Vector3 p;
                float r;
                if (k < n) { p = Vector3.Lerp(pivot, head, k / (float)n); r = shape.handleRadius; }
                else if (!shape.hasHeadCapsule) { if (k > n) break; p = head; r = shape.headRadius; }
                else { p = Vector3.Lerp(endA, endB, (k - n) / (float)(m + 1)); r = shape.headCapsuleRadius; }
                r += gateMargin;

                float ballGap = Vector3.Distance(p, c) - ballRadius - r;
                Vector3 deck = c + Vector3.up * deckHeight;
                float vertical = Mathf.Abs(p.y - deck.y) - deckHalf - r;
                float horizontal = Flat(p - deck).magnitude - deckRadius - r;
                float deckGap = Mathf.Max(vertical, horizontal);

                minGap = Mathf.Min(minGap, Mathf.Min(ballGap, deckGap));
            }
        }
        return minGap;
    }

    /// <summary>해머의 비공개 설정을 읽어 시간에 따른 손잡이·머리 위치를 계산함. Hammer와 같은 식.</summary>
    private class HammerShape
    {
        private static readonly Dictionary<Hammer, HammerShape> cache = new();
        private const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        private Hammer hammer;
        private Rigidbody arm;
        private Vector3 axis;
        private Quaternion rest;
        private Vector3 headOffset;
        private float amplitude, period, phase;
        public float headRadius, handleRadius;
        public bool hasHeadCapsule;
        private Vector3 headEndA, headEndB;
        public float headCapsuleRadius;

        public static HammerShape Of(Hammer h)
        {
            if (h == null) return null;
            if (cache.TryGetValue(h, out var shape) && shape.arm != null) return shape;

            T Get<T>(string field) => (T)typeof(Hammer).GetField(field, Flags).GetValue(h);
            shape = new HammerShape
            {
                hammer = h,
                arm = Get<Rigidbody>("_arm"),
                axis = Get<Vector3>("_swingAxis").normalized,
                rest = Get<Quaternion>("_armRestLocalRotation"),
                headOffset = Get<Vector3>("_headOffset"),
                amplitude = Get<float>("_amplitude"),
                period = Mathf.Max(0.01f, Get<float>("_period")),
                phase = Get<float>("_phaseOffset"),
                headRadius = Get<float>("_hitRadius"),
                handleRadius = Get<float>("_handleRadius"),
                hasHeadCapsule = Get<bool>("_hasHeadCapsule"),
                headEndA = Get<Vector3>("_headEndA"),
                headEndB = Get<Vector3>("_headEndB"),
                headCapsuleRadius = Get<float>("_headCapsuleRadius")
            };
            cache[h] = shape;
            return shape;
        }

        public void Pose(float time, out Vector3 pivot, out Vector3 head)
        {
            Pose(time, out pivot, out head, out _, out _);
        }

        /// <summary>머리 캡슐의 양 끝까지 구함. 캡슐이 없으면 양 끝 모두 머리 중심.</summary>
        public void Pose(float time, out Vector3 pivot, out Vector3 head, out Vector3 endA, out Vector3 endB)
        {
            float angle = amplitude * Mathf.Sin(2f * Mathf.PI * (time / period + phase));
            Quaternion rotation = hammer.transform.rotation * Quaternion.AngleAxis(angle, axis) * rest;
            pivot = arm.position;
            head = pivot + rotation * headOffset;
            endA = hasHeadCapsule ? pivot + rotation * headEndA : head;
            endB = hasHeadCapsule ? pivot + rotation * headEndB : head;
        }
    }

    // ---- 폭탄 피하기 ----

    /// <summary>판 위 폭탄 반경(+여유) 안이면 폭탄 반대쪽 경계로 자리를 옮김.</summary>
    private Vector2 AvoidBombs(Vector2 spot)
    {
        var bombs = FindObjectsByType<Bomb>(FindObjectsSortMode.None);
        if (bombs.Length == 0) return spot;

        Vector3 center = platformBody.position;
        float limit = DeckRadius * 0.85f;
        foreach (var b in bombs)
        {
            Vector3 o = b.transform.position - center;
            if (Mathf.Abs(o.y) > 4f) continue;
            var bomb = new Vector2(o.x, o.z);
            if (bomb.magnitude > DeckRadius + 2f) continue;

            float radius = BombRadius(b) + bombAvoidMargin;
            Vector2 away = spot - bomb;
            if (away.magnitude >= radius) continue;

            Vector2 dir = away.sqrMagnitude > 0.01f ? away.normalized
                        : (bomb.sqrMagnitude > 0.01f ? -bomb.normalized : Vector2.up);
            spot = bomb + dir * radius;
            if (spot.magnitude > limit)
            {
                // 가장자리를 넘으면 판 안쪽으로 당기되 폭탄 반대편 가장자리로 감
                spot = -bomb.normalized * limit;
            }
        }
        return Vector2.ClampMagnitude(spot, limit);
    }

    private static float BombRadius(Bomb b)
    {
        var f = typeof(Bomb).GetField("_radius", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return f != null && f.GetValue(b) is float r ? r : 4f;
    }

    /// <summary>트롤 작전을 무작위로 바꿈. 같은 작전만 반복하면 팀이 적응하기 쉬워 방해가 단조로움.</summary>
    private void ChangeTrollTactic(float now)
    {
        trollTactic = (TrollTactic)Random.Range(0, 3);
        trollSide = Random.value < 0.5f ? -1f : 1f;
        nextTacticTime = now + Random.Range(trollTacticTime.x, trollTacticTime.y);
    }

    /// <summary>
    /// 트롤이 설 자리. 던지기는 쓰지 않고 위치로만 방해함.
    ///   Counter: 팀이 기울이려는 반대쪽 가장자리. 가속과 제동을 모두 약하게 만듦.
    ///   Sideways: 목표 방향에 수직인 가장자리. 경로를 옆으로 휘게 만듦.
    ///   Center: 가운데에서 대기. 쉬는 척하다가 다음 작전으로 넘어감.
    /// 거리 제곱 영향 때문에 가장자리에 설수록 방해가 커짐.
    /// </summary>
    private Vector2 TrollSpot()
    {
        float edge = DeckRadius * 0.85f;
        Vector3 spot;

        switch (trollTactic)
        {
            case TrollTactic.Counter:
                Vector3 against = plannedLean.sqrMagnitude > 0.01f ? -plannedLean.normalized : -plannedDirection;
                spot = against * edge;
                break;

            case TrollTactic.Sideways:
                Vector3 side = new Vector3(-plannedDirection.z, 0f, plannedDirection.x) * trollSide;
                spot = side * edge;
                break;

            default:
                spot = Vector3.zero;
                break;
        }

        Vector2 noise = Random.insideUnitCircle * aimError;
        return Vector2.ClampMagnitude(new Vector2(spot.x + noise.x, spot.z + noise.y), edge);
    }

    /// <summary>도착 후 앞뒤좌우로 흩어져 균형을 잡음. 그대로 서 있으면 쏠린 쪽으로 다시 굴러가기 때문.</summary>
    private void SpreadToBalance()
    {
        float r = DeckRadius * 0.5f;
        Vector2[] spots = { new(0f, r), new(r, 0f), new(0f, -r), new(-r, 0f) };
        for (int i = 0; i < dummies.Count; i++)
        {
            Vector2 spot = spots[i % spots.Length];
            MoveDummy(i, spot.x, spot.y);
        }
    }

    /// <summary>
    /// i번째 더미가 설 자리. lean 지점을 중심으로 lean 방향에 수직인 줄로 나란히 섬. 위치 오차를 더함.
    /// 판 밖으로 나가지 않게 반경 안으로 자름.
    /// </summary>
    private Vector2 SlotFor(int index, int count, Vector3 lean)
    {
        Vector3 dir = lean.sqrMagnitude > 0.01f ? lean.normalized : Vector3.forward;
        Vector3 side = new Vector3(-dir.z, 0f, dir.x);
        Vector3 spot = lean + side * ((index - (count - 1) * 0.5f) * spacing);

        Vector2 noise = Random.insideUnitCircle * aimError;
        Vector2 result = new Vector2(spot.x + noise.x, spot.z + noise.y);
        return Vector2.ClampMagnitude(result, DeckRadius * 0.85f);
    }

    /// <summary>웨이포인트 표시용 기둥. 충돌 없음.</summary>
    private GameObject CreateMarker(Vector3 point, bool finish)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = finish ? "Course Finish" : "Course Waypoint";
        var col = marker.GetComponent<Collider>();
        col.enabled = false;
        Destroy(col);

        float groundY = wheelBody.position.y - 5f;
        if (Physics.Raycast(new Vector3(point.x, wheelBody.position.y + 50f, point.z), Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore))
            groundY = hit.point.y;

        marker.transform.position = new Vector3(point.x, groundY + 10f, point.z);
        marker.transform.localScale = new Vector3(waypointRadius * 0.4f, 10f, waypointRadius * 0.4f);

        var renderer = marker.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null) renderer.material = new Material(shader);
        renderer.material.color = finish ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.85f, 0.2f);
        return marker;
    }

    // ---- 인스펙터 메뉴 (플레이 중 Host에서 우클릭으로 실행) ----

    private bool CanRun()
    {
        if (Runner != null && Runner.IsRunning && Object.HasStateAuthority && platformBody != null) return true;
        Debug.LogWarning($"{name}: 플레이 중 Host를 누른 뒤에 실행하세요.", this);
        return false;
    }

    [ContextMenu("휠 리셋 (시작 위치로)")]
    private void MenuResetWheel()
    {
        if (CanRun()) ResetWheel();
    }

    [ContextMenu("더미 1명 가장자리")]
    private void MenuOneEdge()
    {
        if (!CanRun()) return;
        StopAllCoroutines();
        ClearDummies();
        PlaceDummy(0, 0f, DeckRadius * 0.8f);
    }

    [ContextMenu("더미 4명 한쪽 몰기")]
    private void MenuFourOneSide()
    {
        if (!CanRun()) return;
        StopAllCoroutines();
        ClearDummies();
        float z = DeckRadius * 0.75f;
        for (int i = 0; i < 4; i++) PlaceDummy(i, -3f + i * 2f, z);
    }

    [ContextMenu("더미 4명 흩어짐 (균형)")]
    private void MenuFourSpread()
    {
        if (!CanRun()) return;
        StopAllCoroutines();
        ClearDummies();
        float r = DeckRadius * 0.6f;
        PlaceDummy(0, 0f, r);
        PlaceDummy(1, r, 0f);
        PlaceDummy(2, 0f, -r);
        PlaceDummy(3, -r, 0f);
    }

    [ContextMenu("더미 4명 한 명씩 반대편으로 걷기")]
    private void MenuWalkAcross()
    {
        if (!CanRun()) return;
        StopAllCoroutines();
        MenuFourOneSide();
        StartCoroutine(WalkAcross());
    }

    /// <summary>한쪽에 몰린 4명이 1.5초 간격으로 한 명씩 반대편으로 걸어감. 속도는 캐릭터 이동 속도.</summary>
    private System.Collections.IEnumerator WalkAcross()
    {
        float z = DeckRadius * 0.75f;
        yield return new WaitForSeconds(2f);
        for (int i = 0; i < 4; i++)
        {
            MoveDummy(i, -3f + i * 2f, -z);
            yield return new WaitForSeconds(1.5f);
        }
    }

    [ContextMenu("실제 플레이어 무게 끄기/켜기")]
    private void MenuTogglePlayerWeight()
    {
        if (!CanRun()) return;
        foreach (var player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (!player.TryGetComponent<WeightSource>(out var source)) continue;
            source.Contributes = !source.Contributes;
            Debug.Log($"{name}: {player.name} 무게 {(source.Contributes ? "켜짐" : "꺼짐")}", this);
        }
    }

    [ContextMenu("전략 주행: 사각 코스 시작")]
    private void MenuStartCourse()
    {
        if (!CanRun()) return;
        StopAllCoroutines();
        StartCourse();
    }

    [ContextMenu("전략 주행: 트롤 1명 포함 시작")]
    private void MenuStartCourseWithTroll()
    {
        if (!CanRun()) return;
        StopAllCoroutines();
        StartCourse(withTroll: true);
    }

    [ContextMenu("전략 주행: 중지")]
    private void MenuStopCourse()
    {
        StopStrategy();
        if (strategyReport.Length > 0) Debug.Log($"{name}: 전략 주행 결과\n{strategyReport}", this);
    }

    [ContextMenu("더미 모두 제거")]
    private void MenuClear()
    {
        StopAllCoroutines();
        StopStrategy();
        ClearDummies();
    }

    [ContextMenu("기믹 코스: 주행")]
    private void MenuRunCourse()
    {
        if (CanRun()) RunCourse(false);
    }

    [ContextMenu("기믹 코스: 트롤 1명 포함 주행")]
    private void MenuRunCourseWithTroll()
    {
        if (CanRun()) RunCourse(true);
    }

    [ContextMenu("빙판 추격 시작")]
    private void IceChaseMenu() => StartIceChase();

    [ContextMenu("빙판 추격 중지")]
    private void IceChaseStopMenu() => StopIceChase();

    [ContextMenu("해머 반복 보기")]
    private void HammerLoopMenu() => RunHammerLoop(hammerLoopRounds);

    [ContextMenu("기록 시작")]
    private void MenuStartRecording()
    {
        if (CanRun()) StartRecording("manual_" + System.DateTime.Now.ToString("HHmmss"));
    }

    [ContextMenu("기록 종료")]
    private void MenuStopRecording()
    {
        string path = StopRecording();
        if (path != null) Debug.Log($"{name}: 기록 저장 {path}", this);
    }

    // ---- 기록 ----

    /// <summary>기록을 시작함. 이전 기록이 있으면 먼저 저장함.</summary>
    public void StartRecording(string name)
    {
        StopRecording();

        logName = string.IsNullOrEmpty(name) ? "run" : name;
        logStartTime = Runner != null ? (float)Runner.SimulationTime : 0f;
        stampTotal = 0;
        stampsThisTick = 0;
        log = new StringBuilder();
        log.AppendLine("t,tick,wheelX,wheelY,wheelZ,velX,velY,velZ,hSpeed,angSpeed,grounded," +
                       "tiltDeg,tiltNorm,downX,downZ,comX,comZ,comDist,totalWeight,dummiesOnDeck,waypoint,waypointDist,stamps,dummyDetail");
    }

    /// <summary>기록을 끝내고 CSV로 저장함. 저장한 경로를 돌려줌. 기록 중이 아니면 null.</summary>
    public string StopRecording()
    {
        if (log == null) return null;

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "BalanceTest"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, logName + ".csv");
        File.WriteAllText(path, log.ToString());

        log = null;
        return path;
    }

    /// <summary>
    /// 더미별 상태. "무게인식 땅닿음 판위섬 발-판높이(cm)"를 |로 이음. 예: 1 1 1 3|...
    /// 발-판 높이는 발 위 1m에서 아래로 쏴서 이 판 콜라이더까지 잰 값. 판을 못 찾으면 -.
    /// </summary>
    private string DummyDetail()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < dummies.Count; i++)
        {
            var d = dummies[i];
            if (i > 0) sb.Append('|');
            if (!d.IsValid) { sb.Append("x"); continue; }

            bool weighed = d.source != null && d.source.DirectPlatform == solver;
            bool standing = d.carrier != null && d.carrier.IsStandingOnDeck;
            sb.Append(weighed ? '1' : '0').Append(' ')
              .Append(d.cc.Grounded ? '1' : '0').Append(' ')
              .Append(standing ? '1' : '0').Append(' ');

            Vector3 foot = d.obj.transform.position;
            if (d.controller != null)
                foot = d.obj.transform.TransformPoint(d.controller.center) + Vector3.down * (d.controller.height * 0.5f);
            int count = Physics.RaycastNonAlloc(foot + Vector3.up, Vector3.down, hits, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int k = 0; k < count; k++)
                if (hits[k].collider.GetComponentInParent<PlatformTilt>() == platform) best = Mathf.Min(best, hits[k].distance - 1f);
            sb.Append(float.IsPositiveInfinity(best) ? "-" : Mathf.RoundToInt(best * 100f).ToString());
        }
        return sb.ToString();
    }

    /// <summary>물리 스텝이 끝난 뒤의 상태를 기록함.</summary>
    void IAfterTick.AfterTick()
    {
        if (!Object.HasStateAuthority || platformBody == null) return;
        WriteLogLine();
    }

    /// <summary>물리 스텝이 끝난 뒤의 휠 상태를 한 줄 기록함.</summary>
    private void WriteLogLine()
    {
        if (log == null || wheelBody == null || platform == null) return;

        Vector3 p = wheelBody.position;
        Vector3 v = wheelBody.linearVelocity;
        Vector3 down = platform.DownhillDirection;
        Vector3 com = solver != null ? solver.WorldOffset : Vector3.zero;

        int onDeck = 0;
        for (int i = 0; i < dummies.Count; i++)
            if (dummies[i].IsValid && dummies[i].source != null && dummies[i].source.DirectPlatform == solver) onDeck++;

        var c = CultureInfo.InvariantCulture;
        log.Append(((float)Runner.SimulationTime - logStartTime).ToString("F4", c)).Append(',')
           .Append(Runner.Tick.Raw).Append(',')
           .Append(p.x.ToString("F3", c)).Append(',').Append(p.y.ToString("F3", c)).Append(',').Append(p.z.ToString("F3", c)).Append(',')
           .Append(v.x.ToString("F3", c)).Append(',').Append(v.y.ToString("F3", c)).Append(',').Append(v.z.ToString("F3", c)).Append(',')
           .Append(new Vector2(v.x, v.z).magnitude.ToString("F3", c)).Append(',')
           .Append(wheelBody.angularVelocity.magnitude.ToString("F3", c)).Append(',')
           .Append(wheel.IsGrounded ? 1 : 0).Append(',')
           .Append(platform.TiltAngle.ToString("F2", c)).Append(',')
           .Append(platform.NormalizedTilt.ToString("F3", c)).Append(',')
           .Append(down.x.ToString("F3", c)).Append(',').Append(down.z.ToString("F3", c)).Append(',')
           .Append(com.x.ToString("F3", c)).Append(',').Append(com.z.ToString("F3", c)).Append(',')
           .Append(new Vector2(com.x, com.z).magnitude.ToString("F3", c)).Append(',')
           .Append(solver != null ? solver.TotalWeight.ToString("F2", c) : "0").Append(',')
           .Append(onDeck).Append(',')
           .Append(strategyActive ? waypointIndex + 1 : 0).Append(',')
           .Append(strategyActive && waypoints.Count > 0
               ? Vector3.Distance(new Vector3(p.x, 0f, p.z), waypoints[waypointIndex]).ToString("F2", c)
               : "0").Append(',')
           .Append(stampsThisTick).Append(',')
           .Append(DummyDetail()).AppendLine();
        stampsThisTick = 0;
    }
}
