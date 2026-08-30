using System.Collections.Generic;
using UnityEngine;

/// <summary>Which side a stone belongs to. Set by the match manager when the stone is spawned.</summary>
public enum StoneSide { Neutral, Player, AI }

/// <summary>A stone's lifecycle phase, driven by <see cref="StoneLauncher"/> (and the manager for Lost).</summary>
public enum StonePhase { Idle, Sliding, Stopped, Lost }

/// <summary>
/// Identity + state for one curling stone — the entity other systems (abilities, events, scoring)
/// hang off of, instead of passing raw <c>GameObject</c>s around. Deliberately thin: it holds who
/// the stone is, what phase it is in, and which powers it carries, and caches the Rigidbody.
/// Behaviour lives in stackable <see cref="StoneAbility"/> components; physics lives in
/// <see cref="StoneLauncher"/>.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Stone : MonoBehaviour
{
    [Tooltip("Set by the match manager on spawn.")]
    public StoneSide Side = StoneSide.Neutral;

    /// <summary>Current lifecycle phase. Updated by the launcher; the manager sets Lost.
    /// [field: SerializeField] surfaces it in the Inspector (read-only in code) so you can watch it
    /// change live in Play mode; it stays settable only through <see cref="SetPhase"/>.</summary>
    [field: SerializeField] public StonePhase Phase { get; private set; } = StonePhase.Idle;

    /// <summary>The stone's Rigidbody, cached for abilities and recovery code.</summary>
    public Rigidbody Body { get; private set; }

    /// <summary>
    /// The powers this stone carries — the single source of truth, used both by
    /// <see cref="StoneLauncher"/> (to fire the physics hooks) and by scoring (see
    /// <see cref="ScorePoints"/>). Cached in Awake, which is why the match manager attaches a
    /// loadout's powers while the stone GameObject is still INACTIVE.
    /// </summary>
    public IReadOnlyList<StoneAbility> Abilities { get; private set; } =
        System.Array.Empty<StoneAbility>();

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        RefreshAbilities();
    }

    // Also refresh on every activation, NOT just in Awake. Awake runs once per component lifetime,
    // and Instantiate() runs it immediately when the prefab root is saved active — i.e. BEFORE the
    // spawn path has attached the stone's powers. Caching only in Awake therefore left Abilities
    // permanently empty, silently killing every power, and made the whole system depend on an
    // invisible "is the prefab asset active?" flag. OnEnable re-runs on SetActive(true), and the
    // spawn path calls RefreshAbilities() explicitly as well.
    private void OnEnable() => RefreshAbilities();

    public void SetPhase(StonePhase phase) => Phase = phase;

    /// <summary>Re-scan the GameObject for abilities. Only needed if a power is added after Awake
    /// (the normal spawn path adds them before the stone is activated).</summary>
    public void RefreshAbilities() => Abilities = GetComponents<StoneAbility>();

    /// <summary>
    /// How many points this stone is worth when it counts toward a score, after every power has had
    /// a say. Scoring code calls this instead of assuming one point per stone.
    /// Powers stack because the value is chained through each ability in turn — two "double score"
    /// powers on one stone therefore give x4.
    /// </summary>
    public int ScorePoints(int basePoints = 1)
    {
        int points = basePoints;
        for (int i = 0; i < Abilities.Count; i++)
            points = Abilities[i].ModifyStonePoints(points);
        return points;
    }
}
