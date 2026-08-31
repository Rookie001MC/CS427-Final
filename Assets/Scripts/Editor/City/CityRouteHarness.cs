using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Validation for Skybound City, in the idiom the project already uses: menu-driven, run against
/// the real colliders in the open scene, and written to a report in SceneBackups/.
///
/// The Phase 6A report recommended building this *before* any geometry, because every dimension in
/// that report is a prediction from a formula and a harness is what turns predictions into facts.
/// Three checks, deliberately independent:
///
///   A  Walkability - floods the street network from the spawn and reports how much of each
///      district it reached. This is the direct test of the Phase 6B exit criterion "player can
///      walk the whole 600 x 600 m at street level", and unlike a hand-authored path it cannot
///      miss a block by not thinking to visit it.
///   B  Massing - counts what is actually in the scene against the Phase 6A object budgets, and
///      prints the height bands and a top-down map so the silhouette can be judged from numbers
///      rather than from memory.
///   C  Routes - walks every street-level <see cref="CityRoutes"/> definition with the real
///      CharacterController against the real colliders. This is `IndustrialRouteHarness`
///      generalised from its hard-coded Route[] to named definitions, as the report asked.
///      Phase 6C's rooftop routes are probed rather than walked: their legs are mantles, bridges
///      and jumps, and the runner has no input to press, so walking one would only prove that a
///      player who never jumps cannot climb a fire escape. What C can prove about them - and what
///      no amount of plan arithmetic can - is that every surface the plan says they stand on is
///      really in the scene, at the height it was planned at, with room to stand. Whether each move
///      between those surfaces is inside the move set is measured by D.
/// </summary>
public static class CityRouteHarness
{
    private const float Dt = 1f / 60f;

    /// <summary>Flood-fill sample spacing. Fine enough to find a 3.5 m alley.</summary>
    private const float SampleStep = 2.5f;

    /// <summary>
    /// Largest height change accepted between adjacent samples, i.e. 24 degrees over
    /// <see cref="SampleStep"/>. Comfortably walkable, covers the 20 degree Cut ramp, and low
    /// enough that a 2 m ledge - which is a mantle, not a walk - is not counted as connected.
    /// </summary>
    private const float MaxStepUp = 1.1f;

    private const float ProbeTop = 3.5f;
    private const float ProbeBottom = -10.5f;

    /// <summary>
    /// How far a probed rooftop surface may sit from where the plan put it. A slab's own thickness
    /// is 0.25-0.6 m and the builder places surfaces exactly, so anything past this is a real
    /// disagreement between the plan and the scene rather than rounding.
    /// </summary>
    private const float SurfaceTolerance = 0.35f;

    // ------------------------------------------------------------------ A: walkability

    [MenuItem("Tools/Skybound City/A - Validate Street Walkability", priority = 20)]
    public static void ValidateWalkability()
    {
        if (!ReadPlayer(out BasicFirstPersonController move, out CharacterController cc))
        {
            return;
        }

        // Everything in this scene was placed by transform in edit mode, and PhysX does not see
        // those moves until an explicit sync. Without this every query below hits nothing.
        Physics.SyncTransforms();

        // The player's own CharacterController is a collider like any other, and it sits on the
        // one sample that matters most - the spawn. Take it out of the queries for the duration.
        bool wasEnabled = cc.enabled;
        cc.enabled = false;
        Physics.SyncTransforms();

        int n = Mathf.RoundToInt(CityDesign.CoreExtent / SampleStep);
        float origin = -CityDesign.CoreExtent * 0.5f + SampleStep * 0.5f;

        bool[,] walkable = new bool[n, n];
        float[,] height = new float[n, n];
        int walkableCount = 0;

        for (int ix = 0; ix < n; ix++)
        {
            for (int iz = 0; iz < n; iz++)
            {
                float x = origin + ix * SampleStep;
                float z = origin + iz * SampleStep;

                if (!Standable(x, z, cc, out float y))
                {
                    continue;
                }

                walkable[ix, iz] = true;
                height[ix, iz] = y;
                walkableCount++;
            }
        }

        cc.enabled = wasEnabled;

        // Flood from the spawn, so the answer is "reachable by the player", not "open ground".
        Vector3 spawn = move.transform.position;
        if (!NearestWalkable(walkable, origin, n, spawn, out int sx, out int sz))
        {
            Debug.LogError("[SkyboundCity] No walkable ground found - is the city built?");
            return;
        }

        bool[,] reached = new bool[n, n];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        reached[sx, sz] = true;
        queue.Enqueue(new Vector2Int(sx, sz));
        int reachedCount = 1;

        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };

