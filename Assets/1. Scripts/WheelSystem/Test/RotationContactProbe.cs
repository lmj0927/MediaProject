#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Fusion;
using UnityEngine;

/// <summary>실제 Host/Transport/에셋 메시를 쓰는 접촉 진단. 완주 테스트와 별도이다.</summary>
[DefaultExecutionOrder(900)]
public class RotationContactProbe : SimulationBehaviour, IAfterTick
{
    private SphereDriver sphere;
    private PlatformTilt platform;
    private Rigidbody body, deck;
    private Collider deckCollider;
    private RotatingObstacleContacts contacts;
    private MeshCollider[] sources;
    private MeshCollider[] registrySources;
    private HostPhysicsStepper stepper;
    private ObstacleMotionRegistry registry;
    private Collider[] parts;
    private readonly List<object> results = new();
    private MethodInfo applyPose;
    private int index, ticks;
    private float radius, initialDepth, maxDepth, maxSpeed, maxDisplacement;
    private float intermediateDepth;
    private float maxGroundDepth, maxUpwardSpeed;
    private BoxCollider floor;
    private Vector3 startPosition, sourcePosition;
    private Quaternion sourceRotation;
    private bool initialized, caseReady, finished;
    private string folder;
    private bool Grounded => index >= sources.Length * 4 && index < sources.Length * 5;
    private bool RegistryCase => index >= sources.Length * 5 + 2;
    private bool Sweep => !RegistryCase && index >= sources.Length * 5;
    private bool Push => RegistryCase || Grounded || index % 2 == 1;
    private bool SphereContact => !RegistryCase && !Sweep && !Grounded && index % 4 >= 2;
    private MeshCollider Source => RegistryCase ? registrySources[index - sources.Length * 5 - 2] : Sweep ? sources.First(c => c.sharedMesh.name == "obstacle_001") :
        sources[Grounded ? index - sources.Length * 4 : index / 4];

