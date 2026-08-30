using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// STOPPABLE STONE — the player can halt the stone mid-slide with a key press.
///
/// A limited resource: <see cref="usesPerThrow"/> brakes per throw (one by default), usable only
/// while the stone is actually sliding. Set <see cref="brakeDeceleration"/> above zero for a hard
/// skid instead of an instant dead stop.
///
/// <b>Why the key is read in Update, not in the slide hook.</b> <see cref="OnSlideTick"/> is fired
/// from FixedUpdate, which can run zero or several times per rendered frame — a
/// <c>wasPressedThisFrame</c> read there is missed or counted twice. So Update latches the press
/// and the physics step consumes the latch.
///
/// The brake does not talk to <see cref="StoneLauncher"/>: once the velocity is at (or heading for)
/// zero, the launcher's own stop-detection sees speed &lt;= stopThreshold on the next step and ends
/// the shot normally — Phase becomes Stopped, <see cref="StoneAbility.OnStopped"/> fires, and the
/// match manager's turn gate is untouched.
/// </summary>
public class StoppableStoneAbility : StoneAbility
{
    [Tooltip("Key that stops the stone mid-slide. Kept clear of the throw controls " +
             "(arrows = aim/power, Q/E = curl, A/D = offset, Space = shoot).")]
    public Key stopKey = Key.S;

    [Tooltip("How many times the stone can be braked during one throw.")]
    public int usesPerThrow = 1;

    [Tooltip("Deceleration in m/s^2 once braking. 0 = stop dead on the spot.")]
    public float brakeDeceleration = 0f;

    private int usesLeft;
    private bool brakeRequested;   // latched in Update, consumed in the physics step
    private bool braking;          // true while a gradual brake is still bleeding off speed
    private Stone stone;

    /// <summary>Only shown while the power can still be used on a stone that is moving.</summary>
    public override string HudHint =>
        usesLeft > 0 && stone != null && IsSliding ? $"Press {stopKey} to stop the stone" : null;

    // Latching a press only means something while the stone is in motion. A stone with no Stone
    // component has no phase to read, so stay permissive there: OnLaunch clears any early latch,
    // and OnSlideTick stops running once the stone settles, so a stray latch is harmless.
    private bool IsSliding => stone == null || stone.Phase == StonePhase.Sliding;

    private void Awake()
    {
        stone    = GetComponent<Stone>();
        usesLeft = usesPerThrow;
    }

    private void Update()
    {
        if (usesLeft <= 0 || !IsPlayerControlled() || !IsSliding)
            return;

        if (Keyboard.current != null && Keyboard.current[stopKey].wasPressedThisFrame)
            brakeRequested = true;
    }

    public override void OnLaunch(StoneLauncher launcher)
    {
        // Fresh throw: restore the charges and drop any press latched before release.
        usesLeft       = usesPerThrow;
        brakeRequested = false;
        braking        = false;
    }

    public override void OnSlideTick(StoneLauncher launcher)
    {
        if (brakeRequested)
        {
            brakeRequested = false;
            usesLeft--;
            braking = true;
        }

        if (!braking)
            return;

        Rigidbody rb = launcher.Body;

        if (brakeDeceleration <= 0f)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            braking = false;                     // the launcher ends the shot next step
            return;
        }

        float speed = rb.linearVelocity.magnitude;
        float newSpeed = speed - brakeDeceleration * Time.fixedDeltaTime;
        if (newSpeed <= 0f)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            braking = false;
            return;
        }

        rb.linearVelocity = rb.linearVelocity.normalized * newSpeed;
    }

    // Keyboard input must never drive an AI stone. Only the player has an inventory today, but a
    // power is just a component — this keeps it inert if it ever lands on the other side.
    private bool IsPlayerControlled() => stone == null || stone.Side != StoneSide.AI;
}
