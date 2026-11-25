using AzureCTL;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

if(args.Length!=1)throw new ArgumentException("Pass a built Azurepro checkout. Tests copy its curated fixture and never edit its active save.");
var source=new CtlClient(args[0]).Root;
foreach(var category in new[]{"hero","soul"})
{
    var preset=Commands.Sections.Single(s=>s.Category==category).Operations.Single(o=>o.Template.SequenceEqual(new[]{category,"rarity-preset"}));
    Check(!preset.NeedsSelection&&preset.Fields.Length==0,"batch preset requires no individual selection");
    Check(!CtlReadTransport.IsReadOnly(preset.Expand("",new Dictionary<string,string>())),"batch preset uses mutation transport");
}
Check(CtlReadTransport.IsReadOnly(["hero","list"])&&CtlReadTransport.IsReadOnly(["catalog","heroes"])&&CtlReadTransport.IsReadOnly(["world","difficulty","list"]),"direct reader accepts list queries");
Check(!CtlReadTransport.IsReadOnly(["hero","set","107002","level","60"])&&!CtlReadTransport.IsReadOnly(["gear","remove","1"])&&!CtlReadTransport.IsReadOnly(["world","difficulty","set","100","7"]),"mutations cannot enter direct reader");
var cache=new LibraryCache();var loads=0;
Task<List<LibraryItem>> CachedLoad(){loads++;return Task.FromResult(new List<LibraryItem>{new("1","Fixture","cached")});}
await cache.GetAsync(source,2,"hero",CachedLoad);await cache.GetAsync(source.ToUpperInvariant(),2,"hero",CachedLoad);
Check(loads==1,"repeat tab reads reuse loaded data");
await cache.GetAsync(source,3,"hero",CachedLoad);await cache.GetAsync(source,2,"gear",CachedLoad);await cache.GetAsync(source+"-other",2,"hero",CachedLoad);
Check(loads==4,"tab caches isolate players, categories and checkout folders");
cache.Invalidate(source,2);await cache.GetAsync(source,2,"hero",CachedLoad);await cache.GetAsync(source,3,"hero",CachedLoad);
Check(loads==5,"mutation invalidates current player without crossing accounts");
var pending=new TaskCompletionSource<List<LibraryItem>>();var first=cache.GetAsync(source,2,"pet",()=>pending.Task);
Check(ReferenceEquals(first,cache.GetAsync(source,2,"pet",CachedLoad)),"preload and tab click share one request");
cache.Clear();var fresh=cache.GetAsync(source,2,"pet",CachedLoad);pending.SetResult([]);await first;
Check(ReferenceEquals(fresh,cache.GetAsync(source,2,"pet",CachedLoad)),"old in-flight result cannot replace refreshed data");
try{await cache.GetAsync(source,2,"failure",()=>Task.FromException<List<LibraryItem>>(new IOException("fixture")));}catch(IOException){}
Check((await cache.GetAsync(source,2,"failure",CachedLoad)).Count==1,"failed preload is retried");
var migrationCase=Path.Combine(Path.GetTempPath(),"azurectl-layout-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(migrationCase,"server/scripts"));Directory.CreateDirectory(Path.Combine(migrationCase,"saves"));
File.WriteAllText(Path.Combine(migrationCase,"server/scripts/ctl.ps1"),"");
File.WriteAllText(Path.Combine(migrationCase,"saves/active.bin"),"existing save sentinel");
try{_ = new CtlClient(migrationCase);throw new Exception("Missing migration was accepted");}
catch(IOException e){Check(e.Message.Contains("one-time folder migration"),"actionable mixed-layout error");}
Check(File.ReadAllText(Path.Combine(migrationCase,"saves/active.bin"))=="existing save sentinel"&&!File.Exists(Path.Combine(migrationCase,"server/saves/active.bin")),"folder detection never moves or replaces saves");
var stage=Path.Combine(Path.GetTempPath(),"azurectl-qa-"+Guid.NewGuid().ToString("N"),"server");
Directory.CreateDirectory(Path.Combine(stage,"scripts"));Directory.CreateDirectory(Path.Combine(stage,"saves"));Directory.CreateDirectory(Path.Combine(stage,"zig-out/bin"));Directory.CreateDirectory(Path.Combine(stage,"assets/jp/maps"));
foreach(var name in new[]{"ctl.ps1","layout.ps1","diagnostics.ps1","project-paths.ps1","save-bin.ps1"})File.Copy(Path.Combine(source,"scripts",name),Path.Combine(stage,"scripts",name));
foreach(var id in new[]{100,101})File.Copy(Path.Combine(source,$"assets/jp/maps/worldmap_{id}.bin"),Path.Combine(stage,$"assets/jp/maps/worldmap_{id}.bin"));
var exe=Path.Combine(stage,"zig-out/bin/zetsa-gamesv-jp.exe");
File.Copy(Path.Combine(source,"zig-out/bin/zetsa-gamesv-jp.exe"),exe);
File.Copy(Path.Combine(source,"assets/saves/maxed.bin"),Path.Combine(stage,"saves/active.bin"));
var ctl=new CtlClient(stage);
var bytes=File.ReadAllBytes(ctl.SavePath);
Check(SaveAccounts.Decode(bytes).Any(x=>x.Id==2),"fixture account discovery");
var bad=(byte[])bytes.Clone();bad[^1]^=1;ExpectBadSave(bad);
ExpectBadSave(bytes[..63]);bad=(byte[])bytes.Clone();bad[8]=2;ExpectBadSave(bad);
foreach(var section in Commands.Sections)foreach(var op in section.Operations)
{
    var command=op.Expand("107002",op.Fields.ToDictionary(x=>x.Key,x=>x.Default));
    Check(command.Length>1&&!command.Any(x=>x.Contains('{')||x.Length==0),"complete command template: "+op.Name);
}
var list=await ctl.RunAsync(2,["hero","list"]);
Check(list.Success&&CtlClient.ParseList("hero",list.Output).Count>=23,"offline stderr output and owned library");
var wrapper=Path.Combine(stage,"scripts/ctl.ps1");var originalWrapper=File.ReadAllText(wrapper);
try
{
    File.WriteAllText(wrapper,"throw 'Read query unexpectedly started PowerShell'");
    var timer=Stopwatch.StartNew();var fast=await ctl.RunAsync(2,["hero","list"]);timer.Stop();
    Check(fast.Success&&CtlClient.ParseList("hero",fast.Output).Count>=23,"offline lists bypass PowerShell wrapper");
    Console.WriteLine($"Direct offline character list: {timer.ElapsedMilliseconds} ms");
}
finally{File.WriteAllText(wrapper,originalWrapper);}
Check(CtlClient.ParseList("pet","500057\tconfig 500057\tlevel 100\tgrade SSS\tbond 10")[0].Name=="Chonoko","official Kibo display names");
Check(CtlClient.ParseList("pet","918273\tconfig 500057\tlevel 100")[0].ImageKey=="pet/500057","Kibo image uses species, not owned GUID");
Check(CtlClient.ParseList("gear","918273\tconfig 1010211\tlevel 9")[0].ImageKey=="gear/1010211","equipment image uses config, not owned GUID");
Check(CtlClient.ParseList("gear","1010211\tslot 1\trarity 0")[0].ImageKey=="gear/1010211","equipment catalog image uses config");
Check(CtlClient.ParseList("hero","104001\tCHIYO\tlevel 100")[0].ImageKey=="hero/104001","Chiyo portrait mapping");
Check(CtlClient.ParseList("soul","10164\tlevel 100")[0].ImageKey=="soul/10164","Chiyo Anima image mapping");
Check(!Directory.Exists(Path.Combine(stage,"saves/backups")),"read did not create backups");

var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
var info=new ProcessStartInfo(exe){WorkingDirectory=stage,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
info.ArgumentList.Add("-a");info.ArgumentList.Add($"127.0.0.1:{port}");info.Environment["AZUREPRO_SAVE_FILE"]=ctl.SavePath;
using var server=Process.Start(info)!;var listening=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
server.ErrorDataReceived+=(_,e)=>{if(e.Data?.Contains($"listening at 127.0.0.1:{port}")==true)listening.TrySetResult();};server.BeginErrorReadLine();server.BeginOutputReadLine();
try
{
    await listening.Task.WaitAsync(TimeSpan.FromSeconds(10));
    using var tcp=new TcpClient{ReceiveBufferSize=4*1024*1024};await tcp.ConnectAsync(IPAddress.Loopback,port);using var stream=tcp.GetStream();stream.ReadTimeout=8000;
    Wire.Send(stream,5001,1,[10,5,..Encoding.UTF8.GetBytes("maxed")]);Wire.Until(stream,5001,1);
    Wire.Send(stream,9100,2,[]);Wire.Until(stream,9100,2);
    var deadline=DateTime.UtcNow.AddSeconds(5);while(!ctl.IsLive){Check(DateTime.UtcNow<deadline,"live endpoint ready");await Task.Delay(50);}
    uint sequence=100;
    async Task<(CommandResult Result,List<int> Pushes)> Run(string[] command)
    {
        var answer=await ctl.RunAsync(2,command);Wire.Send(stream,503,++sequence,[]);return(answer,Wire.Until(stream,503,sequence));
    }
    Check((await Run(["hero","set","107002","talent","1"])).Result.Success,"live talent edit");
    var level=await Run(["hero","set","107002","level","60"]);Check(level.Result.Success,"live level edit");
    Check(level.Pushes.Contains(10006)&&level.Pushes.LastIndexOf(5008)>level.Pushes.LastIndexOf(10006),"same-command menu repaint order");
    var before=SHA256.HashData(File.ReadAllBytes(ctl.SavePath));
    Check(!(await Run(["hero","set","107002","level","101"])).Result.Success,"official limit rejection");
    Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(ctl.SavePath))),"rejected edit leaves save unchanged");
    File.WriteAllText(wrapper,"throw 'Live read unexpectedly started PowerShell'");
    try
    {
        var inspect=await Run(["hero","inspect","107002"]);Check(inspect.Result.Success&&inspect.Result.Output.Contains("level 60"),"live read bypasses wrapper and sees in-memory state");
        Check((await ctl.RunAsync(2,["catalog","heroes"])).Success,"live catalog uses compiled tables without opening locked save");
    }
    finally{File.WriteAllText(wrapper,originalWrapper);}
    Check(!Directory.Exists(Path.Combine(stage,"saves/backups")),"live commands do not create backups");
}
finally{if(!server.HasExited)server.Kill();await server.WaitForExitAsync();}
var offline=await ctl.RunAsync(2,["hero","inspect","107002"]);Check(offline.Success&&offline.Output.Contains("level 60"),"persistence after server exit");
Check(!Directory.Exists(Path.Combine(stage,"saves/backups")),"no backup saves");
Console.WriteLine("PASS: account/checksum validation, all command templates, offline catalogs, live mutation, official limits, repaint order, persistence and no backups.");
Console.WriteLine("Isolated evidence: "+stage);

