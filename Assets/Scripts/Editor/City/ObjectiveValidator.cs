using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Asserts that Skybound City's mission is playable, and playable in any order.
///
/// The three harnesses before it each answer a different question: A whether the streets connect,
/// B whether the massing is inside its budgets, C whether the surfaces the routes stand on are
/// really there, D whether every authored jump grades at the tier it was declared at. This one
/// answers Phase 6D's: given all of that, can a player actually finish the mission - and can they
/// finish it having chosen the relays in any of the 120 orders there are.
///
/// Most of it is plan-side, so it runs from a cold project with nothing open. The last section is
/// the scene, and it is the one that catches the failure this phase is actually exposed to: a
/// relay whose trigger is not wired to the route, a gate the tracker does not hold a reference to,
/// a fatal fall left at the component's default rather than the design's figure. The plan cannot
/// see any of those, because none of them is a dimension.
/// </summary>
public static class ObjectiveValidator
{
    [MenuItem("Tools/Skybound City/E - Validate Objectives", priority = 24)]
    public static void Validate()
    {
        CityPlanResult plan = CityPlan.Generate();
        RoofGraph roofs = RoofGraph.Build(plan);
        RoofGraph street = RoofGraph.BuildWithStreet(plan);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("SKYBOUND CITY - OBJECTIVE VALIDATION");
        sb.AppendLine($"relays {plan.Objectives.Relays.Count}  " +
                      $"anchors {plan.Objectives.Anchors.Count}  " +
                      $"volumes {plan.Volumes.Count}  colliders {plan.ColliderCount} / 1100");
        sb.AppendLine();

        int fail = 0;
        fail += CheckResolution(sb, plan);
        fail += CheckFallRule(sb, plan, roofs);
        fail += CheckRelays(sb, plan, street);
        fail += CheckAnchors(sb, plan);
        fail += CheckTowerGate(sb, plan);
        fail += CheckMissionOrders(sb, plan, street);
        fail += CheckGuidance(sb, plan);
        fail += CheckWorkedRoutes(sb, plan);
        fail += CheckScene(sb, plan);

        sb.AppendLine();
        sb.AppendLine(fail == 0
            ? "RESULT: the mission is completable, in every order the relays can be taken in."
            : $"RESULT: {fail} rule violation(s).");

        CityRouteHarness.Write("city_objectives.txt", sb, fail);
    }

    // ------------------------------------------------------------------ route guidance

