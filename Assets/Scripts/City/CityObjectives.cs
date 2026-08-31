using System.Collections.Generic;
using UnityEngine;

/// <summary>What a Phase 6D trigger volume is for.</summary>
public enum ObjectiveVolumeKind
{
    /// <summary>An objective relay. Entering it captures the relay.</summary>
    Relay,

    /// <summary>A respawn anchor. Entering it moves where a death sends the player.</summary>
    Anchor,

    /// <summary>The summit. Entering it ends the mission, once every relay is captured.</summary>
    Finish
}

/// <summary>Why a respawn anchor is where it is.</summary>
public enum AnchorKind
{
    /// <summary>The top of a way in off the street: you climbed it, you do not climb it twice.</summary>
    AscentTop,

    /// <summary>A relay. Capturing one is also the strongest checkpoint the mission has.</summary>
    Relay
}

/// <summary>A planned trigger volume. Never collidable in the walking sense - it is a trigger.</summary>
public readonly struct VolumePlan
{
    public readonly string Name;
    public readonly string GroupName;
    public readonly ObjectiveVolumeKind Kind;
    public readonly CityRect Footprint;
    public readonly float BottomY;
    public readonly float TopY;

    /// <summary>The objective this volume belongs to, or empty for the finish.</summary>
    public readonly string Owner;

    public VolumePlan(string name, string groupName, ObjectiveVolumeKind kind, CityRect footprint,
        float bottomY, float topY, string owner)
    {
        Name = name;
        GroupName = groupName;
        Kind = kind;
        Footprint = footprint;
        BottomY = bottomY;
        TopY = topY;
        Owner = owner;
    }

    public Vector3 Centre => new Vector3(Footprint.CentreX, (BottomY + TopY) * 0.5f,
        Footprint.CentreZ);

    public Vector3 Size => new Vector3(Footprint.Width, Mathf.Max(0.01f, TopY - BottomY),
        Footprint.Depth);
}

/// <summary>One resolved objective relay: where it stands and what it is called.</summary>
public sealed class RelayObjective
{
    public string Name;
    public string DisplayName;

    /// <summary>The roof surface node it stands on, in the roof graph's naming.</summary>
    public string Node;

    public string CellName;
    public DistrictGroup Group;

    public CityRect Roof;
    public float RoofY;

    public CityRect Pad;
    public CityRect Mast;
    public CityRect Trigger;

    /// <summary>Where the player stands when they capture it, and where a death returns them.</summary>
    public Vector3 Position => new Vector3(Pad.CentreX, RoofY, Pad.CentreZ);

    /// <summary>Facing the tower, because that is where the mission ends.</summary>
    public float Yaw;
}

/// <summary>One resolved respawn anchor.</summary>
public sealed class AnchorObjective
{
    public string Name;
    public AnchorKind Kind;

    /// <summary>The surface it stands on.</summary>
    public string Node;

    public CityRect Pad;
    public CityRect Trigger;
    public float SurfaceY;
    public float Yaw;

    public Vector3 Position => new Vector3(Pad.CentreX, SurfaceY, Pad.CentreZ);
}

/// <summary>The hoarding that shuts the tower until the mission is done.</summary>
public sealed class TowerGatePlan
{
    /// <summary>Across the foot of the first run.</summary>
    public CityRect FootWall;

    /// <summary>Along its open side, for as far as the run is still inside the climb ceiling.</summary>
    public CityRect SideWall;

    public float BaseY;
    public float TopY;

    /// <summary>How far along the run a player could otherwise mantle onto it.</summary>
    public float ClimbableRun;

    public float Height => TopY - BaseY;
}

/// <summary>The objective layer, as data, beside the traversal layer it is hung on.</summary>
public sealed class CityObjectivesResult
{
    public readonly List<RelayObjective> Relays = new List<RelayObjective>();
    public readonly List<AnchorObjective> Anchors = new List<AnchorObjective>();

    public TowerGatePlan Gate;

    /// <summary>The summit volume that ends the mission.</summary>
    public VolumePlan Finish;

    /// <summary>Collected rather than thrown, so the validator can report all of them at once.</summary>
    public readonly List<string> Problems = new List<string>();

    public RelayObjective Relay(string name)
    {
        foreach (RelayObjective relay in Relays)
        {
            if (relay.Name == name)
            {
                return relay;
            }
        }

        return null;
    }

    /// <summary>Anchors that are relays, i.e. the ones that are also objectives.</summary>
    public int RelayAnchorCount
    {
        get
        {
            int count = 0;

            foreach (AnchorObjective anchor in Anchors)
            {
                if (anchor.Kind == AnchorKind.Relay)
                {
                    count++;
                }
            }

            return count;
        }
    }
}

