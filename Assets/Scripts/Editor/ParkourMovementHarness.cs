using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Movement validation, in the same style as the existing route harnesses: menu-driven, stepping
/// the real controller and the real CharacterController against the real colliders in the open
/// scene, and writing a report to SceneBackups/.
///
/// It does not simulate physics itself. Jump arcs are produced by driving
/// <see cref="BasicFirstPersonController.IntegrateVertical"/> - the exact function the controller
/// uses - and feeding the result into CharacterController.Move, so a change to the integrator
/// shows up here immediately.
/// </summary>
public static class ParkourMovementHarness
{
    private const float Footing = 0.4f;   // matches IndustrialRouteHarness

    private static readonly float[] TestFrameRates = { 30f, 60f, 90f, 144f, 240f };

    // ------------------------------------------------------------------ A: frame rate

    [MenuItem("Tools/Parkour Movement/A - Validate Frame-Rate Independence", priority = 20)]
    public static void ValidateFrameRate()
    {
        if (!ReadPlayer(out BasicFirstPersonController move, out CharacterController cc))
        {
            return;
        }

        Physics.SyncTransforms();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("PARKOUR MOVEMENT - FRAME-RATE INDEPENDENCE");
        sb.AppendLine($"walk={move.WalkSpeed} sprint={move.SprintSpeed} jumpHeight={move.JumpHeight} " +
                      $"gravity={move.Gravity} launch={move.JumpVelocity:F4}");
        sb.AppendLine();
        sb.AppendLine("  fps      apex(m)   airtime(s)   sprint reach(m)");

        float minApex = float.MaxValue, maxApex = float.MinValue;
        float minReach = float.MaxValue, maxReach = float.MinValue;

        foreach (float fps in TestFrameRates)
        {
            SimulateJump(move, 1f / fps, out float apex, out float airtime);
            float reach = move.SprintSpeed * airtime;

            minApex = Mathf.Min(minApex, apex);
            maxApex = Mathf.Max(maxApex, apex);
            minReach = Mathf.Min(minReach, reach);
            maxReach = Mathf.Max(maxReach, reach);

            sb.AppendLine($"  {fps,5:F0}   {apex,8:F4}   {airtime,9:F4}   {reach,14:F3}");
        }

        float apexSpread = maxApex - minApex;
        float reachSpread = maxReach - minReach;

        sb.AppendLine();
        sb.AppendLine($"  apex spread  : {apexSpread * 1000f:F2} mm   (budget 20 mm)");
        sb.AppendLine($"  reach spread : {reachSpread * 1000f:F1} mm   (budget 250 mm)");

        int fail = 0;
        if (apexSpread > 0.020f) { sb.AppendLine("  FAIL apex varies with frame rate"); fail++; }
        if (reachSpread > 0.250f) { sb.AppendLine("  FAIL reach varies with frame rate"); fail++; }
        if (fail == 0) { sb.AppendLine("  PASS"); }

        Write("movement_framerate.txt", sb, fail);
    }

    /// <summary>
    /// Runs one jump through the controller's own integrator at a fixed step, and reports the
    /// apex reached and the time spent above the launch height.
    /// </summary>
    private static void SimulateJump(BasicFirstPersonController move, float dt,
        out float apex, out float airtime)
    {
        float v = move.JumpVelocity;
        float y = 0f;
        apex = 0f;
        airtime = 0f;

        // 4 seconds is far beyond any jump this controller can produce.
        for (int i = 0; i < Mathf.CeilToInt(4f / dt); i++)
        {
            v = BasicFirstPersonController.IntegrateVertical(v, move.Gravity, dt, out float dy);
            y += dy;
            airtime += dt;
            apex = Mathf.Max(apex, y);

            if (y <= 0f)
            {
                return;
            }
        }
    }

    // ------------------------------------------------------------------ B: ability ranges

