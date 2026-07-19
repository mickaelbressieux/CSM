using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Gestionnaire du comportement de l'IA sur la scène.
/// Attacher ce script sur un GameObject vide dans la scène.
///
/// Usage :
///  - Assigner le prefab de la pierre (avec AIStoneController dessus) dans stonePrefab.
///  - Choisir la difficulté et le profil dans l'Inspector.
///  - Appuyer sur K en Play pour générer et lancer une pierre IA.
/// </summary>
public class AISceneManager : MonoBehaviour
{
    // ── Énumérations ─────────────────────────────────────────────────────────

    public enum AIDifficulty
    {
        Facile,
        Intermediaire,
        Difficile
    }

    public enum AIProfile
    {
        PCTest
    }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Pierre IA")]
    [Tooltip("Prefab de la pierre à instancier. Le prefab doit avoir un composant AIStoneController.")]
    [SerializeField] private GameObject stonePrefab;

    [Header("Comportement IA")]
    [SerializeField] private AIDifficulty difficulty = AIDifficulty.Intermediaire;
    [SerializeField] private AIProfile    activeProfile = AIProfile.PCTest;

    // ── Constantes du profil PCTest ───────────────────────────────────────────

    /// <summary>Tag de l'objet cible principal (centre de la maison).</summary>
    private const string CenterTag = "Center";

    /// <summary>Tag du joueur adverse.</summary>
    private const string PlayerTag = "Player";

    /// <summary>Distance maximale (en mètres, plan XZ) entre le joueur et le Center
    /// pour déclencher le ciblage du joueur.</summary>
    private const float PlayerDetectionDistance = 10f;

