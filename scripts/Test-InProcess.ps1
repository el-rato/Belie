param(
    [ValidateSet('All', 'Stress', 'Regression')]
    [string]$Suite = 'All',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:VSTEST_TELEMETRY_OPTEDIN = '0'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testProject = Join-Path $projectRoot 'WallpaperProfiles.Tests/WallpaperProfiles.Tests.csproj'
if (-not $NoBuild) {
    dotnet build $testProject -c Release --no-restore --verbosity quiet -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# xUnit executes in this process, avoiding VSTest's localhost socket requirement.
# Keep generated runner sources under obj so neither app nor tests compile them.
$runnerDirectory = Join-Path $projectRoot 'WallpaperProfiles.Tests/obj/inprocess-runner'
$testDirectory = Join-Path $projectRoot 'WallpaperProfiles.Tests/bin/Release/net8.0-windows10.0.19041.0'
$resultsDirectory = Join-Path $projectRoot 'TestResults/cleanup'
New-Item -ItemType Directory -Path $runnerDirectory, $resultsDirectory -Force | Out-Null
$temporaryDirectory = Join-Path $resultsDirectory 'tmp'
New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
$escapedDirectory = [System.Security.SecurityElement]::Escape($testDirectory)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="xunit.runner.utility.netcoreapp10"><HintPath>$escapedDirectory/xunit.runner.utility.netcoreapp10.dll</HintPath></Reference>
    <Reference Include="xunit.abstractions"><HintPath>$escapedDirectory/xunit.abstractions.dll</HintPath></Reference>
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $runnerDirectory 'Runner.csproj') -Encoding utf8

@'
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Xunit.Runners;

var assemblyPath = Path.GetFullPath(args[0]);
var suite = args[1];
var resultPath = args[2];
var directory = Path.GetDirectoryName(assemblyPath)!;
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(directory, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
using var finished = new ManualResetEventSlim();
using var runner = AssemblyRunner.WithoutAppDomain(assemblyPath);
var results = new ConcurrentQueue<object>();
var errors = new ConcurrentQueue<string>();
var exitCode = 1;
runner.TestCaseFilter = test => suite == "All"
    || (test.Traits.TryGetValue("Category", out var values) && values.Contains("Stress")) == (suite == "Stress");
runner.OnDiscoveryComplete = info => Console.WriteLine($"Discovered {info.TestCasesToRun} tests ({suite}).");
runner.OnTestPassed = info =>
{
    Console.WriteLine($"PASS {info.TestDisplayName} ({info.ExecutionTime:N3}s)");
    if (!string.IsNullOrWhiteSpace(info.Output)) Console.Write(info.Output);
    results.Enqueue(new { Name = info.TestDisplayName, Status = "Passed", Seconds = info.ExecutionTime, info.Output });
};
runner.OnTestFailed = info =>
{
    Console.WriteLine($"FAIL {info.TestDisplayName}: {info.ExceptionMessage}\n{info.ExceptionStackTrace}");
    results.Enqueue(new { Name = info.TestDisplayName, Status = "Failed", Seconds = info.ExecutionTime,
        info.Output, Error = info.ExceptionMessage, Stack = info.ExceptionStackTrace });
};
runner.OnTestSkipped = info =>
{
    Console.WriteLine($"SKIP {info.TestDisplayName}: {info.SkipReason}");
    results.Enqueue(new { Name = info.TestDisplayName, Status = "Skipped", Reason = info.SkipReason });
};
runner.OnErrorMessage = info =>
{
    errors.Enqueue(info.ExceptionMessage);
    Console.WriteLine($"RUNNER ERROR: {info.ExceptionMessage}");
};
runner.OnExecutionComplete = info =>
{
    Console.WriteLine($"Total: {info.TotalTests}; Failed: {info.TestsFailed}; Skipped: {info.TestsSkipped}; Time: {info.ExecutionTime:N3}s");
    File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Suite = suite, info.TotalTests,
        info.TestsFailed, info.TestsSkipped, info.ExecutionTime, Errors = errors.ToArray(), Tests = results.ToArray() },
        new JsonSerializerOptions { WriteIndented = true }));
    exitCode = info.TotalTests > 0 && info.TestsFailed == 0 && errors.IsEmpty ? 0 : 1;
    finished.Set();
};
runner.Start(new AssemblyRunnerStartOptions { Parallel = false, MaxParallelThreads = 1 });
if (!finished.Wait(TimeSpan.FromMinutes(10)))
{
    Console.Error.WriteLine("Test run exceeded ten minutes.");
    // Test runner disposal may wait on a stuck test, so terminate with a failing exit code.
    Environment.Exit(1);
}
// ExecutionComplete is raised before the runner transitions to Idle.
while (runner.Status != AssemblyRunnerStatus.Idle) Thread.Sleep(10);
return exitCode;
'@ | Set-Content -LiteralPath (Join-Path $runnerDirectory 'Program.cs') -Encoding utf8

dotnet build (Join-Path $runnerDirectory 'Runner.csproj') -c Release --verbosity quiet --ignore-failed-sources -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
try {
    # Some restricted Windows hosts cannot rename files in their redirected system temp folder.
    $env:TEMP = $temporaryDirectory
    $env:TMP = $temporaryDirectory
    Push-Location $resultsDirectory
    dotnet (Join-Path $runnerDirectory 'bin/Release/net8.0-windows/Runner.dll') `
        (Join-Path $testDirectory 'WallpaperProfiles.Tests.dll') $Suite (Join-Path $resultsDirectory "$Suite.json")
    $resultCode = $LASTEXITCODE
}
finally {
    Pop-Location
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}
exit $resultCode
