using UnityEngine;

/// <summary>
/// Per-turn scene context the match manager hands to a freshly spawned provider.
///
/// It lets the manager pass scene references a prefab cannot bake in (what to aim at, the shared
/// aim arrow) <b>without naming any concrete provider type</b> — the manager just calls
/// <see cref="Configure"/> through this interface. A provider implements it only if it needs
/// such context; providers that don't can ignore it entirely.
/// </summary>
public interface IShotContextReceiver
{
    void Configure(ShotContext context);
}

/// <summary>
/// Immutable bundle of the scene references a provider might need for one turn. Fields may be
/// null when they don't apply (e.g. no target for a provider that doesn't aim at one, or no
/// arrow on a non-human turn).
/// </summary>
public readonly struct ShotContext
{
    /// <summary>The house center to aim at. Null for providers that don't aim at a target.</summary>
    public readonly Transform HouseCenter;

    /// <summary>The shared aim-preview arrow for the human player. Null when there is none.</summary>
    public readonly GameObject AimArrow;

    public ShotContext(Transform houseCenter, GameObject aimArrow)
    {
        HouseCenter = houseCenter;
        AimArrow = aimArrow;
    }
}
