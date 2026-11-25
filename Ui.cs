using System.Drawing.Drawing2D;

namespace AzureCTL;

// LiveLog's desktop palette, spacing, rounded panels and lavender selection.
static class Ui
{
    public static readonly Color Bg = Color.FromArgb(16,17,21), Sidebar = Color.FromArgb(21,22,28), Surface = Color.FromArgb(27,29,37),
        Field = Color.FromArgb(20,22,29), Border = Color.FromArgb(43,46,58), Text = Color.FromArgb(243,241,250),
        Dim = Color.FromArgb(148,151,172), Accent = Color.FromArgb(193,162,255), Selected = Color.FromArgb(48,40,63),
        Hover = Color.FromArgb(37,39,50), Good = Color.FromArgb(120,223,186), Bad = Color.FromArgb(255,151,161);
    public static Font Font(float size, bool bold = false) => new(bold ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
    }
    public static void Card(Graphics g, RectangleF r, Color fill, float radius = 12, bool border = true)
    {
        using var path = Round(r, radius); using var brush = new SolidBrush(fill); g.FillPath(brush,path);
        if (border) { using var pen = new Pen(Border); g.DrawPath(pen,path); }
    }
    public static void TextIn(Graphics g, string text, Font font, Color color, Rectangle box, bool center = false)
        => TextRenderer.DrawText(g,text,font,box,color,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|(center?TextFormatFlags.HorizontalCenter:TextFormatFlags.Left));
    public static Label Label(string text, float size=14, bool bold=false, Color? color=null) => new() { Text=text, Font=Font(size,bold), ForeColor=color??Dim, BackColor=Color.Transparent, AutoSize=false };
}

sealed class Card : Panel
{
    public Card() { DoubleBuffered=true; BackColor=Ui.Surface; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Ui.Card(e.Graphics,new(0,0,Width-1,Height-1),BackColor,12*DeviceDpi/96f);
        base.OnPaint(e);
    }
}

sealed class Pill : Button
{
    public bool Primary { get; set; }
    public bool Selected { get; set; }
    bool hover;
    public Pill(string text, bool primary=false)
    {
        Text=text;Primary=primary;Font=Ui.Font(14,true);ForeColor=Ui.Text;BackColor=Ui.Bg;
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;TabStop=true;Height=40;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnMouseEnter(EventArgs e) {hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e) {hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor??Ui.Bg);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var fill=Primary?Ui.Accent:Selected?Ui.Selected:hover?Ui.Hover:Color.FromArgb(35,37,48);
        Ui.Card(e.Graphics,new(0,0,Width-1,Height-1),fill,8*DeviceDpi/96f,false);
        Ui.TextIn(e.Graphics,Text,Font,!Enabled?Ui.Dim:Primary?Ui.Bg:Selected?Ui.Accent:Ui.Text,ClientRectangle,true);
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),Ui.Accent,fill);
    }
}

sealed class Input : Panel
{
    public TextBox Box { get; } = new() { BorderStyle=BorderStyle.None, BackColor=Ui.Field, ForeColor=Ui.Text, Font=Ui.Font(14) };
    public Input(string placeholder="")
    {
        BackColor=Ui.Field;DoubleBuffered=true;Height=42;Box.PlaceholderText=placeholder;Controls.Add(Box);
        Box.GotFocus+=(_,_)=>Invalidate();Box.LostFocus+=(_,_)=>Invalidate();
    }
    protected override void OnResize(EventArgs e) {base.OnResize(e);var s=DeviceDpi/96f;Box.SetBounds((int)(12*s),(Height-Box.PreferredHeight)/2,Math.Max(10,Width-(int)(24*s)),Box.PreferredHeight);}
    protected override void OnPaint(PaintEventArgs e) {e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;Ui.Card(e.Graphics,new(0,0,Width-1,Height-1),Ui.Field,8*DeviceDpi/96f);if(Box.Focused){using var pen=new Pen(Ui.Accent);using var p=Ui.Round(new(0,0,Width-1,Height-1),8*DeviceDpi/96f);e.Graphics.DrawPath(pen,p);}base.OnPaint(e);}
}

