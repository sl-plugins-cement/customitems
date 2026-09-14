using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

namespace CustomItems;

/// <summary>
/// The full reusable "custom held mesh" pattern, including the viewmodel-hide (force-deselect) the SRA and
/// Medic tools both do. Create one per service/item domain; it tracks per-player visuals and the
/// force-deselect bookkeeping so a service only has to say <see cref="Show"/> on equip, <see cref="Hide"/> on
/// unequip, and <see cref="AbsorbForcedNone"/> in its own <c>ChangedItem</c> handler.
///
/// It deliberately does NOT own "armed" state or arm semantics (deploy-on-repress, heal channels, etc.) —
/// those are item-specific and stay in the owning service. This manager owns only the mesh and the
/// viewmodel-hide, parameterised by <see cref="HeldVisualMode"/>.
/// </summary>
public sealed class HeldMeshManager
{
    private readonly string _logTag;
    private readonly Dictionary<int, HeldMeshVisual> _visuals = new();
    private readonly HashSet<int> _suppressForcedNone = new();

    /// <param name="logTag">Short owner tag for log lines, e.g. "SRA" or "MedicHeal".</param>
    public HeldMeshManager(string logTag = "CustomItems")
    {
        _logTag = logTag;
    }

    /// <summary>True if a live held mesh is currently shown for this player.</summary>
    public bool IsShown(Player player) =>
        player != null && _visuals.TryGetValue(player.PlayerId, out HeldMeshVisual visual) && !visual.IsDestroyed;

    /// <summary>
    /// Shows (or refreshes) the held mesh for a player according to <paramref name="mode"/>:
    /// <see cref="HeldVisualMode.None"/> shows nothing; <see cref="HeldVisualMode.Overlay"/> shows the mesh
    /// over the native viewmodel; <see cref="HeldVisualMode.HideAndReplace"/> additionally force-deselects the
    /// native item (and remembers to absorb the resulting <c>ChangedItem(None)</c>). Call on tool equip.
    /// </summary>
    public void Show(Player player, HeldMeshSpec spec, HeldVisualMode mode)
    {
        if (player == null || player.IsDestroyed || mode == HeldVisualMode.None)
        {
            return;
        }

        int id = player.PlayerId;
        if (_visuals.TryGetValue(id, out HeldMeshVisual existing))
        {
            if (!existing.IsDestroyed)
            {
                if (mode == HeldVisualMode.HideAndReplace)
                {
                    ForceDeselect(player);
                }

                return;
            }

            existing.Destroy();
            _visuals.Remove(id);
        }

        HeldMeshVisual visual = new(player, spec);
        if (visual.Spawn())
        {
            _visuals[id] = visual;
        }
        else if (spec.PreserveAuthoredOrigin)
        {
            // A missing canonical asset must not hide the only usable/native presentation.
            return;
        }

        if (mode == HeldVisualMode.HideAndReplace)
        {
            ForceDeselect(player);
        }
    }

    /// <summary>
    /// Force-deselects the player's current item to re-hide the viewmodel without re-spawning the mesh
    /// (e.g. after a failed deploy that must keep the tool hidden). Use when the mesh is already shown.
    /// </summary>
    public void ReHide(Player player)
    {
        if (player != null && !player.IsDestroyed)
        {
            ForceDeselect(player);
        }
    }

    /// <summary>
    /// Call from your <c>ChangedItem</c> handler when <c>NewItem == null</c>: returns true if this deselect
    /// was the manager's own force-deselect (so the caller stays armed and ignores it), false for a genuine
    /// deselect (so the caller should disarm and <see cref="Hide"/>).
    /// </summary>
    public bool AbsorbForcedNone(Player player) =>
        player != null && _suppressForcedNone.Remove(player.PlayerId);

    /// <summary>Tears down the held mesh and clears the player's force-deselect bookkeeping. Call on unequip/death/leave.</summary>
    public void Hide(Player player)
    {
        if (player == null)
        {
            return;
        }

        int id = player.PlayerId;
        if (_visuals.TryGetValue(id, out HeldMeshVisual visual))
        {
            visual.Destroy();
            _visuals.Remove(id);
        }

        _suppressForcedNone.Remove(id);
    }

    /// <summary>Tears down every held mesh and clears all state. Call on round reset / plugin disable.</summary>
    public void Clear()
    {
        foreach (HeldMeshVisual visual in _visuals.Values.ToList())
        {
            visual.Destroy();
        }

        _visuals.Clear();
        _suppressForcedNone.Clear();
    }

    private void ForceDeselect(Player player)
    {
        _suppressForcedNone.Add(player.PlayerId);
        try
        {
            player.CurrentItem = null;
        }
        catch (Exception exception)
        {
            _suppressForcedNone.Remove(player.PlayerId);
            Logger.Warn($"[CustomItems:{_logTag}] Failed to force-deselect to hide the viewmodel: {exception.GetBaseException().Message}");
        }
    }
}
