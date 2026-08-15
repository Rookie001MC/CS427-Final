using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Validates the FULL Level 1 route - the original 34 jumps plus the 20 Stage 7/8 jumps - by
/// driving the real CharacterController against the real colliders in edit mode, mirroring
/// BasicFirstPersonController.HandleMovement statement for statement (including sampling
/// isGrounded BEFORE the frame's moves) and steering toward the landing pad rather than
/// holding W through the whole flight.
///
/// Also re-checks every checkpoint, the fall/respawn line, and sweeps for decorative colliders
/// that could catch a faller or form a shortcut.
///
/// Writes SceneBackups/level1_full_report.txt. Read-only with respect to the scene: the player
/// is restored to its authored spawn at the end.
/// </summary>
public static class Level1FullHarness
{
    private const float Footing = 0.4f;
    private const float Dt = 1f / 60f;
    private static float walk, sprint, jumpHeight, gravity, fallReset, launch;

    private static readonly string[] Route =
    {
        // ---- original Level 1 (34 jumps)
        "Platform_Start","Plat_S1","Plat_S2","Plat_S3","Plat_S4","Plat_S5","Plat_S6","Plat_S7",
        "Plat_S8_Pillar","Deck_North","Plat_B1","Plat_B2","Plat_B3","Plat_B4","Plat_B5","Plat_B6",
        "Plat_B7_Beam","Plat_B8_Ledge","Plat_B9_Ledge","Deck_South","Plat_K1","Plat_K2","Plat_K3",
        "Plat_K4","Plat_K5","Plat_K6","Plat_K7_TowerShelf","Ledge_W1","Ledge_W2","Ledge_W3",
        "Ledge_N1","Ledge_N2","Ledge_N3","Ledge_E1","Finish_TowerCap",
        // ---- Stage 7 (9 jumps)
        "T7_Span_A","T7_Vent_B","T7_Beam_C","T7_Roof_D","T7_Plat_E","T7_Span_F","T7_Vent_G",
        "T7_Beam_H","T7_Deck_I",
        // ---- Stage 8 (11 jumps)
        "T8_Ledge_01","T8_Ledge_02","T8_Ledge_03","T8_Ledge_04","T8_Ledge_05","T8_Ledge_06",
        "T8_Ledge_07","T8_Ledge_08","T8_Ledge_09","T8_MastShelf","T8_Summit",
    };

    private static readonly HashSet<string> SprintFrom = new HashSet<string>
    {
        "Plat_B1","Plat_B2","Plat_K1","Plat_K2","Plat_K3","Plat_K4","Plat_K5","Plat_K7_TowerShelf",
        "Finish_TowerCap","T7_Plat_E",
    };

    private static GameObject F(string n) => GameObject.Find(n);