/// <summary>
/// Phase 6D: the mission hung on the Phase 6C traversal layer.
///
/// Phase 6C ended with five relay *sites* and a summit nothing could unlock. This is what turns
/// them into a mission, and - like every phase before it - it is data first and components second:
/// the relays, the respawn anchors, the gate across the tower spiral and the finish volume are all
/// derived here from the plan, and `SkyboundCityBuilder` only instantiates what it is handed.
///
/// Three things are worth saying about the shape of it:
///
///   <b>Nothing here is walkable.</b> Every marker this layer emits is decoration with its collider
///   destroyed, and every volume is a trigger. The single exception is the tower gate, which exists
///   precisely to be in the way. That is what lets Phase 6D leave the Phase 6B walkability flood
///   fill, the Phase 6C tier measurements and the route harness's surface probes all reading exactly
///   what they read before - the harnesses ignore triggers, and a relay plinth a player cannot
///   stand on cannot change what a roof measures.
///
///   <b>The mission is order-free by construction.</b> Nothing in this file says which relay is
///   first. The exit criterion - completable in any relay order - is a reachability claim about the
///   street-augmented roof graph, and <see cref="CanCompleteInAnyOrder"/> settles it by measuring
///   rather than by asserting.
///
///   <b>Falling now costs something.</b> <see cref="CityDesign.FatalFallHeight"/> is derived from
///   <see cref="CityDesign.SafeDropHeight"/>, which is the drop the Phase 6C roof graph already
///   refused to count as a connection. That ordering is the whole reason 6C excluded big drops: the
///   redundancy it proved has to survive 6D making a fall fatal.
/// </summary>
public static class CityObjectives
{
    public const string ObjectiveGroup = "OBJECTIVES";
    public const string GateGroup = "TOWER_GATE";

    /// <summary>How the summit reads in the mission HUD. It is not a relay: it is the way out.</summary>
    public const string SummitName = "Skybound Tower";

    // ------------------------------------------------------------------ entry point

    /// <summary>
    /// Hangs the objective layer on a finished plan, adding its geometry to that plan and
    /// returning the mission as data. Pure, like everything else in this folder.
    /// </summary>
    public static CityObjectivesResult Plan(CityPlanResult plan)
    {
        CityObjectivesResult result = new CityObjectivesResult();

        PlanRelays(plan, result);
        PlanAscentAnchors(plan, result);
        PlanTowerGate(plan, result);
        PlanFinish(plan, result);

        return result;
    }

    // ------------------------------------------------------------------ relays

    private static void PlanRelays(CityPlanResult plan, CityObjectivesResult result)
    {
        Vector3 tower = new Vector3(CityTraversal.ShaftFootprint.CentreX, 0f,
            CityTraversal.ShaftFootprint.CentreZ);

        foreach (RelayPlan site in plan.Traversal.Relays)
        {
            CityRect pad = CityRect.FromCentre(site.Footprint.CentreX, site.Footprint.CentreZ,
                CityDesign.RelayPadSize, CityDesign.RelayPadSize);

            if (Mathf.Min(site.Footprint.Width, site.Footprint.Depth) <
                CityDesign.RelayPadSize + 2f * CityDesign.AnchorInset)
            {
                result.Problems.Add($"{site.Name}: {site.Node} is too small to carry a relay pad " +
                                    $"({site.Footprint.Width:F1} x {site.Footprint.Depth:F1} m).");
            }

            RelayObjective relay = new RelayObjective
            {
                Name = site.Name,
                DisplayName = DisplayName(site.Group),
                Node = site.Node,
                CellName = site.CellName,
                Group = site.Group,
                Roof = site.Footprint,
                RoofY = site.SurfaceY,
                Pad = pad,
                Mast = CityRect.FromCentre(pad.CentreX, pad.CentreZ,
                    CityDesign.RelayMastSize, CityDesign.RelayMastSize),
                Trigger = pad,
                Yaw = YawTowards(new Vector3(pad.CentreX, 0f, pad.CentreZ), tower)
            };

            result.Relays.Add(relay);

            // Decoration, both of them: a plinth a player can stand on would be a 0.15 m step on a
            // roof the Phase 6C harness probes, and a mast with a collider would block the capsule
            // check at the centre of that roof.
            plan.Blocks.Add(new BlockPlan($"{relay.Name}_Pad", ObjectiveGroup,
                CityPieceKind.Objective, relay.Pad, relay.RoofY,
                relay.RoofY + CityDesign.RelayPadRise, collidable: false));
            plan.Blocks.Add(new BlockPlan($"{relay.Name}_Mast", ObjectiveGroup,
                CityPieceKind.Objective, relay.Mast, relay.RoofY + CityDesign.RelayPadRise,
                relay.RoofY + CityDesign.RelayPadRise + CityDesign.RelayMastHeight,
                collidable: false));

            plan.Volumes.Add(new VolumePlan($"{relay.Name}_Volume", ObjectiveGroup,
                ObjectiveVolumeKind.Relay, relay.Trigger, relay.RoofY,
                relay.RoofY + CityDesign.ObjectiveTriggerHeight, relay.Name));

            // A relay is the mission's strongest checkpoint, so it is an anchor too. The player
            // never has to re-climb a district they have already finished with.
            AnchorObjective anchor = new AnchorObjective
            {
                Name = $"{relay.Name}_Anchor",
                Kind = AnchorKind.Relay,
                Node = relay.Node,
                Pad = relay.Pad,
                Trigger = relay.Trigger,
                SurfaceY = relay.RoofY,
                Yaw = relay.Yaw
            };

            result.Anchors.Add(anchor);
        }
    }