    /// <summary>Multiplicateur de force quand l'IA cible le joueur (+10 %).</summary>
    private const float PlayerForceBonusMultiplier = 1.10f;

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (WasKPressedThisFrame())
            SpawnAndLaunchAIStone();
    }

    // ── Génération et lancement ───────────────────────────────────────────────

    private void SpawnAndLaunchAIStone()
    {
        if (stonePrefab == null)
        {
            Debug.LogWarning("[AISceneManager] stonePrefab non assigné dans l'Inspector.", this);
            return;
        }

        GameObject stone = Instantiate(stonePrefab, Vector3.zero, Quaternion.identity);
        AIStoneController ctrl = stone.GetComponent<AIStoneController>();

        if (ctrl == null)
        {
            Debug.LogWarning("[AISceneManager] Le prefab de la pierre ne contient pas de composant AIStoneController.", this);
            Destroy(stone);
            return;
        }

        switch (activeProfile)
        {
            case AIProfile.PCTest:
                ApplyProfile_PCTest(ctrl);
                break;
        }
    }

    // ── Profil PCTest ─────────────────────────────────────────────────────────

    /// <summary>
    /// Stratégie PCTest :
    ///   • Cible principale : objet tagué "Center".
    ///   • Si un objet tagué "Player" se trouve à moins de 3 m du Center (plan XZ),
    ///     l'IA cible ce joueur avec une force augmentée de 10 %.
    /// </summary>
    private void ApplyProfile_PCTest(AIStoneController ctrl)
    {
        string targetLabel = CenterTag;
        bool targetIsPlayer = false;

        GameObject centerObj = SafeFindWithTag(CenterTag);
        GameObject playerClosestToCenter = FindClosestPlayerToCenterWithinRange(centerObj, PlayerDetectionDistance);

        bool launchPrepared;
        if (playerClosestToCenter != null)
        {
            targetIsPlayer = true;
            targetLabel = playerClosestToCenter.name + " (tag " + PlayerTag + ")";
            launchPrepared = ctrl.PrepareSimulationLaunchToTarget(playerClosestToCenter);
        }
        else
        {
            ctrl.SimulationTargetTag = CenterTag;
            launchPrepared = ctrl.PrepareSimulationLaunch();
        }

        if (!launchPrepared)
        {
            Debug.LogWarning("[AISceneManager] PCTest : impossible de calculer une trajectoire vers '"
                             + targetLabel + "'. La pierre est détruite.", this);
            Destroy(ctrl.gameObject);
            return;
        }

        // Récupération des paramètres calculés
        ctrl.GetPendingLaunchParams(out float force, out float angleDeg, out float curl);

        // Bonus de force si ciblage joueur (+10 %)
        if (targetIsPlayer)
        {
            force *= PlayerForceBonusMultiplier;
            Debug.Log("[AISceneManager] PCTest : bonus force joueur appliqué (+10 %) → force = " +
                      force.ToString("F2"), this);
        }

        // Application du bruit de difficulté
        ApplyDifficultyNoise(difficulty, ref force, ref angleDeg, ref curl);

        // Injection des paramètres finaux dans le tir en attente
        ctrl.OverridePendingLaunchParams(force, angleDeg, curl);

        Debug.Log(string.Format("[AISceneManager] Tir IA ({0} / {1}) → Force={2:F2} | Angle={3:F1}° | Curl={4:F2} | Cible={5}",
                  activeProfile, difficulty, force, angleDeg, curl, targetLabel), this);
    }

    private static GameObject FindClosestPlayerToCenterWithinRange(GameObject centerObj, float maxDistance)
    {
        if (centerObj == null)
            return null;

        GameObject[] players = SafeFindAllWithTag(PlayerTag);
        if (players == null || players.Length == 0)
            return null;

        GameObject closest = null;
        float bestDist = float.MaxValue;

        Vector3 centerPos = centerObj.transform.position;
        foreach (GameObject player in players)
        {
            if (player == null)
                continue;

            Vector3 delta = player.transform.position - centerPos;
            delta.y = 0f;
            float dist = delta.magnitude;

            if (dist > maxDistance)
                continue;

            if (dist < bestDist)
            {
                bestDist = dist;
                closest = player;
            }
        }

        return closest;
    }

    // ── Bruit de difficulté ───────────────────────────────────────────────────

    /// <summary>
    /// Applique un bruit aléatoire aux paramètres de tir selon la difficulté.
    /// </summary>
    private static void ApplyDifficultyNoise(AIDifficulty diff,
                                              ref float force,
                                              ref float angleDeg,
                                              ref float curl)
    {
        float pctForce, pctCurl, pctAngle;
        float fallbackCurlMax, fallbackAngleMax;

        switch (diff)
        {
            case AIDifficulty.Facile:
                pctForce = 0.125f; pctCurl = 0.125f; pctAngle = 0.125f;
                fallbackCurlMax  = 0.15f;
                fallbackAngleMax = 8f;
                break;

            case AIDifficulty.Intermediaire:
                pctForce = 0.085f; pctCurl = 0.085f; pctAngle = 0.085f;
                fallbackCurlMax  = 0.10f;
                fallbackAngleMax = 5f;
                break;

            default: // Difficile
                pctForce = 0.025f; pctCurl = 0.025f; pctAngle = 0.05f;
                fallbackCurlMax  = 0.05f;
                fallbackAngleMax = 3f;
                break;
        }

        // Bruit sur la force (toujours en % de la force calculée)
        force += UnityEngine.Random.Range(-pctForce, pctForce) * force;
        force  = Mathf.Max(0.01f, force);

        // Bruit sur curl et angle
        bool curlAndAngleZero = Mathf.Approximately(curl, 0f) && Mathf.Approximately(angleDeg, 0f);

        if (curlAndAngleZero)
        {
            // Cas spécial : valeurs absolues aléatoires
            curl     = UnityEngine.Random.Range(-fallbackCurlMax,  fallbackCurlMax);
            angleDeg = UnityEngine.Random.Range(-fallbackAngleMax, fallbackAngleMax);
        }
        else
        {
            // Bruit proportionnel (en % de la valeur absolue pour préserver le signe)
            curl     += UnityEngine.Random.Range(-pctCurl,  pctCurl)  * Mathf.Abs(curl);
            angleDeg += UnityEngine.Random.Range(-pctAngle, pctAngle) * Mathf.Abs(angleDeg);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Retourne le premier objet avec le tag donné, ou null si le tag est
    /// inexistant ou qu'aucun objet n'est trouvé.</summary>
    private static GameObject SafeFindWithTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        try   { return GameObject.FindWithTag(tag); }
        catch (UnityException) { return null; }
    }

    /// <summary>Retourne tous les objets avec le tag donné, ou null en cas d'erreur.</summary>
    private static GameObject[] SafeFindAllWithTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        try   { return GameObject.FindGameObjectsWithTag(tag); }
        catch (UnityException) { return null; }
    }

    // ── Détection touche K ────────────────────────────────────────────────────

    private static bool WasKPressedThisFrame()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
            pressed = Keyboard.current.kKey.wasPressedThisFrame;
#endif

        if (pressed) return true;

        try   { pressed = Input.GetKeyDown(KeyCode.K); }
        catch (InvalidOperationException) { pressed = false; }

        return pressed;
    }
}
