# Transport traversal test (re-scoping to actual gimmicks)

**User correction, 2026-11-02:** work only on `map`; do not push or update `main`.
Main integration is through a GitHub pull request. Local main was restored to
origin/main cdf1bff. Preserve the user's revised scene.

The older widened-Hammer and bridge-bypass tests below do **not** meet the intended
acceptance. The target is to avoid/use actual gimmicks. The existing route still
contains old bypass points and needs re-planning for the revised map. No genuine
gimmick start-to-finish pass or accepted full video has been established.

Current priority: **Tools > Transport Traversal > Check Rotating Contacts** tests
private-Host contacts using actual Transport and asset mesh families. It compares
native deck/sphere overlap against non-launch pushing, with isolated runtime
poses. This diagnostic never counts as traversal success. Results are written
under `RotationContacts-*`. Do not save the runtime probe's isolated scene poses.

Target: `Assets/3. Scenes/InGameMap_WheelTone.unity`.

## Run

Save the target scene and wait for Unity compilation/reload to finish. Choose
**Tools > Transport Traversal > Run From Start** for the configured diagnostic
clock, or **Run From Start (Normal Clock)** for full-route acceptance.

**Record From Start (Normal Clock)** records the same continuous run to
`full_traversal.mp4` in its timestamped evidence directory (1280×720, 15fps,
wall-clock timing, no audio). It uses `C:/ffmpeg/bin/ffmpeg.exe` and a runtime-only
follow camera; the scene camera and gameplay settings are not saved or changed.
`video.txt` records frame count, duration, test result and encoder exit status.

The harness creates a private Fusion GameMode.Host session with no avatars,
using loaded scene NetworkObjects, existing obstacle motion and HostPhysicsStepper.
It does not change Build Settings. Photon connectivity is required.

`route.json` contains ordered world points, desired speed and clockSpeed (1–4).
The pilot steers X/Z using only COM external bias, limited to the existing deck
radius and 5m/s bias movement. It slows forward motion for lateral recovery.
No direct force, pose or velocity override occurs during driving.
If it overshoots the finish, the same COM controller can reverse at up to 6m/s
to settle back at the endpoint.

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

Failures: no new forward metre for 60s (allows Hammer recovery); centre below sphere radius + 1m (water is
at y=0); lateral departure >90m; 1500s timeout. Pass requires 2s within 7m horizontally
and 4m vertically of the finish with actual road support. Final acceptance uses
normal clock and no section seed. One failed pilot run does not prove impossibility.

## Current checkpoint

The initial work was committed as f0b015f and merged into local main as fe8396e.
Further work is on codex/transport-traversal, incorporating main's existing balance
and Hammer changes. No additional WheelConfig performance tuning was applied.

Verified local passes: 1480–1900 (first connector ascent), 2490–2705 (zigzag
uphill), 2660–3050 (high drop, connections and rotor), 2950–3600 (platform/blade
bypass), 3900–4250 (trapdoors and Hammers), and 4500–4740 (final pad islands).
Partial overlapping runs covered the pillar/disc sections, third uphill and
4740–5290 (final approach and raised static lane over the last seesaws).
The first full diagnostic fell through an open trapdoor at z4053 (run
20261102-013111-742). A 60m-wide fixed wooden lane now covers z3960–4110;
last four Hammers moved to x±65 on their widened island after repeated kicks.
That section then passed in36.22s (20261102-014220-511).
The first normal-clock recording reached z4295, stopped at the curved log lip
(20261102-014314-464,562s), and was NOT a full pass. A 60m-wide wooden approach
and descent now cover z4270–4355. The remaining course z4250–finish passed again
after visual alignment (20261102-015843-563,232.11s), with zero Console errors
and warnings. Full normal-clock start-to-finish acceptance is still OPEN.
Another normal-clock attempt stalled against stone_004 beside the rotating cube
at z2613 (20261102-020030-960). Moved that stone to x=-30 and its translated
third-course counterpart to x=-26.6, keeping their sampled terrain offsets.
Section2490–finish then passed401.06s (20261102-020831-671).
The next normal-clock run still stopped at the rotating pair (20261102-021049-062).
Mesh-corner sweep radius is24.254m; original pivot separation45.3m did not leave
a phase-independent passage. Pair pivots are now x=-45/+36 (third course+3.4),
81m apart, leaving at least32.49m between conservative sweep spheres versus
the14.94m deck. Single cubes moved left10m to clear the right approach lane.
Shapes, spin behaviour and the existing zigzag route are unchanged.
See UphillCubeClearance.json. Full normal-clock acceptance remains required.
The recording smoke test at z5490–finish passed with zero Console errors,
encoder exit0, 81 frames (run 20261102-013013-178, diagnostic clock).

Map changes: seven local wooden connections; widened the pillar approach bridge
at z2142 to measured 40m; moved central barriers at z521/555; widened Hammer
recovery islands while preserving scenery proportions; raised 12 Hammer pivots
8m to reduce underside launches; moved end gate z1589 to the side and widened
its island; reduced rotor z2849 XZ scale to 35% and moved it left for a right lane.
Added a 60m-wide four-leg wooden bypass at z2980–3570. Three final JumpPads
moved beside the central route without changing launch settings. Added wooden
connections across final-course gaps, including a 40m-wide lane above the last
seesaws. Exit signs, final doors and barriers moved outside the central lane;
their island widened on world X. Ten raised Hammer pivots have visual-only wood
supports (colliders disabled).
All25 added collision deck segments have tiled wood visuals aligned to the
actual baked SkinnedMeshRenderer geometry (53 modules; WoodVisualAlignment.json).
Do not assume these wood bridge assets have MeshFilters; sampling only those
returns empty bounds. Their visual colliders remain disabled.

SphereDriver ground-check fix: the original 0.5R inner sphere misses contact on
an actual 32.85deg connector. A 0.95R cast detects it with the same flat-ground
padding; the connector then passed. Evidence: GroundSlopeProbe.json.

Original map backup and raw logs remain local under .ai/Traversal. Player
coordination, human difficulty, all obstacle phases and client sync are not proved
by this virtual COM test. Stop Play Mode before editing and before the next run.
