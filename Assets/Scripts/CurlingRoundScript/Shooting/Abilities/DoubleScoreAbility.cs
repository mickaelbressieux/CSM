using UnityEngine;

/// <summary>
/// DOUBLE SCORE — when this stone is one of the counting (winning) stones at the end, it is worth
/// twice the usual point.
///
/// A pure scoring power: it overrides none of the physics hooks, only
/// <see cref="StoneAbility.ModifyStonePoints"/>, which <see cref="Stone.ScorePoints"/> chains
/// through every ability when the end is counted. Because the value is chained rather than
/// overwritten, two of these on one stone give x4, and it composes with any future scoring power.
///
/// It only ever applies to a stone that already counts — the "who wins the end / which stones are
/// closer than the opponent's best" decision stays entirely in the scoring code.
/// </summary>
public class DoubleScoreAbility : StoneAbility
{
    [Tooltip("Points this stone scores are multiplied by this. 2 = double score.")]
    public int scoreMultiplier = 2;

    public override int ModifyStonePoints(int points) => points * scoreMultiplier;
}
