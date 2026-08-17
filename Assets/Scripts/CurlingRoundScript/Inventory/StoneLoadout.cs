using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One stone the player owns: a name plus the powers bolted onto it (0..N). This is the recipe —
/// nothing physical. The match manager turns it into a real stone at spawn time by instantiating
/// the normal stone prefab and calling <see cref="ApplyTo"/>.
///
/// Plain <c>[Serializable]</c> rather than a ScriptableObject: a loadout is per-save player state
/// that changes as the player buys powers, whereas a <see cref="StonePowerDefinition"/> is fixed
/// game data shared by every save.
///
/// Duplicate powers are allowed on purpose — abilities are independent components, so two of the
/// same power simply stack.
/// </summary>
[Serializable]
public class StoneLoadout
{
    public string displayName = "Stone";

    [Tooltip("Powers on this stone. Leave empty for an ordinary stone; duplicates stack.")]
    public List<StonePowerDefinition> powers = new List<StonePowerDefinition>();

    public StoneLoadout() { }

    public StoneLoadout(string displayName) => this.displayName = displayName;

    /// <summary>Number of powers actually assigned (nulls from an unfilled inspector slot ignored).</summary>
    public int PowerCount
    {
        get
        {
            int n = 0;
            foreach (StonePowerDefinition power in powers)
                if (power != null) n++;
            return n;
        }
    }

    /// <summary>
    /// Attach every power to a freshly spawned stone. MUST be called while the GameObject is still
    /// inactive, so the components exist before Awake/OnEnable run — see the timing note on
    /// <see cref="StonePowerDefinition"/>.
    /// </summary>
    public void ApplyTo(GameObject stone)
    {
        if (stone == null)
            return;

        foreach (StonePowerDefinition power in powers)
        {
            if (power != null)
                power.AttachTo(stone);
        }
    }

    /// <summary>"Stone 1 (Heavy, Double Score)" — for HUD lines and inventory screens.</summary>
    public string Describe()
    {
        if (PowerCount == 0)
            return $"{displayName} (no power)";

        List<string> names = new List<string>();
        foreach (StonePowerDefinition power in powers)
            if (power != null) names.Add(power.DisplayName);

        return $"{displayName} ({string.Join(", ", names)})";
    }
}
