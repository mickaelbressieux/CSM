using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// A full-screen curtain that survives scene loads, so a transition can fade out in one scene
/// and fade back in once the next one is up.
///
/// It builds its own canvas on first use and marks itself DontDestroyOnLoad, so there is no
/// prefab to wire and no object to remember to put in every scene. Fading back in is
/// automatic: it watches for a scene load and lifts the curtain itself, which is what makes
/// the return from a match fade in without anything in the curling scene knowing about it.
/// </summary>
public class ScreenFader : MonoBehaviour
{
    private const float DefaultFadeInDuration = 0.45f;

    private static ScreenFader instance;

    private CanvasGroup group;
    private Image curtain;
    private Coroutine fade;

    /// <summary>The live fader, created on first access.</summary>
    public static ScreenFader Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new GameObject("ScreenFader").AddComponent<ScreenFader>();
            }

            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        BuildCanvas();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    /// <summary>Darken the screen to <paramref name="color"/>, then call back. The curtain is
    /// left opaque on purpose: the caller is about to load a scene, and the matching fade in
    /// happens on the other side.</summary>
    public void FadeOut(float duration, Color color, Action onComplete)
    {
        curtain.color = new Color(color.r, color.g, color.b, curtain.color.a);
        Play(FadeRoutine(1f, duration, onComplete));
    }

    public void FadeIn(float duration)
    {
        Play(FadeRoutine(0f, duration, null));
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Only if something actually left the curtain down.
        if (group.alpha > 0f)
        {
            FadeIn(DefaultFadeInDuration);
        }
    }

    private void Play(IEnumerator routine)
    {
        if (fade != null)
        {
            StopCoroutine(fade);
        }

        fade = StartCoroutine(routine);
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration, Action onComplete)
    {
        float startAlpha = group.alpha;

        if (duration > 0f)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration));
                yield return null;
            }
        }

        SetAlpha(targetAlpha);
        fade = null;
        onComplete?.Invoke();
    }

    private void SetAlpha(float alpha)
    {
        group.alpha = alpha;
        // Swallow clicks while anything is on screen, so the player cannot act on a scene
        // they can no longer see.
        group.blocksRaycasts = alpha > 0.001f;
    }

    private void BuildCanvas()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above every other canvas in the game, whatever they set.
        canvas.sortingOrder = short.MaxValue;

        gameObject.AddComponent<GraphicRaycaster>();

        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        GameObject curtainObject = new GameObject("Curtain", typeof(RectTransform));
        curtainObject.transform.SetParent(transform, false);

        RectTransform rect = (RectTransform)curtainObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        curtain = curtainObject.AddComponent<Image>();
        curtain.color = Color.black;
    }
}
