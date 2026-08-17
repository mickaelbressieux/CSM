using UnityEngine;

/// <summary>
/// The "physics half" of a curling throw, split out of the old
/// <c>CurlingStoneController</c>. It listens to an <see cref="IShotProvider"/> for a
/// committed <see cref="ShotData"/> and executes it: applies the launch impulse and
/// pre-shot spin, simulates the curl during the slide, and detects when the stone stops.
///
/// It is source-agnostic — the same launcher works for the human player today and for an
/// AI provider on a later branch, because it only ever sees <see cref="ShotData"/>.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class StoneLauncher : MonoBehaviour
{
    [Header("Curl")]
    // Heading deflection per meter traveled per unit of curl.
    // Positive curl = curl RIGHT relative to the stone's direction of travel.
    public float curlDegreesPerMeter = 0.5f;

    [Header("Pre-shot Spin")]
    // rad/s of Y-axis spin per curl unit — clockwise (viewed from above) for positive curl.
    public float preShotSpinSpeed = 2f;

    [Header("Physics")]
    public float slideDrag = 0.001f;     // drag applied once the stone is shot
    public float stopThreshold = 0.05f;

    [Header("Shot Source")]
    // A component implementing IShotProvider (e.g. PlayerShotProvider). Serialized as a
    // MonoBehaviour so any provider can be wired in from the inspector without this
    // launcher naming a concrete type.
    public MonoBehaviour shotProviderSource;

    private Rigidbody rb;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private IShotProvider provider;

    // Stackable powers on this stone. Their lifecycle hooks (launch / slide / stop / collision) are
    // fired below on EVERY StoneAbility present, so multiple powers stack. Cached in OnEnable.
    private StoneAbility[] abilities = System.Array.Empty<StoneAbility>();
    private Stone stoneIdentity;

    /// <summary>The stone's Rigidbody, exposed so abilities can affect the shot in flight.</summary>
    public Rigidbody Body => rb;

    // The axis a ShotData.LateralOffset shifts the launch position along. World +X, because
    // the whole shot system reasons in world axes (PlayerShotProvider aims relative to
    // Vector3.forward), so the sheet runs along world +Z.
    // If the sheet is ever rotated in the scene, use startRotation * Vector3.right instead.
    private static Vector3 SheetRight => Vector3.right;

    // How an unshot stone is held in place. Only Y is frozen — X/Z are left free ON PURPOSE so
    // the lateral-offset preview can reposition the stone: the solver treats a frozen linear
    // axis as authoritative and reverts any rb.position write on it, which would silently undo
    // the preview. X/Z are instead pinned in code, by writing rb.position and zeroing the
    // velocity every FixedUpdate while unshot (see the pre-shot branch below).
    private const RigidbodyConstraints PreShotConstraints = RigidbodyConstraints.FreezePositionY;

    // The committed shot, stashed from ShotReady and applied on the next FixedUpdate so
    // the impulse is visible to the physics engine before the stop-check runs.
    private ShotData pendingShot;
    private bool shootPending = false;
    private float activeCurl = 0f; // curl of the shot currently in flight

    public bool HasBeenShot  { get; private set; } = false;
    public bool ShotFinished { get; private set; } = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        startRotation = transform.rotation;

        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.constraints     = PreShotConstraints;
    }

    private void OnEnable()
    {
        provider = shotProviderSource as IShotProvider;
        if (provider == null && shotProviderSource != null)
            Debug.LogError($"{nameof(StoneLauncher)}: assigned shotProviderSource does not implement IShotProvider.", this);

        if (provider != null)
            provider.ShotReady += OnShotReady;

        abilities = GetComponents<StoneAbility>();
        stoneIdentity = GetComponent<Stone>();
    }

    private void OnDisable()
    {
        if (provider != null)
            provider.ShotReady -= OnShotReady;
    }

    // Queue the shot; it is applied in the next FixedUpdate.
    private void OnShotReady(ShotData shot)
    {
        if (HasBeenShot)
            return;
        pendingShot  = shot;
        shootPending = true;
    }

    private void FixedUpdate()
    {
        // Apply the queued shot inside FixedUpdate so the impulse velocity is visible to
        // the physics engine before the stop-check runs.
        if (shootPending)
        {
            shootPending     = false;
            HasBeenShot      = true;
            activeCurl       = pendingShot.Curl;
            rb.constraints   = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.linearDamping = slideDrag;
            // Shift the launch position sideways before the impulse, so the trajectory is
            // parallel-translated rather than rotated. Re-applied here (not only in the
            // pre-shot preview below) because a provider may commit an offset it never
            // previewed — the AI, for instance, composes its whole shot at release time.
            rb.position      = startPosition + SheetRight * pendingShot.LateralOffset;
            rb.AddForce(pendingShot.Direction * pendingShot.Power, ForceMode.Impulse);
            // Set spin once at launch — positive curl spins clockwise (right curl).
            // Let angular damping decay it naturally; do NOT override each frame.
            rb.angularVelocity = new Vector3(0f, activeCurl * preShotSpinSpeed, 0f);
            stoneIdentity?.SetPhase(StonePhase.Sliding);
            for (int i = 0; i < abilities.Length; i++) abilities[i].OnLaunch(this);
            return; // skip stop-check this frame; velocity is updated after physics step
        }

        if (!HasBeenShot || ShotFinished)
        {
            if (!HasBeenShot)
            {
                ShotData preview   = provider != null ? provider.CurrentShot : default(ShotData);
                rb.linearVelocity  = Vector3.zero;
                // Spin stone for visual pre-shot feedback, driven by the live aim curl.
                rb.angularVelocity = new Vector3(0f, preview.Curl * preShotSpinSpeed, 0f);
                // Hard-pin the stone to its (offset) launch spot every step. This both holds it
                // still on the unfrozen X/Z axes and slides it sideways to the live lateral
                // offset, so the player sees where the throw will start from. The aim arrow
                // tracks the stone's transform, so it follows along on its own.
                rb.position        = startPosition + SheetRight * preview.LateralOffset;
            }
            return;
        }

        // Detect stop
        if (rb.linearVelocity.magnitude <= stopThreshold)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            ShotFinished = true;
            stoneIdentity?.SetPhase(StonePhase.Stopped);
            for (int i = 0; i < abilities.Length; i++) abilities[i].OnStopped(this);
            return;
        }

        // Still sliding this step: let stacked powers act on the moving stone (brake, boost, ...).
        for (int i = 0; i < abilities.Length; i++) abilities[i].OnSlideTick(this);

        // Curl: deflect the velocity heading by a small angle each physics step.
        // angleDeg > 0 → stone curves RIGHT relative to its direction of travel.
        // Speed is preserved (rotation keeps vector length constant).
        if (Mathf.Abs(activeCurl) > 0.001f)
        {
            float speed        = rb.linearVelocity.magnitude;
            float distThisStep = speed * Time.fixedDeltaTime;
            float angleDeg     = activeCurl * curlDegreesPerMeter * distThisStep;
            rb.linearVelocity  = Quaternion.Euler(0f, angleDeg, 0f) * rb.linearVelocity;
        }
    }

    /// <summary>
    /// Reset the stone to its start pose and re-arm the shot provider for a new throw.
    /// </summary>
    public void ResetStone()
    {
        HasBeenShot   = false;
        ShotFinished  = false;
        shootPending  = false;
        activeCurl    = 0f;

        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.linearDamping   = 0f;
        rb.constraints     = PreShotConstraints;

        transform.position = startPosition;
        transform.rotation = startRotation;

        stoneIdentity?.SetPhase(StonePhase.Idle);
        provider?.Rearm();
    }

    // Forward physics collisions to stacked powers (e.g. a "boost on hit" ability).
    private void OnCollisionEnter(Collision collision)
    {
        for (int i = 0; i < abilities.Length; i++)
            abilities[i].OnStoneCollision(this, collision);
    }
}
