using System;

/// <summary>
/// A visual flourish that plays before a scene transition commits. <see cref="SceneTransitionTrigger"/>
/// hands over control, and the transition only proceeds once the effect calls back.
///
/// Implementations MUST invoke <paramref name="onComplete"/> exactly once, or the player is
/// left stranded in a scene that never changes.
/// </summary>
public interface ITransitionEffect
{
    void Play(Action onComplete);
}
