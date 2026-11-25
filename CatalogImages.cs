using System.Reflection;

namespace AzureCTL;

// Embedded, bounded 96px thumbnails from the official JP client.
// Inventory GUIDs are never used as equipment/species asset IDs.
static class CatalogImages
{
    static readonly Dictionary<string,Image?> Cache = new(StringComparer.Ordinal);
    public static Image? Get(string? key)
    {
        if(key==null)return null;
        if(Cache.TryGetValue(key,out var cached))return cached;
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AzureCTL.Images."+key.Replace('/','.')+".png");
        if(stream==null)return Cache[key]=null;
        using var image=Image.FromStream(stream);
        return Cache[key]=new Bitmap(image);
    }
}
