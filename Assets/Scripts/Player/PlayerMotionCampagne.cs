using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

public class PlayerMotionCampagne : MonoBehaviour
{
    // Speed at which the object moves (units per second)
    public float moveSpeed = 5f;
    // Distance to consider we've reached the destination
    public float stoppingDistance = 0.1f;

    // Layer mask to use for ground raycasts (set to the ground layer in the Inspector)
    public LayerMask groundLayer = ~0; // default: everything
    // Debug draw the target
    public bool debugDrawTarget = true;

    // Internal state
    Vector3 targetPosition;
    bool moving = false;

    // Min and max bounds for the XZ plane movement
    public float minX = -50f;
    public float maxX = 50f;
    public float minZ = -50f;
    public float maxZ = 50f;

    // Rotation speed in degrees per second when turning to face movement direction
    public float rotationSpeed = 720f;
    // If the model's forward is inverted, set this to true to rotate 180° when facing movement
    public bool invertFacing = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // New Input System
        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("PlayerMotionCampagne: No main camera found for raycasting.");
            }
            else
            {
                Vector2 mp = mouse.position.ReadValue();
                Ray ray = cam.ScreenPointToRay(mp);
                SetTargetFromRay(ray);
            }
        }

        if (moving)
        {
            // Ensure target stays on the same Y as the object so movement is constrained to XZ plane
            targetPosition.y = transform.position.y;

            // Compute horizontal direction and rotate towards it
            Vector3 dir = targetPosition - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                if (invertFacing)
                    targetRot *= Quaternion.Euler(0f, 180f, 0f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            if (Vector3.Distance(transform.position, targetPosition) <= stoppingDistance)
            {
                moving = false;
            }
        }
#else
        // Legacy Input Manager
        if (Input.GetMouseButtonDown(1)) // 1 = right mouse button
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("PlayerMotionCampagne: No main camera found for raycasting.");
            }
            else
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                SetTargetFromRay(ray);
            
            }
        }

        if (moving)
        {
            // Ensure target stays on the same Y as the object so movement is constrained to XZ plane
            targetPosition.y = transform.position.y;

            // Compute horizontal direction and rotate towards it
            Vector3 dir = targetPosition - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                if (invertFacing)
                    targetRot *= Quaternion.Euler(0f, 180f, 0f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            if (Vector3.Distance(transform.position, targetPosition) <= stoppingDistance)
            {
                moving = false;
            }
        }
#endif
    }

    // Try to set target from a ray: prefer Physics.Raycast against groundLayer, fallback to intersection with horizontal plane at object's Y
    void SetTargetFromRay(Ray ray)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer.value))
        {
            // Ignore hits against this object's own collider to prevent selecting a point on the player
            Collider selfCol = GetComponent<Collider>();
            if (selfCol != null && hit.collider == selfCol)
            {
                // fallback to plane intersection below
            }
            else
            {
                targetPosition = hit.point;
                // force target onto same Y as the object so movement stays on XZ plane
                targetPosition.y = transform.position.y;

                // clamp to movement bounds
                targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
                targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);

                moving = true;
                return;
            }
        }

        // Fallback: intersect with horizontal plane at object's Y
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
        if (groundPlane.Raycast(ray, out float enter))
        {
            targetPosition = ray.GetPoint(enter);
            // force target onto same Y as the object so movement stays on XZ plane
            targetPosition.y = transform.position.y;

            // clamp to movement bounds
            targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);

            moving = true;
        }
    }

    void OnDrawGizmos()
    {
        if (!debugDrawTarget) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(targetPosition, 0.25f);
        if (moving)
        {
            Gizmos.DrawLine(transform.position, targetPosition);
            // Draw forward marker
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(targetPosition, targetPosition + Vector3.up * 0.5f);
        }
    }
}
