using UnityEngine;

/// <summary>
/// Trigger volume that moves the player's respawn point when entered.
/// Place over a safe deck; the player keeps the scene's original spawn until the first
/// checkpoint is crossed.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class CheckpointVolume : MonoBehaviour
{
    [SerializeField] private Transform respawnPoint;
    [SerializeField] private string checkpointName = "Checkpoint";
    [SerializeField] private bool logOnActivate = true;

    private bool activated;

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
        if (activated)
        {
            return;
        }

        BasicFirstPersonController controller = other.GetComponentInParent<BasicFirstPersonController>();
        if (controller == null)
        {
            return;
        }

        Vector3 target = respawnPoint != null ? respawnPoint.position : transform.position;
        controller.SetSpawn(target);
        activated = true;

        if (logOnActivate)
        {
            Debug.Log($"Checkpoint reached: {checkpointName}", this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 target = respawnPoint != null ? respawnPoint.position : transform.position;
        Gizmos.color = new Color(0.15f, 0.85f, 1f, 0.9f);
        Gizmos.DrawWireSphere(target, 0.5f);
        Gizmos.DrawLine(transform.position, target);
    }
}
