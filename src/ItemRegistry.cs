using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

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
    private readonly Dictionary<ushort, Entry> _items = new();
    private readonly string _owner;

    /// <summary>Creates a registry with the default diagnostics owner tag (API 1-compatible constructor).</summary>
    public ItemRegistry()
        : this("CustomItems")
    {
    }

    public ItemRegistry(string owner)
    {
        _owner = string.IsNullOrWhiteSpace(owner) ? "CustomItems" : owner.Trim();
    }

    /// <summary>
    /// Enables one DEBUG line per lifecycle transition. Keep this off in normal production operation;
    /// <see cref="LifecycleChanged"/> remains available for structured diagnostics and tests.
    /// </summary>
    public bool TraceLifecycle { get; set; }

    /// <summary>Raised after each accepted lifecycle transition.</summary>
    public event Action<TrackedItemLifecycleEvent<TKind>>? LifecycleChanged;

    /// <summary>Number of currently tracked serials.</summary>
    public int Count => _items.Count;

    /// <summary>Marks an item serial as the given kind (overwrites any previous kind for that serial).</summary>
    public void Mark(ushort serial, TKind kind) => Track(serial, kind, ownerUserId: null, source: nameof(Mark));

    /// <summary>Marks a live item instance as the given kind.</summary>
    public void Mark(Item item, TKind kind)
    {
        if (item != null)
        {
            Track(item.Serial, kind, item.CurrentOwner?.UserId, nameof(Mark));
        }
    }

    /// <summary>Marks a dropped pickup as the given kind (the serial survives the item↔pickup transition).</summary>
    public void Mark(Pickup pickup, TKind kind)
    {
        if (pickup != null)
        {
            Track(pickup.Serial, kind, ownerUserId: null, source: nameof(Mark));
        }
    }

    /// <summary>Records creation by a grant path, then tracks the resulting serial and holder.</summary>
    public void TrackGranted(
        ushort serial,
        TKind kind,
        string? ownerUserId = null,
        [CallerMemberName] string source = "")
    {
        Publish(serial, kind, TrackedItemLifecycleStage.Grant, source, fromUserId: null, ownerUserId);
        Track(serial, kind, ownerUserId, source);
    }

    /// <summary>Tracks or reclassifies a serial and optionally records its current holder.</summary>
    public void Track(
        ushort serial,
        TKind kind,
        string? ownerUserId = null,
        [CallerMemberName] string source = "")
    {
        string? previousOwner = _items.TryGetValue(serial, out Entry previous) ? previous.OwnerUserId : null;
        string? owner = ownerUserId;
        _items[serial] = new Entry(kind, owner);
        Publish(serial, kind, TrackedItemLifecycleStage.Track, source, previousOwner, owner);
    }

    /// <summary>Records that a tracked serial moved into another player's inventory.</summary>
    public bool Transfer(
        ushort serial,
        string? toUserId,
        [CallerMemberName] string source = "")
    {
        if (!_items.TryGetValue(serial, out Entry entry))
        {
            return false;
        }

        if (string.Equals(entry.OwnerUserId, toUserId, StringComparison.Ordinal))
        {
            return true;
        }

        _items[serial] = new Entry(entry.Kind, toUserId);
        Publish(serial, entry.Kind, TrackedItemLifecycleStage.Transfer, source, entry.OwnerUserId, toUserId);
        return true;
    }

    /// <summary>Records that a tracked serial left its holder and now exists as a world pickup.</summary>
    public bool Drop(
        ushort serial,
        string? fromUserId = null,
        [CallerMemberName] string source = "")
    {
        if (!_items.TryGetValue(serial, out Entry entry))
        {
            return false;
        }

        string? previousOwner = fromUserId ?? entry.OwnerUserId;
        _items[serial] = new Entry(entry.Kind, ownerUserId: null);
        Publish(serial, entry.Kind, TrackedItemLifecycleStage.Drop, source, previousOwner, toUserId: null);
        return true;
    }

    /// <summary>Records permanent destruction and removes the serial from the registry.</summary>
    public bool Destroy(ushort serial, [CallerMemberName] string source = "")
    {
        if (!_items.TryGetValue(serial, out Entry entry))
        {
            return false;
        }

        Publish(serial, entry.Kind, TrackedItemLifecycleStage.Destroy, source, entry.OwnerUserId, toUserId: null);
        _items.Remove(serial);
        Publish(serial, entry.Kind, TrackedItemLifecycleStage.Untrack, source, entry.OwnerUserId, toUserId: null);
        return true;
    }

    /// <summary>True if <paramref name="serial"/> is tracked as exactly <paramref name="kind"/>.</summary>
    public bool IsKind(ushort serial, TKind kind) =>
        _items.TryGetValue(serial, out Entry tracked) && EqualityComparer<TKind>.Default.Equals(tracked.Kind, kind);

    /// <summary>True if <paramref name="serial"/> is tracked as any kind, returning that kind.</summary>
    public bool TryGetKind(ushort serial, out TKind kind)
    {
        if (_items.TryGetValue(serial, out Entry entry))
        {
            kind = entry.Kind;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>Returns the currently recorded holder UserId, or null for a world pickup/no known holder.</summary>
    public bool TryGetOwner(ushort serial, out string? ownerUserId)
    {
        if (_items.TryGetValue(serial, out Entry entry))
        {
            ownerUserId = entry.OwnerUserId;
            return true;
        }

        ownerUserId = null;
        return false;
    }

    /// <summary>True if <paramref name="serial"/> is tracked as any kind.</summary>
    public bool IsTracked(ushort serial) => _items.ContainsKey(serial);

    /// <summary>Stops tracking a serial (e.g. when the item is consumed/destroyed). Safe if untracked.</summary>
    public bool Unmark(ushort serial) => Untrack(serial, nameof(Unmark));

    /// <summary>Stops tracking a serial while preserving source attribution in lifecycle diagnostics.</summary>
    public bool Untrack(ushort serial, [CallerMemberName] string source = "")
    {
        if (!_items.TryGetValue(serial, out Entry entry))
        {
            return false;
        }

        _items.Remove(serial);
        Publish(serial, entry.Kind, TrackedItemLifecycleStage.Untrack, source, entry.OwnerUserId, toUserId: null);
        return true;
    }

    /// <summary>Drops all tracking. Call on round reset so serials never leak across rounds.</summary>
    public void Clear()
    {
        foreach (KeyValuePair<ushort, Entry> pair in new Dictionary<ushort, Entry>(_items))
        {
            Publish(pair.Key, pair.Value.Kind, TrackedItemLifecycleStage.Untrack, nameof(Clear), pair.Value.OwnerUserId, toUserId: null);
        }

        _items.Clear();
    }

    /// <summary>Snapshot of all tracked (serial, kind) pairs, for diagnostics.</summary>
    public IReadOnlyDictionary<ushort, TKind> Snapshot()
    {
        Dictionary<ushort, TKind> snapshot = new(_items.Count);
        foreach (KeyValuePair<ushort, Entry> pair in _items)
        {
            snapshot[pair.Key] = pair.Value.Kind;
        }

        return snapshot;
    }

    private void Publish(
        ushort serial,
        TKind kind,
        TrackedItemLifecycleStage stage,
        string source,
        string? fromUserId,
        string? toUserId)
    {
        var transition = new TrackedItemLifecycleEvent<TKind>(
            serial,
            kind,
            stage,
            string.IsNullOrWhiteSpace(source) ? "unknown" : source,
            fromUserId,
            toUserId);
        if (TraceLifecycle)
        {
            Logger.Debug(
                $"[CustomItems:{_owner}] lifecycle stage={stage} serial={serial} kind={kind} " +
                $"from={fromUserId ?? "world"} to={toUserId ?? "world"} source={transition.Source}",
                true);
        }

        Action<TrackedItemLifecycleEvent<TKind>>? subscribers = LifecycleChanged;
        if (subscribers == null)
        {
            return;
        }

        foreach (Action<TrackedItemLifecycleEvent<TKind>> subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber(transition);
            }
            catch (Exception exception)
            {
                Logger.Warn(
                    $"[CustomItems:{_owner}] Lifecycle subscriber threw at stage={stage} serial={serial} " +
                    $"source={transition.Source}: {exception.GetBaseException()}");
            }
        }
    }

    private readonly struct Entry
    {
        public Entry(TKind kind, string? ownerUserId)
        {
            Kind = kind;
            OwnerUserId = ownerUserId;
        }

        public TKind Kind { get; }

        public string? OwnerUserId { get; }
    }
}
