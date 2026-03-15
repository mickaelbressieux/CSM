using UnityEngine;

/// <summary>
/// Controls the curling stone behaviour, including pre-shot spinning based on curl direction.
/// Before the stone is shot, adjusting <see cref="CurlAmount"/> causes the stone to spin on
/// itself: a positive (right) curl produces clockwise spin and a negative (left) curl produces
/// counter-clockwise spin when viewed from above.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class StoneController : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Maximum angular speed (radians per second) applied to the stone at full curl.")]
    private float maxSpinSpeed = 5f;

    private Rigidbody _rigidbody;
    private float _curlAmount = 0f;
    private bool _hasBeenShot = false;
    private bool _spinDirty = false;

    /// <summary>
    /// Gets or sets the curl amount in the range [−1, 1].
    /// Negative values represent left curl; positive values represent right curl.
    /// When changed before shooting, the stone's spin is updated on the next physics step.
    /// </summary>
    public float CurlAmount
    {
        get => _curlAmount;
        set
        {
            if (_hasBeenShot)
            {
                Debug.LogWarning("CurlAmount cannot be changed after the stone has been shot.");
                return;
            }

            _curlAmount = Mathf.Clamp(value, -1f, 1f);
            _spinDirty = true;
        }
    }

    /// <summary>Gets a value indicating whether the stone has already been shot.</summary>
    public bool HasBeenShot => _hasBeenShot;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (_spinDirty && !_hasBeenShot)
        {
            _rigidbody.angularVelocity = new Vector3(0f, _curlAmount * maxSpinSpeed, 0f);
            _spinDirty = false;
        }
    }

    /// <summary>
    /// Launches the stone in the specified direction with the given impulse force.
    /// Once shot, changes to <see cref="CurlAmount"/> no longer affect the stone's spin.
    /// </summary>
    /// <param name="direction">World-space direction in which to shoot the stone.</param>
    /// <param name="force">Non-negative magnitude of the impulse force to apply.</param>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// Thrown when <paramref name="force"/> is negative.
    /// </exception>
    public void Shoot(Vector3 direction, float force)
    {
        if (force < 0f)
        {
            throw new System.ArgumentOutOfRangeException(nameof(force), "Force must be non-negative.");
        }

        _hasBeenShot = true;
        _spinDirty = false;
        _rigidbody.AddForce(direction.normalized * force, ForceMode.Impulse);
    }
}
