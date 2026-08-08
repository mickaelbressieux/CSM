using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// TEMPORARY, throwaway fake AI — a showcase that a non-human source can drive the exact
/// same <see cref="StoneLauncher"/> as the player, purely by implementing
/// <see cref="IShotProvider"/>. It is intentionally dumb: it aims straight at a target and
/// throws with a configurable power and curl (optionally jittered), after a short "thinking"
/// pause. The configured curl bends the path on its own — that visible curve is the point.
///
/// This is NOT the real AI (see <c>AIStoneController</c>, left untouched). Everything under
/// this <c>AIShotProviderTemp</c> folder is meant to be deleted/replaced.
/// </summary>
public class FakeAIShotProvider : MonoBehaviour, IShotProvider
{
    [Header("Shot tuning")]
    [Tooltip("Launch impulse magnitude the AI aims for.")]
    public float basePower = 17f;
    [Tooltip("Random +/- variation added to basePower. Set 0 for an exact, repeatable shot.")]
    public float powerJitter = 0f;
    [Tooltip("Curl the AI applies. Negative = left, positive = right.")]
    public float baseCurl = 0f;
    [Tooltip("Random +/- variation added to baseCurl. Set 0 for an exact, repeatable curl.")]
    public float curlJitter = 0f;

    [Header("Behaviour")]
    [Tooltip("Seconds the AI 'thinks' before releasing, once it is its turn.")]
    public float thinkDelaySeconds = 1f;

    [Tooltip("What the AI aims at (the house center). Assigned by the match manager.")]
    public Transform target;

    // Whether a shot may still be produced. Set false once fired so it throws exactly once.
    private bool armed = true;
    // The shot the AI intends / has committed to, exposed for the HUD and idle spin.
    private ShotData intendedShot;

    /// <inheritdoc/>
    public event Action<ShotData> ShotReady;

    /// <inheritdoc/>
    public ShotData CurrentShot => intendedShot;

    private void OnEnable()
    {
        // The manager activates the stone only when it is this AI's turn, so "on enable"
        // is the natural moment to start deliberating.
        if (armed)
            StartCoroutine(ThinkThenShoot());
    }

    private IEnumerator ThinkThenShoot()
    {
        yield return new WaitForSeconds(thinkDelaySeconds);
        if (!armed)
            yield break;

        armed = false;
        intendedShot = BuildShot();
        ShotReady?.Invoke(intendedShot);
    }

    private ShotData BuildShot()
    {
        // Aim flat along the ice toward the target; fall back to forward if unset.
        Vector3 dir = Vector3.forward;
        if (target != null)
        {
            dir = target.position - transform.position;
            dir.y = 0f;
        }

        float power = basePower + UnityEngine.Random.Range(-powerJitter, powerJitter);
        float curl  = baseCurl  + UnityEngine.Random.Range(-curlJitter, curlJitter);
        return new ShotData(dir, power, curl);
    }

    /// <inheritdoc/>
    public void Rearm()
    {
        armed = true;
        if (isActiveAndEnabled)
            StartCoroutine(ThinkThenShoot());
    }
}
