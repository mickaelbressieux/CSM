using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot wiring for the campaign - curling round - campaign loop. Everything it does is
/// something you could do by hand in the Inspector; it is here so the wiring is applied once,
/// consistently, and can be re-run after a merge instead of remembered.
///
/// Like <see cref="MainMenuSceneBuilder"/>, this is scaffolding: run it, check the result,
/// then it is safe to delete.
/// </summary>
public static class CampaignLoopSetup
{
    private const string CampaignScenePath = "Assets/Scenes/CampagneMap.unity";
    private const string CurlingScenePath = "Assets/Scenes/CurlingRound.unity";
    private const string NodeMaterialPath = "Assets/Materials/CampaignNode.mat";

    /// <summary>
    /// The campaign objects that start a match when the player walks into them, by name.
    /// This is the list to edit: re-running the tool wires exactly these and strips the
    /// wiring off any object that used to be a match node and is no longer listed.
    /// </summary>
    private static readonly string[] MatchNodeNames =
    {
        "Cube (1)",
        "Cube (2)"
    };

    [MenuItem("Tools/CSM/Wire Campaign Loop")]
    public static void Wire()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        List<string> report = new List<string>();

        RegisterBuildScenes(report);
        WireCampaignScene(report);
        WireCurlingScene(report);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = string.Join("\n", report);
        Debug.Log("Campaign loop wiring:\n" + summary);
        EditorUtility.DisplayDialog("Campaign loop wired", summary, "OK");
    }

    /// <summary>Make sure every scene the loop jumps to can actually be loaded at runtime, and
    /// drop entries whose scene file no longer exists.</summary>
    private static void RegisterBuildScenes(List<string> report)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();

        foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
        {
            if (File.Exists(entry.path))
            {
                scenes.Add(entry);
            }
            else
            {
                report.Add($"- removed missing build scene '{entry.path}'");
            }
        }

        if (!scenes.Exists(s => s.path == CurlingScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(CurlingScenePath, true));
            report.Add("- added CurlingRound to the build scene list");
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void WireCampaignScene(List<string> report)
    {
        Scene scene = EditorSceneManager.OpenScene(CampaignScenePath, OpenSceneMode.Single);

        SceneAsset curlingScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(CurlingScenePath);
        Material nodeMaterial = LoadOrCreateNodeMaterial();

        // The trigger only fires for this object, so the player has to walk into the node -
        // a stray PNJ bumping into it must not start a match.
        PlayerMotionCampagne player = Object.FindFirstObjectByType<PlayerMotionCampagne>(FindObjectsInactive.Include);
        if (player == null)
        {
            report.Add("- WARNING: no PlayerMotionCampagne found; nodes will have no designated object");
        }

        List<GameObject> nodes = new List<GameObject>();
        foreach (string nodeName in MatchNodeNames)
        {
            GameObject node = FindByName(nodeName);
            if (node == null)
            {
                report.Add($"- WARNING: no object named '{nodeName}' in CampagneMap");
                continue;
            }

            nodes.Add(node);
            WireMatchNode(node, curlingScene, nodeMaterial, player, report);
        }

        StripFormerNodes(nodes, report);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void WireMatchNode(
        GameObject node, SceneAsset curlingScene, Material nodeMaterial,
        PlayerMotionCampagne player, List<string> report)
    {
        Collider collider = node.GetComponent<Collider>();
        if (collider == null)
        {
            report.Add($"- WARNING: '{node.name}' has no Collider, so it can never be walked into");
        }
        else
        {
            // OnTriggerEnter needs a trigger collider. The PNJ movers pin their own Y every
            // frame, so losing solid collision does not drop them through the ground.
            collider.isTrigger = true;
        }

        // A script-driven mover with a non-kinematic body just accumulates gravity it never
        // gets to use; kinematic is what these were always meant to be.
        Rigidbody body = node.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic)
        {
            body.isKinematic = true;
            report.Add($"- '{node.name}' Rigidbody set to kinematic (it is moved by script)");
        }

        SceneTransitionTrigger trigger = GetOrAdd<SceneTransitionTrigger>(node);

        // The explosion has to live on the same object, because it animates that object's
        // transform and disables its colliders.
        ExplosionEffect explosion = GetOrAdd<ExplosionEffect>(node);
        ScreenFadeEffect fade = GetOrAdd<ScreenFadeEffect>(node);

        // Explode, then fade. The trigger only ever knows about the sequence, so reordering
        // these - or swapping the explosion for a character animation later - is an edit to
        // this one list.
        TransitionEffectSequence sequence = GetOrAdd<TransitionEffectSequence>(node);
        SerializedObject serializedSequence = new SerializedObject(sequence);
        SerializedProperty effects = serializedSequence.FindProperty("effectSources");
        effects.arraySize = 2;
        effects.GetArrayElementAtIndex(0).objectReferenceValue = explosion;
        effects.GetArrayElementAtIndex(1).objectReferenceValue = fade;
        serializedSequence.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serialized = new SerializedObject(trigger);
        serialized.FindProperty("targetSceneAsset").objectReferenceValue = curlingScene;
        serialized.FindProperty("targetSceneName").stringValue = Path.GetFileNameWithoutExtension(CurlingScenePath);
        serialized.FindProperty("transitionEffectSource").objectReferenceValue = sequence;
        if (player != null)
        {
            serialized.FindProperty("designatedObject").objectReferenceValue = player.gameObject;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        // The material asset only supplies a URP-correct shader; the appearance component
        // owns the actual colour and swaps it when the node is cleared.
        ApplyNodeMaterial(node, nodeMaterial);
        TintedNodeAppearance appearance = GetOrAdd<TintedNodeAppearance>(node);

        CampaignMatchNode matchNode = GetOrAdd<CampaignMatchNode>(node);
        SerializedObject serializedNode = new SerializedObject(matchNode);
        serializedNode.FindProperty("nodeId").stringValue = node.name;
        serializedNode.FindProperty("appearanceSource").objectReferenceValue = appearance;
        // Nothing to list for stopping the node's movement: CampaignMatchNode discovers the
        // scripts on its own object at runtime.
        serializedNode.ApplyModifiedPropertiesWithoutUndo();

        report.Add($"- '{node.name}' explodes, fades, loads CurlingRound, and turns blue once played");
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    /// <summary>Take the match wiring back off anything that is no longer in
    /// <see cref="MatchNodeNames"/>, so changing the list is enough to move a node.</summary>
    private static void StripFormerNodes(List<GameObject> keep, List<string> report)
    {
        SceneTransitionTrigger[] triggers =
            Object.FindObjectsByType<SceneTransitionTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (SceneTransitionTrigger trigger in triggers)
        {
            GameObject go = trigger.gameObject;
            if (keep.Contains(go))
            {
                continue;
            }

            RestoreDefaultMaterial(go);

            // CampaignMatchNode RequireComponents the trigger, so it has to go first or Unity
            // refuses to remove the trigger out from under it.
            RemoveIfPresent<CampaignMatchNode>(go);
            RemoveIfPresent<TintedNodeAppearance>(go);
            RemoveIfPresent<TransitionEffectSequence>(go);
            RemoveIfPresent<ScreenFadeEffect>(go);
            RemoveIfPresent<ExplosionEffect>(go);

            Object.DestroyImmediate(trigger);
            report.Add($"- '{go.name}' is no longer a match node; wiring removed");
        }
    }

    private static void RemoveIfPresent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component != null)
        {
            Object.DestroyImmediate(component);
        }
    }

    private static GameObject FindByName(string name)
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name == name)
            {
                return t.gameObject;
            }
        }

        return null;
    }

    private static void WireCurlingScene(List<string> report)
    {
        Scene scene = EditorSceneManager.OpenScene(CurlingScenePath, OpenSceneMode.Single);

        SoloCurlingGameManager manager =
            Object.FindFirstObjectByType<SoloCurlingGameManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            report.Add("- WARNING: no SoloCurlingGameManager found in CurlingRound");
            return;
        }

        // TestDrop never ends, so there would be no EndScored event to return home on.
        if (manager.mode != SoloCurlingGameManager.GameMode.Match)
        {
            SerializedObject serialized = new SerializedObject(manager);
            serialized.FindProperty("mode").enumValueIndex = (int)SoloCurlingGameManager.GameMode.Match;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            report.Add("- SoloCurlingGameManager switched from TestDrop to Match mode");
        }

        if (manager.GetComponent<ReturnToCampaignOnMatchEnd>() == null)
        {
            manager.gameObject.AddComponent<ReturnToCampaignOnMatchEnd>();
            report.Add("- added ReturnToCampaignOnMatchEnd to the curling GameManager");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>The campaign nodes are meant to read as "red cube"; they ship with Unity's
    /// default material, so give them one.</summary>
    private static Material LoadOrCreateNodeMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(NodeMaterialPath);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader) { name = "CampaignNode" };
        material.color = new Color(0.78f, 0.16f, 0.18f);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", material.color);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(NodeMaterialPath));
        AssetDatabase.CreateAsset(material, NodeMaterialPath);
        return material;
    }

    private static void ApplyNodeMaterial(GameObject target, Material material)
    {
        if (material == null)
        {
            return;
        }

        Renderer renderer = target.GetComponent<Renderer>();
        // Only claim an untouched node; a material you picked yourself is left alone.
        if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.name == "Default-Material")
        {
            renderer.sharedMaterial = material;
        }
    }

    /// <summary>Hand a demoted node its plain look back, so a former match node does not keep
    /// advertising itself in red.</summary>
    private static void RestoreDefaultMaterial(GameObject target)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.name != "CampaignNode")
        {
            return;
        }

        Material defaultMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        if (defaultMaterial != null)
        {
            renderer.sharedMaterial = defaultMaterial;
        }
    }
}
