using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Affiche une conversation ligne par ligne et verrouille les controles du joueur.
/// Ce composant doit rester actif sur le Canvas, en dehors du panneau masque.
/// </summary>
public class DialogueController : MonoBehaviour
{
    [Header("Interface")]
    [SerializeField] GameObject dialoguePanel;
    [SerializeField] TMP_Text speakerText;
    [SerializeField] TMP_Text dialogueText;
    [Tooltip("Texte d'aide affiche pendant une discussion classique.")]
    [SerializeField] TMP_Text continueHintText;
    [FormerlySerializedAs("continueButton")]
    [SerializeField] Button actionButton;
    [SerializeField] TMP_InputField countryNameInput;
    [Tooltip("Texte UI place en dehors de Dialogue Panel et affiche au-dessus du joueur pres d'un personnage.")]
    [SerializeField] TMP_Text interactionPromptText;

    [Header("Affichage")]
    [Min(0f)]
    [SerializeField] float charactersPerSecond = 70f;

    string[] currentLines;
    string currentFullLine;
    int currentLineIndex;
    Coroutine typingRoutine;
    PlayerMotionCampagne activePlayer;
    PlayerCamera playerCamera;
    bool shouldSaveCountryName;
    bool countryInputVisible;
    bool countryNameSavedThisDialogue;
    TMP_Text actionButtonLabel;
    string defaultActionButtonLabel;
    Transform interactionPromptTarget;
    Object interactionPromptOwner;

    public bool IsDialogueActive { get; private set; }
    public const string CountryNamePlayerPrefsKey = "PlayerCountryName";
    public static string SavedCountryName => PlayerPrefs.GetString(CountryNamePlayerPrefsKey, string.Empty);

    void Awake()
    {
        if (dialoguePanel != null && dialoguePanel != gameObject)
            dialoguePanel.SetActive(false);
        if (countryNameInput != null)
            countryNameInput.gameObject.SetActive(false);
        if (interactionPromptText != null)
            interactionPromptText.enabled = false;

        if (dialogueText != null)
            dialogueText.text = string.Empty;
        if (continueHintText != null)
        {
            continueHintText.text = "Press Space or click Continue";
            continueHintText.enabled = false;
        }

        if (actionButton != null)
        {
            actionButtonLabel = actionButton.GetComponentInChildren<TMP_Text>(true);
            defaultActionButtonLabel = actionButtonLabel != null ? actionButtonLabel.text : string.Empty;
            actionButton.onClick.AddListener(HandleActionButton);
            actionButton.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (!IsDialogueActive || countryInputVisible || !AdvancePressedThisFrame())
            return;

        AdvanceDialogue();
    }

    void LateUpdate()
    {
        if (interactionPromptText == null || interactionPromptTarget == null || IsDialogueActive)
            return;

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            interactionPromptText.enabled = false;
            return;
        }

        Vector3 screenPosition = mainCamera.WorldToScreenPoint(
            interactionPromptTarget.position + Vector3.up * 2.5f);
        interactionPromptText.enabled = screenPosition.z > 0f;

        if (interactionPromptText.enabled)
            interactionPromptText.rectTransform.position = screenPosition;
    }

    public void ShowInteractionPrompt(Object owner, Transform playerTransform)
    {
        if (interactionPromptText == null || IsDialogueActive || playerTransform == null)
            return;

        interactionPromptOwner = owner;
        interactionPromptTarget = playerTransform;
        interactionPromptText.text = "Press F to talk";
        interactionPromptText.enabled = true;
    }

    public void HideInteractionPrompt(Object owner = null)
    {
        if (owner != null && interactionPromptOwner != owner)
            return;

        interactionPromptOwner = null;
        interactionPromptTarget = null;

        if (interactionPromptText != null)
            interactionPromptText.enabled = false;
    }

    public void AdvanceDialogue()
    {
        if (!IsDialogueActive || countryInputVisible)
            return;

        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }

