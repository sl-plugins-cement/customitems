namespace CustomItems;

/// <summary>
/// How a custom held mesh relates to the holder's native first-person viewmodel.
/// </summary>
public enum HeldVisualMode
{
    /// <summary>No custom mesh and no viewmodel change (the "off" setting).</summary>
    None,

    /// <summary>Show the custom mesh while the native viewmodel stays visible (a cosmetic add-on).</summary>
    Overlay,

    /// <summary>
    /// Force-deselect the native item so its viewmodel never renders, and show the custom mesh in its
    /// place — a full visual takeover (the SRA jar / Medic medkit behaviour). The deselect raises a
    /// <c>ChangedItem(None)</c> the caller must absorb via <see cref="HeldMeshManager.AbsorbForcedNone"/>.
    /// </summary>
    HideAndReplace,
}
