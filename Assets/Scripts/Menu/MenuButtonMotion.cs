using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Gives a menu button a curling-stone nudge: it slides sideways and grows a little when the
/// pointer is over it or it holds keyboard/gamepad focus, then eases back when it does not.
///
/// It owns the button's anchoredPosition, so keep the button out of a layout group - the
/// layout would rewrite the position every frame and the two would fight. The main menu
/// scaffolder positions buttons manually for exactly this reason.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class MenuButtonMotion : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Tooltip("How far the button slides when highlighted, in canvas units.")]
    [SerializeField] private float slideDistance = 18f;
    [SerializeField, Range(1f, 1.5f)] private float highlightScale = 1.06f;
    [Tooltip("Higher is snappier. This is the exponential approach rate, not a duration.")]
    [SerializeField, Range(1f, 30f)] private float responsiveness = 12f;

    private RectTransform rect;
    private Vector2 restPosition;
    private bool pointerOver;
    private bool selected;

    private bool Highlighted => pointerOver || selected;

    private void Awake()
    {
        rect = (RectTransform)transform;
        restPosition = rect.anchoredPosition;
    }

    private void OnDisable()
    {
        // Snap back, so re-enabling the panel never shows a button frozen mid-slide.
        pointerOver = false;
        selected = false;
        rect.anchoredPosition = restPosition;
        rect.localScale = Vector3.one;
    }

    private void Update()
    {
        Vector2 targetPosition = Highlighted
            ? restPosition + new Vector2(slideDistance, 0f)
            : restPosition;
        Vector3 targetScale = Highlighted
            ? new Vector3(highlightScale, highlightScale, 1f)
            : Vector3.one;

        // Frame-rate independent exponential ease; unscaledDeltaTime so the menu still
        // animates if anything has paused the game by setting Time.timeScale to zero.
        float t = 1f - Mathf.Exp(-responsiveness * Time.unscaledDeltaTime);
        rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, targetPosition, t);
        rect.localScale = Vector3.Lerp(rect.localScale, targetScale, t);
    }

    public void OnPointerEnter(PointerEventData eventData) => pointerOver = true;
    public void OnPointerExit(PointerEventData eventData) => pointerOver = false;
    public void OnSelect(BaseEventData eventData) => selected = true;
    public void OnDeselect(BaseEventData eventData) => selected = false;
}
