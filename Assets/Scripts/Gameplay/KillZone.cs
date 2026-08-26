using System;
using UnityEngine;

/// <summary>
/// Reusable instant-death trigger. Drop one anywhere and it works: the manager subscribes to the
/// static event rather than holding a list, so new zones need no wiring.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class KillZone : MonoBehaviour
{
    [SerializeField] private string zoneName = "Kill Zone";

    public string ZoneName => zoneName;

    /// <summary>Raised when the player enters any kill zone.</summary>
    public static event Action<KillZone> PlayerEntered;

    private void Reset()
    {
        Collider attached = GetComponent<Collider>();
        if (attached != null)
        {
            attached.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<BasicFirstPersonController>() == null)
        {
            return;
        }

        PlayerEntered?.Invoke(this);
    }

    private void OnDrawGizmos()
    {
        Collider attached = GetComponent<Collider>();
        if (attached == null)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.12f);
        Gizmos.DrawCube(attached.bounds.center, attached.bounds.size);
        Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(attached.bounds.center, attached.bounds.size);
    }
}