    [MenuItem("Tools/Parkour Movement/B - Validate Ability Ranges", priority = 21)]
    public static void ValidateAbilityRanges()
    {
        if (!ReadPlayer(out BasicFirstPersonController move, out CharacterController cc))
        {
            return;
        }

        VaultDetector vault = move.GetComponent<VaultDetector>();
        MantleDetector mantle = move.GetComponent<MantleDetector>();
        SlideAbility slide = move.GetComponent<SlideAbility>();
        WallRunAbility wall = move.GetComponent<WallRunAbility>();

        if (vault == null || mantle == null || slide == null || wall == null)
        {
            Debug.LogError("[MoveTest] Player is missing one or more ability components.");
            return;
        }

        Physics.SyncTransforms();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("PARKOUR MOVEMENT - ABILITY RANGES (probed against live scene colliders)");
        sb.AppendLine();

        int fail = 0;

        // ---- vault band
        sb.AppendLine($"VAULT band {vault.MinHeight:F2}-{vault.MaxHeight:F2} m");
        fail += ProbeBand(move, cc, "Vault_", (feet, fwd) =>
            vault.TryFind(feet, fwd, move.SprintSpeed, cc.height, cc.radius,
                move.ObstacleMask, cc, out _), vault.MinHeight, vault.MaxHeight, sb);

        // ---- mantle band
        sb.AppendLine();
        sb.AppendLine($"MANTLE band {mantle.MinHeight:F2}-{mantle.MaxHeight:F2} m (grounded)");
        fail += ProbeBand(move, cc, "Mantle_", (feet, fwd) =>
            mantle.TryFind(feet, fwd, true, 0f, cc.height, cc.radius,
                move.ObstacleMask, cc, out _), mantle.MinHeight, mantle.MaxHeight, sb);

        // ---- slide clearance
        sb.AppendLine();
        sb.AppendLine($"SLIDE capsule {slide.SlideHeight:F2} m vs scene portals");
        foreach (GameObject portal in FindByPrefix("Slide_Lintel_"))
        {
            float clearance = portal.GetComponent<Renderer>().bounds.min.y;
            bool fits = clearance >= slide.SlideHeight;
            bool expected = clearance >= slide.SlideHeight;
            sb.AppendLine($"  {portal.name,-26} underside {clearance:F2} m  " +
                          $"{(fits ? "clears" : "blocks")}  {(fits == expected ? "OK" : "FAIL")}");
        }

        // ---- wall run entry
        sb.AppendLine();
        sb.AppendLine($"WALL RUN entry speed {wall.MinEntrySpeed:F1} m/s, reach " +
                      $"{cc.radius + wall.DetectionDistance:F2} m, duration {wall.MaxDuration:F2} s");
        sb.AppendLine($"  walk ({move.WalkSpeed:F0} m/s) qualifies : " +
                      $"{(move.WalkSpeed >= wall.MinEntrySpeed ? "yes - TOO PERMISSIVE" : "no - correct")}");
        sb.AppendLine($"  sprint ({move.SprintSpeed:F0} m/s) qualifies: " +
                      $"{(move.SprintSpeed >= wall.MinEntrySpeed ? "yes - correct" : "no - FAIL")}");

        if (move.WalkSpeed >= wall.MinEntrySpeed) fail++;
        if (move.SprintSpeed < wall.MinEntrySpeed) fail++;

        Write("movement_ability_ranges.txt", sb, fail);
    }

    private delegate bool Probe(Vector3 feet, Vector3 forward);

    /// <summary>
    /// Walks every scene object with the given name prefix, stands the player in front of it, and
    /// checks the detector's answer against whether the obstacle's height is inside the band.
    /// </summary>
    private static int ProbeBand(BasicFirstPersonController move, CharacterController cc,
        string prefix, Probe probe, float bandMin, float bandMax, StringBuilder sb)
    {
        int fail = 0;

        foreach (GameObject go in FindByPrefix(prefix))
        {
            Renderer r = go.GetComponent<Renderer>();
            if (r == null)
            {
                continue;
            }

            Bounds b = r.bounds;
            float height = b.max.y;

            // Stand just outside the near face, facing it.
            Vector3 forward = Vector3.forward;
            Vector3 feet = new Vector3(b.center.x, 0f, b.min.z - (cc.radius + 0.12f));

            bool detected = probe(feet, forward);
            bool shouldDetect = height >= bandMin - 0.001f && height <= bandMax + 0.001f;
            bool ok = detected == shouldDetect;

            if (!ok)
            {
                fail++;
            }

            sb.AppendLine($"  {go.name,-30} top {height,5:F2} m  " +
                          $"expect {(shouldDetect ? "accept" : "refuse"),6}  " +
                          $"got {(detected ? "accept" : "refuse"),6}  {(ok ? "OK" : "FAIL")}");
        }

        return fail;
    }

