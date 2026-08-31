using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A named path through the city, as a chain of waypoints and the tier it is authored at.
///
/// The Phase 6A report asked for `IndustrialRouteHarness` to be generalised "from a hard-coded
/// Route[] array to named route definitions". This is that: routes are data, live in the runtime
/// assembly, and are consumed by both the harness (which walks them with the real
/// CharacterController) and the tier validator (which measures them).
/// </summary>
public readonly struct CityRoute
{
    public readonly string Name;

    /// <summary>What the route is graded at. Every leg must measure at this tier or easier.</summary>
    public readonly RouteTier Tier;

    /// <summary>True when the route is walked rather than jumped - no leg may be a gap.</summary>
    public readonly bool StreetLevel;

    public readonly Vector3[] Waypoints;

    /// <summary>
    /// For a rooftop route, the <see cref="RoofGraph"/> nodes the route passes through, in order.
    /// Empty for a street route.
    ///
    /// A rooftop route is a path across surfaces, not a line across the ground: its legs are jumps,
    /// bridges and ascents, and grading them by the straight-line distance between two waypoints
    /// would measure the wrong thing entirely. The nodes are what the tier validator measures; the
    /// waypoints exist so the harness can confirm each of those surfaces is really in the scene.
    /// </summary>
    public readonly string[] Nodes;

    public CityRoute(string name, RouteTier tier, bool streetLevel, Vector3[] waypoints)
        : this(name, tier, streetLevel, waypoints, System.Array.Empty<string>())
    {
    }

    public CityRoute(string name, RouteTier tier, bool streetLevel, Vector3[] waypoints,
        string[] nodes)
    {
        Name = name;
        Tier = tier;
        StreetLevel = streetLevel;
        Waypoints = waypoints;
        Nodes = nodes;
    }

    public float TotalLength
    {
        get
        {
            float total = 0f;

            for (int i = 0; i < Waypoints.Length - 1; i++)
            {
                total += Vector3.Distance(Waypoints[i], Waypoints[i + 1]);
            }

            return total;
        }
    }
}

/// <summary>
/// Every named route through Skybound City.
///
/// Phase 6B authored the street level: can the player walk the whole 600 x 600 m. Phase 6C adds the
/// rooftop layer beside it, and the two are graded and checked in different ways because they are
/// different claims. A street route is a line the player walks, so it is validated by walking it
/// with the real CharacterController. A rooftop route is a chain of *surfaces* - a fire escape, a
/// roof, a bridge, another roof - so it is validated by measuring the move between each pair
/// against the tier table, and by confirming each of those surfaces is really where the plan says.
///
/// Every coordinate is derived, never typed in. Street routes come from <see cref="CityDesign"/>;
/// rooftop routes come from <see cref="CityTraversal"/> and are pathed by <see cref="RoofGraph"/>,
/// so only their two ends and their tier are authored.
/// </summary>
public static class CityRoutes
{
    /// <summary>Waypoint height. Just above the pavement, so the runner drops onto it.</summary>
    private const float StreetY = 0.6f;

    /// <summary>
    /// Built once. Everything it derives from is a compile-time constant or a fixed seed, so the
    /// answer cannot change inside a session - and the rooftop half costs a plan and a graph.
    /// </summary>
    private static CityRoute[] cache;

    public static IReadOnlyList<CityRoute> All => cache ?? (cache = Build());

    private static CityRoute[] Build()
    {
        List<CityRoute> routes = new List<CityRoute>(StreetRoutes());
        routes.AddRange(RoofRoutes());
        return routes.ToArray();
    }

    // ------------------------------------------------------------------ street level (6B)

