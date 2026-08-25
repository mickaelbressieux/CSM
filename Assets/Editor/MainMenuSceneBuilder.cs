using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-shot scaffolding for the main menu scene.
///
/// The menu is pure UI, so building it by hand is a few dozen clicks of creating objects and
/// dragging references. This does it once, then gets out of the way: what it produces is an
/// ordinary scene with ordinary components, editable entirely in the Inspector. Once you have
/// run it and tweaked the result, this file can be deleted - re-running it would overwrite
/// your tweaks.
/// </summary>
public static class MainMenuSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const int UILayer = 5;

    private static readonly Color IceDark = new Color32(0x14, 0x21, 0x2E, 0xFF);
    private static readonly Color TextBright = new Color32(0xE8, 0xF1, 0xF8, 0xFF);
    private static readonly Color TextMuted = new Color32(0x9F, 0xB4, 0xC6, 0xFF);
    private static readonly Color ButtonFace = new Color32(0x1E, 0x33, 0x47, 0xF2);
    private static readonly Color Accent = new Color32(0xC8, 0x38, 0x3D, 0xFF);
    private static readonly Color PanelFace = new Color32(0x16, 0x26, 0x36, 0xFA);

    [MenuItem("Tools/CSM/Build Main Menu Scene")]
    public static void Build()
    {
        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog(
                "Overwrite MainMenu?",
                $"{ScenePath} already exists. Rebuilding it discards any changes you made there.\n\nOverwrite?",
                "Overwrite", "Cancel"))
        {
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateCamera();
        Canvas canvas = CreateCanvas();
        CreateEventSystem();

        CreateBackdrop(canvas.transform);
        CreateTitle(canvas.transform);
        OptionsPanel options = CreateOptionsPanel(canvas.transform);
        CreateMenuButtons(canvas.transform, options);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        RegisterAsFirstBuildScene();

        Debug.Log($"Main menu scene built at {ScenePath} and registered as build scene 0.");
        EditorUtility.DisplayDialog(
            "Main menu built",
            $"Created {ScenePath} and made it the first scene in the build list.\n\n" +
            "Press Play to try it. You can delete Assets/Editor/MainMenuSceneBuilder.cs now.",
            "OK");
    }

    // ------------------------------------------------------------------
    // Scene furniture
    // ------------------------------------------------------------------

    private static void CreateCamera()
    {
        GameObject go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener))
        {
            tag = "MainCamera"
        };

        Camera camera = go.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = IceDark;
        // A Screen Space Overlay canvas does not need a camera, but without one Unity warns
        // that nothing is rendering, and there would be no AudioListener for the volume
        // setting to act on.
        camera.cullingMask = 0;
    }

    private static Canvas CreateCanvas()
    {
        GameObject go = new GameObject("Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
        {
            layer = UILayer
        };

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private static void CreateEventSystem()
    {
        // InputSystemUIInputModule, not StandaloneInputModule: the project is configured for
        // the new Input System only (Player Settings > Active Input Handling), and the legacy
        // module would silently receive nothing.
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    // ------------------------------------------------------------------
    // Menu content
    // ------------------------------------------------------------------

    private static void CreateBackdrop(Transform parent)
    {
        RectTransform rect = CreateUIObject("Backdrop", parent);
        Stretch(rect);
        rect.gameObject.AddComponent<IceBackdrop>();
    }

    private static void CreateTitle(Transform parent)
    {
        // Two-tone via rich text rather than two objects, so the words stay on one baseline
        // however long the title gets.
        string titleText = $"Curling <color=#{ColorUtility.ToHtmlStringRGB(Accent)}>Sa Mère</color>";
        RectTransform title = CreateText(parent, "Title", titleText, 128f, TextBright, FontStyles.Bold);
        Anchor(title, new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(1600f, 160f));
        title.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
    }

    private static void CreateMenuButtons(Transform parent, OptionsPanel options)
    {
        RectTransform root = CreateUIObject("Menu", parent);
        Anchor(root, new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(400f, 400f));

        // Positioned by hand rather than by a layout group: MenuButtonMotion animates each
        // button's anchoredPosition, and a layout group would overwrite it every frame.
        Button newGame = CreateMenuButton(root, "NewGameButton", "New Game", 150f);
        Button loadGame = CreateMenuButton(root, "LoadGameButton", "Load Game", 60f);
        Button optionsButton = CreateMenuButton(root, "OptionsButton", "Options", -30f);
        Button exit = CreateMenuButton(root, "ExitButton", "Exit", -120f);

        MainMenuController controller =
            new GameObject("MainMenu", typeof(MainMenuController)).GetComponent<MainMenuController>();

        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("newGameButton").objectReferenceValue = newGame;
        serialized.FindProperty("loadGameButton").objectReferenceValue = loadGame;
        serialized.FindProperty("optionsButton").objectReferenceValue = optionsButton;
        serialized.FindProperty("exitButton").objectReferenceValue = exit;
        serialized.FindProperty("optionsPanel").objectReferenceValue = options.gameObject;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Button CreateMenuButton(Transform parent, string name, string label, float y)
    {
        GameObject go = TMP_DefaultControls.CreateButton(StandardResources());
        go.name = name;
        go.transform.SetParent(parent, false);
        SetLayerRecursively(go, UILayer);

        RectTransform rect = (RectTransform)go.transform;
        Anchor(rect, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(360f, 72f));

        Image face = go.GetComponent<Image>();
        face.color = ButtonFace;

        TextMeshProUGUI text = go.GetComponentInChildren<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 34f;
        text.color = TextBright;
        text.alignment = TextAlignmentOptions.Center;

        go.AddComponent<MenuButtonMotion>();
        return go.GetComponent<Button>();
    }

    private static OptionsPanel CreateOptionsPanel(Transform parent)
    {
        // A full-screen dimmer that also blocks clicks on the menu behind it.
        RectTransform root = CreateUIObject("OptionsPanel", parent);
        Stretch(root);
        Image dimmer = root.gameObject.AddComponent<Image>();
        dimmer.color = new Color(0f, 0f, 0f, 0.72f);

        RectTransform window = CreateUIObject("Window", root);
        Anchor(window, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 480f));
        Image windowFace = window.gameObject.AddComponent<Image>();
        windowFace.sprite = StandardResources().standard;
        windowFace.type = Image.Type.Sliced;
        windowFace.color = PanelFace;

        RectTransform header = CreateText(window, "Header", "Options", 48f, TextBright, FontStyles.Bold);
        Anchor(header, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(600f, 60f));
        header.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;

        Slider volume = CreateVolumeRow(window, -20f);
        Toggle fullscreen = CreateFullscreenRow(window, -100f);
        TMP_Dropdown resolution = CreateResolutionRow(window, -180f);
        Button back = CreateBackButton(window);

        OptionsPanel panel = root.gameObject.AddComponent<OptionsPanel>();
        SerializedObject serialized = new SerializedObject(panel);
        serialized.FindProperty("masterVolumeSlider").objectReferenceValue = volume;
        serialized.FindProperty("fullscreenToggle").objectReferenceValue = fullscreen;
        serialized.FindProperty("resolutionDropdown").objectReferenceValue = resolution;
        serialized.FindProperty("backButton").objectReferenceValue = back;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // MainMenuController turns it off at runtime too; off by default keeps the Scene view
        // usable while editing the menu.
        root.gameObject.SetActive(false);
        return panel;
    }

    private static Slider CreateVolumeRow(RectTransform window, float y)
    {
        CreateRowLabel(window, "VolumeLabel", "Master volume", y);

        GameObject go = DefaultControls.CreateSlider(DefaultResources());
        go.name = "MasterVolumeSlider";
        go.transform.SetParent(window, false);
        SetLayerRecursively(go, UILayer);
        Anchor((RectTransform)go.transform, new Vector2(0.5f, 0.5f), new Vector2(140f, y), new Vector2(300f, 32f));

        return go.GetComponent<Slider>();
    }

    private static Toggle CreateFullscreenRow(RectTransform window, float y)
    {
        CreateRowLabel(window, "FullscreenLabel", "Fullscreen", y);

        GameObject go = DefaultControls.CreateToggle(DefaultResources());
        go.name = "FullscreenToggle";
        go.transform.SetParent(window, false);
        SetLayerRecursively(go, UILayer);

        // Left edge lines up with the slider and dropdown in the rows above and below.
        Anchor((RectTransform)go.transform, new Vector2(0.5f, 0.5f), new Vector2(10f, y), new Vector2(40f, 40f));

        // DefaultControls builds a legacy uGUI Text label; the row already has a TMP one, and
        // leaving it would mix the two text systems in one scene.
        Transform legacyLabel = go.transform.Find("Label");
        if (legacyLabel != null)
        {
            Object.DestroyImmediate(legacyLabel.gameObject);
        }

        // The default checkbox is 20px, which is tiny against a 1920-wide reference canvas,
        // and it is anchored to the root's top-left to leave room for that label. With the
        // label gone, centre it and size it to fill the row.
        RectTransform background = (RectTransform)go.transform.Find("Background");
        if (background != null)
        {
            background.anchorMin = new Vector2(0.5f, 0.5f);
            background.anchorMax = new Vector2(0.5f, 0.5f);
            background.pivot = new Vector2(0.5f, 0.5f);
            background.anchoredPosition = Vector2.zero;
            background.sizeDelta = new Vector2(40f, 40f);

            RectTransform checkmark = (RectTransform)background.Find("Checkmark");
            if (checkmark != null)
            {
                checkmark.sizeDelta = new Vector2(34f, 34f);
            }
        }

        return go.GetComponent<Toggle>();
    }

    private static TMP_Dropdown CreateResolutionRow(RectTransform window, float y)
    {
        CreateRowLabel(window, "ResolutionLabel", "Resolution", y);

        GameObject go = TMP_DefaultControls.CreateDropdown(StandardResources());
        go.name = "ResolutionDropdown";
        go.transform.SetParent(window, false);
        SetLayerRecursively(go, UILayer);
        Anchor((RectTransform)go.transform, new Vector2(0.5f, 0.5f), new Vector2(140f, y), new Vector2(300f, 44f));

        return go.GetComponent<TMP_Dropdown>();
    }

    private static Button CreateBackButton(RectTransform window)
    {
        GameObject go = TMP_DefaultControls.CreateButton(StandardResources());
        go.name = "BackButton";
        go.transform.SetParent(window, false);
        SetLayerRecursively(go, UILayer);
        Anchor((RectTransform)go.transform, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(240f, 60f));

        go.GetComponent<Image>().color = ButtonFace;

        TextMeshProUGUI text = go.GetComponentInChildren<TextMeshProUGUI>();
        text.text = "Back";
        text.fontSize = 30f;
        text.color = TextBright;
        text.alignment = TextAlignmentOptions.Center;

        return go.GetComponent<Button>();
    }

    private static void CreateRowLabel(RectTransform window, string name, string label, float y)
    {
        RectTransform rect = CreateText(window, name, label, 28f, TextMuted, FontStyles.Normal);
        Anchor(rect, new Vector2(0.5f, 0.5f), new Vector2(-180f, y), new Vector2(280f, 40f));
        rect.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void RegisterAsFirstBuildScene()
    {
        EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;

        // Rebuild the list with MainMenu at index 0, dropping any previous entry for it so a
        // re-run does not leave a duplicate behind.
        var rebuilt = new System.Collections.Generic.List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };

        foreach (EditorBuildSettingsScene entry in existing)
        {
            if (entry.path != ScenePath)
            {
                rebuilt.Add(entry);
            }
        }

        EditorBuildSettings.scenes = rebuilt.ToArray();
    }

    private static RectTransform CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform)) { layer = UILayer };
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static RectTransform CreateText(
        Transform parent, string name, string content, float size, Color color, FontStyles style)
    {
        RectTransform rect = CreateUIObject(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.raycastTarget = false;
        return rect;
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    // The same built-in skin sprites Unity's own "GameObject > UI" menu items use, so the
    // controls built here look like ones you would have added by hand.
    private static TMP_DefaultControls.Resources cachedStandard;
    private static DefaultControls.Resources cachedDefault;

    private static TMP_DefaultControls.Resources StandardResources()
    {
        if (cachedStandard.standard == null)
        {
            cachedStandard = new TMP_DefaultControls.Resources
            {
                standard = Builtin("UI/Skin/UISprite.psd"),
                background = Builtin("UI/Skin/Background.psd"),
                inputField = Builtin("UI/Skin/InputFieldBackground.psd"),
                knob = Builtin("UI/Skin/Knob.psd"),
                checkmark = Builtin("UI/Skin/Checkmark.psd"),
                dropdown = Builtin("UI/Skin/DropdownArrow.psd"),
                mask = Builtin("UI/Skin/UIMask.psd")
            };
        }

        return cachedStandard;
    }

    private static DefaultControls.Resources DefaultResources()
    {
        if (cachedDefault.standard == null)
        {
            TMP_DefaultControls.Resources source = StandardResources();
            cachedDefault = new DefaultControls.Resources
            {
                standard = source.standard,
                background = source.background,
                inputField = source.inputField,
                knob = source.knob,
                checkmark = source.checkmark,
                dropdown = source.dropdown,
                mask = source.mask
            };
        }

        return cachedDefault;
    }

    private static Sprite Builtin(string path) => AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
}
