using System.Diagnostics;
using Xunit;

namespace AISwarmX.Dashboard.Api.Tests;

public sealed class StartDashboardScriptTests
{
    [Fact]
    public void StartDashboard_MigratesAndUnregistersLegacyScheduledTaskSafelyAndScopesLegacyProcessStop()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fx = new TempDashboardScriptSandbox();
        var pwsh = ResolvePwshPath();
        if (pwsh is null)
        {
            return;
        }

        // Scenario 1: -RegisterAutoStart migrates legacy MOBAdev-Dashboard-AutoStart -> AISwarmX-Dashboard-AutoStart (treating ObjectNotFound as absent)
        var regResult = RunMockedScript(
            pwsh,
            fx.ScriptPath,
            """
            $tasks = @{ 'MOBAdev-Dashboard-AutoStart' = @{ TaskName = 'MOBAdev-Dashboard-AutoStart' } }
            $log = New-Object System.Collections.Generic.List[string]
            function Get-ScheduledTask {
              [CmdletBinding()] param([string]$TaskName)
              if ($tasks.ContainsKey($TaskName)) { return $tasks[$TaskName] }
              $err = [System.Management.Automation.ErrorRecord]::new([System.IO.FileNotFoundException]::new('NotFound'), 'CmdletizationQuery_NotFound_TaskName', [System.Management.Automation.ErrorCategory]::ObjectNotFound, $TaskName)
              $PSCmdlet.ThrowTerminatingError($err)
            }
            function New-ScheduledTaskAction { param([string]$Execute, [string]$Argument, [string]$WorkingDirectory) return @{ Execute = $Execute } }
            function New-ScheduledTaskTrigger { param([switch]$AtLogOn, [string]$User) return @{ AtLogOn = $true } }
            function New-ScheduledTaskSettingsSet { param([switch]$AllowStartIfOnBatteries, [switch]$DontStopIfGoingOnBatteries, [switch]$StartWhenAvailable, $ExecutionTimeLimit, [int]$RestartCount, $RestartInterval) return @{} }
            function Register-ScheduledTask { [CmdletBinding()] param([string]$TaskName, $Action, $Trigger, $Settings, [string]$Description, [switch]$Force) $tasks[$TaskName] = $Action; $log.Add("REG:$TaskName::$($Action.Execute)") }
            function Unregister-ScheduledTask { [CmdletBinding()] param([string]$TaskName, [switch]$Confirm) [void]$tasks.Remove($TaskName); $log.Add("UNREG:$TaskName") }
            & $env:TARGET_SCRIPT -RegisterAutoStart
            Write-Output ($log -join '|')
            Write-Output ('KEYS:' + (($tasks.Keys | Sort-Object) -join ','))
            """);

