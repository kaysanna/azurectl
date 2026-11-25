#nullable disable
using System.Diagnostics;
namespace AzureCTL;
static class UpdateWindow
{
    internal static int Apply(string[] args)
    {
        string target=args[1];
        bool restart=args.Length==4;
        string stage=AppContext.BaseDirectory;
        FileStream lease=null;
        try
        {
            lease=OpenStageLease(stage);
            if(!int.TryParse(args[2],out int pid)||!long.TryParse(args[3],out long ticks))throw new InvalidDataException("Invalid updater process.");
            try
            {
                using var parent=Process.GetProcessById(pid);
                if(parent.StartTime.ToUniversalTime().Ticks!=ticks)throw new IOException("The original application process changed.");
                if(!parent.WaitForExit(30000))throw new IOException("AzureCTL did not close. Update cancelled.");
            }
            catch(ArgumentException){} // Parent already exited.
            string targetExe=Path.Combine(Path.GetFullPath(target),"AzureCTL.exe");
            string lockName="Local\\AzureCTLUpdate-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(targetExe.ToUpperInvariant())));
            using var updateLock=new Mutex(false,lockName);
            bool acquired;
            try{acquired=updateLock.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
            if(!acquired)throw new IOException("Another AzureCTL update is already running.");
            try
            {
            var manifest=AppUpdate.ReadManifest(File.ReadAllText(Path.Combine(stage,"update.json")));
            if(AppUpdate.ParseVersion(manifest.Version)<=AppUpdate.Current)throw new InvalidDataException("Update is not newer than the installed app.");
            if(AppUpdate.ParseVersion(FileVersionInfo.GetVersionInfo(targetExe).FileVersion)>=AppUpdate.ParseVersion(manifest.Version))
                throw new IOException("This version of AzureCTL is already installed.");
            AppUpdate.Install(stage,target,manifest);
            if(restart)Process.Start(new ProcessStartInfo(Path.Combine(target,"AzureCTL.exe")){UseShellExecute=true,WorkingDirectory=target,ArgumentList={"--updated"}});
            File.WriteAllText(Path.Combine(stage,"completed"),"");
            foreach(var file in new[]{"update.zip","update.json"})try{File.Delete(Path.Combine(stage,file));}catch{}
            try{Directory.Delete(Path.Combine(stage,"payload"),true);}catch{}
            return 0;
            }
            finally{updateLock.ReleaseMutex();}
        }
        catch(Exception ex)
        {
            BackgroundUpdate.LogError(ex);
            if(restart)
            {
                MessageBox.Show("AzureCTL could not install the update.\n\n"+ex.Message,"AzureCTL update",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                try{Process.Start(new ProcessStartInfo(Path.Combine(target,"AzureCTL.exe")){UseShellExecute=true,WorkingDirectory=target,ArgumentList={"--skip-update"}});}catch{}
            }
            return 1;
        }
        finally{lease?.Dispose();ScheduleStageCleanup(stage);}
    }

    internal static bool IsStage(string path)
    {
        try
        {
            string full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            string root=Path.GetFullPath(AppUpdate.StageRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetDirectoryName(full),root,StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(Path.GetFileName(full),"N",out _);
        }
        catch{return false;}
    }
    internal static FileStream OpenStageLease(string stage)
    {
        if(!IsStage(stage))throw new IOException("Invalid update staging directory.");
        AppUpdate.CheckDirectory(stage);
        return new FileStream(Path.Combine(stage,"active.lease"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);
    }
    internal static void DeleteStage(string stage)
    {
        try
        {
            if(!IsStage(stage)||!Directory.Exists(stage))return;
            AppUpdate.CheckDirectory(stage);
            // An active downloader or installer holds a shared lease. Never delete it.
            using(var lease=new FileStream(Path.Combine(stage,"active.lease"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){}
            if(Directory.EnumerateFileSystemEntries(stage,"*",SearchOption.AllDirectories).Any(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0))return;
            Directory.Delete(stage,true);
        }
        catch{}
    }
    static void ScheduleStageCleanup(string stage)
    {
        try
        {
            if(!IsStage(stage))return;
            AppUpdate.CheckDirectory(stage);
            // Windows cannot delete this running helper. A hidden native PowerShell
            // waits for this exact process, then removes only this validated GUID stage.
            string Quote(string text)=>"'"+text.Replace("'","''")+"'";
            string script="$stage="+Quote(Path.GetFullPath(stage).TrimEnd('\\','/'))+";$root="+Quote(Path.GetFullPath(AppUpdate.StageRoot).TrimEnd('\\','/'))+";"+
                "$p=Get-Process -Id "+Environment.ProcessId+" -ErrorAction SilentlyContinue;"+
                "if($p -and $p.StartTime.ToUniversalTime().Ticks -eq "+Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks+"){$p.WaitForExit()};"+
                "$full=[IO.Path]::GetFullPath($stage);$parent=[IO.Path]::GetDirectoryName($full);$id=[Guid]::Empty;"+
                "if(-not [string]::Equals($parent,$root,[StringComparison]::OrdinalIgnoreCase) -or -not [Guid]::TryParseExact([IO.Path]::GetFileName($full),'N',[ref]$id)){exit};"+
                "if(-not(Test-Path -LiteralPath $full)){exit};"+
                "$d=Get-Item -LiteralPath $full;while($d){if(($d.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){exit};$d=$d.Parent};"+
                "if(Get-ChildItem -LiteralPath $full -Force -Recurse | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0}){exit};"+
                "try{$lease=[IO.File]::Open([IO.Path]::Combine($full,'active.lease'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$lease.Dispose();Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop}catch{}";
            var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"))
                {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetTempPath()};
            foreach(var arg in new[]{"-NoProfile","-NonInteractive","-WindowStyle","Hidden","-EncodedCommand",Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))})info.ArgumentList.Add(arg);
            Process.Start(info)?.Dispose();
        }
        catch(Exception ex){BackgroundUpdate.LogError(ex);}
    }
}
