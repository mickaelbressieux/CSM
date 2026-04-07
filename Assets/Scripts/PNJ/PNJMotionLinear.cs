using UnityEngine;

public class PNJMotionLinear : MonoBehaviour
{
    // Waypoints are read automatically from direct children of this object.
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private bool freezeWaypointsOnStart = true;
    public float moveSpeed = 3f;
    public bool loop = true;
    public bool pingPong = false;
    public float waitTimeAtPoint = 0.5f;
    public bool constrainToXZ = true; // keep movement on same Y as this object
    public float rotationSpeed = 720f; // degrees per second

    int currentIndex = 0;
    bool waiting = false;
    float waitTimer = 0f;
    int direction = 1; // 1 = forward, -1 = backward (for ping-pong)
    private Vector3[] waypointPositions = System.Array.Empty<Vector3>();

    private void OnValidate()
    {
        RefreshWaypointsFromChildren();
    }

    private void OnTransformChildrenChanged()
    {
        RefreshWaypointsFromChildren();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RefreshWaypointsFromChildren();
        RebuildWaypointPositions();

        // Clamp currentIndex
        if (waypointPositions == null || waypointPositions.Length == 0) return;
        currentIndex = Mathf.Clamp(currentIndex, 0, waypointPositions.Length - 1);

        // Optionally set initial position to first waypoint
        // transform.position = waypoints[currentIndex].position;
    }

    private void RefreshWaypointsFromChildren()
    {
        int count = transform.childCount;
        if (count <= 0)
        {
            waypoints = System.Array.Empty<Transform>();
            currentIndex = 0;
            return;
        }

        waypoints = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            waypoints[i] = transform.GetChild(i);
        }

        currentIndex = Mathf.Clamp(currentIndex, 0, waypoints.Length - 1);
    }

    private void RebuildWaypointPositions()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            waypointPositions = System.Array.Empty<Vector3>();
            return;
        }

        waypointPositions = new Vector3[waypoints.Length];
        for (int i = 0; i < waypoints.Length; i++)
        {
            Transform point = waypoints[i];
            if (point == null)
            {
                waypointPositions[i] = transform.position;
                continue;
            }

            waypointPositions[i] = point.position;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!freezeWaypointsOnStart)
        {
            RebuildWaypointPositions();
        }

        if (waypointPositions == null || waypointPositions.Length == 0) return;

        if (waiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f) waiting = false;
            else return;
        }

        Vector3 targetPos = waypointPositions[currentIndex];
        if (constrainToXZ) targetPos.y = transform.position.y;

        // Rotate towards movement direction
        Vector3 dir = (targetPos - transform.position);
        Vector3 dirFlat = dir;
        dirFlat.y = 0f;
        if (dirFlat.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dirFlat);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        // Move towards target
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPos) <= 0.01f)
        {
            // Arrived
            if (waitTimeAtPoint > 0f)
            {
                waiting = true;
                waitTimer = waitTimeAtPoint;
            }

            // Advance index
            if (pingPong)
            {
                if (direction == 1)
                {
                    if (currentIndex >= waypoints.Length - 1)
                    {
                        direction = -1;
                        currentIndex += direction;
                    }
                    else currentIndex += direction;
                }
                else
                {
                    if (currentIndex <= 0)
                    {
                        direction = 1;
                        currentIndex += direction;
                    }
                    else currentIndex += direction;
                }
            }
            else
            {
                currentIndex++;
                if (currentIndex >= waypointPositions.Length)
                {
                    if (loop) currentIndex = 0;
                    else currentIndex = waypointPositions.Length - 1; // stay at last
                }
            }
        }
    }

    void OnDrawGizmos()
    {
        if (Application.isPlaying && waypointPositions != null && waypointPositions.Length > 0)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypointPositions.Length; i++)
            {
                Vector3 p = waypointPositions[i];
                Gizmos.DrawWireSphere(p, 0.2f);
                if (i < waypointPositions.Length - 1) Gizmos.DrawLine(p, waypointPositions[i + 1]);
                if (loop && i == waypointPositions.Length - 1 && waypointPositions.Length > 1) Gizmos.DrawLine(p, waypointPositions[0]);
            }
            return;
        }

        if (transform.childCount > 0)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform t = transform.GetChild(i);
                if (t == null) continue;
                Gizmos.DrawWireSphere(t.position, 0.2f);
                if (i < transform.childCount - 1) Gizmos.DrawLine(t.position, transform.GetChild(i + 1).position);
                if (loop && i == transform.childCount - 1 && transform.childCount > 1) Gizmos.DrawLine(t.position, transform.GetChild(0).position);
            }
        }
    }
}