sealed class Choice : Control
{
    public List<object> Items { get; }=[];
    int selected=-1;
    public event EventHandler? SelectedIndexChanged;
    public int SelectedIndex { get=>selected;set {if(value==selected)return;selected=value;Invalidate();SelectedIndexChanged?.Invoke(this,EventArgs.Empty);} }
    public object? SelectedItem { get=>selected>=0&&selected<Items.Count?Items[selected]:null;set=>SelectedIndex=Items.IndexOf(value!); }
    public Choice()
    {
        DoubleBuffered=true;BackColor=Ui.Field;ForeColor=Ui.Text;Font=Ui.Font(14);Cursor=Cursors.Hand;TabStop=true;AccessibleRole=AccessibleRole.ComboBox;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var s=DeviceDpi/96f;e.Graphics.Clear(Parent?.BackColor??Ui.Bg);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Ui.Card(e.Graphics,new(0,0,Width-1,Height-1),Ui.Field,8*s);Ui.TextIn(e.Graphics,SelectedItem?.ToString()??"Choose an option",Font,Enabled?Ui.Text:Ui.Dim,new((int)(12*s),0,Math.Max(1,Width-(int)(46*s)),Height));
        using var pen=new Pen(Ui.Dim,1.5f*s);var x=Width-19*s;var y=Height/2f;e.Graphics.DrawLines(pen,new PointF[]{new(x-4*s,y-2*s),new(x,y+2*s),new(x+4*s,y-2*s)});
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4),Ui.Accent,Ui.Field);
    }
    protected override void OnClick(EventArgs e){base.OnClick(e);ShowOptions();}
    protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Up or Keys.Down||base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down){SelectedIndex=Math.Clamp(selected+(e.KeyCode==Keys.Down?1:-1),0,Items.Count-1);e.Handled=true;}else if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){ShowOptions();e.Handled=true;}}
    void ShowOptions()
    {
        if(Items.Count==0||!Enabled)return;Focus();
        var list=new ListBox{BorderStyle=BorderStyle.None,BackColor=Ui.Field,ForeColor=Ui.Text,Font=Font,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=(int)(36*DeviceDpi/96f),IntegralHeight=false};
        list.Items.AddRange(Items.ToArray());list.SelectedIndex=selected;
        var height=Math.Min(list.ItemHeight*Items.Count+4,(int)(300*DeviceDpi/96f));
        list.Size=new(Math.Max(100,Width-2),height);
        var host=new ToolStripControlHost(list){AutoSize=false,Size=list.Size,Margin=Padding.Empty,Padding=Padding.Empty};
        var popup=new ToolStripDropDown{Padding=new Padding(1),BackColor=Ui.Border,AutoSize=true,DropShadowEnabled=false};popup.Items.Add(host);
        list.DrawItem+=(_,a)=>{if(a.Index<0)return;var active=(a.State&DrawItemState.Selected)!=0;using var b=new SolidBrush(active?Ui.Selected:Ui.Field);a.Graphics.FillRectangle(b,a.Bounds);Ui.TextIn(a.Graphics,Items[a.Index].ToString()??"",Font,active?Ui.Accent:Ui.Text,Rectangle.Inflate(a.Bounds,-10,0));};
        void Pick(){if(list.SelectedIndex>=0)SelectedIndex=list.SelectedIndex;popup.Close();}
        list.MouseClick+=(_,_)=>Pick();list.KeyDown+=(_,a)=>{if(a.KeyCode==Keys.Enter){Pick();a.Handled=true;}};
        // ToolStrip is still unwinding its mouse dispatch during Closed.
        // Dispose on the next UI turn, after that dispatch has returned.
        popup.Closed+=(_,_)=>BeginInvoke((Action)(()=>popup.Dispose()));popup.Show(this,new Point(0,Height+4));list.Focus();
    }
}

// Actions stay visible instead of requiring a dropdown to be reopened.
sealed class ActionPicker : Panel
{
    readonly List<Pill> buttons=[];
    Operation[] items=[];
    int selected=-1;
    float S=>DeviceDpi/96f;
    public Operation? SelectedItem=>selected>=0&&selected<items.Length?items[selected]:null;
    public event EventHandler? SelectedIndexChanged;
    public ActionPicker(){DoubleBuffered=true;BackColor=Ui.Surface;AccessibleRole=AccessibleRole.Grouping;}
    public void SetItems(IEnumerable<Operation> values)
    {
        foreach(var button in buttons){Controls.Remove(button);button.Dispose();}
        buttons.Clear();items=values.ToArray();selected=-1;
        for(var i=0;i<items.Length;i++)
        {
            var index=i;var button=new Pill(items[i].Name){AccessibleName=items[i].Name,AccessibleDescription=items[i].Description};
            button.Click+=(_,_)=>SelectAction(index);buttons.Add(button);Controls.Add(button);
        }
        LayoutButtons();if(items.Length>0)SelectAction(0);
    }
    int Columns(float width)=>width>=360?3:2;
    public float HeightForWidth(float width)=>items.Length==0?0:((items.Length+Columns(width)-1)/Columns(width))*38-6;
    void SelectAction(int index)
    {
        if(selected==index)return;selected=index;
        for(var i=0;i<buttons.Count;i++){buttons[i].Selected=i==selected;buttons[i].Invalidate();}
        SelectedIndexChanged?.Invoke(this,EventArgs.Empty);
    }
    void LayoutButtons()
    {
        var width=ClientSize.Width/S;var columns=Columns(width);var cell=(width-(columns-1)*8)/columns;
        for(var i=0;i<buttons.Count;i++)buttons[i].SetBounds((int)Math.Round(i%columns*(cell+8)*S),(int)Math.Round(i/columns*38*S),(int)Math.Round(cell*S),(int)Math.Round(32*S));
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);LayoutButtons();}
}

