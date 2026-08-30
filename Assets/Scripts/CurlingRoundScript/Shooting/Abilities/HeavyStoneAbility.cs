using UnityEngine;

/// <summary>
/// HEAVY STONE — multiplies the stone's mass (x2 by default).
///
/// A heavier stone wins collisions: it barely deflects when it strikes another stone, and it
/// resists being knocked out of the house once parked. The mass change is applied in Awake, not at
/// launch, so it protects the stone for its whole life on the sheet.
///
/// <b>Why an extra impulse is added at launch.</b> <see cref="StoneLauncher"/> throws with
/// <c>AddForce(Direction * Power, ForceMode.Impulse)</c>, and an impulse gives Δv = impulse / mass —
/// so doubling the mass alone would halve the launch speed and every throw would fall short. To
/// keep the throw feeling identical, <see cref="OnLaunch"/> tops the impulse up to
/// <c>Power * massMultiplier</c>: with mass also multiplied by the same factor, the launch velocity
/// comes out exactly nominal. The stone flies its usual trajectory but carries double the momentum.
///
/// Note it adds an <i>impulse</i> rather than scaling <c>Body.linearVelocity</c>: AddForce is only
/// integrated by the physics step at the end of the FixedUpdate that launched the stone, so the
/// velocity is still zero when this hook runs. Both impulses land in the same step and sum.
///
/// Unity's linear damping is a velocity decay rate and is mass-independent, so the slide length
/// needs no correction either.
/// </summary>
public class HeavyStoneAbility : StoneAbility
{
    [Tooltip("Mass is multiplied by this. 2 = twice as heavy.")]
    public float massMultiplier = 2f;

    [Tooltip("Top the launch impulse up so the stone still travels at its normal speed. Turn off " +
             "for a physically honest heavy stone that is genuinely harder to throw far.")]
    public bool preserveLaunchSpeed = true;

    private void Awake()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null && massMultiplier > 0f)
            rb.mass *= massMultiplier;
    }

    public override void OnLaunch(StoneLauncher launcher)
    {
        if (!preserveLaunchSpeed || massMultiplier <= 1f)
            return;

        ShotData shot = launcher.ActiveShot;
        launcher.Body.AddForce(
            shot.Direction * shot.Power * (massMultiplier - 1f), ForceMode.Impulse);
    }
}
