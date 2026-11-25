using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace AzureCTL;

sealed class MainForm : Form
{
    readonly Panel sidebar=new(){BackColor=Ui.Sidebar};
    readonly Label brand=Ui.Label("AzureCTL",25,true,Ui.Text),tag=Ui.Label("AZUR PROMILIA",11),heading=Ui.Label("Characters",29,true,Ui.Text),subtitle=Ui.Label("Manage your selected player from one workspace.",14);
    readonly Label rootLabel=Ui.Label("AZUREPRO FOLDER",11),libraryLabel=Ui.Label("CHARACTER LIBRARY",11),status=Ui.Label("Choose an Azurepro folder to connect.",12),mode=Ui.Label("NOT CONNECTED",11,true);
    readonly Input rootField=new("Choose your Azurepro folder"),search=new("Search characters");
    readonly Choice players=new();
    readonly Pill browse=new("Choose folder"),refresh=new("Refresh"),logs=new("Activity"),minimize=new("—"),maximize=new("□"),close=new("×");
    readonly Pill checkUpdates=new("Check for updates");
    readonly Label updateText=Ui.Label("",11),version=Ui.Label("v"+AppUpdate.Current.ToString(3),11);
    readonly BackgroundUpdate updates;
    bool updateChecking;
    readonly Library library=new();
    readonly Card editor=new();
    readonly Panel editorBody=new(){BackColor=Ui.Surface},editorFooter=new(){BackColor=Ui.Surface};
    readonly Label selectedName=Ui.Label("Pick a character",24,true,Ui.Text),selectedDetail=Ui.Label("from the list, or enter an ID below",13);
    readonly Label operationLabel=Ui.Label("ACTION",11),manualLabel=Ui.Label("SELECTED ID",11),note=Ui.Label("",13),previewLabel=Ui.Label("COMMAND",11),result=Ui.Label("",13);
    readonly ActionPicker operation=new();
    readonly Input manualId=new("Character or inventory ID"),preview=new();
    readonly Pill apply=new("Apply",true),inspect=new("Inspect"),catalog=new("Catalog");
    readonly List<Pill> navigation=[];
    readonly List<(Arg Arg,Control Input,Label Label)> fields=[];
    readonly List<string> activity=[];
    readonly System.Windows.Forms.Timer connectionTimer=new(){Interval=2000};
    CtlClient? client;
    List<LibraryItem> allItems=[];
    List<LibraryItem> heroChoices=[];
    int page;
    bool busy,initializing=true;
    string SettingsPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AzureCTL","settings.json");
    Section Section=>Commands.Sections[page];
    Operation? SelectedOperation=>operation.SelectedItem;
    uint Player=>players.SelectedItem is PlayerChoice selected?selected.Id:0;
    float S=>DeviceDpi/96f;
    int Px(float value)=>(int)Math.Round(value*S);

