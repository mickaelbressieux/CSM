using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Catalogue entry for <see cref="StoppableStoneAbility"/>. The number of brakes per throw
/// is the natural upgrade axis for story mode.</summary>
[CreateAssetMenu(fileName = "StoppablePower", menuName = "Curling/Powers/Stoppable Stone")]
public class StoppablePowerDefinition : StonePowerDefinition
{
    protected override string DefaultDisplayName => "Stoppable Stone";

    [Header("Tuning")]
    [Tooltip("Key that stops the stone mid-slide.")]
    public Key stopKey = Key.S;

    [Tooltip("How many times the stone can be braked during one throw.")]
    public int usesPerThrow = 1;

    [Tooltip("Deceleration in m/s^2 once braking. 0 = stop dead on the spot.")]
    public float brakeDeceleration = 0f;

    public override StoneAbility AttachTo(GameObject stone)
    {
        StoppableStoneAbility ability = Attach<StoppableStoneAbility>(stone);
        ability.stopKey           = stopKey;
        ability.usesPerThrow      = usesPerThrow;
        ability.brakeDeceleration = brakeDeceleration;
        return ability;
    }
}
