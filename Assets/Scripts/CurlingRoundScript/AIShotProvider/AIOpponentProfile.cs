using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable, scene-independent definition of one curling opponent.
/// Create one asset per AI personality and assign it to the matching NPC.
/// </summary>
[CreateAssetMenu(fileName = "AI Opponent Profile", menuName = "Curling/AI Opponent Profile")]
public sealed class AIOpponentProfile : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string opponentName = "Opponent";
    [Tooltip("Identifiant stable et unique utilise par la progression de campagne.")]
    [SerializeField] private string progressionId;

    [Header("Difficulty")]
    [SerializeField] private StrategicAIDifficulty difficulty = StrategicAIDifficulty.Intermediate;

    [Header("Match")]
    [Tooltip("Number of stones this opponent can throw during a match.")]
    [SerializeField, Min(1)] private int stoneCount = 3;

    [Header("Strategy over multiple throws")]
    [Tooltip("Used when the sequence is empty or has ended without Repeat enabled.")]
    [SerializeField] private AIStrategyType defaultStrategy = AIStrategyType.Adaptive;
    [Tooltip("Entry 0 is throw 1, entry 1 is throw 2, and so on.")]
    [SerializeField] private List<AIStrategyType> strategyByThrow = new List<AIStrategyType>();
    [SerializeField] private bool repeatStrategySequence;

    [Header("Behaviour")]
    [SerializeField, Min(0f)] private float thinkDelaySeconds = 1f;
    [SerializeField, Min(0f)] private float playerDetectionDistanceFromCenter = 10f;
    [SerializeField, Min(0.01f)] private float playerHitPowerMultiplier = 1.10f;
    [SerializeField, Min(0.01f)] private float emergencyPower = 17f;

    public string OpponentName => string.IsNullOrWhiteSpace(opponentName) ? name : opponentName;
    public string ProgressionId => string.IsNullOrWhiteSpace(progressionId) ? name : progressionId.Trim();
    public StrategicAIDifficulty Difficulty => difficulty;
    public int StoneCount => Mathf.Max(1, stoneCount);
    public float ThinkDelaySeconds => thinkDelaySeconds;
    public float PlayerDetectionDistanceFromCenter => playerDetectionDistanceFromCenter;
    public float PlayerHitPowerMultiplier => playerHitPowerMultiplier;
    public float EmergencyPower => emergencyPower;

    public AIStrategyType GetStrategyForThrow(int oneBasedThrowNumber)
    {
        if (strategyByThrow == null || strategyByThrow.Count == 0 || oneBasedThrowNumber <= 0)
            return defaultStrategy;

        int index = oneBasedThrowNumber - 1;
        if (repeatStrategySequence)
            index %= strategyByThrow.Count;

        return index >= 0 && index < strategyByThrow.Count
            ? strategyByThrow[index]
            : defaultStrategy;
    }

    private void OnValidate()
    {
        stoneCount = Mathf.Max(1, stoneCount);
        thinkDelaySeconds = Mathf.Max(0f, thinkDelaySeconds);
        playerDetectionDistanceFromCenter = Mathf.Max(0f, playerDetectionDistanceFromCenter);
        playerHitPowerMultiplier = Mathf.Max(0.01f, playerHitPowerMultiplier);
        emergencyPower = Mathf.Max(0.01f, emergencyPower);
    }
}
