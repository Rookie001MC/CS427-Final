using System;
using UnityEngine;

/// <summary>
/// One ascending run of a planned switchback stair, expressed entirely in world space.
///
/// <see cref="Start"/> is the centre line at the low landing's outgoing edge and
/// <see cref="Direction"/> is the horizontal ascending direction. The high endpoint is derived
/// from the visible step dimensions, so the builder never has to reconstruct planner geometry.
/// Consecutive flights share the same transition landing by value. Landing depths are measured
/// along the direction of travel through each landing, independently of its world axis.
/// </summary>
[Serializable]
public readonly struct StairFlightPlan
{
    public readonly string Name;
    public readonly Vector3 Start;
    public readonly Vector3 Direction;
    public readonly int StepCount;
    public readonly float RiserHeight;
    public readonly float TreadDepth;
    public readonly float ClearWidth;
    public readonly CityRect LandingBefore;
    public readonly CityRect LandingAfter;
    public readonly float LandingBeforeDepth;
    public readonly float LandingAfterDepth;

    public StairFlightPlan(string name, Vector3 start, Vector3 direction, int stepCount,
        float riserHeight, float treadDepth, float clearWidth,
        in CityRect landingBefore, in CityRect landingAfter,
        float landingBeforeDepth, float landingAfterDepth)
    {
        Name = name;
        Start = start;
        Direction = new Vector3(direction.x, 0f, direction.z).normalized;
        StepCount = stepCount;
        RiserHeight = riserHeight;
        TreadDepth = treadDepth;
        ClearWidth = clearWidth;
        LandingBefore = landingBefore;
        LandingAfter = landingAfter;
        LandingBeforeDepth = landingBeforeDepth;
        LandingAfterDepth = landingAfterDepth;
    }

    public float Rise => StepCount * RiserHeight;

    public float HorizontalRun => StepCount * TreadDepth;

    public Vector3 End => Start + Direction * HorizontalRun + Vector3.up * Rise;

    public float PitchDegrees => Mathf.Atan2(Rise, HorizontalRun) * Mathf.Rad2Deg;

    /// <summary>Axis-aligned horizontal envelope of the flight, including its clear width.</summary>
    public CityRect Footprint
    {
        get
        {
            Vector3 end = End;
            Vector3 right = Vector3.Cross(Vector3.up, Direction) * (ClearWidth * 0.5f);
            float minX = Mathf.Min(Start.x - right.x, Start.x + right.x,
                end.x - right.x, end.x + right.x);
            float maxX = Mathf.Max(Start.x - right.x, Start.x + right.x,
                end.x - right.x, end.x + right.x);
            float minZ = Mathf.Min(Start.z - right.z, Start.z + right.z,
                end.z - right.z, end.z + right.z);
            float maxZ = Mathf.Max(Start.z - right.z, Start.z + right.z,
                end.z - right.z, end.z + right.z);
            return new CityRect(minX, maxX, minZ, maxZ);
        }
    }
}
