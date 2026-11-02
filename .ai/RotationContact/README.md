# Rotating gimmick contact checks

Work on `map` only. Local `main` is restored to `origin/main` cdf1bff.
Do not push or merge main directly; integration is via a GitHub pull request.

The user chose depenetration/pushing without a Hammer launch impulse. Existing
Host obstacle motion now checks intermediate rotation poses against the actual
Transport sphere, convex deck and fences. Contact correction shifts sphere and
deck together, removes inward velocity, and resolves sphere penetration against
nearby static/kinematic supports. Layer and pair collision exclusions are kept.
Full turns use the unwrapped RotationScript angle. Third-party scripts and assets
are not modified.

## Repeat the check

Open InGameMap_WheelTone in Edit Mode, wait for compilation, then choose
**Tools > Transport Traversal > Check Rotating Contacts**. It starts a private
Fusion Host with no avatars. The probe uses runtime-only isolated positions and
a temporary floor; stop Play Mode when it finishes. Do not save runtime poses.
This is a contact diagnostic, never a traversal pass.

Results: `.ai/Traversal/RotationContacts-*/results.json`.
Latest verified evidence is copied here as `verified-results.json`,
`verification.json`, `script-hashes.json` and `console.json`.

24 checks cover native versus push contact for four actual mesh families,
ground/gravity at authored rotations, a one-tick full-turn stress case, and the
actual HostPhysicsStepper pipeline for RotationScript and OscillateRotation.
The full-turn stress case deliberately uses an unrealistic angular step: it
checks that matching endpoint poses do not hide an intervening collision; it is
not gameplay tuning or a promise about push travel distance.

On the verified run, corrected obstacle overlap and upward exit velocity were0
in these checks. Console errors/warnings were0. This does not establish all
possible phases, player/client sync, or a full genuine-gimmick traversal.

## Next traversal acceptance

Preserve the user's current scene structure. Restore only oversized Hammer-area
widths against `Assets/3. Scenes/Temp/InGameMap_WheelTone.unity`.
Require actual gimmick use in the run; old bridge-bypass videos are not accepted.
JumpPad launch values, moving-platform timing and Transport speed may be tuned
when measurements show they prevent traversal. The final accepted run must be
continuous, normal clock, start-to-finish, with its video and usage evidence.
