# Transport automatic traversal

Target: `Assets/3. Scenes/InGameMap_WheelTone.unity`.

Open this scene, exit Play Mode, then choose **Tools > Transport Traversal > Run From Start**.
The command enters Play Mode and creates a private `GameMode.Host` session. It uses
the loaded scene without changing Build Settings, spawns no avatars, and runs the
existing HostPhysicsStepper and networked obstacles. Normal Play Mode does not
activate this test. A Photon connection is required for Host initialization.

`route.json` contains ordered world-space points (increasing Z), desired speed,
and `clockSpeed` (1 for normal time, 4 for diagnostic acceleration). The test
clock calls Fusion's updater API; every physics step still uses Runner.DeltaTime.
Use clockSpeed=1 for final full-route acceptance.
The pilot follows X/Z and controls only `CenterOfMassSolver.SetExternalBias`.
Bias magnitude is limited to the existing platform effective radius and moves
at at most 5 m/s; existing smoothing, tilt, torque and physics remain active.
No teleport, initial velocity grant, direct force, or checkpoint reset is used.
Y is used for finish validation, not for steering. This checks virtual COM
capability, not human coordination or whether a real occupant can remain aboard.

Each run creates a timestamped directory containing the route, scene SHA256,
telemetry.jsonl (0.5 simulation-second samples) and result.json. status.json is
the latest snapshot. Runtime faults must be checked in Unity Console separately;
a result alone does not establish an error-free run.

Failures: no new forward metre for 15 seconds, centre below sphere radius + 1m (this map's water is y=0
and road is above y=12), more than 45m lateral departure, or 1500s timeout.
Pass: within 7m horizontally of the finish, within 4m vertically of expected
sphere centre, and grounded for 2 seconds. All runs start from saved scene state.
Stop Play Mode before modifying the scene or scripts and before the next run.

Cycle: inspect telemetry/collision names and local geometry; first check for a
controller/route error; modify the map only after identifying a geometry or
placement problem; save and run from start again. Prefer slopes, width,
connections and obstacle placement before changing Transport settings.
Preserve the existing map composition and challenges. A failed pilot run is not
proof that every possible player trajectory is impossible.

Validation in progress. No full-route pass has been established yet.

Latest checkpoint (2026-11-01): wood connections at z235–295 and z440–501 passed;
central barriers at z521.82 and z555.40 moved to x=-25 and x=25 respectively.
Latest full-from-start diagnostic reached z808.57, then was knocked sideways near
z804 (log_construction_001 / Hammer collider contacts; ~50m/s lateral velocity).
Next investigate clearance/timing of the hanging Hammers around z802/827.
Transport config has not been tuned. Normal-clock full-route acceptance is open.
