using UnityEngine;

/// <summary>
/// Base class for a stone "power" (very heavy, mid-slide brake, boost on hit, ...).
///
/// Powers are <b>stackable</b>: each is an independent component, and <see cref="StoneLauncher"/>
/// fires these hooks on <i>every</i> StoneAbility on the stone. Add two components → both run.
/// Author a new power by subclassing and overriding only the hooks it needs; the rest are no-ops.
///
/// The passed <see cref="StoneLauncher"/> gives access to the physics (<c>launcher.Body</c>) and,
/// via <c>GetComponent&lt;Stone&gt;()</c>, the stone's identity/phase. Abilities may also subscribe
/// to <see cref="MatchEvents"/> (in OnEnable/OnDisable) to react to the wider match.
/// </summary>
public abstract class StoneAbility : MonoBehaviour
{
    /// <summary>The stone was just launched (impulse applied). Good for one-shot setup.</summary>
    public virtual void OnLaunch(StoneLauncher launcher) { }

    /// <summary>Called each physics step while the stone is sliding — brake, boost, steer, etc.</summary>
    public virtual void OnSlideTick(StoneLauncher launcher) { }

    /// <summary>The stone has come to rest.</summary>
    public virtual void OnStopped(StoneLauncher launcher) { }

    /// <summary>The stone hit something (forwarded from OnCollisionEnter) — e.g. boost the other stone.</summary>
    public virtual void OnStoneCollision(StoneLauncher launcher, Collision collision) { }
}
