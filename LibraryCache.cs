namespace AzureCTL;

// Lists belong to a specific checkout/account. Refresh and mutations invalidate
// their tasks too, so an old in-flight response cannot repopulate a new cache.
public sealed class LibraryCache
{
    readonly Dictionary<(string Root,uint Player,string Category),Task<List<LibraryItem>>> lists=[];
    static string RootKey(string root)=>Path.GetFullPath(root).TrimEnd('\\','/').ToUpperInvariant();
    public Task<List<LibraryItem>> GetAsync(string root,uint player,string category,Func<Task<List<LibraryItem>>> load)
    {
        var key=(RootKey(root),player,category);
        if(lists.TryGetValue(key,out var existing)&&!existing.IsFaulted&&!existing.IsCanceled)return existing;
        return lists[key]=load();
    }
    public void Clear()=>lists.Clear();
    public void Invalidate(string root,uint player,string category)=>lists.Remove((RootKey(root),player,category));
    public void Invalidate(string root,uint player)
    {
        var normalized=RootKey(root);
        foreach(var key in lists.Keys.Where(x=>x.Root==normalized&&x.Player==player).ToArray())lists.Remove(key);
    }
}
