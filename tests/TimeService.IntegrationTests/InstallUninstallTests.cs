using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace TimeService.IntegrationTests;

[Collection(nameof(PublishedExeCollection))]
public class InstallUninstallTests(PublishedExeFixture fixture)
{
    [SkippableFact]
    public void Install_then_uninstall_round_trips_via_real_scm()
    {
        Skip.IfNot(IsAdministrator(), "Requires an elevated (administrator) test runner to register Windows services.");

        var serviceName = $"OctopusTimeService_Test_{Guid.NewGuid():N}";
        var installed = false;

        try
        {
            Assert.False(ServiceExists(serviceName), "Service should not exist before install.");

            var installExit = RunExe(fixture.ExePath, ["install", "--serviceName", serviceName]);
            Assert.Equal(0, installExit);
            installed = true;

            Assert.True(ServiceExists(serviceName), "Service should exist after install.");

            var uninstallExit = RunExe(fixture.ExePath, ["uninstall", "--serviceName", serviceName]);
            Assert.Equal(0, uninstallExit);
            installed = false;

            Assert.False(ServiceExists(serviceName), "Service should not exist after uninstall.");
        }
        finally
        {
            if (installed)
            {
                // Best-effort cleanup on assertion/exception path.
                RunExe(fixture.ExePath, ["uninstall", "--serviceName", serviceName]);
            }
        }
    }

    private static int RunExe(string exePath, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {exePath}");

        proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static bool ServiceExists(string serviceName)
    {
        foreach (var svc in ServiceController.GetServices())
        {
            using (svc)
            {
                if (string.Equals(svc.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