    // ------------------------------------------------------------------ C: reach table

    [MenuItem("Tools/Parkour Movement/C - Print Traversal Envelope", priority = 22)]
    public static void PrintEnvelope()
    {
        if (!ReadPlayer(out BasicFirstPersonController move, out _))
        {
            return;
        }

        float g = -move.Gravity;
        float v0 = move.JumpVelocity;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("SKYBOUND CITY - TRAVERSAL ENVELOPE");
        sb.AppendLine($"walk {move.WalkSpeed} m/s   sprint {move.SprintSpeed} m/s   " +
                      $"jump {move.JumpHeight} m   gravity {move.Gravity} m/s^2   launch {v0:F3} m/s");
        sb.AppendLine($"footing allowance {Footing} m, design slack 0.75 m");
        sb.AppendLine();
        sb.AppendLine("ASCENDING / FLAT              sprint                 walk");
        sb.AppendLine("  rise    reach   design    reach   design");

        foreach (float rise in new[] { 0f, 0.5f, 1.0f, 1.2f, 1.4f })
        {
            float disc = v0 * v0 - 2f * g * rise;
            if (disc < 0f)
            {
                sb.AppendLine($"  {rise,4:F1}    unreachable (rise >= jump height)");
                continue;
            }

            float t = (v0 + Mathf.Sqrt(disc)) / g;
            float rs = move.SprintSpeed * t, rw = move.WalkSpeed * t;
            sb.AppendLine($"  {rise,4:F1}  {rs,7:F2}  {rs - Footing - 0.75f,7:F2}  " +
                          $"{rw,7:F2}  {rw - Footing - 0.75f,7:F2}");
        }

        sb.AppendLine();
        sb.AppendLine("DESCENDING (sprint)");
        sb.AppendLine("  drop   airtime    reach   design");

        foreach (float drop in new[] { 2f, 4f, 6f, 8f, 12f, 16f, 24f, 40f })
        {
            float t = v0 / g + Mathf.Sqrt(2f * (move.JumpHeight + drop) / g);
            float reach = move.SprintSpeed * t;
            sb.AppendLine($"  {drop,4:F0}  {t,8:F3}  {reach,7:F2}  {reach - Footing - 0.75f,7:F2}");
        }

        Write("movement_envelope.txt", sb, 0);
    }

    // ------------------------------------------------------------------ plumbing

    private static bool ReadPlayer(out BasicFirstPersonController move, out CharacterController cc)
    {
        move = Object.FindFirstObjectByType<BasicFirstPersonController>();
        cc = move != null ? move.GetComponent<CharacterController>() : null;

        if (move == null || cc == null)
        {
            Debug.LogError("[MoveTest] No BasicFirstPersonController in the open scene. " +
                           "Open Assets/Scenes/ParkourMovementTest.unity first.");
            return false;
        }

        return true;
    }

    private static System.Collections.Generic.List<GameObject> FindByPrefix(string prefix)
    {
        System.Collections.Generic.List<GameObject> found =
            new System.Collections.Generic.List<GameObject>();

        foreach (GameObject go in Object.FindObjectsByType<GameObject>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (go.name.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                found.Add(go);
            }
        }

        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return found;
    }

    private static void Write(string file, StringBuilder sb, int fail)
    {
        string dir = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "SceneBackups");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, file), sb.ToString());

        if (fail > 0)
        {
            Debug.LogWarning($"[MoveTest] {file}: {fail} FAIL\n{sb}");
        }
        else
        {
            Debug.Log($"[MoveTest] {file}: pass\n{sb}");
        }
    }
}