        Assert.Equal(0, regResult.ExitCode);
        Assert.Contains("REG:AISwarmX-Dashboard-AutoStart::", regResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("AISwarmX.Dashboard.Api.exe", regResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("UNREG:MOBAdev-Dashboard-AutoStart", regResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("KEYS:AISwarmX-Dashboard-AutoStart", regResult.StdOut, StringComparison.Ordinal);

        // Scenario 2: -UnregisterAutoStart cleans up BOTH legacy and current tasks
        var unregResult = RunMockedScript(
            pwsh,
            fx.ScriptPath,
            """
            $tasks = @{
              'AISwarmX-Dashboard-AutoStart' = @{ TaskName = 'AISwarmX-Dashboard-AutoStart' }
              'MOBAdev-Dashboard-AutoStart'  = @{ TaskName = 'MOBAdev-Dashboard-AutoStart' }
            }
            $log = New-Object System.Collections.Generic.List[string]
            function Get-ScheduledTask { [CmdletBinding()] param([string]$TaskName) if ($tasks.ContainsKey($TaskName)) { return $tasks[$TaskName] }; return $null }
            function Unregister-ScheduledTask { [CmdletBinding()] param([string]$TaskName, [switch]$Confirm) [void]$tasks.Remove($TaskName); $log.Add("UNREG:$TaskName") }
            & $env:TARGET_SCRIPT -UnregisterAutoStart
            Write-Output ($log -join '|')
            Write-Output ('COUNT:' + $tasks.Count)
            """);

        Assert.Equal(0, unregResult.ExitCode);
        Assert.Contains("UNREG:AISwarmX-Dashboard-AutoStart", unregResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("UNREG:MOBAdev-Dashboard-AutoStart", unregResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("COUNT:0", unregResult.StdOut, StringComparison.Ordinal);

        // Scenario 3: -Background stops ONLY verified port + exact directory-boundary legacy process (wrong port 101, wrong checkout 102, prefix-sharing MobaDev.Dashboard.Api-backup 103 survive)
        var siblingBackupExe = fx.SiblingBackupExePath;
        var bgResult = RunMockedScript(
            pwsh,
            fx.ScriptPath,
            $$"""
            $tasks = @{ 'MOBAdev-Dashboard-AutoStart' = @{ TaskName = 'MOBAdev-Dashboard-AutoStart' } }
            $log = New-Object System.Collections.Generic.List[string]
            $script:startedNew = $false
            function Get-ScheduledTask { [CmdletBinding()] param([string]$TaskName) if ($tasks.ContainsKey($TaskName)) { return $tasks[$TaskName] }; return $null }
            function New-ScheduledTaskAction { param([string]$Execute, [string]$Argument, [string]$WorkingDirectory) return @{ Execute = $Execute } }
            function New-ScheduledTaskTrigger { param([switch]$AtLogOn, [string]$User) return @{} }
            function New-ScheduledTaskSettingsSet { param([switch]$AllowStartIfOnBatteries, [switch]$DontStopIfGoingOnBatteries, [switch]$StartWhenAvailable, $ExecutionTimeLimit, [int]$RestartCount, $RestartInterval) return @{} }
            function Register-ScheduledTask { [CmdletBinding()] param([string]$TaskName, $Action, $Trigger, $Settings, [string]$Description, [switch]$Force) $tasks[$TaskName] = $Action; $log.Add("REG:$TaskName") }
            function Unregister-ScheduledTask { [CmdletBinding()] param([string]$TaskName, [switch]$Confirm) [void]$tasks.Remove($TaskName); $log.Add("UNREG:$TaskName") }
            function Start-ScheduledTask { param([string]$TaskName) $script:startedNew = $true; $log.Add("START:$TaskName") }
            function Get-Process { param([string]$Name)
              if ($Name -eq 'MobaDev.Dashboard.Api') {
                return @(
                  [pscustomobject]@{ Id = 101; Path = '{{fx.LegacyExePath}}' },
                  [pscustomobject]@{ Id = 102; Path = 'C:\OtherCheckout\dashboard\src\MobaDev.Dashboard.Api\bin\Release\net10.0\MobaDev.Dashboard.Api.exe' },
                  [pscustomobject]@{ Id = 103; Path = '{{siblingBackupExe}}' },
                  [pscustomobject]@{ Id = 999; Path = '{{fx.LegacyExePath}}' }
                )
              }
              return @()
            }
            function Get-NetTCPConnection { param([int]$LocalPort, [string]$State)
              if ($LocalPort -eq 5179) {
                return @([pscustomobject]@{ OwningProcess = 999 }, [pscustomobject]@{ OwningProcess = 102 }, [pscustomobject]@{ OwningProcess = 103 })
              }
              return @([pscustomobject]@{ OwningProcess = 101 })
            }
            function Stop-Process { [CmdletBinding()] param([int]$Id, [switch]$Force) $log.Add("STOP:$Id") }
            function Invoke-WebRequest { param([string]$Uri, [switch]$UseBasicParsing, [int]$TimeoutSec)
              if ($script:startedNew) { return [pscustomobject]@{ StatusCode = 200 } }
              throw 'not ready until new task started'
            }
            & $env:TARGET_SCRIPT -Background
            Write-Output ($log -join '|')
            Write-Output ('KEYS:' + (($tasks.Keys | Sort-Object) -join ','))
            """);

        Assert.Equal(0, bgResult.ExitCode);
        Assert.Contains("REG:AISwarmX-Dashboard-AutoStart", bgResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("UNREG:MOBAdev-Dashboard-AutoStart", bgResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("STOP:999", bgResult.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("STOP:101", bgResult.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("STOP:102", bgResult.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("STOP:103", bgResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("START:AISwarmX-Dashboard-AutoStart", bgResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("KEYS:AISwarmX-Dashboard-AutoStart", bgResult.StdOut, StringComparison.Ordinal);

        // Scenario 4: Lookup error (access denied on Get-ScheduledTask) propagates loudly without registering colliding tasks
        var lookupDeniedResult = RunMockedScript(
            pwsh,
            fx.ScriptPath,
            """
            $tasks = @{ 'MOBAdev-Dashboard-AutoStart' = @{ TaskName = 'MOBAdev-Dashboard-AutoStart' } }
            function Get-ScheduledTask { [CmdletBinding()] param([string]$TaskName) throw [UnauthorizedAccessException]::new('Lookup access denied.') }
            function New-ScheduledTaskAction { param([string]$Execute, [string]$Argument, [string]$WorkingDirectory) return @{ Execute = $Execute } }
            function New-ScheduledTaskTrigger { param([switch]$AtLogOn, [string]$User) return @{} }
            function New-ScheduledTaskSettingsSet { param([switch]$AllowStartIfOnBatteries, [switch]$DontStopIfGoingOnBatteries, [switch]$StartWhenAvailable, $ExecutionTimeLimit, [int]$RestartCount, $RestartInterval) return @{} }
            function Register-ScheduledTask { [CmdletBinding()] param([string]$TaskName, $Action, $Trigger, $Settings, [string]$Description, [switch]$Force) $tasks[$TaskName] = $Action }
            try {
              & $env:TARGET_SCRIPT -RegisterAutoStart
              Write-Output 'UNEXPECTED_SUCCESS'
            }
            catch {
              Write-Output ('CAUGHT:' + $_.Exception.Message)
            }
            Write-Output ('KEYS:' + (($tasks.Keys | Sort-Object) -join ','))
            """);

        Assert.DoesNotContain("UNEXPECTED_SUCCESS", lookupDeniedResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("CAUGHT:Lookup access denied.", lookupDeniedResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("KEYS:MOBAdev-Dashboard-AutoStart", lookupDeniedResult.StdOut, StringComparison.Ordinal);

        // Scenario 5: Post-unregister verification failure (task remains present silently) fails loudly and rolls back new task
        var verifyFailResult = RunMockedScript(
            pwsh,
            fx.ScriptPath,
            """
            $tasks = @{ 'MOBAdev-Dashboard-AutoStart' = @{ TaskName = 'MOBAdev-Dashboard-AutoStart' } }
            function Get-ScheduledTask { [CmdletBinding()] param([string]$TaskName) if ($tasks.ContainsKey($TaskName)) { return $tasks[$TaskName] }; return $null }
            function New-ScheduledTaskAction { param([string]$Execute, [string]$Argument, [string]$WorkingDirectory) return @{ Execute = $Execute } }
            function New-ScheduledTaskTrigger { param([switch]$AtLogOn, [string]$User) return @{} }
            function New-ScheduledTaskSettingsSet { param([switch]$AllowStartIfOnBatteries, [switch]$DontStopIfGoingOnBatteries, [switch]$StartWhenAvailable, $ExecutionTimeLimit, [int]$RestartCount, $RestartInterval) return @{} }
            function Register-ScheduledTask { [CmdletBinding()] param([string]$TaskName, $Action, $Trigger, $Settings, [string]$Description, [switch]$Force) $tasks[$TaskName] = $Action }
            function Unregister-ScheduledTask { [CmdletBinding()] param([string]$TaskName, [switch]$Confirm)
              if ($TaskName -eq 'AISwarmX-Dashboard-AutoStart') { [void]$tasks.Remove($TaskName) }
              # Stubbornly leave MOBAdev-Dashboard-AutoStart present on silent failure
            }
            try {
              & $env:TARGET_SCRIPT -RegisterAutoStart
              Write-Output 'UNEXPECTED_SUCCESS'
            }
            catch {
              Write-Output ('CAUGHT:' + $_.Exception.Message)
            }
            Write-Output ('KEYS:' + (($tasks.Keys | Sort-Object) -join ','))
            """);

        Assert.DoesNotContain("UNEXPECTED_SUCCESS", verifyFailResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("still exists after Unregister-ScheduledTask", verifyFailResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("KEYS:MOBAdev-Dashboard-AutoStart", verifyFailResult.StdOut, StringComparison.Ordinal);

        // Scenario 6: Rollback failure surfaces AggregateException with BOTH legacy removal error and rollback error
        var rollbackFailResult = RunMockedScript(
            pwsh,
            fx.ScriptPath,
            """
            $tasks = @{ 'MOBAdev-Dashboard-AutoStart' = @{ TaskName = 'MOBAdev-Dashboard-AutoStart' } }
            function Get-ScheduledTask { [CmdletBinding()] param([string]$TaskName) if ($tasks.ContainsKey($TaskName)) { return $tasks[$TaskName] }; return $null }
            function New-ScheduledTaskAction { param([string]$Execute, [string]$Argument, [string]$WorkingDirectory) return @{ Execute = $Execute } }
            function New-ScheduledTaskTrigger { param([switch]$AtLogOn, [string]$User) return @{} }
            function New-ScheduledTaskSettingsSet { param([switch]$AllowStartIfOnBatteries, [switch]$DontStopIfGoingOnBatteries, [switch]$StartWhenAvailable, $ExecutionTimeLimit, [int]$RestartCount, $RestartInterval) return @{} }
            function Register-ScheduledTask { [CmdletBinding()] param([string]$TaskName, $Action, $Trigger, $Settings, [string]$Description, [switch]$Force) $tasks[$TaskName] = $Action }
            function Unregister-ScheduledTask { [CmdletBinding()] param([string]$TaskName, [switch]$Confirm) throw [UnauthorizedAccessException]::new("Access denied on $TaskName") }
            try {
              & $env:TARGET_SCRIPT -RegisterAutoStart
              Write-Output 'UNEXPECTED_SUCCESS'
            }
            catch {
              Write-Output ('CAUGHT:' + $_.Exception.Message)
            }
            """);

        Assert.DoesNotContain("UNEXPECTED_SUCCESS", rollbackFailResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("Access denied on MOBAdev-Dashboard-AutoStart", rollbackFailResult.StdOut, StringComparison.Ordinal);
        Assert.Contains("AND failed to roll back 'AISwarmX-Dashboard-AutoStart'", rollbackFailResult.StdOut, StringComparison.Ordinal);
    }

    private static string? ResolvePwshPath()
    {
        foreach (var candidate in new[] { "pwsh.exe", "pwsh" })
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(candidate, "-NoProfile -Command \"exit 0\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
                if (probe is not null && probe.WaitForExit(5000) && probe.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch
            {
                // Continue probing.
            }
        }

        return null;
    }

    private static (int ExitCode, string StdOut) RunMockedScript(string pwsh, string scriptPath, string harnessBody)
    {
        var tempHarness = Path.Combine(Path.GetDirectoryName(scriptPath)!, $"harness-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(tempHarness, harnessBody);
        try
        {
            var psi = new ProcessStartInfo(pwsh)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(tempHarness);
            psi.Environment["TARGET_SCRIPT"] = scriptPath;

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start pwsh.");
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            Assert.True(proc.WaitForExit(15_000), $"pwsh timed out: {stderr}");
            return (proc.ExitCode, stdout + "\n" + stderr);
        }
        finally
        {
            File.Delete(tempHarness);
        }
    }

    private sealed class TempDashboardScriptSandbox : IDisposable
    {
        private readonly string _rootPath;
        public string ScriptPath { get; }
        public string LegacyExePath { get; }
        public string SiblingBackupExePath { get; }

        public TempDashboardScriptSandbox()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), $"aiswarmx-script-test-{Guid.NewGuid():N}");
            var repoRoot = FindRepoRoot();
            ScriptPath = Path.Combine(_rootPath, "Start-Dashboard.ps1");
            Directory.CreateDirectory(_rootPath);
            File.Copy(Path.Combine(repoRoot, "dashboard", "Start-Dashboard.ps1"), ScriptPath, overwrite: true);

            var apiDir = Path.Combine(_rootPath, "src", "AISwarmX.Dashboard.Api");
            var exeDir = Path.Combine(apiDir, "bin", "Release", "net10.0");
            var distDir = Path.Combine(_rootPath, "src", "AISwarmX.Dashboard.Web", "dist");
            Directory.CreateDirectory(exeDir);
            Directory.CreateDirectory(distDir);
            File.WriteAllText(Path.Combine(apiDir, "AISwarmX.Dashboard.Api.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(exeDir, "AISwarmX.Dashboard.Api.exe"), string.Empty);
            File.WriteAllText(Path.Combine(distDir, "index.html"), "<html></html>");
            LegacyExePath = Path.Combine(_rootPath, "src", "MobaDev.Dashboard.Api", "bin", "Release", "net10.0", "MobaDev.Dashboard.Api.exe");
            SiblingBackupExePath = Path.Combine(_rootPath, "src", "MobaDev.Dashboard.Api-backup", "bin", "Release", "net10.0", "MobaDev.Dashboard.Api.exe");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dashboard", "AISwarmX.Dashboard.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repo root.");
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
    }
}
