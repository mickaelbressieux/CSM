using UnityEngine;

/// <summary>
/// SAMPLE power, kept as the smallest possible reference for authoring one. Adds a one-shot forward
/// impulse at launch, so the stone travels faster. Proves the <see cref="StoneAbility"/> hook works
/// and that powers stack: put two of these on a stone and both fire, doubling the bonus.
///
/// It aims along <c>launcher.ActiveShot.Direction</c> rather than the Rigidbody's velocity, because
/// the launch impulse is only integrated by the physics step at the END of the FixedUpdate that
/// fires this hook — the velocity is still zero here. Real powers should read the shot the same way.
/// </summary>
public class ExtraPowerAbility : StoneAbility
{
    [Tooltip("Extra launch impulse added along the stone's travel direction.")]
    public float bonusImpulse = 5f;

    public override void OnLaunch(StoneLauncher launcher)
    {
        launcher.Body.AddForce(launcher.ActiveShot.Direction * bonusImpulse, ForceMode.Impulse);
    }
}