        while (queue.Count > 0)
        {
            Vector2Int at = queue.Dequeue();

            for (int d = 0; d < 4; d++)
            {
                int nx = at.x + dx[d];
                int nz = at.y + dz[d];

                if (nx < 0 || nz < 0 || nx >= n || nz >= n)
                {
                    continue;
                }

                if (!walkable[nx, nz] || reached[nx, nz])
                {
                    continue;
                }

                if (Mathf.Abs(height[nx, nz] - height[at.x, at.y]) > MaxStepUp)
                {
                    continue;
                }

                reached[nx, nz] = true;
                reachedCount++;
                queue.Enqueue(new Vector2Int(nx, nz));
            }
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("SKYBOUND CITY - STREET WALKABILITY");
        sb.AppendLine($"sample step {SampleStep} m, grid {n} x {n}, max step up {MaxStepUp} m");
        sb.AppendLine($"spawn {spawn}");
        sb.AppendLine();
        sb.AppendLine($"walkable samples : {walkableCount}");
        sb.AppendLine($"reached from spawn: {reachedCount} " +
                      $"({(walkableCount > 0 ? reachedCount * 100f / walkableCount : 0f):F1} % of walkable)");
        sb.AppendLine();
        sb.AppendLine("district                  walkable  reached   %");

        int fail = 0;

        foreach (DistrictCell cell in CityDesign.Cells)
        {
            CountIn(cell.Bounds, walkable, reached, origin, n, out int open, out int got);
            float pct = open > 0 ? got * 100f / open : 0f;
            string flag = got == 0 ? "  *** UNREACHABLE" : pct < 90f ? "  ** partial" : string.Empty;

            if (got == 0)
            {
                fail++;
            }

            sb.AppendLine($"{cell.Name,-24} {open,9} {got,8}  {pct,5:F1}{flag}");
        }

        CountIn(CityPlan.CutBounds(), walkable, reached, origin, n, out int cutOpen, out int cutGot);
        sb.AppendLine($"{"The Cut (below street)",-24} {cutOpen,9} {cutGot,8}  " +
                      $"{(cutOpen > 0 ? cutGot * 100f / cutOpen : 0f),5:F1}");

        if (cutGot == 0)
        {
            sb.AppendLine("  *** the Cut floor is not reachable on foot - check the north ramp");
            fail++;
        }

        sb.AppendLine();
        sb.AppendLine(AsciiReachability(reached, walkable, n));
        sb.AppendLine();
        sb.AppendLine(fail == 0
            ? "RESULT: every district is reachable on foot from the spawn."
            : $"RESULT: {fail} area(s) unreachable.");

        Write("city_walkability.txt", sb, fail);
    }

