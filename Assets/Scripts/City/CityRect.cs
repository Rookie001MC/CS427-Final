using UnityEngine;

/// <summary>
/// An axis-aligned footprint on the ground plane, expressed in world X/Z.
///
/// Unity's <see cref="Rect"/> uses x/y, which reads as a screen rectangle and has repeatedly been
/// a source of confusion in this project's builders (a "y" that is really a "z"). This type names
/// the axes it actually uses, so a footprint can never be silently interpreted as an elevation.
/// </summary>
public readonly struct CityRect
{
    public readonly float MinX;
    public readonly float MaxX;
    public readonly float MinZ;
    public readonly float MaxZ;

    public CityRect(float minX, float maxX, float minZ, float maxZ)
    {
        MinX = Mathf.Min(minX, maxX);
        MaxX = Mathf.Max(minX, maxX);
        MinZ = Mathf.Min(minZ, maxZ);
        MaxZ = Mathf.Max(minZ, maxZ);
    }

    public static CityRect FromCentre(float centreX, float centreZ, float width, float depth)
        => new CityRect(centreX - width * 0.5f, centreX + width * 0.5f,
                        centreZ - depth * 0.5f, centreZ + depth * 0.5f);

    public float Width => MaxX - MinX;
    public float Depth => MaxZ - MinZ;
    public float Area => Width * Depth;
    public float CentreX => (MinX + MaxX) * 0.5f;
    public float CentreZ => (MinZ + MaxZ) * 0.5f;

    public Vector3 Centre(float y) => new Vector3(CentreX, y, CentreZ);

    public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

    public bool Overlaps(in CityRect other)
        => MinX < other.MaxX && MaxX > other.MinX && MinZ < other.MaxZ && MaxZ > other.MinZ;

    /// <summary>Shrinks on all four sides. A negative amount grows the rect.</summary>
    public CityRect Inset(float amount)
        => new CityRect(MinX + amount, MaxX - amount, MinZ + amount, MaxZ - amount);

    /// <summary>
    /// Shortest horizontal gap between two footprints. Zero when they touch or overlap.
    /// Diagonal separation is measured as a true 2D distance, matching the reach test the route
    /// harnesses use - a corner-to-corner jump is longer than either axis alone.
    /// </summary>
    public float GapTo(in CityRect other)
    {
        float dx = Mathf.Max(0f, Mathf.Max(MinX - other.MaxX, other.MinX - MaxX));
        float dz = Mathf.Max(0f, Mathf.Max(MinZ - other.MaxZ, other.MinZ - MaxZ));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Length of the boundary two footprints share, or 0 if they only meet at a corner.
    ///
    /// <see cref="GapTo"/> reports 0 for two rectangles that touch at a single point, which is
    /// exactly the case a player cannot walk through - the tower spiral's corner landings meet the
    /// shaft that way and would grade as a free step. This is the question that actually matters
    /// for a flush transition: is there a shared edge wide enough to stand on.
    /// </summary>
    public float SharedEdgeWith(in CityRect other)
    {
        float overlapX = Mathf.Min(MaxX, other.MaxX) - Mathf.Max(MinX, other.MinX);
        float overlapZ = Mathf.Min(MaxZ, other.MaxZ) - Mathf.Max(MinZ, other.MinZ);

        if (overlapX < 0f || overlapZ < 0f)
        {
            return 0f;
        }

        if (overlapX <= 0.001f)
        {
            return overlapZ;
        }

        if (overlapZ <= 0.001f)
        {
            return overlapX;
        }

        return Mathf.Min(overlapX, overlapZ);
    }

    public override string ToString()
        => $"x[{MinX:F1}..{MaxX:F1}] z[{MinZ:F1}..{MaxZ:F1}]";
}
