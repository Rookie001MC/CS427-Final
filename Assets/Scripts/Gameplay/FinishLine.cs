using System;
using UnityEngine;

/// <summary>
/// End-of-run trigger. Reports entry; <see cref="GameManager"/> decides whether the run is
/// eligible to finish, since it is the object that knows checkpoint progress.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class FinishLine : MonoBehaviour
{
    [Tooltip("Block the finish until every checkpoint on the route has been crossed.")]
    [SerializeField] private bool requireAllCheckpoints = true;

    public bool RequireAllCheckpoints => requireAllCheckpoints;

    /// <summary>Raised when the player enters the finish volume.</summary>
    public static event Action<FinishLine> PlayerEntered;

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

        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.14f);
        Gizmos.DrawCube(attached.bounds.center, attached.bounds.size);
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.95f);
        Gizmos.DrawWireCube(attached.bounds.center, attached.bounds.size);
    }
}
