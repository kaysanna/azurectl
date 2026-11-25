#nullable disable
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace AzureCTL;

// Shared by the downloader and separate installer process. Packages contain only
// the executable, including its embedded license. Server files and settings are
// never copied or replaced.
static class AppUpdate
{
    internal const string Project = "https://github.com/kaysanna/azurectl";
    internal const string Latest = "https://api.github.com/repos/kaysanna/azurectl/releases/latest";
    internal const long MaxPackage = 200 * 1024 * 1024;
    internal static string StageRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AzureCTL","Updates");
    internal static void CleanupStages()
    {
        try
        {
            if(!Directory.Exists(StageRoot))return;
            CheckDirectory(StageRoot);
            foreach(var dir in Directory.GetDirectories(StageRoot))
            {
                if(!Guid.TryParseExact(Path.GetFileName(dir),"N",out _))continue;
                if(!File.Exists(Path.Combine(dir,"completed")) && Directory.GetLastWriteTimeUtc(dir)>DateTime.UtcNow.AddDays(-1))continue;
                try
                {
                    CheckDirectory(dir);
                    // Downloaders and installers keep this shared lease open, even
                    // while an update waits days for its owning app to close.
                    using(var lease=new FileStream(Path.Combine(dir,"active.lease"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){}
                    if(Directory.EnumerateFileSystemEntries(dir,"*",SearchOption.AllDirectories).Any(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0))continue;
                    Directory.Delete(dir,true);
                }
                catch{}
            }
        }
        catch{}
    }
    internal sealed record Manifest(string Version, string Url, string Sha256, long Size);
    internal static Version Current => typeof(AppUpdate).Assembly.GetName().Version;
    internal static Version ParseVersion(string text)
    {
        if (!Version.TryParse(text?.TrimStart('v'), out var v) || v.Major < 0 || v.Minor < 0)
            throw new InvalidDataException("Invalid update version.");
        return new Version(v.Major, v.Minor, Math.Max(0,v.Build), Math.Max(0,v.Revision));
    }
    internal static Uri AssetUri(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/kaysanna/azurectl/releases/download/", StringComparison.Ordinal) ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Update link does not belong to the AzureCTL GitHub repository.");
        return uri;
    }
    internal static Manifest ReadManifest(string json)
    {
        var m = JsonSerializer.Deserialize<Manifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive=true })
            ?? throw new InvalidDataException("Empty update manifest.");
        ParseVersion(m.Version); AssetUri(m.Url);
        if (m.Size < 1 || m.Size > MaxPackage || m.Sha256?.Length != 64 || !m.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid update size or checksum.");
        return m;
    }
    internal static string[] ValidatePackage(string payloadPath, Manifest manifest)
    {
        var info=new FileInfo(payloadPath);
        if(info.Length != manifest.Size)throw new InvalidDataException("Update download is incomplete.");
        using(var input=File.OpenRead(payloadPath))
            if(!Convert.ToHexString(SHA256.HashData(input)).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update checksum does not match.");
        using(var input=File.OpenRead(payloadPath))
            if(input.ReadByte()!=77 || input.ReadByte()!=90)throw new InvalidDataException("Invalid Windows executable.");
        return ["AzureCTL.exe"];
    }

    internal static async Task Download(HttpClient http, Uri url, string path, long limit, CancellationToken token, Action<long> progress=null)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));token=timeout.Token;
        using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength is long length && length>limit)throw new InvalidDataException("Update is too large.");
        await using var input=await response.Content.ReadAsStreamAsync(token);
        await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);
        byte[] buffer=new byte[65536];long total=0;int read;
        while((read=await input.ReadAsync(buffer,token))>0)
        {
            total+=read;if(total>limit)throw new InvalidDataException("Update is too large.");
            await output.WriteAsync(buffer.AsMemory(0,read),token);progress?.Invoke(total);
        }
    }
    // Prepare and validate beside the installed EXE, then replace it in one same-volume
    // rename. A failed replacement leaves the original EXE intact; no backup is retained.
    internal static void Install(string stage,string target,Manifest manifest)
    {
        target=Path.GetFullPath(target);
        CheckDirectory(target);
        CheckDirectory(stage);
        var executable=Path.Combine(target,"AzureCTL.exe");
        if(!File.Exists(executable))throw new IOException("Original AzureCTL executable was not found.");
        if((File.GetAttributes(executable)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Cannot replace a linked file.");
        var payloadPath=Path.Combine(stage,"update.exe");
        ValidatePackage(payloadPath,manifest);
        var temp=executable+".update-"+Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(payloadPath,temp,false);
            var incoming=System.Diagnostics.FileVersionInfo.GetVersionInfo(temp).FileVersion;
            if(ParseVersion(incoming)!=ParseVersion(manifest.Version))
                throw new InvalidDataException("Executable version does not match release.");
            using(var input=File.OpenRead(temp))
                if(input.ReadByte()!=77 || input.ReadByte()!=90)throw new InvalidDataException("Invalid Windows executable.");
            CheckDirectory(target);
            if((File.GetAttributes(executable)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Cannot replace a linked file.");
            File.Move(temp,executable,true);
        }
        finally
        {
            // Cleanup must not hide the original validation/replacement error.
            try{if(File.Exists(temp))File.Delete(temp);}catch{}
        }
    }
    internal static void CheckDirectory(string path)
    {
        for(var dir=new DirectoryInfo(path);dir!=null;dir=dir.Parent)
            if((dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Cannot update through a redirected directory.");
    }
}
