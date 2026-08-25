using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The main menu's four buttons. Listeners are wired in code rather than through the
/// inspector's UnityEvent list, so the references you see in the Inspector are the whole
/// story and there is no second place for the wiring to drift out of sync.
///
/// New Game loads the campaign through <see cref="SceneManager"/> directly, not through
/// <see cref="CampainManager"/>: that singleton does not exist yet at menu time. It wakes up
/// inside the campaign scene and owns every transition from then on.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Scenes")]
    [Tooltip("Scene loaded by New Game. Must be in the Build Profile's scene list.")]
    [SerializeField] private string campaignSceneName = "CampagneMap";

    [Header("Buttons")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button loadGameButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button exitButton;

    [Header("Panels")]
    [SerializeField] private GameObject optionsPanel;

    private void Awake()
    {
        Bind(newGameButton, StartNewGame);
        Bind(loadGameButton, LoadGame);
        Bind(optionsButton, OpenOptions);
        Bind(exitButton, ExitGame);

        if (optionsPanel != null)
        {
            optionsPanel.SetActive(false);
        }
    }

    private void Start()
    {
        // There is no save system yet, so this greys the button out. Implement
        // SaveSystem.HasSave() and it lights up on its own.
        if (loadGameButton != null)
        {
            loadGameButton.interactable = SaveSystem.HasSave();
        }
    }

    public void StartNewGame()
    {
        if (string.IsNullOrWhiteSpace(campaignSceneName))
        {
            Debug.LogError($"{nameof(MainMenuController)}: campaignSceneName is empty.", this);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(campaignSceneName))
        {
            Debug.LogError(
                $"{nameof(MainMenuController)}: scene '{campaignSceneName}' is not in the active " +
                "Build Profile's scene list.", this);
            return;
        }

        SceneManager.LoadScene(campaignSceneName);
    }

    public void LoadGame()
    {
        if (!SaveSystem.Load())
        {
            return;
        }

        StartNewGame();
    }

    public void OpenOptions()
    {
        if (optionsPanel != null)
        {
            optionsPanel.SetActive(true);
        }
    }

    public void CloseOptions()
    {
        if (optionsPanel != null)
        {
            optionsPanel.SetActive(false);
        }
    }

    public void ExitGame()
    {
        GameSettings.Flush();

#if UNITY_EDITOR
        // Application.Quit() does nothing in the editor, so the button would look broken.
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            Debug.LogWarning($"{nameof(MainMenuController)}: a button reference is missing.", this);
            return;
        }

        button.onClick.AddListener(action);
    }
}