    [MenuItem("Tools/Parkour/F - Validate FULL Route (54 jumps)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogError("[Full] ABORT - exit play mode first."); return; }

        GameObject player = F("FPP_Player");
        if (player == null) { Debug.LogError("[Full] FPP_Player not found."); return; }
        CharacterController cc = player.GetComponent<CharacterController>();
        BasicFirstPersonController fpp = player.GetComponent<BasicFirstPersonController>();
        SerializedObject pso = new SerializedObject(fpp);
        walk = pso.FindProperty("walkSpeed").floatValue;
        sprint = pso.FindProperty("sprintSpeed").floatValue;
        jumpHeight = pso.FindProperty("jumpHeight").floatValue;
        gravity = pso.FindProperty("gravity").floatValue;
        fallReset = pso.FindProperty("fallResetHeight").floatValue;
        launch = Mathf.Sqrt(jumpHeight * -2f * gravity);

        Physics.SyncTransforms();
        Vector3 restore = player.transform.position;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== LEVEL 1 FULL ROUTE VALIDATION (original 34 + Stage 7/8 20 = 54 jumps) ===");
        sb.AppendLine($"walk={walk} sprint={sprint} jumpHeight={jumpHeight} gravity={gravity} "
                    + $"fallReset={fallReset} launch={launch:F3} footing={Footing}");
        sb.AppendLine();

        int fail = 0, tight = 0, warn = 0;

        // ---------------------------------------------------------------- A. reachability
        sb.AppendLine("--- A. reachability (analytic) ---");
        sb.AppendLine("jump                                          rise   gap   reach  slack  speed  verdict");
        for (int i = 0; i < Route.Length - 1; i++)
        {
            GameObject a = F(Route[i]), b = F(Route[i + 1]);
            if (a == null || b == null) { sb.AppendLine($"{Route[i]} -> {Route[i + 1]}: MISSING"); fail++; continue; }
            Bounds ba = a.GetComponent<Renderer>().bounds, bb = b.GetComponent<Renderer>().bounds;
            float rise = bb.max.y - ba.max.y;
            float dx = Mathf.Max(0f, Mathf.Max(ba.min.x - bb.max.x, bb.min.x - ba.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(ba.min.z - bb.max.z, bb.min.z - ba.max.z));
            float gap = Mathf.Sqrt(dx * dx + dz * dz);
            bool sp = SprintFrom.Contains(Route[i]);
            float speed = sp ? sprint : walk;

            string v; float reach, slack;
            if (rise >= jumpHeight) { reach = 0f; slack = -999f; v = "FAIL rise>=jumpHeight"; fail++; }
            else if (rise <= 0.30f && gap <= 0.01f) { reach = 0f; slack = 999f; v = "OK walk-up"; }
            else
            {
                float g = -gravity;
                float disc = launch * launch - 2f * g * rise;
                float t = (launch + Mathf.Sqrt(Mathf.Max(0f, disc))) / g;
                reach = speed * t; slack = reach - Footing - gap;
                if (slack < 0f) { v = "FAIL unreachable"; fail++; }
                else if (slack < 0.75f) { v = "WARN tight"; tight++; }
                else v = "OK";
            }
            sb.AppendLine($"{Route[i],-22} -> {Route[i + 1],-20} {rise,5:F2} {gap,5:F2} {reach,6:F2} {slack,6:F2}  "
                        + $"{(sp ? "sprint" : "walk  ")} {v}");
        }

        // ---------------------------------------------------------------- B. real controller run
        sb.AppendLine();
        sb.AppendLine("--- B. route run: real CharacterController, real colliders ---");
        sb.AppendLine("leg                                           speed   rise  gap   apex  landed on            verdict");
        int legFail = 0, legWarn = 0;
        for (int i = 0; i < Route.Length - 1; i++)
        {
            GameObject a = F(Route[i]), b = F(Route[i + 1]);
            if (a == null || b == null) continue;
            Bounds from = a.GetComponent<Renderer>().bounds, to = b.GetComponent<Renderer>().bounds;
            Vector3 toC = new Vector3(to.center.x, to.max.y, to.center.z);
            Vector3 takeoff = Inset(ClosestOnTop(from, toC), from, 0.5f);
            Vector3 fromC = new Vector3(from.center.x, from.max.y, from.center.z);
            Vector3 landing = Inset(ClosestOnTop(to, fromC), to, 0.6f);

            bool sp = SprintFrom.Contains(Route[i]);
            float speed = sp ? sprint : walk;
            float rise = to.max.y - from.max.y;
            float dxx = Mathf.Max(0f, Mathf.Max(from.min.x - to.max.x, to.min.x - from.max.x));
            float dzz = Mathf.Max(0f, Mathf.Max(from.min.z - to.max.z, to.min.z - from.max.z));
            float gap = Mathf.Sqrt(dxx * dxx + dzz * dzz);

            cc.enabled = false;
            player.transform.position = takeoff + Vector3.up * 0.06f;
            cc.enabled = true;
            Physics.SyncTransforms();

            float vs = -2f;
            for (int k = 0; k < 12; k++) cc.Move(Vector3.up * vs * Dt);

            float startY = player.transform.position.y, apex = startY;
            bool consumed = false, airborne = false, landed = false, fell = false;
            int frames = 0;
            while (frames < 420)
            {
                frames++;
                Vector3 flat = landing - player.transform.position; flat.y = 0f;
                bool push = flat.magnitude > 0.15f;
                if (push) player.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
                bool space = !airborne && frames <= 4;

                bool grounded = cc.isGrounded;                     // sampled before this frame's moves
                if (frames > 1 && push) cc.Move(player.transform.forward * speed * Dt);
                if (grounded && vs < 0f) { vs = -2f; consumed = false; }
                if (grounded && !consumed && space) { vs = launch; consumed = true; }
                vs += gravity * Dt;
                cc.Move(Vector3.up * vs * Dt);

                Vector3 p = player.transform.position;
                apex = Mathf.Max(apex, p.y);
                if (frames <= 2) continue;
                if (!cc.isGrounded) airborne = true;
                else if (airborne) { landed = true; break; }
                if (p.y < fallReset) { fell = true; break; }
            }

            string under = GroundUnder(player.transform.position);
            string verdict;
            if (fell) { verdict = $"FAIL fell (apex {apex - startY:F2})"; legFail++; }
            else if (!landed) { verdict = $"FAIL never landed (y={player.transform.position.y:F2})"; legFail++; }
            else if (under == Route[i + 1]) verdict = $"OK  (rise used {apex - startY:F2} m)";
            else if (SameSurface(under, Route[i + 1])) verdict = $"OK  (on '{under}', same surface as target)";
            else if (under == Route[i]) { verdict = "FAIL landed back on takeoff"; legFail++; }
            else { verdict = $"WARN landed on '{under}'"; legWarn++; }

            sb.AppendLine($"{Route[i],-20} -> {Route[i + 1],-20} {(sp ? "sprint" : "walk  ")} {rise,5:F2} {gap,5:F2} "
                        + $"{apex - startY,5:F2}  {under,-20} {verdict}");
        }
        fail += legFail; warn += legWarn;

        cc.enabled = false; player.transform.position = restore; cc.enabled = true;
        Physics.SyncTransforms();

        // ---------------------------------------------------------------- C. checkpoints
        sb.AppendLine();
        sb.AppendLine("--- C. checkpoints ---");
        var vols = Object.FindObjectsByType<CheckpointVolume>(FindObjectsSortMode.None);
        sb.AppendLine($"  checkpoint volumes in scene: {vols.Length}");
        foreach (var cv in vols)
        {
            SerializedObject cso = new SerializedObject(cv);
            Transform rp = cso.FindProperty("respawnPoint").objectReferenceValue as Transform;
            Collider col = cv.GetComponent<Collider>();
            bool ok = col != null && col.isTrigger && rp != null;
            if (!ok) { warn++; }
            string standing = rp != null ? GroundUnder(rp.position + Vector3.up * 0.4f) : "<none>";
            sb.AppendLine($"  {cv.gameObject.name,-22} trigger={(col != null && col.isTrigger)} "
                        + $"respawn={(rp == null ? "<null>" : rp.position.ToString())} over='{standing}' -> {(ok ? "OK" : "PROBLEM")}");
        }

        // ---------------------------------------------------------------- D. fall / respawn safety
        sb.AppendLine();
        sb.AppendLine("--- D. fall corridor: solid colliders that could catch a faller (top between fallReset and 0) ---");
        int catchers = 0;
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (col.GetComponent<CharacterController>() != null || col.isTrigger) continue;
            Bounds b = col.bounds;
            if (b.max.y < 0f && b.max.y > fallReset)
            { sb.AppendLine($"  {col.gameObject.name} top {b.max.y:F2} (group {(col.transform.parent == null ? "-" : col.transform.parent.name)})"); catchers++; }
        }
        sb.AppendLine($"  count={catchers}");

        // ---------------------------------------------------------------- E. shortcut sweep
        sb.AppendLine();
        sb.AppendLine("--- E. decorative colliders in the new content (shortcut risk) ---");
        int decorCols = 0;
        foreach (string gn in new[] { "ENV_DISTRICT_EAST", "EDGE_LIPS_EXT", "ENV_LOWERCITY", "ENV_SKYLINE" })
        {
            GameObject g = F(gn);
            if (g == null) continue;
            int c = g.GetComponentsInChildren<Collider>(true).Length;
            sb.AppendLine($"  {gn,-20} colliders={c}");
            decorCols += c;
        }
        sb.AppendLine($"  total decorative colliders={decorCols} -> {(decorCols == 0 ? "no shortcut surfaces" : "REVIEW")}");
        if (decorCols != 0) warn++;

        sb.AppendLine();
        sb.AppendLine($"=== RESULT: {Route.Length - 1} jumps | {fail} failure(s), {tight} tight, {warn} warning(s) ===");

        string dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "SceneBackups");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "level1_full_report.txt"), sb.ToString());
        Debug.Log($"[Full] {Route.Length - 1} jumps: {fail} fail, {tight} tight, {warn} warn. Report written.");
    }

    private static bool SameSurface(string under, string target)
    {
        if (string.IsNullOrEmpty(under) || under == "<none>") return false;
        GameObject u = F(under), t = F(target);
        if (u == null || t == null) return false;
        Renderer ru = u.GetComponent<Renderer>(), rt = t.GetComponent<Renderer>();
        if (ru == null || rt == null) return false;
        Bounds bu = ru.bounds, bt = rt.bounds;
        bool overlap = bu.max.x > bt.min.x && bu.min.x < bt.max.x && bu.max.z > bt.min.z && bu.min.z < bt.max.z;
        return overlap && Mathf.Abs(bu.max.y - bt.max.y) < 0.35f;
    }

    private static string GroundUnder(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit, 1.4f, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.gameObject.name;
        return "<none>";
    }

    private static Vector3 ClosestOnTop(Bounds r, Vector3 to) => new Vector3(
        Mathf.Clamp(to.x, r.min.x, r.max.x), r.max.y, Mathf.Clamp(to.z, r.min.z, r.max.z));

    private static Vector3 Inset(Vector3 p, Bounds r, float amt)
    {
        Vector3 c = new Vector3(r.center.x, r.max.y, r.center.z);
        Vector3 d = c - p; d.y = 0f;
        if (d.sqrMagnitude < 1e-5f) return p;
        return p + d.normalized * Mathf.Min(amt, d.magnitude);
    }
}
