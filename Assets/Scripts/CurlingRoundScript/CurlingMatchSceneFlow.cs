using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gere la sortie d'un match de curling : reprise de la campagne mise en pause
/// en cas de victoire, ou rechargement complet de la campagne en cas de defaite.
/// </summary>
[DisallowMultipleComponent]
public class CurlingMatchSceneFlow : MonoBehaviour, ISceneTransitionDataReceiver
{
    [Tooltip("Scene utilisee si le match a ete lance directement sans donnees de transition.")]
    [SerializeField] string fallbackReturnScene = "CampagneMap";
    [Min(0f)]
    [SerializeField] float resultDisplayDuration = 2f;

    SceneTransitionData matchTransitionData;
    string returnSceneName;
    bool flowStarted;

    void OnEnable()
    {
        MatchEvents.MatchCompleted += OnMatchCompleted;
    }

    void Start()
    {
        CampainManager manager = CampainManager.Instance;
        if (matchTransitionData == null && manager != null && manager.LastTransitionData != null)
            ReceiveTransitionData(manager.LastTransitionData);
    }

    void OnDisable()
    {
        MatchEvents.MatchCompleted -= OnMatchCompleted;
    }

    public void ReceiveTransitionData(SceneTransitionData data)
    {
        if (data == null)
            return;

        matchTransitionData = data;

        if (!data.TryGetString(SceneTransitionDataKeys.MatchReturnScene, out returnSceneName) ||
            string.IsNullOrWhiteSpace(returnSceneName))
        {
            returnSceneName = data.SourceSceneName;
            data.SetString(SceneTransitionDataKeys.MatchReturnScene, returnSceneName);
        }
    }

    void OnMatchCompleted(bool playerWon, string result)
    {
        if (flowStarted)
            return;

        Debug.Log(
            $"CurlingMatchSceneFlow: resultat='{result}', playerWon={playerWon}, " +
            $"sceneRetour='{(string.IsNullOrWhiteSpace(returnSceneName) ? fallbackReturnScene : returnSceneName)}'.",
            this);

        flowStarted = true;
        StartCoroutine(ChangeSceneAfterResult(playerWon));
    }

    IEnumerator ChangeSceneAfterResult(bool playerWon)
    {
        if (resultDisplayDuration > 0f)
            yield return new WaitForSecondsRealtime(resultDisplayDuration);

        if (playerWon)
            ReturnToInitialScene();
        else
            ReturnAfterDefeat();
    }

    void ReturnToInitialScene()
    {
        CampainManager manager = CampainManager.Instance;
        if (manager != null && manager.HasPausedScene)
        {
            Debug.Log("CurlingMatchSceneFlow: victoire, reprise de la campagne mise en pause.", this);
            if (!manager.ResumePausedScene())
                flowStarted = false;
            return;
        }

        string targetScene = string.IsNullOrWhiteSpace(returnSceneName)
            ? fallbackReturnScene
            : returnSceneName;

        Debug.Log($"CurlingMatchSceneFlow: victoire, retour vers '{targetScene}'.", this);
        LoadScene(targetScene, null);
    }

    void ReturnAfterDefeat()
    {
        CampainManager manager = CampainManager.Instance;
        if (manager != null && manager.HasPausedScene)
        {
            Debug.Log("CurlingMatchSceneFlow: defaite, rechargement complet de la campagne.", this);
            if (!manager.ResetPausedScene())
                flowStarted = false;
            return;
        }

        Debug.Log($"CurlingMatchSceneFlow: defaite, chargement de '{fallbackReturnScene}'.", this);
        LoadScene(fallbackReturnScene, null);
    }

    void LoadScene(string sceneName, SceneTransitionData transitionData)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("CurlingMatchSceneFlow: aucune scene cible n'est configuree.", this);
            flowStarted = false;
            return;
        }

        CampainManager manager = CampainManager.Instance;
        if (manager != null)
        {
            if (!manager.LoadScene(sceneName, transitionData))
                flowStarted = false;
            return;
        }

        if (Application.CanStreamedLevelBeLoaded(sceneName))
            SceneManager.LoadScene(sceneName);
        else
        {
            Debug.LogError($"CurlingMatchSceneFlow: scene '{sceneName}' introuvable dans le Build Profile.", this);
            flowStarted = false;
        }
    }
}