    private void Update()
    {
        if (initialized || finished || TransportTraversalTest.Active == null || TransportTraversalTest.Active.Result != "Running") return;
        var readySphere = FindFirstObjectByType<SphereDriver>();
        var readyPlatform = FindFirstObjectByType<PlatformTilt>();
        if (readySphere.Object == null || !readySphere.Object.IsValid || readyPlatform.Object == null || !readyPlatform.Object.IsValid) return;
        var test = TransportTraversalTest.Active;
        test.Complete("RotationContactDiagnosticOnly");
        sphere = FindFirstObjectByType<SphereDriver>();
        platform = FindFirstObjectByType<PlatformTilt>();
        body = sphere.GetComponent<Rigidbody>(); deck = platform.GetComponent<Rigidbody>();
        deckCollider = platform.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger && c.name.StartsWith("DeckCollider"));
        parts = sphere.transform.root.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger &&
            (c.attachedRigidbody == body || c.attachedRigidbody == deck)).ToArray();
        sources = FindObjectsByType<ithappy.RotationScript>(FindObjectsSortMode.None)
            .Where(s => s.GetComponent<NetworkTransform>() != null).Select(s => s.GetComponent<MeshCollider>())
            .Where(c => c != null && c.enabled && !c.isTrigger).GroupBy(c => c.sharedMesh).Select(g => g.First()).ToArray();
        registrySources = new[] { sources.First(c => c.sharedMesh.name == "obstacle_001"),
            FindObjectsByType<ithappy.OscillateRotation>(FindObjectsSortMode.None).Where(s => s.GetComponent<NetworkTransform>() != null)
                .Select(s => s.GetComponent<MeshCollider>()).First(c => c != null && c.enabled && !c.isTrigger) };
        applyPose = typeof(PlatformTilt).GetMethod("ApplyPose", BindingFlags.Instance | BindingFlags.NonPublic);
        body.useGravity = false; sphere.enabled = false; platform.enabled = false;
        stepper = test.GetComponent<HostPhysicsStepper>();
        registry = (ObstacleMotionRegistry)typeof(HostPhysicsStepper).GetField("_obstacles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stepper);
        stepper.SetObstacleMotions(null);
        contacts = new RotatingObstacleContacts(); contacts.CaptureLoadedScenes();
        folder = Path.Combine(TransportTraversalTest.Folder, "RotationContacts-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        initialized = true;
        if (Runner == null) test.Runner.AddGlobal(this);
    }

    public override void FixedUpdateNetwork()
    {
        if (!initialized || finished || !Runner.IsServer) return;
        if (!caseReady && !SetupCase()) { Finish("SetupFailed"); return; }
        applyPose.Invoke(platform, new object[] { Quaternion.identity });
        if (RegistryCase) return;
        var source = Source;
        var spin = source.GetComponent<ithappy.RotationScript>();
        Vector3 axis = spin.rotationAxis == ithappy.RotationScript.RotationAxis.X ? Vector3.right :
            spin.rotationAxis == ithappy.RotationScript.RotationAxis.Y ? Vector3.up : Vector3.forward;
        float degrees = Sweep ? (ticks == 0 ? 360 : 0) : spin.rotationSpeed * Runner.DeltaTime;
        if (Sweep) axis = Vector3.up;
        if (Push) contacts.Move(source.transform, source.transform.position, source.transform.rotation,
            new Collider[] { source }, radius, axis, degrees);
        else source.transform.rotation *= Quaternion.AngleAxis(degrees, axis);
    }

    void IAfterTick.AfterTick()
    {
        if (!initialized || finished || !caseReady) return;
        var source = Source;
        if (Push && !RegistryCase) contacts.Resolve(source.transform.position, radius, new Collider[] { source }, false);
        maxDepth = Mathf.Max(maxDepth, Depth(source));
        maxSpeed = Mathf.Max(maxSpeed, body.linearVelocity.magnitude);
        maxUpwardSpeed = Mathf.Max(maxUpwardSpeed, body.linearVelocity.y);
        if (Grounded && Physics.ComputePenetration(body.GetComponent<SphereCollider>(), body.position, body.rotation,
            floor, floor.transform.position, floor.transform.rotation, out _, out float groundDepth)) maxGroundDepth = Mathf.Max(maxGroundDepth, groundDepth);
        maxDisplacement = Mathf.Max(maxDisplacement, Vector3.Distance(body.position, startPosition));
        if (++ticks < (Grounded ? 96 : Sweep ? 12 : 24)) return;
        results.Add(new { mesh = source.sharedMesh.name, mode = RegistryCase ? "HostStepperIntegration" : Push ? "Push" : "NativeBaseline",
            scenario = RegistryCase ? "Actual HostPhysicsStepper and captured motion registry" : Grounded ? "Grounded, actual authored rotation" : Sweep ? "360-degree sweep: endpoints clear, intermediate pose intersects" : SphereContact ? "Sphere contact" : "Deck-only contact",
            initialDepth, intermediateDepth, maxDepth, maxGroundDepth, maxSpeed, maxUpwardSpeed, maxDisplacement,
            rotationChanged = Quaternion.Angle(sourceRotation, source.transform.rotation), seconds = ticks * Runner.DeltaTime });
        source.transform.SetPositionAndRotation(sourcePosition, sourceRotation);
        if (RegistryCase) stepper.SetObstacleMotions(null);
        index++; caseReady = false;
        if (index >= sources.Length * 5 + 2 + registrySources.Length) Finish("Completed");
    }

    private bool SetupCase()
    {
        var source = Source;
        sourcePosition = source.transform.position; sourceRotation = source.transform.rotation;
        if (floor != null) floor.enabled = false;
        source.transform.SetPositionAndRotation(new Vector3(10000, 1000, 0), Grounded || RegistryCase ? sourceRotation : Quaternion.identity);
        Physics.SyncTransforms();
        Bounds b = source.bounds;
        radius = Vector3.Distance(b.center, source.transform.position) + b.extents.magnitude;
        bool placed = false;
        foreach (float y in new[] { 0f, -.25f, .25f })
        foreach (float z in new[] { 0f, -.25f, .25f })
        {
            if (placed) continue;
            Vector3 origin = new Vector3(b.max.x + 20, b.center.y + y * b.size.y, b.center.z + z * b.size.z);
            if (!source.Raycast(new Ray(origin, Vector3.left), out var hit, b.size.x + 40) || hit.normal.x < .5f) continue;
            float halfWidth = deckCollider.bounds.extents.x;
            Vector3 p = SphereContact
                ? new Vector3(hit.point.x + platform.SphereRadius - .15f, hit.point.y, hit.point.z)
                : new Vector3(hit.point.x + halfWidth + (Sweep ? .2f : -.15f), hit.point.y - platform.HeightOffset, hit.point.z);
            sphere.GetComponent<NetworkTransform>().Teleport(p, Quaternion.identity);
            sphere.transform.SetPositionAndRotation(p, Quaternion.identity);
            body.position = p; body.rotation = Quaternion.identity;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            deck.position = p + Vector3.up * platform.HeightOffset; deck.rotation = Quaternion.identity;
            applyPose.Invoke(platform, new object[] { Quaternion.identity });
            Physics.SyncTransforms();
            bool sphereHit = Physics.ComputePenetration(body.GetComponent<SphereCollider>(), body.position, body.rotation,
                source, source.transform.position, source.transform.rotation, out _, out _);
            initialDepth = Depth(source);
            intermediateDepth = Sweep ? Enumerable.Range(1, 71).Max(i => Depth(source, Quaternion.AngleAxis(i * 5, Vector3.up))) : 0;
            placed = Sweep
                ? !sphereHit && initialDepth < .001f && intermediateDepth > .05f
                : (SphereContact ? sphereHit : !sphereHit) && initialDepth > .02f;
        }
        if (!placed)
        {
            results.Add(new { mesh = source.sharedMesh.name, scenario = Sweep ? "Sweep" : SphereContact ? "Sphere" : "Deck", error = "Could not prepare the contact geometry" });
            source.transform.SetPositionAndRotation(sourcePosition, sourceRotation);
            return false;
        }
        body.useGravity = Grounded;
        if (Grounded)
        {
            if (floor == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Rotation contact probe floor (runtime only)";
                floor = go.GetComponent<BoxCollider>();
                go.transform.localScale = new Vector3(200, 1, 200);
            }
            floor.transform.position = body.position - Vector3.up * (platform.SphereRadius + .5f);
            floor.enabled = true;
            Physics.SyncTransforms();
        }
        startPosition = body.position;
        if (RegistryCase) stepper.SetObstacleMotions(registry);
        ticks = 0; maxDepth = maxSpeed = maxDisplacement = maxGroundDepth = maxUpwardSpeed = 0;
        caseReady = true;
        return true;
    }

    private float Depth(Collider source, Quaternion? obstacleRotation = null)
    {
        float worst = 0;
        foreach (var c in parts)
        {
            Rigidbody rb = c.attachedRigidbody;
            Quaternion localRotation = Quaternion.Inverse(rb.transform.rotation) * c.transform.rotation;
            Vector3 offset = Quaternion.Inverse(rb.transform.rotation) * (c.transform.position - rb.transform.position);
            if (Physics.ComputePenetration(c, rb.position + rb.rotation * offset, rb.rotation * localRotation,
                source, source.transform.position, obstacleRotation ?? source.transform.rotation, out _, out float distance)) worst = Mathf.Max(worst, distance);
        }
        return worst;
    }

    private void Finish(string result)
    {
        finished = true;
        File.WriteAllText(Path.Combine(folder, "results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
            result, mode = "Private Fusion Host, runtime-only isolated contact probe, NOT traversal", cases = results
        }, Newtonsoft.Json.Formatting.Indented));
        Debug.Log("Rotation contact diagnostic: " + result + "; " + folder);
    }
}
#endif
