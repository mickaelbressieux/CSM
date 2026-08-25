using System;
using UnityEngine;

/// <summary>
/// Fades the whole screen out as part of a scene transition. A thin adapter onto
/// <see cref="ScreenFader"/>, so the transition seam stays a plain <see cref="ITransitionEffect"/>
/// and nothing has to know the fader is a persistent singleton.
///
/// The curtain is deliberately left down when this finishes: the caller loads a scene next,
/// and <see cref="ScreenFader"/> lifts it again on the other side.
/// </summary>
public class ScreenFadeEffect : MonoBehaviour, ITransitionEffect
{
    [SerializeField] private float duration = 0.5f;
    [SerializeField] private Color fadeColor = Color.black;

    public void Play(Action onComplete)
    {
        ScreenFader.Instance.FadeOut(duration, fadeColor, onComplete);
    }
}
