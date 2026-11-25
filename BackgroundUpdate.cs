#nullable disable
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AzureCTL;

// One check per ordinary launch. Ready updates install after the user closes the app.
sealed class BackgroundUpdate : IDisposable
{
    readonly Form owner;
    readonly Func<HttpClient> createHttp;
    readonly bool autoCheck;
    internal event Action<string,bool> StatusChanged;
    void Status(string text,bool busy=false)=>StatusChanged?.Invoke(text,busy);
    readonly CancellationTokenSource cancel=new();
    string readyStage;
    FileStream readyLease;
    readonly object gate=new();
    Task checkTask;
    bool started,closed,handedOff,disposed,finished;
    internal const string ReadyText="Update downloaded — Restart now to apply";
    internal bool IsReady { get { lock(gate)return readyStage!=null&&!handedOff&&!closed&&!disposed; } }
    internal bool InstallAndRestart()
    {
        lock(gate)
        {
            if(!IsReady)return false;
            try{StartInstaller(true);}
            catch(Exception ex){LogError(ex);Status("Could not start installer — try Restart again");return false;}
        }
        Status("Installing update…",true);
        owner.Close();
        return true;
    }
    internal static ProcessStartInfo InstallerInfo(string stage,bool restart)
    {
        var info=new ProcessStartInfo(Path.Combine(stage,"AzureCTL.exe")){UseShellExecute=false,WorkingDirectory=stage,CreateNoWindow=true};
        info.ArgumentList.Add("--apply-update");info.ArgumentList.Add(AppContext.BaseDirectory);
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        info.ArgumentList.Add(Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());
        if(!restart)info.ArgumentList.Add("--no-restart");
        return info;
    }
    void StartInstaller(bool restart)
    {
        Directory.SetLastWriteTimeUtc(readyStage,DateTime.UtcNow);
        var helper=Path.Combine(readyStage,"AzureCTL.exe");File.Copy(Environment.ProcessPath,helper,true);
        using var process=Process.Start(InstallerInfo(readyStage,restart)) ?? throw new IOException("Could not start update installer.");
        handedOff=true;
    }
    internal BackgroundUpdate(Form owner,Func<HttpClient> createHttp=null,bool autoCheck=true)
    {
        this.owner=owner;
        this.autoCheck=autoCheck;
        this.createHttp=createHttp ?? (()=>new HttpClient{Timeout=TimeSpan.FromMinutes(10)});
        owner.Shown+=Shown;
        owner.FormClosed+=Closed;
    }
    void Shown(object sender,EventArgs e)
    {
        if(autoCheck && !started)_=CheckNowAsync();
    }
    internal Task CheckNowAsync()
    {
        lock(gate)
        {
            if(closed||disposed)return Task.CompletedTask;
            if(checkTask!=null&&!checkTask.IsCompleted)return checkTask;
            if(readyStage!=null){Status(ReadyText);return Task.CompletedTask;}
            started=true;finished=false;
            checkTask=Task.Run(async()=>{
                try{Status("Checking for updates…",true);await CheckAsync().ConfigureAwait(false);}
                catch(Exception ex){LogError(ex);Status("Update check failed — try again");}
                finally{lock(gate){finished=true;if(disposed)cancel.Dispose();}}
            });
            return checkTask;
        }
    }
    async Task CheckAsync()
    {
        string stage=null;FileStream lease=null;
        try
        {
            using var http=createHttp();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AzureCTL/"+AppUpdate.Current);
            using var check=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            check.CancelAfter(TimeSpan.FromSeconds(10));
            using var response=await http.GetAsync(AppUpdate.Latest,HttpCompletionOption.ResponseHeadersRead,check.Token);
            if(response.StatusCode==HttpStatusCode.NotFound){Status("No updates available");return;}
            response.EnsureSuccessStatusCode();
            using var memory=new MemoryStream();
            await using(var body=await response.Content.ReadAsStreamAsync(check.Token))
            {
                var buffer=new byte[16384];int count;
                while((count=await body.ReadAsync(buffer,check.Token))>0)
                {
                    if(memory.Length+count>1024*1024)throw new InvalidDataException("Release metadata is too large.");
                    memory.Write(buffer,0,count);
                }
            }
            using var release=JsonDocument.Parse(Encoding.UTF8.GetString(memory.ToArray()));
            var root=release.RootElement;var tag=root.GetProperty("tag_name").GetString();
            if(AppUpdate.ParseVersion(tag)<=AppUpdate.Current){Status("AzureCTL is up to date");return;}
            if(root.TryGetProperty("draft",out var draft)&&draft.GetBoolean() || root.TryGetProperty("prerelease",out var pre)&&pre.GetBoolean()){Status("No updates available");return;}
            var asset=root.GetProperty("assets").EnumerateArray().SingleOrDefault(x=>x.GetProperty("name").GetString()=="AzureCTL.exe");
            if(asset.ValueKind==JsonValueKind.Undefined)throw new InvalidDataException("Release has no AzureCTL.exe.");
            var digest=asset.GetProperty("digest").GetString();
            if(digest==null || !digest.StartsWith("sha256:",StringComparison.Ordinal))throw new InvalidDataException("Release executable has no SHA-256 digest.");
            var manifest=AppUpdate.ReadManifest(JsonSerializer.Serialize(new AppUpdate.Manifest(tag,asset.GetProperty("browser_download_url").GetString(),digest[7..],asset.GetProperty("size").GetInt64())));
            Status($"Downloading {tag}…",true);
            stage=Path.Combine(AppUpdate.StageRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            lease=UpdateWindow.OpenStageLease(stage);
            await File.WriteAllTextAsync(Path.Combine(stage,"update.json"),JsonSerializer.Serialize(manifest),check.Token);
            int previousPercent=-1;
            await AppUpdate.Download(http,AppUpdate.AssetUri(manifest.Url),Path.Combine(stage,"update.exe"),manifest.Size,cancel.Token,total=>{
                int percent=(int)Math.Clamp(total*100/Math.Max(1,manifest.Size),0,100);
                if(percent!=previousPercent){previousPercent=percent;Status($"Downloading {tag} — {percent}%",true);}
            });
            Status($"Verifying {tag}…",true);
            await Task.Run(()=>AppUpdate.ValidatePackage(Path.Combine(stage,"update.exe"),manifest),cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            lock(gate)
            {
                if(closed || disposed)return;
                readyStage=stage;readyLease=lease;stage=null;lease=null;
            }
            Status(ReadyText);
        }
        catch(OperationCanceledException){if(!cancel.IsCancellationRequested)Status("Update check timed out — try again");}
        catch(Exception ex){LogError(ex);Status("Update check failed — try again");}
        finally{lease?.Dispose();if(stage!=null)UpdateWindow.DeleteStage(stage);}
    }
    void Closed(object sender,FormClosedEventArgs e)
    {
        lock(gate){closed=true;cancel.Cancel();}
        if(readyStage==null||handedOff)return;
        try
        {
            StartInstaller(false);
        }
        catch(Exception ex){LogError(ex);}
    }
    internal static void LogError(Exception ex)
    {
        try
        {
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AzureCTL");
            Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"update-error.txt"),DateTime.UtcNow+"\n"+ex.Message);
        }
        catch{}
    }
    public void Dispose()
    {
        lock(gate)
        {
            if(disposed)return;disposed=true;
            owner.Shown-=Shown;owner.FormClosed-=Closed;
            cancel.Cancel();
            readyLease?.Dispose();readyLease=null;
            if(!handedOff && readyStage!=null)UpdateWindow.DeleteStage(readyStage);
            if(!started||finished)cancel.Dispose();
        }
        // Network continuations run off the UI thread, so cancellation can close
        // file handles and remove a partial stage before the process terminates.
        try{checkTask?.Wait(TimeSpan.FromSeconds(2));}catch{}
    }
}
