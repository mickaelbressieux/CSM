using UnityEngine;

// Attach this script to the GameManager object in the target scene.
public class SceneGameManagerReceiver : MonoBehaviour, ISceneTransitionDataReceiver
{
    [SerializeField] private bool logOnReceive = true;
    [SerializeField] private bool showOnScreen = true;
    [SerializeField] private Rect displayRect = new Rect(20f, 20f, 420f, 520f);
    [SerializeField] private string panelTitle = "Transition Data";

    private Vector2 scroll;

    public SceneTransitionData LastReceivedData { get; private set; }

    public void ReceiveTransitionData(SceneTransitionData data)
    {
        LastReceivedData = data;

        if (!logOnReceive || data == null)
        {
            return;
        }

        Debug.Log($"GameManager received transition data: {data.SourceSceneName} -> {data.TargetSceneName}");
    }

    public bool TryGetInt(string key, out int value)
    {
        value = 0;
        return LastReceivedData != null && LastReceivedData.TryGetInt(key, out value);
    }

    public bool TryGetFloat(string key, out float value)
    {
        value = 0f;
        return LastReceivedData != null && LastReceivedData.TryGetFloat(key, out value);
    }

    public bool TryGetBool(string key, out bool value)
    {
        value = false;
        return LastReceivedData != null && LastReceivedData.TryGetBool(key, out value);
    }

    public bool TryGetString(string key, out string value)
    {
        value = string.Empty;
        return LastReceivedData != null && LastReceivedData.TryGetString(key, out value);
    }

    private void OnGUI()
    {
        if (!showOnScreen)
        {
            return;
        }

        displayRect = GUI.Window(7002, displayRect, DrawDataWindow, panelTitle);
    }

    private void DrawDataWindow(int windowId)
    {
        if (LastReceivedData == null)
        {
            GUI.Label(new Rect(12f, 28f, displayRect.width - 24f, 20f), "Aucune donnee recue pour le moment.");
            GUI.DragWindow(new Rect(0f, 0f, displayRect.width, 24f));
            return;
        }

        float top = 28f;
        float bottomPadding = 12f;
        float viewWidth = displayRect.width - 24f;
        float viewHeight = displayRect.height - top - bottomPadding;
        Rect viewRect = new Rect(12f, top, viewWidth, viewHeight);

        float contentHeight = EstimateContentHeight(LastReceivedData);
        Rect contentRect = new Rect(0f, 0f, viewWidth - 16f, contentHeight);
        scroll = GUI.BeginScrollView(viewRect, scroll, contentRect);

        float y = 6f;
        y = DrawLine($"Source: {LastReceivedData.SourceSceneName}", y, contentRect.width);
        y = DrawLine($"Target: {LastReceivedData.TargetSceneName}", y, contentRect.width);
        y += 6f;

        y = DrawSection("Integers", LastReceivedData.IntValues, y, contentRect.width);
        y = DrawSection("Floats", LastReceivedData.FloatValues, y, contentRect.width);
        y = DrawSection("Booleans", LastReceivedData.BoolValues, y, contentRect.width);
        y = DrawSection("Strings", LastReceivedData.StringValues, y, contentRect.width);

        y = DrawLine("Inventory", y, contentRect.width);
        if (LastReceivedData.InventorySnapshot == null || LastReceivedData.InventorySnapshot.Count == 0)
        {
            y = DrawLine("  (vide)", y, contentRect.width);
        }
        else
        {
            foreach (var item in LastReceivedData.InventorySnapshot)
            {
                y = DrawLine($"  - {item.Key}: {item.Value}", y, contentRect.width);
            }
        }

        GUI.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, displayRect.width, 24f));
    }

    private float DrawSection<T>(string title, System.Collections.Generic.IReadOnlyDictionary<string, T> values, float y, float width)
    {
        y = DrawLine(title, y, width);
        if (values == null || values.Count == 0)
        {
            y = DrawLine("  (vide)", y, width);
            return y;
        }

        foreach (var kv in values)
        {
            y = DrawLine($"  - {kv.Key}: {kv.Value}", y, width);
        }

        return y;
    }

    private float DrawLine(string text, float y, float width)
    {
        GUI.Label(new Rect(6f, y, width - 12f, 20f), text);
        return y + 20f;
    }

    private float EstimateContentHeight(SceneTransitionData data)
    {
        int lineCount = 0;
        lineCount += 2;
        lineCount += 1 + Mathf.Max(1, data.IntValues.Count);
        lineCount += 1 + Mathf.Max(1, data.FloatValues.Count);
        lineCount += 1 + Mathf.Max(1, data.BoolValues.Count);
        lineCount += 1 + Mathf.Max(1, data.StringValues.Count);

        int inventoryCount = data.InventorySnapshot == null ? 0 : data.InventorySnapshot.Count;
        lineCount += 1 + Mathf.Max(1, inventoryCount);

        return 12f + lineCount * 20f;
    }
}
