using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Validator for Level 2 (IndustrialParkour). Two independent checks, both in edit mode:
///
///  1. Reachability - the analytic reach formula, identical to ParkourLevelBuilder.Step8.
///  2. Route run    - drives the real CharacterController against the real colliders, mirroring
///                    BasicFirstPersonController.HandleMovement statement for statement
///                    (including sampling isGrounded BEFORE the frame's moves), steering toward
///                    the landing pad rather than holding W for the whole flight.
///
/// Writes SceneBackups/industrial_report.txt.
/// </summary>
public static class IndustrialRouteHarness
{
    private const float Footing = 0.4f;
    private const float Dt = 1f / 60f;

    private static float walk, sprint, jumpHeight, gravity, launch;

    private static GameObject F(string n) => GameObject.Find(n);

    [MenuItem("Tools/Industrial/A - Validate Reachability")]
    public static void Reachability()
    {
        if (!ReadPlayer(out _, out _)) return;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== LEVEL 2 REACHABILITY ===");
        sb.AppendLine($"walk={walk} sprint={sprint} jumpHeight={jumpHeight} gravity={gravity} launch={launch:F3}");
        sb.AppendLine($"footing allowance={Footing} m");
        sb.AppendLine();
        sb.AppendLine("jump                                          rise   gap   reach  slack  speed  verdict");

        int fail = 0, tight = 0;
        var rows = IndustrialLevelBuilder.Rows;
        for (int i = 0; i < rows.Count - 1; i++)
        {
            string from = rows[i].Name, to = rows[i + 1].Name;
            GameObject a = F(from), b = F(to);
            if (a == null || b == null) { sb.AppendLine($"{from} -> {to}: MISSING"); fail++; continue; }

            Bounds ba = a.GetComponent<Renderer>().bounds, bb = b.GetComponent<Renderer>().bounds;
            float rise = bb.max.y - ba.max.y;
            float dx = Mathf.Max(0f, Mathf.Max(ba.min.x - bb.max.x, bb.min.x - ba.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(ba.min.z - bb.max.z, bb.min.z - ba.max.z));
            float gap = Mathf.Sqrt(dx * dx + dz * dz);
            bool isSprint = System.Array.IndexOf(IndustrialLevelBuilder.SprintFrom, from) >= 0;
            float speed = isSprint ? sprint : walk;

            string verdict; float reach, slack;
            if (rise >= jumpHeight) { reach = 0f; slack = -999f; verdict = "FAIL rise>=jumpHeight"; fail++; }
            else if (rise <= 0.30f && gap <= 0.01f) { reach = 0f; slack = 999f; verdict = "OK walk-up"; }
            else
            {
                float g = -gravity;
                float disc = launch * launch - 2f * g * rise;
                float t = (launch + Mathf.Sqrt(Mathf.Max(0f, disc))) / g;
                reach = speed * t;
                slack = reach - Footing - gap;
                if (slack < 0f) { verdict = "FAIL unreachable"; fail++; }
                else if (slack < 0.75f) { verdict = "WARN tight"; tight++; }
                else verdict = "OK";
            }
            sb.AppendLine($"{from,-22} -> {to,-20} {rise,5:F2} {gap,5:F2} {reach,6:F2} {slack,6:F2}  {(isSprint ? "sprint" : "walk  ")} {verdict}");
        }
        sb.AppendLine($"--- {rows.Count - 1} jumps checked: {fail} fail, {tight} tight ---");
        Write("industrial_reachability.txt", sb, fail, tight);
    }

    // ---------------------------------------------------------------- route run

    [MenuItem("Tools/Industrial/B - Run Route Harness")]
    public static void RouteRun()
    {
        if (!ReadPlayer(out GameObject player, out CharacterController cc)) return;

        // The builder moved every collider by transform. In edit mode PhysX does not see those
        // moves until an explicit sync, so without this every raycast and sweep hits nothing.
        Physics.SyncTransforms();

        Vector3 restore = player.transform.position;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== LEVEL 2 ROUTE HARNESS (edit mode, real CharacterController) ===");
        sb.AppendLine($"walk={walk} sprint={sprint} jumpHeight={jumpHeight} gravity={gravity} launch={launch:F3} dt={Dt:F5}");
        sb.AppendLine();

        int fail = 0, warn = 0;
        sb.AppendLine("--- stance clearance (blockers taller than stepOffset 0.30) ---");
        foreach (var row in IndustrialLevelBuilder.Rows)
        {
            GameObject go = F(row.Name);
            if (go == null) continue;
            Bounds bd = go.GetComponent<Renderer>().bounds;
            Vector3 feet = new Vector3(bd.center.x, bd.max.y + 0.06f, bd.center.z);
            var hits = Physics.OverlapCapsule(feet + Vector3.up * 0.37f, feet + Vector3.up * 1.63f, 0.33f,
                ~0, QueryTriggerInteraction.Ignore);
            List<string> blockers = new List<string>();
            foreach (var c in hits)
            {
                if (c.GetComponent<CharacterController>() != null) continue;
                if (c.bounds.max.y <= bd.max.y + 0.30f) continue;      // steppable, not a blocker
                if (!blockers.Contains(c.gameObject.name)) blockers.Add(c.gameObject.name);
            }
            if (blockers.Count > 0)
            {
                sb.AppendLine($"  {row.Name}: centre stance blocked by [{string.Join(", ", blockers)}]");
                warn++;
            }
        }
        if (warn == 0) sb.AppendLine("  all centre stances clear");
        sb.AppendLine();

        sb.AppendLine("--- jump run: mirrors HandleMovement, real Move/collision ---");
        sb.AppendLine("leg                                           speed   rise  gap   apex  landed on            verdict");

        var rows = IndustrialLevelBuilder.Rows;
        for (int i = 0; i < rows.Count - 1; i++)
        {
            string fromName = rows[i].Name, toName = rows[i + 1].Name;
            GameObject a = F(fromName), b = F(toName);
            if (a == null || b == null) { sb.AppendLine($"{fromName} -> {toName}: MISSING"); fail++; continue; }

            Bounds from = a.GetComponent<Renderer>().bounds, to = b.GetComponent<Renderer>().bounds;
            Vector3 toC = new Vector3(to.center.x, to.max.y, to.center.z);
            Vector3 takeoff = Inset(ClosestOnTop(from, toC), from, 0.3f);
            Vector3 fromC = new Vector3(from.center.x, from.max.y, from.center.z);
            Vector3 landing = Inset(ClosestOnTop(to, fromC), to, 0.6f);

            bool isSprint = System.Array.IndexOf(IndustrialLevelBuilder.SprintFrom, fromName) >= 0;
            float speed = isSprint ? sprint : walk;
            float rise = to.max.y - from.max.y;
            float dx = Mathf.Max(0f, Mathf.Max(from.min.x - to.max.x, to.min.x - from.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(from.min.z - to.max.z, to.min.z - from.max.z));
            float gap = Mathf.Sqrt(dx * dx + dz * dz);

            cc.enabled = false;
            player.transform.position = takeoff + Vector3.up * 0.06f;
            cc.enabled = true;
            Physics.SyncTransforms();

            float verticalSpeed = -2f;
            for (int s = 0; s < 12; s++) { cc.Move(Vector3.up * verticalSpeed * Dt); }

            float startY = player.transform.position.y, apex = startY;
            bool jumpConsumed = false, airborne = false, landed = false, fell = false;
            int frames = 0;
            const int maxFrames = 420;

            while (frames < maxFrames)
            {
                frames++;
                Vector3 flat = landing - player.transform.position; flat.y = 0f;
                bool push = flat.magnitude > 0.15f;
                if (push) player.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);

                bool spaceHeld = !airborne && frames <= 4;

                // ---- mirror of BasicFirstPersonController.HandleMovement ----
                bool grounded = cc.isGrounded;                 // sampled BEFORE this frame's moves
                if (frames > 1 && push) cc.Move(player.transform.forward * speed * Dt);
                if (grounded && verticalSpeed < 0f) { verticalSpeed = -2f; jumpConsumed = false; }
                if (grounded && !jumpConsumed && spaceHeld) { verticalSpeed = launch; jumpConsumed = true; }
                verticalSpeed += gravity * Dt;
                cc.Move(Vector3.up * verticalSpeed * Dt);
                // ---- end mirror ----

                Vector3 p = player.transform.position;
                apex = Mathf.Max(apex, p.y);
                if (frames <= 2) continue;
                if (!cc.isGrounded) airborne = true;
                else if (airborne) { landed = true; break; }
                if (p.y < -11f) { fell = true; break; }
            }

            string under = GroundUnder(player.transform.position);
            string verdict;
            if (fell) { verdict = $"FAIL fell (apex {apex - startY:F2})"; fail++; }
            else if (!landed) { verdict = $"FAIL never landed (y={player.transform.position.y:F2})"; fail++; }
            else if (under == toName) verdict = $"OK  (rise used {apex - startY:F2} m)";
            else if (IsPartOf(under, toName)) verdict = $"OK  (on '{under}', part of target)";
            else if (under == fromName) { verdict = "FAIL landed back on takeoff"; fail++; }
            else { verdict = $"WARN landed on '{under}'"; warn++; }

            sb.AppendLine($"{fromName,-20} -> {toName,-20} {(isSprint ? "sprint" : "walk  ")} {rise,5:F2} {gap,5:F2} {apex - startY,5:F2}  {under,-20} {verdict}");
        }

        cc.enabled = false;
        player.transform.position = restore;
        cc.enabled = true;

        sb.AppendLine();
        sb.AppendLine($"=== RESULT: {fail} failure(s), {warn} warning(s) ===");
        Write("industrial_report.txt", sb, fail, warn);
    }

    /// <summary>A landing counts if it is the target, or a prop sitting on/next to the target.</summary>
    private static bool IsPartOf(string under, string target)
    {
        if (string.IsNullOrEmpty(under) || under == "<none>") return false;
        GameObject u = F(under), t = F(target);
        if (u == null || t == null) return false;
        if (u.transform.parent != null && t.transform.parent != null &&
            u.transform.parent == t.transform.parent)
        {
            // same stage group: accept lips/stripes/brackets that overlap the pad footprint
            Bounds bu = u.GetComponent<Renderer>().bounds, bt = t.GetComponent<Renderer>().bounds;
            bool overlap = bu.max.x > bt.min.x && bu.min.x < bt.max.x &&
                           bu.max.z > bt.min.z && bu.min.z < bt.max.z;
            if (overlap && Mathf.Abs(bu.max.y - bt.max.y) < 0.35f) return true;
        }
        return false;
    }

    private static string GroundUnder(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit, 1.4f, ~0,
            QueryTriggerInteraction.Ignore))
            return hit.collider.gameObject.name;
        return "<none>";
    }

    private static Vector3 ClosestOnTop(Bounds rect, Vector3 to) => new Vector3(
        Mathf.Clamp(to.x, rect.min.x, rect.max.x), rect.max.y, Mathf.Clamp(to.z, rect.min.z, rect.max.z));

    private static Vector3 Inset(Vector3 point, Bounds rect, float amount)
    {
        Vector3 centre = new Vector3(rect.center.x, rect.max.y, rect.center.z);
        Vector3 d = centre - point; d.y = 0f;
        if (d.sqrMagnitude < 1e-5f) return point;
        return point + d.normalized * Mathf.Min(amount, d.magnitude);
    }

    private static bool ReadPlayer(out GameObject player, out CharacterController cc)
    {
        player = F("FPP_Player"); cc = null;
        if (player == null) { Debug.LogError("[Industrial] FPP_Player not found - open IndustrialParkour."); return false; }
        cc = player.GetComponent<CharacterController>();
        BasicFirstPersonController fpp = player.GetComponent<BasicFirstPersonController>();
        if (cc == null || fpp == null) { Debug.LogError("[Industrial] player is missing components."); return false; }

        SerializedObject so = new SerializedObject(fpp);
        walk = so.FindProperty("walkSpeed").floatValue;
        sprint = so.FindProperty("sprintSpeed").floatValue;
        jumpHeight = so.FindProperty("jumpHeight").floatValue;
        gravity = so.FindProperty("gravity").floatValue;
        launch = Mathf.Sqrt(jumpHeight * -2f * gravity);
        return true;
    }

    private static void Write(string file, StringBuilder sb, int fail, int warn)
    {
        string dir = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "SceneBackups");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, file), sb.ToString());
        Debug.Log($"[Industrial] {file}: {fail} fail, {warn} warn\n{sb}");
    }
}
