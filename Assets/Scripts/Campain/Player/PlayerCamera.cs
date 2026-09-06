using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

public class PlayerCamera : MonoBehaviour
{
    [Header("Suivi")]
    [Tooltip("Objet suivi par la camera. Si vide, le parent est utilise, puis l'objet portant PlayerMotionCampagne.")]
    [SerializeField] private Transform followedObject;

    // Speed of camera movement in units per second
    public float panSpeed = 15f;
    [Header("Rotation")]
    [Tooltip("Vitesse de rotation avec les touches A et E, en degres par seconde.")]
    [SerializeField] float cameraRotationSpeed = 90f;
    [Tooltip("Rotation en degres par pixel lorsque la molette est maintenue.")]
    [SerializeField] float mouseRotationSensitivity = 0.2f;
    // Size in pixels of the screen edge that triggers mouse panning
    public int edgeSize = 10;
    // Movement limits
    public float minX = -50f;
    public float maxX = 50f;
    public float minZ = -50f;
    public float maxZ = 50f;

    private Vector3 followOffset;
    private Vector3 manualPanOffset;
    private bool followOffsetInitialized;
    private Quaternion fixedRotation;
    private bool controlsEnabled = true;

    public bool ControlsEnabled => controlsEnabled;

    public void SetControlsEnabled(bool enabled)
    {
        controlsEnabled = enabled;
    }

    /// <summary>
    /// Remplace les limites de deplacement puis replace immediatement la camera dans la nouvelle
    /// zone. Le decalage manuel est recalcule pour que le panoramique reste reactif si la zone
    /// vient d'etre reduite.
    /// </summary>
    public void SetPositionLimits(float newMinX, float newMaxX, float newMinZ, float newMaxZ)
    {
        minX = Mathf.Min(newMinX, newMaxX);
        maxX = Mathf.Max(newMinX, newMaxX);
        minZ = Mathf.Min(newMinZ, newMaxZ);
        maxZ = Mathf.Max(newMinZ, newMaxZ);

        Vector3 clampedPosition = transform.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, minX, maxX);
        clampedPosition.z = Mathf.Clamp(clampedPosition.z, minZ, maxZ);
        transform.position = clampedPosition;

        if (followedObject != null && followOffsetInitialized)
            manualPanOffset = clampedPosition - followedObject.position - followOffset;
    }

    void Awake()
    {
        fixedRotation = transform.rotation;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ResolveFollowedObject();
    }

    // LateUpdate keeps the camera synchronized after the followed object has moved.
    void LateUpdate()
    {
        ResolveFollowedObject();

        if (!controlsEnabled)
            return;

        Vector3 move = Vector3.zero;
        bool recenterRequested = false;
        float keyboardRotation = 0f;
        float mouseRotation = 0f;
        bool orbitingWithMouse = false;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // New Input System: explicitly map arrow/WASD to X and Z axes
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            // Recherche par caractere affiche pour respecter AZERTY et QWERTY.
            var rotateLeftKey = keyboard.FindKeyOnCurrentKeyboardLayout("a");
            var rotateRightKey = keyboard.FindKeyOnCurrentKeyboardLayout("e");
            var panLeftKey = keyboard.FindKeyOnCurrentKeyboardLayout("q");

            // A/E font tourner la camera. Q/D et les fleches conservent le panoramique.
            if (rotateLeftKey != null && rotateLeftKey.isPressed) keyboardRotation -= 1f;
            if (rotateRightKey != null && rotateRightKey.isPressed) keyboardRotation += 1f;
            if (keyboard.leftArrowKey.isPressed || (panLeftKey != null && panLeftKey.isPressed)) move.x -= 1f;
            if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) move.x += 1f;

            // Vertical: up/down -> Z axis
            if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed || keyboard.zKey.isPressed) move.z += 1f;
            if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) move.z -= 1f;

            recenterRequested = keyboard.spaceKey.wasPressedThisFrame;
        }

        var mouse = Mouse.current;
        if (mouse != null)
        {
            orbitingWithMouse = mouse.middleButton.isPressed;
            if (orbitingWithMouse)
                mouseRotation = mouse.delta.ReadValue().x * mouseRotationSensitivity;

            Vector2 mp = mouse.position.ReadValue();
            if (!orbitingWithMouse && mp.x <= edgeSize)
                move += Vector3.left;
            else if (!orbitingWithMouse && mp.x >= Screen.width - edgeSize)
                move += Vector3.right;

            if (!orbitingWithMouse && mp.y <= edgeSize)
                move += Vector3.back;
            else if (!orbitingWithMouse && mp.y >= Screen.height - edgeSize)
                move += Vector3.forward;
        }
