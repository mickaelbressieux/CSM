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
    public float curlSideForce = 3f;  // lateral acceleration (m/s²) per curl unit

    [Header("Physics")]
    public float slideDrag = 0.4f;     // drag applied once the stone is shot
    public float stopThreshold = 0.05f;

    private Rigidbody rb;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private float aimAngle = 0f;
    private float currentPower;
    private float curlLeft  = 0f;
    private float curlRight = 0f;
    private bool  shootPending = false;

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
    }

    private void FixedUpdate()
    {
        // Apply the queued shot inside FixedUpdate so the impulse velocity
        // is visible to the physics engine before the stop-check runs.
        if (shootPending)
        {
            shootPending  = false;
            HasBeenShot   = true;
            rb.linearDamping = slideDrag;
            Vector3 dir = Quaternion.Euler(0f, aimAngle, 0f) * Vector3.forward;
            rb.AddForce(dir * currentPower, ForceMode.Impulse);
            return; // skip stop-check this frame; velocity is updated after physics step
        }

        if (!HasBeenShot || ShotFinished)
        {
            if (!HasBeenShot)
            {
                rb.linearVelocity  = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
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

        // Apply curl: constant lateral acceleration independent of mass
        float netCurl = curlRight - curlLeft;
        if (Mathf.Abs(netCurl) > 0.001f)
        {
            Vector3 lateralDir = Vector3.Cross(Vector3.up, rb.linearVelocity.normalized);
            rb.AddForce(lateralDir * netCurl * curlSideForce, ForceMode.Acceleration);
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

        transform.position = startPosition;
        transform.rotation = startRotation;

        aimAngle     = 0f;
        currentPower = (minPower + maxPower) / 2f;
        curlLeft     = 0f;
        curlRight    = 0f;
    }

    public float   GetCurrentPower()  => currentPower;
    public float   GetCurlLeft()      => curlLeft;
    public float   GetCurlRight()     => curlRight;
    public Vector3 GetAimDirection()  => Quaternion.Euler(0f, aimAngle, 0f) * Vector3.forward;
}
