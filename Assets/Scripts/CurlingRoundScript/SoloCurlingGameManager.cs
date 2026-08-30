using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class SoloCurlingGameManager : MonoBehaviour, ISceneTransitionDataReceiver
{
    // TestDrop = the original showcase (drop N enemy stones, player shoots one).
    // Match    = temp turn-based round: AI and player alternate, AI first, 3 stones each.
    public enum GameMode { TestDrop, Match }

    [Header("Game Mode")]
    public GameMode mode = GameMode.TestDrop;

    [Header("References")]
    public Transform houseCenter;
    public Transform stoneStartPoint;

    [Header("Stones (both modes spawn from prefabs)")]
    // Each stone prefab is authored with exactly one StoneLauncher + one IShotProvider, and the
    // launcher's shotProviderSource wired to that provider. Physics/provider tuning lives on the
    // prefab, so swapping in a real AI is just a different prefab - no manager code changes.
    public GameObject playerStonePrefab;      // carries StoneLauncher + PlayerShotProvider
    public GameObject aiStonePrefab;          // carries StoneLauncher + an IShotProvider (FakeAIShotProvider today)
    [FormerlySerializedAs("matchAimArrow")]
    public GameObject aimArrowPrefab;         // aim-preview PREFAB; instantiated per player stone
    public CurlingUIManager soloCurlingUI;    // the single UI; driven with banner + active shot

    [Header("Match Mode (temp showcase)")]
    public int stonesPerSide = 3;

    [Header("Match - recovery")]
    public Key forceNextTurnKey = Key.N;      // force the current turn to end / pass to the other player
    public float killY = -2f;                 // a stone below this Y has fallen off the sheet -> out of play

    [Header("Enemy Stones")]
    public GameObject enemyStonePrefab;
    public int   enemyStoneCount   = 3;
    public float enemyMinRadius    = 0.3f;
    public float enemyMaxRadius    = 3.5f;
    public float stoneRadius       = 0.145f; // physical radius used for overlap checks
    public int   maxSpawnAttempts  = 30;     // retries per stone before giving up
    public float enemySlideDrag    = 0.001f; // linear drag while gliding (match player stone)
    public float enemyStopThreshold = 0.05f; // velocity below which enemy stone is frozen
    public string enemyTag = "opponent";

    private List<GameObject> enemyStones = new List<GameObject>();
    // Match-mode stones, tracked by side so scoring never relies on tags.
    private List<GameObject> playerStones = new List<GameObject>();
    private List<GameObject> aiStones     = new List<GameObject>();
    // Stones that fell off the sheet (below killY): frozen and excluded from scoring.
    private HashSet<GameObject> lostStones = new HashSet<GameObject>();
    private GameObject currentStone;      // the stone in play this turn (not yet in a side list)
    private bool forceEndTurn;            // set when the user forces the current turn to end
    private StoneLauncher playerLauncher; // the spawned player stone (test mode: HUD + scoring)
    private float stoneGroundY;
    private bool warnedMissingEnemyTag;

    private bool resultProcessed = false;
    private int  lastScore       = 0;

    public void ReceiveTransitionData(SceneTransitionData data)
    {
        if (data == null)
            return;

        if (data.TryGetBool(SceneTransitionDataKeys.CurlingMatchMode, out bool useMatchMode))
            mode = useMatchMode ? GameMode.Match : GameMode.TestDrop;

        if (data.TryGetInt(SceneTransitionDataKeys.CurlingPlayerStoneCount, out int playerStoneCount))
            stonesPerSide = Mathf.Max(1, playerStoneCount);

        if (data.TryGetObject(SceneTransitionDataKeys.CurlingEnemyProfile, out AIOpponentProfile opponentProfile))
        {
            AIOpponentController opponent = AIOpponentController.ActiveOpponent;
            if (opponent == null)
                opponent = FindFirstObjectByType<AIOpponentController>();

            if (opponent != null)
                opponent.SetProfile(opponentProfile, true);
            else
                Debug.LogWarning(name + ": no AIOpponentController found for the selected profile.", this);
        }
    }

    private void Start()
    {
        // Installe automatiquement le gestionnaire de sortie du match.
        if (GetComponent<CurlingMatchSceneFlow>() == null)
            gameObject.AddComponent<CurlingMatchSceneFlow>();

        // Both modes spawn their stones from prefabs (tuning lives on the prefab).
        // Drive the shared UI even if it wasn't wired in the inspector.
        if (soloCurlingUI == null) soloCurlingUI = FindFirstObjectByType<CurlingUIManager>();
        stoneGroundY = stoneStartPoint != null ? stoneStartPoint.position.y : 0f;

        if (mode == GameMode.Match)
        {
            StartCoroutine(RunMatch());
            return;
        }

        // Test-drop: spawn the player stone from the prefab, then drop the enemies.
        SpawnTestPlayerStone();
        SpawnEnemyStones();
    }

    private void SpawnTestPlayerStone()
    {
        StoneLauncher launcher;
        IShotProvider provider;
        BuildStone(false, out launcher, out provider);
        playerLauncher = launcher;
        if (soloCurlingUI != null)
            soloCurlingUI.SetActiveShot(launcher, provider);
    }

    private void Update()
    {
        // Match mode drives its own turns / reset inside RunMatch(); here we only watch
        // for the force-next-turn key so a stuck stone can be skipped.
        if (mode == GameMode.Match)
        {
            if (Keyboard.current != null && Keyboard.current[forceNextTurnKey].wasPressedThisFrame)
                forceEndTurn = true;
            return;
        }

        if (playerLauncher == null || houseCenter == null)
            return;

        if (playerLauncher.ShotFinished && !resultProcessed && AllEnemiesStopped())
        {
            lastScore = ComputeScore();
            resultProcessed = true;

            Debug.Log("Pierre arrêtée. Score = " + lastScore);
        }

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            DoReset();
        }
    }

    private void DoReset()
    {
        // Stop all enemy stones immediately before destroying them
        foreach (var s in enemyStones)
        {
            if (s == null) continue;
            Rigidbody erb = s.GetComponent<Rigidbody>();
            if (erb != null) { erb.linearVelocity = Vector3.zero; erb.angularVelocity = Vector3.zero; }
        }

        // Fresh player stone (its aim arrow is destroyed with it).
        if (playerLauncher != null) DestroyStoneAndArrow(playerLauncher.gameObject);
        SpawnTestPlayerStone();
        resultProcessed = false;
        lastScore = 0;
        SpawnEnemyStones();
    }

    private bool AllEnemiesStopped()
    {
        foreach (var s in enemyStones)
        {
            if (s == null) continue;
            Rigidbody rb = s.GetComponent<Rigidbody>();
            if (rb != null && rb.linearVelocity.magnitude > 0.01f)
                return false;
        }
        return true;
    }

    private void FixedUpdate()
    {
        // Snap slow-moving stones to a full stop so they don't creep indefinitely.
        if (mode == GameMode.Match)
        {
            // A stone that fell off the sheet never slows down on its own, which would
            // otherwise wedge the round; freeze any that dropped below killY.
            KillIfFallen(currentStone);
            foreach (var s in playerStones) KillIfFallen(s);
            foreach (var s in aiStones)     KillIfFallen(s);

            SnapSlowStones(playerStones);
            SnapSlowStones(aiStones);
            return;
        }

        SnapSlowStones(enemyStones);
    }

    // Freeze a stone that has dropped off the sheet and mark it out of play.
    private void KillIfFallen(GameObject s)
    {
        if (s == null || lostStones.Contains(s)) return;
        if (s.transform.position.y < killY)
        {
            NeutralizeStone(s);
            lostStones.Add(s);
            Stone id = s.GetComponent<Stone>();
            if (id != null) id.SetPhase(StonePhase.Lost);
            MatchEvents.RaiseStoneLost(id);
        }
    }

    // Stop a stone dead where it is (used by kill-plane recovery and manual force-skip).
    private void NeutralizeStone(GameObject s)
    {
        if (s == null) return;
        Rigidbody rb = s.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.constraints     = RigidbodyConstraints.FreezeAll;
        }
    }

    private void SnapSlowStones(List<GameObject> stones)
    {
        foreach (var s in stones)
        {
            if (s == null) continue;
            Rigidbody rb = s.GetComponent<Rigidbody>();
            if (rb == null) continue;
            if (rb.linearVelocity.magnitude < enemyStopThreshold && rb.linearVelocity.magnitude > 0f)
            {
                rb.linearVelocity  = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    private void SpawnEnemyStones()
    {
        foreach (var s in enemyStones)
            if (s != null) Destroy(s);
        enemyStones.Clear();

        if (enemyStonePrefab == null || houseCenter == null)
            return;

        for (int i = 0; i < enemyStoneCount; i++)
        {
            Vector3 pos = Vector3.zero;
            bool placed = false;

            for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
            {
                float angle  = Random.Range(0f, 360f);
                float radius = Random.Range(enemyMinRadius, enemyMaxRadius);
                float x = houseCenter.position.x + radius * Mathf.Cos(angle * Mathf.Deg2Rad);
                float z = houseCenter.position.z + radius * Mathf.Sin(angle * Mathf.Deg2Rad);
                Vector3 candidate = new Vector3(x, stoneGroundY, z);

                bool overlaps = false;
                foreach (var existing in enemyStones)
                {
                    if (existing == null) continue;
                    float dist = Vector2.Distance(
                        new Vector2(existing.transform.position.x, existing.transform.position.z),
                        new Vector2(candidate.x, candidate.z));
                    if (dist < stoneRadius * 2f) { overlaps = true; break; }
                }

                if (!overlaps) { pos = candidate; placed = true; break; }
            }

            if (placed)
            {
                GameObject go = Instantiate(enemyStonePrefab, pos, Quaternion.identity);
                AssignEnemyTag(go);
                Rigidbody erb = go.GetComponent<Rigidbody>();
                if (erb != null)
                {
                    erb.linearDamping  = enemySlideDrag;
                    erb.angularDamping = 0.05f;
                    erb.constraints    = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                }
                enemyStones.Add(go);
            }
        }
    }

    private int ComputeScore()
    {
        Vector3 center = houseCenter.position;
        center.y = 0f;

        Vector3 playerPos = playerLauncher.transform.position;
        playerPos.y = 0f;
        float playerDist = Vector3.Distance(playerPos, center);

        // Count how many enemy stones are closer than the player stone
        int closerEnemies = 0;
        foreach (var s in enemyStones)
        {
            if (s == null) continue;
            Vector3 ep = s.transform.position;
            ep.y = 0f;
            if (Vector3.Distance(ep, center) < playerDist)
                closerEnemies++;
        }

        // +1 if player is closest, otherwise -1 per enemy stone that beats them
        return closerEnemies == 0 ? 1 : -closerEnemies;
    }

    public int   GetLastScore()           => lastScore;
    public bool  HasResult()              => resultProcessed;

    public float GetDistanceToCenter()
    {
        if (playerLauncher == null || houseCenter == null)
            return -1f;

        Vector3 stonePos  = playerLauncher.transform.position;
        Vector3 targetPos = houseCenter.position;

        stonePos.y  = 0f;
        targetPos.y = 0f;

        return Vector3.Distance(stonePos, targetPos);
    }

    // ------------------------------------------------------------------
    // Temp match mode: AI and player alternate throws (AI first).
    // The player's count comes from stonesPerSide; the AI's comes from its opponent profile.
    // Every stone is spawned at runtime and driven through the SAME
    // StoneLauncher via the IShotProvider seam — that is what this showcases.
    // ------------------------------------------------------------------

    private IEnumerator RunMatch()
    {
        while (true)
        {
            ClearMatchStones();

            int playerStoneCount = Mathf.Max(1, stonesPerSide);
            AIOpponentProfile opponentProfile = AIOpponentController.ActiveOpponent?.Profile;
            int aiStoneCount = opponentProfile != null
                ? opponentProfile.StoneCount
                : playerStoneCount;
            int aiThrows = 0;
            int playerThrows = 0;
            int totalThrows = aiStoneCount + playerStoneCount;

            for (int i = 0; i < totalThrows; i++)
            {
                bool isAI;
                if (aiThrows >= aiStoneCount)
                    isAI = false;
                else if (playerThrows >= playerStoneCount)
                    isAI = true;
                else
                    isAI = i % 2 == 0; // the AI starts while both sides still have stones

                int throwNumber = isAI ? ++aiThrows : ++playerThrows;
                int sideStoneCount = isAI ? aiStoneCount : playerStoneCount;
                yield return StartCoroutine(RunTurn(isAI, throwNumber, sideStoneCount));
            }

            bool playerWon;
            string result = ComputeMatchResult(out playerWon);
            if (playerWon)
            {
                AIOpponentProfile defeatedOpponent = AIOpponentController.ActiveOpponent?.Profile;
                CampainManager.Instance?.RegisterOpponentVictory(defeatedOpponent);
            }
            Debug.Log("Match end. " + result);
            MatchEvents.RaiseEndScored(result);
            MatchEvents.RaiseMatchCompleted(playerWon, result);
            if (soloCurlingUI != null)
            {
                soloCurlingUI.SetActiveShot(null, null);
                soloCurlingUI.SetBanner(result + "\nReturning to campaign...");
            }

            yield return new WaitUntil(() =>
                Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame);
        }
    }

    private IEnumerator RunTurn(bool isAI, int throwNumber, int sideStoneCount)
    {
        StoneLauncher launcher;
        IShotProvider provider;
        GameObject go = BuildStone(isAI, out launcher, out provider);
        currentStone = go;
        forceEndTurn = false;

        if (launcher == null)
        {
            // Misconfigured prefab (BuildStone already logged the error); skip this turn instead
            // of NRE-ing on launcher below.
            DestroyStoneAndArrow(go);
            currentStone = null;
            yield break;
        }

        Stone stoneId = go.GetComponent<Stone>();
        MatchEvents.RaiseTurnStarted(stoneId);

        if (soloCurlingUI != null)
        {
            // Player turns show the live aim HUD; AI turns show a banner only.
            soloCurlingUI.SetActiveShot(launcher, isAI ? null : provider);
            soloCurlingUI.SetBanner(isAI
                ? $"AI is throwing... ({throwNumber}/{sideStoneCount})"
                : $"Your throw ({throwNumber}/{sideStoneCount}) - arrows aim/power, Q/E curl, A/D offset, Space to shoot");
        }

        // Wait for the shot to be released (or a forced skip)...
        yield return new WaitUntil(() => launcher.HasBeenShot || forceEndTurn);

        if (launcher.HasBeenShot)
            MatchEvents.RaiseStoneReleased(stoneId);

        // ...then for it (and any stones it bumped) to come to rest, unless the turn is
        // forced or the safety timeout fires.
        if (!forceEndTurn)
        {
            float elapsed = 0f;
            yield return new WaitUntil(() =>
                (launcher.ShotFinished && AllMatchStonesSettled()) ||
                forceEndTurn ||
                (elapsed += Time.deltaTime) > 20f);
        }

        currentStone = null;

        if (!launcher.HasBeenShot)
        {
            // Skipped before the stone was ever thrown - discard it.
            DestroyStoneAndArrow(go);
            forceEndTurn = false;
            yield break;
        }

        if (forceEndTurn)
            NeutralizeStone(go); // stop a runaway/stuck stone where it is

        MatchEvents.RaiseStoneStopped(stoneId);
        if (isAI) aiStones.Add(go); else playerStones.Add(go);
        forceEndTurn = false;
    }

    // Instantiate a stone from the right prefab. Each prefab is authored with exactly one
    // StoneLauncher + one IShotProvider (the launcher's shotProviderSource wired to that provider),
    // so there is nothing to add or strip here. We only hand the provider the scene references a
    // prefab can't bake in - what to aim at, and a per-stone aim-arrow instance - through
    // IShotContextReceiver, so this manager never names a concrete provider type. Used by BOTH
    // modes; the caller decides UI banners. Returns the now-active stone.
    private GameObject BuildStone(bool isAI, out StoneLauncher launcher, out IShotProvider provider)
    {
        GameObject prefab = isAI ? aiStonePrefab : playerStonePrefab;
        GameObject go = Instantiate(prefab, stoneStartPoint.position, stoneStartPoint.rotation);

        // Wire everything BEFORE the object goes live, so StoneLauncher.OnEnable
        // subscribes to a provider that is already assigned.
        go.SetActive(false);

        launcher = go.GetComponentInChildren<StoneLauncher>(true);
        provider = launcher != null ? launcher.shotProviderSource as IShotProvider : null;
        if (launcher == null || provider == null)
        {
            Debug.LogError(
                $"{name}: '{(prefab != null ? prefab.name : "stone prefab")}' must carry a StoneLauncher " +
                "whose shotProviderSource implements IShotProvider. Check the prefab wiring.", this);
            go.SetActive(true);
            return go;
        }

        // Spawn a private aim-arrow instance for a human stone. NOT parented to the stone: the stone
        // prefab is scaled to 0.06, so a child would inherit that scale (invisibly tiny) and its
        // pre-shot spin. It lives at world scale, is positioned each frame by the provider, and
        // DestroyStoneAndArrow() cleans it up with the stone.
        GameObject aimArrowInstance = null;
        if (!isAI && aimArrowPrefab != null)
            aimArrowInstance = Instantiate(aimArrowPrefab, go.transform.position, aimArrowPrefab.transform.rotation);

        // Inject the per-turn scene context (house center to aim at, this stone's aim arrow)
        // without naming a concrete provider type. Providers that don't need it ignore it.
        (provider as IShotContextReceiver)?.Configure(new ShotContext(houseCenter, aimArrowInstance));

        // Stamp identity so abilities / events / scoring can tell stones apart.
        Stone id = go.GetComponent<Stone>();
        if (id != null) id.Side = isAI ? StoneSide.AI : StoneSide.Player;

        go.SetActive(true); // now Awake/OnEnable run with everything wired
        return go;
    }

    private bool AllMatchStonesSettled()
    {
        return StonesSettled(playerStones) && StonesSettled(aiStones);
    }

    private bool StonesSettled(List<GameObject> stones)
    {
        foreach (var s in stones)
        {
            if (s == null) continue;
            Rigidbody rb = s.GetComponent<Rigidbody>();
            if (rb != null && rb.linearVelocity.magnitude > 0.01f)
                return false;
        }
        return true;
    }

    private void ClearMatchStones()
    {
        foreach (var s in playerStones) DestroyStoneAndArrow(s);
        foreach (var s in aiStones)     DestroyStoneAndArrow(s);
        DestroyStoneAndArrow(currentStone);
        playerStones.Clear();
        aiStones.Clear();
        lostStones.Clear();
        currentStone = null;
        forceEndTurn = false;
    }

    // Destroy a stone and the aim-arrow instance it owns (if any). Safe on null / AI stones.
    private void DestroyStoneAndArrow(GameObject go)
    {
        if (go == null) return;
        PlayerShotProvider p = go.GetComponent<PlayerShotProvider>();
        if (p != null && p.aimArrow != null) Destroy(p.aimArrow);
        Destroy(go);
    }

    // Standard curling end scoring: the side with the nearest stone scores one point
    // for every one of its stones closer to the button than the opponent's nearest.
    private string ComputeMatchResult(out bool playerWon)
    {
        Vector3 center = houseCenter.position;
        center.y = 0f;

        float playerNearest = NearestDistance(playerStones, center);
        float aiNearest      = NearestDistance(aiStones, center);

        if (playerNearest == float.MaxValue && aiNearest == float.MaxValue)
        {
            playerWon = false;
            return "No stones in play - draw.";
        }

        playerWon = playerNearest <= aiNearest;
        float opponentNearest = playerWon ? aiNearest : playerNearest;
        List<GameObject> winners = playerWon ? playerStones : aiStones;

        int points = 0;
        foreach (var s in winners)
        {
            if (s == null || lostStones.Contains(s)) continue;
            Vector3 p = s.transform.position;
            p.y = 0f;
            if (Vector3.Distance(p, center) < opponentNearest)
                points++;
        }

        string who = playerWon ? "You" : "AI";
        return $"{who} score {points}  (you: {Readable(playerNearest)}, AI: {Readable(aiNearest)})";
    }

    private static string Readable(float dist) =>
        dist == float.MaxValue ? "-" : dist.ToString("F2");

    private float NearestDistance(List<GameObject> stones, Vector3 center)
    {
        float best = float.MaxValue;
        foreach (var s in stones)
        {
            if (s == null || lostStones.Contains(s)) continue;
            Vector3 p = s.transform.position;
            p.y = 0f;
            float d = Vector3.Distance(p, center);
            if (d < best) best = d;
        }
        return best;
    }

    private void AssignEnemyTag(GameObject target)
    {
        if (target == null)
            return;

        if (!IsTagDefined(enemyTag))
        {
            if (!warnedMissingEnemyTag)
            {
                warnedMissingEnemyTag = true;
                Debug.LogWarning("Tag '" + enemyTag + "' is not defined. Add it in Tags and Layers to tag spawned stones.", this);
            }
            return;
        }

        warnedMissingEnemyTag = false;
        target.tag = enemyTag;
    }

    private bool IsTagDefined(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return false;

        if (tagName == "Untagged")
            return true;

        try
        {
            GameObject.FindWithTag(tagName);
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }
}
