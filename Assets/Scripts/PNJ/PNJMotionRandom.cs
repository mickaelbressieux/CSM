using System.Collections;
using UnityEngine;

public class PNJMotionRandom : MonoBehaviour
{
    // Radius of the circle within which to pick random targets
    public float radius = 5f;
    // Movement speed in units per second
    public float moveSpeed = 3f;
    // Wait time between moves (random between min and max)
    [Tooltip("Minimum wait time in seconds")]
    public float waitTimeMin = 1f;
    [Tooltip("Maximum wait time in seconds")]
    public float waitTimeMax = 3f;
    // If true, movement constrained to XZ plane (same Y as this object)
    public bool constrainToXZ = true;
    // Rotation speed to face the movement direction
    public float rotationSpeed = 720f;
    // Minimum distance from current position for a new target to avoid jitter (in units)
    public float minTargetDistance = 0.5f;
    // Distance threshold to consider arrived
    public float arrivalThreshold = 0.05f;
    // Use this transform as center of circle; if null, use initial position
    public Transform centerTransform;
    // Draw gizmos
    public bool debugDraw = true;

    Vector3 centerPos;
    Vector3 targetPos;
    bool moving = false;
    float waitTimer = 0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        centerPos = centerTransform != null ? centerTransform.position : transform.position;
        // ensure sensible wait times in seconds
        waitTimeMin = Mathf.Max(0f, waitTimeMin);
        waitTimeMax = Mathf.Max(waitTimeMin, waitTimeMax);
        ChooseNewTarget();
    }

    // Update is called once per frame
    void Update()
    {
        // Update center if transform provided
        if (centerTransform != null) centerPos = centerTransform.position;

        if (moving)
        {
            // Ensure target Y matches if constraining
            if (constrainToXZ) targetPos.y = transform.position.y;

            // Rotate towards movement direction
            Vector3 dir = targetPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            // Move
            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, targetPos) <= arrivalThreshold)
            {
                moving = false;
                waitTimer = Random.Range(waitTimeMin, waitTimeMax);
            }
        }
        else
        {
            // Waiting
            if (waitTimer > 0f)
            {
                waitTimer -= Time.deltaTime;
                if (waitTimer <= 0f) ChooseNewTarget();
            }
            else
            {
                // If wait times are zero, immediately choose new target
                ChooseNewTarget();
            }
        }
    }

    void ChooseNewTarget()
    {
        // pick random point in unit circle then scale by radius
        Vector3 candidate = transform.position;
        int attempts = 0;
        while (attempts < 10)
        {
            Vector2 rnd = Random.insideUnitCircle * radius;
            candidate = centerPos + new Vector3(rnd.x, 0f, rnd.y);
            if (constrainToXZ) candidate.y = transform.position.y;
            if (Vector3.Distance(candidate, transform.position) >= minTargetDistance)
                break;
            attempts++;
        }
        // set target and start moving
        targetPos = candidate;
        moving = true;
    }

    void OnDrawGizmosSelected()
    {
        if (!debugDraw) return;
        // draw circle
        Gizmos.color = Color.yellow;
        Vector3 c = (centerTransform != null) ? centerTransform.position : (Application.isPlaying ? centerPos : transform.position);
        // draw approximate circle using segments
        int seg = 36;
        Vector3 prev = c + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float a = (i / (float)seg) * Mathf.PI * 2f;
            Vector3 next = c + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        // draw target if valid
        Gizmos.color = Color.cyan;
        if (Application.isPlaying)
        {
            Gizmos.DrawWireSphere(targetPos, 0.15f);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, targetPos);
        }
    }

    void OnValidate()
    {
        // ensure sensible values
        waitTimeMin = Mathf.Max(0f, waitTimeMin);
        waitTimeMax = Mathf.Max(waitTimeMin, waitTimeMax);
        radius = Mathf.Max(0f, radius);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        minTargetDistance = Mathf.Max(0f, minTargetDistance);
        arrivalThreshold = Mathf.Max(0f, arrivalThreshold);
    }
}
