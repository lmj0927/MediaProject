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
    [Serializable] public class Route { public Vector3[] points; public float speed = 12f; public float clockSpeed = 1f; public float sectionStartZ, sectionEndZ; }
    public static readonly string Folder = Path.Combine(Application.dataPath, "../.ai/Traversal");
    public static TransportTraversalTest Active { get; private set; }
    public string Result { get; private set; } = "Starting";
    public float Elapsed { get; private set; }
    public float BestZ { get; private set; }
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

    public static async void StartTest(float clockSpeed = 0f, float startZ = 0f, float endZ = 0f)
    {
        if (!Application.isPlaying || Active != null) return;
        Directory.CreateDirectory(Folder);
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/3. Scenes/InGameMap_WheelTone.unity")
            throw new InvalidOperationException("Open InGameMap_WheelTone before starting traversal.");
        var go = new GameObject("Transport Traversal Test (runtime only)");
        var test = go.AddComponent<TransportTraversalTest>();
        Active = test;
        test.runDirectory = Path.Combine(Folder, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(test.runDirectory);
        File.WriteAllText(Path.Combine(test.runDirectory, "scene-sha256.txt"),
            BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(scene.path))).Replace("-", ""));
        test.route = JsonUtility.FromJson<Route>(File.ReadAllText(Path.Combine(Folder, "route.json")));
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
        Vector3 p = body.position;
        if (p.z > BestZ + 1f) { BestZ = p.z; lastProgress = Elapsed; }
        int index = 0;
        while (index < route.points.Length - 2 && route.points[index + 1].z < p.z) index++;
        Vector3 a = route.points[index], b = route.points[index + 1];
        float t = Mathf.InverseLerp(a.z, b.z, p.z);
        float targetX = Mathf.Lerp(a.x, b.x, t);
        Vector3 end = finish;
        float remaining = end.z - p.z;
        float laneSpeed = route.speed * Mathf.Clamp01((25f - Mathf.Abs(targetX - p.x)) / 15f);
        Vector3 desired = new Vector3(Mathf.Clamp((targetX - p.x) * 0.3f, -6f, 6f), 0,
            Mathf.Min(laneSpeed, Mathf.Max(0f, remaining * 0.7f)));
        Vector3 velocity = body.linearVelocity; velocity.y = 0;
        Vector3 command = Vector3.ClampMagnitude((desired - velocity) / 6f, 1f);
        bias = Vector3.MoveTowards(bias, command * platform.EffectiveOffsetRadius,
            5f * dt); // virtual occupant movement limited to 5 m/s on the deck
        solver.SetExternalBias(bias);
        if (Elapsed >= nextSample) { Sample(targetX); nextSample = Elapsed + 0.5f; }
        // This island course has water at y=0 and its lowest road above y=12.
        // Overhead scenery is not a valid road-height reference for fall detection.
        if (p.y < platform.SphereRadius + 1f) Complete("FellBelowRoute");
        else if (Mathf.Abs(p.x - targetX) > 90f) Complete("LateralDeparture");
        else if (Elapsed - lastProgress > 30f) Complete("NoForwardProgress");
        else if (Elapsed > 1500f) Complete("Timeout");
        else {
            Vector3 delta = p - end;
            bool atFinish = new Vector2(delta.x, delta.z).magnitude < 7f &&
                Mathf.Abs(p.y - (end.y + platform.SphereRadius)) < 4f && IsSupported();
            finishTime = atFinish ? finishTime + dt : 0;
            if (finishTime >= 2f) Complete(route.sectionStartZ > 0f ? "DiagnosticPassed" : "Passed");
        }
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
    }

    private void OnDestroy()
    {
        if (Result == "Running") Complete("Interrupted");
        telemetry?.Dispose(); motions?.Restore();
        if (Active == this) Active = null;
    }

    [Serializable] private class Status { public string result, runDirectory, scene; public float elapsed, bestZ; public Vector3 position; }
    [Serializable] private class SampleData { public float time, simulationTime, tilt, targetX; public Vector3 position, velocity, angularVelocity, bias; public bool grounded, knockedBack; public string[] collisions; }
}
