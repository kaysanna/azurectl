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
    readonly LibraryCache libraryCache=new();
    int cacheEpoch,libraryRequest;
    bool loadingLibrary;
    readonly System.Windows.Forms.Timer connectionTimer=new(){Interval=2000};
    CtlClient? client;
    List<LibraryItem> allItems=[];
    List<LibraryItem> heroChoices=[];
    int page;
    bool busy,initializing=true;
    string SettingsPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AzureCTL","settings.json");
    Section Section=>Commands.Sections[page];
    Operation? SelectedOperation=>operation.SelectedItem;
    bool AddingMissingKibos=>SelectedOperation is{} op&&Commands.IsAddMissingKibos(op.Template);
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
        rootField.Box.TextChanged+=(_,_)=>{if(initializing||busy)return;client=null;libraryCache.Clear();cacheEpoch++;libraryRequest++;loadingLibrary=false;allItems=[];Filter();status.Text="Folder changed · press Refresh to connect";UpdateConnection();UpdatePreview();};
        search.Box.TextChanged+=(_,_)=>Filter();
        players.SelectedIndexChanged+=async(_,_)=>{if(!initializing&&!busy){await LoadLibrary();if(client!=null)_ = WarmLibraries(client,Player,cacheEpoch);}};
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
        Resize+=(_,_)=>LayoutControls();DpiChanged+=(_,_)=>{UpdateMinimumSize();LayoutControls();};
        foreach(var control in new Control[]{this,brand,heading})control.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,2,0);}};
        try{if(File.Exists(SettingsPath)){using var settings=JsonDocument.Parse(File.ReadAllText(SettingsPath));rootField.Box.Text=settings.RootElement.GetProperty("root").GetString()??"";}}catch{}
        for(var i=0;i<args.Length-1;i++)if(args[i]=="--root")rootField.Box.Text=args[++i];
        initializing=false;SetOperations();LayoutControls();
        Shown+=async(_,_)=>
        {
            UpdateMinimumSize();var area=Screen.FromControl(this).WorkingArea;
            if(Width>area.Width||Height>area.Height)Size=new(Math.Min(Width,area.Width),Math.Min(Height,area.Height));
            Location=new(Math.Clamp(Left,area.Left,area.Right-Width),Math.Clamp(Top,area.Top,area.Bottom-Height));
            LayoutControls();
            if(rootField.Box.Text.Length>0)await Connect();
        };
    }

    [DllImport("user32.dll")]static extern bool ReleaseCapture();
    [DllImport("user32.dll")]static extern nint SendMessage(nint window,uint message,nint wparam,nint lparam);
    void UpdateMinimumSize()
    {
        var area=Screen.FromControl(this).WorkingArea;
        // MinimumSize is physical pixels; reserve the logical workspace at this monitor's DPI.
        MinimumSize=new(Math.Min(area.Width,Px(1040)),Math.Min(area.Height,Px(740)));
    }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if(message.Msg!=0x84||WindowState!=FormWindowState.Normal)return; // WM_NCHITTEST
        var packed=message.LParam.ToInt64();
        var point=PointToClient(new Point(unchecked((short)packed),unchecked((short)(packed>>16))));
        var grip=Px(8);var left=point.X<grip;var right=point.X>=ClientSize.Width-grip;
        var top=point.Y<grip;var bottom=point.Y>=ClientSize.Height-grip;
        if(left||right||top||bottom)
            message.Result=(nint)(top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:11);
    }
    void Place(Control c,float x,float y,float w,float h)=>c.SetBounds(Px(x),Px(y),Math.Max(1,Px(w)),Math.Max(1,Px(h)));

    void LayoutControls()
    {
        var w=ClientSize.Width/S;var h=ClientSize.Height/S;var compact=h<820||w<1240;
        var side=compact?196f:232f;var x=side+24;var content=w-x-24;var gap=compact?16f:24f;
        var left=Math.Max(200,Math.Min((content-gap)*.46f,content-gap-370));var right=content-left-gap;
        Place(sidebar,0,0,side,h);Place(brand,20,compact?22:36,side-32,40);Place(tag,22,compact?64:80,side-32,20);
        var footerTop=h-150;var navTop=compact?104f:140f;
        var navStep=Math.Min(54,(footerTop-navTop-12)/Math.Max(1,navigation.Count));
        for(var i=0;i<navigation.Count;i++){Place(navigation[i],12,navTop+i*navStep,side-24,Math.Min(42,navStep-6));navigation[i].Selected=i==page;navigation[i].Invalidate();}
        Place(updateText,20,footerTop,side-32,36);Place(checkUpdates,12,footerTop+42,side-24,34);
        Place(version,20,footerTop+84,side-32,20);Place(mode,20,footerTop+112,side-32,22);
        Place(heading,x,compact?42:66,content-150,38);Place(subtitle,x,compact?84:108,content,26);
        Place(minimize,w-132,8,36,28);Place(maximize,w-90,8,36,28);Place(close,w-48,8,36,28);
        var rootTop=compact?138f:166f;var browseWidth=compact?98f:130f;var refreshWidth=compact?88f:126f;
        var playerWidth=compact?Math.Clamp(content*.28f,150,220):256f;var rootWidth=content-browseWidth-playerWidth-refreshWidth-30;
        Place(rootLabel,x,rootTop-22,content,20);
        Place(rootField,x,rootTop,rootWidth,42);Place(browse,x+rootWidth+10,rootTop,browseWidth,42);
        Place(players,x+rootWidth+browseWidth+20,rootTop,playerWidth,42);Place(refresh,x+content-refreshWidth,rootTop,refreshWidth,42);
        var searchTop=rootTop+80;
        Place(libraryLabel,x,searchTop-24,left,20);Place(search,x,searchTop,left,42);Place(library,x,searchTop+56,left,h-searchTop-118);
        // The right card aligns exactly with the top of the search field.
        Place(editor,x+left+gap,searchTop,right,h-searchTop-66);
        Place(status,x,h-47,Math.Max(100,content-124),30);Place(logs,x+content-112,h-49,112,32);
        LayoutEditor();
    }

    void LayoutEditor()
    {
        if(editor.Width==0)return;
        var compact=ClientSize.Height/S<820||ClientSize.Width/S<1240;
        var footer=compact?62:76;
        editorBody.SetBounds(1,1,editor.Width-2,Math.Max(1,editor.Height-Px(footer)));
        editorFooter.SetBounds(1,editor.Height-Px(footer-1),editor.Width-2,Px(footer-2));
        var w=editorBody.ClientSize.Width/S;var inner=w-40;
        var titleWidth=AddingMissingKibos?inner:inner-160;
        Place(selectedName,20,compact?8:14,titleWidth,32);Place(selectedDetail,20,compact?39:48,titleWidth,24);
        Place(manualLabel,20+inner-144,14,144,18);Place(manualId,20+inner-144,38,144,36);
        var actionHeight=operation.HeightForWidth(inner);
        var actionTop=compact?90:108;
        Place(operationLabel,20,actionTop-22,inner,18);Place(operation,20,actionTop,inner,actionHeight);
        var fieldsTop=actionTop+actionHeight+10;
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
        Place(apply,20,compact?10:18,footerInner-182,40);Place(inspect,20+footerInner-168,compact?10:18,80,40);Place(catalog,20+footerInner-80,compact?10:18,80,40);
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
            if(Section.Category=="pet"&&arg.Key=="gene"){var choice=new GenePicker();choice.SelectedIndexChanged+=(_,_)=>UpdatePreview();input=choice;}
            else if(arg.Choices is{} choices){var choice=new Choice();choice.Items.AddRange(choices);choice.SelectedItem=arg.Default;choice.SelectedIndexChanged+=(_,_)=>UpdatePreview();input=choice;}
            else {var field=new Input();field.Box.Text=arg.Default;field.Box.TextChanged+=(_,_)=>UpdatePreview();input=field;}
            input.AccessibleName=arg.Label;if(input is Input edit)edit.Box.AccessibleName=arg.Label;
            fields.Add((arg,input,label));editorBody.Controls.Add(label);editorBody.Controls.Add(input);
        }
        manualLabel.Visible=manualId.Visible=!AddingMissingKibos;
        result.Text="";
        note.Text=op.Description;apply.Text=op.Name;
        UpdateSelected();LayoutEditor();UpdatePreview();
    }
    string[] CurrentCommand()
    {
        var op=SelectedOperation??throw new InvalidOperationException("Choose an action.");
        var id=manualId.Box.Text.Trim();if(op.NeedsSelection&&(!ulong.TryParse(id,out var parsedId)||parsedId==0))throw new InvalidOperationException("Select an entry or enter its numeric ID.");
        var values=fields.ToDictionary(x=>x.Arg.Key,x=>x.Input is GenePicker gene?gene.SelectedId:x.Input is Choice choice?choice.SelectedItem?.ToString()??"":((Input)x.Input).Box.Text.Trim());
        foreach(var value in values.Values)if(value.Length==0||value.Any(char.IsWhiteSpace))throw new InvalidOperationException("Complete all action fields with a single value.");
        return op.Expand(id,values);
    }
    void UpdatePreview()
    {
        try {preview.Box.Text=$"{Player} "+string.Join(' ',CurrentCommand());apply.Enabled=client!=null&&Player!=0&&!busy&&!loadingLibrary;}
        catch {preview.Box.Text="Select an entry and complete the fields";apply.Enabled=false;}
    }
    void UpdateSelected()
    {
        if(AddingMissingKibos)
        {
            selectedName.Text="Kibo collection";
            selectedDetail.Text=players.SelectedItem?.ToString()??"Choose a player account";
            return;
        }
        var id=manualId.Box.Text.Trim();var item=allItems.FirstOrDefault(x=>x.Id==id);
        selectedName.Text=item?.Name??(id.Length>0?Section.Name+" · "+id:Section.Category=="world"?"World difficulty":"Pick "+(Section.Category is "hero" or "heroes"?"a character":"an entry"));
        selectedDetail.Text=item?.Detail??(id.Length>0?"Manual ID · validated by Azurepro":"from the list, or enter an ID below");
    }
    void Filter()=>library.SetItems(allItems.Where(x=>(x.Name+" "+x.Id+" "+x.Detail).Contains(search.Box.Text,StringComparison.OrdinalIgnoreCase)),manualId.Box.Text);

    async Task Connect()
    {
        if(busy)return;libraryRequest++;loadingLibrary=false;SetBusy(true,"Connecting to Azurepro…");
        libraryCache.Clear();cacheEpoch++;
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
        if(client!=null){await LoadLibrary();_ = WarmLibraries(client,Player,cacheEpoch);}
    }

    static async Task<List<LibraryItem>> ReadLibrary(CtlClient connection,uint player,Section section)
    {
        string[] cmd=section.Category switch{"heroes"=>["catalog","heroes"],"gear-catalog"=>["catalog","gear"],"souls"=>["catalog","souls"],"world"=>["world","difficulty","list"],_=>[section.Category,"list"]};
        var answer=await connection.RunAsync(player,cmd);if(!answer.Success)throw new IOException(answer.Error.Length>0?answer.Error:answer.Output);
        return section.Category=="world"?Regex.Matches(answer.Output,@"map (\d+): tier (\d+)").Select(m=>new LibraryItem(m.Groups[1].Value,"Map "+m.Groups[1].Value,"World level "+m.Groups[2].Value)).ToList():CtlClient.ParseList(section.Category=="gear-catalog"?"gear":section.Category,answer.Output);
    }
    async Task WarmLibraries(CtlClient connection,uint player,int epoch)
    {
        foreach(var section in Commands.Sections)
        {
            if(IsDisposed||Disposing||client!=connection||Player!=player||epoch!=cacheEpoch)return;
            try{await libraryCache.GetAsync(connection.Root,player,section.Category,()=>ReadLibrary(connection,player,section));}
            catch{/* A failed preload is retried and reported if its tab is opened. */}
        }
    }

    async Task LoadLibrary()
    {
        if(IsDisposed||Disposing||busy)return;
        var request=++libraryRequest;
        if(client==null||Player==0){loadingLibrary=false;allItems=[];Filter();UpdatePreview();return;}
        var section=Section;var connection=client;var player=Player;var epoch=cacheEpoch;
        var old=manualId.Box.Text;
        try
        {
            var task=libraryCache.GetAsync(connection.Root,player,section.Category,()=>ReadLibrary(connection,player,section));
            if(!task.IsCompletedSuccessfully)
            {
                loadingLibrary=true;allItems=[];manualId.Box.Text="";Filter();UpdateSelected();
                library.Enabled=manualId.Enabled=inspect.Enabled=false;UpdatePreview();
                status.Text="Loading "+section.Name.ToLowerInvariant()+" list…";
            }
            var items=await task;
            if(IsDisposed||Disposing||request!=libraryRequest||client!=connection||Player!=player||epoch!=cacheEpoch)return;
            allItems=items;
            if(section.Category=="hero")heroChoices=allItems.ToList();
            manualId.Box.Text=allItems.Any(x=>x.Id==old)?old:"";Filter();UpdateSelected();
            status.Text=$"{allItems.Count} entries · "+(connection.IsLive?"Live changes enabled":"Server offline · edits persist to active save");status.ForeColor=Ui.Dim;
        }
        catch(Exception ex){if(request==libraryRequest&&!IsDisposed&&!Disposing)Report(ex.Message,false);}
        finally
        {
            if(request==libraryRequest&&!IsDisposed&&!Disposing)
            {
                loadingLibrary=false;library.Enabled=manualId.Enabled=!busy;inspect.Enabled=!busy&&client!=null;UpdatePreview();
            }
        }
    }

    async Task Apply()
    {
        if(client==null||busy||loadingLibrary)return;
        string[] command;try{command=CurrentCommand();}catch(Exception ex){Report(ex.Message,false);return;}
        if(command.Length>1&&command[0]=="gear"&&command[1]=="remove"&&MessageBox.Show(this,"Remove equipment "+manualId.Box.Text+" from the current save?","Remove equipment",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        var addMissingKibos=Commands.IsAddMissingKibos(command);
        SetBusy(true,"Applying "+SelectedOperation!.Name.ToLowerInvariant()+"…");
        try
        {
            var answer=await client.RunAsync(Player,command);Record(command,answer);
            if(!answer.Success)throw new IOException(CtlClient.ActionError(command,answer));
            Report(answer.Output.Length>0?answer.Output:"Updated the active save.",true);
        }
        catch(Exception ex){Report(ex.Message,false);}
        finally
        {
            if(addMissingKibos)libraryCache.Invalidate(client!.Root,Player,"pet");
            else {libraryCache.Invalidate(client!.Root,Player);cacheEpoch++;}
            SetBusy(false);
        }
        await LoadLibrary();
        if(!addMissingKibos&&client!=null)_ = WarmLibraries(client,Player,cacheEpoch);
    }

    async Task InspectSelected()
    {
        if(client==null||busy||loadingLibrary||manualId.Box.Text.Length==0)return;
        string[] command=Section.Category switch{"hero"=>["hero","inspect",manualId.Box.Text],"gear"=>["gear","inspect",manualId.Box.Text],"pet"=>["pet","inspect",manualId.Box.Text],"heroes"=>["catalog","heroes",manualId.Box.Text],"gear-catalog"=>["catalog","gear",manualId.Box.Text],"soul" or "souls"=>["catalog","souls",manualId.Box.Text],_=>["world","difficulty","list"]};
        SetBusy(true,"Reading selected entry…");
        try{var answer=await client.RunAsync(Player,command);if(!answer.Success)throw new IOException(answer.Error.Length>0?answer.Error:answer.Output);ShowText(allItems.FirstOrDefault(x=>x.Id==manualId.Box.Text)?.Name??selectedName.Text,answer.Output);}
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
        busy=value;browse.Enabled=refresh.Enabled=players.Enabled=operation.Enabled=!value;manualId.Enabled=library.Enabled=!value&&!loadingLibrary;
        rootField.Enabled=!value;
        checkUpdates.Enabled=!value&&!updateChecking;
        foreach(var button in navigation)button.Enabled=!value;inspect.Enabled=!value&&!loadingLibrary&&client!=null;catalog.Enabled=!value&&client!=null;
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
