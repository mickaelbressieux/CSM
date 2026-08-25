using UnityEngine;

/// <summary>
/// Runtime memory for the NPC currently playing the curling match.
/// It survives individual stones and supplies one immutable order per AI throw.
/// </summary>
[DisallowMultipleComponent]
public sealed class AIOpponentController : MonoBehaviour
{
    public readonly struct ShotOrder
    {
        public readonly int ThrowNumber;
        public readonly AIStrategyType Strategy;
        public readonly StrategicAIDifficulty Difficulty;
        public readonly float ThinkDelaySeconds;
        public readonly float PlayerDetectionDistanceFromCenter;
        public readonly float PlayerHitPowerMultiplier;
        public readonly float EmergencyPower;

        public ShotOrder(int throwNumber, AIStrategyType strategy,
            StrategicAIDifficulty difficulty, float thinkDelaySeconds,
            float playerDetectionDistanceFromCenter, float playerHitPowerMultiplier,
            float emergencyPower)
        {
            ThrowNumber = throwNumber;
            Strategy = strategy;
            Difficulty = difficulty;
            ThinkDelaySeconds = thinkDelaySeconds;
            PlayerDetectionDistanceFromCenter = playerDetectionDistanceFromCenter;
            PlayerHitPowerMultiplier = playerHitPowerMultiplier;
            EmergencyPower = emergencyPower;
        }
    }

    [Header("Opponent")]
    [SerializeField] private AIOpponentProfile profile;
    [Tooltip("Useful for a test scene containing only one opponent. Leave disabled on a map with several NPCs.")]
    [SerializeField] private bool beginEncounterOnEnable;
    [Tooltip("Normally disabled so pressing R can replay against the same opponent.")]
    [SerializeField] private bool endEncounterWhenMatchEnds;

    public static AIOpponentController ActiveOpponent { get; private set; }

    public AIOpponentProfile Profile => profile;
    public bool IsActiveOpponent => ActiveOpponent == this;
    public int ThrowsPlanned { get; private set; }
    public int StonesReleased { get; private set; }
    public int StonesStopped { get; private set; }
    public int StonesLost { get; private set; }
    public ShotData LastPreparedShot { get; private set; }
    public GameObject LastTarget { get; private set; }
    public bool LastPlanWasFallback { get; private set; }
    public string LastMatchResult { get; private set; } = string.Empty;

    private bool matchHasEnded;

    private void OnEnable()
    {
        MatchEvents.StoneReleased += OnStoneReleased;
        MatchEvents.StoneStopped += OnStoneStopped;
        MatchEvents.StoneLost += OnStoneLost;
        MatchEvents.EndScored += OnEndScored;

        if (beginEncounterOnEnable)
            BeginEncounter();
    }

    private void OnDisable()
    {
        MatchEvents.StoneReleased -= OnStoneReleased;
        MatchEvents.StoneStopped -= OnStoneStopped;
        MatchEvents.StoneLost -= OnStoneLost;
        MatchEvents.EndScored -= OnEndScored;

        if (ActiveOpponent == this)
            ActiveOpponent = null;
    }

    /// <summary>Call this from the NPC interaction that starts the match.</summary>
    public void BeginEncounter()
    {
        if (profile == null)
        {
            Debug.LogError(name + ": no AIOpponentProfile is assigned.", this);
            return;
        }

        if (ActiveOpponent != null && ActiveOpponent != this)
            ActiveOpponent.EndEncounter();

        ResetRuntimeState();
        ActiveOpponent = this;
        Debug.Log("AI encounter started: " + profile.OpponentName, this);
    }

    /// <summary>Call this when the player leaves the opponent or closes the encounter.</summary>
    public void EndEncounter()
    {
        if (ActiveOpponent == this)
            ActiveOpponent = null;
    }

    public void ResetRuntimeState()
    {
        ResetMatchProgress();
        LastMatchResult = string.Empty;
        matchHasEnded = false;
    }

    private void ResetMatchProgress()
    {
        ThrowsPlanned = 0;
        StonesReleased = 0;
        StonesStopped = 0;
        StonesLost = 0;
        LastPreparedShot = default;
        LastTarget = null;
        LastPlanWasFallback = false;
    }

    /// <summary>
    /// Reserves the next throw and returns the strategy/configuration selected by the profile.
    /// This counter, rather than the temporary stone, owns progression through the sequence.
    /// </summary>
    public ShotOrder RequestShotOrder()
    {
        if (!IsActiveOpponent || profile == null)
            return default;

        // SoloCurlingGameManager can replay with R without recreating the opponent.
        // The first requested shot of that replay starts the profile sequence again.
        if (matchHasEnded)
        {
            ResetMatchProgress();
            matchHasEnded = false;
        }

        ThrowsPlanned++;
        return new ShotOrder(
            ThrowsPlanned,
            profile.GetStrategyForThrow(ThrowsPlanned),
            profile.Difficulty,
            profile.ThinkDelaySeconds,
            profile.PlayerDetectionDistanceFromCenter,
            profile.PlayerHitPowerMultiplier,
            profile.EmergencyPower);
    }

    public void RecordPreparedShot(ShotOrder order, ShotData shot, GameObject target, bool wasFallback)
    {
        if (!IsActiveOpponent)
            return;

        LastPreparedShot = shot;
        LastTarget = target;
        LastPlanWasFallback = wasFallback;
        Debug.Log(profile.OpponentName + " prepared throw " + order.ThrowNumber
            + " using " + order.Strategy + ".", this);
    }

    private void OnStoneReleased(Stone stone)
    {
        if (IsActiveOpponent && stone != null && stone.Side == StoneSide.AI)
            StonesReleased++;
    }

    private void OnStoneStopped(Stone stone)
    {
        if (IsActiveOpponent && stone != null && stone.Side == StoneSide.AI)
            StonesStopped++;
    }

    private void OnStoneLost(Stone stone)
    {
        if (IsActiveOpponent && stone != null && stone.Side == StoneSide.AI)
            StonesLost++;
    }

    private void OnEndScored(string result)
    {
        if (!IsActiveOpponent)
            return;

        LastMatchResult = result ?? string.Empty;
        matchHasEnded = true;
        if (endEncounterWhenMatchEnds)
            EndEncounter();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveOpponent()
    {
        ActiveOpponent = null;
    }
}
