using UnityEngine;

/// <summary>
/// Base class for a stone "power" (very heavy, mid-slide brake, double score, ...).
///
/// Powers are <b>stackable</b>: each is an independent component, and <see cref="StoneLauncher"/>
/// fires the physics hooks on <i>every</i> StoneAbility on the stone. Add two components → both run.
/// Author a new power by subclassing and overriding only the hooks it needs; the rest are no-ops.
///
/// The hooks come in two families:
/// <list type="bullet">
/// <item><b>Physics</b> — <see cref="OnLaunch"/> / <see cref="OnSlideTick"/> / <see cref="OnStopped"/>
/// / <see cref="OnStoneCollision"/>, fired by <see cref="StoneLauncher"/> during the throw.</item>
/// <item><b>Scoring</b> — <see cref="ModifyStonePoints"/>, chained by <see cref="Stone.ScorePoints"/>
/// when the end is counted. A power can use either family, or both.</item>
/// </list>
///
/// The passed <see cref="StoneLauncher"/> gives access to the physics (<c>launcher.Body</c>) and,
/// via <c>GetComponent&lt;Stone&gt;()</c>, the stone's identity/phase. Abilities may also subscribe
/// to <see cref="MatchEvents"/> (in OnEnable/OnDisable) to react to the wider match.
///
/// A power is normally attached at spawn by a <see cref="StonePowerDefinition"/>, which copies the
/// asset's tuning onto the component and stamps <see cref="PowerName"/>.
/// </summary>
public abstract class StoneAbility : MonoBehaviour
{
    /// <summary>Human-readable name for the HUD / inventory UI, stamped by the
    /// <see cref="StonePowerDefinition"/> that attached this power. Falls back to the type name
    /// for abilities authored by hand on a prefab.</summary>
    public string PowerName
    {
        get => string.IsNullOrEmpty(powerName) ? GetType().Name : powerName;
        set => powerName = value;
    }
    [SerializeField, Tooltip("Set automatically when the power comes from a StonePowerDefinition.")]
    private string powerName;

    /// <summary>Optional one-line hint the HUD shows while this power is usable (e.g. "Press S to
    /// stop the stone"). Return null when there is nothing to say — the default.</summary>
    public virtual string HudHint => null;

    // ---- Physics hooks (fired by StoneLauncher) ----------------------------------------------

    /// <summary>The stone was just launched (impulse applied). Good for one-shot setup.</summary>
    public virtual void OnLaunch(StoneLauncher launcher) { }

    /// <summary>Called each physics step while the stone is sliding — brake, boost, steer, etc.</summary>
    public virtual void OnSlideTick(StoneLauncher launcher) { }

    /// <summary>The stone has come to rest.</summary>
    public virtual void OnStopped(StoneLauncher launcher) { }

    /// <summary>The stone hit something (forwarded from OnCollisionEnter) — e.g. boost the other stone.</summary>
    public virtual void OnStoneCollision(StoneLauncher launcher, Collision collision) { }

    // ---- Scoring hook (chained by Stone.ScorePoints) -----------------------------------------

    /// <summary>
    /// This stone counts toward a score: return the adjusted number of points. Chained across every
    /// ability on the stone, so powers stack (return <paramref name="points"/> unchanged to opt out).
    /// </summary>
    public virtual int ModifyStonePoints(int points) => points;
}