    public MainForm(string[] args)
    {
        Text="AzureCTL";BackColor=Ui.Bg;ForeColor=Ui.Text;Font=Ui.Font(14);FormBorderStyle=FormBorderStyle.None;
        AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new(1420,940);MinimumSize=new(1180,780);StartPosition=FormStartPosition.CenterScreen;
        Controls.Add(sidebar);sidebar.Controls.AddRange([brand,tag,mode,checkUpdates,updateText,version]);
        for(var i=0;i<Commands.Sections.Length;i++)
        {
            var index=i;var button=new Pill(Commands.Sections[i].Name);button.Click+=async(_,_)=>await ChangePage(index);navigation.Add(button);sidebar.Controls.Add(button);
        }
        Controls.AddRange([heading,subtitle,rootLabel,rootField,browse,players,refresh,search,libraryLabel,library,editor,status,logs,minimize,maximize,close]);
        editor.Controls.AddRange([editorBody,editorFooter]);
        editorBody.Controls.AddRange([selectedName,selectedDetail,manualLabel,manualId,operationLabel,operation,note,previewLabel,preview,result]);
        editorFooter.Controls.AddRange([apply,inspect,catalog]);
        preview.Visible=previewLabel.Visible=false;result.TextChanged+=(_,_)=>LayoutEditor();
        preview.Box.ReadOnly=true;preview.Box.Font=new Font("Consolas",12,FontStyle.Regular,GraphicsUnit.Pixel);
        selectedName.AutoEllipsis=true;selectedDetail.AutoEllipsis=true;note.AutoEllipsis=false;result.AutoEllipsis=true;status.AutoEllipsis=true;
        rootField.Box.AccessibleName="Azurepro folder";players.AccessibleName="Player account";search.Box.AccessibleName="Search library";
        manualId.Box.AccessibleName="Selected character or item ID";operation.AccessibleName="CTL action";preview.Box.AccessibleName="Command preview";
        apply.AccessibleName="Apply CTL command";inspect.AccessibleName="Inspect selected entry";catalog.AccessibleName="Show official catalog";
        browse.Click+=async(_,_)=>{using var dialog=new FolderBrowserDialog{Description="Choose Azurepro (the folder containing build.cmd and server)",UseDescriptionForTitle=true};if(dialog.ShowDialog(this)==DialogResult.OK){rootField.Box.Text=dialog.SelectedPath;await Connect();}};
        refresh.Click+=async(_,_)=>await Connect();
        rootField.Box.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await Connect();}};
        rootField.Box.TextChanged+=(_,_)=>{if(initializing||busy)return;client=null;allItems=[];Filter();status.Text="Folder changed · press Refresh to connect";UpdateConnection();UpdatePreview();};
        search.Box.TextChanged+=(_,_)=>Filter();
        players.SelectedIndexChanged+=async(_,_)=>{if(!initializing&&!busy)await LoadLibrary();};
        library.SelectionChanged+=item=>{manualId.Box.Text=item.Id;UpdateSelected();};
        manualId.Box.TextChanged+=(_,_)=>{UpdateSelected();UpdatePreview();};
        operation.SelectedIndexChanged+=(_,_)=>BuildFields();
        apply.Click+=async(_,_)=>await Apply();
        inspect.Click+=async(_,_)=>await InspectSelected();
        catalog.Click+=async(_,_)=>await ShowCatalog();
        logs.Click+=(_,_)=>ShowText("Activity",activity.Count==0?"No commands have been run.":string.Join("\r\n\r\n",activity));
        minimize.Click+=(_,_)=>WindowState=FormWindowState.Minimized;
        maximize.Click+=(_,_)=>{MaximizedBounds=Screen.FromControl(this).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;};
        close.Click+=(_,_)=>Close();
        updates=new BackgroundUpdate(this,autoCheck:!args.Contains("--skip-update")&&!args.Contains("--updated"));
        updates.StatusChanged+=(text,checking)=>
        {
            if(IsDisposed||Disposing)return;
            try{BeginInvoke((Action)(()=>
            {
                if(IsDisposed||Disposing)return;
                updateChecking=checking;updateText.Text=text;
                checkUpdates.Text=updates.IsReady?"Restart to update":"Check for updates";
                checkUpdates.Enabled=!checking&&!busy;
            }));}catch(InvalidOperationException){}
        };
        checkUpdates.Click+=async(_,_)=>{if(busy)return;if(updates.IsReady)updates.InstallAndRestart();else await updates.CheckNowAsync();};
        connectionTimer.Tick+=(_,_)=>UpdateConnection();connectionTimer.Start();
        Resize+=(_,_)=>LayoutControls();DpiChanged+=(_,_)=>LayoutControls();
        foreach(var control in new Control[]{this,brand,heading})control.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,2,0);}};
        try{if(File.Exists(SettingsPath)){using var settings=JsonDocument.Parse(File.ReadAllText(SettingsPath));rootField.Box.Text=settings.RootElement.GetProperty("root").GetString()??"";}}catch{}
        for(var i=0;i<args.Length-1;i++)if(args[i]=="--root")rootField.Box.Text=args[++i];
        initializing=false;SetOperations();LayoutControls();
        Shown+=async(_,_)=>{if(rootField.Box.Text.Length>0)await Connect();};
    }

    [DllImport("user32.dll")]static extern bool ReleaseCapture();
    [DllImport("user32.dll")]static extern nint SendMessage(nint window,uint message,nint wparam,nint lparam);
    void Place(Control c,float x,float y,float w,float h)=>c.SetBounds(Px(x),Px(y),Math.Max(1,Px(w)),Math.Max(1,Px(h)));

    void LayoutControls()
    {
        var w=ClientSize.Width/S;var h=ClientSize.Height/S;var x=264f;var content=w-x-32;var left=(content-24)*.46f;var right=content-left-24;
        Place(sidebar,0,0,232,h);Place(brand,24,36,190,40);Place(tag,26,80,180,20);
        for(var i=0;i<navigation.Count;i++){Place(navigation[i],16,140+i*54,200,42);navigation[i].Selected=i==page;navigation[i].Invalidate();}
        Place(mode,25,h-76,190,24);Place(heading,x,66,content-150,38);Place(subtitle,x,108,content,26);
        Place(updateText,25,h-208,190,42);Place(checkUpdates,16,h-156,200,36);Place(version,25,h-111,190,20);
        Place(minimize,w-132,8,36,28);Place(maximize,w-90,8,36,28);Place(close,w-48,8,36,28);
        Place(rootLabel,x,144,Math.Max(100,content-550),20);
        Place(rootField,x,166,Math.Max(180,content-552),42);Place(browse,x+content-540,166,130,42);
        Place(players,x+content-396,168,256,40);Place(refresh,x+content-126,166,126,42);
        Place(libraryLabel,x,220,left,20);Place(search,x,246,left,42);Place(library,x,304,left,Math.Max(240,h-370));
        // The right card aligns exactly with the top of the search field.
        Place(editor,x+left+24,246,right,Math.Max(350,h-312));
        Place(status,x,h-47,Math.Max(100,content-124),30);Place(logs,x+content-112,h-49,112,32);
        LayoutEditor();
    }

    void LayoutEditor()
    {
        if(editor.Width==0)return;
        editorBody.SetBounds(1,1,editor.Width-2,Math.Max(1,editor.Height-Px(76)));
        editorFooter.SetBounds(1,editor.Height-Px(75),editor.Width-2,Px(74));
        var w=editorBody.ClientSize.Width/S;var inner=w-40;
        var titleWidth=inner-160;
        Place(selectedName,20,14,titleWidth,32);Place(selectedDetail,20,48,titleWidth,32);
        Place(manualLabel,20+inner-144,14,144,18);Place(manualId,20+inner-144,38,144,36);
        var actionHeight=operation.HeightForWidth(inner);
        Place(operationLabel,20,86,inner,18);Place(operation,20,108,inner,actionHeight);
        var fieldsTop=108+actionHeight+14;
        var fieldWidth=(inner-12)/2;
        for(var i=0;i<fields.Count;i++)
        {
            var fx=20+i%2*(fieldWidth+12);var fy=fieldsTop+i/2*64;
            Place(fields[i].Label,fx,fy,fieldWidth,18);Place(fields[i].Input,fx,fy+22,fieldWidth,36);
        }
        var after=fieldsTop+((fields.Count+1)/2)*64+2;
        note.Visible=result.Text.Length==0;result.Visible=!note.Visible;
        Place(note,20,after,inner,48);Place(result,20,after,inner,48);
        var footerInner=editorFooter.ClientSize.Width/S-40;
        Place(apply,20,18,Math.Max(130,footerInner-182),40);Place(inspect,20+footerInner-168,18,80,40);Place(catalog,20+footerInner-80,18,80,40);
    }

    async Task ChangePage(int index)
    {
        if(busy)return;page=index;search.Box.Text="";manualId.Box.Text="";result.Text="";SetOperations();LayoutControls();await LoadLibrary();
    }
    void SetOperations()
    {
        heading.Text=Section.Name;subtitle.Text=Section.Subtitle;libraryLabel.Text=Section.Category switch{"hero" or "heroes"=>"CHARACTER LIBRARY","pet"=>"KIBO LIBRARY",_=>Section.Name.ToUpperInvariant()+" LIBRARY"};
        search.Box.PlaceholderText="Search "+Section.Name.ToLowerInvariant()+" or an ID";
        operation.SetItems(Section.Operations);UpdateSelected();
    }
    void BuildFields()
    {
        foreach(var field in fields){editorBody.Controls.Remove(field.Input);editorBody.Controls.Remove(field.Label);field.Input.Dispose();field.Label.Dispose();}fields.Clear();
        if(SelectedOperation is not{} op)return;
        foreach(var arg in op.Fields)
        {
            var label=Ui.Label(arg.Label,11);Control input;
            if(arg.Choices is{} choices){var choice=new Choice();choice.Items.AddRange(choices);choice.SelectedItem=arg.Default;choice.SelectedIndexChanged+=(_,_)=>UpdatePreview();input=choice;}
            else {var field=new Input();field.Box.Text=arg.Default;field.Box.TextChanged+=(_,_)=>UpdatePreview();input=field;}
            input.AccessibleName=arg.Label;if(input is Input edit)edit.Box.AccessibleName=arg.Label;
            fields.Add((arg,input,label));editorBody.Controls.Add(label);editorBody.Controls.Add(input);
        }
        note.Text=op.Description;apply.Text=op.Name;LayoutEditor();UpdatePreview();
    }
    string[] CurrentCommand()
    {
        var op=SelectedOperation??throw new InvalidOperationException("Choose an action.");
        var id=manualId.Box.Text.Trim();if(op.NeedsSelection&&(!ulong.TryParse(id,out var parsedId)||parsedId==0))throw new InvalidOperationException("Select an entry or enter its numeric ID.");
        var values=fields.ToDictionary(x=>x.Arg.Key,x=>x.Input is Choice choice?choice.SelectedItem?.ToString()??"":((Input)x.Input).Box.Text.Trim());
        foreach(var value in values.Values)if(value.Length==0||value.Any(char.IsWhiteSpace))throw new InvalidOperationException("Complete all action fields with a single value.");
        return op.Expand(id,values);
    }
    void UpdatePreview()
    {
        try {preview.Box.Text=$"{Player} "+string.Join(' ',CurrentCommand());apply.Enabled=client!=null&&Player!=0&&!busy;}
        catch {preview.Box.Text="Select an entry and complete the fields";apply.Enabled=false;}
    }
    void UpdateSelected()
    {
        var id=manualId.Box.Text.Trim();var item=allItems.FirstOrDefault(x=>x.Id==id);
        selectedName.Text=item?.Name??(id.Length>0?Section.Name+" · "+id:Section.Category=="world"?"World difficulty":"Pick "+(Section.Category is "hero" or "heroes"?"a character":"an entry"));
        selectedDetail.Text=item?.Detail??(id.Length>0?"Manual ID · validated by Azurepro":"from the list, or enter an ID below");
    }
    void Filter()=>library.SetItems(allItems.Where(x=>(x.Name+" "+x.Id+" "+x.Detail).Contains(search.Box.Text,StringComparison.OrdinalIgnoreCase)),manualId.Box.Text);

    async Task Connect()
    {
        if(busy)return;SetBusy(true,"Connecting to Azurepro…");
        try
        {
            var next=new CtlClient(rootField.Box.Text.Trim());
            var selected=Player;var accounts=await Task.Run(()=>SaveAccounts.Read(next.SavePath));if(accounts.Count==0)throw new IOException("No player exists in this save yet. Log into Azurepro once, then refresh.");
            client=next;initializing=true;players.Items.Clear();players.Items.AddRange(accounts.Cast<object>().ToArray());players.SelectedItem=accounts.FirstOrDefault(x=>x.Id==selected)??accounts[0];initializing=false;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);File.WriteAllText(SettingsPath,JsonSerializer.Serialize(new{root=rootField.Box.Text.Trim()}));
            status.Text="Connected · "+next.SavePath;status.ForeColor=Ui.Good;
        }
        catch(Exception ex){client=null;Report(ex.Message,false);}
        finally{initializing=false;SetBusy(false);UpdateConnection();}
        if(client!=null)await LoadLibrary();
    }

    async Task LoadLibrary()
    {
        if(IsDisposed||Disposing)return;
        if(client==null||Player==0||busy){allItems=[];Filter();UpdatePreview();return;}
        var old=manualId.Box.Text;SetBusy(true,"Loading "+Section.Name.ToLowerInvariant()+"…");
        try
        {
            string[] cmd=Section.Category switch{"heroes"=>["catalog","heroes"],"gear-catalog"=>["catalog","gear"],"souls"=>["catalog","souls"],"world"=>["world","difficulty","list"],_=>[Section.Category,"list"]};
            var answer=await client.RunAsync(Player,cmd);if(!answer.Success)throw new IOException(answer.Error.Length>0?answer.Error:answer.Output);
            allItems=Section.Category=="world"?Regex.Matches(answer.Output,@"map (\d+): tier (\d+)").Select(m=>new LibraryItem(m.Groups[1].Value,"Map "+m.Groups[1].Value,"World level "+m.Groups[2].Value)).ToList():CtlClient.ParseList(Section.Category=="gear-catalog"?"gear":Section.Category,answer.Output);
            if(Section.Category=="hero")heroChoices=allItems.ToList();
            Filter();manualId.Box.Text=allItems.Any(x=>x.Id==old)?old:"";UpdateSelected();
            status.Text=$"{allItems.Count} entries · "+(client.IsLive?"Live changes enabled":"Server offline · edits persist to active save");status.ForeColor=Ui.Dim;
        }
        catch(Exception ex){Report(ex.Message,false);}
        finally{SetBusy(false);}
    }

    async Task Apply()
    {
        if(client==null||busy)return;
        string[] command;try{command=CurrentCommand();}catch(Exception ex){Report(ex.Message,false);return;}
        if(command.Length>1&&command[0]=="gear"&&command[1]=="remove"&&MessageBox.Show(this,"Remove equipment "+manualId.Box.Text+" from the current save?","Remove equipment",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        SetBusy(true,"Applying "+SelectedOperation!.Name.ToLowerInvariant()+"…");
        try
        {
            var answer=await client.RunAsync(Player,command);Record(command,answer);
            if(!answer.Success)throw new IOException(answer.Error.Length>0?answer.Error:answer.Output);
            Report(answer.Output.Length>0?answer.Output:"Updated the active save.",true);
        }
        catch(Exception ex){Report(ex.Message,false);}
        finally{SetBusy(false);}
        await LoadLibrary();
    }

    async Task InspectSelected()
    {
        if(client==null||busy||manualId.Box.Text.Length==0)return;
        string[] command=Section.Category switch{"hero"=>["hero","inspect",manualId.Box.Text],"gear"=>["gear","inspect",manualId.Box.Text],"pet"=>["pet","inspect",manualId.Box.Text],"heroes"=>["catalog","heroes",manualId.Box.Text],"gear-catalog"=>["catalog","gear",manualId.Box.Text],"soul" or "souls"=>["catalog","souls",manualId.Box.Text],_=>["world","difficulty","list"]};
        SetBusy(true,"Reading selected entry…");
        try{var answer=await client.RunAsync(Player,command);if(!answer.Success)throw new IOException(answer.Error.Length>0?answer.Error:answer.Output);ShowText(selectedName.Text,answer.Output);}
        catch(Exception ex){Report(ex.Message,false);}
        finally{SetBusy(false);}
    }
    async Task ShowCatalog()
    {
        if(client==null||busy)return;SetBusy(true,"Reading official catalog…");
        try{var answer=await client.RunAsync(Player,["catalog",Section.Category=="pet"?"genes":Section.Category is "hero" or "heroes"?"heroes":Section.Category is "soul" or "souls"?"souls":"gear"]);if(!answer.Success)throw new IOException(answer.Error);ShowText("Official catalog",answer.Output);}
        catch(Exception ex){Report(ex.Message,false);}
        finally{SetBusy(false);}
    }
    void SetBusy(bool value,string? message=null)
    {
        if(IsDisposed||Disposing)return;
        busy=value;browse.Enabled=refresh.Enabled=players.Enabled=operation.Enabled=manualId.Enabled=!value;library.Enabled=!value;
        rootField.Enabled=!value;
        checkUpdates.Enabled=!value&&!updateChecking;
        foreach(var button in navigation)button.Enabled=!value;inspect.Enabled=catalog.Enabled=!value&&client!=null;
        foreach(var field in fields)field.Input.Enabled=!value;
        if(message!=null)status.Text=message;
        else if(!value&&status.Text.EndsWith('…'))status.Text="Ready · "+(client?.IsLive==true?"live changes enabled":"active save selected");
        UseWaitCursor=value;UpdatePreview();
    }
    void UpdateConnection(){mode.Text=client==null?"NOT CONNECTED":client.IsLive?"● LIVE SERVER":"● OFFLINE SAVE";mode.ForeColor=client?.IsLive==true?Ui.Good:Ui.Dim;}
    void Report(string text,bool success){if(IsDisposed||Disposing)return;result.Text=text;result.ForeColor=success?Ui.Good:Ui.Bad;status.Text=success?"Updated · "+(client?.IsLive==true?"applied live":"saved to active.bin"):text.Split('\n')[0];status.ForeColor=success?Ui.Good:Ui.Bad;}
    void Record(string[] command,CommandResult answer){activity.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · UID {Player}\r\n{string.Join(' ',command)}\r\n{answer.Output}\r\n{answer.Error}".Trim());if(activity.Count>100)activity.RemoveAt(0);}
    void ShowText(string title,string text)
    {
        using var dialog=new Form{Text=title,BackColor=Ui.Bg,ForeColor=Ui.Text,StartPosition=FormStartPosition.CenterParent,Size=new(850,650),MinimumSize=new(600,400)};
        var output=new TextBox{Text=text.Replace("\n","\r\n").Replace("\r\r\n","\r\n"),ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=Ui.Surface,ForeColor=Ui.Text,Font=new Font("Consolas",12),Margin=new Padding(20)};
        dialog.Padding=new Padding(24);dialog.Controls.Add(output);dialog.ShowDialog(this);
    }
    protected override void Dispose(bool disposing){if(disposing){updates?.Dispose();connectionTimer.Dispose();}base.Dispose(disposing);}
}
