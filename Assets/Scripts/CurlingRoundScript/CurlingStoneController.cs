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
    // Heading deflection per meter traveled per unit of curl.
    // Positive curlAmount = curl RIGHT relative to the stone's direction of travel.
    public float curlDegreesPerMeter = 0.5f;

    [Header("Pre-shot Spin")]
    // rad/s of Y-axis spin per curl unit — clockwise (viewed from above) for positive curl.
    public float preShotSpinSpeed = 2f;

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
    // Negative = curl left, positive = curl right (relative to direction of travel).
    private float curlAmount = 0f;
    private bool  shootPending = false;
    private Quaternion arrowBaseRotation;

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

        // Q  →  curl left   |   E  →  curl right
        if (Keyboard.current.qKey.isPressed)
            curlAmount -= curlChangeSpeed * Time.deltaTime;
        if (Keyboard.current.eKey.isPressed)
            curlAmount += curlChangeSpeed * Time.deltaTime;
        curlAmount = Mathf.Clamp(curlAmount, -maxCurlPower, maxCurlPower);

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
            // Set spin once at launch — positive curlAmount spins clockwise (right curl).
            // Let angular damping decay it naturally; do NOT override each frame.
            rb.angularVelocity = new Vector3(0f, curlAmount * preShotSpinSpeed, 0f);
            return; // skip stop-check this frame; velocity is updated after physics step
        }

        if (!HasBeenShot || ShotFinished)
        {
            if (!HasBeenShot)
            {
                rb.linearVelocity  = Vector3.zero;
                // Spin stone for visual pre-shot feedback.
                rb.angularVelocity = new Vector3(0f, curlAmount * preShotSpinSpeed, 0f);
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

        // Curl: deflect the velocity heading by a small angle each physics step.
        // angleDeg > 0 → stone curves RIGHT relative to its direction of travel.
        // Speed is preserved (rotation keeps vector length constant).
        if (Mathf.Abs(curlAmount) > 0.001f)
        {
            float speed        = rb.linearVelocity.magnitude;
            float distThisStep = speed * Time.fixedDeltaTime;
            float angleDeg     = curlAmount * curlDegreesPerMeter * distThisStep;
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
        curlAmount   = 0f;

        if (aimArrow != null) aimArrow.SetActive(true);
    }

    public float   GetCurrentPower()  => currentPower;
    // Negative = curl left, positive = curl right.
    public float   GetCurlAmount()    => curlAmount;
    public Vector3 GetAimDirection()  => Quaternion.Euler(0f, aimAngle, 0f) * Vector3.forward;
}
