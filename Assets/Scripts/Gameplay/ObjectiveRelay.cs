using UnityEngine;

/// <summary>
/// One of Skybound City's five objective relays: what it is called, which district it belongs to,
/// and what it looks like before and after it is captured.
///
/// It deliberately owns no progress of its own. The crossing is counted by the
/// <see cref="CheckpointVolume"/> on the same GameObject, running under a
/// <see cref="CheckpointManager"/> in <see cref="CheckpointRouteOrder.Set"/> mode, so the mission
/// reuses the run systems the other two levels use rather than growing a parallel copy of them -
/// the timer, the split times, the HUD readout, the finish gate and RESTART RUN all work here for
/// free. <see cref="ObjectiveTracker"/> maps a counted crossing back to the relay it belongs to and
/// tells the relay to change its face.
/// </summary>
public sealed class ObjectiveRelay : MonoBehaviour
{
    [Tooltip("Stable id. Matches the relay's name in CityTraversal.Relays.")]
    [SerializeField] private string relayId = "Relay";

    [Tooltip("How the relay reads in the HUD - the district's name.")]
    [SerializeField] private string displayName = "District";

    [Tooltip("The volume that counts this relay. Set-mode member of the CheckpointManager route.")]
    [SerializeField] private CheckpointVolume volume;

    [Tooltip("Renderers that change material when the relay is captured. The mast, normally.")]
    [SerializeField] private Renderer[] statusRenderers;

    [SerializeField] private Material idleMaterial;
    [SerializeField] private Material capturedMaterial;

    public string RelayId => relayId;
    public string DisplayName => displayName;
    public bool Captured { get; private set; }

    /// <summary>
    /// The volume that counts this relay, resolved on demand rather than only in Awake:
    /// <see cref="ObjectiveTracker"/> matches a counted crossing back to the relay it belongs to
    /// through this property, and a relay whose Awake has not run must not answer "no volume" and
    /// quietly drop the capture.
    /// </summary>
    public CheckpointVolume Volume
    {
        get
        {
            if (volume == null)
            {
                volume = GetComponent<CheckpointVolume>();
            }

            return volume;
        }
    }

    /// <summary>Where the compass points, and where a respawn returns the player.</summary>
    public Vector3 Position => transform.position;

    private void Awake()
    {
        // Reading the property is what resolves the reference.
        _ = Volume;
        Apply(Captured);
    }

    /// <summary>Called by <see cref="ObjectiveTracker"/> when the crossing has been counted.</summary>
    internal void MarkCaptured()
    {
        if (Captured)
        {
            return;
        }

        Captured = true;
        Apply(true);
    }

    /// <summary>Puts the relay back to uncaptured. Used by RESTART RUN.</summary>
    public void ResetRelay()
    {
        Captured = false;
        Apply(false);
    }

    private void Apply(bool captured)
    {
        Material material = captured ? capturedMaterial : idleMaterial;

        if (material == null || statusRenderers == null)
        {
            return;
        }

        foreach (Renderer renderer in statusRenderers)
        {
            if (renderer != null)
            {
                // sharedMaterial, not material: this swaps which asset the renderer points at
                // rather than instancing a copy of it per relay.
                renderer.sharedMaterial = material;
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Captured
            ? new Color(0.30f, 1f, 0.55f, 0.9f)
            : new Color(0.15f, 0.85f, 1f, 0.9f);

        Gizmos.DrawWireSphere(transform.position, 1.2f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 8f);

#if UNITY_EDITOR
        UnityEditor.Handles.color = Gizmos.color;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 9f, displayName);
#endif
    }
}
