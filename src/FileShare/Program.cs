using System;
using Velopack;

namespace FileShare;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before any other code: handles Velopack's install/update/uninstall hooks.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
