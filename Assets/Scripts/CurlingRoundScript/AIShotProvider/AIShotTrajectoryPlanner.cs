using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIShotTarget
{
    Center,
    Player
}

/// <summary>
/// Builds a ShotData with a mathematical model of the CURRENT StoneLauncher physics.
/// No recorded trajectory log is loaded or consulted.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(StoneLauncher))]
public sealed class AIShotTrajectoryPlanner : MonoBehaviour
{
    private struct Candidate
    {
        public Vector3 Direction;
        public float Force;
        public float Curl;
        public float LateralOffset;
        public float Error;
    }

    [Header("Targets")]
    [SerializeField] private string centerTag = "Center";
    [SerializeField] private string playerTag = "Player";

    [Header("Obstacle detection")]
    [SerializeField] private LayerMask obstacleLayerMask = ~0;
    [SerializeField, Min(0f)] private float obstacleClearanceMargin = 0.15f;
    [SerializeField, Range(5, 50)] private int pathSegments = 20;

    [Header("Current physics approximation")]
    [Tooltip("Estimated constant slowdown caused by ice/contact friction, in m/s^2. "
        + "Tune this value against one real straight throw in the current scene.")]
    [SerializeField, Min(0.001f)] private float estimatedSlidingDeceleration = 5.5f;
    [SerializeField, Min(0.01f)] private float minimumForce = 1f;
    [SerializeField, Min(0.01f)] private float maximumForce = 150f;
    [SerializeField, Min(1f)] private float maximumPredictedDistance = 500f;

    [Header("Trajectory search")]
    [SerializeField, Min(0.01f)] private float acceptableTargetDistance = 0.75f;
    [SerializeField, Min(0.1f)] private float maximumAngle = 35f;
    [SerializeField, Min(0.1f)] private float angleStep = 1f;
    [SerializeField, Min(0.01f)] private float maximumCurl = 1.7f;
    [SerializeField, Min(0.01f)] private float curlStep = 0.1f;
    [SerializeField, Min(0f)] private float maximumLateralOffset = 4f;
    [SerializeField, Range(8, 32)] private int forceSearchIterations = 16;

    public float MaxCurl => Mathf.Max(maximumCurl, 0.01f);
    public float MaxLateral => Mathf.Max(maximumLateralOffset, 0.01f);
    public GameObject LastTarget { get; private set; }
    public float LastPredictedDistance { get; private set; } = -1f;
    public bool LastShotWasFallback { get; private set; }

