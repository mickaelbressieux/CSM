using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SoloCurlingGameManager : MonoBehaviour
{
    [Header("References")]
    public CurlingStoneController stone;
    public Transform houseCenter;
    public Transform stoneStartPoint;

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
    private float stoneGroundY;
    private bool warnedMissingEnemyTag;

    private bool resultProcessed = false;
    private int  lastScore       = 0;

    private void Start()
    {
        if (stoneStartPoint != null)
            stone.transform.position = stoneStartPoint.position;

        stoneGroundY = stone.transform.position.y;
        SpawnEnemyStones();
    }

    private void Update()
    {
        if (stone == null || houseCenter == null)
            return;

        if (stone.ShotFinished && !resultProcessed && AllEnemiesStopped())
        {
            lastScore = ComputeScore();
            resultProcessed = true;

            Debug.Log("Pierre arrêtée. Score = " + lastScore);
        }

        if (resultProcessed && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            DoReset();
        }
        else if (!resultProcessed && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            DoReset();
        }
    }

    private void DoReset()
    {
        stone.ResetStone();
        if (stoneStartPoint != null)
            stone.transform.position = stoneStartPoint.position;
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
        // Snap slow-moving enemy stones to a full stop so they don't creep indefinitely
        foreach (var s in enemyStones)
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

        Vector3 playerPos = stone.transform.position;
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
        if (stone == null || houseCenter == null)
            return -1f;

        Vector3 stonePos  = stone.transform.position;
        Vector3 targetPos = houseCenter.position;

        stonePos.y  = 0f;
        targetPos.y = 0f;

        return Vector3.Distance(stonePos, targetPos);
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