static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);}
static void ExpectBadSave(byte[] bytes){try{SaveAccounts.Decode(bytes);}catch(IOException){return;}throw new Exception("Invalid save was accepted");}

static class Wire
{
    static readonly byte[] Key=Convert.FromHexString("5254455767647374255e263233666238372a235733726466643637572423265e2a262a4023212444466564667479245e25545723234523402524647354485e2a262a266661736466613c3e3c3f3e3e4c666466646646245424676761647366345e266b254023242a2923214647464447732423252526254447466561776566");
    static void Crypt(Span<byte> bytes,int size){for(var i=0;i<bytes.Length;i++)bytes[i]^=Key[(size/3+i)%Key.Length];}
    public static void Send(NetworkStream stream,ushort id,uint sequence,byte[] body)
    {
        var frame=new byte[25+body.Length];BinaryPrimitives.WriteUInt32BigEndian(frame,(uint)frame.Length);BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5),id);BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(9),sequence);body.CopyTo(frame,25);Crypt(frame.AsSpan(7),frame.Length);stream.Write(frame);
    }
    public static List<int> Until(NetworkStream stream,int expected,uint sequence)
    {
        var ids=new List<int>();var header=new byte[7];
        while(true)
        {
            stream.ReadExactly(header);var size=checked((int)BinaryPrimitives.ReadUInt32BigEndian(header));if(size<25||size>16*1024*1024)throw new IOException("Invalid frame size");
            var id=BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(5));var payload=new byte[size-7];stream.ReadExactly(payload);Crypt(payload,size);
            if(id==5003)throw new IOException("Server forced a relog");
            if(id==expected&&BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(2))==sequence){if(BinaryPrimitives.ReadUInt16BigEndian(payload)!=0)throw new IOException("Protocol rejected request");return ids;}
            ids.Add(id);
        }
    }
}
