using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

[Serializable]
public class DialogueChoice
{
    public string id;
    public string label;

    public DialogueChoice() { }

    public DialogueChoice(string id, string label)
    {
        this.id = id;
        this.label = label;
    }
}

[Serializable]
public class DialogueRequest
{
    public string speakerName = "PNJ";
    [TextArea(2, 5)] public string[] lines;
    public Sprite portrait;
    public bool saveCountryName;
    public DialogueChoice[] choices;
}

public readonly struct DialogueResult
{
    public string SelectedChoiceId { get; }
    public bool HasChoice => !string.IsNullOrWhiteSpace(SelectedChoiceId);

    public DialogueResult(string selectedChoiceId)
    {
        SelectedChoiceId = selectedChoiceId;
    }
}

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
    [Tooltip("Emplacement UI du portrait. Sa position et sa taille se reglent directement dans le Canvas.")]
    [SerializeField] Image portraitImage;
    [Tooltip("Texte d'aide affiche pendant une discussion classique.")]
    [SerializeField] TMP_Text continueHintText;
    [FormerlySerializedAs("continueButton")]
    [SerializeField] Button actionButton;
    [Tooltip("Bouton du premier choix. S'il est vide, une copie du bouton d'action est creee au lancement.")]
    [SerializeField] Button firstChoiceButton;
    [Tooltip("Bouton du second choix. S'il est vide, une copie du bouton d'action est creee au lancement.")]
    [SerializeField] Button secondChoiceButton;
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
    UnityEngine.Object interactionPromptOwner;
    DialogueChoice[] currentChoices;
    Action<DialogueResult> completionCallback;
    bool choicesVisible;

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
        SetPortrait(null);
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

        EnsureChoiceButtons();
        SetChoiceButtonsVisible(false);
    }

    void Update()
    {
        if (!IsDialogueActive || countryInputVisible || choicesVisible || !AdvancePressedThisFrame())
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

    public void ShowInteractionPrompt(UnityEngine.Object owner, Transform playerTransform)
    {
        if (interactionPromptText == null || IsDialogueActive || playerTransform == null)
            return;

        interactionPromptOwner = owner;
        interactionPromptTarget = playerTransform;
        interactionPromptText.text = "Press F to talk";
        interactionPromptText.enabled = true;
    }

    public void HideInteractionPrompt(UnityEngine.Object owner = null)
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
        if (!IsDialogueActive || countryInputVisible || choicesVisible)
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
        return StartDialogue(new DialogueRequest
        {
            speakerName = speakerName,
            lines = lines,
            saveCountryName = saveCountryName
        }, player, null);
    }

    public bool StartDialogue(
        DialogueRequest request,
        PlayerMotionCampagne player,
        Action<DialogueResult> onFinished)
    {
        if (request == null)
            return false;

        bool hasText = request.lines != null && request.lines.Length > 0;
        bool hasChoices = request.choices != null && request.choices.Length > 0;
        if (IsDialogueActive || player == null || (!hasText && !request.saveCountryName && !hasChoices))
            return false;

        if (request.saveCountryName &&
            (countryNameInput == null || actionButton == null))
        {
            Debug.LogError(
                "DialogueController: Country Name Input et Action Button doivent etre renseignes pour sauvegarder le pays.",
                this);
            return false;
        }

        activePlayer = player;
        playerCamera = FindFirstObjectByType<PlayerCamera>();
        currentLines = request.lines ?? Array.Empty<string>();
        currentLineIndex = -1;
        shouldSaveCountryName = request.saveCountryName;
        currentChoices = request.choices;
        completionCallback = onFinished;
        choicesVisible = false;
        countryInputVisible = false;
        countryNameSavedThisDialogue = false;
        IsDialogueActive = true;
        HideInteractionPrompt();

        activePlayer.SetMovementEnabled(false);
        if (playerCamera != null)
            playerCamera.SetControlsEnabled(false);

        if (speakerText != null)
            speakerText.text = request.speakerName;
        SetPortrait(request.portrait);
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
        SetChoiceButtonsVisible(false);
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        ShowNextLine();
        return true;
    }

    public void CloseDialogue()
    {
        CompleteDialogue(new DialogueResult(null));
    }

    void CompleteDialogue(DialogueResult result)
    {
        if (!IsDialogueActive)
            return;

        Action<DialogueResult> callback = completionCallback;

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
        choicesVisible = false;
        currentChoices = null;
        completionCallback = null;

        if (dialogueText != null)
            dialogueText.text = string.Empty;
        SetPortrait(null);
        if (continueHintText != null)
            continueHintText.enabled = false;
        if (actionButton != null)
        {
            actionButton.gameObject.SetActive(false);
            SetActionButtonLabel(defaultActionButtonLabel);
        }
        if (countryNameInput != null)
            countryNameInput.gameObject.SetActive(false);
        SetChoiceButtonsVisible(false);
        if (dialoguePanel != null && dialoguePanel != gameObject)
            dialoguePanel.SetActive(false);

        if (activePlayer != null)
            activePlayer.SetMovementEnabled(true);
        if (playerCamera != null)
            playerCamera.SetControlsEnabled(true);

        activePlayer = null;
        playerCamera = null;

        callback?.Invoke(result);
    }

    void ShowNextLine()
    {
        currentLineIndex++;
        if (currentLines == null || currentLineIndex >= currentLines.Length)
        {
            if (shouldSaveCountryName && !countryNameSavedThisDialogue)
                ShowCountryInput(false);
            else if (currentChoices != null && currentChoices.Length > 0)
                ShowChoices();
            else
                CompleteDialogue(new DialogueResult(null));
            return;
        }

        currentFullLine = currentLines[currentLineIndex] ?? string.Empty;

        if (typingRoutine != null)
            StopCoroutine(typingRoutine);
        typingRoutine = StartCoroutine(TypeCurrentLine());

        // Les actions liees au dialogue sont proposees avec la derniere replique :
        // le joueur n'a pas a cliquer une derniere fois sur Continuer pour les voir.
        bool waitingForCountryName = shouldSaveCountryName && !countryNameSavedThisDialogue;
        if (!waitingForCountryName && currentChoices != null && currentChoices.Length > 0 &&
            currentLineIndex == currentLines.Length - 1)
        {
            ShowChoices();
        }

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

    void ShowChoices()
    {
        EnsureChoiceButtons();
        bool needsSecondButton = currentChoices.Length > 1;
        if (firstChoiceButton == null || (needsSecondButton && secondChoiceButton == null))
        {
            Debug.LogError("DialogueController: les boutons requis pour afficher les choix sont absents.", this);
            CompleteDialogue(new DialogueResult(null));
            return;
        }

        choicesVisible = true;
        if (continueHintText != null)
        {
            continueHintText.text = "Choose an answer";
            continueHintText.enabled = true;
        }
        if (actionButton != null)
            actionButton.gameObject.SetActive(false);

        ConfigureChoiceButton(firstChoiceButton, currentChoices[0], 0);
        firstChoiceButton.gameObject.SetActive(true);

        bool hasSecondChoice = needsSecondButton && secondChoiceButton != null;
        if (hasSecondChoice)
            ConfigureChoiceButton(secondChoiceButton, currentChoices[1], 1);
        if (secondChoiceButton != null)
            secondChoiceButton.gameObject.SetActive(hasSecondChoice);
    }

    void ConfigureChoiceButton(Button button, DialogueChoice choice, int index)
    {
        button.onClick.RemoveAllListeners();
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = string.IsNullOrWhiteSpace(choice?.label) ? $"Choice {index + 1}" : choice.label;

        string choiceId = choice?.id ?? string.Empty;
        button.onClick.AddListener(() => HandleChoice(choiceId));
    }

    void HandleChoice(string choiceId)
    {
        if (!IsDialogueActive || !choicesVisible)
            return;

        CompleteDialogue(new DialogueResult(choiceId));
    }

    void EnsureChoiceButtons()
    {
        if (actionButton == null)
            return;

        if (firstChoiceButton == null)
            firstChoiceButton = CreateChoiceButton("First Choice", -1f);
        if (secondChoiceButton == null)
            secondChoiceButton = CreateChoiceButton("Second Choice", 1f);
    }

    Button CreateChoiceButton(string objectName, float horizontalDirection)
    {
        Button copy = Instantiate(actionButton, actionButton.transform.parent);
        copy.name = objectName;
        copy.onClick.RemoveAllListeners();

        RectTransform templateRect = actionButton.transform as RectTransform;
        RectTransform copyRect = copy.transform as RectTransform;
        if (templateRect != null && copyRect != null)
        {
            float spacing = Mathf.Max(20f, templateRect.rect.width * 0.6f);
            copyRect.anchoredPosition = templateRect.anchoredPosition + Vector2.right * horizontalDirection * spacing;
        }

        return copy;
    }

    void SetChoiceButtonsVisible(bool visible)
    {
        if (firstChoiceButton != null)
            firstChoiceButton.gameObject.SetActive(visible);
        if (secondChoiceButton != null)
            secondChoiceButton.gameObject.SetActive(visible);
    }

    void SetActionButtonLabel(string value)
    {
        if (actionButtonLabel != null)
            actionButtonLabel.text = value;
    }

    void SetPortrait(Sprite portrait)
    {
        if (portraitImage == null)
            return;

        portraitImage.sprite = portrait;
        portraitImage.gameObject.SetActive(portrait != null);
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
        if (firstChoiceButton != null)
            firstChoiceButton.onClick.RemoveAllListeners();
        if (secondChoiceButton != null)
            secondChoiceButton.onClick.RemoveAllListeners();

        if (activePlayer != null)
            activePlayer.SetMovementEnabled(true);
        if (playerCamera != null)
            playerCamera.SetControlsEnabled(true);
    }
}
