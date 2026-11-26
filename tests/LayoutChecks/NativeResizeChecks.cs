using System.Reflection;
using System.Runtime.InteropServices;

static class NativeResizeChecks
{
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    static extern bool ValidateRect(nint window, nint rect);

    [DllImport("user32.dll")]
    static extern bool GetUpdateRect(nint window, out NativeRect rect, bool erase);

    sealed class TestWindow : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    public static void Run(Assembly assembly)
    {
        using var form = new TestWindow { ShowInTaskbar = false, ClientSize = new Size(500, 500), Opacity = 0 };
        var names = new[] { "Card", "Pill", "Input", "Choice", "Library", "ActionPicker" };
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var type = assembly.GetType("AzureCTL." + name, throwOnError: true)!;
            object[] parameters = name switch
            {
                "Input" => ["Search"],
                "Pill" => ["Apply", false],
                _ => []
            };
            var control = (Control)Activator.CreateInstance(type, parameters)!;
            control.SetBounds(12, 12 + i * 62, 220, 42);
            form.Controls.Add(control);
        }

        form.Show();
        Application.DoEvents();
        foreach (Control control in form.Controls)
        {
            foreach (var size in new[] { new Size(280, 52), new Size(220, 42) })
            {
                // Discard previous paint requests so only this size change contributes to the update region.
                if (!ValidateRect(control.Handle, 0))
                    throw new InvalidOperationException("Could not validate initial control pixels.");
                control.Size = size;
                if (!GetUpdateRect(control.Handle, out var rect, false) ||
                    rect.Left != 0 || rect.Top != 0 || rect.Right != control.Width || rect.Bottom != control.Height)
                    throw new InvalidOperationException($"{control.GetType().Name} did not invalidate its previous edges after sizing to {size}.");
            }
        }

        var input = form.Controls.Cast<Control>().Single(c => c.GetType().Name == "Input");
        var box = (TextBox)input.Controls[0];
        using var font = new Font("Segoe UI", 24, FontStyle.Regular, GraphicsUnit.Pixel);
        var originalFont = box.Font;
        box.Font = font;
        if (box.Top != (input.Height - box.PreferredHeight) / 2)
            throw new InvalidOperationException("Input did not recenter its text box when the font changed without a resize.");
        box.Font = originalFont;

        var button = form.Controls.Cast<Control>().Single(c => c.GetType().Name == "Pill");
        using var bitmap = new Bitmap(button.Width, button.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            // ButtonBase can paint as opaque, skipping OnPaintBackground.
            var paint = button.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            paint.Invoke(button, [new PaintEventArgs(graphics, button.ClientRectangle)]);
        }
        foreach(var point in new[]{Point.Empty,new Point(button.Width-1,0),new Point(0,button.Height-1),new Point(button.Width-1,button.Height-1)})
            if(bitmap.GetPixel(point.X,point.Y).ToArgb()!=form.BackColor.ToArgb())
                throw new InvalidOperationException("Rounded button corners must paint the parent background, including opaque paint paths.");

        Console.WriteLine($"PASS native resize invalidation for {names.Length} controls and input font recentering");
        Console.WriteLine("PASS rounded button corner pixels match the parent background");
    }
}
