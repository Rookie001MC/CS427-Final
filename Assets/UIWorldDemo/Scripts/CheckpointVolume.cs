using UnityEngine;

/// <summary>
/// A sequential checkpoint gate.
///
/// Reports to a <see cref="CheckpointManager"/> when one is bound, which owns ordering, split
/// times and progress. If no manager is bound the volume falls back to its original standalone
/// behaviour - moving the controller's own spawn point - so scenes that predate the run systems
/// keep working unchanged.
///
/// The serialized field names are load-bearing: the editor level builders write respawnPoint,
/// checkpointName and logOnActivate through SerializedObject.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class CheckpointVolume : MonoBehaviour
{
    [SerializeField] private Transform respawnPoint;
    [SerializeField] private string checkpointName = "Checkpoint";
    [SerializeField] private bool logOnActivate = true;

    private CheckpointManager manager;
    private bool activated;

    /// <summary>1-based position in the route. -1 until a manager binds this volume.</summary>
    public int Index { get; private set; } = -1;

    public string CheckpointName => checkpointName;
    public Transform RespawnPoint => respawnPoint;
    public bool Activated => activated;

    public Vector3 RespawnPosition => respawnPoint != null ? respawnPoint.position : transform.position;
    public Quaternion RespawnRotation => respawnPoint != null ? respawnPoint.rotation : transform.rotation;

    private void Reset()
    {
        Collider attached = GetComponent<Collider>();
        if (attached != null)
        {
            attached.isTrigger = true;
        }
    }

    /// <summary>Called by <see cref="CheckpointManager"/> during Awake.</summary>
    internal void Bind(CheckpointManager owner, int oneBasedIndex)
    {
        manager = owner;
        Index = oneBasedIndex;
    }

    /// <summary>Re-arms the gate so a restarted run can cross it again.</summary>
    public void ResetActivation() => activated = false;

    private void OnTriggerEnter(Collider other)
    {
        if (activated)
        {
            return;
        }

        BasicFirstPersonController controller = other.GetComponentInParent<BasicFirstPersonController>();
        if (controller == null)
        {
            return;
        }

        if (manager != null)
        {
            // Out of order: stay armed so the gate still counts when reached properly.
            if (!manager.TryActivate(this))
            {
                return;
            }
        }
        else
        {
            controller.SetSpawn(RespawnPosition);
        }

        activated = true;

        if (logOnActivate)
        {
            Debug.Log($"Checkpoint reached: {checkpointName}", this);
        }
    }

    private void OnDrawGizmos()
    {
        Color accent = activated
            ? new Color(0.30f, 1f, 0.55f, 1f)
            : new Color(0.15f, 0.85f, 1f, 1f);

        Collider attached = GetComponent<Collider>();
        if (attached != null)
        {
            Gizmos.color = new Color(accent.r, accent.g, accent.b, 0.15f);
            Gizmos.DrawCube(attached.bounds.center, attached.bounds.size);
            Gizmos.color = accent;
            Gizmos.DrawWireCube(attached.bounds.center, attached.bounds.size);
        }

        Vector3 target = RespawnPosition;
        Gizmos.color = accent;
        Gizmos.DrawWireSphere(target, 0.45f);
        Gizmos.DrawLine(transform.position, target);
        Gizmos.DrawLine(target, target + Vector3.up * 2f);
        Gizmos.DrawRay(target + Vector3.up * 1.6f, RespawnRotation * Vector3.forward * 1.5f);

#if UNITY_EDITOR
        string label = Index > 0 ? $"{Index}. {checkpointName}" : checkpointName;
        UnityEditor.Handles.color = accent;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.8f, label);
#endif
    }
}
