using System;

/// <summary>
/// The match-wide event channel: milestones anything can react to (a power, the UI, an audio
/// system) without wiring a reference to the manager. <see cref="SoloCurlingGameManager"/> raises
/// these; subscribers listen.
///
/// Deliberately a static hub (not a singleton MonoBehaviour) so subscribers need zero setup — the
/// trade-off is that <b>subscribers MUST unsubscribe</b> (subscribe in OnEnable, unsubscribe in
/// OnDisable) to avoid leaks and stale handlers. The statics are cleared on each play session via
/// <see cref="Reset"/> so entering Play Mode never carries handlers over.
///
/// The <see cref="Stone"/> argument may be null if a stone has no Stone component; handle it.
/// </summary>
public static class MatchEvents
{
    public static event Action<Stone> TurnStarted;
    public static event Action<Stone> StoneReleased;
    public static event Action<Stone> StoneStopped;
    public static event Action<Stone> StoneLost;
    public static event Action<string> EndScored;
    public static event Action<bool, string> MatchCompleted;

    public static void RaiseTurnStarted(Stone s)   => TurnStarted?.Invoke(s);
    public static void RaiseStoneReleased(Stone s) => StoneReleased?.Invoke(s);
    public static void RaiseStoneStopped(Stone s)  => StoneStopped?.Invoke(s);
    public static void RaiseStoneLost(Stone s)     => StoneLost?.Invoke(s);
    public static void RaiseEndScored(string result) => EndScored?.Invoke(result);
    public static void RaiseMatchCompleted(bool playerWon, string result) =>
        MatchCompleted?.Invoke(playerWon, result);

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        TurnStarted = StoneReleased = StoneStopped = StoneLost = null;
        EndScored = null;
        MatchCompleted = null;
    }
}
