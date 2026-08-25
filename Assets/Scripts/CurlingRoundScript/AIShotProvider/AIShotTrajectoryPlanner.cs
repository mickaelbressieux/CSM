using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

public enum AIShotTarget
{
    Center,
    Player
}

/// <summary>
/// Builds a ShotData from the recorded curling simulations without moving the real stone.
/// The resulting shot is executed by StoneLauncher through an IShotProvider.
/// </summary>
[DisallowMultipleComponent]
public sealed class AIShotTrajectoryPlanner : MonoBehaviour
{
    private struct Candidate
    {
        public Vector3 Direction;
        public float Force;
        public float PredictorCurl;
        public float LateralOffset;
        public float Error;
    }

    [Header("Targets")]
    [SerializeField] private string centerTag = "Center";
    [SerializeField] private string playerTag = "Player";

    [Header("Obstacle detection")]
    [SerializeField] private LayerMask obstacleLayerMask = ~0;
    [SerializeField, Min(0f)] private float obstacleClearanceMargin = 0.15f;

    [Header("Trajectory search")]
    [SerializeField, Min(0.01f)] private float acceptableTargetDistance = 0.75f;
    [SerializeField, Min(0.1f)] private float maximumAngle = 35f;
    [SerializeField, Min(0.1f)] private float angleStep = 1f;
    [SerializeField, Min(0.01f)] private float maximumCurl = 1.7f;
    [SerializeField, Min(0.01f)] private float curlStep = 0.1f;
    [SerializeField, Min(0f)] private float maximumLateralOffset = 4f;
    [SerializeField, Range(8, 32)] private int forceSearchIterations = 20;

    [Header("Recorded simulation data")]
    [Tooltip("Path relative to the project root (the parent of Assets).")]
    [SerializeField] private string simulationFolder = "Logs/simu curl pc";
    [SerializeField, Range(5, 50)] private int waypointCount = 20;

    public float MaxCurl => Mathf.Max(maximumCurl, 0.01f);
    public float MaxLateral => Mathf.Max(maximumLateralOffset, 0.01f);
    public GameObject LastTarget { get; private set; }
    public float LastPredictedDistance { get; private set; } = -1f;
    public bool LastShotWasFallback { get; private set; }

    private readonly Dictionary<Vector2Int, Vector2> stopByKey = new Dictionary<Vector2Int, Vector2>();
    private readonly Dictionary<Vector2Int, Vector2[]> waypointsByKey = new Dictionary<Vector2Int, Vector2[]>();
    private readonly List<float> forceGrid = new List<float>();
    private readonly List<float> curlGrid = new List<float>();
    private Collider[] ownColliders = Array.Empty<Collider>();
    private bool predictorLoaded;

    private static readonly Regex SimulationFileRegex = new Regex(
        @"^sim_F(?<f>[0-9]+(?:,[0-9]+)?)_C(?<neg>m)?(?<c>[0-9]+(?:,[0-9]+)?)\.txt$",
        RegexOptions.Compiled);

    private void Awake()
    {
        ownColliders = GetComponentsInChildren<Collider>(true);
    }

