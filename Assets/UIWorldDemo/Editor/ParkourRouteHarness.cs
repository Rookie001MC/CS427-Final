using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Edit-mode route harness.
///
/// Why not play mode: in this environment the editor window is unfocused, so the player loop does
/// not tick. EditorApplication.Step() advances a variable number of frames and injected Input
/// System events apply inconsistently, which produces failures that are harness artefacts rather
/// than level defects. So this harness drops the input layer and instead replicates
/// BasicFirstPersonController.HandleMovement verbatim (same statement order, same two separate
/// Move calls), reading walkSpeed / sprintSpeed / jumpHeight / gravity from the live component.
///
/// What is therefore genuinely exercised: the real CharacterController, the real scene colliders,
/// real sweeps/step-offset/slope handling, real landing surfaces, and the real CheckpointVolume
/// and BasicFirstPersonController code. What is simulated: keyboard input and PhysX trigger
/// dispatch (trigger overlap is verified geometrically instead).
/// </summary>
public static class ParkourRouteHarness
{
    private const float Dt = 1f / 60f;

    private static readonly string[] Route =
    {
        "Platform_Start", "Plat_S1", "Plat_S2", "Plat_S3", "Plat_S4",
        "Plat_S5", "Plat_S6", "Plat_S7", "Plat_S8_Pillar", "Deck_North",
        "Plat_B1", "Plat_B2", "Plat_B3",
        "Plat_B4", "Plat_B5", "Plat_B6", "Plat_B7_Beam", "Plat_B8_Ledge", "Plat_B9_Ledge", "Deck_South",
        "Plat_K1", "Plat_K2", "Plat_K3", "Plat_K4", "Plat_K5", "Plat_K6", "Plat_K7_TowerShelf",
        "Ledge_W1", "Ledge_W2", "Ledge_W3", "Ledge_N1", "Ledge_N2", "Ledge_N3", "Ledge_E1",
        "Finish_TowerCap"
    };

    private static readonly HashSet<string> SprintFrom = new HashSet<string>
    {
        "Plat_B1", "Plat_B2", "Plat_K1", "Plat_K2", "Plat_K3", "Plat_K4", "Plat_K5",
        "Plat_K7_TowerShelf"
    };

    private static float walkSpeed, sprintSpeed, jumpHeight, gravity, fallResetHeight;
    private static int failures, warnings;
    private static StringBuilder log;

