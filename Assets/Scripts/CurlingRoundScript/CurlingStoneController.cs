using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CurlingStoneController : MonoBehaviour
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
    public float curlDegreesPerMeter = 0.4f; // heading degrees rotated per meter traveled per curl unit

    [Header("Pre-shot Spin")]
    public float preShotSpinSpeed = 2f;     // rad/s of Y-axis spin per curl unit
    public float spinSpeedPerVelocity = 2f; // rad/s of Y spin per m/s of linear speed after launch

    [Header("Aim Arrow")]
    public GameObject aimArrow;
    public float arrowYOffset = 0.05f;       // raise above the ice surface
    public float arrowForwardOffset = 2f; // distance from stone centre along aim direction

    [Header("Physics")]
    public float slideDrag = 0.001f;     // drag applied once the stone is shot
    public float stopThreshold = 0.05f;

    private Rigidbody rb;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private float aimAngle = 0f;
    private float currentPower;
    private float curlLeft  = 0f;
    private float curlRight = 0f;
    private bool  shootPending = false;
    private Quaternion arrowBaseRotation;
    private float launchAngVelY  = 0f;
    private float launchSpeed    = 1f;

    public bool HasBeenShot  { get; private set; } = false;
    public bool ShotFinished { get; private set; } = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        startRotation = transform.rotation;
        currentPower = (minPower + maxPower) / 2f;

        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.constraints     = RigidbodyConstraints.FreezePosition;

        if (aimArrow != null)
            arrowBaseRotation = aimArrow.transform.rotation;
    }

    private void Update()
    {
        if (HasBeenShot || Keyboard.current == null)
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

        // Q / A  →  curl left gauge
        if (Keyboard.current.qKey.isPressed)
            curlLeft += curlChangeSpeed * Time.deltaTime;
        if (Keyboard.current.aKey.isPressed)
            curlLeft -= curlChangeSpeed * Time.deltaTime;
        curlLeft = Mathf.Clamp(curlLeft, 0f, maxCurlPower);

        // E / D  →  curl right gauge
        if (Keyboard.current.eKey.isPressed)
            curlRight += curlChangeSpeed * Time.deltaTime;
        if (Keyboard.current.dKey.isPressed)
            curlRight -= curlChangeSpeed * Time.deltaTime;
        curlRight = Mathf.Clamp(curlRight, 0f, maxCurlPower);

        // Space  →  queue shot for next FixedUpdate
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
            shootPending = true;

        // Keep the aim arrow aligned with the current aim angle
        if (aimArrow != null)
        {
            Vector3 aimDir = Quaternion.Euler(0f, aimAngle, 0f) * Vector3.forward;
            aimArrow.transform.position = new Vector3(
                transform.position.x + aimDir.x * arrowForwardOffset,
                transform.position.y + arrowYOffset,
                transform.position.z + aimDir.z * arrowForwardOffset);
            aimArrow.transform.rotation = Quaternion.LookRotation(Vector3.up, aimDir)
                                        * Quaternion.Euler(0f, 0f, -90f);
        }
    }

    private void FixedUpdate()
    {
        // Apply the queued shot inside FixedUpdate so the impulse velocity
        // is visible to the physics engine before the stop-check runs.
        if (shootPending)
        {
            shootPending     = false;
            HasBeenShot      = true;
            rb.constraints   = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.linearDamping = slideDrag;
            if (aimArrow != null) aimArrow.SetActive(false);
            Vector3 dir = Quaternion.Euler(0f, aimAngle, 0f) * Vector3.forward;
            rb.AddForce(dir * currentPower, ForceMode.Impulse);
            // Capture pre-shot spin so post-launch spin starts at the same value
            launchAngVelY = (curlRight - curlLeft) * preShotSpinSpeed;
            launchSpeed   = currentPower / rb.mass; // approximate: impulse/mass ≈ Δv
            rb.angularVelocity = new Vector3(0f, launchAngVelY, 0f);
            return; // skip stop-check this frame; velocity is updated after physics step
        }

        if (!HasBeenShot || ShotFinished)
        {
            if (!HasBeenShot)
            {
                rb.linearVelocity  = Vector3.zero;
                float preShotCurl = curlRight - curlLeft;
                rb.angularVelocity = new Vector3(0f, preShotCurl * preShotSpinSpeed, 0f);
            }
            return;
        }

        // Detect stop
        if (rb.linearVelocity.magnitude <= stopThreshold)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            ShotFinished = true;
            return;
        }

        // Drive Y-axis spin proportionally to current linear speed, anchored to launch spin
        float speedRatio = (launchSpeed > 0.001f) ? rb.linearVelocity.magnitude / launchSpeed : 0f;
        rb.angularVelocity = new Vector3(0f, launchAngVelY * speedRatio, 0f);

        // Apply curl: rotate the velocity heading by a tiny angle each frame.
        // Degrees turned = curlDegreesPerMeter * distanceTravelledThisStep * netCurl
        // This keeps speed constant so the stone can never be pushed backwards.
        float netCurl = curlRight - curlLeft;
        if (Mathf.Abs(netCurl) > 0.001f)
        {
            float speed        = rb.linearVelocity.magnitude;
            float distThisStep = speed * Time.fixedDeltaTime;
            float angleDeg     = netCurl * curlDegreesPerMeter * distThisStep;
            rb.linearVelocity  = Quaternion.Euler(0f, angleDeg, 0f) * rb.linearVelocity;
        }
    }

    public void ResetStone()
    {
        HasBeenShot   = false;
        ShotFinished  = false;
        shootPending  = false;

        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.linearDamping   = 0f;
        rb.constraints     = RigidbodyConstraints.FreezePosition;

        transform.position = startPosition;
        transform.rotation = startRotation;

        aimAngle     = 0f;
        currentPower = (minPower + maxPower) / 2f;
        curlLeft     = 0f;
        curlRight    = 0f;

        if (aimArrow != null) aimArrow.SetActive(true);
    }

    public float   GetCurrentPower()  => currentPower;
    public float   GetCurlLeft()      => curlLeft;
    public float   GetCurlRight()     => curlRight;
    public Vector3 GetAimDirection()  => Quaternion.Euler(0f, aimAngle, 0f) * Vector3.forward;
}