    /// <summary>
    /// The world-space guidance, measured rather than eyeballed.
    ///
    /// Two halves, and the second is the one that catches a real class of mistake. The first checks
    /// the graph `CityNavigation` derives from the plan: that it reaches every objective from the
    /// spawn and from all thirteen ways up, and that no street leg of any of those routes passes
    /// through a building - which is the whole difference between guidance and a line drawn on a
    /// map. The second checks the graph that is actually *baked into the open scene*, because a
    /// guide wired to an empty array draws nothing, reports no error, and looks exactly like a
    /// guide with nothing to say.
    /// </summary>
    private static int CheckGuidance(StringBuilder sb, CityPlanResult plan)
    {
        sb.AppendLine("ROUTE GUIDANCE");

        CityNavigation.Result nav = CityNavigation.Build(plan);
        CityNavGraph graph = nav.Graph;
        int fail = nav.Problems.Count;

        foreach (string problem in nav.Problems)
        {
            sb.AppendLine("  *** " + problem);
        }

        sb.AppendLine($"  nav graph: {graph.Nodes.Count} nodes ({nav.StreetNodes} street, " +
                      $"{nav.FootNodes} ways up, {nav.SurfaceNodes} surfaces), " +
                      $"{graph.Links.Count} links, {nav.Targets.Count} targets");

        Vector3 spawn = CityDesign.SpawnPosition;
        int start = graph.Nearest(spawn);

        sb.AppendLine();
        sb.AppendLine("  from the spawn        legs  route m  direct m  ratio  hardest  chevrons");

        List<string> ids = new List<string>(nav.Targets.Keys);
        ids.Sort(System.StringComparer.Ordinal);

        foreach (string id in ids)
        {
            int to = graph.IndexOf(nav.Targets[id]);
            List<int> path = graph.Path(start, to);

            if (path == null)
            {
                fail++;
                sb.AppendLine($"  {id,-22} *** NO ROUTE");
                continue;
            }

            Vector3 target = graph.Nodes[to].Position;
            List<Vector3> line = graph.Waypoints(spawn, path, target);
            List<Breadcrumb> crumbs = CityNavigation.Breadcrumbs(line, null,
                CityDesign.GuideMarkerCount);

            float length = 0f;

            for (int i = 0; i < line.Count - 1; i++)
            {
                length += (line[i + 1] - line[i]).magnitude;
            }

            float direct = Mathf.Max(1f, (target - spawn).magnitude);

            sb.AppendLine($"  {id,-22} {path.Count,4} {length,8:F0} {direct,9:F0} " +
                          $"{length / direct,6:F2}  {graph.WorstTier(path),-8} {crumbs.Count,7}");

            // A route that measures the same as the crow flies is a route that goes through a
            // building, whatever the graph says it is made of.
            if (length < direct * 1.05f)
            {
                fail++;
                sb.AppendLine("      *** this route is a straight line to the objective");
            }
        }

        // Every way up reaches every objective, and no street leg of any route crosses anything.
        int pairs = 0;
        int unreachable = 0;
        int throughSomething = 0;

        foreach (AscentPlan ascent in plan.Traversal.StreetAscents())
        {
            int foot = graph.IndexOf(CityNavigation.FootPrefix + ascent.Name);

            foreach (string id in ids)
            {
                pairs++;
                List<int> path = graph.Path(foot, graph.IndexOf(nav.Targets[id]));

                if (path == null)
                {
                    unreachable++;
                    sb.AppendLine($"  *** {ascent.Name} cannot reach {id}");
                    continue;
                }

                foreach (int link in path)
                {
                    NavLink edge = graph.Links[link];

                    if (graph.Nodes[edge.From].Kind == NavNodeKind.Surface
                        || graph.Nodes[edge.To].Kind == NavNodeKind.Surface)
                    {
                        continue;
                    }

                    if (CityNavigation.BlockedSegment(plan, graph.Nodes[edge.From].Position,
                            graph.Nodes[edge.To].Position))
                    {
                        throughSomething++;
                        sb.AppendLine($"  *** {graph.Nodes[edge.From].Name} -> " +
                                      $"{graph.Nodes[edge.To].Name} crosses something solid");
                    }
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine($"  way-up x objective routes         {pairs - unreachable,4} / {pairs,-4} " +
                      $"{(unreachable == 0 ? "ok" : "*** UNREACHABLE")}");
        sb.AppendLine($"  street legs through a building    {throughSomething,4} / 0    " +
                      $"{(throughSomething == 0 ? "ok" : "*** THROUGH A WALL")}");

        fail += unreachable + throughSomething;

        // --- and the guide that is actually in the scene ------------------------------
        RouteGuide guide = Object.FindFirstObjectByType<RouteGuide>(FindObjectsInactive.Include);

        if (guide == null)
        {
            sb.AppendLine("  no RouteGuide in the open scene - scene half skipped.");
            sb.AppendLine();
            return fail;
        }

        fail += Present(sb, "guide nav nodes baked into the scene", guide.NodeCount,
            graph.Nodes.Count);
        fail += Present(sb, "guide nav links baked into the scene", guide.LinkCount,
            graph.Links.Count);
        fail += Present(sb, "guide objectives", guide.TargetCount, nav.Targets.Count);
        fail += Present(sb, "chevrons in the pool", guide.MarkerCount, CityDesign.GuideMarkerCount);
        fail += Rule(sb, "the guide is wired to a tracker, a player and a beacon", 0f,
            guide.IsWired);

        int routed = 0;

        foreach (string id in ids)
        {
            List<Vector3> route = guide.RouteFrom(CityDesign.SpawnPosition, id,
                graph.Nodes[graph.IndexOf(nav.Targets[id])].Position);

            if (route != null && route.Count >= 3)
            {
                routed++;
            }
            else
            {
                sb.AppendLine($"  *** the scene's guide cannot route to {id}");
            }
        }

        fail += Present(sb, "objectives the scene's own guide can route to", routed, ids.Count);
        fail += Present(sb, "action markers in the pool", guide.ActionMarkerCount,
            CityDesign.GuideActionMarkerCount);

        sb.AppendLine();
        return fail;
    }

    /// <summary>
    /// The guidance a player would actually be shown, written out as moves.
    ///
    /// It is in this report because "the graph connects them" and "a person could follow it" are
    /// different claims, and only the second one is the feature. Every leg names the piece of
    /// Phase 6C geometry that carries it, so a route that quietly relied on a jump nobody can make
    /// would be visible here as well as failing the tier check above.
    /// </summary>
    private static int CheckWorkedRoutes(StringBuilder sb, CityPlanResult plan)
    {
        sb.AppendLine("WORKED ROUTES  (relay to relay, in words)");

        CityNavigation.Result nav = CityNavigation.Build(plan);
        CityNavGraph graph = nav.Graph;
        int fail = 0;

        List<Vector3> positions = new List<Vector3>();
        List<bool> available = new List<bool>();

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            positions.Add(relay.Position);
            available.Add(true);
        }

        // The order the mission actually sends the player in, from the spawn onwards: the sticky
        // nearest-uncaptured rule, applied five times.
        Vector3 at = CityDesign.SpawnPosition;
        int held = -1;

        for (int leg = 0; leg < plan.Objectives.Relays.Count; leg++)
        {
            held = ObjectiveFocus.Choose(positions, available, at, held,
                CityDesign.ObjectiveStickiness);

            if (held < 0)
            {
                break;
            }

            RelayObjective target = plan.Objectives.Relays[held];
            int from = graph.Nearest(at);
            int to = graph.IndexOf(nav.Targets[target.Name]);
            List<int> path = graph.Path(from, to);

            sb.AppendLine();
            sb.AppendLine($"  LEG {leg + 1}: -> {target.DisplayName} relay");

            if (path == null)
            {
                fail++;
                sb.AppendLine("    *** NO ROUTE");
                continue;
            }

            foreach (string line in CityNavigation.Describe(plan, nav, path))
            {
                sb.AppendLine("    " + line);
            }

            sb.AppendLine($"    ARRIVE {target.DisplayName} relay pad "
                          + $"(hardest move on this leg: {graph.WorstTier(path)})");

            if (graph.WorstTier(path) > RouteTier.Orange)
            {
                fail++;
                sb.AppendLine("    *** this leg grades harder than a mantle");
            }

            available[held] = false;
            at = target.Position;
        }

        sb.AppendLine();
        return fail;
    }

    // ------------------------------------------------------------------ resolution

    private static int CheckResolution(StringBuilder sb, CityPlanResult plan)
    {
        sb.AppendLine("RESOLUTION");

        int fail = plan.Objectives.Problems.Count;

        foreach (string problem in plan.Objectives.Problems)
        {
            sb.AppendLine("  *** " + problem);
        }

        sb.AppendLine($"  relays {plan.Objectives.Relays.Count} of " +
                      $"{CityTraversal.Relays.Length} authored");

        if (plan.Objectives.Relays.Count != CityTraversal.Relays.Length)
        {
            fail++;
            sb.AppendLine("  *** a relay site did not become a relay");
        }

        int waysIn = 0;

        foreach (AscentPlan unused in plan.Traversal.StreetAscents())
        {
            waysIn++;
        }

        int expected = waysIn + plan.Objectives.Relays.Count;
        bool anchorsOk = plan.Objectives.Anchors.Count == expected;

        sb.AppendLine($"  anchors {plan.Objectives.Anchors.Count}: one per way in ({waysIn}) plus " +
                      $"one per relay ({plan.Objectives.Relays.Count})" +
                      (anchorsOk ? string.Empty : "   *** MISMATCH"));

        if (!anchorsOk)
        {
            fail++;
        }

        sb.AppendLine();
        return fail;
    }

    // ------------------------------------------------------------------ the fall rule

    /// <summary>
    /// The claim Phase 6C left for this phase to make good on: making a fall fatal must not take
    /// away a single connection the roof graph counted.
    /// </summary>
    private static int CheckFallRule(StringBuilder sb, CityPlanResult plan, RoofGraph roofs)
    {
        int fail = 0;

        sb.AppendLine("THE FALL RULE");
        fail += Rule(sb, "fatal fall above the safe drop",
            CityDesign.FatalFallHeight - CityDesign.SafeDropHeight,
            CityDesign.FatalFallHeight > CityDesign.SafeDropHeight);
        fail += Rule(sb, "fatal fall above the Cut's depth",
            CityDesign.FatalFallHeight + CityDesign.CutFloorY,
            CityDesign.FatalFallHeight > -CityDesign.CutFloorY);
        fail += Rule(sb, "death plane below the controller's own reset",
            CityDesign.DeathPlaneY - CityDesign.ControllerFallResetY,
            CityDesign.ControllerFallResetY < CityDesign.DeathPlaneY);

        // A descent is either a fall or a flight of stairs, and only the first is this rule's
        // business: the Center-Industrial link stair drops 21.6 m and the player takes it 1.8 m at
        // a time. So the ascents are measured by their step, and everything else by its whole drop.
        float worst = 0f;
        string worstEdge = "none";
        float worstStep = 0f;
        string worstStair = "none";

        foreach (string node in roofs.Nodes)
        {
            float from = plan.Traversal.Surfaces[node].SurfaceY;

            foreach (RoofEdge edge in roofs.From(node))
            {
                AscentPlan ascent = CityTraversal.Ascent(plan.Traversal, edge.Via);

                if (ascent != null)
                {
                    if (!ascent.IsRamped && ascent.StepRise > worstStep)
                    {
                        worstStep = ascent.StepRise;
                        worstStair = ascent.Name;
                    }

                    continue;
                }

                float drop = from - plan.Traversal.Surfaces[edge.To].SurfaceY;

                if (drop > worst)
                {
                    worst = drop;
                    worstEdge = $"{node} -> {edge.To}";
                }
            }
        }

        bool survivable = worst <= CityDesign.FatalFallHeight;

        sb.AppendLine($"  deepest fall counted as a route     {worst,8:F2} m  ({worstEdge})" +
                      (survivable ? string.Empty : "   *** FATAL"));
        sb.AppendLine($"  tallest step down a stair           {worstStep,8:F2} m  ({worstStair})");

        if (!survivable)
        {
            fail++;
        }

        if (worstStep > CityDesign.FatalFallHeight)
        {
            fail++;
            sb.AppendLine("  *** a single step of that stair is a fatal fall");
        }

        sb.AppendLine();
        return fail;
    }

    // ------------------------------------------------------------------ relays

    private static int CheckRelays(StringBuilder sb, CityPlanResult plan, RoofGraph street)
    {
        int fail = 0;
        HashSet<DistrictGroup> districts = new HashSet<DistrictGroup>();

        sb.AppendLine("RELAYS");
        sb.AppendLine("  relay                  district        host                          " +
                      "surface   pad   clearance  ways in");

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            float clearance = Mathf.Min(relay.Roof.Width, relay.Roof.Depth) * 0.5f
                              - CityDesign.RelayPadSize * 0.5f;
            int waysIn = street.AccessRoutes(relay.Node).Count;

            bool ok = clearance >= CityDesign.AnchorInset && waysIn >= 3 &&
                      relay.Group != DistrictGroup.Landmark && districts.Add(relay.Group);

            if (!ok)
            {
                fail++;
            }

            sb.AppendLine($"  {relay.Name,-22} {relay.DisplayName,-15} {relay.Node,-28} " +
                          $"{relay.RoofY,7:F2} {CityDesign.RelayPadSize,5:F1} {clearance,10:F2} " +
                          $"{waysIn,8}" + (ok ? string.Empty : "   *** SEE ABOVE"));
        }

        sb.AppendLine($"  districts covered: {districts.Count} of 5, landmark excluded");

        if (districts.Count != 5)
        {
            fail++;
        }

        sb.AppendLine();
        return fail;
    }

    // ------------------------------------------------------------------ anchors

    private static int CheckAnchors(StringBuilder sb, CityPlanResult plan)
    {
        int fail = 0;

        sb.AppendLine("RESPAWN ANCHORS");
        sb.AppendLine("  anchor                                  kind        surface   edge margin");

        foreach (AnchorObjective anchor in plan.Objectives.Anchors)
        {
            if (!plan.Traversal.Surfaces.TryGetValue(anchor.Node, out TraversalSurface host))
            {
                fail++;
                sb.AppendLine($"  {anchor.Name,-40} *** stands on {anchor.Node}, which is not a " +
                              "surface");
                continue;
            }

            // How far the pad's nearest edge is from the roof's nearest edge. Negative means the
            // anchor overhangs, which would respawn the player into thin air.
            float margin = Mathf.Min(
                Mathf.Min(anchor.Pad.MinX - host.Footprint.MinX, host.Footprint.MaxX - anchor.Pad.MaxX),
                Mathf.Min(anchor.Pad.MinZ - host.Footprint.MinZ, host.Footprint.MaxZ - anchor.Pad.MaxZ));

            bool ok = margin >= 0f &&
                      Mathf.Abs(anchor.SurfaceY - host.SurfaceY) < 0.001f;

            if (!ok)
            {
                fail++;
            }

            sb.AppendLine($"  {anchor.Name,-40} {anchor.Kind,-10} {anchor.SurfaceY,8:F2} " +
                          $"{margin,12:F2}" + (ok ? string.Empty : "   *** OFF THE ROOF"));
        }

        sb.AppendLine();
        return fail;
    }

    // ------------------------------------------------------------------ the tower gate

    private static int CheckTowerGate(StringBuilder sb, CityPlanResult plan)
    {
        int fail = 0;
        TowerGatePlan gate = plan.Objectives.Gate;

        sb.AppendLine("TOWER GATE");

        if (gate == null)
        {
            sb.AppendLine("  *** there is no gate, so the tower is never locked");
            sb.AppendLine();
            return 1;
        }

        AscentPlan spiral = Spiral(plan);
        float climb = TraversalEnvelope.MantleAssistedClimb(TraversalEnvelope.Default);
        float needed = climb / Mathf.Tan(spiral.PitchDegrees * Mathf.Deg2Rad);
        float sideLength = Mathf.Max(gate.SideWall.Width, gate.SideWall.Depth);
        float footWidth = Mathf.Max(gate.FootWall.Width, gate.FootWall.Depth);
        float runWidth = Mathf.Min(spiral.FootRun.Width, spiral.FootRun.Depth);

        fail += Rule(sb, "gate taller than the mantle-assisted climb", gate.Height - climb,
            gate.Height > climb);
        fail += Rule(sb, "side wall past the climbable run", sideLength - needed,
            sideLength >= needed);
        fail += Rule(sb, "foot wall spans the run", footWidth - runWidth, footWidth >= runWidth);
        fail += Rule(sb, "gate stands on the surface the spiral starts from",
            gate.BaseY - spiral.BaseY, Mathf.Abs(gate.BaseY - spiral.BaseY) < 0.001f);

        // The inboard side needs no wall: the shaft is there, and the slot left between it and the
        // run is narrower than the player.
        float slot = spiral.FootRun.GapTo(CityTraversal.ShaftFootprint);
        fail += Rule(sb, "slot between the shaft and the run, vs a 0.7 m player", slot,
            slot < 0.7f);

        sb.AppendLine($"  the gate shuts the only run a player can step onto; run 1 starts " +
                      $"{spiral.StepRise:F1} m above the podium roof.");
        sb.AppendLine();
        return fail;
    }

    private static AscentPlan Spiral(CityPlanResult plan)
    {
        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            if (ascent.Kind == AscentKind.TowerSpiral)
            {
                return ascent;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ the exit criterion

    private static int CheckMissionOrders(StringBuilder sb, CityPlanResult plan, RoofGraph street)
    {
        int fail = 0;

        sb.AppendLine("MISSION ORDERS  (the Phase 6D exit criterion)");
        sb.AppendLine("  a stop is reachable from another when the street-augmented roof graph " +
                      "connects them:");
        sb.AppendLine("  the pavement counts, because climbing down a fire escape and walking two " +
                      "blocks is a route.");
        sb.AppendLine();

        List<string> stops = CityObjectives.AllStops(plan);

        foreach (string from in stops)
        {
            List<string> missing = new List<string>();

            foreach (string to in stops)
            {
                if (from != to && !street.CanReach(from, to))
                {
                    missing.Add(to);
                }
            }

            if (missing.Count > 0)
            {
                fail++;
            }

            sb.AppendLine($"  from {from,-30} unreachable: " +
                          (missing.Count == 0 ? "none" : string.Join(", ", missing)));
        }

        int travellable = CityObjectives.MissionOrders(plan, street, out string problem);
        int total = CityObjectives.Factorial(plan.Objectives.Relays.Count);
        bool ok = travellable == total;

        sb.AppendLine();
        sb.AppendLine($"  orderings travellable: {travellable} / {total}" +
                      (ok ? string.Empty : "   *** " + problem));

        if (!ok)
        {
            fail++;
        }

        sb.AppendLine();
        return fail;
    }

    // ------------------------------------------------------------------ the scene

    /// <summary>
    /// The half no amount of plan arithmetic can answer: is the mission actually wired up.
    /// Skipped, not failed, when the city scene is not open - the rest of this report is designed
    /// to run cold.
    /// </summary>
    private static int CheckScene(StringBuilder sb, CityPlanResult plan)
    {
        sb.AppendLine("THE SCENE");

        ObjectiveTracker tracker = Object.FindFirstObjectByType<ObjectiveTracker>();

        if (tracker == null)
        {
            sb.AppendLine("  no ObjectiveTracker in the open scene - skipped. Open " +
                          $"{SkyboundCityBuilder.ScenePath} to check the wiring.");
            sb.AppendLine();
            return 0;
        }

        int fail = 0;

        ObjectiveRelay[] relays = Object.FindObjectsByType<ObjectiveRelay>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        RespawnAnchor[] anchors = Object.FindObjectsByType<RespawnAnchor>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        CheckpointManager checkpoints = Object.FindFirstObjectByType<CheckpointManager>();
        FallImpactDetector impact = Object.FindFirstObjectByType<FallImpactDetector>();
        FinishLine finish = Object.FindFirstObjectByType<FinishLine>();
        RespawnManager respawn = Object.FindFirstObjectByType<RespawnManager>();
        GameManager game = Object.FindFirstObjectByType<GameManager>();

        fail += Present(sb, "ObjectiveRelay components", relays.Length,
            plan.Objectives.Relays.Count);
        fail += Present(sb, "RespawnAnchor components", anchors.Length,
            plan.Objectives.Anchors.Count);
        fail += Present(sb, "CheckpointManager", checkpoints != null ? 1 : 0, 1);
        fail += Present(sb, "FallImpactDetector", impact != null ? 1 : 0, 1);
        fail += Present(sb, "FinishLine", finish != null ? 1 : 0, 1);
        fail += Present(sb, "RespawnManager", respawn != null ? 1 : 0, 1);
        fail += Present(sb, "GameManager", game != null ? 1 : 0, 1);

        if (checkpoints != null)
        {
            fail += Rule(sb, "checkpoint route is a Set, not a sequence", 0f,
                checkpoints.Order == CheckpointRouteOrder.Set);
            fail += Present(sb, "relays on the checkpoint route", checkpoints.Total,
                plan.Objectives.Relays.Count);
        }

        if (impact != null)
        {
            fail += Rule(sb, "fatal fall matches the design",
                impact.FatalFallHeight - CityDesign.FatalFallHeight,
                Mathf.Abs(impact.FatalFallHeight - CityDesign.FatalFallHeight) < 0.001f);
        }

        if (finish != null)
        {
            fail += Rule(sb, "the finish is gated on the whole relay set", 0f,
                finish.RequireAllCheckpoints);
        }

        int wired = 0;

        foreach (ObjectiveRelay relay in relays)
        {
            if (relay.Volume != null)
            {
                wired++;
            }
        }

        fail += Present(sb, "relays wired to a checkpoint volume", wired, relays.Length);

        // Transform.Find rather than GameObject.Find: once the mission is complete the gate is
        // deactivated, and a validator that reported it missing then would be reporting success.
        GameObject world = GameObject.Find(CityKit.WorldRoot);
        Transform gate = world != null ? world.transform.Find(CityObjectives.GateGroup) : null;

        // The gate is counted by its colliders, not by its child transforms.
        //
        // This is the question the rule was always asking, and counting children was only ever a
        // proxy for it. Phase 6E hung the gate's chevrons and warning beacons in a
        // TOWER_GATE_DETAIL child of this group - they have to live under it, because
        // ObjectiveTracker opens the tower by deactivating this transform and dressing anywhere
        // else would be left hanging in the air over an opened spiral - which made childCount 3 and
        // failed a scene that was correct. A collider is the wall itself rather than a stand-in for
        // one, and the expected number comes from the plan instead of being typed in twice.
        int walls = 0;

        foreach (BlockPlan block in plan.Blocks)
        {
            if (block.Kind == CityPieceKind.Gate && block.Collidable)
            {
                walls++;
            }
        }

        int gateColliders = gate != null
            ? gate.GetComponentsInChildren<Collider>(true).Length
            : 0;

        fail += Present(sb, "tower gate walls, as colliders", gateColliders, walls);

        // And the other half, which the old count could not have asked at all: everything Phase 6E
        // added to the gate is decoration. A solid chevron at the foot of the spiral would be a
        // ledge in the one place in the city whose whole purpose is to have no way past it.
        Transform dressing = gate != null ? gate.Find(CityDressing.GateDetailGroup) : null;
        int dressingRenderers = dressing != null
            ? dressing.GetComponentsInChildren<MeshRenderer>(true).Length
            : 0;
        int dressingColliders = dressing != null
            ? dressing.GetComponentsInChildren<Collider>(true).Length
            : 0;

        fail += Present(sb, $"the gate's {dressingRenderers} dressing pieces carry no collider",
            dressingColliders, 0);

        sb.AppendLine();
        return fail;
    }

    private static int Present(StringBuilder sb, string label, int actual, int expected)
    {
        bool ok = actual == expected;
        sb.AppendLine($"  {label,-46} {actual,4} / {expected,-4} {(ok ? "ok" : "*** MISSING")}");
        return ok ? 0 : 1;
    }

    private static int Rule(StringBuilder sb, string label, float value, bool ok)
    {
        sb.AppendLine($"  {label,-46} {value,8:F2}   {(ok ? "ok" : "*** FAIL")}");
        return ok ? 0 : 1;
    }
}
