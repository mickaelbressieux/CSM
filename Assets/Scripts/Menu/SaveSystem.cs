using UnityEngine;

/// <summary>
/// The save/load seam. Deliberately not implemented yet: the main menu's Load button asks
/// <see cref="HasSave"/> whether it should be interactable, so today it simply greys out.
///
/// To make saving real, implement the three methods below and nothing else has to change.
/// The data worth persisting already has accessors:
/// <see cref="CampainManager.GetInventorySnapshot"/> and
/// <see cref="CampainManager.GetSkillsSnapshot"/> return plain dictionaries, and the active
/// scene name plus the player's transform round out a campaign save.
/// A JSON file under <see cref="Application.persistentDataPath"/> is the obvious first pass.
/// </summary>
public static class SaveSystem
{
    /// <summary>File name a future implementation should use, kept here so the menu, the
    /// save path and any editor tooling all agree on one value.</summary>
    public const string SaveFileName = "campaign.json";

    public static string SaveFilePath => System.IO.Path.Combine(Application.persistentDataPath, SaveFileName);

    /// <summary>True when there is a campaign to resume. Drives the Load button's
    /// interactable state in <see cref="MainMenuController"/>.</summary>
    public static bool HasSave()
    {
        // TODO: return System.IO.File.Exists(SaveFilePath);
        return false;
    }

    /// <summary>Write the current campaign state to disk.</summary>
    public static void Save()
    {
        Debug.LogWarning("SaveSystem.Save() is not implemented yet.");
    }

    /// <summary>Restore a saved campaign. Returns false when there is nothing to load.</summary>
    public static bool Load()
    {
        Debug.LogWarning("SaveSystem.Load() is not implemented yet.");
        return false;
    }
}
