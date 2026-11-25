using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzureCTL;

// Only these queries bypass the PowerShell build/migration wrapper. Mutations
// continue through the existing wrapper and server-side validation.
public static class CtlReadTransport
{
    public static bool IsReadOnly(IReadOnlyList<string> a)=>
        a.Count==2&&a[1]=="list"&&a[0] is "hero" or "gear" or "soul" or "pet" ||
        a.Count==3&&a[1]=="inspect"&&a[0] is "hero" or "gear" or "pet" ||
        a.Count is 2 or 3&&a[0]=="catalog"&&a[1] is "heroes" or "gear" or "souls" or "sets" or "genes" ||
        a.Count==3&&a[0]=="world"&&a[1]=="difficulty"&&a[2]=="list";

    public static async Task<CommandResult?> TryRunAsync(CtlClient client,uint player,IReadOnlyList<string> command,CancellationToken cancel)
    {
        if(!IsReadOnly(command))return null;
        var control=client.SavePath+".ctl";var readyPath=Path.Combine(control,"ready.json");
        var catalog=command[0]=="catalog";
        if(File.Exists(readyPath))
        {
            using var ready=JsonDocument.Parse(await File.ReadAllTextAsync(readyPath,cancel));
            var r=ready.RootElement;
            if(r.GetProperty("protocol").GetInt32()!=1)throw new IOException("Unsupported live CTL protocol. Restart the server after updating it.");
            var instance=r.GetProperty("instance").GetString()??"";
            if(!Regex.IsMatch(instance,"^[a-f0-9]{32}$"))throw new IOException("Invalid live CTL endpoint.");
            Process owner;
            try{owner=Process.GetProcessById(r.GetProperty("pid").GetInt32());}catch(ArgumentException){return null;}
            using(owner)
            {
                string? path;try{path=owner.MainModule?.FileName;}catch{return null;}
                if(path==null)return null;
                if(!path.StartsWith(client.Root.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(path).Equals("zetsa-gamesv-jp.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Live CTL endpoint does not belong to this checkout.");
                if(catalog)return await RunExecutable(path,client,player,command,cancel);
                return await QueryLive(control,instance,owner,player,command,cancel);
            }
        }
        var exe=File.Exists(Path.Combine(client.Root,".local/client.json"))
            ?Path.Combine(client.Root,".local/chiyo/server-build/bin/zetsa-gamesv-jp.exe")
            :Path.Combine(client.Root,"zig-out/bin/zetsa-gamesv-jp.exe");
        return File.Exists(exe)?await RunExecutable(exe,client,player,command,cancel):null;
    }

    static async Task<CommandResult> QueryLive(string folder,string instance,Process owner,uint player,IReadOnlyList<string> command,CancellationToken cancel)
    {
        var id=Guid.NewGuid().ToString("N");var request=Path.Combine(folder,id+".request.json");var response=Path.Combine(folder,id+".response.json");var temp=Path.Combine(folder,id+".tmp");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            var args=new[]{player.ToString(CultureInfo.InvariantCulture)}.Concat(command).ToArray();
            await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(new{instance,args}),timeout.Token);File.Move(temp,request);
            while(!File.Exists(response))
            {
                if(owner.HasExited)throw new IOException("The live server stopped while reading the list.");
                await Task.Delay(20,timeout.Token);
            }
            if(new FileInfo(response).Length>8*1024*1024)throw new IOException("Live CTL response is too large.");
            using var reply=JsonDocument.Parse(await File.ReadAllTextAsync(response,timeout.Token));var r=reply.RootElement;
            var output=r.GetProperty("output").GetString()??"";var ok=r.GetProperty("ok").GetBoolean();
            return new(ok,output,ok?"":r.GetProperty("error").GetString()??"Read failed.");
        }
        catch(OperationCanceledException){throw new IOException("Live CTL read timed out. Refresh to try again.");}
        finally{foreach(var file in new[]{temp,request,response})try{File.Delete(file);}catch{}}
    }

    static async Task<CommandResult> RunExecutable(string exe,CtlClient client,uint player,IReadOnlyList<string> command,CancellationToken cancel)
    {
        var info=new ProcessStartInfo(exe){WorkingDirectory=client.Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment["AZUREPRO_SAVE_FILE"]=client.SavePath;info.ArgumentList.Add("ctl");
        if(command[0]!="catalog")info.ArgumentList.Add(player.ToString(CultureInfo.InvariantCulture));
        foreach(var arg in command)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new IOException("Could not start the compiled CTL reader.");
        var stdout=process.StandardOutput.ReadToEndAsync(cancel);var stderr=process.StandardError.ReadToEndAsync(cancel);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromSeconds(40));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){if(!process.HasExited)process.Kill();throw new IOException("CTL read timed out. Refresh to try again.");}
        var output=await stdout;var error=await stderr;
        return process.ExitCode==0?new(true,string.Join("\n",new[]{output.Trim(),error.Trim()}.Where(x=>x.Length>0)),""):new(false,output,error.Trim());
    }
}
