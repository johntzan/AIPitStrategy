// Stub to allow PluginPicture to compile when no embedded icon resource is present.
// Replace with actual resx/image when adding a real icon.
namespace PitStrategy.Plugin.Properties
{
    internal static class Resources
    {
        public static byte[]? IconBytes { get; } = null;
    }
}