    /// <summary>How a district's relay is named in the HUD.</summary>
    public static string DisplayName(DistrictGroup group)
    {
        switch (group)
        {
            case DistrictGroup.CityCenter: return "City Center";
            case DistrictGroup.Residential: return "Residential";
            case DistrictGroup.Industrial: return "Industrial";
            case DistrictGroup.Corporate: return "Corporate";
            case DistrictGroup.OldQuarter: return "Old Quarter";
            default: return "Landmark";
        }
    }

    // ------------------------------------------------------------------ anchors

    /// <summary>
    /// One anchor at the top of every way in off the street.
    ///
    /// The rule is deliberately structural rather than hand-placed: the ways in are exactly the
    /// thing Phase 6C counts when it says a relay has three routes, so anchoring them is what makes
    /// a death cost the last climb rather than the whole mission. It also means a new fire escape
    /// arrives with its anchor already on it.
    /// </summary>
    private static void PlanAscentAnchors(CityPlanResult plan, CityObjectivesResult result)
    {
        foreach (AscentPlan ascent in plan.Traversal.StreetAscents())
        {
            if (!plan.Traversal.Surfaces.TryGetValue(ascent.TopNode, out TraversalSurface top))
            {
                result.Problems.Add($"{ascent.Name}: tops out on {ascent.TopNode}, which is not a " +
                                    "surface in the plan.");
                continue;
            }

            // Stand the anchor on the roof, not on the last ledge of the stack: inset from the
            // facade by enough that the whole pad is clear of the edge.
            float inset = CityDesign.AnchorInset + CityDesign.AnchorPadSize * 0.5f;
            CityRect inner = top.Footprint.Inset(inset);

            if (inner.Width <= 0f || inner.Depth <= 0f)
            {
                inner = CityRect.FromCentre(top.Footprint.CentreX, top.Footprint.CentreZ, 0f, 0f);
            }

            Vector2 near = ascent.Landings.Count > 0
                ? new Vector2(ascent.Landings[ascent.Landings.Count - 1].CentreX,
                    ascent.Landings[ascent.Landings.Count - 1].CentreZ)
                : new Vector2(top.Footprint.CentreX, top.Footprint.CentreZ);

            float x = Mathf.Clamp(near.x, inner.MinX, inner.MaxX);
            float z = Mathf.Clamp(near.y, inner.MinZ, inner.MaxZ);

            CityRect pad = CityRect.FromCentre(x, z, CityDesign.AnchorPadSize,
                CityDesign.AnchorPadSize);

            AnchorObjective anchor = new AnchorObjective
            {
                Name = $"{ascent.Name} Anchor",
                Kind = AnchorKind.AscentTop,
                Node = ascent.TopNode,
                Pad = pad,
                Trigger = pad,
                SurfaceY = top.SurfaceY,

                // Facing in off the edge, so a respawn never starts the player looking at the drop
                // they just took.
                Yaw = YawTowards(new Vector3(x, 0f, z),
                    new Vector3(top.Footprint.CentreX, 0f, top.Footprint.CentreZ))
            };

            result.Anchors.Add(anchor);

            plan.Blocks.Add(new BlockPlan($"{anchor.Name}_Pad".Replace(' ', '_'), ObjectiveGroup,
                CityPieceKind.Objective, anchor.Pad, anchor.SurfaceY,
                anchor.SurfaceY + CityDesign.AnchorPadRise, collidable: false));

            plan.Volumes.Add(new VolumePlan($"{anchor.Name}_Volume".Replace(' ', '_'),
                ObjectiveGroup, ObjectiveVolumeKind.Anchor, anchor.Trigger, anchor.SurfaceY,
                anchor.SurfaceY + CityDesign.ObjectiveTriggerHeight, anchor.Name));
        }
    }