    private static CityRoute[] StreetRoutes()
    {
        float ringStreet = CityDesign.PlazaRingStreet;
        float avE = CityDesign.AvenueCentre(1);
        float avW = CityDesign.AvenueCentre(-1);
        float avN = avE;
        float avS = avW;
        float perE = CityDesign.PerimeterCentre(1);
        float perW = CityDesign.PerimeterCentre(-1);

        CityRect cut = CityPlan.CutBounds();
        CityRect quarter = CityDesign.Cell("OldQuarter").Bounds;

        return new[]
        {
            // --- leaving the start area ---------------------------------------------------
            // The plaza is enclosed on all four sides, so every one of these first steps onto the
            // ring street before it can go anywhere. If the City Center lot split ever stops
            // pinning the plaza to the centre, these break loudly, which is the point.
            new CityRoute("Plaza -> East Avenue", RouteTier.Green, true, new[]
            {
                P(0f, 0f), P(0f, ringStreet), P(avE, ringStreet), P(avE, 0f)
            }),
            new CityRoute("Plaza -> West Avenue", RouteTier.Green, true, new[]
            {
                P(0f, 0f), P(0f, ringStreet), P(avW, ringStreet), P(avW, 0f)
            }),
            new CityRoute("Plaza -> North Avenue", RouteTier.Green, true, new[]
            {
                P(0f, 0f), P(ringStreet, 0f), P(ringStreet, avN), P(0f, avN)
            }),
            new CityRoute("Plaza -> South Avenue", RouteTier.Green, true, new[]
            {
                P(0f, 0f), P(ringStreet, 0f), P(ringStreet, avS), P(0f, avS)
            }),

            // --- the two circuits ---------------------------------------------------------
            new CityRoute("Avenue Ring", RouteTier.Green, true, new[]
            {
                P(avW, avS), P(avE, avS), P(avE, avN), P(avW, avN), P(avW, avS)
            }),
            new CityRoute("Perimeter Ring", RouteTier.Green, true, new[]
            {
                P(perW, perW), P(perE, perW), P(perE, perE), P(perW, perE), P(perW, perW)
            }),

            // --- avenue to perimeter, one per corner --------------------------------------
            new CityRoute("North Avenue -> Perimeter", RouteTier.Green, true, new[]
            {
                P(avE, avN), P(avE, perE), P(perE, perE)
            }),
            new CityRoute("South Avenue -> Perimeter", RouteTier.Green, true, new[]
            {
                P(avW, avS), P(avW, perW), P(perW, perW)
            }),

            // --- the Cut ------------------------------------------------------------------
            // Down the north ramp, along the trench floor, and back out. This is the only route
            // that leaves y = 0, and it exists because the trench is the one place in the greybox
            // where a walkable surface is below the street.
            new CityRoute("The Cut (descent)", RouteTier.Green, true, new[]
            {
                new Vector3(cut.CentreX, StreetY, quarter.MaxZ + CityDesign.AvenueWidth * 0.5f),
                new Vector3(cut.CentreX, StreetY, cut.MaxZ - 2f),
                new Vector3(cut.CentreX, CityDesign.CutFloorY + StreetY, cut.MaxZ - 34f),
                new Vector3(cut.CentreX, CityDesign.CutFloorY + StreetY, cut.MinZ + 12f)
            })
        };
    }

    private static Vector3 P(float x, float z) => new Vector3(x, StreetY, z);

    // ------------------------------------------------------------------ rooftops (6C)

    /// <summary>
    /// Turns each authored pair of ends into the route the network actually offers, by asking
    /// <see cref="RoofGraph"/> for the fewest-move path between them.
    ///
    /// A route whose ends do not connect is emitted with a single waypoint rather than dropped, so
    /// the validator reports "this journey is no longer possible" instead of quietly listing one
    /// route fewer than the phase claims.
    /// </summary>
    private static List<CityRoute> RoofRoutes()
    {
        List<CityRoute> routes = new List<CityRoute>();
        CityPlanResult plan = CityPlan.Generate();
        CityTraversalResult traversal = plan.Traversal;
        RoofGraph graph = RoofGraph.Build(plan);

        foreach (RoofRouteSite site in CityTraversal.RoofRoutes)
        {
            AscentPlan entry = CityTraversal.Ascent(traversal, site.Entry);
            string target = CityTraversal.TargetNode(traversal, site.Target);

            if (entry == null || !traversal.Surfaces.ContainsKey(target))
            {
                routes.Add(new CityRoute(site.Name, site.Tier, false, new Vector3[0], new string[0]));
                continue;
            }

            List<string> path = graph.Path(entry.TopNode, target);

            if (path == null)
            {
                routes.Add(new CityRoute(site.Name, site.Tier, false, new Vector3[0],
                    new[] { entry.TopNode }));
                continue;
            }

            Vector3[] waypoints = new Vector3[path.Count];

            for (int i = 0; i < path.Count; i++)
            {
                // A rooftop waypoint is the surface itself, not a point hovering above it: it is
                // never walked to, only probed for, and the harness needs to know exactly what
                // height it expects to find something at.
                waypoints[i] = traversal.Surfaces[path[i]].Centre;
            }

            routes.Add(new CityRoute(site.Name, site.Tier, false, waypoints, path.ToArray()));
        }

        return routes;
    }
}
