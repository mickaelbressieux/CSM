using UnityEngine;

/// <summary>Catalogue entry for <see cref="DoubleScoreAbility"/>. Raise the multiplier for a
/// higher-tier version of the same power.</summary>
[CreateAssetMenu(fileName = "DoubleScorePower", menuName = "Curling/Powers/Double Score")]
public class DoubleScorePowerDefinition : StonePowerDefinition
{
    protected override string DefaultDisplayName => "Double Score";

    // Changes how the end is scored rather than how this stone behaves -> it wears a flag.
    public override PowerCategory Category => PowerCategory.PassiveOther;

    [Header("Tuning")]
    [Tooltip("Points this stone scores are multiplied by this. 2 = double score.")]
    public int scoreMultiplier = 2;

    public override StoneAbility AttachTo(GameObject stone)
    {
        DoubleScoreAbility ability = Attach<DoubleScoreAbility>(stone);
        ability.scoreMultiplier = scoreMultiplier;
        return ability;
    }
}
