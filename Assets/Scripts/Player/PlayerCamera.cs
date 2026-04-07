using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

public class PlayerCamera : MonoBehaviour
{
    // Speed of camera movement in units per second
    public float panSpeed = 15f;
    // Size in pixels of the screen edge that triggers mouse panning
    public int edgeSize = 10;
    // Movement limits
    public float minX = -50f;
    public float maxX = 50f;
    public float minZ = -50f;
    public float maxZ = 50f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 move = Vector3.zero;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // New Input System: explicitly map arrow/WASD to X and Z axes
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            // Horizontal: left/right -> X axis
            if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed || keyboard.qKey.isPressed) move.x -= 1f;
            if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) move.x += 1f;

            // Vertical: up/down -> Z axis
            if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed || keyboard.zKey.isPressed) move.z += 1f;
            if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) move.z -= 1f;
        }

        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 mp = mouse.position.ReadValue();
            if (mp.x <= edgeSize)
                move += Vector3.left;
            else if (mp.x >= Screen.width - edgeSize)
                move += Vector3.right;

            if (mp.y <= edgeSize)
                move += Vector3.back;
            else if (mp.y >= Screen.height - edgeSize)
                move += Vector3.forward;
        }
#else
        // Legacy Input Manager: explicit key checks so right/left only affect X
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q)) move.x -= 1f;
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) move.x += 1f;

        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Z)) move.z += 1f;
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) move.z -= 1f;

        Vector3 mouse = Input.mousePosition;
        if (mouse.x <= edgeSize)
            move += Vector3.left;
        else if (mouse.x >= Screen.width - edgeSize)
            move += Vector3.right;

        if (mouse.y <= edgeSize)
            move += Vector3.back;
        else if (mouse.y >= Screen.height - edgeSize)
            move += Vector3.forward;
#endif

        // Normalize to avoid faster diagonal movement
        if (move.sqrMagnitude > 1f)
            move.Normalize();

        // Apply movement in world XZ plane relative to world axes
        Vector3 newPos = transform.position + move * panSpeed * Time.deltaTime;

        // Clamp to limits
        newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
        newPos.z = Mathf.Clamp(newPos.z, minZ, maxZ);

        transform.position = newPos;
    }
}
