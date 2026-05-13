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

    [SkippableFact]
    public void Install_with_dependent_flag_adds_our_service_to_target_dependency_list()
    {
        Skip.IfNot(IsAdministrator(), "Requires an elevated (administrator) test runner to register Windows services.");

        var targetServiceName = $"OctopusTimeService_Target_{Guid.NewGuid():N}";
        var ourServiceName = $"OctopusTimeService_Test_{Guid.NewGuid():N}";
        var targetInstalled = false;
        var ourInstalled = false;

        try
        {
            // Install a target service first (no deps of its own).
            Assert.Equal(0, RunExe(fixture.ExePath, ["install", "--serviceName", targetServiceName]));
            targetInstalled = true;

            // Install our service with the target as a dependent.
            Assert.Equal(0, RunExe(fixture.ExePath,
                ["install", "--serviceName", ourServiceName, "--dependent", targetServiceName]));
            ourInstalled = true;

            // SCM should now report that the target depends on our service.
            using var target = new ServiceController(targetServiceName);
            var depNames = target.ServicesDependedOn.Select(s => s.ServiceName).ToArray();
            Assert.Contains(depNames,
                n => string.Equals(n, ourServiceName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (ourInstalled)
                RunExe(fixture.ExePath, ["uninstall", "--serviceName", ourServiceName]);
            if (targetInstalled)
                RunExe(fixture.ExePath, ["uninstall", "--serviceName", targetServiceName]);
        }
    }

    [SkippableFact]
    public void Install_rejects_dependent_that_does_not_exist()
    {
        Skip.IfNot(IsAdministrator(), "Requires an elevated (administrator) test runner to register Windows services.");

        var ourServiceName = $"OctopusTimeService_Test_{Guid.NewGuid():N}";
        var bogusDep = $"DoesNotExist_{Guid.NewGuid():N}";

        var exit = RunExe(fixture.ExePath,
            ["install", "--serviceName", ourServiceName, "--dependent", bogusDep]);

        Assert.NotEqual(0, exit);
        // Pre-validation must reject before we register anything.
        Assert.False(ServiceExists(ourServiceName),
            "Our service should not have been registered when --dependent validation fails.");
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
