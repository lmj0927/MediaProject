using System;
using System.IO;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicitly started, Host-only map test. No force/pose overrides during a run.</summary>
[DefaultExecutionOrder(-200)]
public class TransportTraversalTest : SimulationBehaviour
{
    [Serializable] public class Route { public Vector3[] points; public float speed = 12f; public float clockSpeed = 1f; public float sectionStartZ, sectionEndZ; public bool requireGimmickUse = true; }
    public static readonly string Folder = Path.Combine(Application.dataPath, "../.ai/Traversal");
    public static TransportTraversalTest Active { get; private set; }
    public string Result { get; private set; } = "Starting";
    public float Elapsed { get; private set; }
    public float BestZ { get; private set; }
    public string RunDirectory => runDirectory;
    private SphereDriver sphere;
    private Rigidbody body;
    private PlatformTilt platform;
    private CenterOfMassSolver solver;
    private Route route;
    private Vector3 bias;
    private Vector3 finish;
    private Vector3 sectionSpawn;
    private bool sceneReady;
    private float lastProgress, nextSample, finishTime;
    private string runDirectory;
    private StreamWriter telemetry;
    private ObstacleMotionRegistry motions;
    private TraversalContacts[] probes;
    private readonly System.Collections.Generic.List<PadUse> padUses = new();
    private readonly System.Collections.Generic.List<MovingUse> movingUses = new();
    private readonly System.Collections.Generic.Dictionary<Collider, MovingUse> movingColliders = new();
    private readonly System.Collections.Generic.HashSet<MovingUse> supportedRows = new();
    private readonly RaycastHit[] supportHits = new RaycastHit[32];
    private Hammer[] hammers;
    private float hammerCommitUntil;
    private float movingCommitUntil;
    private bool rotorExitAligned;
    private bool logAligned;

