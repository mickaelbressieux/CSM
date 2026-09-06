using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

public enum CurlingEncounterMode
{
    Voluntary,
    Ambush
}

/// <summary>
/// Orchestre une rencontre de campagne sans melanger le dialogue, le deplacement du PNJ et le
/// chargement du match. En mode volontaire, F ouvre un choix. En embuscade, l'ennemi approche,
/// parle, puis propose uniquement le combat.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CurlingEncounter : MonoBehaviour
{
    const string FightChoiceId = "fight";
    const string LeaveChoiceId = "leave";

    [Header("Rencontre")]
    [SerializeField] CurlingEncounterMode mode = CurlingEncounterMode.Voluntary;
    [SerializeField] DialogueController dialogueController;
    [SerializeField] SceneTransitionTrigger battleStarter;
    [Tooltip("Necessaire uniquement en mode Ambush. Les enfants suivent les racines configurees dans ce composant.")]
    [SerializeField] EnemyApproachController approachController;

    [Header("Dialogue")]
    [SerializeField] string speakerName = "Adversaire";
    [TextArea(2, 5)]
    [SerializeField] string[] lines = { "Veux-tu m'affronter au curling ?" };
    [Tooltip("Image affichee dans l'emplacement Portrait du Canvas pendant cette rencontre.")]
    [SerializeField] Sprite portrait;
    [SerializeField] string fightButtonLabel = "Se battre";
    [SerializeField] string leaveButtonLabel = "Ne rien faire";

    readonly HashSet<Collider> playerCollidersInside = new HashSet<Collider>();
    PlayerMotionCampagne nearbyPlayer;
    bool encounterRunning;
    bool encounterConsumed;

    void Awake()
    {
        if (dialogueController == null)
            dialogueController = FindFirstObjectByType<DialogueController>();
        if (battleStarter == null)
            battleStarter = GetComponent<SceneTransitionTrigger>();
        if (approachController == null)
            approachController = GetComponentInParent<EnemyApproachController>();
    }

    void Update()
    {
        if (mode != CurlingEncounterMode.Voluntary || encounterRunning || encounterConsumed ||
            nearbyPlayer == null || dialogueController == null || dialogueController.IsDialogueActive)
            return;

        dialogueController.ShowInteractionPrompt(this, nearbyPlayer.transform);
        if (InteractionPressedThisFrame())
            StartEncounterDialogue(nearbyPlayer, allowLeaving: true);
    }

    void OnTriggerEnter(Collider other)
    {
        if (encounterConsumed)
            return;

        PlayerMotionCampagne player = other.GetComponentInParent<PlayerMotionCampagne>();
        if (player == null)
            return;

        playerCollidersInside.Add(other);
        nearbyPlayer = player;

        if (mode == CurlingEncounterMode.Ambush && !encounterRunning)
            StartAmbush(player);
    }

    void OnTriggerExit(Collider other)
    {
        if (!playerCollidersInside.Remove(other))
            return;

        if (playerCollidersInside.Count == 0)
        {
            nearbyPlayer = null;
            if (!encounterRunning && dialogueController != null)
                dialogueController.HideInteractionPrompt(this);
        }
    }

    void StartAmbush(PlayerMotionCampagne player)
    {
        encounterRunning = true;
        dialogueController?.HideInteractionPrompt(this);
        player.SetMovementEnabled(false);

        if (approachController != null)
            approachController.BeginApproach(
                player.transform,
                () => StartEncounterDialogue(player, allowLeaving: false));
        else
            StartEncounterDialogue(player, allowLeaving: false);
    }

    void StartEncounterDialogue(PlayerMotionCampagne player, bool allowLeaving)
    {
        if (dialogueController == null || battleStarter == null || player == null)
        {
            Debug.LogError("CurlingEncounter: DialogueController, SceneTransitionTrigger et joueur sont requis.", this);
            AbortEncounter(player);
            return;
        }

        encounterRunning = true;
        dialogueController.HideInteractionPrompt(this);

        string resolvedSpeakerName = speakerName;
        if (string.IsNullOrWhiteSpace(resolvedSpeakerName) && battleStarter.EnemyProfile != null)
            resolvedSpeakerName = battleStarter.EnemyProfile.OpponentName;

        DialogueRequest request = new DialogueRequest
        {
            speakerName = resolvedSpeakerName,
            lines = lines,
            portrait = portrait,
            choices = allowLeaving
                ? new[]
                {
                    new DialogueChoice(FightChoiceId, fightButtonLabel),
                    new DialogueChoice(LeaveChoiceId, leaveButtonLabel)
                }
                : new[]
                {
                    new DialogueChoice(FightChoiceId, fightButtonLabel)
                }
        };

        bool started = dialogueController.StartDialogue(
            request,
            player,
            result => HandleDialogueFinished(result, allowLeaving));

        if (!started)
            AbortEncounter(player);
    }

    void HandleDialogueFinished(DialogueResult result, bool couldLeave)
    {
        bool shouldFight = !couldLeave || result.SelectedChoiceId == FightChoiceId;
        if (shouldFight)
        {
            bool transitionStarted = battleStarter.StartTransition();
            encounterConsumed = transitionStarted;
            encounterRunning = false;
            if (!transitionStarted)
                approachController?.CancelApproach();
            return;
        }

        encounterRunning = false;
        approachController?.CancelApproach();
    }

    void AbortEncounter(PlayerMotionCampagne player)
    {
        encounterRunning = false;
        approachController?.CancelApproach();
        if (player != null)
            player.SetMovementEnabled(true);
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
        if (trigger != null)
            trigger.isTrigger = true;
    }

    void OnDisable()
    {
        if (dialogueController != null)
            dialogueController.HideInteractionPrompt(this);
    }
}
