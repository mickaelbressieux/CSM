using UnityEngine;

/// <summary>
/// SAMPLE power (delete when real powers exist). Adds a one-shot forward impulse at launch, so the
/// stone travels faster. Proves the <see cref="StoneAbility"/> hook works and that powers stack:
/// put two of these on a stone and both fire, doubling the bonus.
/// </summary>
public class ExtraPowerAbility : StoneAbility
{
    [Tooltip("Extra launch impulse added along the stone's travel direction.")]
    public float bonusImpulse = 5f;

    public override void OnLaunch(StoneLauncher launcher)
    {
        Rigidbody rb = launcher.Body;
        if (rb.linearVelocity.sqrMagnitude > 1e-6f)
            rb.AddForce(rb.linearVelocity.normalized * bonusImpulse, ForceMode.Impulse);
    }
}