    /// <summary>Yaw in degrees that turns +Z to face <paramref name="target"/>. Zero when they coincide.</summary>
    public static float YawTowards(Vector3 from, Vector3 target)
    {
        Vector3 delta = target - from;
        delta.y = 0f;

        return delta.sqrMagnitude < 0.0001f
            ? 0f
            : Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
    }

    // ------------------------------------------------------------------ the tower gate

    /// <summary>
    /// The one piece of Phase 6D geometry a player can walk into.
    ///
    /// The spiral is eight runs, and only the first one can be stepped onto: every later run starts
    /// a whole run's rise above whatever is below it. So the gate has two pieces and no more - one
    /// across the foot, and one along the open side of the first run for as far as its deck is
    /// still inside the climb ceiling. Past that the spiral gates itself.
    /// </summary>
    private static void PlanTowerGate(CityPlanResult plan, CityObjectivesResult result)
    {
        AscentPlan spiral = null;

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            if (ascent.Kind == AscentKind.TowerSpiral)
            {
                spiral = ascent;
                break;
            }
        }

        if (spiral == null)
        {
            result.Problems.Add("there is no tower spiral to gate.");
            return;
        }

        // How far along the run a standing player on the podium roof could still mantle onto it.
        float climb = TraversalEnvelope.MantleAssistedClimb(TraversalEnvelope.Default);
        float slope = Mathf.Tan(spiral.PitchDegrees * Mathf.Deg2Rad);
        float climbable = (slope > 0.0001f ? climb / slope : 0f) + CityDesign.TowerGateMargin;

        float baseY = spiral.BaseY;
        float topY = baseY + CityDesign.TowerGateHeight;
        float t = CityDesign.TowerGateThickness;

        CityRect run = spiral.FootRun;
        CityRect foot = spiral.FootLanding;
        CityRect footWall;
        CityRect sideWall;

        if (spiral.FootRunAlongZ)
        {
            // The run travels along Z, so its foot is whichever end the foot landing is at.
            bool footIsNorth = foot.CentreZ > run.CentreZ;
            float footFace = footIsNorth ? run.MaxZ : run.MinZ;

            footWall = new CityRect(run.MinX - t, run.MaxX + t,
                footIsNorth ? footFace : footFace - t,
                footIsNorth ? footFace + t : footFace);

            // The open side is the one away from the shaft.
            bool shaftIsWest = CityTraversal.ShaftFootprint.CentreX < run.CentreX;
            float openFace = shaftIsWest ? run.MaxX : run.MinX;

            sideWall = new CityRect(
                shaftIsWest ? openFace : openFace - t,
                shaftIsWest ? openFace + t : openFace,
                footIsNorth ? footFace - climbable : footFace,
                footIsNorth ? footFace : footFace + climbable);
        }
        else
        {
            bool footIsEast = foot.CentreX > run.CentreX;
            float footFace = footIsEast ? run.MaxX : run.MinX;

            footWall = new CityRect(
                footIsEast ? footFace : footFace - t,
                footIsEast ? footFace + t : footFace,
                run.MinZ - t, run.MaxZ + t);

            bool shaftIsSouth = CityTraversal.ShaftFootprint.CentreZ < run.CentreZ;
            float openFace = shaftIsSouth ? run.MaxZ : run.MinZ;

            sideWall = new CityRect(
                footIsEast ? footFace - climbable : footFace,
                footIsEast ? footFace : footFace + climbable,
                shaftIsSouth ? openFace : openFace - t,
                shaftIsSouth ? openFace + t : openFace);
        }

        result.Gate = new TowerGatePlan
        {
            FootWall = footWall,
            SideWall = sideWall,
            BaseY = baseY,
            TopY = topY,
            ClimbableRun = climbable
        };

