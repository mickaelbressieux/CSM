using System;
using UnityEngine;

/// <summary>
/// Plays several <see cref="ITransitionEffect"/>s one after another, and is itself an
/// <see cref="ITransitionEffect"/> - so anywhere one effect fits, a whole sequence fits.
///
/// This is what lets a campaign node explode and then fade the screen without either effect
/// knowing the other exists, and what lets you swap the explosion for a character animation
/// later by changing one entry in the list.
/// </summary>
public class TransitionEffectSequence : MonoBehaviour, ITransitionEffect
{
    [Header("Played in order, top to bottom")]
    // Components implementing ITransitionEffect. Serialized as MonoBehaviours so any effect
    // can be wired in from the inspector without this sequence naming a concrete type - the
    // same idiom as StoneLauncher.shotProviderSource.
    [SerializeField] private MonoBehaviour[] effectSources = Array.Empty<MonoBehaviour>();

    public void Play(Action onComplete)
    {
        PlayFrom(0, onComplete);
    }

    private void PlayFrom(int index, Action onComplete)
    {
        if (effectSources == null || index >= effectSources.Length)
        {
            onComplete?.Invoke();
            return;
        }

        ITransitionEffect effect = Resolve(effectSources[index]);
        if (effect == null)
        {
            // A missing or mis-typed entry must not strand the player in a scene that never
            // changes, so skip it and carry on down the list.
            PlayFrom(index + 1, onComplete);
            return;
        }

        effect.Play(() => PlayFrom(index + 1, onComplete));
    }

    private ITransitionEffect Resolve(MonoBehaviour source)
    {
        if (source == null)
        {
            return null;
        }

        if (source is ITransitionEffect effect)
        {
            return effect;
        }

        Debug.LogError(
            $"{nameof(TransitionEffectSequence)}: '{source.GetType().Name}' does not implement ITransitionEffect.",
            this);
        return null;
    }
}
