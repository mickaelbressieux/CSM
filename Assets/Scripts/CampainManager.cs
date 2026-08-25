using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class InventoryEntry
{
    public string itemId;
    public int quantity = 1;
}

[Serializable]
public class SkillEntry
{
    public string skillId;
    public int level = 1;
}

public class CampainManager : MonoBehaviour
{
    [Header("Persistence")]
    [SerializeField] private bool persistBetweenScenes = true;

    [Header("Personnage - Inventaire")]
    [SerializeField] private List<InventoryEntry> startingInventory = new List<InventoryEntry>();

    [Header("Personnage - Competences")]
    [SerializeField] private List<SkillEntry> startingSkills = new List<SkillEntry>();

    private readonly Dictionary<string, int> characterInventory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> characterSkills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    // Campaign progress. This object outlives the scene, which is the whole point: it is what
    // carries the player's place on the map and which nodes are done across a trip to the
    // curling scene and back.
    private readonly HashSet<string> clearedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool hasReturnPoint;
    private Vector3 returnPlayerPosition;
    private Vector3? returnCameraPosition;

    public static CampainManager Instance { get; private set; }

    public event Action<string, int> OnInventoryChanged;
    public event Action<string, int> OnSkillChanged;
    public event Action<string> OnSceneLoadRequested;

    public SceneTransitionData LastTransitionData { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (persistBetweenScenes)
        {
            DontDestroyOnLoad(gameObject);
        }

        BuildStartingCharacterData();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void BuildStartingCharacterData()
    {
        BuildStartingInventory();
        BuildStartingSkills();
    }

    private void BuildStartingInventory()
    {
        characterInventory.Clear();

        foreach (InventoryEntry entry in startingInventory)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.itemId) || entry.quantity <= 0)
            {
                continue;
            }

