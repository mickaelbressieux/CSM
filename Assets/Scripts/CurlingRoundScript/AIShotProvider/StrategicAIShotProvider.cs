using System;
using System.Collections;
using UnityEngine;

public enum StrategicAIDifficulty
{
    Easy,
    Intermediate,
    Hard,
    Insane
}

public enum AIStrategyType
{
    CenterControl,
    AttackNearestPlayer,
    Adaptive
}

/// <summary>
/// Chooses the AI strategy and publishes exactly one ShotData through IShotProvider.
/// StoneLauncher remains the only component that moves the real stone.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AIShotTrajectoryPlanner))]
public sealed class StrategicAIShotProvider : MonoBehaviour, IShotProvider, IShotContextReceiver
{
    [Header("Local fallback (used when no opponent controller is active)")]
    [SerializeField] private StrategicAIDifficulty difficulty = StrategicAIDifficulty.Intermediate;
    [SerializeField] private AIStrategyType strategy = AIStrategyType.Adaptive;
    [SerializeField, Min(0f)] private float playerDetectionDistanceFromCenter = 10f;
    [SerializeField, Min(0.01f)] private float playerHitPowerMultiplier = 1.10f;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float thinkDelaySeconds = 1f;

    [Header("Failure fallback")]
    [Tooltip("Used only if the target or recorded simulation data is missing.")]
    [SerializeField, Min(0.01f)] private float emergencyPower = 17f;

    private AIShotTrajectoryPlanner planner;
    private Transform houseCenter;
    private Coroutine thinkingRoutine;
    private bool armed = true;
    private ShotData intendedShot;

    public event Action<ShotData> ShotReady;

    public ShotData CurrentShot => intendedShot;
    public float MaxCurl => planner != null ? planner.MaxCurl : 0.01f;
    public float MaxLateral => planner != null ? planner.MaxLateral : 0.01f;

    private void Awake()
    {
        planner = GetComponent<AIShotTrajectoryPlanner>();
    }

    private void OnEnable()
    {
        StartThinkingIfArmed();
    }

    private void OnDisable()
    {
        if (thinkingRoutine != null)
        {
            StopCoroutine(thinkingRoutine);
            thinkingRoutine = null;
        }
    }

    public void Configure(ShotContext context)
    {
        if (context.HouseCenter != null)
            houseCenter = context.HouseCenter;
    }

    public void Rearm()
    {
        armed = true;
        intendedShot = default;
        StartThinkingIfArmed();
    }

    private void StartThinkingIfArmed()
    {
        if (!armed || !isActiveAndEnabled || thinkingRoutine != null)
            return;
        thinkingRoutine = StartCoroutine(ThinkThenCommit());
    }

    private IEnumerator ThinkThenCommit()
    {
        // Waiting one frame also guarantees that SoloCurlingGameManager.Configure has run
        // after prefab creation, even if the prefab was initially active.
        yield return null;

        AIOpponentController opponent = AIOpponentController.ActiveOpponent;
        AIOpponentController.ShotOrder order = opponent != null
            ? opponent.RequestShotOrder()
            : BuildLocalOrder();

        // A default order means the active controller could not provide a profile.
        if (order.ThrowNumber == 0 && opponent != null)
        {
            opponent = null;
            order = BuildLocalOrder();
        }

        if (order.ThinkDelaySeconds > 0f)
            yield return new WaitForSeconds(order.ThinkDelaySeconds);

        thinkingRoutine = null;
        if (!armed)
            yield break;

        GameObject target = ChooseTarget(order.Strategy,
            order.PlayerDetectionDistanceFromCenter, out bool targetIsPlayer);
        bool planned = target != null && planner.TryBuildShot(target, out intendedShot);

        if (!planned)
        {
            intendedShot = BuildEmergencyShot(target, order.EmergencyPower);
            Debug.LogWarning("Strategic AI: trajectory planning failed; using emergency shot.", this);
        }

        if (targetIsPlayer)
        {
            intendedShot = new ShotData(intendedShot.Direction,
                intendedShot.Power * order.PlayerHitPowerMultiplier,
                intendedShot.Curl,
                intendedShot.LateralOffset);
        }

        intendedShot = ApplyDifficulty(intendedShot, order.Difficulty,
            planner.MaxCurl, planner.MaxLateral);
        armed = false;

        opponent?.RecordPreparedShot(order, intendedShot, target,
            !planned || planner.LastShotWasFallback);

        Debug.Log("Strategic AI: " + order.Strategy + " / " + order.Difficulty
            + " -> target=" + (target != null ? target.name : "none")
            + ", power=" + intendedShot.Power.ToString("F2")
            + ", curl=" + intendedShot.Curl.ToString("F2")
            + ", offset=" + intendedShot.LateralOffset.ToString("F2"), this);

        ShotReady?.Invoke(intendedShot);
    }