sealed class Library : Control
{
    public List<LibraryItem> Items { get; private set; } = [];
    public LibraryItem? Selected { get; private set; }
    public event Action<LibraryItem>? SelectionChanged;
    int scroll,hover=-1;
    float S=>DeviceDpi/96f;
    int RowHeight=>(int)(66*S);
    public Library(){DoubleBuffered=true;BackColor=Ui.Surface;TabStop=true;Cursor=Cursors.Hand;Font=Ui.Font(14);}
    public void SetItems(IEnumerable<LibraryItem> items, string? selected=null)
    {
        Items=items.ToList();Selected=Items.FirstOrDefault(x=>x.Id==selected);scroll=0;Invalidate();
    }
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);scroll=Math.Clamp(scroll-e.Delta/120*RowHeight,0,Math.Max(0,Items.Count*RowHeight-(Height-(int)(44*S))));Invalidate();}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);hover=(e.Y-(int)(44*S)+scroll)/RowHeight;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=-1;Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);Focus();if(e.Y<44*S)return;var index=(e.Y-(int)(44*S)+scroll)/RowHeight;
        if(index>=0&&index<Items.Count){Selected=Items[index];Invalidate();SelectionChanged?.Invoke(Selected);}
    }
    protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Up or Keys.Down||base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);if(e.KeyCode is not(Keys.Up or Keys.Down)||Items.Count==0)return;
        var index=Math.Clamp(Selected==null?0:Items.IndexOf(Selected)+(e.KeyCode==Keys.Down?1:-1),0,Items.Count-1);
        Selected=Items[index];scroll=Math.Clamp(index*RowHeight-(Height-(int)(44*S)-RowHeight),0,Math.Max(0,Items.Count*RowHeight-(Height-(int)(44*S))));Invalidate();SelectionChanged?.Invoke(Selected);e.Handled=true;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;Ui.Card(g,new(0,0,Width-1,Height-1),Ui.Surface,12*S);
        using var small=Ui.Font(11*S);using var name=Ui.Font(15*S,true);using var detail=Ui.Font(11*S);
        Ui.TextIn(g,"NAME",small,Ui.Dim,new((int)(18*S),0,Width/2,(int)(44*S)));
        Ui.TextIn(g,"ID",small,Ui.Dim,new(Width-(int)(122*S),0,(int)(100*S),(int)(44*S)));
        var clip=g.Save();g.SetClip(new Rectangle(1,(int)(44*S),Math.Max(1,Width-2),Math.Max(1,Height-(int)(44*S)-1)));
        for(var i=0;i<Items.Count;i++)
        {
            var y=(int)(44*S)+i*RowHeight-scroll;if(y+RowHeight<44*S||y>Height)continue;var row=Items[i];
            if(row==Selected)Ui.Card(g,new(1,y,Width-2,RowHeight),Ui.Selected,0,false);else if(i==hover)Ui.Card(g,new(1,y,Width-2,RowHeight),Ui.Hover,0,false);
            using var pen=new Pen(Ui.Border);g.DrawLine(pen,16*S,y+RowHeight,Width-16*S,y+RowHeight);
            var badge=new RectangleF(14*S,y+8*S,48*S,48*S);Ui.Card(g,badge,Ui.Field,9*S,false);
            var image=CatalogImages.Get(row.ImageKey);
            if(image!=null){var ratio=Math.Min(badge.Width/image.Width,badge.Height/image.Height);g.DrawImage(image,badge.X+(badge.Width-image.Width*ratio)/2,badge.Y+(badge.Height-image.Height*ratio)/2,image.Width*ratio,image.Height*ratio);}
            else Ui.TextIn(g,row.Name.Length>0?row.Name[..1]:"?",name,Ui.Accent,Rectangle.Round(badge),true);
            Ui.TextIn(g,row.Name,name,row==Selected?Ui.Accent:Ui.Text,new((int)(74*S),y+(int)(8*S),Math.Max(1,Width-(int)(210*S)),(int)(26*S)));
            Ui.TextIn(g,row.Detail,detail,Ui.Dim,new((int)(74*S),y+(int)(34*S),Math.Max(1,Width-(int)(96*S)),(int)(22*S)));
            Ui.TextIn(g,row.Id,small,Ui.Dim,new(Width-(int)(122*S),y+(int)(8*S),(int)(105*S),(int)(26*S)));
        }
        g.Restore(clip);
        if(Items.Count==0)Ui.TextIn(g,"No matching entries",Font,Ui.Dim,new(15,(int)(60*S),Width-30,80),true);
        var full=Items.Count*RowHeight;var available=Height-(int)(44*S);
        if(full>available){var thumb=Math.Max(28*S,available*(float)available/full);var top=44*S+scroll/(float)(full-available)*(available-thumb);Ui.Card(g,new(Width-7*S,top,3*S,thumb),Ui.Dim,2*S,false);}
    }
}
