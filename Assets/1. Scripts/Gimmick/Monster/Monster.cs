using Fusion;
using UnityEngine;

/// <summary>
/// 판 위를 배회하며 무게중심을 흔드는 몬스터. 플레이어가 잡아서 판 밖으로 던져 처리한다.
/// 몸체(잡기/던지기/판 캐리/폭발 넉백/낙하 Despawn)는 같은 오브젝트의 HoldableProp + WheelRider가 담당.
/// Host SA FUN: 판 로컬 2차 베지어(P0→P1→P2)를 호 길이 속도로 따라감. 끝나면 바로 다음 곡선 (대기 없음).
/// 제어점 3개가 모두 배회 원 안에 있으면 곡선 전체가 원 안에 남음(볼록집합) → 스스로 떨어지지 않음.
/// 잡힘/공중 중에는 멈추고, 판에 착지하면 잠깐 쉰 뒤 다시 배회.
/// 위치·회전 표시는 HoldableProp, Animator는 Speed/Grounded만 복제 값으로 구동.
/// PlatformTilt(-50) 이후, HoldableProp(50) 이전에 돌아야 이번 틱 걷기 속도가 WheelRider.Simulate에 반영됨.
/// </summary>
[DefaultExecutionOrder(45)]
[RequireComponent(typeof(HoldableProp))]
[RequireComponent(typeof(WheelRider))]
public class Monster : NetworkBehaviour
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");

    private enum WanderState { ResumePause, Move }

    [Header("Wander")]
    [Tooltip("걷기 속도(m/s). 베지어 호 길이를 이 속도로 따라감.")]
    [SerializeField] private float _moveSpeed = 1.5f;

    [Tooltip("회전 속도(도/s). 곡선 접선 방향을 따라감.")]
    [SerializeField] private float _turnSpeed = 540f;

    [Tooltip("판 반경에 곱한 원 안에서만 제어점을 고름. 1보다 작아야 가장자리에서 떨어지지 않음.")]
    [Range(0.1f, 0.95f)]
    [SerializeField] private float _wanderRadiusScale = 0.7f;

    [Tooltip("끝점 P2는 시작점 P0에서 최소 이 거리.")]
    [SerializeField] private float _minPathDistance = 1f;

    [Tooltip("현(P0–P2) 길이에 곱해 제어점 P1을 옆으로 밀어 곡률을 만듦. 클수록 더 휘어짐.")]
    [Range(0f, 1.5f)]
    [SerializeField] private float _curveBulge = 0.55f;

    [Tooltip("이 시간 안에 곡선을 끝내지 못하면(막힘 등) 새 곡선을 고름.")]
    [SerializeField] private float _maxPathSeconds = 8f;

    [Tooltip("놓이거나 던져진 뒤 판에 착지하면 이 시간 동안만 쉼.")]
    [SerializeField] private float _landPauseSeconds = 0.5f;

    private const int TargetPickAttempts = 8;
    private const int LengthSampleCount = 16;

    private HoldableProp _prop;
    private WheelRider _rider;
    private Rigidbody _rigidbody;
    private Animator _animator;

    // Host SA 전용 배회 상태 (복제 안 함)
    private WanderState _state;
    private TickTimer _pathTimer;
    private bool _hasPath;
    private bool _wasWandering;

    private Vector3 _localP0;
    private Vector3 _localP1;
    private Vector3 _localP2;
    private float _pathLength;
    private float _distanceAlong;
    private Vector3 _lastTangentLocal = Vector3.forward;

    private float _yaw;

    [Networked] private float AnimSpeed { get; set; }
    [Networked] private NetworkBool GroundedNet { get; set; }

    private Vector3 BodyPosition => _rigidbody != null ? _rigidbody.position : transform.position;

    private void Awake()
    {
        _prop = GetComponent<HoldableProp>();
        _rider = GetComponent<WheelRider>();
        _rigidbody = GetComponent<Rigidbody>();
        _animator = GetComponentInChildren<Animator>();

        if (_rigidbody != null)
            _rigidbody.constraints |= RigidbodyConstraints.FreezeRotation;
    }

    public override void Spawned()
    {
        if (!Object.HasStateAuthority)
            return;

        _yaw = FlatYaw(transform.forward, transform.eulerAngles.y);
        GroundedNet = true;
        _wasWandering = false;
        _hasPath = false;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        if (_prop.IsHeld)
        {
            StopWandering(grounded: true);
            return;
        }

        var platform = _rider.CurrentPlatform;
        if (_rider.IsThrownFlight || !_rider.IsOnPlatform || platform == null)
        {
            StopWandering(grounded: false);
            return;
        }

        GroundedNet = true;

        if (!_wasWandering)
        {
            _wasWandering = true;
            _hasPath = false;
            _yaw = FlatYaw(transform.forward, _yaw);
            EnterResumePause(_landPauseSeconds);
        }

        platform.GetStepTargetPose(out var platformPosition, out var platformRotation);
        Vector3 move = TickWander(platform, platformPosition, platformRotation);

        _rider.SetSelfMoveVelocity(move);
        AnimSpeed = move.magnitude;

        if (move.sqrMagnitude > 1e-6f)
            _yaw = Mathf.MoveTowardsAngle(_yaw, FlatYaw(move, _yaw), _turnSpeed * Runner.DeltaTime);

        ApplyYawToBody();
    }

    private Vector3 TickWander(PlatformTilt platform, Vector3 platformPosition, Quaternion platformRotation)
    {
        if (_state == WanderState.ResumePause)
        {
            if (!_pathTimer.ExpiredOrNotRunning(Runner))
                return Vector3.zero;

            BeginNewPath(platform, platformPosition, platformRotation);
        }

        if (!_hasPath)
            BeginNewPath(platform, platformPosition, platformRotation);

        float dt = Runner.DeltaTime;
        _distanceAlong += _moveSpeed * dt;

        if (_distanceAlong >= _pathLength || _pathTimer.Expired(Runner))
        {
            BeginNewPath(platform, platformPosition, platformRotation);
            if (!_hasPath || _pathLength < 1e-4f)
                return Vector3.zero;
            _distanceAlong = Mathf.Min(_distanceAlong, _pathLength * 0.01f);
        }

        float t = Mathf.Clamp01(_distanceAlong / Mathf.Max(_pathLength, 1e-4f));
        Vector3 tangentLocal = BezierTangent(_localP0, _localP1, _localP2, t);
        tangentLocal.y = 0f;
        if (tangentLocal.sqrMagnitude < 1e-6f)
            tangentLocal = _lastTangentLocal;
        else
            _lastTangentLocal = tangentLocal.normalized;

        // 곡선에서 살짝 벗어나면 목표점 쪽으로도 끌어당겨 경로를 유지
        Vector3 onCurveLocal = BezierPoint(_localP0, _localP1, _localP2, t);
        Vector3 onCurveWorld = platformPosition + platformRotation * onCurveLocal;
        Vector3 toCurve = onCurveWorld - BodyPosition;
        toCurve.y = 0f;

        Vector3 along = platformRotation * _lastTangentLocal;
        along.y = 0f;
        if (along.sqrMagnitude > 1e-6f)
            along.Normalize();

        Vector3 steer = along;
        if (toCurve.sqrMagnitude > 0.01f)
            steer = (along + toCurve.normalized * 0.35f).normalized;

        return steer * _moveSpeed;
    }

    private void BeginNewPath(PlatformTilt platform, Vector3 platformPosition, Quaternion platformRotation)
    {
        float radius = platform.PlatformRadius * _wanderRadiusScale;
        float minDistance = Mathf.Min(_minPathDistance, radius);
        Vector3 currentLocal = Quaternion.Inverse(platformRotation) * (BodyPosition - platformPosition);
        currentLocal = ClampToDisk(currentLocal, radius);

        Vector3 end = currentLocal;
        for (int i = 0; i < TargetPickAttempts; i++)
        {
            Vector2 point = Random.insideUnitCircle * radius;
            end = new Vector3(point.x, currentLocal.y, point.y);
            Vector3 offset = end - currentLocal;
            offset.y = 0f;
            if (offset.magnitude >= minDistance)
                break;
        }

        end = ClampToDisk(end, radius);

        Vector3 chord = end - currentLocal;
        chord.y = 0f;
        Vector3 mid = currentLocal + chord * 0.5f;
        Vector3 side = Vector3.Cross(Vector3.up, chord.sqrMagnitude > 1e-4f ? chord.normalized : _lastTangentLocal);
        if (side.sqrMagnitude < 1e-6f)
            side = Vector3.right;
        side.Normalize();

        float bulge = chord.magnitude * _curveBulge * Random.Range(0.4f, 1f);
        if (Random.value < 0.5f)
            bulge = -bulge;

        // 이전 접선과 맞추면 곡선이 이어져 보임: 제어점을 진행 방향 쪽으로도 살짝 밈
        Vector3 control = mid + side * bulge;
        if (_lastTangentLocal.sqrMagnitude > 1e-4f)
            control += _lastTangentLocal.normalized * (chord.magnitude * 0.15f);
        control.y = currentLocal.y;
        control = ClampToDisk(control, radius);

        _localP0 = currentLocal;
        _localP1 = control;
        _localP2 = end;
        _pathLength = EstimateBezierLength(_localP0, _localP1, _localP2);
        _distanceAlong = 0f;
        _hasPath = _pathLength > 0.05f;
        _state = WanderState.Move;
        _pathTimer = TickTimer.CreateFromSeconds(Runner, _maxPathSeconds);

        if (_hasPath)
        {
            Vector3 startTangent = BezierTangent(_localP0, _localP1, _localP2, 0f);
            startTangent.y = 0f;
            if (startTangent.sqrMagnitude > 1e-6f)
                _lastTangentLocal = startTangent.normalized;
        }
    }

    private void EnterResumePause(float seconds)
    {
        _state = WanderState.ResumePause;
        _hasPath = false;
        float pause = Mathf.Max(0f, seconds);
        _pathTimer = pause > 0f
            ? TickTimer.CreateFromSeconds(Runner, pause)
            : TickTimer.None;
    }

    private void StopWandering(bool grounded)
    {
        _wasWandering = false;
        _hasPath = false;
        AnimSpeed = 0f;
        GroundedNet = grounded;
    }

    private void ApplyYawToBody()
    {
        var rotation = Quaternion.Euler(0f, _yaw, 0f);
        if (_rigidbody != null)
        {
            _rigidbody.rotation = rotation;
            _rigidbody.angularVelocity = Vector3.zero;
        }
        else
        {
            transform.rotation = rotation;
        }
    }

    public override void Render()
    {
        UpdateAnimator();
    }

    private void UpdateAnimator()
    {
        if (_animator == null)
            return;

        _animator.SetFloat(SpeedHash, AnimSpeed);
        _animator.SetBool(GroundedHash, GroundedNet);
    }

    private static Vector3 BezierPoint(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }

    private static Vector3 BezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        return 2f * (1f - t) * (p1 - p0) + 2f * t * (p2 - p1);
    }

    private static float EstimateBezierLength(Vector3 p0, Vector3 p1, Vector3 p2)
    {
        float length = 0f;
        Vector3 prev = p0;
        for (int i = 1; i <= LengthSampleCount; i++)
        {
            float t = i / (float)LengthSampleCount;
            Vector3 point = BezierPoint(p0, p1, p2, t);
            Vector3 delta = point - prev;
            delta.y = 0f;
            length += delta.magnitude;
            prev = point;
        }

        return Mathf.Max(length, 0.01f);
    }

    private static Vector3 ClampToDisk(Vector3 local, float radius)
    {
        Vector3 flat = local;
        flat.y = local.y;
        Vector2 xz = new(local.x, local.z);
        float max = Mathf.Max(0.01f, radius);
        if (xz.sqrMagnitude > max * max)
        {
            xz = xz.normalized * max;
            flat.x = xz.x;
            flat.z = xz.y;
        }

        return flat;
    }

    private static float FlatYaw(Vector3 direction, float fallback)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-6f)
            return fallback;
        return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!_hasPath || !Application.isPlaying)
            return;

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        Vector3 prev = transform.position;
        var plat = _rider != null ? _rider.CurrentPlatform : null;
        if (plat == null)
            return;

        plat.GetStepTargetPose(out var platformPosition, out var platformRotation);
        prev = platformPosition + platformRotation * _localP0;
        for (int i = 1; i <= LengthSampleCount; i++)
        {
            float t = i / (float)LengthSampleCount;
            Vector3 next = platformPosition + platformRotation * BezierPoint(_localP0, _localP1, _localP2, t);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(platformPosition + platformRotation * _localP0, 0.08f);
        Gizmos.DrawWireSphere(platformPosition + platformRotation * _localP1, 0.08f);
        Gizmos.DrawWireSphere(platformPosition + platformRotation * _localP2, 0.08f);
    }
#endif
}
