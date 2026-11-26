using System.Collections;
using System.Reflection;

static class Checks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [STAThread] static void Main()
    {
        ApplicationConfiguration.Initialize();
        var type=Assembly.Load("AzureCTL").GetType("AzureCTL.MainForm")!;
        using var form=(Form)Activator.CreateInstance(type,new object[]{new[]{"--skip-update"}})!;
        _=form.Handle;
        object Field(string name)=>type.GetField(name,Private)!.GetValue(form)!;
        Control Control(string name)=>(Control)Field(name);
        void Invoke(string name)=>type.GetMethod(name,Private)!.Invoke(form,null);
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        Invoke("UpdateMinimumSize");
        var minimum=form.MinimumSize;
        form.Size=new Size(400,300);
        Check(form.Width>=minimum.Width&&form.Height>=minimum.Height,"Minimum window dimensions must be enforced");
        var cases=0;
        foreach(var size in new[]{minimum,new Size(Math.Max(minimum.Width,1420),Math.Max(minimum.Height,940)),new Size(Math.Max(minimum.Width,1920),Math.Max(minimum.Height,1080))})
        {
            form.ClientSize=size;
            for(var page=0;page<8;page++)
            {
                type.GetField("page",Private)!.SetValue(form,page);Invoke("SetOperations");Invoke("LayoutControls");
                var nav=((IEnumerable)Field("navigation")).Cast<Control>().ToArray();
                Check(nav.All(n=>n.Bottom<Control("updateText").Top),"Navigation overlaps updater");
                var toolbar=new[]{"rootField","browse","players","refresh"}.Select(Control).ToArray();
                for(var i=0;i<toolbar.Length-1;i++)Check(toolbar[i].Right<=toolbar[i+1].Left,"Folder/account controls overlap");
                Check(toolbar[^1].Right<=form.ClientSize.Width,"Toolbar leaves the window");
                Check(Control("editor").Top==Control("search").Top,"Editor and search are misaligned");
                var picker=Field("operation");var pt=picker.GetType();
                var items=(Array)pt.GetField("items",Private)!.GetValue(picker)!;
                for(var action=0;action<items.Length;action++)
                {
                    pt.GetMethod("SelectAction",Private)!.Invoke(picker,new object[]{action});Invoke("LayoutEditor");
                    var body=Control("editorBody");
                    Check(Control("operation").Bottom<=body.Height,"Actions are clipped");
                    Check(Control("note").Bottom<=body.Height,"Action fields/help are clipped");
                    foreach(var name in new[]{"apply","inspect","catalog"})
                    {var c=Control(name);Check(c.Left>=0&&c.Right<=c.Parent!.Width&&c.Bottom<=c.Parent.Height,"Footer buttons overlap or clip");}
                    cases++;
                }
            }
        }
        form.ClientSize=minimum;
        foreach(var (point,expected) in new[]{(new Point(1,1),13),(new Point(form.ClientSize.Width-1,1),14),(new Point(1,form.ClientSize.Height-1),16),(new Point(form.ClientSize.Width-1,form.ClientSize.Height-1),17)})
        {
            var p=form.PointToScreen(point);var msg=Message.Create(form.Handle,0x84,0,(nint)((p.Y<<16)|(p.X&0xffff)));
            object[] parameters={msg};type.GetMethod("WndProc",Private)!.Invoke(form,parameters);
            Check(((Message)parameters[0]).Result==(nint)expected,"Resize corner hit testing failed");
        }
        var geneCatalog=type.Assembly.GetType("AzureCTL.GeneCatalog")!;
        var geneRows=((IEnumerable)geneCatalog.GetField("Entries",BindingFlags.Static|BindingFlags.Public)!.GetValue(null)!).Cast<object>().ToArray();
        Check(geneRows.Length==103,"Expected all 102 official genes plus the clear-slot option");
        var found=((IEnumerable)geneCatalog.GetMethod("Search")!.Invoke(null,new object[]{"533001"})!).Cast<object>().ToArray();
        Check(found.Any(g=>(uint)g.GetType().GetProperty("Id")!.GetValue(g)! == 533001),"Gene search must find official IDs");
        var effectQuery=((string)geneRows[1].GetType().GetProperty("Effect")!.GetValue(geneRows[1])!).Split(' ')[0];
        Check(((IEnumerable)geneCatalog.GetMethod("Search")!.Invoke(null,new object[]{effectQuery})!).Cast<object>().Any(),"Gene search must include effect descriptions");
        Console.WriteLine($"PASS {cases} page/action/size layouts, minimum size and all resize corners; DPI {form.DeviceDpi}");
        Console.WriteLine("PASS complete named gene catalog and ID/effect search");
    }
}
