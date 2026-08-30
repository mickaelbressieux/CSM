/// <summary>
/// What kind of power a <see cref="StonePowerDefinition"/> is. Two systems read it:
///
/// <list type="bullet">
/// <item><b>Visuals</b> — the category decides which channel the power's art hangs on
/// (<see cref="StoneVisuals"/>): an antenna, the stone body, or a flag. The mapping is fixed and
/// lives in exactly one place, so the visual language stays readable as powers are added — a player
/// learns once that an antenna means "I can trigger this".</item>
/// <item><b>UI</b> — the natural way to group an inventory or shop screen later.</item>
/// </list>
///
/// A power's category is intrinsic to what it does, so each definition subclass declares it in code
/// rather than exposing it as an authorable field.
/// </summary>
public enum PowerCategory
{
    /// <summary>The player triggers it during the throw (e.g. the Stoppable brake). Wears an antenna.</summary>
    Activated,

    /// <summary>Passively buffs the stone itself (e.g. Heavy). Swaps the stone body.</summary>
    PassiveSelf,

    /// <summary>Passively affects scoring or other stones (e.g. Double Score). Wears a flag.</summary>
    PassiveOther
}