    private AIOpponentController.ShotOrder BuildLocalOrder()
    {
        return new AIOpponentController.ShotOrder(
            0,
            strategy,
            difficulty,
            thinkDelaySeconds,
            playerDetectionDistanceFromCenter,
            playerHitPowerMultiplier,
            emergencyPower);
    }

    private GameObject ChooseTarget(AIStrategyType selectedStrategy,
        float detectionDistance, out bool targetIsPlayer)
    {
        targetIsPlayer = false;
        switch (selectedStrategy)
        {
            case AIStrategyType.AttackNearestPlayer:
                if (planner.TryFindNearestTarget(AIShotTarget.Player, out GameObject nearestPlayer))
                {
                    targetIsPlayer = true;
                    return nearestPlayer;
                }
                return ResolveCenter();

            case AIStrategyType.Adaptive:
                GameObject center = ResolveCenter();
                if (center != null)
                {
                    GameObject playerNearCenter = planner.FindPlayerClosestTo(
                        center.transform.position, detectionDistance);
                    if (playerNearCenter != null)
                    {
                        targetIsPlayer = true;
                        return playerNearCenter;
                    }
                }
                return center;

            default:
                return ResolveCenter();
        }
    }

    private GameObject ResolveCenter()
    {
        if (houseCenter != null)
            return houseCenter.gameObject;
        planner.TryFindNearestTarget(AIShotTarget.Center, out GameObject center);
        return center;
    }

    private ShotData BuildEmergencyShot(GameObject target, float fallbackPower)
    {
        Vector3 direction = transform.forward;
        if (target != null)
        {
            direction = target.transform.position - transform.position;
            direction.y = 0f;
        }
        return new ShotData(direction, fallbackPower, 0f, 0f);
    }

    private static ShotData ApplyDifficulty(ShotData source,
        StrategicAIDifficulty selectedDifficulty, float maximumCurl, float maximumLateral)
    {
        // Insane uses the exact trajectory produced by the planner: no random power,
        // direction, curl or lateral-offset modification is applied.
        if (selectedDifficulty == StrategicAIDifficulty.Insane)
            return source;

        float powerNoise;
        float angleNoise;
        float curlNoise;
        float lateralNoise;

        switch (selectedDifficulty)
        {
            case StrategicAIDifficulty.Easy:
                powerNoise = 0.125f;
                angleNoise = 8f;
                curlNoise = 0.15f;
                lateralNoise = 0.15f;
                break;

            case StrategicAIDifficulty.Intermediate:
                powerNoise = 0.085f;
                angleNoise = 5f;
                curlNoise = 0.10f;
                lateralNoise = 0.10f;
                break;

            default:
                powerNoise = 0.025f;
                angleNoise = 3f;
                curlNoise = 0.05f;
                lateralNoise = 0.05f;
                break;
        }

        float power = Mathf.Max(0.01f,
            source.Power * (1f + UnityEngine.Random.Range(-powerNoise, powerNoise)));
        float angle = UnityEngine.Random.Range(-angleNoise, angleNoise);
        float curl = Mathf.Clamp(source.Curl
            + UnityEngine.Random.Range(-curlNoise, curlNoise), -maximumCurl, maximumCurl);
        float lateral = Mathf.Clamp(source.LateralOffset
            + UnityEngine.Random.Range(-lateralNoise, lateralNoise), -maximumLateral, maximumLateral);
        Vector3 direction = Quaternion.Euler(0f, angle, 0f) * source.Direction;
        return new ShotData(direction, power, curl, lateral);
    }
}
