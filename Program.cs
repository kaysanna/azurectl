namespace AzureCTL;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if(args.Length>0&&args[0]=="--apply-update")
        {
            if((args.Length!=4 && !(args.Length==5&&args[4]=="--no-restart")) || !UpdateWindow.IsStage(AppContext.BaseDirectory))
            { Environment.ExitCode=1;return; }
            Environment.ExitCode=UpdateWindow.Apply(args);return;
        }
        AppUpdate.CleanupStages();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "AzureCTL", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm(args));
    }
}
