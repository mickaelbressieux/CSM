using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The human "input half" of a curling throw, split out of the old
/// <c>CurlingStoneController</c>. Reads the keyboard to compose aim / power / curl,
/// draws the aim-preview arrow, and — on Space — commits the shot by raising
/// <see cref="ShotReady"/>. It knows nothing about physics; <see cref="StoneLauncher"/>
/// handles that.
///
/// This is one implementation of <see cref="IShotProvider"/>; an AI implementation will
/// sit behind the same interface on a later branch.
/// </summary>
public class PlayerShotProvider : MonoBehaviour, IShotProvider, IShotContextReceiver
{
    [Header("Aiming")]
    public float aimSpeed = 60f;       // degrees per second
    public float maxAimAngle = 45f;    // max angle left/right from forward

    [Header("Power")]
    public float minPower = 5f;
    public float maxPower = 30f;
    public float powerChangeSpeed = 10f;

    [Header("Curl")]
    public float maxCurlPower = 5f;
    public float curlChangeSpeed = 3f;

    [Header("Lateral Offset")]
    public float maxLateralOffset = 2f;      // max sideways shift of the launch point, in meters
    public float lateralChangeSpeed = 2f;    // meters per second

    [Header("Aim Arrow")]
    public GameObject aimArrow;
    public float arrowYOffset = 0.05f;       // raise above the ice surface
    public float arrowForwardOffset = 2f;    // distance from stone centre along aim direction

    // In-progress shot values.
    private float aimAngle = 0f;
    private float currentPower;
    // Negative = curl left, positive = curl right (relative to direction of travel).
    private float curlAmount = 0f;
    // Sideways shift of the launch point. Negative = left, positive = right.
    private float lateralOffset = 0f;

    // While armed, the provider reads input and can raise a shot. Set false the instant
    // Space is pressed so a shot cannot be fired twice, until Rearm() is called on reset.
    private bool armed = true;

    /// <inheritdoc/>
    public event Action<ShotData> ShotReady;

    /// <inheritdoc/>
    public ShotData CurrentShot =>
        new ShotData(Quaternion.Euler(0f, aimAngle, 0f) * Vector3.forward, currentPower, curlAmount, lateralOffset);

    /// <inheritdoc/>
    public float MaxCurl => maxCurlPower;

    /// <inheritdoc/>
    public float MaxLateral => maxLateralOffset;

    /// <inheritdoc/>
    // The manager injects the shared aim arrow (a scene object the prefab can't reference).
    // Ignores a null so a prefab-set arrow is preserved.
    public void Configure(ShotContext context)
    {
        if (context.AimArrow != null)
            aimArrow = context.AimArrow;
    }

    private void Awake()
    {
        currentPower = (minPower + maxPower) / 2f;
        if (aimArrow != null) aimArrow.SetActive(true);
    }

    private void Update()
    {
        if (!armed || Keyboard.current == null)
            return;

        // Left / right  →  aim
        if (Keyboard.current.leftArrowKey.isPressed)
            aimAngle -= aimSpeed * Time.deltaTime;
        if (Keyboard.current.rightArrowKey.isPressed)
            aimAngle += aimSpeed * Time.deltaTime;

        aimAngle = Mathf.Clamp(aimAngle, -maxAimAngle, maxAimAngle);

        // Up / down  →  power
        if (Keyboard.current.upArrowKey.isPressed)
            currentPower += powerChangeSpeed * Time.deltaTime;
        if (Keyboard.current.downArrowKey.isPressed)
            currentPower -= powerChangeSpeed * Time.deltaTime;

        currentPower = Mathf.Clamp(currentPower, minPower, maxPower);

        // Q  →  curl left   |   E  →  curl right
        if (Keyboard.current.qKey.isPressed)
            curlAmount -= curlChangeSpeed * Time.deltaTime;
        if (Keyboard.current.eKey.isPressed)
            curlAmount += curlChangeSpeed * Time.deltaTime;
        curlAmount = Mathf.Clamp(curlAmount, -maxCurlPower, maxCurlPower);

        // A  →  offset left   |   D  →  offset right
        // Shifts where the throw starts from, without touching the aim angle.
        if (Keyboard.current.aKey.isPressed)
            lateralOffset -= lateralChangeSpeed * Time.deltaTime;
        if (Keyboard.current.dKey.isPressed)
            lateralOffset += lateralChangeSpeed * Time.deltaTime;
        lateralOffset = Mathf.Clamp(lateralOffset, -maxLateralOffset, maxLateralOffset);

        // Space  →  commit the shot
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
            CommitShot();

        UpdateAimArrow();
    }

    private void CommitShot()
    {
        armed = false;
        if (aimArrow != null) aimArrow.SetActive(false);
        ShotReady?.Invoke(CurrentShot);
    }

    // Keep the aim arrow aligned with the current aim angle.
    private void UpdateAimArrow()
    {
        if (aimArrow == null)
            return;

        Vector3 aimDir = Quaternion.Euler(0f, aimAngle, 0f) * Vector3.forward;
        aimArrow.transform.position = new Vector3(
            transform.position.x + aimDir.x * arrowForwardOffset,
            transform.position.y + arrowYOffset,
            transform.position.z + aimDir.z * arrowForwardOffset);
        aimArrow.transform.rotation = Quaternion.LookRotation(Vector3.up, aimDir)
                                    * Quaternion.Euler(0f, 0f, -90f);
    }

    /// <inheritdoc/>
    public void Rearm()
    {
        aimAngle      = 0f;
        currentPower  = (minPower + maxPower) / 2f;
        curlAmount    = 0f;
        lateralOffset = 0f;
        armed         = true;

        if (aimArrow != null) aimArrow.SetActive(true);
    }
}