#else
        // A/E font tourner la camera. Q/D et les fleches conservent le panoramique.
        if (Input.GetKey(KeyCode.A)) keyboardRotation -= 1f;
        if (Input.GetKey(KeyCode.E)) keyboardRotation += 1f;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.Q)) move.x -= 1f;
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) move.x += 1f;

        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Z)) move.z += 1f;
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) move.z -= 1f;

        recenterRequested = Input.GetKeyDown(KeyCode.Space);

        orbitingWithMouse = Input.GetMouseButton(2);
        if (orbitingWithMouse)
            mouseRotation = Input.GetAxisRaw("Mouse X") * mouseRotationSensitivity * 20f;

        Vector3 mouse = Input.mousePosition;
        if (!orbitingWithMouse && mouse.x <= edgeSize)
            move += Vector3.left;
        else if (!orbitingWithMouse && mouse.x >= Screen.width - edgeSize)
            move += Vector3.right;

        if (!orbitingWithMouse && mouse.y <= edgeSize)
            move += Vector3.back;
        else if (!orbitingWithMouse && mouse.y >= Screen.height - edgeSize)
            move += Vector3.forward;
#endif

        float yawDelta = keyboardRotation * cameraRotationSpeed * Time.deltaTime + mouseRotation;
        if (Mathf.Abs(yawDelta) > 0.0001f)
            RotateCamera(yawDelta);

        // Normalize to avoid faster diagonal movement
        if (move.sqrMagnitude > 1f)
            move.Normalize();

        if (recenterRequested)
            manualPanOffset = Vector3.zero;

        Vector3 newPos;
        if (followedObject != null && followOffsetInitialized)
        {
            if (!recenterRequested)
                manualPanOffset += move * panSpeed * Time.deltaTime;

            newPos = followedObject.position + followOffset + manualPanOffset;
        }
        else
        {
            // Keep the original free-camera behaviour when no object can be found.
            newPos = transform.position + move * panSpeed * Time.deltaTime;
        }

        // Clamp to limits
        newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
        newPos.z = Mathf.Clamp(newPos.z, minZ, maxZ);

        transform.position = newPos;
        transform.rotation = fixedRotation;
    }

    private void RotateCamera(float yawDelta)
    {
        Quaternion yawRotation = Quaternion.AngleAxis(yawDelta, Vector3.up);
        fixedRotation = yawRotation * fixedRotation;

        if (!followOffsetInitialized)
            return;

        followOffset = yawRotation * followOffset;
        manualPanOffset = yawRotation * manualPanOffset;
    }

    private void ResolveFollowedObject()
    {
        if (followedObject != null)
        {
            InitializeFollowOffset();
            return;
        }

        if (transform.parent != null)
        {
            followedObject = transform.parent;
        }
        else
        {
            PlayerMotionCampagne playerMotion = FindFirstObjectByType<PlayerMotionCampagne>();
            if (playerMotion != null)
                followedObject = playerMotion.transform;
        }

        InitializeFollowOffset();
    }

    private void InitializeFollowOffset()
    {
        if (followOffsetInitialized || followedObject == null)
            return;

        followOffset = transform.position - followedObject.position;
        manualPanOffset = Vector3.zero;
        followOffsetInitialized = true;
    }
}
