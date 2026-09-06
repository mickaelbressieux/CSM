using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class SceneTransitionData
{
    public string SourceSceneName { get; private set; }
    public string TargetSceneName { get; private set; }

    private readonly Dictionary<string, int> intValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> floatValues = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> boolValues = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> stringValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UnityEngine.Object> objectValues = new Dictionary<string, UnityEngine.Object>(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> InventorySnapshot { get; private set; }

    public IReadOnlyDictionary<string, int> IntValues => new ReadOnlyDictionary<string, int>(intValues);
    public IReadOnlyDictionary<string, float> FloatValues => new ReadOnlyDictionary<string, float>(floatValues);
    public IReadOnlyDictionary<string, bool> BoolValues => new ReadOnlyDictionary<string, bool>(boolValues);
    public IReadOnlyDictionary<string, string> StringValues => new ReadOnlyDictionary<string, string>(stringValues);
    public IReadOnlyDictionary<string, UnityEngine.Object> ObjectValues => new ReadOnlyDictionary<string, UnityEngine.Object>(objectValues);

    public void SetSceneNames(string sourceSceneName, string targetSceneName)
    {
        SourceSceneName = sourceSceneName;
        TargetSceneName = targetSceneName;
    }

    public void SetInt(string key, int value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        intValues[key] = value;
    }

    public void SetFloat(string key, float value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        floatValues[key] = value;
    }

    public void SetBool(string key, bool value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        boolValues[key] = value;
    }

    public void SetString(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        stringValues[key] = value ?? string.Empty;
    }

    public void SetObject(string key, UnityEngine.Object value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        objectValues[key] = value;
    }

    public void SetInventorySnapshot(Dictionary<string, int> inventory)
    {
        if (inventory == null)
        {
            InventorySnapshot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        InventorySnapshot = new Dictionary<string, int>(inventory, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetInt(string key, out int value)
    {
        return intValues.TryGetValue(key, out value);
    }

    public bool TryGetFloat(string key, out float value)
    {
        return floatValues.TryGetValue(key, out value);
    }

    public bool TryGetBool(string key, out bool value)
    {
        return boolValues.TryGetValue(key, out value);
    }

    public bool TryGetString(string key, out string value)
    {
        return stringValues.TryGetValue(key, out value);
    }

    public bool TryGetObject<T>(string key, out T value) where T : UnityEngine.Object
    {
        value = null;
        if (!objectValues.TryGetValue(key, out UnityEngine.Object storedValue))
        {
            return false;
        }

        value = storedValue as T;
        return value != null;
    }
}

public static class SceneTransitionDataKeys
{
    public const string EnemyType = "enemyType";
    public const string MapType = "mapType";
    public const string CurlingMatchMode = "curlingMatchMode";
    public const string CurlingEnemyProfile = "curlingEnemyProfile";
    public const string CurlingPlayerStoneCount = "curlingPlayerStoneCount";
    public const string MatchReturnScene = "matchReturnScene";
}

public interface ISceneTransitionDataReceiver
{
    void ReceiveTransitionData(SceneTransitionData data);
}

public class SceneTransitionTrigger : MonoBehaviour
{
    [Header("Transition")]
#if UNITY_EDITOR
    [SerializeField] private SceneAsset targetSceneAsset;
#endif
    [SerializeField] private string targetSceneName;
    [Tooltip("Compatibilite avec les anciennes zones: charge directement la scene au contact. Pour une rencontre, laisser decoche et utiliser CurlingEncounter.")]
    [SerializeField] private bool loadDirectlyOnTrigger;
    [SerializeField] private bool triggerOnlyOnce = true;
    [SerializeField] private GameObject designatedObject;
    [SerializeField] private bool debugLogs = true;

    [Header("Data To Send")]
    [SerializeField] private string enemyType = "DefaultEnemy";
    [SerializeField] private string mapType = "DefaultMap";

    [Header("Curling Match")]
    [SerializeField] private bool openInMatchMode = true;
    [SerializeField] private AIOpponentProfile enemyProfile;
    [SerializeField, Min(1)] private int playerStoneCount = 3;

    private bool hasTriggered;

    public AIOpponentProfile EnemyProfile => enemyProfile;

    private void Awake()
    {
        // Migration des anciennes zones de combat : elles deviennent des rencontres volontaires
        // sans imposer de modification manuelle des scenes existantes. Un CurlingEncounter deja
        // configure dans l'Inspector (par exemple en mode Ambush) reste naturellement prioritaire.
        if (!loadDirectlyOnTrigger && GetComponent<CurlingEncounter>() == null)
            gameObject.AddComponent<CurlingEncounter>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        playerStoneCount = Mathf.Max(1, playerStoneCount);

        if (targetSceneAsset != null)
        {
            targetSceneName = targetSceneAsset.name;
            return;
        }

        // Keep a manual fallback if no scene asset is assigned.
        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            targetSceneName = string.Empty;
        }
    }
#endif

    private void OnTriggerEnter(Collider other)
    {
        if (!loadDirectlyOnTrigger)
            return;

        if (debugLogs)
        {
            Debug.Log($"SceneTransitionTrigger '{name}': OnTriggerEnter par '{other.gameObject.name}'.");
        }

        TryTrigger(other.gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!loadDirectlyOnTrigger)
            return;

        if (debugLogs)
        {
            Debug.Log($"SceneTransitionTrigger '{name}': OnTriggerEnter2D par '{other.gameObject.name}'.");
        }

        TryTrigger(other.gameObject);
    }

    private void TryTrigger(GameObject other)
    {
        if (hasTriggered && triggerOnlyOnce)
        {
            if (debugLogs)
            {
                Debug.Log($"SceneTransitionTrigger '{name}': deja declenche (triggerOnlyOnce actif).");
            }

            return;
        }

        if (!MatchesTriggerObject(other))
        {
            if (debugLogs)
            {
                Debug.Log($"SceneTransitionTrigger '{name}': objet '{other.name}' ignore (pas l'objet designe).", other);
            }

            return;
        }

        StartTransition();
    }

    private bool MatchesTriggerObject(GameObject other)
    {
        if (designatedObject == null)
        {
            Debug.LogWarning("SceneTransitionTrigger: designatedObject n'est pas assigne.");
            return false;
        }

        Transform otherRoot = other.transform.root;
        Transform designatedRoot = designatedObject.transform.root;
        if (debugLogs)
        {
            Debug.Log($"SceneTransitionTrigger '{name}': comparaison root entrant '{otherRoot.name}' vs root designe '{designatedRoot.name}'.");
        }

        return otherRoot == designatedRoot;
    }

    private SceneTransitionData BuildTransitionData(CampainManager manager)
    {
        SceneTransitionData data = new SceneTransitionData();

        data.SetString(SceneTransitionDataKeys.EnemyType, enemyType);
        data.SetString(SceneTransitionDataKeys.MapType, mapType);
        data.SetBool(SceneTransitionDataKeys.CurlingMatchMode, openInMatchMode);
        data.SetObject(SceneTransitionDataKeys.CurlingEnemyProfile, enemyProfile);
        data.SetInt(SceneTransitionDataKeys.CurlingPlayerStoneCount, Mathf.Max(1, playerStoneCount));
        // Memorise la carte avant de quitter la scene. La scene du match ne doit
        // jamais devenir par erreur sa propre scene de retour.
        data.SetString(SceneTransitionDataKeys.MatchReturnScene, SceneManager.GetActiveScene().name);
        data.SetInventorySnapshot(manager.GetCharacterInventorySnapshot());

        return data;
    }

    /// <summary>Lance explicitement le match apres la resolution d'une rencontre ou d'un dialogue.</summary>
    public bool StartTransition()
    {
        if (hasTriggered && triggerOnlyOnce)
            return false;

        CampainManager manager = CampainManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("SceneTransitionTrigger: CampainManager introuvable.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogWarning("SceneTransitionTrigger: targetSceneName est vide.");
            return false;
        }

        if (debugLogs)
            Debug.Log($"SceneTransitionTrigger '{name}': tentative de chargement de la scene '{targetSceneName}'.");

        SceneTransitionData data = BuildTransitionData(manager);
        bool started = manager.LoadSceneWithPausedSource(targetSceneName, data);
        if (started)
        {
            hasTriggered = true;
            if (debugLogs)
                Debug.Log($"SceneTransitionTrigger '{name}': chargement de scene lance avec succes.");
        }
        else if (debugLogs)
        {
            Debug.LogWarning($"SceneTransitionTrigger '{name}': echec du chargement de scene '{targetSceneName}'. Regarde les warnings CampainManager.");
        }

        return started;
    }
}
