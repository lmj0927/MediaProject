using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using ithappy;

/// <summary>
/// Host 전용: Unity 자동 물리를 끄고 매 틱 모든 FixedUpdateNetwork 뒤에 Physics.Simulate를 한 번 호출한다.
/// FUN(캐리/토크/판 포즈)과 PhysX 스텝이 같은 틱에 1:1로 묶인다.
/// Client는 자동 물리를 그대로 둔다 (공유 휠·Prop 예측 없음).
/// </summary>
[DefaultExecutionOrder(1000)]
public class HostPhysicsStepper : SimulationBehaviour
{
    private bool _active;
    private SimulationMode _originalMode;
    private int _lastSimulatedTick = -1;
    private ObstacleMotionRegistry _obstacles;

    public void SetObstacleMotions(ObstacleMotionRegistry obstacles) => _obstacles = obstacles;

    public override void FixedUpdateNetwork()
    {
        if (Runner == null || !Runner.IsServer || !Runner.IsForward)
            return;

        if (!_active)
        {
            _originalMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            _active = true;
        }

        int tick = Runner.Tick.Raw;
        if (tick == _lastSimulatedTick)
            return;

        _lastSimulatedTick = tick;
        _obstacles?.Simulate(Runner.DeltaTime);
        Physics.SyncTransforms();
        Physics.Simulate(Runner.DeltaTime);
        _obstacles?.ResolveContacts();
    }

    private void OnDisable() => Restore();

    private void OnDestroy() => Restore();

    private void Restore()
    {
        if (!_active)
            return;

        Physics.simulationMode = _originalMode;
        _active = false;
    }
}

/// <summary>NetworkTransform이 붙은 에셋 장애물만 Host 틱에서 움직인다.</summary>
public sealed class ObstacleMotionRegistry
{
    private readonly List<Motion> motions = new();
    private readonly HashSet<Transform> captured = new();
    private readonly RotatingObstacleContacts contacts = new();

    public void CaptureLoadedScenes()
    {
        contacts.CaptureLoadedScenes();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var nt in root.GetComponentsInChildren<NetworkTransform>(true))
            {
                var t = nt.transform;
                if (!t.name.StartsWith("obstacle_") || !captured.Add(t)) continue;
                var position = t.GetComponent<OscillatePosition>();
                var rotation = t.GetComponent<OscillateRotation>();
                var spin = t.GetComponent<RotationScript>();
                if (position == null && rotation == null && spin == null) continue;
                motions.Add(new Motion(t, position, rotation, spin));
            }
        }
    }

    public void Simulate(float dt)
    {
        foreach (var motion in motions) motion.Simulate(dt, contacts);
    }

    public void ResolveContacts()
    {
        foreach (var motion in motions) motion.ResolveContacts(contacts);
    }

    public void Restore()
    {
        foreach (var motion in motions) motion.Restore();
    }

    private sealed class Motion
    {
        private readonly Transform target;
        private readonly OscillatePosition position;
        private readonly OscillateRotation rotation;
        private readonly RotationScript spin;
        private readonly Vector3 startPosition;
        private readonly Quaternion startRotation;
        private readonly bool positionEnabled, rotationEnabled, spinEnabled;
        private float elapsed;
        private float delay;
        private bool initialized;
        private readonly Collider[] colliders;
        private readonly float radius;

        public Motion(Transform target, OscillatePosition position, OscillateRotation rotation, RotationScript spin)
        {
            this.target = target;
            this.position = position;
            this.rotation = rotation;
            this.spin = spin;
            startPosition = target.position;
            startRotation = target.rotation;
            if (position != null) { positionEnabled = position.enabled; position.enabled = false; }
            if (rotation != null) { rotationEnabled = rotation.enabled; rotation.enabled = false; }
            if (spin != null) { spinEnabled = spin.enabled; spin.enabled = false; }
            if (rotationEnabled || spinEnabled)
            {
                var solids = new List<Collider>();
                foreach (var c in target.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger || !c.enabled) continue;
                    solids.Add(c);
                    radius = Mathf.Max(radius, Vector3.Distance(c.bounds.center, target.position) + c.bounds.extents.magnitude);
                }
                colliders = solids.ToArray();
            }
            else colliders = System.Array.Empty<Collider>();
        }

        public void Simulate(float dt, RotatingObstacleContacts contacts)
        {
            if (target == null) return;
            if (!initialized)
            {
                if (positionEnabled && position.useRandomDelay) delay = Random.Range(0f, position.maxRandomDelay);
                if (rotationEnabled && rotation.useRandomDelay) delay = Random.Range(0f, rotation.maxRandomDelay);
                initialized = true;
            }

            Vector3 nextPosition = positionEnabled
                ? startPosition + position.moveAxis.normalized * position.moveDistance * Progress(position.duration) : target.position;
            Quaternion nextRotation = rotationEnabled
                ? startRotation * Quaternion.AngleAxis(rotation.rotationAngle * Progress(rotation.duration), rotation.rotationAxis) : target.rotation;
            Vector3 spinAxis = Vector3.zero;
            float spinDegrees = 0f;
            if (spinEnabled)
            {
                spinAxis = spin.rotationAxis == RotationScript.RotationAxis.X ? Vector3.right :
                    spin.rotationAxis == RotationScript.RotationAxis.Y ? Vector3.up : Vector3.forward;
                spinDegrees = spin.rotationSpeed * dt;
            }
            if (colliders.Length > 0) contacts.Move(target, nextPosition, nextRotation, colliders, radius, spinAxis, spinDegrees);
            else target.SetPositionAndRotation(nextPosition, nextRotation * Quaternion.AngleAxis(spinDegrees, spinAxis));
            elapsed += dt;
        }

        public void ResolveContacts(RotatingObstacleContacts contacts)
        {
            if (target != null && colliders.Length > 0) contacts.Resolve(target.position, radius, colliders, false);
        }

        private float Progress(float duration)
        {
            if (elapsed < delay || duration <= 0f) return 0f;
            float p = Mathf.PingPong((elapsed - delay) * 2f / duration, 1f);
            return p < 0.5f ? 4f * p * p * p : 1f - Mathf.Pow(-2f * p + 2f, 3f) * 0.5f;
        }

        public void Restore()
        {
            if (position != null) position.enabled = positionEnabled;
            if (rotation != null) rotation.enabled = rotationEnabled;
            if (spin != null) spin.enabled = spinEnabled;
        }
    }
}
