using UnityEngine;

/// <summary>
/// Immutable description of a single curling throw.
///
/// Produced by an <see cref="IShotProvider"/> (the human player today, the AI on a
/// later branch) and consumed by <see cref="StoneLauncher"/>, which actually applies
/// the physics. It is deliberately source-agnostic: the launcher never knows or cares
/// whether a shot came from a keyboard or from an AI.
///
/// The aim is stored as a world-space <see cref="Direction"/> vector rather than an
/// angle, because the AI naturally reasons in "direction toward this target" and would
/// otherwise have to convert back to an angle relative to world-forward.
/// </summary>
public readonly struct ShotData
{
    /// <summary>Normalized world-space direction the stone is launched along.</summary>
    public readonly Vector3 Direction;

    /// <summary>Launch impulse magnitude (the old <c>currentPower</c>).</summary>
    public readonly float Power;

    /// <summary>
    /// Signed curl. Negative = curl left, positive = curl right, relative to the
    /// stone's direction of travel.
    /// </summary>
    public readonly float Curl;

    /// <summary>
    /// Signed sideways shift of the launch position, in world meters along the sheet's
    /// right axis. Negative = left, positive = right — the same convention as
    /// <see cref="Curl"/>.
    ///
    /// <see cref="Direction"/> is deliberately unaffected, so a non-zero offset
    /// parallel-translates the whole trajectory instead of rotating it (the real-curling
    /// "move on the hack"). That is what makes it a different tool from the aim angle.
    /// </summary>
    public readonly float LateralOffset;

    // lateralOffset is defaulted so existing three-argument callers keep working.
    public ShotData(Vector3 direction, float power, float curl, float lateralOffset = 0f)
    {
        // Normalize defensively so callers may pass any non-unit direction
        // (e.g. a raw target-minus-position vector).
        Direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        Power = power;
        Curl = curl;
        LateralOffset = lateralOffset;
    }
}
