using UnityEngine;

/// <summary>Catalogue entry for <see cref="HeavyStoneAbility"/>. Tune the weight here, not in code.</summary>
[CreateAssetMenu(fileName = "HeavyPower", menuName = "Curling/Powers/Heavy Stone")]
public class HeavyPowerDefinition : StonePowerDefinition
{
    protected override string DefaultDisplayName => "Heavy Stone";

    // Buffs only the stone carrying it -> the stone body is what changes.
    public override PowerCategory Category => PowerCategory.PassiveSelf;

    [Header("Tuning")]
    [Tooltip("Mass is multiplied by this. 2 = twice as heavy.")]
    public float massMultiplier = 2f;

    [Tooltip("Compensate the launch impulse so the stone still travels at its normal speed.")]
    public bool preserveLaunchSpeed = true;

    public override StoneAbility AttachTo(GameObject stone)
    {
        HeavyStoneAbility ability = Attach<HeavyStoneAbility>(stone);
        ability.massMultiplier      = massMultiplier;
        ability.preserveLaunchSpeed = preserveLaunchSpeed;
        return ability;
    }
}
