using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
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

    public Dictionary<string, int> InventorySnapshot { get; private set; }

    public IReadOnlyDictionary<string, int> IntValues => new ReadOnlyDictionary<string, int>(intValues);
    public IReadOnlyDictionary<string, float> FloatValues => new ReadOnlyDictionary<string, float>(floatValues);
    public IReadOnlyDictionary<string, bool> BoolValues => new ReadOnlyDictionary<string, bool>(boolValues);
    public IReadOnlyDictionary<string, string> StringValues => new ReadOnlyDictionary<string, string>(stringValues);

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
}

public interface ISceneTransitionDataReceiver
{
    void ReceiveTransitionData(SceneTransitionData data);
}

public class SceneTransitionTrigger : MonoBehaviour
{
    private const string EnemyTypeKey = "enemyType";
    private const string MapTypeKey = "mapType";

    [Header("Transition")]
#if UNITY_EDITOR
    [SerializeField] private SceneAsset targetSceneAsset;
#endif
    [SerializeField] private string targetSceneName;
    [SerializeField] private bool triggerOnlyOnce = true;
    [SerializeField] private GameObject designatedObject;
    [SerializeField] private bool debugLogs = true;

    [Header("Data To Send")]
    [SerializeField] private string enemyType = "DefaultEnemy";
    [SerializeField] private string mapType = "DefaultMap";

    private bool hasTriggered;

#if UNITY_EDITOR
    private void OnValidate()
    {
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
        if (debugLogs)
        {
            Debug.Log($"SceneTransitionTrigger '{name}': OnTriggerEnter par '{other.gameObject.name}'.");
        }

        TryTrigger(other.gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
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

        CampainManager manager = CampainManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("SceneTransitionTrigger: CampainManager introuvable.");
            return;
        }

        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogWarning("SceneTransitionTrigger: targetSceneName est vide.");
            return;
        }

        if (debugLogs)
        {
            Debug.Log($"SceneTransitionTrigger '{name}': tentative de chargement de la scene '{targetSceneName}'.");
        }

        SceneTransitionData data = BuildTransitionData(manager);
        if (manager.LoadScene(targetSceneName, data))
        {
            hasTriggered = true;
            if (debugLogs)
            {
                Debug.Log($"SceneTransitionTrigger '{name}': chargement de scene lance avec succes.");
            }
        }
        else if (debugLogs)
        {
            Debug.LogWarning($"SceneTransitionTrigger '{name}': echec du chargement de scene '{targetSceneName}'. Regarde les warnings CampainManager.");
        }
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

        data.SetString(EnemyTypeKey, enemyType);
        data.SetString(MapTypeKey, mapType);
        data.SetInventorySnapshot(manager.GetCharacterInventorySnapshot());

        return data;
    }
}