    private void OnValidate()
    {
        obstacleClearanceMargin = Mathf.Max(0f, obstacleClearanceMargin);
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

        if (!EnsurePredictorLoaded())
            return false;

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

            // First try the obstacle-free straight simulation requested by the strategy.
            if (!IsPathObstructed(virtualStart, direct, target))
            {
                shot = ToShotData(direct);
                LastPredictedDistance = direct.Error;
                return true;
            }

            RememberIfBetterClear(direct, virtualStart, target, ref hasClearCandidate, ref bestClearCandidate);

            // Then increase angle and curl progressively. The first clear trajectory that
            // reaches the target is returned; farther clear shots are retained as fallbacks.
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

                    foreach (Vector2 angleAndPredictorCurl in CompatibleAngleCurlPairs(
                        angleMagnitude, curlMagnitude))
                    {
                        float angle = angleAndPredictorCurl.x;
                        float predictorCurl = angleAndPredictorCurl.y;
                        Candidate candidate = BuildCandidate(virtualStart, targetPosition,
                            baseDirection, baseYaw, angle, predictorCurl, lateralOffset);

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

        // No exact route: send the collision-free solution ending closest to the target.
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

    private List<float> BuildOffsetAttempts(Vector3 targetPosition)
    {
        var attempts = new List<float> { 0f };
        float alignedOffset = Mathf.Clamp(targetPosition.x - transform.position.x,
            -maximumLateralOffset, maximumLateralOffset);

        AddUnique(attempts, alignedOffset);       // directly opposite the target
        AddUnique(attempts, alignedOffset * 0.5f); // halfway from the initial position
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
        float baseYaw, float angle, float predictorCurl, float lateralOffset)
    {
        float yaw = baseYaw + angle * Mathf.Deg2Rad;
        float force = FindBestForce(start, target, yaw, predictorCurl, out Vector3 stop);
        return new Candidate
        {
            Direction = Quaternion.Euler(0f, angle, 0f) * baseDirection,
            Force = force,
            PredictorCurl = predictorCurl,
            LateralOffset = lateralOffset,
            Error = FlatDistance(stop, target)
        };
    }

    private float FindBestForce(Vector3 start, Vector3 target, float yaw, float curl, out Vector3 bestStop)
    {
        float low = forceGrid[0];
        float high = forceGrid[forceGrid.Count - 1];
        float bestForce = low;
        bestStop = PredictWorldStop(start, low, curl, yaw);
        float bestError = FlatDistance(bestStop, target);

        // Ternary minimization is stable for both straight and curved recorded paths.
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
        TryPredictFinalOffset(force, curl, out Vector2 localStop);
        return LocalToWorld(start, localStop, yaw);
    }

    private bool IsPathObstructed(Vector3 start, Candidate candidate, GameObject target)
    {
        if (!TryPredictWaypoints(candidate.Force, candidate.PredictorCurl, out Vector2[] points))
            return true;

        float yaw = Mathf.Atan2(candidate.Direction.x, candidate.Direction.z);
        float radius = GetStoneRadius() + obstacleClearanceMargin;
        Vector3 previous = start;

        foreach (Vector2 localPoint in points)
        {
            Vector3 next = LocalToWorld(start, localPoint, yaw);
            Collider[] hits = Physics.OverlapCapsule(previous, next, radius,
                obstacleLayerMask, QueryTriggerInteraction.Ignore);

            foreach (Collider hit in hits)
                if (IsBlockingCollider(hit, start.y, target))
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

    private void RememberIfBetterClear(Candidate candidate, Vector3 start, GameObject target,
        ref bool hasBest, ref Candidate best)
    {
        if (IsPathObstructed(start, candidate, target))
            return;
        if (!hasBest || candidate.Error < best.Error)
        {
            hasBest = true;
            best = candidate;
        }
    }

    private static ShotData ToShotData(Candidate candidate)
    {
        // Recorded simulations use the opposite curl sign from StoneLauncher.
        return new ShotData(candidate.Direction, candidate.Force,
            -candidate.PredictorCurl, candidate.LateralOffset);
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
        float angleMagnitude, float predictorCurlMagnitude)
    {
        // With no curl, both aim directions remain useful and are unrestricted.
        if (predictorCurlMagnitude <= 0.0001f)
        {
            foreach (float angle in SignedValues(angleMagnitude))
                yield return new Vector2(angle, 0f);
            yield break;
        }

        // A non-zero rotation must be paired with a non-zero aim in the opposite
        // direction in the final ShotData. Recorded simulations use the opposite curl
        // convention, and ToShotData negates PredictorCurl. Therefore angle and the
        // INTERNAL predictor curl must have the SAME sign:
        //   aim + / predictor curl + -> final curl -
        //   aim - / predictor curl - -> final curl +
        if (angleMagnitude <= 0.0001f)
            yield break;

        yield return new Vector2(angleMagnitude, predictorCurlMagnitude);
        yield return new Vector2(-angleMagnitude, -predictorCurlMagnitude);
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

    private bool EnsurePredictorLoaded()
    {
        if (predictorLoaded)
            return forceGrid.Count > 0 && curlGrid.Count > 0;

        predictorLoaded = true;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", simulationFolder));
        if (!Directory.Exists(folder))
        {
            Debug.LogWarning("AI trajectory: simulation folder not found: " + folder, this);
            return false;
        }

        var stopSamples = new Dictionary<Vector2Int, List<Vector2>>();
        var waypointSamples = new Dictionary<Vector2Int, List<Vector2[]>>();

        foreach (string path in Directory.EnumerateFiles(folder, "sim_F*_C*.txt"))
        {
            if (!TryParseForceCurl(Path.GetFileName(path), out float force, out float curl)
                || !TryReadWaypoints(path, waypointCount, out Vector2[] points))
                continue;

            Vector2Int key = PredictorKey(force, curl);
            if (!stopSamples.TryGetValue(key, out List<Vector2> stops))
                stopSamples.Add(key, stops = new List<Vector2>());
            if (!waypointSamples.TryGetValue(key, out List<Vector2[]> paths))
                waypointSamples.Add(key, paths = new List<Vector2[]>());
            stops.Add(points[points.Length - 1]);
            paths.Add(points);
        }

        var forces = new HashSet<float>();
        var curls = new HashSet<float>();
        foreach (KeyValuePair<Vector2Int, List<Vector2>> pair in stopSamples)
        {
            Vector2 mean = Vector2.zero;
            foreach (Vector2 value in pair.Value)
                mean += value;
            stopByKey[pair.Key] = mean / pair.Value.Count;
            forces.Add(pair.Key.x / 100f);
            curls.Add(pair.Key.y / 100f);
        }

        foreach (KeyValuePair<Vector2Int, List<Vector2[]>> pair in waypointSamples)
        {
            var mean = new Vector2[waypointCount];
            foreach (Vector2[] path in pair.Value)
                for (int i = 0; i < waypointCount; i++)
                    mean[i] += path[i];
            for (int i = 0; i < waypointCount; i++)
                mean[i] /= pair.Value.Count;
            waypointsByKey[pair.Key] = mean;
        }

        forceGrid.AddRange(forces);
        curlGrid.AddRange(curls);
        forceGrid.Sort();
        curlGrid.Sort();

        bool loaded = forceGrid.Count > 0 && curlGrid.Count > 0;
        if (!loaded)
            Debug.LogWarning("AI trajectory: no usable simulation data in " + folder, this);
        return loaded;
    }

    private bool TryPredictFinalOffset(float force, float curl, out Vector2 offset)
    {
        offset = Vector2.zero;
        if (!TryGetInterpolation(force, curl, out Vector2Int k00, out Vector2Int k10,
            out Vector2Int k01, out Vector2Int k11, out float forceT, out float curlT))
            return false;

        Vector2 q00 = GetNearestStop(k00);
        Vector2 q10 = GetNearestStop(k10);
        Vector2 q01 = GetNearestStop(k01);
        Vector2 q11 = GetNearestStop(k11);
        offset = Vector2.Lerp(Vector2.Lerp(q00, q10, forceT),
            Vector2.Lerp(q01, q11, forceT), curlT);
        return true;
    }

    private bool TryPredictWaypoints(float force, float curl, out Vector2[] points)
    {
        points = null;
        if (!TryGetInterpolation(force, curl, out Vector2Int k00, out Vector2Int k10,
            out Vector2Int k01, out Vector2Int k11, out float forceT, out float curlT))
            return false;

        if (!waypointsByKey.TryGetValue(k00, out Vector2[] p00))
            return false;
        if (!waypointsByKey.TryGetValue(k10, out Vector2[] p10)) p10 = p00;
        if (!waypointsByKey.TryGetValue(k01, out Vector2[] p01)) p01 = p00;
        if (!waypointsByKey.TryGetValue(k11, out Vector2[] p11)) p11 = p00;

        points = new Vector2[waypointCount];
        for (int i = 0; i < waypointCount; i++)
        {
            Vector2 low = Vector2.Lerp(p00[i], p10[i], forceT);
            Vector2 high = Vector2.Lerp(p01[i], p11[i], forceT);
            points[i] = Vector2.Lerp(low, high, curlT);
        }
        return true;
    }

    private bool TryGetInterpolation(float force, float curl, out Vector2Int k00, out Vector2Int k10,
        out Vector2Int k01, out Vector2Int k11, out float forceT, out float curlT)
    {
        k00 = k10 = k01 = k11 = default;
        forceT = curlT = 0f;
        if (forceGrid.Count == 0 || curlGrid.Count == 0)
            return false;

        float f = Mathf.Clamp(force, forceGrid[0], forceGrid[forceGrid.Count - 1]);
        float c = Mathf.Clamp(curl, curlGrid[0], curlGrid[curlGrid.Count - 1]);
        int fi0 = FindLowerIndex(forceGrid, f);
        int fi1 = Mathf.Min(fi0 + 1, forceGrid.Count - 1);
        int ci0 = FindLowerIndex(curlGrid, c);
        int ci1 = Mathf.Min(ci0 + 1, curlGrid.Count - 1);
        float f0 = forceGrid[fi0], f1 = forceGrid[fi1];
        float c0 = curlGrid[ci0], c1 = curlGrid[ci1];
        k00 = PredictorKey(f0, c0);
        k10 = PredictorKey(f1, c0);
        k01 = PredictorKey(f0, c1);
        k11 = PredictorKey(f1, c1);
        forceT = Mathf.Approximately(f0, f1) ? 0f : (f - f0) / (f1 - f0);
        curlT = Mathf.Approximately(c0, c1) ? 0f : (c - c0) / (c1 - c0);
        return true;
    }

    private Vector2 GetNearestStop(Vector2Int key)
    {
        if (stopByKey.TryGetValue(key, out Vector2 exact))
            return exact;

        int bestDistance = int.MaxValue;
        Vector2 best = Vector2.zero;
        foreach (KeyValuePair<Vector2Int, Vector2> pair in stopByKey)
        {
            int dx = pair.Key.x - key.x;
            int dy = pair.Key.y - key.y;
            int distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = pair.Value;
            }
        }
        return best;
    }

    private static int FindLowerIndex(List<float> grid, float value)
    {
        if (grid.Count == 1 || value <= grid[0])
            return 0;
        for (int i = 0; i < grid.Count - 1; i++)
            if (grid[i] <= value && value <= grid[i + 1])
                return i;
        return grid.Count - 2;
    }

    private static Vector2Int PredictorKey(float force, float curl)
    {
        return new Vector2Int(Mathf.RoundToInt(force * 100f), Mathf.RoundToInt(curl * 100f));
    }

    private static bool TryParseForceCurl(string fileName, out float force, out float curl)
    {
        force = curl = 0f;
        Match match = SimulationFileRegex.Match(fileName);
        if (!match.Success || !TryParseNumber(match.Groups["f"].Value, out force)
            || !TryParseNumber(match.Groups["c"].Value, out curl))
            return false;
        if (match.Groups["neg"].Success)
            curl = -curl;
        return true;
    }

    private static bool TryReadWaypoints(string path, int count, out Vector2[] points)
    {
        points = null;
        var samples = new List<Vector3>(); // time, x, z
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && TryParseNumber(parts[0], out float time)
                && TryParseNumber(parts[1], out float x) && TryParseNumber(parts[2], out float z))
                samples.Add(new Vector3(time, x, z));
        }

        if (samples.Count < 2 || samples[samples.Count - 1].x <= 0f)
            return false;

        points = new Vector2[count];
        float finalTime = samples[samples.Count - 1].x;
        int lower = 0;
        for (int i = 0; i < count; i++)
        {
            float wantedTime = finalTime * (i + 1f) / count;
            while (lower < samples.Count - 2 && samples[lower + 1].x < wantedTime)
                lower++;
            int upper = Mathf.Min(lower + 1, samples.Count - 1);
            float duration = samples[upper].x - samples[lower].x;
            float t = Mathf.Approximately(duration, 0f) ? 0f : (wantedTime - samples[lower].x) / duration;
            points[i] = new Vector2(
                Mathf.Lerp(samples[lower].y, samples[upper].y, t),
                Mathf.Lerp(samples[lower].z, samples[upper].z, t));
        }
        return true;
    }

    private static bool TryParseNumber(string text, out float value)
    {
        return float.TryParse(text.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value);
    }
}
