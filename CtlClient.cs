using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzureCTL;

public sealed record PlayerChoice(uint Id, string Account)
{
    public override string ToString() => $"{Account} · UID {Id}";
}
public sealed record LibraryItem(string Id, string Name, string Detail, bool Owned = true, string? ImageKey = null)
{
    public override string ToString() => $"{Name} · {Id}";
}
public sealed record CommandResult(bool Success, string Output, string Error);

public sealed class CtlClient
{
    public string Root { get; }
    public string SavePath => Path.Combine(Root, "saves", "active.bin");
    public CtlClient(string selected)
    {
        var root = Path.GetFullPath(selected);
        if (File.Exists(Path.Combine(root, "server", "scripts", "ctl.ps1"))) root = Path.Combine(root, "server");
        if (!File.Exists(Path.Combine(root, "scripts", "ctl.ps1"))) throw new IOException("Choose your Azurepro folder, containing server\\scripts\\ctl.ps1.");
        if (Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)).Equals("server",StringComparison.OrdinalIgnoreCase)
            && !File.Exists(Path.Combine(root,"saves","active.bin"))
            && File.Exists(Path.Combine(Directory.GetParent(root)!.FullName,"saves","active.bin")))
            throw new IOException("This Azurepro checkout needs its one-time folder migration. Close its game/server and run the top-level build.cmd, then Refresh. Your existing save is still in the old saves folder; AzureCTL has not moved or edited it.");
        Root = root;
    }

    public bool IsLive
    {
        get
        {
            try
            {
                using var ready = JsonDocument.Parse(File.ReadAllText(SavePath + ".ctl/ready.json"));
                var pid = ready.RootElement.GetProperty("pid").GetInt32();
                using var process = Process.GetProcessById(pid);
                var path = process.MainModule?.FileName;
                return !process.HasExited && path is not null && Path.GetFileName(path).Equals("zetsa-gamesv-jp.exe", StringComparison.OrdinalIgnoreCase)
                    && path.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    public async Task<CommandResult> RunAsync(uint player, IReadOnlyList<string> command, CancellationToken cancel = default)
    {
        if (command.Count == 0) throw new ArgumentException("Choose a command first.");
        if(CtlReadTransport.IsReadOnly(command))
        {
            var read=await CtlReadTransport.TryRunAsync(this,player,command,cancel);
            if(read!=null)return read;
        }
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Root };
        info.Environment["PSModulePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsPowerShell", "Modules") + ";" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "Modules");
        foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Root, "scripts", "ctl.ps1") }) info.ArgumentList.Add(value);
        if (command[0] != "catalog" && command[0] != "help") info.ArgumentList.Add(player.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var value in command) info.ArgumentList.Add(value);
        using var process = Process.Start(info) ?? throw new IOException("Could not start Azurepro CTL.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancel);
        var stderr = process.StandardError.ReadToEndAsync(cancel);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            // Only our launcher is terminated. Never terminate the game/server,
            // retry a mutation, or fall back to writing a save ourselves.
            if (!process.HasExited) process.Kill();
            throw new IOException("CTL response was interrupted. The edit may have applied. Refresh and inspect its result before retrying.");
        }
        var output = await stdout;
        var error = await stderr;
        output = Regex.Replace(output, @"(?m)^Diagnostics:.*(?:\r?\n|$)", "").Trim();
        // Zig's standalone CTL prints successful lists to stderr; the live
        // mailbox prints them to stdout. Both are command output on success.
        if(process.ExitCode==0 && !string.IsNullOrWhiteSpace(error)) { output=string.Join("\n",new[]{output,error.Trim()}.Where(x=>x.Length>0)); error=""; }
        return new(process.ExitCode == 0, output, error.Trim());
    }

    public static List<LibraryItem> ParseList(string category, string text)
    {
        var items = new List<LibraryItem>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length < 2 || !ulong.TryParse(parts[0], out _)) continue;
            var name = category switch
            {
                "hero" or "heroes" => HeroName(parts[0], parts[1]),
                "pet" => CatalogNames.Get("pet_names",parts[1].Replace("config ",""),"Kibo "+parts[1].Replace("config ","")),
                "pets" => CatalogNames.Get("pet_names",parts[0],"Kibo "+parts[0]),
                "gear" => CatalogNames.Get("gear_names",parts[1].StartsWith("config ",StringComparison.Ordinal)?parts[1][7..]:parts[0],"Equipment "+parts[0]),
                "soul" or "souls" => CatalogNames.Get("soul_names",parts[0],"Anima "+parts[0]),
                "genes" => "Gene " + parts[0],
                _ => parts[0],
            };
            var imageGroup = category switch { "hero" or "heroes" => "hero", "pet" or "pets" => "pet", "gear" => "gear", "soul" or "souls" => "soul", _ => "" };
            var configId = category is "pet" || category=="gear" && parts[1].StartsWith("config ",StringComparison.Ordinal) ? parts[1].Replace("config ","") : parts[0];
            items.Add(new(parts[0], name, string.Join(" · ", parts.Skip(category is "hero" or "heroes" ? 2 : 1)), ImageKey:imageGroup.Length==0?null:imageGroup+"/"+configId));
        }
        return items;
    }

    public static string HeroName(string id, string name) => id == "104001" ? "Minamoto Chiyo" : id=="101010"?"Tushan Xiaoyu":
        CatalogNames.Get("hero_names",id,System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' ').ToLowerInvariant()));
}

