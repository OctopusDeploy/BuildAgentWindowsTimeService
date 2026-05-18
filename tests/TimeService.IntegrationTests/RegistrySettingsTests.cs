using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;
using Octopus.Shellfish;

namespace TimeService.IntegrationTests;

[Collection(nameof(PublishedExeCollection))]
public class RegistrySettingsTests(PublishedExeFixture fixture)
{
    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";

    [SkippableFact]
    public void Install_writes_default_NtpCheckIntervalSeconds_to_registry()
    {
        Skip.IfNot(IsAdministrator(), "Requires an elevated (administrator) test runner to register Windows services.");

        var serviceName = $"OctopusTimeService_Test_{Guid.NewGuid():N}";
        var installed = false;
        try
        {
            Assert.Equal(0, RunExe(fixture.ExePath, ["install", "--serviceName", serviceName]));
            installed = true;

            using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\{serviceName}");
            Assert.NotNull(key);
            Assert.Equal(30, key!.GetValue("NtpCheckIntervalSeconds"));
        }
        finally
        {
            if (installed) RunExe(fixture.ExePath, ["uninstall", "--serviceName", serviceName]);
        }
    }

    [SkippableFact]
    public void Install_with_ntpCheckInterval_writes_custom_value_to_registry()
    {
        Skip.IfNot(IsAdministrator(), "Requires an elevated (administrator) test runner to register Windows services.");

        var serviceName = $"OctopusTimeService_Test_{Guid.NewGuid():N}";
        var installed = false;
        try
        {
            Assert.Equal(0, RunExe(fixture.ExePath,
                ["install", "--serviceName", serviceName, "--ntpCheckInterval", "90"]));
            installed = true;

            using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\{serviceName}");
            Assert.NotNull(key);
            Assert.Equal(90, key!.GetValue("NtpCheckIntervalSeconds"));
            Assert.Equal(RegistryValueKind.DWord, key.GetValueKind("NtpCheckIntervalSeconds"));
        }
        finally
        {
            if (installed) RunExe(fixture.ExePath, ["uninstall", "--serviceName", serviceName]);
        }
    }

    [Fact]
    public void Install_rejects_non_numeric_ntpCheckInterval()
    {
        var serviceName = $"OctopusTimeService_Test_{Guid.NewGuid():N}";
        var exit = RunExe(fixture.ExePath,
            ["install", "--serviceName", serviceName, "--ntpCheckInterval", "abc"]);

        Assert.NotEqual(0, exit);
        Assert.False(ServiceExists(serviceName),
            "Service must not be registered when argument validation fails.");
    }

    [Fact]
    public void Install_rejects_non_positive_ntpCheckInterval()
    {
        var serviceName = $"OctopusTimeService_Test_{Guid.NewGuid():N}";
        var exit = RunExe(fixture.ExePath,
            ["install", "--serviceName", serviceName, "--ntpCheckInterval", "0"]);

        Assert.NotEqual(0, exit);
        Assert.False(ServiceExists(serviceName));
    }

    [SkippableFact]
    public void Install_writes_Dependents_csv_to_registry()
    {
        Skip.IfNot(IsAdministrator(), "Requires an elevated (administrator) test runner to register Windows services.");

        var targetA = $"OctopusTimeService_Target_{Guid.NewGuid():N}";
        var targetB = $"OctopusTimeService_Target_{Guid.NewGuid():N}";
        var ourService = $"OctopusTimeService_Test_{Guid.NewGuid():N}";
        var targetAInstalled = false;
        var targetBInstalled = false;
        var ourInstalled = false;

        try
        {
            Assert.Equal(0, RunExe(fixture.ExePath, ["install", "--serviceName", targetA]));
            targetAInstalled = true;
            Assert.Equal(0, RunExe(fixture.ExePath, ["install", "--serviceName", targetB]));
            targetBInstalled = true;

            Assert.Equal(0, RunExe(fixture.ExePath,
                ["install", "--serviceName", ourService,
                 "--dependent", targetA, "--dependent", targetB]));
            ourInstalled = true;

            using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\{ourService}");
            Assert.NotNull(key);
            var dependents = key!.GetValue("Dependents") as string;
            Assert.Equal($"{targetA},{targetB}", dependents);
        }
        finally
        {
            if (ourInstalled) RunExe(fixture.ExePath, ["uninstall", "--serviceName", ourService]);
            if (targetAInstalled) RunExe(fixture.ExePath, ["uninstall", "--serviceName", targetA]);
            if (targetBInstalled) RunExe(fixture.ExePath, ["uninstall", "--serviceName", targetB]);
        }
    }

    [SkippableFact]
    public void Uninstall_without_dependent_flag_recovers_dependents_from_registry()
    {
        Skip.IfNot(IsAdministrator(), "Requires an elevated (administrator) test runner to register Windows services.");

        var target = $"OctopusTimeService_Target_{Guid.NewGuid():N}";
        var ourService = $"OctopusTimeService_Test_{Guid.NewGuid():N}";
        var targetInstalled = false;
        var ourInstalled = false;

        try
        {
            Assert.Equal(0, RunExe(fixture.ExePath, ["install", "--serviceName", target]));
            targetInstalled = true;
            Assert.Equal(0, RunExe(fixture.ExePath,
                ["install", "--serviceName", ourService, "--dependent", target]));
            ourInstalled = true;

            // Precondition: target depends on us.
            using (var ctrl = new ServiceController(target))
            {
                var deps = ctrl.ServicesDependedOn.Select(s => s.ServiceName).ToArray();
                Assert.Contains(deps, n => string.Equals(n, ourService, StringComparison.OrdinalIgnoreCase));
            }

            // Uninstall WITHOUT --dependent — the installer should recover the target from
            // the Dependents registry value we wrote during install, and strip the link.
            Assert.Equal(0, RunExe(fixture.ExePath, ["uninstall", "--serviceName", ourService]));
            ourInstalled = false;

            using (var ctrl = new ServiceController(target))
            {
                var deps = ctrl.ServicesDependedOn.Select(s => s.ServiceName).ToArray();
                Assert.DoesNotContain(deps,
                    n => string.Equals(n, ourService, StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            if (ourInstalled) RunExe(fixture.ExePath, ["uninstall", "--serviceName", ourService]);
            if (targetInstalled) RunExe(fixture.ExePath, ["uninstall", "--serviceName", target]);
        }
    }

    [SkippableFact]
    public void Uninstall_removes_the_service_registry_key()
    {
        Skip.IfNot(IsAdministrator(), "Requires an elevated (administrator) test runner to register Windows services.");

        var serviceName = $"OctopusTimeService_Test_{Guid.NewGuid():N}";
        var installed = false;
        try
        {
            Assert.Equal(0, RunExe(fixture.ExePath,
                ["install", "--serviceName", serviceName, "--ntpCheckInterval", "45"]));
            installed = true;

            using (var key = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\{serviceName}"))
                Assert.NotNull(key);

            Assert.Equal(0, RunExe(fixture.ExePath, ["uninstall", "--serviceName", serviceName]));
            installed = false;

            using var afterKey = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\{serviceName}");
            Assert.Null(afterKey);
        }
        finally
        {
            if (installed) RunExe(fixture.ExePath, ["uninstall", "--serviceName", serviceName]);
        }
    }

    private static int RunExe(string exePath, string[] args) =>
        new ShellCommand(exePath)
            .WithArguments(args)
            .Execute()
            .ExitCode;

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