    public static async void StartTest(float clockSpeed = 0f, float startZ = 0f, float endZ = 0f)
    {
        if (!Application.isPlaying || Active != null) return;
        Directory.CreateDirectory(Folder);
        var scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        if (UnityEditor.AssetDatabase.AssetPathToGUID(scene.path) != RoomSession.GameSceneGuid)
            throw new InvalidOperationException("Open the game map before starting traversal.");
#endif
        var go = new GameObject("Transport Traversal Test (runtime only)");
        var test = go.AddComponent<TransportTraversalTest>();
        Active = test;
        test.runDirectory = Path.Combine(Folder, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(test.runDirectory);
        File.WriteAllText(Path.Combine(test.runDirectory, "scene-sha256.txt"),
            BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(scene.path))).Replace("-", ""));
        string routePath = Path.Combine(Folder, "route.json");
        if (!File.Exists(routePath))
            routePath = Path.Combine(Application.dataPath, "../Tools/Traversal/route.json");
        test.route = JsonUtility.FromJson<Route>(File.ReadAllText(routePath));
        if (clockSpeed > 0f) test.route.clockSpeed = clockSpeed;
        test.route.sectionStartZ = startZ;
        test.route.sectionEndZ = endZ;
        test.finish = test.route.points[test.route.points.Length - 1];
        File.WriteAllText(Path.Combine(test.runDirectory, "route.json"), JsonUtility.ToJson(test.route, true));
        using (var hash = System.Security.Cryptography.SHA256.Create())
            File.WriteAllLines(Path.Combine(test.runDirectory, "script-hashes.txt"),
                Directory.GetFiles(Path.Combine(Application.dataPath, "1. Scripts"), "*.cs", SearchOption.AllDirectories)
                    .OrderBy(path => path).Select(path => path + " " + BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "")));
        File.WriteAllText(Path.Combine(test.runDirectory, "wheel-configs.json"), "[" +
            string.Join(",", Resources.FindObjectsOfTypeAll<WheelConfig>().Select(c => JsonUtility.ToJson(c))) + "]");
        test.sphere = FindFirstObjectByType<SphereDriver>();
        test.body = test.sphere.GetComponent<Rigidbody>();
        test.platform = FindFirstObjectByType<PlatformTilt>();
        test.solver = test.platform.GetComponent<CenterOfMassSolver>();
        test.hammers = FindObjectsByType<Hammer>(FindObjectsSortMode.None).OrderBy(h => h.transform.position.z).ToArray();
        foreach (var pad in FindObjectsByType<JumpPad>(FindObjectsSortMode.None)) {
            test.padUses.Add(new PadUse { pad = pad, position = pad.transform.position,
                id = "JumpPad@" + pad.transform.position.z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                required = startZ <= 0f || (pad.transform.position.z >= startZ && pad.transform.position.z <= endZ) });
            pad.Launched += test.RecordLaunch;
        }
        foreach (var group in FindObjectsByType<ithappy.OscillateRotation>(FindObjectsSortMode.None)
            .Where(s => s.name == "obstacle_11_002" || s.name == "obstacle_8_002").GroupBy(s => Mathf.RoundToInt(s.transform.position.z * 10f))) {
            var panel = group.First();
            bool trapdoor = panel.name == "obstacle_8_002";
            float z = trapdoor ? panel.GetComponent<Collider>().bounds.center.z : panel.transform.position.z;
            var use = new MovingUse { id = "MovingRow@" + z.ToString("F1", System.Globalization.CultureInfo.InvariantCulture), z = z,
                trapdoor = trapdoor, panel = panel.transform, closedRotation = panel.transform.rotation,
                required = startZ <= 0f || (z >= startZ && z <= endZ) };
            test.movingUses.Add(use);
            foreach (var s in group) foreach (var c in s.GetComponentsInChildren<Collider>())
                if (!c.isTrigger && c.enabled) test.movingColliders[c] = use;
        }
        File.WriteAllText(Path.Combine(test.runDirectory, "jump-pad-configs.json"), "[" +
            string.Join(",", test.padUses.Select(p => JsonUtility.ToJson(p.pad))) + "]");
        test.probes = test.sphere.transform.root.GetComponentsInChildren<Rigidbody>()
            .Select(rb => rb.gameObject.AddComponent<TraversalContacts>()).ToArray();
        test.BestZ = test.body.position.z;
        // Existing avatars are not spawned: this runner has no BasicSpawner callbacks.
        foreach (var spawner in FindObjectsByType<BasicSpawner>(FindObjectsSortMode.None)) spawner.enabled = false;
        test.motions = new ObstacleMotionRegistry();
        test.motions.CaptureLoadedScenes();
        var runner = go.AddComponent<NetworkRunner>();
        runner.ProvideInput = false;
        var clock = go.AddComponent<TraversalClock>();
        clock.Speed = test.route.clockSpeed;
        var manager = go.AddComponent<TraversalSceneManager>();
        manager.ScenePath = scene.path;
        var result = await runner.StartGame(new StartGameArgs {
            GameMode = GameMode.Host,
            SessionName = "Traversal-" + Guid.NewGuid().ToString("N"),
            IsVisible = false, IsOpen = false, PlayerCount = 1,
            Scene = SceneRef.FromIndex(0), SceneManager = manager, Updater = clock
        });
        if (test == null || !Application.isPlaying) return;
        if (!result.Ok) { test.Complete("HostStartFailed: " + result); return; }
        if (startZ > 0f) {
            if (!test.TryRoadPoint(startZ, out var start) || !test.TryRoadPoint(endZ, out test.finish)) {
                test.Complete("DiagnosticSetupFailed: no road at section endpoint"); return;
            }
            test.sectionSpawn = start + Vector3.up * (test.platform.SphereRadius + 0.15f);
        }
        var stepper = go.AddComponent<HostPhysicsStepper>();
        stepper.SetObstacleMotions(test.motions);
        if (stepper.Runner == null) runner.AddGlobal(stepper);
        test.telemetry = new StreamWriter(Path.Combine(test.runDirectory, "telemetry.jsonl")) { AutoFlush = true };
        if (test.Runner == null) runner.AddGlobal(test);
        test.Result = "Running";
        test.WriteStatus();
    }

    public override void FixedUpdateNetwork()
    {
        if (Result != "Running" || !Runner.IsServer || !Runner.IsForward) return;
        if (!sceneReady) {
            if (sphere.Object == null || platform.Object == null || !sphere.Object.IsValid || !platform.Object.IsValid) return;
            if (route.sectionStartZ > 0f) {
                sphere.GetComponent<NetworkTransform>().Teleport(sectionSpawn, Quaternion.identity);
                sphere.transform.SetPositionAndRotation(sectionSpawn, Quaternion.identity);
                body.position = sectionSpawn; body.rotation = Quaternion.identity;
                body.linearVelocity = body.angularVelocity = Vector3.zero;
                platform.GetComponent<Rigidbody>().position = sectionSpawn + Vector3.up * platform.HeightOffset;
                BestZ = route.sectionStartZ;
            }
            sceneReady = true;
        }
        float dt = Runner.DeltaTime;
        if (Elapsed == 0f && route.sectionStartZ > 0f && Mathf.Abs(body.position.z - route.sectionStartZ) > 5f) {
            Complete("DiagnosticSpawnFailed"); return;
        }
        Elapsed += dt;
        UpdateGimmickUse(dt);
        Vector3 p = body.position;
        if (p.z > BestZ + 1f) { BestZ = p.z; lastProgress = Elapsed; }
        int index = 0;
        while (index < route.points.Length - 2 && route.points[index + 1].z < p.z) index++;
        Vector3 a = route.points[index], b = route.points[index + 1];
        float t = Mathf.InverseLerp(a.z, b.z, p.z);
        float targetX = Mathf.Lerp(a.x, b.x, t);
        var nextPad = padUses.Where(use => use.required && !use.launched && use.position.z >= p.z - 15f)
            .OrderBy(use => use.position.z).FirstOrDefault();
        if (route.requireGimmickUse && nextPad != null && nextPad.position.z - p.z < 100f) targetX = nextPad.position.x;
        Vector3 end = finish;
        float remaining = end.z - p.z;
        float laneSpeed = route.speed * Mathf.Clamp01((15f - Mathf.Abs(targetX - p.x)) / 10f);
        if (p.z > 3070f && p.z < 3300f) laneSpeed = Mathf.Min(laneSpeed, 6f);
        Vector3 desired = new Vector3(Mathf.Clamp((targetX - p.x) * 0.3f, -6f, 6f), 0,
            Mathf.Clamp(remaining * 0.7f, -6f, laneSpeed));
        ApplyHammerGate(p, ref desired);
        ApplyMovingGate(p, ref desired);
        // Finish the lift exit before changing lanes for the following rotating blades.
        if (p.z > 3297f && p.z < 3330f && Mathf.Abs(p.x - targetX) > 4f)
            desired.z = Mathf.Clamp((3305f - p.z) * .7f, -4f, desired.z);
        // Turn toward the narrow log bridge while still on the rotor island.
        if (p.z > 2856f && p.z < 2890f && !rotorExitAligned) {
            rotorExitAligned = p.z >= 2868f && Mathf.Abs(p.x + 9.3f) <= 2.5f && Mathf.Abs(body.linearVelocity.x) <= 1.25f;
            if (!rotorExitAligned) desired.z = Mathf.Clamp((2874f - p.z) * .7f, -4f, desired.z);
        }
        // Settle on the right log's centre before the ground drops into the two-log crossing.
        if (p.z > 3448f && p.z < 3485f && !logAligned) {
            logAligned = p.z >= 3460f && Mathf.Abs(p.x - 12f) <= 2.5f && Mathf.Abs(body.linearVelocity.x) <= 1f;
            if (!logAligned) desired.z = Mathf.Clamp((3462f - p.z) * .7f, -4f, desired.z);
        }
        Vector3 velocity = body.linearVelocity; velocity.y = 0;
        Vector3 command = Vector3.ClampMagnitude((desired - velocity) / 6f, 1f);
        if (!sphere.IsGrounded) command = Vector3.zero;
        bias = Vector3.MoveTowards(bias, command * platform.EffectiveOffsetRadius,
            5f * dt); // virtual occupant movement limited to 5 m/s on the deck
        solver.SetExternalBias(bias);
        if (Elapsed >= nextSample) { Sample(targetX); nextSample = Elapsed + 0.5f; }
        // This island course has water at y=0 and its lowest road above y=12.
        // Overhead scenery is not a valid road-height reference for fall detection.
        if (p.y < platform.SphereRadius + 1f) Complete("FellBelowRoute");
        else if (route.requireGimmickUse && padUses.Any(use => use.required && !use.launched && p.z > use.position.z + 20f)) Complete("MissedJumpPad");
        else if (route.requireGimmickUse && movingUses.Any(use => use.required && use.rideSeconds < .25f && p.z > use.z + 30f)) Complete("MissedMovingPlatformRow");
        else if (Mathf.Abs(p.x - targetX) > 90f) Complete("LateralDeparture");
        // Wide Hammer recovery can take over 30s after losing forward distance.
        else if (Elapsed - lastProgress > 60f) Complete("NoForwardProgress");
        else if (Elapsed > 1500f) Complete("Timeout");
        else {
            Vector3 delta = p - end;
            bool atFinish = new Vector2(delta.x, delta.z).magnitude < 7f &&
                Mathf.Abs(p.y - (end.y + platform.SphereRadius)) < 4f && IsSupported();
            finishTime = atFinish ? finishTime + dt : 0;
            if (finishTime >= 2f) Complete(route.requireGimmickUse && !GimmicksUsed()
                ? "MissingGimmickUse" : route.sectionStartZ > 0f ? "DiagnosticPassed" : "Passed");
        }
    }

    private void ApplyHammerGate(Vector3 p, ref Vector3 desired)
    {
        if (p.z < hammerCommitUntil || desired.z <= 0f) return;
        var next = hammers.FirstOrDefault(h => h.transform.position.z > p.z - 10f && h.transform.position.z < p.z + 80f &&
            Mathf.Abs(h.transform.position.x - p.x) < 55f);
        if (next == null) return;
        float start = next.transform.position.z;
        float end = start;
        foreach (var h in hammers.Where(h => h.transform.position.z > start)) {
            if (h.transform.position.z - end > 40f) break;
            end = h.transform.position.z;
        }
        float stop = start - 26f;
        if (p.z < stop - 12f) return;
        float speed = Mathf.Min(12f, route.speed);
        bool clear = true;
        float time = (float)Runner.SimulationTime;
        for (float ahead = 0f; ahead < (end + 22f - p.z) / speed + 4f; ahead += .1f) {
            // Account for the time needed to accelerate from the waiting position.
            float v0 = Mathf.Clamp(body.linearVelocity.z, 0f, speed);
            float lag = v0 < 3f ? 1f : 0f;
            float accelerating = Mathf.Min(Mathf.Max(0f, ahead - lag), (speed - v0) / 4f);
            float travel = v0 * Mathf.Min(ahead, lag) + v0 * accelerating + 2f * accelerating * accelerating +
                speed * Mathf.Max(0f, ahead - lag - accelerating);
            var forecast = new Vector3(p.x, p.y, p.z + travel);
            foreach (var h in hammers) {
                if (Mathf.Abs(h.transform.position.z - forecast.z) > 30f) continue;
                for (float margin = -.3f; margin <= .31f; margin += .3f)
                    if (h.ThreatensTraversal(forecast, platform.SphereRadius, platform.HeightOffset, platform.EffectiveOffsetRadius + 1f, time + ahead + margin)) { clear = false; break; }
                if (!clear) break;
            }
            if (!clear) break;
        }
        if (clear && Mathf.Abs(desired.x) < 1f && Mathf.Abs(body.linearVelocity.z) < .5f && Mathf.Abs(bias.z) < 2f && IsSupported()) {
            hammerCommitUntil = end + 22f;
            File.AppendAllText(Path.Combine(runDirectory, "hazard-events.jsonl"), JsonUtility.ToJson(new GateEvent { time = Elapsed, start = start, end = end }) + Environment.NewLine);
        }
        else desired.z = Mathf.Clamp((stop - p.z) * .7f, -6f, desired.z);
    }

    [Serializable] private class GateEvent { public float time, start, end; }

    private void ApplyMovingGate(Vector3 p, ref Vector3 desired)
    {
        if (p.z < movingCommitUntil || desired.z <= 0f) return;
        var next = movingUses.Where(row => row.z > p.z + 12f && row.z < p.z + 65f).OrderBy(row => row.z).FirstOrDefault();
        if (next == null) return;
        float stop = next.z - (next.trapdoor ? 23f : 30f);
        if (p.z < stop - 15f) return;
        var ray = new Ray(new Vector3(p.x, 180f, next.trapdoor ? next.z : next.z - 15f), Vector3.down);
        float height = float.NegativeInfinity;
        foreach (var pair in movingColliders)
            if (pair.Value == next && pair.Key.Raycast(ray, out var hit, 180f) && hit.normal.y > .7f) height = Mathf.Max(height, hit.point.y);
        float rise = height - (p.y - platform.SphereRadius);
        float alignmentMetric = next.trapdoor ? Quaternion.Angle(next.closedRotation, next.panel.rotation) : rise;
        if (Elapsed - next.riseSampleTime >= .25f) {
            next.approaching = !float.IsNaN(next.previousRise) && alignmentMetric < next.previousRise - .02f;
            next.previousRise = alignmentMetric;
            next.riseSampleTime = Elapsed;
        }
        bool aligning = next.trapdoor ? next.approaching && alignmentMetric < 2.5f :
            !supportedRows.Any(row => row != next) || next.approaching;
        bool exposed = !next.trapdoor || !Physics.RaycastAll(ray, 180f, ~0, QueryTriggerInteraction.Ignore)
            .Any(hit => hit.normal.y > .7f && hit.point.y >= height - .05f &&
                (hit.collider.name == "BridgeCollision" || hit.collider.name.StartsWith("land_") || hit.collider.name.StartsWith("CourseConnector_")));
        if (aligning && exposed && rise < 3.5f && rise > -12f && p.z >= stop - 3f && Mathf.Abs(body.linearVelocity.z) < .5f) {
            // The synchronized trapdoors share a closing window. Stopping on a panel
            // to time the next one would leave the Transport on it as it opens.
            movingCommitUntil = next.trapdoor ? movingUses
                .Where(row => row.trapdoor && row.z >= next.z && row.z <= next.z + 80f).Max(row => row.z) + 23f : next.z - 11f;
            File.AppendAllText(Path.Combine(runDirectory, "hazard-events.jsonl"), JsonUtility.ToJson(new GateEvent { time = Elapsed, start = next.z, end = height }) + Environment.NewLine);
        }
        else desired.z = Mathf.Clamp((stop - p.z) * .7f, -4f, desired.z);
    }

    private void Sample(float targetX)
    {
        var hits = new System.Collections.Generic.List<string>();
        foreach (var probe in probes) { hits.AddRange(probe.Names); probe.Names.Clear(); }
        var sample = new SampleData { time = Elapsed, position = body.position,
            simulationTime = (float)Runner.SimulationTime, knockedBack = sphere.IsKnockedBack,
            velocity = body.linearVelocity, angularVelocity = body.angularVelocity,
            bias = bias, tilt = platform.TiltAngle, grounded = IsSupported(),
            targetX = targetX, collisions = hits.Distinct().ToArray() };
        telemetry.WriteLine(JsonUtility.ToJson(sample));
        WriteStatus();
    }

    public void Complete(string result)
    {
        Result = result;
        if (solver != null) solver.SetExternalBias(Vector3.zero);
        telemetry?.Dispose(); telemetry = null;
        WriteStatus();
        Debug.Log("Transport traversal: " + result + " at " + body.position + ", " + Elapsed + "s");
    }

    private bool IsSupported() => Physics.RaycastAll(body.position, Vector3.down,
        platform.SphereRadius + 0.75f, ~0, QueryTriggerInteraction.Ignore)
        .Any(h => !h.collider.transform.IsChildOf(sphere.transform.root) && h.normal.y > 0.2f);

    private bool TryRoadPoint(float z, out Vector3 point)
    {
        int i = 0;
        while (i < route.points.Length - 2 && route.points[i + 1].z < z) i++;
        float x = Mathf.Lerp(route.points[i].x, route.points[i + 1].x,
            Mathf.InverseLerp(route.points[i].z, route.points[i + 1].z, z));
        var hits = Physics.RaycastAll(new Vector3(x, 200f, z), Vector3.down, 199f, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => !h.collider.transform.IsChildOf(sphere.transform.root) && h.normal.y > 0.7f &&
                System.Text.RegularExpressions.Regex.IsMatch(h.collider.name, "^(land_|bridge_|log_|big_log_|BridgeCollision|CourseConnector_|obstacle_11_002|obstacle_14_002)"))
            .OrderByDescending(h => h.point.y).ToArray();
        point = hits.Length > 0 ? hits[0].point : Vector3.zero;
        return hits.Length > 0;
    }

    private void WriteStatus()
    {
        string json = JsonUtility.ToJson(new Status { result = Result, elapsed = Elapsed,
            bestZ = BestZ, position = body != null ? body.position : Vector3.zero,
            runDirectory = runDirectory, scene = SceneManager.GetActiveScene().path }, true);
        File.WriteAllText(Path.Combine(Folder, "status.json"), json);
        File.WriteAllText(Path.Combine(runDirectory, "result.json"), json);
        File.WriteAllText(Path.Combine(runDirectory, "gimmick-usage.json"), JsonUtility.ToJson(new Usage {
            mandatory = route.requireGimmickUse, satisfied = GimmicksUsed(), pads = padUses.ToArray(), movingRows = movingUses.ToArray()
        }, true));
    }

    private void OnDestroy()
    {
        if (Result == "Running") Complete("Interrupted");
        foreach (var use in padUses) if (use.pad != null) use.pad.Launched -= RecordLaunch;
        telemetry?.Dispose(); motions?.Restore();
        if (Active == this) Active = null;
    }

    private void RecordLaunch(JumpPad pad, Rigidbody target)
    {
        if (Result != "Running" || target != body) return;
        var use = padUses.First(p => p.pad == pad);
        use.launches++;
        use.launched = target.linearVelocity.y > 1f;
        use.launchTime = Elapsed;
        use.launchPosition = target.position;
        use.launchVelocity = target.linearVelocity;
        use.airborne = true;
        File.AppendAllText(Path.Combine(runDirectory, "gimmick-events.jsonl"), JsonUtility.ToJson(use) + Environment.NewLine);
    }

    private void UpdateGimmickUse(float dt)
    {
        bool supported = IsSupported();
        foreach (var use in padUses) {
            if (!use.airborne) continue;
            if (!supported) use.airSeconds += dt;
            else if (use.airSeconds >= .25f && body.linearVelocity.y <= 0f) {
                use.airborne = false; use.landingPosition = body.position;
            }
        }
        supportedRows.Clear();
        float castRadius = platform.SphereRadius * .95f;
        int count = Physics.SphereCastNonAlloc(body.position, castRadius, Vector3.down, supportHits,
            platform.SphereRadius - castRadius + .75f, ~0, QueryTriggerInteraction.Ignore);
        float nearestSupport = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
            if (supportHits[i].normal.y > .2f && supportHits[i].collider.transform.root != sphere.transform.root)
                nearestSupport = Mathf.Min(nearestSupport, supportHits[i].distance);
        for (int i = 0; i < count; i++)
            if (supportHits[i].normal.y > .2f && supportHits[i].distance <= nearestSupport + .02f &&
                movingColliders.TryGetValue(supportHits[i].collider, out var use)) supportedRows.Add(use);
        foreach (var use in supportedRows) use.rideSeconds += dt;
    }

    private bool GimmicksUsed() => padUses.Where(p => p.required).All(p => p.launched && p.airSeconds >= .25f) &&
        movingUses.Where(p => p.required).All(p => p.rideSeconds >= .25f);

    [Serializable] private class PadUse {
        [NonSerialized] public JumpPad pad;
        public string id; public Vector3 position; public bool required, launched, airborne;
        public int launches; public float launchTime, airSeconds; public Vector3 launchPosition, launchVelocity, landingPosition;
    }
    [Serializable] private class MovingUse {
        public string id; public float z, rideSeconds; public bool required, trapdoor;
        [NonSerialized] public Transform panel;
        [NonSerialized] public Quaternion closedRotation;
        [NonSerialized] public float previousRise = float.NaN, riseSampleTime;
        [NonSerialized] public bool approaching;
    }
    [Serializable] private class Usage { public bool mandatory, satisfied; public PadUse[] pads; public MovingUse[] movingRows; }

    [Serializable] private class Status { public string result, runDirectory, scene; public float elapsed, bestZ; public Vector3 position; }
    [Serializable] private class SampleData { public float time, simulationTime, tilt, targetX; public Vector3 position, velocity, angularVelocity, bias; public bool grounded, knockedBack; public string[] collisions; }
}