            if (characterInventory.ContainsKey(entry.itemId))
            {
                characterInventory[entry.itemId] += entry.quantity;
            }
            else
            {
                characterInventory.Add(entry.itemId, entry.quantity);
            }
        }
    }

    private void BuildStartingSkills()
    {
        characterSkills.Clear();

        foreach (SkillEntry entry in startingSkills)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.skillId) || entry.level <= 0)
            {
                continue;
            }

            if (characterSkills.ContainsKey(entry.skillId))
            {
                characterSkills[entry.skillId] = Mathf.Max(characterSkills[entry.skillId], entry.level);
            }
            else
            {
                characterSkills.Add(entry.skillId, entry.level);
            }
        }
    }

    public int GetItemCount(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return 0;
        }

        return characterInventory.TryGetValue(itemId, out int count) ? count : 0;
    }

    public bool HasItem(string itemId, int amount = 1)
    {
        if (amount <= 0)
        {
            return true;
        }

        return GetItemCount(itemId) >= amount;
    }

    public void AddItem(string itemId, int amount = 1)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
        {
            return;
        }

        if (characterInventory.ContainsKey(itemId))
        {
            characterInventory[itemId] += amount;
        }
        else
        {
            characterInventory.Add(itemId, amount);
        }

        OnInventoryChanged?.Invoke(itemId, characterInventory[itemId]);
    }

    public bool RemoveItem(string itemId, int amount = 1)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0 || !characterInventory.ContainsKey(itemId))
        {
            return false;
        }

        int newAmount = characterInventory[itemId] - amount;
        if (newAmount < 0)
        {
            return false;
        }

        if (newAmount == 0)
        {
            characterInventory.Remove(itemId);
        }
        else
        {
            characterInventory[itemId] = newAmount;
        }

        OnInventoryChanged?.Invoke(itemId, GetItemCount(itemId));
        return true;
    }

    public Dictionary<string, int> GetInventorySnapshot()
    {
        return new Dictionary<string, int>(characterInventory, StringComparer.OrdinalIgnoreCase);
    }

    public void ClearInventory()
    {
        characterInventory.Clear();
    }

    public int GetSkillLevel(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return 0;
        }

        return characterSkills.TryGetValue(skillId, out int level) ? level : 0;
    }

    public void SetSkillLevel(string skillId, int level)
    {
        if (string.IsNullOrWhiteSpace(skillId) || level <= 0)
        {
            return;
        }

        characterSkills[skillId] = level;
        OnSkillChanged?.Invoke(skillId, level);
    }

    public void IncreaseSkillLevel(string skillId, int amount = 1)
    {
        if (string.IsNullOrWhiteSpace(skillId) || amount <= 0)
        {
            return;
        }

        int newLevel = GetSkillLevel(skillId) + amount;
        if (newLevel <= 0)
        {
            newLevel = 1;
        }

        characterSkills[skillId] = newLevel;
        OnSkillChanged?.Invoke(skillId, newLevel);
    }

    public Dictionary<string, int> GetSkillsSnapshot()
    {
        return new Dictionary<string, int>(characterSkills, StringComparer.OrdinalIgnoreCase);
    }

    public Dictionary<string, int> GetCharacterInventorySnapshot()
    {
        return GetInventorySnapshot();
    }

    public Dictionary<string, int> GetCharacterSkillsSnapshot()
    {
        return GetSkillsSnapshot();
    }

    // ------------------------------------------------------------------
    // Campaign progress
    // ------------------------------------------------------------------

    public bool IsNodeCleared(string nodeId)
    {
        return !string.IsNullOrWhiteSpace(nodeId) && clearedNodes.Contains(nodeId);
    }

    public void MarkNodeCleared(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        clearedNodes.Add(nodeId);
    }

    /// <summary>
    /// Remember where the player (and optionally the camera) stood, so leaving the campaign
    /// and coming back puts them exactly where they were rather than at the scene's authored
    /// start. Restored automatically the next time a campaign scene loads.
    /// </summary>
    public void StoreReturnPoint(Vector3 playerPosition, Vector3? cameraPosition = null)
    {
        returnPlayerPosition = playerPosition;
        returnCameraPosition = cameraPosition;
        hasReturnPoint = true;
    }

    public bool LoadScene(string sceneName)
    {
        return LoadScene(sceneName, null);
    }

    public bool LoadScene(string sceneName, SceneTransitionData transitionData)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("CampainManager: sceneName est vide.");
            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning($"CampainManager: la scene '{sceneName}' n'est pas dans le Build Profile actif (ou Shared Scene List).");
            return false;
        }

        if (transitionData != null)
        {
            transitionData.SetSceneNames(SceneManager.GetActiveScene().name, sceneName);
            LastTransitionData = transitionData;
        }
        else
        {
            LastTransitionData = null;
        }

        OnSceneLoadRequested?.Invoke(sceneName);
        SceneManager.LoadScene(sceneName);
        return true;
    }

    public bool LoadSceneAtListIndex(int index)
    {
        if (index < 0 || index >= SceneManager.sceneCountInBuildSettings)
        {
            return false;
        }

        string scenePath = SceneUtility.GetScenePathByBuildIndex(index);
        string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return false;
        }

        return LoadScene(sceneName);
    }

    public bool LoadNextScene()
    {
        int currentIndex = SceneManager.GetActiveScene().buildIndex;
        if (currentIndex < 0)
        {
            Debug.LogWarning("CampainManager: index de la scene actuelle invalide.");
            return false;
        }

        int nextIndex = currentIndex + 1;
        if (nextIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogWarning("CampainManager: aucune scene suivante disponible.");
            return false;
        }

        return LoadSceneAtListIndex(nextIndex);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Before the transition-data dispatch below, which returns early when there is no
        // payload - and the trip back from a match carries none.
        RestoreReturnPoint();

        if (LastTransitionData == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(LastTransitionData.TargetSceneName)
            && !string.Equals(scene.name, LastTransitionData.TargetSceneName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DispatchTransitionData();
    }

    /// <summary>
    /// Put the player back where they left. Presence of a campaign player is the test for
    /// "this is a campaign scene", so the stored point simply waits through the curling scene
    /// instead of needing a scene name configured here.
    /// </summary>
    private void RestoreReturnPoint()
    {
        if (!hasReturnPoint)
        {
            return;
        }

        PlayerMotionCampagne player = FindFirstObjectByType<PlayerMotionCampagne>();
        if (player == null)
        {
            return;
        }

        player.transform.position = returnPlayerPosition;

        if (returnCameraPosition.HasValue && Camera.main != null)
        {
            Camera.main.transform.position = returnCameraPosition.Value;
        }

        hasReturnPoint = false;
    }

    private void DispatchTransitionData()
    {
        MonoBehaviour[] allBehaviours = FindObjectsOfType<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in allBehaviours)
        {
            if (behaviour is ISceneTransitionDataReceiver receiver)
            {
                receiver.ReceiveTransitionData(LastTransitionData);
            }
        }
    }
}
