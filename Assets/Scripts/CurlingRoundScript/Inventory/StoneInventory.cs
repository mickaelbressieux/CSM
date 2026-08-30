using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's collection of stones — put this on a GameObject named "Inventory" in the curling
/// scene and hand it to <c>SoloCurlingGameManager.playerStoneInventory</c>.
///
/// Each entry is a <see cref="StoneLoadout"/>: a stone plus the powers on it. In a match the
/// player's throws consume the list in order (throw 0 → stone [0], ...); a throw past the end of
/// the list gets an ordinary stone, so a missing or short inventory never breaks a round.
///
/// Authored in the Inspector today. The runtime API (<see cref="AddStone"/>, <see cref="AddPower"/>,
/// <see cref="RemovePower"/>, <see cref="InventoryChanged"/>) is what story mode will drive when the
/// player buys or upgrades a stone.
///
/// <b>Not</b> wired into <see cref="CampainManager"/> yet, on purpose: its inventory is a
/// <c>Dictionary&lt;string,int&gt;</c> of item id → quantity, which cannot express "stone #2 carries
/// powers A and B". Bridging the two is a story-mode job, and
/// <see cref="StonePowerDefinition.powerId"/> is the key it will use.
/// </summary>
public class StoneInventory : MonoBehaviour
{
    [Tooltip("The player's stones, consumed in order during a match. Empty entries = ordinary stones.")]
    [SerializeField] private List<StoneLoadout> stones = new List<StoneLoadout>();

    /// <summary>Raised whenever the collection changes, so an inventory UI can refresh itself.</summary>
    public event Action InventoryChanged;

    public IReadOnlyList<StoneLoadout> Stones => stones;

    public int Count => stones.Count;

    /// <summary>
    /// The loadout for throw <paramref name="index"/>, or <c>null</c> past the end of the list —
    /// callers treat null as "build an ordinary stone".
    /// </summary>
    public StoneLoadout GetStone(int index) =>
        index >= 0 && index < stones.Count ? stones[index] : null;

    /// <summary>Add an empty stone to the collection and return it, so powers can be added to it.</summary>
    public StoneLoadout AddStone(string displayName = null)
    {
        StoneLoadout loadout = new StoneLoadout(
            string.IsNullOrWhiteSpace(displayName) ? $"Stone {stones.Count + 1}" : displayName);
        stones.Add(loadout);
        InventoryChanged?.Invoke();
        return loadout;
    }

    public bool RemoveStone(int index)
    {
        if (index < 0 || index >= stones.Count)
            return false;

        stones.RemoveAt(index);
        InventoryChanged?.Invoke();
        return true;
    }

    /// <summary>Bolt a power onto one of the player's stones. Duplicates are allowed — they stack.</summary>
    public bool AddPower(int stoneIndex, StonePowerDefinition power)
    {
        StoneLoadout loadout = GetStone(stoneIndex);
        if (loadout == null || power == null)
            return false;

        loadout.powers.Add(power);
        InventoryChanged?.Invoke();
        return true;
    }

    /// <summary>Remove one instance of a power from a stone.</summary>
    public bool RemovePower(int stoneIndex, StonePowerDefinition power)
    {
        StoneLoadout loadout = GetStone(stoneIndex);
        if (loadout == null || power == null || !loadout.powers.Remove(power))
            return false;

        InventoryChanged?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (stones.Count == 0)
            return;

        stones.Clear();
        InventoryChanged?.Invoke();
    }
}
