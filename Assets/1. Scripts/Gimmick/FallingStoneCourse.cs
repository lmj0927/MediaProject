using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>같은 비탈의 두 경로로 돌을 굴리고, Transport에 닿으면 Host가 한 번만 밀어낸다.</summary>
[DefaultExecutionOrder(110)]
[RequireComponent(typeof(NetworkObject))]
public sealed class FallingStoneCourse : NetworkBehaviour
{
    [SerializeField] private NetworkPrefabRef _stonePrefab;
    [SerializeField] private PlatformTilt _transport;

    [Header("Drop Paths")]
    [SerializeField] private Vector3 _secondLaneOffset;
    [SerializeField] private Vector3 _travel;
    [Min(0.1f)][SerializeField] private float _rollSeconds = 3.5f;
    [Tooltip("왼쪽과 오른쪽 경로를 번갈아 사용하는 낙하 간격(초).")]
    [Min(0.1f)][SerializeField] private float _dropInterval = 5f;

    [Header("Impact")]
    [Min(0.01f)][SerializeField] private float _stoneRadius = 12.5f;
    [Tooltip("돌이 진행한 수평 방향으로 Transport에 더하는 속도(m/s).")]
    [Min(0f)][SerializeField] private float _knockbackSpeed = 12f;

    private readonly List<Stone> _stones = new();
    private readonly Collider[] _hits = new Collider[32];
    private SphereDriver _wheel;
    private TickTimer _nextDrop;
    private bool _secondLane;

    private struct Stone
    {
        public NetworkObject Object;
        public Vector3 Start;
        public int SpawnTick;
    }

    public override void Spawned()
    {
        if (!HasStateAuthority) return;

        _wheel = _transport.Sphere.GetComponent<SphereDriver>();
        _stones.Clear();
        _secondLane = false;
        ScheduleDrop();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !Runner.IsForward) return;

        if (_nextDrop.Expired(Runner))
        {
            Vector3 start = _secondLane ? transform.TransformPoint(_secondLaneOffset) : transform.position;
            var spawned = Runner.Spawn(_stonePrefab, start, transform.rotation);
            _stones.Add(new Stone { Object = spawned, Start = start, SpawnTick = Runner.Tick.Raw });
            _secondLane = !_secondLane;
            ScheduleDrop();
        }

        Vector3 travel = transform.TransformVector(_travel);
        Vector3 direction = Vector3.ProjectOnPlane(travel, Vector3.up).normalized;
        Vector3 rollAxis = Vector3.Cross(Vector3.up, direction);

        for (int i = _stones.Count - 1; i >= 0; i--)
        {
            Stone stone = _stones[i];
            float elapsed = (Runner.Tick.Raw - stone.SpawnTick) * Runner.DeltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / _rollSeconds);
            Vector3 next = stone.Start + travel * progress;

            bool hit = HitsTransport(stone.Object.transform.position, next);
            if (hit || elapsed >= _rollSeconds)
            {
                if (hit) _wheel.ApplyKnockback(direction * _knockbackSpeed);
                Runner.Despawn(stone.Object);
                _stones.RemoveAt(i);
                continue;
            }

            Quaternion rotation = Quaternion.AngleAxis(360f * progress, rollAxis) * transform.rotation;
            stone.Object.transform.SetPositionAndRotation(next, rotation);
        }
    }

    private bool HitsTransport(Vector3 from, Vector3 to)
    {
        int count = Physics.OverlapCapsuleNonAlloc(from, to, _stoneRadius, _hits, ~0, QueryTriggerInteraction.Ignore);
        Collider[] hits = _hits;
        if (count == _hits.Length)
        {
            hits = Physics.OverlapCapsule(from, to, _stoneRadius, ~0, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }

        for (int i = 0; i < count; i++)
        {
            Transform target = hits[i].transform;
            if (target.IsChildOf(_transport.transform) || target.IsChildOf(_wheel.transform)) return true;
        }

        return false;
    }

    private void ScheduleDrop()
    {
        _nextDrop = TickTimer.CreateFromSeconds(Runner, _dropInterval);
    }
}
