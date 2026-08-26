using System;
using UnityEngine;

/// <summary>
/// Fires once when the player drops below a configurable world Y. Stays disarmed until
/// <see cref="Rearm"/> is called, so a player left below the line does not spam the event while
/// the Game Over screen is up.
/// </summary>
public sealed class FallDetector : MonoBehaviour
{
    [SerializeField] private Transform target;

    [Tooltip("World Y below which the player is considered dead.")]
    [SerializeField] private float deathHeight = -11f;

    public float DeathHeight => deathHeight;

    /// <summary>Raised once per arming, when the target falls below <see cref="deathHeight"/>.</summary>
    public event Action FellBelowThreshold;

    private bool armed = true;

    private void Update()
    {
        if (!armed || target == null || target.position.y >= deathHeight)
        {
            return;
        }

        armed = false;
        FellBelowThreshold?.Invoke();
    }

    /// <summary>Re-arms after a respawn.</summary>
    public void Rearm() => armed = true;

    private void OnDrawGizmosSelected()
    {
        Vector3 centre = target != null
            ? new Vector3(target.position.x, deathHeight, target.position.z)
            : new Vector3(transform.position.x, deathHeight, transform.position.z);

        Gizmos.color = new Color(1f, 0.35f, 0.25f, 0.9f);
        Gizmos.DrawWireCube(centre, new Vector3(200f, 0.02f, 200f));
    }
}
