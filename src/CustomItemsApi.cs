namespace CustomItems;

/// <summary>Version marker for the shared CustomItems runtime contract.</summary>
public static class CustomItemsApi
{
    /// <summary>
    /// API 3 removes the ServerKeybinds dependency and is consumed as source: consumers compile these files
    /// into their own plugin assembly instead of loading a shared CustomItems.dll. The API 2 tracked-item
    /// lifecycle contract and the API 1 registry and held-mesh signatures are unchanged.
    /// </summary>
    public const int Version = 3;
}
