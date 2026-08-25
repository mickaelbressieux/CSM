using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sends the player back to the campaign once a match is over, after a pause long enough to
/// read the result off the HUD.
///
/// Rides the existing <see cref="MatchEvents.EndScored"/> channel rather than reaching into
/// <see cref="SoloCurlingGameManager"/>, so the round logic knows nothing about the campaign
/// and stays playable on its own. Drop this component on any object in the curling scene;
/// remove it and the round behaves exactly as it did before.
///
/// Per the <see cref="MatchEvents"/> contract, it subscribes in OnEnable and unsubscribes in
/// OnDisable.
/// </summary>
public class ReturnToCampaignOnMatchEnd : MonoBehaviour
{
    [Header("Destination")]
    [Tooltip("Scene to return to. Must be in the Build Profile's scene list.")]
    [SerializeField] private string campaignSceneName = "CampagneMap";

    [Header("Timing")]
    [Tooltip("Seconds the result stays on screen before the campaign loads.")]
    [SerializeField] private float returnDelay = 5f;

    [Tooltip("Fade to black before loading the campaign. Zero cuts straight there.")]
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("HUD")]
    [Tooltip("Optional. Found automatically if left empty; used to show the countdown.")]
    [SerializeField] private CurlingUIManager curlingUI;

    private Coroutine returnRoutine;

    private void OnEnable()
    {
        MatchEvents.EndScored += HandleEndScored;
    }

    private void OnDisable()
    {
        MatchEvents.EndScored -= HandleEndScored;
    }

    private void HandleEndScored(string result)
    {
        // A rematch could raise this again; only the first one gets to schedule the exit.
        if (returnRoutine != null)
        {
            return;
        }

        returnRoutine = StartCoroutine(ReturnRoutine(result));
    }

    private IEnumerator ReturnRoutine(string result)
    {
        if (curlingUI == null)
        {
            curlingUI = FindFirstObjectByType<CurlingUIManager>();
        }

        // The game manager writes its own end-of-match banner on the same frame this event
        // fires. The HUD renders whatever was set last each frame, so ticking the countdown
        // here quietly takes the banner over without the manager needing to know.
        for (float remaining = returnDelay; remaining > 0f; remaining -= Time.deltaTime)
        {
            if (curlingUI != null)
            {
                curlingUI.SetBanner($"{result}\n\nReturning to the campaign in {Mathf.CeilToInt(remaining)}...");
            }

            yield return null;
        }

        if (fadeDuration <= 0f)
        {
            LoadCampaign();
            yield break;
        }

        // The fader lifts the curtain itself once the campaign is up, so the trip home is
        // symmetrical with the way in.
        ScreenFader.Instance.FadeOut(fadeDuration, Color.black, LoadCampaign);
    }

    private void LoadCampaign()
    {
        if (string.IsNullOrWhiteSpace(campaignSceneName))
        {
            Debug.LogError($"{nameof(ReturnToCampaignOnMatchEnd)}: campaignSceneName is empty.", this);
            return;
        }

        // Prefer the campaign manager so inventory and skills survive the trip. It only
        // exists when we arrived from the campaign; playing the round standalone from the
        // editor falls back to a plain load.
        CampainManager manager = CampainManager.Instance;
        if (manager != null && manager.LoadScene(campaignSceneName))
        {
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(campaignSceneName))
        {
            Debug.LogError(
                $"{nameof(ReturnToCampaignOnMatchEnd)}: scene '{campaignSceneName}' is not in the " +
                "active Build Profile's scene list.", this);
            return;
        }

        SceneManager.LoadScene(campaignSceneName);
    }
}
