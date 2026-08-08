using System;

/// <summary>
/// The seam between "who decides a shot" and "what the stone does with it".
///
/// A provider composes a <see cref="ShotData"/> (over several frames for the human,
/// after some deliberation for the AI) and raises <see cref="ShotReady"/> exactly once
/// when the shot is committed. <see cref="StoneLauncher"/> listens for that event and
/// executes the throw, without ever knowing which concrete provider produced it.
///
/// <see cref="PlayerShotProvider"/> implements this today; an <c>AIShotProvider</c> will
/// implement the same interface on a later branch and drop into the same launcher.
/// </summary>
public interface IShotProvider
{
    /// <summary>
    /// Raised once when the provider has committed to a shot. Event-based (rather than
    /// the launcher polling each frame) because neither the player nor the AI produces a
    /// shot instantly, so "tell me when you're ready" fits both.
    /// </summary>
    event Action<ShotData> ShotReady;

    /// <summary>
    /// The live, still-in-progress shot being composed, for the HUD to preview power /
    /// curl / aim before release. Meaningful only while the provider is armed.
    /// </summary>
    ShotData CurrentShot { get; }

    /// <summary>
    /// Re-arm the provider to accept a fresh shot. Called on round reset, after a shot
    /// has been fired and the stone reset to its start.
    /// </summary>
    void Rearm();
}