    private readonly Collider[] obstacleHits = new Collider[64];
    private Collider[] ownColliders = Array.Empty<Collider>();
    private Rigidbody body;
    private StoneLauncher launcher;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        launcher = GetComponent<StoneLauncher>();
        ownColliders = GetComponentsInChildren<Collider>(true);
    }

    private void OnValidate()
    {
        obstacleClearanceMargin = Mathf.Max(0f, obstacleClearanceMargin);
        pathSegments = Mathf.Clamp(pathSegments, 5, 50);
        estimatedSlidingDeceleration = Mathf.Max(0.001f, estimatedSlidingDeceleration);
        minimumForce = Mathf.Max(0.01f, minimumForce);
        maximumForce = Mathf.Max(minimumForce, maximumForce);
        maximumPredictedDistance = Mathf.Max(1f, maximumPredictedDistance);
        acceptableTargetDistance = Mathf.Max(0.01f, acceptableTargetDistance);
        maximumAngle = Mathf.Max(0.1f, maximumAngle);
        angleStep = Mathf.Max(0.1f, angleStep);
        maximumCurl = Mathf.Max(0.01f, maximumCurl);
        curlStep = Mathf.Max(0.01f, curlStep);
        maximumLateralOffset = Mathf.Max(0f, maximumLateralOffset);
    }

    public bool TryBuildShot(AIShotTarget targetChoice, Transform centerOverride, out ShotData shot)
    {
        shot = default;
        GameObject target = null;

        if (targetChoice == AIShotTarget.Center && centerOverride != null)
            target = centerOverride.gameObject;
        else
            TryFindNearestWithTag(targetChoice == AIShotTarget.Center ? centerTag : playerTag, out target);

        return TryBuildShot(target, out shot);
    }

    public bool TryBuildShot(GameObject target, out ShotData shot)
    {
        shot = default;
        LastTarget = target;
        LastPredictedDistance = -1f;
        LastShotWasFallback = false;

        if (target == null)
        {
            Debug.LogWarning("AI trajectory: no valid target was supplied.", this);
            return false;
        }

        EnsurePhysicsReferences();
        if (body == null || launcher == null)
        {
            Debug.LogError("AI trajectory requires a Rigidbody and StoneLauncher.", this);
            return false;
        }

        Vector3 targetPosition = target.transform.position;
        targetPosition.y = transform.position.y;
        Candidate bestClearCandidate = default;
        bool hasClearCandidate = false;

        foreach (float lateralOffset in BuildOffsetAttempts(targetPosition))
        {
            Vector3 virtualStart = transform.position + Vector3.right * lateralOffset;
            Vector3 toTarget = targetPosition - virtualStart;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.0001f)
                continue;

            Vector3 baseDirection = toTarget.normalized;
            float baseYaw = Mathf.Atan2(baseDirection.x, baseDirection.z);
            Candidate direct = BuildCandidate(virtualStart, targetPosition, baseDirection,
                baseYaw, 0f, 0f, lateralOffset);

            if (!IsPathObstructed(virtualStart, direct, target))
            {
                shot = ToShotData(direct);
                LastPredictedDistance = direct.Error;
                return true;
            }

            int angleSteps = Mathf.CeilToInt(maximumAngle / angleStep);
            int curlSteps = Mathf.CeilToInt(maximumCurl / curlStep);
            int lastShell = angleSteps + curlSteps;

            for (int shell = 1; shell <= lastShell; shell++)
            {
                int firstAngleIndex = Mathf.Max(0, shell - curlSteps);
                int lastAngleIndex = Mathf.Min(angleSteps, shell);

                for (int angleIndex = firstAngleIndex; angleIndex <= lastAngleIndex; angleIndex++)
                {
                    int curlIndex = shell - angleIndex;
                    float angleMagnitude = angleIndex * angleStep;
                    float curlMagnitude = curlIndex * curlStep;

                    foreach (Vector2 pair in CompatibleAngleCurlPairs(angleMagnitude, curlMagnitude))
                    {
                        Candidate candidate = BuildCandidate(virtualStart, targetPosition,
                            baseDirection, baseYaw, pair.x, pair.y, lateralOffset);

                        if (IsPathObstructed(virtualStart, candidate, target))
                            continue;

                        if (!hasClearCandidate || candidate.Error < bestClearCandidate.Error)
                        {
                            hasClearCandidate = true;
                            bestClearCandidate = candidate;
                        }

                        if (candidate.Error <= acceptableTargetDistance)
                        {
                            shot = ToShotData(candidate);
                            LastPredictedDistance = candidate.Error;
                            return true;
                        }
                    }
                }
            }
        }

        if (hasClearCandidate)
        {
            shot = ToShotData(bestClearCandidate);
            LastPredictedDistance = bestClearCandidate.Error;
            LastShotWasFallback = true;
            Debug.LogWarning("AI trajectory: using closest collision-free fallback (error "
                + bestClearCandidate.Error.ToString("F2") + " m).", this);
            return true;
        }

        Debug.LogWarning("AI trajectory: no collision-free candidate was found.", this);
        return false;
    }

    public bool TryFindNearestTarget(AIShotTarget targetChoice, out GameObject target)
    {
        return TryFindNearestWithTag(targetChoice == AIShotTarget.Center ? centerTag : playerTag, out target);
    }

    public GameObject FindPlayerClosestTo(Vector3 position, float maximumDistance)
    {
        if (!TryFindAllWithTag(playerTag, out GameObject[] players))
            return null;

        GameObject closest = null;
        float bestSqr = maximumDistance * maximumDistance;
        foreach (GameObject player in players)
        {
            if (player == null || player == gameObject)
                continue;

            Vector3 delta = player.transform.position - position;
            delta.y = 0f;
            float sqr = delta.sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                closest = player;
            }
        }
        return closest;
    }

    private void EnsurePhysicsReferences()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (launcher == null)
            launcher = GetComponent<StoneLauncher>();
    }

    private List<float> BuildOffsetAttempts(Vector3 targetPosition)
    {
        var attempts = new List<float> { 0f };
        float alignedOffset = Mathf.Clamp(targetPosition.x - transform.position.x,
            -maximumLateralOffset, maximumLateralOffset);
        AddUnique(attempts, alignedOffset);
        AddUnique(attempts, alignedOffset * 0.5f);
        return attempts;
    }

    private static void AddUnique(List<float> values, float value)
    {
        foreach (float existing in values)
            if (Mathf.Abs(existing - value) <= 0.001f)
                return;
        values.Add(value);
    }

    private Candidate BuildCandidate(Vector3 start, Vector3 target, Vector3 baseDirection,
        float baseYaw, float angle, float curl, float lateralOffset)
    {
        float yaw = baseYaw + angle * Mathf.Deg2Rad;
        float force = FindBestForce(start, target, yaw, curl, out Vector3 stop);
        return new Candidate
        {
            Direction = Quaternion.Euler(0f, angle, 0f) * baseDirection,
            Force = force,
            Curl = curl,
            LateralOffset = lateralOffset,
            Error = FlatDistance(stop, target)
        };
    }

    private float FindBestForce(Vector3 start, Vector3 target, float yaw, float curl,
        out Vector3 bestStop)
    {
        float low = minimumForce;
        float high = maximumForce;
        float bestForce = low;
        bestStop = PredictWorldStop(start, low, curl, yaw);
        float bestError = FlatDistance(bestStop, target);

        for (int i = 0; i < forceSearchIterations; i++)
        {
            float left = Mathf.Lerp(low, high, 1f / 3f);
            float right = Mathf.Lerp(low, high, 2f / 3f);
            Vector3 leftStop = PredictWorldStop(start, left, curl, yaw);
            Vector3 rightStop = PredictWorldStop(start, right, curl, yaw);
            float leftError = FlatDistance(leftStop, target);
            float rightError = FlatDistance(rightStop, target);

            if (leftError < bestError)
            {
                bestError = leftError;
                bestForce = left;
                bestStop = leftStop;
            }
            if (rightError < bestError)
            {
                bestError = rightError;
                bestForce = right;
                bestStop = rightStop;
            }

            if (leftError <= rightError)
                high = right;
            else
                low = left;
        }
        return bestForce;
    }

    private Vector3 PredictWorldStop(Vector3 start, float force, float curl, float yaw)
    {
        return PredictPointAtDistance(start, PredictTravelDistance(force), curl, yaw);
    }

    private float PredictTravelDistance(float force)
    {
        float mass = Mathf.Max(body != null ? body.mass : 1f, 0.001f);
        double initialSpeed = Math.Max(force / mass, 0.0);
        double stopSpeed = Math.Max(launcher != null ? launcher.stopThreshold : 0.05f, 0.0);
        if (initialSpeed <= stopSpeed)
            return 0f;

        double deceleration = Math.Max(estimatedSlidingDeceleration, 0.001f);
        double damping = Math.Max(launcher != null ? launcher.slideDrag : 0.0f, 0.0);
        double distance;

        // Continuous approximation of dv/dt = -deceleration - damping * v.
        if (damping <= 0.000001)
        {
            distance = (initialSpeed * initialSpeed - stopSpeed * stopSpeed)
                / (2.0 * deceleration);
        }
        else
        {
            distance = (initialSpeed - stopSpeed) / damping
                - (deceleration / (damping * damping))
                * Math.Log((deceleration + damping * initialSpeed)
                    / (deceleration + damping * stopSpeed));
        }

        return Mathf.Clamp((float)distance, 0f, maximumPredictedDistance);
    }

    private Vector3 PredictPointAtDistance(Vector3 start, float distance, float curl, float yaw)
    {
        float curlDegreesPerMeter = launcher != null ? launcher.curlDegreesPerMeter : 0.5f;
        float curvature = curl * curlDegreesPerMeter * Mathf.Deg2Rad;
        Vector2 local;

        if (Mathf.Abs(curvature) <= 0.000001f)
        {
            local = new Vector2(0f, distance);
        }
        else
        {
            float arcAngle = curvature * distance;
            local = new Vector2(
                (1f - Mathf.Cos(arcAngle)) / curvature,
                Mathf.Sin(arcAngle) / curvature);
        }
        return LocalToWorld(start, local, yaw);
    }

    private bool IsPathObstructed(Vector3 start, Candidate candidate, GameObject target)
    {
        float yaw = Mathf.Atan2(candidate.Direction.x, candidate.Direction.z);
        float totalDistance = PredictTravelDistance(candidate.Force);
        float radius = GetStoneRadius() + obstacleClearanceMargin;
        Vector3 previous = start;

        for (int i = 1; i <= pathSegments; i++)
        {
            float distance = totalDistance * i / pathSegments;
            Vector3 next = PredictPointAtDistance(start, distance, candidate.Curl, yaw);
            int hitCount = Physics.OverlapCapsuleNonAlloc(previous, next, radius, obstacleHits,
                obstacleLayerMask, QueryTriggerInteraction.Ignore);

            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                if (IsBlockingCollider(obstacleHits[hitIndex], start.y, target))
                    return true;

            previous = next;
        }
        return false;
    }

    private bool IsBlockingCollider(Collider hit, float stoneY, GameObject target)
    {
        if (hit == null)
            return false;
        if (target != null && hit.transform.IsChildOf(target.transform))
            return false;
        if (hit.bounds.max.y <= stoneY + 0.05f)
            return false;

        foreach (Collider ownCollider in ownColliders)
            if (hit == ownCollider)
                return false;

        string layerName = LayerMask.LayerToName(hit.gameObject.layer);
        return !string.Equals(layerName, "ground", StringComparison.OrdinalIgnoreCase);
    }

    private static ShotData ToShotData(Candidate candidate)
    {
        // This model uses StoneLauncher's convention directly: positive curls right.
        return new ShotData(candidate.Direction, candidate.Force,
            candidate.Curl, candidate.LateralOffset);
    }

    private static IEnumerable<float> SignedValues(float magnitude)
    {
        if (magnitude <= 0.0001f)
        {
            yield return 0f;
            yield break;
        }
        yield return magnitude;
        yield return -magnitude;
    }

    private static IEnumerable<Vector2> CompatibleAngleCurlPairs(
        float angleMagnitude, float curlMagnitude)
    {
        if (curlMagnitude <= 0.0001f)
        {
            foreach (float angle in SignedValues(angleMagnitude))
                yield return new Vector2(angle, 0f);
            yield break;
        }

        if (angleMagnitude <= 0.0001f)
            yield break;

        // Current StoneLauncher convention, with no log-sign conversion.
        yield return new Vector2(angleMagnitude, -curlMagnitude);
        yield return new Vector2(-angleMagnitude, curlMagnitude);
    }

    private static Vector3 LocalToWorld(Vector3 start, Vector2 local, float yaw)
    {
        float cos = Mathf.Cos(yaw);
        float sin = Mathf.Sin(yaw);
        float x = local.x * cos + local.y * sin;
        float z = -local.x * sin + local.y * cos;
        return new Vector3(start.x + x, start.y, start.z + z);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private float GetStoneRadius()
    {
        float radius = 0.15f;
        foreach (Collider collider in ownColliders)
        {
            if (collider == null)
                continue;
            radius = Mathf.Max(radius, collider.bounds.extents.x, collider.bounds.extents.z);
        }
        return radius;
    }

    private bool TryFindNearestWithTag(string tagName, out GameObject nearest)
    {
        nearest = null;
        if (!TryFindAllWithTag(tagName, out GameObject[] candidates))
            return false;

        float bestSqr = float.MaxValue;
        foreach (GameObject candidate in candidates)
        {
            if (candidate == null || candidate == gameObject)
                continue;
            Vector3 delta = candidate.transform.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < bestSqr)
            {
                bestSqr = delta.sqrMagnitude;
                nearest = candidate;
            }
        }
        return nearest != null;
    }

    private bool TryFindAllWithTag(string tagName, out GameObject[] objects)
    {
        objects = Array.Empty<GameObject>();
        if (string.IsNullOrWhiteSpace(tagName))
            return false;
        try
        {
            objects = GameObject.FindGameObjectsWithTag(tagName);
            return objects.Length > 0;
        }
        catch (UnityException)
        {
            Debug.LogWarning("AI trajectory: tag '" + tagName + "' is not defined.", this);
            return false;
        }
    }
}
