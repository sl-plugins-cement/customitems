namespace CustomItems;

/// <summary>Version marker for the shared CustomItems runtime contract.</summary>
public static class CustomItemsApi
{
    /// <summary>
    /// API 2 adds the tracked-item lifecycle contract while retaining the API 1 registry and held-mesh
    /// signatures for already-built consumers.
    /// </summary>
    public const int Version = 2;
}
