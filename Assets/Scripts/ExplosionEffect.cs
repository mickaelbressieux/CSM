using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Blows up the object it sits on, then reports back. Wired into
/// <see cref="SceneTransitionTrigger"/> so walking into a campaign node reads as an event
/// rather than an instant cut to the next scene.
///
/// The particle prefab and the sound are both optional: the wind-up, spin and collapse alone
/// already read as an explosion, so this works on a bare cube with nothing else assigned.
/// </summary>
public class ExplosionEffect : MonoBehaviour, ITransitionEffect
{
    [Header("Timing")]
    [Tooltip("The anticipation beat: the object swells and starts to spin.")]
    [SerializeField] private float windUpDuration = 0.35f;
    [Tooltip("The burst: the object collapses to nothing.")]
    [SerializeField] private float burstDuration = 0.4f;

    [Header("Shape")]
    [SerializeField] private float windUpScale = 1.35f;
    [Tooltip("Degrees per second at the start of the wind-up; it accelerates into the burst.")]
    [SerializeField] private float spinSpeed = 180f;
    [SerializeField] private AnimationCurve windUpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve burstCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Optional dressing")]
    [SerializeField] private ParticleSystem particlesPrefab;
    [SerializeField] private AudioClip explosionClip;
    [SerializeField, Range(0f, 1f)] private float explosionVolume = 0.8f;

    private bool playing;

    /// <summary>Total time <see cref="Play"/> takes before it calls back.</summary>
    public float Duration => Mathf.Max(0f, windUpDuration) + Mathf.Max(0f, burstDuration);

    public void Play(Action onComplete)
    {
        if (playing)
        {
            // Guard rather than restart: a second caller would otherwise get a callback from
            // a run that already committed a scene load.
            Debug.LogWarning($"{nameof(ExplosionEffect)} on '{name}' is already playing.", this);
            return;
        }

        playing = true;
        TakeOverObject();
        StartCoroutine(PlayRoutine(onComplete));
    }

    private IEnumerator PlayRoutine(Action onComplete)
    {
        Vector3 baseScale = transform.localScale;

        yield return AnimateScale(baseScale, baseScale * windUpScale, windUpDuration, windUpCurve, spinSpeed);

        SpawnParticles();
        PlaySound();

        yield return AnimateScale(baseScale * windUpScale, Vector3.zero, burstDuration, burstCurve, spinSpeed * 4f);

        onComplete?.Invoke();
    }

    private IEnumerator AnimateScale(Vector3 from, Vector3 to, float duration, AnimationCurve curve, float spin)
    {
        if (duration <= 0f)
        {
            transform.localScale = to;
            yield break;
        }

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = curve.Evaluate(elapsed / duration);
            transform.localScale = Vector3.LerpUnclamped(from, to, t);
            transform.Rotate(Vector3.up, spin * Time.deltaTime, Space.Self);
            yield return null;
        }

        transform.localScale = to;
    }

    private void SpawnParticles()
    {
        if (particlesPrefab == null)
        {
            return;
        }

        // Not parented: the object it came from is collapsing to zero scale, which would drag
        // the particles down with it.
        ParticleSystem particles = Instantiate(particlesPrefab, transform.position, Quaternion.identity);
        particles.Play();
    }

    private void PlaySound()
    {
        if (explosionClip == null)
        {
            return;
        }

        AudioSource.PlayClipAtPoint(explosionClip, transform.position, explosionVolume);
    }

    /// <summary>
    /// The object is exploding, so nothing else on it should still be running: this effect
    /// owns its transform until the callback fires. Colliders go quiet so the collapsing
    /// object cannot re-fire its own trigger or shove the player around, and every other
    /// behaviour is switched off so a mover (a patrolling PNJ, say) does not keep writing
    /// position and rotation and fight the spin.
    ///
    /// Disabling a behaviour does not stop code already holding a reference to it, so the
    /// caller's completion callback still runs.
    /// </summary>
    private void TakeOverObject()
    {
        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
        {
            // Never disable this effect: that would kill the coroutine driving the animation.
            if (behaviour != this)
            {
                behaviour.enabled = false;
            }
        }
    }
}