    [MenuItem("Tools/Parkour/R - Run Route Harness")]
    public static void Run()
    {
        log = new StringBuilder();
        failures = 0;
        warnings = 0;

        GameObject player = GameObject.Find("FPP_Player");
        if (player == null)
        {
            Debug.LogError("[Harness] FPP_Player not found.");
            return;
        }

        CharacterController cc = player.GetComponent<CharacterController>();
        BasicFirstPersonController ctrl = player.GetComponent<BasicFirstPersonController>();
        SerializedObject so = new SerializedObject(ctrl);
        walkSpeed = so.FindProperty("walkSpeed").floatValue;
        sprintSpeed = so.FindProperty("sprintSpeed").floatValue;
        jumpHeight = so.FindProperty("jumpHeight").floatValue;
        gravity = so.FindProperty("gravity").floatValue;
        fallResetHeight = so.FindProperty("fallResetHeight").floatValue;

        Vector3 originalPos = player.transform.position;
        Quaternion originalRot = player.transform.rotation;

        log.AppendLine("=== PARKOUR ROUTE HARNESS (edit mode, real CharacterController) ===");
        log.AppendLine($"walk={walkSpeed} sprint={sprintSpeed} jumpHeight={jumpHeight} gravity={gravity} fallReset={fallResetHeight}");
        log.AppendLine($"launch={Mathf.Sqrt(jumpHeight * -2f * gravity):F3} m/s   dt={Dt:F5}");
        log.AppendLine();

        // In edit mode PhysX keeps stale collider poses after transforms are changed by script, so
        // queries would test the geometry's previous positions. Force a sync before anything else.
        Physics.SyncTransforms();

        try
        {
            ClearanceSweep(cc);
            TriggerGeometryCheck(cc);
            CheckpointLogicCheck(player, ctrl);
            JumpRun(player, cc);
        }
        finally
        {
            cc.enabled = false;
            player.transform.position = originalPos;
            player.transform.rotation = originalRot;
            cc.enabled = true;
        }

        log.AppendLine();
        log.AppendLine($"player restored to {player.transform.position} (expected {originalPos})");
        log.AppendLine($"=== RESULT: {failures} failure(s), {warnings} warning(s) ===");

        string dir = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "SceneBackups");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "route_harness_report.txt"), log.ToString());
        Debug.Log($"[Harness] {failures} failure(s), {warnings} warning(s) - report written.");
    }

    // ---------------------------------------------------------------- helpers

    private static Bounds? TopOf(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go == null)
        {
            return null;
        }

        Renderer r = go.GetComponent<Renderer>();
        return r != null ? r.bounds : (Bounds?)null;
    }

    private static Vector3 ClosestOnTop(Bounds rect, Vector3 to)
    {
        return new Vector3(
            Mathf.Clamp(to.x, rect.min.x, rect.max.x),
            rect.max.y,
            Mathf.Clamp(to.z, rect.min.z, rect.max.z));
    }

    /// <summary>
    /// True closest pair of points between two axis-aligned top faces, per axis. Using each rect's
    /// closest point to the *other rect's centre* instead produces a needlessly diagonal line.
    /// </summary>
    private static void ClosestPair(Bounds a, Bounds b, out Vector3 pa, out Vector3 pb)
    {
        Axis(a.min.x, a.max.x, b.min.x, b.max.x, out float ax, out float bx);
        Axis(a.min.z, a.max.z, b.min.z, b.max.z, out float az, out float bz);
        pa = new Vector3(ax, a.max.y, az);
        pb = new Vector3(bx, b.max.y, bz);
    }

    private static void Axis(float aMin, float aMax, float bMin, float bMax, out float a, out float b)
    {
        if (aMax < bMin)
        {
            a = aMax;
            b = bMin;
        }
        else if (bMax < aMin)
        {
            a = aMin;
            b = bMax;
        }
        else
        {
            float mid = (Mathf.Max(aMin, bMin) + Mathf.Min(aMax, bMax)) * 0.5f;
            a = mid;
            b = mid;
        }
    }

    /// <summary>
    /// Pulls a point inside a footprint on both axes, never past the middle of a narrow platform.
    /// </summary>
    private static Vector3 ClampInside(Vector3 p, Bounds rect, float inset)
    {
        return new Vector3(
            ClampAxis(p.x, rect.min.x, rect.max.x, inset),
            p.y,
            ClampAxis(p.z, rect.min.z, rect.max.z, inset));
    }

    private static float ClampAxis(float v, float min, float max, float inset)
    {
        float half = (max - min) * 0.5f;
        float use = Mathf.Min(inset, Mathf.Max(0f, half - 0.05f));
        return Mathf.Clamp(v, min + use, max - use);
    }

    /// <summary>
    /// Unit direction from <paramref name="a"/> into <paramref name="b"/> along whichever axis the
    /// two footprints are separated (or exactly touching) on. Falls back to centre-to-centre.
    /// </summary>
    private static Vector3 ApproachDir(Bounds a, Bounds b)
    {
        const float eps = 1e-3f;
        float zSep = Mathf.Max(b.min.z - a.max.z, a.min.z - b.max.z);
        float xSep = Mathf.Max(b.min.x - a.max.x, a.min.x - b.max.x);

        bool zAdjacent = zSep >= -eps;
        bool xAdjacent = xSep >= -eps;

        // when both axes qualify, take the one with the larger separation (the real approach)
        if (zAdjacent && (!xAdjacent || zSep >= xSep))
        {
            return new Vector3(0f, 0f, b.center.z > a.center.z ? 1f : -1f);
        }

        if (xAdjacent)
        {
            return new Vector3(b.center.x > a.center.x ? 1f : -1f, 0f, 0f);
        }

        Vector3 d = b.center - a.center;
        d.y = 0f;
        return d.sqrMagnitude < 1e-5f ? Vector3.forward : d.normalized;
    }

    /// <summary>
    /// Nudges a stance toward the platform centre until the player capsule is free of geometry.
    /// A ledge that abuts a wall has a usable area inset by the capsule radius.
    /// </summary>
    private static Vector3 FreeStance(Vector3 feet, Bounds rect, CharacterController self)
    {
        for (int i = 0; i <= 14; i++)
        {
            Vector3 candidate = Inset(feet, rect, i * 0.1f);
            Vector3 p1 = candidate + Vector3.up * 0.37f;
            Vector3 p2 = candidate + Vector3.up * 1.63f;
            bool blocked = false;
            foreach (Collider c in Physics.OverlapCapsule(p1, p2, 0.34f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == self)
                {
                    continue;
                }

                Bounds cb = c.bounds;
                if (cb.max.y > candidate.y + 0.30f)   // ignore walkable decals under stepOffset
                {
                    blocked = true;
                    break;
                }
            }

            if (!blocked)
            {
                return candidate;
            }
        }

        return Inset(feet, rect, 0.4f);
    }

    private static Vector3 Inset(Vector3 point, Bounds rect, float amount)
    {
        Vector3 centre = new Vector3(rect.center.x, rect.max.y, rect.center.z);
        Vector3 dir = centre - point;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-5f)
        {
            return point;
        }

        return point + dir.normalized * Mathf.Min(amount, dir.magnitude);
    }

    private static void Place(GameObject player, CharacterController cc, Vector3 feet)
    {
        cc.enabled = false;
        player.transform.position = feet;
        cc.enabled = true;
        Physics.SyncTransforms();
    }

    /// <summary>
    /// Gravity-only settle with no horizontal input, so the stance is not consumed by walking off
    /// the edge before the jump fires. Ends with a downward Move so isGrounded is freshly true.
    /// </summary>
    private static bool Settle(CharacterController cc, ref float verticalSpeed, int maxFrames)
    {
        bool everGrounded = false;
        for (int i = 0; i < maxFrames; i++)
        {
            verticalSpeed += gravity * Dt;
            cc.Move(Vector3.up * verticalSpeed * Dt);

            if (cc.isGrounded)
            {
                everGrounded = true;
                verticalSpeed = -2f;

                // hold for a few more frames so the controller is stably resting
                for (int k = 0; k < 3; k++)
                {
                    cc.Move(Vector3.up * verticalSpeed * Dt);
                }

                return true;
            }
        }

        return everGrounded && cc.isGrounded;
    }

    private static string GroundUnder(Transform t)
    {
        if (Physics.Raycast(t.position + Vector3.up * 0.4f, Vector3.down,
            out RaycastHit hit, 1.4f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.collider.gameObject.name;
        }

        return "<none>";
    }

    // ---------------------------------------------------------------- clearance sweep

    /// <summary>
    /// Perimeter rails, the start wall, narrow gate posts and the goal beacon are deliberate
    /// barriers or avoidable set dressing. Everything else blocking a stance is a defect.
    /// </summary>
    private static bool IntendedBarrier(string name)
    {
        return name == "Start_LeftRail" || name == "Start_RightRail" || name == "Start_BackWall"
            || name == "Rail_DeckNorth_L" || name == "Rail_DeckNorth_R"
            || name == "CP1_Left" || name == "CP1_Right"
            || name == "CP2_Left" || name == "CP2_Right"
            || name == "Finish_Left" || name == "Finish_Right" || name == "Finish_Beacon";
    }

    private static void ClearanceSweep(CharacterController self)
    {
        log.AppendLine("--- stance clearance sweep (blocking colliders taller than stepOffset 0.30) ---");
        int totalBlocked = 0;

        foreach (string name in Route)
        {
            Bounds? maybe = TopOf(name);
            if (maybe == null)
            {
                log.AppendLine($"  {name}: MISSING");
                failures++;
                continue;
            }

            Bounds b = maybe.Value;
            List<string> blockers = new List<string>();
            int blocked = 0;
            int samples = 0;

            float xLo = b.min.x + 0.35f, xHi = b.max.x - 0.35f;
            float zLo = b.min.z + 0.35f, zHi = b.max.z - 0.35f;
            int nx = Mathf.Max(1, Mathf.CeilToInt((xHi - xLo) / 0.7f) + 1);
            int nz = Mathf.Max(1, Mathf.CeilToInt((zHi - zLo) / 0.7f) + 1);

            for (int ix = 0; ix < nx; ix++)
            {
                for (int iz = 0; iz < nz; iz++)
                {
                    float x = nx == 1 ? b.center.x : Mathf.Lerp(xLo, xHi, ix / (float)(nx - 1));
                    float z = nz == 1 ? b.center.z : Mathf.Lerp(zLo, zHi, iz / (float)(nz - 1));
                    samples++;

                    // True player capsule resting on the surface (feet at the surface, height 2.0).
                    // Lifting it to clear stepOffset would make it 0.3 m taller than the real
                    // player and produce false positives against head-height geometry.
                    Vector3 feet = new Vector3(x, b.max.y, z);
                    Vector3 p1 = feet + Vector3.up * 0.35f;
                    Vector3 p2 = feet + Vector3.up * 1.65f;

                    foreach (Collider c in Physics.OverlapCapsule(p1, p2, 0.33f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (c == self)
                        {
                            continue;
                        }

                        // anything no taller than stepOffset is walked over, not blocked
                        if (c.bounds.max.y <= feet.y + 0.30f)
                        {
                            continue;
                        }

                        if (IntendedBarrier(c.gameObject.name))
                        {
                            continue;
                        }

                        blocked++;
                        if (!blockers.Contains(c.gameObject.name))
                        {
                            blockers.Add(c.gameObject.name);
                        }

                        break;
                    }
                }
            }

            if (blocked > 0)
            {
                totalBlocked += blocked;
                log.AppendLine($"  {name}: {blocked}/{samples} stances blocked by [{string.Join(", ", blockers)}]");
                warnings++;
            }
        }

        log.AppendLine(totalBlocked == 0 ? "  all stances clear" : $"  {totalBlocked} blocked stance(s)");
        log.AppendLine();
    }

    // ---------------------------------------------------------------- checkpoint geometry

    private static void TriggerGeometryCheck(CharacterController cc)
    {
        log.AppendLine("--- checkpoint trigger overlap (would PhysX fire it while crossing the deck?) ---");

        (string trigger, string deck)[] pairs =
        {
            ("Checkpoint_DeckNorth", "Deck_North"),
            ("Checkpoint_DeckSouth", "Deck_South")
        };

        foreach ((string triggerName, string deckName) in pairs)
        {
            GameObject tg = GameObject.Find(triggerName);
            Bounds? deck = TopOf(deckName);
            if (tg == null || deck == null)
            {
                log.AppendLine($"  {triggerName}: MISSING");
                failures++;
                continue;
            }

            BoxCollider box = tg.GetComponent<BoxCollider>();
            Bounds tb = box.bounds;
            Bounds d = deck.Value;

            // sample the deck surface and count how many stances put the capsule inside the trigger
            int inside = 0, samples = 0;
            for (int ix = 0; ix <= 6; ix++)
            {
                for (int iz = 0; iz <= 6; iz++)
                {
                    float x = Mathf.Lerp(d.min.x + 0.35f, d.max.x - 0.35f, ix / 6f);
                    float z = Mathf.Lerp(d.min.z + 0.35f, d.max.z - 0.35f, iz / 6f);
                    samples++;
                    Bounds capsule = new Bounds(new Vector3(x, d.max.y + 1f, z), new Vector3(0.7f, 2f, 0.7f));
                    if (tb.Intersects(capsule))
                    {
                        inside++;
                    }
                }
            }

            bool isTrigger = box.isTrigger;
            string verdict = inside > 0 && isTrigger ? "OK" : "FAIL";
            if (verdict == "FAIL")
            {
                failures++;
            }

            log.AppendLine($"  {triggerName}: isTrigger={isTrigger} coverage={inside}/{samples} stances -> {verdict}");
        }

        log.AppendLine();
    }

    // ---------------------------------------------------------------- checkpoint logic

    private static void CheckpointLogicCheck(GameObject player, BasicFirstPersonController ctrl)
    {
        log.AppendLine("--- checkpoint logic (real CheckpointVolume -> real SetSpawn) ---");

        FieldInfo spawnField = typeof(BasicFirstPersonController)
            .GetField("spawnPosition", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo awake = typeof(BasicFirstPersonController)
            .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

        // simulate Awake at the authored spawn so the baseline matches a fresh play session
        Vector3 authored = player.transform.position;
        awake.Invoke(ctrl, null);
        Vector3 baseline = (Vector3)spawnField.GetValue(ctrl);
        bool baseOk = Vector3.Distance(baseline, authored) < 0.001f;
        log.AppendLine($"  initial spawn after Awake = {baseline} (authored {authored}) -> {(baseOk ? "OK" : "FAIL")}");
        if (!baseOk)
        {
            failures++;
        }

        (string trigger, Vector3 expected)[] cases =
        {
            ("Checkpoint_DeckNorth", new Vector3(0f, 4.65f, 78.75f)),
            ("Checkpoint_DeckSouth", new Vector3(8.5f, 11.05f, 5f))
        };

        Collider playerCollider = player.GetComponent<CharacterController>();

        foreach ((string triggerName, Vector3 expected) in cases)
        {
            GameObject tg = GameObject.Find(triggerName);
            CheckpointVolume vol = tg != null ? tg.GetComponent<CheckpointVolume>() : null;
            if (vol == null)
            {
                log.AppendLine($"  {triggerName}: CheckpointVolume MISSING");
                failures++;
                continue;
            }

            // reset the one-shot latch, then drive the real OnTriggerEnter
            FieldInfo activated = typeof(CheckpointVolume)
                .GetField("activated", BindingFlags.NonPublic | BindingFlags.Instance);
            activated.SetValue(vol, false);

            MethodInfo onEnter = typeof(CheckpointVolume)
                .GetMethod("OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance);
            onEnter.Invoke(vol, new object[] { playerCollider });

            Vector3 got = (Vector3)spawnField.GetValue(ctrl);
            float err = Vector3.Distance(got, expected);
            bool ok = err < 0.01f;
            log.AppendLine($"  {triggerName}: spawn -> {got} expected {expected} err {err:F4} -> {(ok ? "OK" : "FAIL")}");
            if (!ok)
            {
                failures++;
            }

            // and confirm the fall-reset would land the player on solid ground there
            string under = Physics.Raycast(got + Vector3.up * 0.4f, Vector3.down,
                out RaycastHit h, 1.4f, ~0, QueryTriggerInteraction.Ignore) ? h.collider.gameObject.name : "<none>";
            log.AppendLine($"    respawn point sits above '{under}'");
            if (under == "<none>")
            {
                log.AppendLine("    FAIL respawn point has no ground beneath it");
                failures++;
            }
        }

        // restore spawn to the authored start
        awake.Invoke(ctrl, null);
        log.AppendLine($"  spawn restored to {(Vector3)spawnField.GetValue(ctrl)}");
        log.AppendLine();
    }

    // ---------------------------------------------------------------- jump run

    private static void JumpRun(GameObject player, CharacterController cc)
    {
        log.AppendLine("--- jump run: replicates HandleMovement verbatim, real Move/collision ---");
        log.AppendLine("leg                                           speed   rise  gap   apex  landed on            verdict");

        float launch = Mathf.Sqrt(jumpHeight * -2f * gravity);

        for (int i = 0; i < Route.Length - 1; i++)
        {
            string fromName = Route[i];
            string toName = Route[i + 1];
            Bounds? fromMaybe = TopOf(fromName);
            Bounds? toMaybe = TopOf(toName);
            if (fromMaybe == null || toMaybe == null)
            {
                log.AppendLine($"{fromName} -> {toName}: MISSING GEOMETRY");
                failures++;
                continue;
            }

            Bounds from = fromMaybe.Value, to = toMaybe.Value;
            ClosestPair(from, to, out Vector3 nearFrom, out Vector3 nearTo);

            // Approach axis: the axis on which the two footprints are separated or touching. The
            // landing target must sit INSIDE the target platform along that axis, otherwise the
            // player stops with their centre still over the gap. The takeoff is nudged backwards,
            // making every tested jump slightly longer than the true edge-to-edge gap.
            // On a diagonally offset leg the point must be pulled inside on BOTH axes, not just the
            // approach axis, or the player's capsule straddles the perpendicular edge and slips off.
            Vector3 approach = ApproachDir(from, to);
            Vector3 takeoff = FreeStance(ClampInside(nearFrom - approach * 0.10f, from, 0.36f), from, cc);
            Vector3 landing = FreeStance(ClampInside(nearTo + approach * 0.55f, to, 0.50f), to, cc);

            float rise = to.max.y - from.max.y;
            float dx = Mathf.Max(0f, Mathf.Max(from.min.x - to.max.x, to.min.x - from.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(from.min.z - to.max.z, to.min.z - from.max.z));
            float gap = Mathf.Sqrt(dx * dx + dz * dz);

            bool sprint = SprintFrom.Contains(fromName);
            float speed = sprint ? sprintSpeed : walkSpeed;

            Place(player, cc, takeoff + Vector3.up * 0.05f);
            Vector3 face = landing - takeoff;
            face.y = 0f;
            if (face.sqrMagnitude > 1e-4f)
            {
                player.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            }

            float verticalSpeed = 0f;
            bool grounded = Settle(cc, ref verticalSpeed, 90);
            if (!grounded)
            {
                log.AppendLine($"{fromName,-20} -> {toName,-20} SETUP FAIL: could not stand on takeoff platform");
                failures++;
                continue;
            }

            float startY = player.transform.position.y;
            float apex = startY;
            bool jumpConsumed = false;
            bool airborne = false;
            bool landed = false;
            bool fell = false;
            int frames = 0;
            const int maxFrames = 400;   // ~6.7 s

            while (frames < maxFrames)
            {
                frames++;

                // Steer toward the landing pad and stop pushing forward once over it. Holding W
                // for the whole flight would carry the player speed*airtime (up to 10.4 m) and
                // overshoot every short gap - that is what a competent player avoids.
                Vector3 flat = landing - player.transform.position;
                flat.y = 0f;
                bool pushForward = flat.magnitude > 0.15f;
                if (pushForward)
                {
                    player.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
                }

                // ---- mirror of BasicFirstPersonController.HandleMovement ----
                // Jump fires on frame 1 before any horizontal move, so the stance is not lost by
                // stepping off the ledge first. Instant acceleration + full air control make this
                // equivalent to jumping at the edge while running.
                bool spaceHeld = !airborne && frames <= 4;

                // Sampled before the moves, exactly as HandleMovement does: a Move with no
                // downward component performs no ground sweep, so reading isGrounded after the
                // horizontal Move below would report false while standing still.
                bool groundedThisFrame = cc.isGrounded;

                if (frames > 1 && pushForward)
                {
                    Vector3 planar = player.transform.forward;
                    cc.Move(planar * speed * Dt);
                }

                if (groundedThisFrame && verticalSpeed < 0f)
                {
                    verticalSpeed = -2f;
                    jumpConsumed = false;
                }

                if (groundedThisFrame && !jumpConsumed && spaceHeld)
                {
                    verticalSpeed = launch;
                    jumpConsumed = true;
                }

                verticalSpeed += gravity * Dt;
                cc.Move(Vector3.up * verticalSpeed * Dt);
                // ---- end mirror ----

                Vector3 p = player.transform.position;
                apex = Mathf.Max(apex, p.y);

                // do not count the pre-jump frame as a landing
                if (frames <= 2)
                {
                    continue;
                }

                if (!cc.isGrounded)
                {
                    airborne = true;
                }
                else if (airborne)
                {
                    landed = true;
                    break;
                }

                if (p.y < fallResetHeight)
                {
                    fell = true;
                    break;
                }
            }

            string under = GroundUnder(player.transform);
            string verdict;

            if (fell)
            {
                verdict = "FAIL fell to reset height";
                failures++;
            }
            else if (!landed)
            {
                verdict = $"FAIL never landed (y={player.transform.position.y:F2})";
                failures++;
            }
            else if (under == toName)
            {
                verdict = "OK";
            }
            else if (IsAcceptableSurface(under, toName))
            {
                verdict = $"OK (on '{under}', part of target)";
            }
            else
            {
                verdict = $"FAIL landed on '{under}'";
                failures++;
            }

            log.AppendLine($"{fromName,-20} -> {toName,-20} {(sprint ? "sprint" : "walk  ")} {rise,5:F2} {gap,5:F2} {apex - startY,5:F2}  {under,-20} {verdict}");
        }
    }

    /// <summary>
    /// Landing on an emissive edge lip, guide strip or the tower roof's goal pad still counts as
    /// landing on the target platform: they are 0.12 m decals sitting on its surface.
    /// </summary>
    private static bool IsAcceptableSurface(string under, string target)
    {
        if (under.StartsWith("Lip_") || under.StartsWith("Guide_Arrow") || under.StartsWith("Start_")
            || under.StartsWith("Beam_Glow") || under == "Finish_GlowPad")
        {
            return true;
        }

        // the tower roof cap and its base share the same walkable plane
        if (target == "Finish_TowerCap" && under == "Finish_TowerBase")
        {
            return true;
        }

        return false;
    }
}
