using System.Reflection;
using System.Text.Json;

namespace AzureCTL;

static class CatalogNames
{
    static readonly Dictionary<string,Dictionary<string,string>> Catalog=Load();
    static Dictionary<string,Dictionary<string,string>> Load()
    {
        var result=new Dictionary<string,Dictionary<string,string>>();
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AzureCTL.Names.json");
        if(stream==null)return result;
        using var data=JsonDocument.Parse(stream);
        foreach(var name in new[]{"pet_names","gear_names","soul_names","hero_names"})
            if(data.RootElement.TryGetProperty(name,out var rows))result[name]=rows.EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.GetString()??x.Name);
        return result;
    }
    public static string Get(string kind,string id,string fallback)=>Catalog.TryGetValue(kind,out var rows)&&rows.TryGetValue(id,out var name)?name:fallback;
}
