using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

public class InventoryUI : MonoBehaviour
{
    private enum CharacterTab
    {
        Inventory,
        Skills
    }

    [Header("Toggle Key")]
    [SerializeField] private KeyCode fallbackToggleKey = KeyCode.I;

    [Header("Window")]
    [SerializeField] private Rect windowRect = new Rect(20f, 20f, 320f, 420f);
    [SerializeField] private string windowTitle = "Personnage";

    private bool isOpen;
    private Vector2 scrollPosition;
    private CharacterTab selectedTab = CharacterTab.Inventory;

    private void Update()
    {
        if (WasTogglePressed())
        {
            isOpen = !isOpen;
        }
    }

    private bool WasTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.iKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(fallbackToggleKey);
#endif
    }

    private void OnGUI()
    {
        if (!isOpen)
        {
            return;
        }

        windowRect = GUI.Window(7001, windowRect, DrawCharacterWindow, windowTitle);
    }

    private void DrawCharacterWindow(int windowId)
    {
        CampainManager manager = CampainManager.Instance;
        if (manager == null)
        {
            GUI.Label(new Rect(12f, 28f, windowRect.width - 24f, 20f), "CampainManager introuvable dans la scene.");
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
            return;
        }

        DrawTabs();

        float top = 58f;
        float bottomPadding = 40f;
        float viewWidth = windowRect.width - 24f;
        float viewHeight = windowRect.height - top - bottomPadding;
        Rect viewRect = new Rect(12f, top, viewWidth, viewHeight);

        Dictionary<string, int> snapshot = selectedTab == CharacterTab.Inventory
            ? manager.GetCharacterInventorySnapshot()
            : manager.GetCharacterSkillsSnapshot();

        float contentHeight = Mathf.Max(28f, snapshot.Count * 24f + 8f);
        Rect contentRect = new Rect(0f, 0f, viewWidth - 16f, contentHeight);

        scrollPosition = GUI.BeginScrollView(viewRect, scrollPosition, contentRect);

        if (snapshot.Count == 0)
        {
            string emptyLabel = selectedTab == CharacterTab.Inventory ? "Inventaire vide" : "Aucune competence";
            GUI.Label(new Rect(6f, 6f, contentRect.width - 12f, 20f), emptyLabel);
        }
        else
        {
            float y = 6f;
            foreach (KeyValuePair<string, int> item in snapshot)
            {
                string suffix = selectedTab == CharacterTab.Inventory ? $"x{item.Value}" : $"niv. {item.Value}";
                GUI.Label(new Rect(6f, y, contentRect.width - 12f, 20f), $"- {item.Key} {suffix}");
                y += 22f;
            }
        }

        GUI.EndScrollView();

        if (GUI.Button(new Rect(windowRect.width - 92f, windowRect.height - 30f, 80f, 22f), "Fermer"))
        {
            isOpen = false;
        }

        GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
    }

    private void DrawTabs()
    {
        Rect toolbarRect = new Rect(12f, 28f, windowRect.width - 24f, 24f);
        int currentIndex = selectedTab == CharacterTab.Inventory ? 0 : 1;
        int nextIndex = GUI.Toolbar(toolbarRect, currentIndex, new[] { "Inventaire", "Competences" });

        if (nextIndex != currentIndex)
        {
            selectedTab = nextIndex == 0 ? CharacterTab.Inventory : CharacterTab.Skills;
            scrollPosition = Vector2.zero;
        }
    }
}
