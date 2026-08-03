using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;

namespace CustomItems;

/// <summary>
/// A light, generic "which custom item is this serial?" registry — the one primitive every custom-item
/// plugin re-implements by hand (reinforcements' <c>ReinforcementState</c> tracked dict, goc-nuke's
/// <c>GocNukeState</c>, EnhancedNTFGun's <c>SpecialItemTracker</c>, EnhancedShotgun's <c>ShotgunTracker</c>).
/// It maps a vanilla item serial to a plugin-defined <typeparamref name="TKind"/> enum, so overlay items can
/// gate their event handlers on "is this tracked serial one of mine?".
///
/// This is mechanism only: it does NOT spawn, give, or know what any item does. Each plugin keeps owning its
/// item logic and simply <see cref="Mark(ushort, TKind)"/>s a serial when it grants one and gates on
/// <see cref="IsKind"/>/<see cref="TryGetKind"/>. Create one instance per plugin (or per item domain) and
/// <see cref="Clear"/> it on round reset.
/// </summary>
/// <typeparam name="TKind">The plugin's own item-kind enum.</typeparam>
public sealed class ItemRegistry<TKind>
    where TKind : struct, Enum
{
    private readonly Dictionary<ushort, TKind> _kinds = new();

    /// <summary>Number of currently tracked serials.</summary>
    public int Count => _kinds.Count;

    /// <summary>Marks an item serial as the given kind (overwrites any previous kind for that serial).</summary>
    public void Mark(ushort serial, TKind kind) => _kinds[serial] = kind;

    /// <summary>Marks a live item instance as the given kind.</summary>
    public void Mark(Item item, TKind kind)
    {
        if (item != null)
        {
            _kinds[item.Serial] = kind;
        }
    }

    /// <summary>Marks a dropped pickup as the given kind (the serial survives the item↔pickup transition).</summary>
    public void Mark(Pickup pickup, TKind kind)
    {
        if (pickup != null)
        {
            _kinds[pickup.Serial] = kind;
        }
    }

    /// <summary>True if <paramref name="serial"/> is tracked as exactly <paramref name="kind"/>.</summary>
    public bool IsKind(ushort serial, TKind kind) =>
        _kinds.TryGetValue(serial, out TKind tracked) && EqualityComparer<TKind>.Default.Equals(tracked, kind);

    /// <summary>True if <paramref name="serial"/> is tracked as any kind, returning that kind.</summary>
    public bool TryGetKind(ushort serial, out TKind kind) => _kinds.TryGetValue(serial, out kind);

    /// <summary>True if <paramref name="serial"/> is tracked as any kind.</summary>
    public bool IsTracked(ushort serial) => _kinds.ContainsKey(serial);

    /// <summary>Stops tracking a serial (e.g. when the item is consumed/destroyed). Safe if untracked.</summary>
    public bool Unmark(ushort serial) => _kinds.Remove(serial);

    /// <summary>Drops all tracking. Call on round reset so serials never leak across rounds.</summary>
    public void Clear() => _kinds.Clear();

    /// <summary>Snapshot of all tracked (serial, kind) pairs, for diagnostics.</summary>
    public IReadOnlyDictionary<ushort, TKind> Snapshot() => new Dictionary<ushort, TKind>(_kinds);
}
