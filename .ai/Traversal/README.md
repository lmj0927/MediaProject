# Transport traversal test (in progress)

Target: `Assets/3. Scenes/InGameMap_WheelTone.unity`.

## Run

Save the target scene and wait for Unity compilation/reload to finish. Choose
**Tools > Transport Traversal > Run From Start** for the configured diagnostic
clock, or **Run From Start (Normal Clock)** for full-route acceptance.

The harness creates a private Fusion GameMode.Host session with no avatars,
using loaded scene NetworkObjects, existing obstacle motion and HostPhysicsStepper.
It does not change Build Settings. Photon connectivity is required.

`route.json` contains ordered world points, desired speed and clockSpeed (1–4).
The pilot steers X/Z using only COM external bias, limited to the existing deck
radius and 5m/s bias movement. It slows forward motion for lateral recovery.
No direct force, pose or velocity override occurs during driving.

## Diagnostics and evidence

Unity eval: `TransportTraversalMenu.RunSection(startZ, endZ)` places the Transport
at rest on sampled road after the scene objects are valid, checks the first tick,
and labels success **DiagnosticPassed**. This is separate from full **Passed**;
local tests do not reproduce full-run arrival timing or prove continuous traversal.

Each timestamped run directory contains route/section bounds, scene SHA256,
C# hashes, WheelConfig snapshot, 0.5s telemetry, and result.json. status.json is
latest. Read the last 30 seconds of collision history, not just the final sample:
moving obstacles can contact intermittently. After a test ends physics may keep
moving the body; inspect the saved failure position. Check Unity Console as well.

Failures: no new forward metre for 30s; centre below sphere radius + 1m (water is
at y=0); lateral departure >90m; 1500s timeout. Pass requires 2s within 7m horizontally
and 4m vertically of the finish with actual road support. Final acceptance uses
normal clock and no section seed. One failed pilot run does not prove impossibility.

## Current checkpoint

The initial work was committed as f0b015f and merged into local main as fe8396e.
Further work is on codex/transport-traversal, incorporating main's existing balance
and Hammer changes. No additional WheelConfig performance tuning was applied.

Verified local passes: 1480–1900 (including first connector ascent), 2490–2705
(zigzag uphill), and 2660–3050 (high drop, two connections and rotor bypass).
Partial overlapping runs covered the second-course pillar and moving-disc sections.
The latest full-from-start run reached z1555 before later repairs. Full normal-clock
start-to-finish acceptance is still OPEN. Current diagnostic: 3050–3310.

Map changes: seven local wooden connections; widened the pillar approach bridge
at z2142 to measured 40m; moved central barriers at z521/555; widened Hammer
recovery islands while preserving scenery proportions; raised 12 Hammer pivots
8m to reduce underside launches; moved end gate z1589 to the side and widened
its island; reduced rotor z2849 XZ scale to 35% and moved it left for a right lane.

SphereDriver ground-check fix: the original 0.5R inner sphere misses contact on
an actual 32.85deg connector. A 0.95R cast detects it with the same flat-ground
padding; the connector then passed. Evidence: GroundSlopeProbe.json.

Original map backup and raw logs remain local under .ai/Traversal. Player
coordination, human difficulty, all obstacle phases and client sync are not proved
by this virtual COM test. Stop Play Mode before editing and before the next run.
