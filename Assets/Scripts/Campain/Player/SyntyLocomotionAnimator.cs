using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Pilote le contrôleur Synty Base Locomotion à partir du déplacement réel
/// de l'objet. Le composant ne dépend d'aucun système de mouvement particulier.
/// </summary>
[DisallowMultipleComponent]
public class SyntyLocomotionAnimator : MonoBehaviour
{
    /// <summary>
    /// Ajoute automatiquement le pilote d'animation au porteur d'un script de
    /// mouvement lorsqu'un Animator configure est present dans sa hierarchie.
    /// </summary>
    public static void EnsureFor(GameObject motionOwner)
    {
        if (motionOwner == null || motionOwner.GetComponent<SyntyLocomotionAnimator>() != null)
            return;

        Animator[] animators = motionOwner.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i].runtimeAnimatorController == null) continue;

            motionOwner.AddComponent<SyntyLocomotionAnimator>();
            return;
        }

        if (animators.Length > 0)
        {
            Debug.LogWarning(
                $"{motionOwner.name}: un Animator a ete trouve, mais aucun Animator Controller ne lui est assigne.",
                motionOwner);
        }
    }

    [Header("Références")]
    [Tooltip("Animator du personnage. S'il est vide, le premier Animator trouvé dans les enfants sera utilisé.")]
    [SerializeField] Animator characterAnimator;
    readonly List<Animator> characterAnimators = new List<Animator>();

    [Header("Déplacement")]
    [Tooltip("Transform dont le déplacement doit être mesuré. S'il est vide, ce sera celui de ce composant.")]
    [SerializeField] Transform motionSource;
    [Tooltip("Désactive le Root Motion afin que le script de mouvement reste responsable de la position.")]
    [SerializeField] bool disableAnimatorRootMotion = true;
    [Tooltip("Les déplacements supérieurs à cette distance en une frame sont considérés comme une téléportation.")]
    [Min(0f)]
    [SerializeField] float teleportDistance = 5f;

    [Header("Allures Synty")]
    [Min(0f)]
    [SerializeField] float walkSpeed = 1.4f;
    [Min(0f)]
    [SerializeField] float runSpeed = 2.5f;
    [Min(0f)]
    [SerializeField] float sprintSpeed = 7f;
    [Min(0f)]
    [SerializeField] float movementThreshold = 0.01f;
    [Min(0f)]
    [SerializeField] float inputHoldThreshold = 0.15f;

    [Header("État")]
    [Tooltip("À désactiver uniquement si un autre système renseigne l'état au sol avec SetGrounded.")]
    [SerializeField] bool assumeGrounded = true;

    static readonly int MovementInputTappedHash = Animator.StringToHash("MovementInputTapped");
    static readonly int MovementInputPressedHash = Animator.StringToHash("MovementInputPressed");
    static readonly int MovementInputHeldHash = Animator.StringToHash("MovementInputHeld");
    static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    static readonly int CurrentGaitHash = Animator.StringToHash("CurrentGait");
    static readonly int IsStoppedHash = Animator.StringToHash("IsStopped");
    static readonly int IsStartingHash = Animator.StringToHash("IsStarting");
    static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
    static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");

    Vector3 previousPosition;
    float movementDuration;
    bool wasMoving;
    bool isGrounded = true;

    public Animator CharacterAnimator => characterAnimator;
    public float CurrentSpeed { get; private set; }

    void Awake()
    {
        ResolveReferences();

        if (disableAnimatorRootMotion)
        {
            for (int i = 0; i < characterAnimators.Count; i++)
                characterAnimators[i].applyRootMotion = false;
        }
    }

    void OnEnable()
    {
        ResetMotionTracking();
        UpdateAnimator(0f);
    }

    void LateUpdate()
    {
        if (motionSource == null)
            return;

        Vector3 displacement = motionSource.position - previousPosition;
        displacement.y = 0f;
        previousPosition = motionSource.position;

        if (teleportDistance > 0f && displacement.magnitude >= teleportDistance)
        {
            CurrentSpeed = 0f;
            movementDuration = 0f;
            wasMoving = false;
        }
        else
        {
            CurrentSpeed = Time.deltaTime > 0f ? displacement.magnitude / Time.deltaTime : 0f;
        }

        UpdateAnimator(CurrentSpeed);
    }

    /// <summary>
    /// Permet à un système de saut ou de détection du sol de renseigner l'état réel.
    /// </summary>
    public void SetGrounded(bool grounded)
    {
        isGrounded = grounded;
    }

    /// <summary>
    /// À appeler après une téléportation manuelle pour ne pas déclencher un sprint.
    /// </summary>
    public void ResetMotionTracking()
    {
        ResolveReferences();
        previousPosition = motionSource != null ? motionSource.position : transform.position;
        CurrentSpeed = 0f;
        movementDuration = 0f;
        wasMoving = false;
    }

    void ResolveReferences()
    {
        if (motionSource == null)
            motionSource = transform;

        characterAnimators.Clear();

        if (characterAnimator != null && characterAnimator.runtimeAnimatorController != null)
            characterAnimators.Add(characterAnimator);

        Animator[] animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator.runtimeAnimatorController == null || characterAnimators.Contains(animator))
                continue;

            characterAnimators.Add(animator);
        }

        characterAnimator = characterAnimators.Count > 0 ? characterAnimators[0] : null;
    }

    void UpdateAnimator(float currentSpeed)
    {
        if (characterAnimators.Count == 0)
            return;

        bool isMoving = currentSpeed > movementThreshold;
        bool movementTapped = isMoving && !wasMoving;

        if (isMoving)
            movementDuration = movementTapped ? 0f : movementDuration + Time.deltaTime;
        else
            movementDuration = 0f;

        bool movementPressed = isMoving && movementDuration > 0f && movementDuration < inputHoldThreshold;
        bool movementHeld = isMoving && movementDuration >= inputHoldThreshold;

        float runThreshold = (walkSpeed + runSpeed) * 0.5f;
        float sprintThreshold = (runSpeed + sprintSpeed) * 0.5f;
        int currentGait = 0; // Idle

        if (isMoving)
            currentGait = currentSpeed < runThreshold ? 1 : currentSpeed < sprintThreshold ? 2 : 3;

        for (int i = 0; i < characterAnimators.Count; i++)
        {
            Animator animator = characterAnimators[i];
            if (animator == null || animator.runtimeAnimatorController == null)
                continue;

            animator.SetFloat(MoveSpeedHash, currentSpeed);
            animator.SetInteger(CurrentGaitHash, currentGait);
            animator.SetBool(MovementInputTappedHash, movementTapped);
            animator.SetBool(MovementInputPressedHash, movementPressed);
            animator.SetBool(MovementInputHeldHash, movementHeld);
            animator.SetBool(IsStoppedHash, !isMoving);
            animator.SetBool(IsStartingHash, movementTapped || movementPressed);
            animator.SetBool(IsWalkingHash, currentGait == 1);
            animator.SetBool(IsGroundedHash, assumeGrounded || isGrounded);
        }

        wasMoving = isMoving;
    }
}