        plan.Blocks.Add(new BlockPlan("Tower_Gate_Foot", GateGroup, CityPieceKind.Gate,
            footWall, baseY, topY));
        plan.Blocks.Add(new BlockPlan("Tower_Gate_Side", GateGroup, CityPieceKind.Gate,
            sideWall, baseY, topY));
    }

    // ------------------------------------------------------------------ the finish

    private static void PlanFinish(CityPlanResult plan, CityObjectivesResult result)
    {
        CityRect shaft = CityTraversal.ShaftFootprint;
        CityRect finish = shaft.Inset(CityDesign.SummitFinishInset);

        result.Finish = new VolumePlan("Summit_Finish", ObjectiveGroup, ObjectiveVolumeKind.Finish,
            finish, CityDesign.TowerShaftTopY,
            CityDesign.TowerShaftTopY + CityDesign.SummitFinishHeight, SummitName);

        plan.Volumes.Add(result.Finish);
    }

    // ------------------------------------------------------------------ the exit criterion

    /// <summary>
    /// The Phase 6D exit criterion, measured: is the mission completable in any relay order?
    ///
    /// A sequence of visits is possible exactly when every ordered pair of stops is connected in
    /// the street-augmented graph, because the player may always travel one stop at a time. So the
    /// question does not need 120 searches: it needs the pairwise matrix, and then every ordering
    /// of the five relays followed by the summit is a lookup. <see cref="MissionOrders"/> walks the
    /// permutations anyway - the claim is stated in orderings, so it is checked in orderings.
    /// </summary>
    public static bool CanCompleteInAnyOrder(CityPlanResult plan, RoofGraph street,
        out string problem)
    {
        List<string> relays = RelayNodes(plan);

        if (relays.Count == 0)
        {
            problem = "there are no relays to visit.";
            return false;
        }

        int travellable = MissionOrders(plan, street, out problem);
        int expected = Factorial(relays.Count);

        if (travellable == expected)
        {
            problem = string.Empty;
            return true;
        }

        return false;
    }

    /// <summary>How many orderings there are of <paramref name="n"/> relays.</summary>
    public static int Factorial(int n)
    {
        int product = 1;

        for (int i = 2; i <= n; i++)
        {
            product *= i;
        }

        return product;
    }

    /// <summary>
    /// Every stop a mission run passes through: the pavement it starts on, the five relays in no
    /// particular order, and the summit it ends on.
    /// </summary>
    public static List<string> AllStops(CityPlanResult plan)
    {
        List<string> stops = new List<string> { RoofGraph.StreetNode };
        stops.AddRange(RelayNodes(plan));
        stops.Add(CityTraversal.ShaftRoofNode);
        return stops;
    }

    public static List<string> RelayNodes(CityPlanResult plan)
    {
        List<string> nodes = new List<string>();

        foreach (RelayPlan relay in plan.Traversal.Relays)
        {
            nodes.Add(relay.Node);
        }

        return nodes;
    }

    /// <summary>
    /// Walks every ordering of the relays - street, five relays in that order, then the summit -
    /// and returns how many of them are travellable. The count is the point: it has to be all of
    /// them, and <paramref name="problem"/> names the first leg that is not.
    /// </summary>
    public static int MissionOrders(CityPlanResult plan, RoofGraph street, out string problem)
    {
        List<string> relays = RelayNodes(plan);
        Dictionary<string, HashSet<string>> reach = new Dictionary<string, HashSet<string>>();

        foreach (string from in AllStops(plan))
        {
            HashSet<string> to = new HashSet<string>();

            foreach (string candidate in AllStops(plan))
            {
                if (candidate != from && street.CanReach(from, candidate))
                {
                    to.Add(candidate);
                }
            }

            reach[from] = to;
        }

        problem = string.Empty;
        int ok = 0;

        foreach (List<string> order in Permutations(relays))
        {
            string at = RoofGraph.StreetNode;
            bool travellable = true;

            foreach (string stop in order)
            {
                if (!reach[at].Contains(stop))
                {
                    problem = $"{at} -> {stop} has no route, so the order " +
                              string.Join(", ", order) + " cannot be played.";
                    travellable = false;
                    break;
                }

                at = stop;
            }

            if (travellable && !reach[at].Contains(CityTraversal.ShaftRoofNode))
            {
                problem = $"{at} -> the summit has no route, so the order " +
                          string.Join(", ", order) + " cannot be finished.";
                travellable = false;
            }

            if (travellable)
            {
                ok++;
            }
        }

        return ok;
    }

    /// <summary>Every ordering of a list. Five relays is 120 of them, which is nothing.</summary>
    public static IEnumerable<List<string>> Permutations(List<string> items)
    {
        if (items.Count <= 1)
        {
            yield return new List<string>(items);
            yield break;
        }

        for (int i = 0; i < items.Count; i++)
        {
            List<string> rest = new List<string>(items);
            string head = rest[i];
            rest.RemoveAt(i);

            foreach (List<string> tail in Permutations(rest))
            {
                List<string> order = new List<string> { head };
                order.AddRange(tail);
                yield return order;
            }
        }
    }
}