/// Read-only account discovery. Mutations always go through Azurepro CTL.
public static class SaveAccounts
{
    public static List<PlayerChoice> Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length is < 64 or > 67108928) throw new IOException("Invalid active save size.");
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        return Decode(bytes);
    }

    public static List<PlayerChoice> Decode(byte[] bytes)
    {
        var data = bytes.AsSpan();
        if (data.Length < 64 || !data[..8].SequenceEqual("AZUPSAVE"u8) || BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) != 1 || BinaryPrimitives.ReadUInt32LittleEndian(data[12..]) != 3075915)
            throw new IOException("This app requires an official-JP Azurepro active.bin save.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(data[22..]) != 0 || BinaryPrimitives.ReadUInt64LittleEndian(data[24..]) != (ulong)data.Length - 64) throw new IOException("Invalid save header.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(data[..32]); hash.AppendData(data[64..]);
        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), data[32..64])) throw new IOException("Save checksum does not match. Wait for the current write to finish, then refresh.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        var preferredSize = BinaryPrimitives.ReadUInt16LittleEndian(data[20..]);
        if (count > 4096 || preferredSize > 127 || 64 + preferredSize > data.Length) throw new IOException("Invalid save account index.");
        var preferred = Encoding.UTF8.GetString(data.Slice(64, preferredSize));
        var offset = 64 + preferredSize;
        var players = new List<PlayerChoice>();
        for (var i = 0; i < count; i++)
        {
            if (offset + 10 > data.Length) throw new IOException("Truncated save index.");
            var nameSize = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            var size = BinaryPrimitives.ReadUInt64LittleEndian(data[(offset + 2)..]);
            offset += 10;
            if (nameSize == 0 || nameSize > 1024 || nameSize > data.Length - offset || size > (ulong)(data.Length - offset - nameSize)) throw new IOException("Invalid save record.");
            var name = Encoding.UTF8.GetString(data.Slice(offset, nameSize)); offset += nameSize;
            if (name.StartsWith("account/by-open-id/", StringComparison.Ordinal))
            {
                var record = Encoding.UTF8.GetString(data.Slice(offset, checked((int)size)));
                var match = Regex.Match(record, @"(?m)^\s*player_id\s*=\s*([0-9]+)\s*$");
                if (match.Success && uint.TryParse(match.Groups[1].Value, out var id) && id != 0) players.Add(new(id, name[19..]));
            }
            offset += checked((int)size);
        }
        if (offset != data.Length) throw new IOException("Unexpected trailing save data.");
        return players.OrderByDescending(p => p.Account == preferred).ThenBy(p => p.Account).ToList();
    }
}