    /// <summary>
    /// Is there a surface here a standing player could occupy? Two independent conditions: a
    /// surface inside the street band, and room for the capsule on top of it. The second is what
    /// rejects samples inside a building - a downward ray started inside a box collider passes
    /// straight through it and finds the pavement underneath.
    /// </summary>
    private static bool Standable(float x, float z, CharacterController cc, out float y)
    {
        y = 0f;

        if (!Physics.Raycast(new Vector3(x, ProbeTop, z), Vector3.down, out RaycastHit hit,
                ProbeTop - ProbeBottom, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        y = hit.point.y;

        if (y < ProbeBottom + 0.5f || y > 2.5f)
        {
            return false;
        }

        float radius = cc.radius * 0.9f;
        Vector3 bottom = new Vector3(x, y + radius + 0.05f, z);
        Vector3 top = new Vector3(x, y + cc.height - radius, z);

        return !Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);
    }

    private static bool NearestWalkable(bool[,] walkable, float origin, int n, Vector3 to,
        out int bx, out int bz)
    {
        bx = bz = -1;
        float best = float.MaxValue;

        for (int ix = 0; ix < n; ix++)
        {
            for (int iz = 0; iz < n; iz++)
            {
                if (!walkable[ix, iz])
                {
                    continue;
                }

                float ddx = origin + ix * SampleStep - to.x;
                float ddz = origin + iz * SampleStep - to.z;
                float d = ddx * ddx + ddz * ddz;

                if (d < best)
                {
                    best = d;
                    bx = ix;
                    bz = iz;
                }
            }
        }

        return bx >= 0;
    }

    private static void CountIn(CityRect rect, bool[,] walkable, bool[,] reached, float origin,
        int n, out int open, out int got)
    {
        open = 0;
        got = 0;

        for (int ix = 0; ix < n; ix++)
        {
            for (int iz = 0; iz < n; iz++)
            {
                if (!walkable[ix, iz])
                {
                    continue;
                }

                if (!rect.Contains(origin + ix * SampleStep, origin + iz * SampleStep))
                {
                    continue;
                }

                open++;

                if (reached[ix, iz])
                {
                    got++;
                }
            }
        }
    }

    private static string AsciiReachability(bool[,] reached, bool[,] walkable, int n)
    {
        const int cols = 60;
        int stride = Mathf.Max(1, n / cols);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("reachable street network ('#' reached, 'x' walkable but cut off, '.' built):");

        for (int iz = n - 1; iz >= 0; iz -= stride)
        {
            sb.Append("  ");

            for (int ix = 0; ix < n; ix += stride)
            {
                sb.Append(reached[ix, iz] ? '#' : walkable[ix, iz] ? 'x' : '.');
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------ B: massing

    [MenuItem("Tools/Skybound City/B - Massing Report", priority = 21)]
    public static void MassingReport()
    {
        GameObject world = GameObject.Find(CityKit.WorldRoot);

        if (world == null)
        {
            Debug.LogError("[SkyboundCity] No WORLD root - open Assets/Scenes/SkyboundCity.unity.");
            return;
        }

        int objects = world.GetComponentsInChildren<Transform>(true).Length;
        int renderers = world.GetComponentsInChildren<MeshRenderer>(true).Length;
        int colliders = world.GetComponentsInChildren<Collider>(true).Length;
        int lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
        int nonStatic = 0;

        foreach (Renderer r in world.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!r.gameObject.isStatic)
            {
                nonStatic++;
            }
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("SKYBOUND CITY - MASSING");
        sb.AppendLine();
        sb.AppendLine("budget (Phase 6A section 16)      actual   limit   verdict");

        int fail = 0;
        fail += Budget(sb, "GameObjects", objects, 4500);
        fail += Budget(sb, "MeshRenderers", renderers, 3800);
        fail += Budget(sb, "Colliders", colliders, 1100);
        fail += Budget(sb, "Realtime lights", lights, 10);
        fail += Budget(sb, "Renderers not static", nonStatic, 0);

        sb.AppendLine();
        sb.AppendLine("district                    n   roof min   roof max   band            coverage");

        foreach (DistrictCell cell in CityDesign.Cells)
        {
            Transform group = world.transform.Find(cell.Name);

            if (group == null)
            {
                sb.AppendLine($"{cell.Name,-24} (landmark - no lot grid)");
                continue;
            }

            float lo = float.MaxValue;
            float hi = float.MinValue;
            float area = 0f;
            int count = 0;

            foreach (MeshRenderer r in group.GetComponentsInChildren<MeshRenderer>(true))
            {
                Bounds b = r.bounds;
                lo = Mathf.Min(lo, b.max.y);
                hi = Mathf.Max(hi, b.max.y);
                area += b.size.x * b.size.z;
                count++;
            }

            if (count == 0)
            {
                continue;
            }

            bool inBand = lo >= cell.MinHeight - 0.05f && hi <= cell.MaxHeight + 0.05f;

            if (!inBand)
            {
                fail++;
            }

            sb.AppendLine($"{cell.Name,-24} {count,3} {lo,10:F2} {hi,10:F2}   " +
                          $"{cell.MinHeight,5:F1} - {cell.MaxHeight,5:F1}  {area / cell.Bounds.Area * 100f,7:F1} %" +
                          (inBand ? string.Empty : "   *** OUT OF BAND"));
        }

        Transform tower = world.transform.Find("SKYBOUND_TOWER");

        if (tower != null)
        {
            float top = 0f;

            foreach (MeshRenderer r in tower.GetComponentsInChildren<MeshRenderer>(true))
            {
                top = Mathf.Max(top, r.bounds.max.y);
            }

            sb.AppendLine();
            sb.AppendLine($"Skybound Tower summit: {top:F1} m (design {CityDesign.TowerTopY:F1} m)");

            if (Mathf.Abs(top - CityDesign.TowerTopY) > 0.05f)
            {
                fail++;
            }
        }

        sb.AppendLine();
        sb.AppendLine(DetailReport(world));
        sb.AppendLine();
        sb.AppendLine(AsciiSkyline(world));
        sb.AppendLine();
        sb.AppendLine(fail == 0 ? "RESULT: massing within budget and inside every height band."
                                : $"RESULT: {fail} problem(s).");

        Write("city_massing.txt", sb, fail);
    }

    private static int Budget(StringBuilder sb, string name, int actual, int limit)
    {
        bool ok = actual <= limit;
        sb.AppendLine($"  {name,-30} {actual,7} {limit,7}   {(ok ? "ok" : "*** OVER")}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// PHASE 6E. What the art layer actually cost, itemised by group, and the one number that
    /// matters about it: how many colliders it added, which must be zero.
    ///
    /// It is measured from the scene rather than from the plan on purpose. `CityDressing` promising
    /// that it emits no colliders and `SkyboundCityBuilder` actually emitting none are two different
    /// claims, and this is the one that checks the second.
    /// </summary>
    private static string DetailReport(GameObject world)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Phase 6E art layer          renderers   colliders");

        int totalRenderers = 0;
        int totalColliders = 0;

        foreach (string group in CityDressing.Groups)
        {
            Transform found = Find(world.transform, group);

            if (found == null)
            {
                sb.AppendLine($"  {group,-26} {"-",9}");
                continue;
            }

            int renderers = found.GetComponentsInChildren<MeshRenderer>(true).Length;
            int colliders = found.GetComponentsInChildren<Collider>(true).Length;

            totalRenderers += renderers;
            totalColliders += colliders;

            sb.AppendLine($"  {group,-26} {renderers,9} {colliders,11}" +
                          (colliders == 0 ? string.Empty : "   *** DECORATION IS SOLID"));
        }

        sb.AppendLine($"  {"total",-26} {totalRenderers,9} {totalColliders,11}");
        return sb.ToString();
    }

    /// <summary>Depth-first search for a group, since one of the eight is nested under the gate.</summary>
    private static Transform Find(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = Find(root.GetChild(i), name);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Elevation strip looking north, so the skyline can be read as a shape. Silhouette is a
    /// judgement call, but "does the tower dominate and do the districts step" is not.
    ///
    /// PHASE 6E: renderers outside the core are skipped. The backdrop ring is 108 blocks standing
    /// between 372 m and 510 m out, and every one of them would be clamped into the two edge columns
    /// of a 60-column strip that only measures the 600 m core - turning the one picture in this
    /// report into two black bars.
    /// </summary>
    private static string AsciiSkyline(GameObject world)
    {
        const int cols = 60;
        const int rows = 20;
        float step = CityDesign.CoreExtent / cols;
        float[] tallest = new float[cols];

        float half = CityDesign.CoreExtent * 0.5f;

        foreach (MeshRenderer r in world.GetComponentsInChildren<MeshRenderer>(true))
        {
            Bounds b = r.bounds;

            if (Mathf.Abs(b.center.x) > half || Mathf.Abs(b.center.z) > half)
            {
                continue;
            }

            int from = Mathf.Clamp(Mathf.FloorToInt((b.min.x + CityDesign.CoreExtent * 0.5f) / step), 0, cols - 1);
            int to = Mathf.Clamp(Mathf.FloorToInt((b.max.x + CityDesign.CoreExtent * 0.5f) / step), 0, cols - 1);

            for (int i = from; i <= to; i++)
            {
                tallest[i] = Mathf.Max(tallest[i], b.max.y);
            }
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"skyline looking north, west on the left (top row = {CityDesign.TowerTopY:F0} m):");

        for (int row = rows - 1; row >= 0; row--)
        {
            float threshold = CityDesign.TowerTopY * (row + 0.5f) / rows;
            sb.Append("  ");

            for (int i = 0; i < cols; i++)
            {
                sb.Append(tallest[i] >= threshold ? '#' : ' ');
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------ C: named routes

    [MenuItem("Tools/Skybound City/C - Run Named Routes", priority = 22)]
    public static void RunRoutes()
    {
        if (!ReadPlayer(out BasicFirstPersonController move, out CharacterController cc))
        {
            return;
        }

        Physics.SyncTransforms();
        Vector3 restore = move.transform.position;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("SKYBOUND CITY - NAMED ROUTES");
        sb.AppendLine($"walk={move.WalkSpeed} sprint={move.SprintSpeed} jump={move.JumpHeight} " +
                      $"gravity={move.Gravity} dt={Dt:F5}");
        sb.AppendLine();
        sb.AppendLine("STREET ROUTES (walked with the real controller)");
        sb.AppendLine("route                            tier    legs   length   verdict");

        int fail = 0;
        int walked = 0;

        foreach (CityRoute route in CityRoutes.All)
        {
            if (!route.StreetLevel)
            {
                continue;
            }

            walked++;
            bool ok = WalkRoute(move, cc, route, out string detail);

            if (!ok)
            {
                fail++;
            }

            sb.AppendLine($"{route.Name,-32} {route.Tier,-7} {route.Waypoints.Length - 1,4} " +
                          $"{route.TotalLength,8:F1}   {(ok ? "ok" : "*** " + detail)}");
        }

        cc.enabled = false;
        move.transform.position = restore;
        cc.enabled = true;
        move.ResetMotion();

        fail += ProbeRoofRoutes(cc, sb, out int probed, out int surfaces);

        sb.AppendLine();
        sb.AppendLine(fail == 0
            ? $"RESULT: {walked} street route(s) walkable, {surfaces} surfaces across " +
              $"{probed} rooftop route(s) all present."
            : $"RESULT: {fail} route problem(s).");

        Write("city_routes.txt", sb, fail);
    }

    /// <summary>
    /// Confirms that every surface a rooftop route stands on is really in the scene, at the height
    /// the plan put it, with room for the player to stand.
    ///
    /// This is the check that catches the class of mistake the plan cannot: a ledge authored on the
    /// wrong facade, a deck the builder skipped, a landing buried in the massing it was hung on.
    /// The centre of a surface is not always clear - the tower podium has a 26 m shaft standing on
    /// it - so the probe also tries four points inside the footprint before giving up.
    /// </summary>
    private static int ProbeRoofRoutes(CharacterController cc, StringBuilder sb, out int probed,
        out int surfaces)
    {
        bool wasEnabled = cc.enabled;
        cc.enabled = false;
        Physics.SyncTransforms();

        // The plan is what says how big each surface is, which is what the probe needs to know
        // where else on it to look when its centre is occupied.
        CityTraversalResult traversal = CityPlan.Generate().Traversal;

        int fail = 0;
        probed = 0;
        surfaces = 0;

        sb.AppendLine();
        sb.AppendLine("ROOFTOP ROUTES (surfaces probed against the real colliders)");
        sb.AppendLine("route                            tier    hops  surfaces  verdict");

        foreach (CityRoute route in CityRoutes.All)
        {
            if (route.StreetLevel)
            {
                continue;
            }

            probed++;

            if (route.Waypoints.Length < 2)
            {
                fail++;
                sb.AppendLine($"{route.Name,-32} {route.Tier,-7} {0,4} {0,9}   " +
                              "*** no route across the roofs connects its ends");
                continue;
            }

            string detail = string.Empty;

            for (int i = 0; i < route.Waypoints.Length; i++)
            {
                surfaces++;
                string node = i < route.Nodes.Length ? route.Nodes[i] : "?";
                CityRect footprint = traversal.Surfaces.TryGetValue(node,
                    out TraversalSurface surface)
                    ? surface.Footprint
                    : CityRect.FromCentre(route.Waypoints[i].x, route.Waypoints[i].z, 1f, 1f);

                if (StandableAt(route.Waypoints[i], footprint, cc, out float found))
                {
                    continue;
                }

                detail = float.IsNaN(found)
                    ? $"nothing to stand on at {node} ({route.Waypoints[i]})"
                    : $"{node} is at {found:F2} m, planned {route.Waypoints[i].y:F2} m";
                break;
            }

            bool ok = detail.Length == 0;

            if (!ok)
            {
                fail++;
            }

            sb.AppendLine($"{route.Name,-32} {route.Tier,-7} {route.Waypoints.Length - 1,4} " +
                          $"{route.Waypoints.Length,9}   {(ok ? "ok" : "*** " + detail)}");
        }

        cc.enabled = wasEnabled;
        Physics.SyncTransforms();
        return fail;
    }

    /// <summary>
    /// Is the planned surface really there, and can a player stand on it? Tries the centre first,
    /// then four points out towards its edges, because a surface is allowed to have something
    /// standing in the middle of it - the tower podium carries a 26 m shaft, and the shaft roof
    /// carries the mast.
    ///
    /// The offsets are a fraction of the footprint rather than a fixed distance for exactly that
    /// reason: 3 m off the podium's centre is still inside the shaft.
    /// </summary>
    private static bool StandableAt(Vector3 planned, CityRect footprint, CharacterController cc,
        out float found)
    {
        found = float.NaN;
        float best = float.NaN;

        float dx = footprint.Width * 0.35f;
        float dz = footprint.Depth * 0.35f;

        Vector3[] offsets =
        {
            Vector3.zero,
            new Vector3(dx, 0f, 0f), new Vector3(-dx, 0f, 0f),
            new Vector3(0f, 0f, dz), new Vector3(0f, 0f, -dz)
        };

        foreach (Vector3 offset in offsets)
        {
            Vector3 at = planned + offset;

            if (!Physics.Raycast(new Vector3(at.x, planned.y + 1.5f, at.z), Vector3.down,
                    out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            if (float.IsNaN(best) || Mathf.Abs(hit.point.y - planned.y) < Mathf.Abs(best - planned.y))
            {
                best = hit.point.y;
            }

            if (Mathf.Abs(hit.point.y - planned.y) > SurfaceTolerance)
            {
                continue;
            }

            float radius = cc.radius * 0.9f;
            Vector3 bottom = new Vector3(at.x, hit.point.y + radius + 0.05f, at.z);
            Vector3 top = new Vector3(at.x, hit.point.y + cc.height - radius, at.z);

            if (!Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                found = hit.point.y;
                return true;
            }
        }

        found = best;
        return false;
    }

    /// <summary>
    /// Walks a route with the real CharacterController against the real colliders, mirroring
    /// <c>BasicFirstPersonController.HandleMovement</c>: horizontal move at walk speed toward the
    /// next waypoint, vertical through the controller's own integrator.
    /// </summary>
    private static bool WalkRoute(BasicFirstPersonController move, CharacterController cc,
        CityRoute route, out string detail)
    {
        detail = string.Empty;

        cc.enabled = false;
        move.transform.position = route.Waypoints[0];
        cc.enabled = true;

        // Settle onto the ground before starting, so a waypoint authored slightly above the
        // pavement does not count as a fall.
        float vertical = 0f;
        for (int i = 0; i < 60; i++)
        {
            vertical = BasicFirstPersonController.IntegrateVertical(vertical, move.Gravity, Dt,
                out float drop);
            cc.Move(new Vector3(0f, drop, 0f));

            if (cc.isGrounded)
            {
                vertical = 0f;
                break;
            }
        }

        for (int leg = 1; leg < route.Waypoints.Length; leg++)
        {
            Vector3 target = route.Waypoints[leg];
            float legLength = Vector3.Distance(move.transform.position, target);

            // Generous: 3x the straight-line time, so an honest detour around a kerb is not a
            // failure but a wall is.
            int budget = Mathf.CeilToInt(legLength / move.WalkSpeed / Dt * 3f) + 120;
            float bestDistance = float.MaxValue;
            int sinceProgress = 0;

            for (int step = 0; step < budget; step++)
            {
                Vector3 position = move.transform.position;
                Vector3 toTarget = target - position;
                toTarget.y = 0f;

                float planar = toTarget.magnitude;

                if (planar < 1.2f && Mathf.Abs(target.y - position.y) < 3f)
                {
                    break;
                }

                if (planar < bestDistance - 0.02f)
                {
                    bestDistance = planar;
                    sinceProgress = 0;
                }
                else if (++sinceProgress > 90)
                {
                    detail = $"stuck on leg {leg} at {position} ({planar:F1} m short)";
                    return false;
                }

                Vector3 horizontal = toTarget.normalized * move.WalkSpeed * Dt;

                if (cc.isGrounded && vertical < 0f)
                {
                    vertical = -2f;
                }

                vertical = BasicFirstPersonController.IntegrateVertical(vertical, move.Gravity, Dt,
                    out float drop);
                cc.Move(horizontal + new Vector3(0f, drop, 0f));

                if (move.transform.position.y < CityDesign.DeathPlaneY)
                {
                    detail = $"fell out of the world on leg {leg}";
                    return false;
                }
            }

            Vector3 end = move.transform.position;
            Vector3 remaining = target - end;
            remaining.y = 0f;

            if (remaining.magnitude > 1.5f)
            {
                detail = $"leg {leg} ran out of time {remaining.magnitude:F1} m short at {end}";
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------------ plumbing

    private static bool ReadPlayer(out BasicFirstPersonController move, out CharacterController cc)
    {
        move = Object.FindFirstObjectByType<BasicFirstPersonController>();
        cc = move != null ? move.GetComponent<CharacterController>() : null;

        if (move == null || cc == null)
        {
            Debug.LogError("[SkyboundCity] No player in the open scene. " +
                           $"Open {SkyboundCityBuilder.ScenePath} first.");
            return false;
        }

        return true;
    }

    internal static void Write(string file, StringBuilder sb, int fail)
    {
        string dir = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "SceneBackups");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, file), sb.ToString());

        if (fail > 0)
        {
            Debug.LogWarning($"[SkyboundCity] {file}: {fail} FAIL\n{sb}");
        }
        else
        {
            Debug.Log($"[SkyboundCity] {file}: pass\n{sb}");
        }
    }
}
