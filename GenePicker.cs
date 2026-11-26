using System.Text.Json;

namespace AzureCTL;

sealed record GeneOption(uint Id,string Name,string Effect,uint FruitId,int Rarity)
{
    public override string ToString()=>$"{Name} · {Id}";
}

static class GeneCatalog
{
    public static readonly GeneOption Clear=new(0,"Clear gene slot","Clears only the last occupied gene slot.",0,0);
    public static readonly IReadOnlyList<GeneOption> Entries=Load();
    static IReadOnlyList<GeneOption> Load()
    {
        using var stream=typeof(GeneCatalog).Assembly.GetManifestResourceStream("AzureCTL.Genes.json")??throw new InvalidDataException("The official gene catalog is missing.");
        using var doc=JsonDocument.Parse(stream);
        var entries=doc.RootElement.GetProperty("genes").Deserialize<GeneOption[]>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??[];
        if(entries.Length==0||entries.Any(x=>x.Id==0||string.IsNullOrWhiteSpace(x.Name)||string.IsNullOrWhiteSpace(x.Effect))||entries.Select(x=>x.Id).Distinct().Count()!=entries.Length)
            throw new InvalidDataException("The official gene catalog is invalid.");
        return [Clear,..entries.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Id)];
    }
    public static List<GeneOption> Search(string query)=>Entries.Where(g=>$"{g.Name} {g.Id} {g.Effect} {g.FruitId}".Contains(query,StringComparison.CurrentCultureIgnoreCase)).ToList();
}

sealed class GenePicker : Panel
{
    readonly Pill button=new("Choose a gene");
    GeneOption selected=GeneCatalog.Clear;
    public string SelectedId=>selected.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public event EventHandler? SelectedIndexChanged;
    public GenePicker()
    {
        DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);
        BackColor=Ui.Surface;Controls.Add(button);button.Dock=DockStyle.Fill;
        button.Click+=(_,_)=>Choose();UpdateText();
    }
    void UpdateText(){button.Text=selected.ToString();button.AccessibleName="Gene: "+selected.Name;button.AccessibleDescription=selected.Effect;}
    void Choose()
    {
        var scale=Ui.Scale(this);int Px(float x)=>(int)Math.Round(x*scale);
        using var dialog=new Form{Text="Select Gene",BackColor=Ui.Bg,ForeColor=Ui.Text,StartPosition=FormStartPosition.CenterParent,ClientSize=new(Px(820),Px(600)),MinimumSize=new(Px(620),Px(440)),AutoScaleMode=AutoScaleMode.None};
        var search=new Input("Search genes by name, ID or effect");
        var list=new ListBox{BackColor=Ui.Surface,ForeColor=Ui.Text,BorderStyle=BorderStyle.None,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=Px(62),IntegralHeight=false};
        var detail=new TextBox{Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Ui.Field,ForeColor=Ui.Text,ScrollBars=ScrollBars.Vertical};
        var choose=new Pill("Select gene",true);var count=Ui.Label("",12);var cancel=new Pill("Cancel");
        dialog.Controls.AddRange([search,list,detail,count,choose,cancel]);
        var fonts=new Dictionary<(int Size,bool Bold),Font>();
        Font ScaledFont(float size,bool bold=false)
        {
            var key=((int)Math.Round(size*scale*64),bold);
            if(!fonts.TryGetValue(key,out var font))fonts[key]=font=Ui.Font(key.Item1/64f,bold);
            return font;
        }
        void Layout()
        {
            var w=dialog.ClientSize.Width;var h=dialog.ClientSize.Height;var pad=Px(20);
            scale=Math.Max(.5f,MathF.Round(Math.Min(w/820f,h/600f)*64)/64);
            pad=Px(20);
            dialog.SuspendLayout();
            dialog.Font=search.Box.Font=detail.Font=ScaledFont(14);list.Font=ScaledFont(16);
            count.Font=ScaledFont(12);choose.Font=cancel.Font=ScaledFont(14,true);list.ItemHeight=Px(62);
            search.SetBounds(pad,pad,w-2*pad,Px(42));count.SetBounds(pad,Px(70),w-2*pad,Px(22));
            list.SetBounds(pad,Px(100),w-2*pad,Math.Max(Px(100),h-Px(264)));
            detail.SetBounds(pad,h-Px(150),w-2*pad,Px(78));
            choose.SetBounds(w-Px(300),h-Px(56),Px(170),Px(36));cancel.SetBounds(w-Px(118),h-Px(56),Px(98),Px(36));
            dialog.ResumeLayout(false);dialog.Invalidate(true);
        }
        void Filter()
        {
            list.BeginUpdate();list.Items.Clear();list.Items.AddRange(GeneCatalog.Search(search.Box.Text.Trim()).Cast<object>().ToArray());
            list.SelectedItem=list.Items.Cast<GeneOption>().FirstOrDefault(x=>x.Id==selected.Id);
            if(list.SelectedIndex<0&&list.Items.Count>0)list.SelectedIndex=0;
            list.EndUpdate();count.Text=$"{list.Items.Count} entries · official gene catalog";choose.Enabled=list.SelectedItem!=null;
        }
        list.DrawItem+=(_,e)=>
        {
            if(e.Index<0)return;var gene=(GeneOption)list.Items[e.Index];var active=(e.State&DrawItemState.Selected)!=0;
            using var brush=new SolidBrush(active?Ui.Selected:Ui.Surface);e.Graphics.FillRectangle(brush,e.Bounds);
            Ui.TextIn(e.Graphics,gene.ToString(),list.Font,active?Ui.Accent:Ui.Text,new(e.Bounds.X+Px(12),e.Bounds.Y+Px(3),e.Bounds.Width-Px(24),Px(27)));
            Ui.TextIn(e.Graphics,gene.Effect,detail.Font,Ui.Dim,new(e.Bounds.X+Px(12),e.Bounds.Y+Px(30),e.Bounds.Width-Px(24),Px(26)));
        };
        list.SelectedIndexChanged+=(_,_)=>{choose.Enabled=list.SelectedItem!=null;detail.Text=list.SelectedItem is GeneOption g?$"{g.Name} · ID {g.Id}\r\n{g.Effect}"+(g.Id==0?"":$"\r\nFruit {g.FruitId} · Rarity {g.Rarity}"):"";};
        void Pick(){if(list.SelectedItem is not GeneOption gene)return;selected=gene;UpdateText();SelectedIndexChanged?.Invoke(this,EventArgs.Empty);dialog.DialogResult=DialogResult.OK;dialog.Close();}
        choose.Click+=(_,_)=>Pick();list.DoubleClick+=(_,_)=>Pick();cancel.Click+=(_,_)=>dialog.Close();
        list.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Pick();}};
        search.Box.TextChanged+=(_,_)=>Filter();dialog.Resize+=(_,_)=>Layout();
        Layout();Filter();dialog.Shown+=(_,_)=>search.Box.Focus();
        try{dialog.ShowDialog(FindForm());}finally{dialog.Dispose();foreach(var font in fonts.Values)font.Dispose();}
    }
}
