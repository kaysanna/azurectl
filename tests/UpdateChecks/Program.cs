using AzureCTL;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

class Checks
{
 [STAThread] static void Main(string[] args)
 {
  ApplicationConfiguration.Initialize();
  if(args.Length>0)PackageChecks(args);
  void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
  Check(!BackgroundUpdate.InstallerInfo("stage",true).ArgumentList.Contains("--no-restart"),"explicit restart runs install helper with restart enabled");
  Check(BackgroundUpdate.InstallerInfo("stage",false).ArgumentList.Contains("--no-restart"),"ordinary close still installs without reopening");
  using(var owner=new Form())using(var updater=new BackgroundUpdate(owner,autoCheck:false))Check(!updater.InstallAndRestart()&&!owner.IsDisposed,"restart unavailable before a verified download");
  var shown=typeof(BackgroundUpdate).GetMethod("Shown",BindingFlags.NonPublic|BindingFlags.Instance);
  using(var form=new Form())
  {
   var h=new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
   using var update=new BackgroundUpdate(form,()=>new HttpClient(h,false));
   var states=new ConcurrentQueue<(string text,bool busy)>();update.StatusChanged+=(t,b)=>states.Enqueue((t,b));
   shown.Invoke(update,new object[]{form,EventArgs.Empty});update.CheckNowAsync().GetAwaiter().GetResult();
   shown.Invoke(update,new object[]{form,EventArgs.Empty});
   Check(h.Calls==1,"one automatic check even after repeated Shown");
   update.CheckNowAsync().GetAwaiter().GetResult();
   Check(h.Calls==2,"manual check can run again after automatic check");
   Check(states.Last()==("No updates available",false),"no-update status enables button");
  }
  using(var form=new Form())
  {
   var release=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
   var h=new Handler((_,ct)=>release.Task.WaitAsync(ct));using var update=new BackgroundUpdate(form,()=>new HttpClient(h,false),false);
   var first=update.CheckNowAsync();var second=update.CheckNowAsync();
   Check(ReferenceEquals(first,second),"repeated clicks share one pending check");
   release.SetResult(Json(new{tag_name="v"+AppUpdate.Current.ToString(3)}));first.GetAwaiter().GetResult();
   Check(h.Calls==1,"concurrent manual checks make one request");
  }
  using(var form=new Form())
  {
   int attempt=0;var h=new Handler((_,_)=>Task.FromResult(++attempt==1?new HttpResponseMessage(HttpStatusCode.ServiceUnavailable):Json(new{tag_name="v"+AppUpdate.Current.ToString(3)})));
   using var update=new BackgroundUpdate(form,()=>new HttpClient(h,false),false);
   string status="";bool busy=true;update.StatusChanged+=(t,b)=>{status=t;busy=b;};
   update.CheckNowAsync().GetAwaiter().GetResult();Check(status.Contains("failed")&&!busy,"failure restores button and reports error");
   update.CheckNowAsync().GetAwaiter().GetResult();Check(status=="AzureCTL is up to date"&&!busy,"manual retry succeeds after failure");
  }
  using(var form=new Form())
  {
   byte[] payload=[77,90,1,2,3];
   var h=new Handler((request,_)=>Task.FromResult(request.RequestUri.AbsoluteUri==AppUpdate.Latest
    ?Json(new{tag_name="v99.0.0",assets=new[]{new{name="AzureCTL.exe",browser_download_url="https://github.com/kaysanna/azurectl/releases/download/v99.0.0/AzureCTL.exe",digest="sha256:"+Convert.ToHexString(SHA256.HashData(payload)),size=payload.Length}}})
    :new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(payload)}));
   using var update=new BackgroundUpdate(form,()=>new HttpClient(h,false),false);var states=new ConcurrentQueue<(string text,bool busy)>();update.StatusChanged+=(t,b)=>states.Enqueue((t,b));
   update.CheckNowAsync().GetAwaiter().GetResult();
   Check(states.Any(s=>s.text=="Downloading v99.0.0 — 100%"&&s.busy),"download percentage reported: "+string.Join("; ",states.Select(s=>s.text)));
   Check(states.Any(s=>s.text=="Verifying v99.0.0…"&&s.busy),"verification status reported");
   Check(states.Last()==(BackgroundUpdate.ReadyText,false),"ready update shown without mid-session install");
   update.CheckNowAsync().GetAwaiter().GetResult();Check(h.Calls==2,"ready update uses only release API and EXE, with no manifest download");
  }
 }

 static void PackageChecks(string[] args)
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
  void Reject(Action run,string name){try{run();}catch(InvalidDataException){Check(true,name);return;}catch(IOException){Check(true,name);return;}catch(UnauthorizedAccessException){Check(true,name);return;}throw new Exception("Accepted "+name);}
  foreach(var url in new[]{"http://github.com/kaysanna/azurectl/releases/download/v1/a","https://evil.test/a","https://github.com/kaysanna/azurectl-evil/releases/download/v1/a","https://user@github.com/kaysanna/azurectl/releases/download/v1/a"})Reject(()=>AppUpdate.AssetUri(url),"reject foreign update URL");
  var root=Path.Combine(Path.GetTempPath(),"AzureCTL-install-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var exe=Path.GetFullPath(args[0]);
  try
  {
   var payloadPath=Path.Combine(root,"update.exe");
   void ResetPayload(){File.Copy(exe,payloadPath,true);}
   AppUpdate.Manifest Manifest()=>new(System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileVersion,"https://github.com/kaysanna/azurectl/releases/download/v0.1.0/AzureCTL.exe",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(payloadPath))),new FileInfo(payloadPath).Length);
   ResetPayload();var m=Manifest();Check(AppUpdate.ValidatePackage(payloadPath,m).Length==1,"valid real executable package");
   Reject(()=>AppUpdate.ValidatePackage(payloadPath,m with{Size=m.Size+1}),"reject truncated package");Reject(()=>AppUpdate.ValidatePackage(payloadPath,m with{Sha256=new string('0',64)}),"reject checksum mismatch");
   File.WriteAllBytes(payloadPath,[1,2,3]);Reject(()=>AppUpdate.ValidatePackage(payloadPath,Manifest()),"reject invalid executable header");
   ResetPayload();m=Manifest();var target=Path.Combine(root,"app");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"AzureCTL.exe"),"original");File.WriteAllText(Path.Combine(target,"settings.json"),"keep settings");Directory.CreateDirectory(Path.Combine(target,"saves"));File.WriteAllText(Path.Combine(target,"saves/active.bin"),"keep save");
   AppUpdate.Install(root,target,m);Check(File.ReadAllBytes(Path.Combine(target,"AzureCTL.exe")).SequenceEqual(File.ReadAllBytes(exe)),"atomic executable install");Check(File.ReadAllText(Path.Combine(target,"settings.json"))=="keep settings"&&File.ReadAllText(Path.Combine(target,"saves/active.bin"))=="keep save","install preserves settings and save");
   Reject(()=>AppUpdate.Install(root,target,m with{Version="99.0.0"}),"reject executable version mismatch");
   using(var locked=new FileStream(Path.Combine(target,"AzureCTL.exe"),FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>AppUpdate.Install(root,target,m),"locked executable preserves original");
   Check(File.ReadAllBytes(Path.Combine(target,"AzureCTL.exe")).SequenceEqual(File.ReadAllBytes(exe)),"failed replacement preserves old executable");
   if(args.Length>1)
   {
    var candidate=Path.GetFullPath(args[1]);var stage=Path.Combine(AppUpdate.StageRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);File.Copy(exe,Path.Combine(stage,"AzureCTL.exe"));
    var package=Path.Combine(stage,"update.exe");File.Copy(candidate,package);
    var future=new AppUpdate.Manifest(System.Diagnostics.FileVersionInfo.GetVersionInfo(candidate).FileVersion,"https://github.com/kaysanna/azurectl/releases/download/v0.1.1/AzureCTL.exe",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))),new FileInfo(package).Length);
    File.WriteAllText(Path.Combine(stage,"update.json"),JsonSerializer.Serialize(future));
    var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(stage,"AzureCTL.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage};foreach(var arg in new[]{"--apply-update",target,int.MaxValue.ToString(),"0","--no-restart"})start.ArgumentList.Add(arg);
    using var process=System.Diagnostics.Process.Start(start);Check(process.WaitForExit(30000)&&process.ExitCode==0,"real separate installer process succeeds");
    Check(File.ReadAllBytes(Path.Combine(target,"AzureCTL.exe")).SequenceEqual(File.ReadAllBytes(candidate)),"real helper installs newer executable");Check(File.ReadAllText(Path.Combine(target,"settings.json"))=="keep settings"&&File.ReadAllText(Path.Combine(target,"saves/active.bin"))=="keep save","real helper preserves save and settings");
   }
  }
  finally{Directory.Delete(root,true);}
 }
 static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
 sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
 {
  internal int Calls;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Interlocked.Increment(ref Calls);return send(request,token);}
 }
}
