using ServerKeybinds;

namespace CustomItems;

/// <summary>Runtime contract for shared infrastructure required by CustomItems consumers.</summary>
public static class SharedDependencies
{
    /// <summary>The loaded CustomItems API version.</summary>
    public static int CustomItemsApiVersion => CustomItemsApi.Version;

    /// <summary>The loaded ServerKeybinds API version.</summary>
    public static int ServerKeybindsApiVersion => KeybindRegistry.ApiVersion;
}
