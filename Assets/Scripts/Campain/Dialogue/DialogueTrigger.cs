using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Zone d'interaction a placer sur un PNJ. Le dialogue demarre avec F lorsque
/// l'un des colliders du joueur se trouve dans la zone.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DialogueTrigger : MonoBehaviour
{
    [SerializeField] DialogueController dialogueController;
    [SerializeField] string speakerName = "PNJ";
    [TextArea(2, 5)]
    [SerializeField] string[] lines;
    [Tooltip("Image affichee dans l'emplacement Portrait du Canvas pendant cette discussion.")]
    [SerializeField] Sprite portrait;
    [SerializeField] bool startAutomatically;
    [Tooltip("A la fin des textes, demande et sauvegarde le nom du pays du joueur.")]
    [SerializeField] bool saveCountryName;

    readonly HashSet<Collider> playerCollidersInside = new HashSet<Collider>();
    PlayerMotionCampagne nearbyPlayer;
    bool automaticInteractionConsumed;

    void Awake()
    {
        if (dialogueController == null)
            dialogueController = FindFirstObjectByType<DialogueController>();
    }

    void Update()
    {
        if (automaticInteractionConsumed || nearbyPlayer == null ||
            dialogueController == null || dialogueController.IsDialogueActive)
            return;

        dialogueController.ShowInteractionPrompt(this, nearbyPlayer.transform);

        if (InteractionPressedThisFrame())
            StartDialogue();
    }

    void OnTriggerEnter(Collider other)
    {
        if (automaticInteractionConsumed)
            return;

        PlayerMotionCampagne player = other.GetComponentInParent<PlayerMotionCampagne>();
        if (player == null)
            return;

        playerCollidersInside.Add(other);
        nearbyPlayer = player;

        if (startAutomatically && dialogueController != null && !dialogueController.IsDialogueActive)
            StartDialogue();
    }

    void OnTriggerExit(Collider other)
    {
        if (!playerCollidersInside.Remove(other))
            return;

        if (playerCollidersInside.Count == 0)
        {
            nearbyPlayer = null;
            if (dialogueController != null)
                dialogueController.HideInteractionPrompt(this);
        }
    }

    void StartDialogue()
    {
        if (automaticInteractionConsumed || dialogueController == null || nearbyPlayer == null)
            return;

        bool dialogueStarted = dialogueController.StartDialogue(
            new DialogueRequest
            {
                speakerName = speakerName,
                lines = lines,
                portrait = portrait,
                saveCountryName = saveCountryName
            },
            nearbyPlayer,
            null);

        if (!dialogueStarted || !startAutomatically)
            return;

        automaticInteractionConsumed = true;
        playerCollidersInside.Clear();
        nearbyPlayer = null;
        dialogueController.HideInteractionPrompt(this);

        Collider interactionCollider = GetComponent<Collider>();
        if (interactionCollider != null)
            interactionCollider.enabled = false;

        enabled = false;
    }

    bool InteractionPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.fKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F);
#endif
    }

    void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        trigger.isTrigger = true;
    }

    void OnDisable()
    {
        if (dialogueController != null)
            dialogueController.HideInteractionPrompt(this);
    }
}