        ShowNextLine();
    }

    public bool StartDialogue(string speakerName, string[] lines, PlayerMotionCampagne player, bool saveCountryName = false)
    {
        bool hasText = lines != null && lines.Length > 0;
        if (IsDialogueActive || player == null || (!hasText && !saveCountryName))
            return false;

        if (saveCountryName &&
            (countryNameInput == null || actionButton == null))
        {
            Debug.LogError(
                "DialogueController: Country Name Input et Action Button doivent etre renseignes pour sauvegarder le pays.",
                this);
            return false;
        }

        activePlayer = player;
        playerCamera = FindFirstObjectByType<PlayerCamera>();
        currentLines = lines ?? new string[0];
        currentLineIndex = -1;
        shouldSaveCountryName = saveCountryName;
        countryInputVisible = false;
        countryNameSavedThisDialogue = false;
        IsDialogueActive = true;
        HideInteractionPrompt();

        activePlayer.SetMovementEnabled(false);
        if (playerCamera != null)
            playerCamera.SetControlsEnabled(false);

        if (speakerText != null)
            speakerText.text = speakerName;
        if (continueHintText != null)
        {
            continueHintText.text = "Press Space or click Continue";
            continueHintText.enabled = true;
        }
        if (actionButton != null)
        {
            actionButton.gameObject.SetActive(true);
            SetActionButtonLabel(defaultActionButtonLabel);
        }
        if (countryNameInput != null)
            countryNameInput.gameObject.SetActive(false);
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        ShowNextLine();
        return true;
    }

    public void CloseDialogue()
    {
        if (!IsDialogueActive)
            return;

        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }

        IsDialogueActive = false;
        currentLines = null;
        currentFullLine = string.Empty;
        shouldSaveCountryName = false;
        countryInputVisible = false;
        countryNameSavedThisDialogue = false;

        if (dialogueText != null)
            dialogueText.text = string.Empty;
        if (continueHintText != null)
            continueHintText.enabled = false;
        if (actionButton != null)
        {
            actionButton.gameObject.SetActive(false);
            SetActionButtonLabel(defaultActionButtonLabel);
        }
        if (countryNameInput != null)
            countryNameInput.gameObject.SetActive(false);
        if (dialoguePanel != null && dialoguePanel != gameObject)
            dialoguePanel.SetActive(false);

        if (activePlayer != null)
            activePlayer.SetMovementEnabled(true);
        if (playerCamera != null)
            playerCamera.SetControlsEnabled(true);

        activePlayer = null;
        playerCamera = null;
    }

    void ShowNextLine()
    {
        currentLineIndex++;
        if (currentLines == null || currentLineIndex >= currentLines.Length)
        {
            if (shouldSaveCountryName && !countryNameSavedThisDialogue)
                ShowCountryInput(false);
            else
                CloseDialogue();
            return;
        }

        currentFullLine = currentLines[currentLineIndex] ?? string.Empty;

        if (typingRoutine != null)
            StopCoroutine(typingRoutine);
        typingRoutine = StartCoroutine(TypeCurrentLine());

        // En mode pays, le champ apparait en meme temps que le premier texte.
        if (shouldSaveCountryName && currentLineIndex == 0 && !countryNameSavedThisDialogue)
            ShowCountryInput(true);
    }

    void ShowCountryInput(bool preserveDialogueText)
    {
        countryInputVisible = true;

        if (!preserveDialogueText && dialogueText != null)
            dialogueText.text = "Enter the name of your country:";
        if (continueHintText != null)
        {
            continueHintText.text = "Enter a country name, then click Confirm";
            continueHintText.enabled = true;
        }
        if (actionButton != null)
        {
            actionButton.gameObject.SetActive(true);
            SetActionButtonLabel("Confirm");
        }

        if (countryNameInput != null)
        {
            countryNameInput.gameObject.SetActive(true);
            countryNameInput.text = SavedCountryName;
            countryNameInput.Select();
            countryNameInput.ActivateInputField();
        }
    }

    public void ConfirmCountryName()
    {
        if (!IsDialogueActive || !countryInputVisible || countryNameInput == null)
            return;

        string countryName = countryNameInput.text.Trim();
        if (string.IsNullOrEmpty(countryName))
        {
            if (dialogueText != null)
                dialogueText.text = "Please enter a country name.";

            countryNameInput.Select();
            countryNameInput.ActivateInputField();
            return;
        }

        PlayerPrefs.SetString(CountryNamePlayerPrefsKey, countryName);
        PlayerPrefs.Save();
        countryNameSavedThisDialogue = true;
        countryInputVisible = false;
        countryNameInput.gameObject.SetActive(false);

        if (continueHintText != null)
        {
            continueHintText.text = "Press Space or click Continue";
            continueHintText.enabled = true;
        }
        SetActionButtonLabel(defaultActionButtonLabel);

        ShowNextLine();
    }

    void HandleActionButton()
    {
        if (countryInputVisible)
            ConfirmCountryName();
        else
            AdvanceDialogue();
    }

    void SetActionButtonLabel(string value)
    {
        if (actionButtonLabel != null)
            actionButtonLabel.text = value;
    }

    IEnumerator TypeCurrentLine()
    {
        if (dialogueText == null)
        {
            typingRoutine = null;
            yield break;
        }

        dialogueText.text = string.Empty;

        if (charactersPerSecond <= 0f)
        {
            dialogueText.text = currentFullLine;
            typingRoutine = null;
            yield break;
        }

        float delay = 1f / charactersPerSecond;
        for (int i = 1; i <= currentFullLine.Length; i++)
        {
            dialogueText.text = currentFullLine.Substring(0, i);
            yield return new WaitForSecondsRealtime(delay);
        }

        typingRoutine = null;
    }

    bool AdvancePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        Keyboard keyboard = Keyboard.current;
        return keyboard != null &&
               (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
#endif
    }

    void OnDestroy()
    {
        if (actionButton != null)
            actionButton.onClick.RemoveListener(HandleActionButton);

        if (activePlayer != null)
            activePlayer.SetMovementEnabled(true);
        if (playerCamera != null)
            playerCamera.SetControlsEnabled(true);
    }
}
