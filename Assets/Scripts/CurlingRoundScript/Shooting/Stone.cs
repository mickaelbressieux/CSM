using UnityEngine;

/// <summary>Which side a stone belongs to. Set by the match manager when the stone is spawned.</summary>
public enum StoneSide { Neutral, Player, AI }

/// <summary>A stone's lifecycle phase, driven by <see cref="StoneLauncher"/> (and the manager for Lost).</summary>
public enum StonePhase { Idle, Sliding, Stopped, Lost }

/// <summary>
/// Identity + state for one curling stone — the entity other systems (abilities, events, scoring)
/// hang off of, instead of passing raw <c>GameObject</c>s around. Deliberately thin: it holds who
/// the stone is and what phase it is in, and caches the Rigidbody. Behaviour lives in stackable
/// <see cref="StoneAbility"/> components; physics lives in <see cref="StoneLauncher"/>.
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

    private void Awake() => Body = GetComponent<Rigidbody>();

    public void SetPhase(StonePhase phase) => Phase = phase;
}
