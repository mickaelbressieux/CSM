using UnityEngine;

[ExecuteAlways]
public class PlayerTagAssigner : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    private bool warnedMissingTag;

    private void OnValidate()
    {
        ApplyTag();
    }

    private void OnEnable()
    {
        ApplyTag();
    }

    private void Awake()
    {
        ApplyTag();
    }

    private void ApplyTag()
    {
        if (string.IsNullOrWhiteSpace(playerTag))
            return;

        if (!IsTagDefined(playerTag))
        {
            if (!warnedMissingTag)
            {
                warnedMissingTag = true;
                Debug.LogWarning("Le tag '" + playerTag + "' n'existe pas. Ajoute-le dans Tags and Layers.", this);
            }
            return;
        }

        warnedMissingTag = false;
        gameObject.tag = playerTag;
    }

    private bool IsTagDefined(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return false;

        if (tagName == "Untagged")
            return true;

        try
        {
            GameObject.FindWithTag(tagName);
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }
}
