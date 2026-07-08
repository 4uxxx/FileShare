using System;
using FileShare.Services;
using Velopack;

namespace FileShare;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before any other code: handles Velopack's install/update/uninstall hooks.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => ShellIntegrationService.Register())
            .OnAfterUpdateFastCallback(_ => ShellIntegrationService.Register())
            .OnBeforeUninstallFastCallback(_ => ShellIntegrationService.Unregister())
            .Run();

        var incomingPath = args.Length > 0 ? args[0] : null;

        var singleInstance = new SingleInstanceService();
        if (!singleInstance.AcquireOrForward(incomingPath))
        {
            // Another instance is already running and has been notified; exit quietly.
            return;
        }

        var app = new App(singleInstance, incomingPath);
        app.InitializeComponent();
        app.Run();
    }
}
