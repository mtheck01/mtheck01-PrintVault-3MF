param(
    [Parameter(Mandatory=$true)][string]$Root,
    [Parameter(Mandatory=$false)][string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
# Root is supplied by build_windows.bat as an already-absolute path.
# Do not canonicalize it with GetFullPath: PowerShell/Windows path parsing can
# reject otherwise-valid batch-expanded paths and hide the real preflight result.
$Root = $Root.Trim().Trim('\"')
if ([string]::IsNullOrWhiteSpace($Root)) { throw 'Root path is empty.' }

function ReadText([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "MISSING FILE: $Path"
    }
    return [System.IO.File]::ReadAllText($Path)
}

function Check([string]$Name, [bool]$Condition, [string]$Detail) {
    if ($Condition) {
        Write-Output "[PASS] $Name"
    } else {
        Write-Output "[FAIL] $Name"
        if ($Detail) { Write-Output "       $Detail" }
        $script:Failures++
    }
}

$Failures = 0
$versionPath = Join-Path $Root 'VERSION'
$propsPath = Join-Path $Root 'Directory.Build.props'
$projectPath = Join-Path $Root 'src\PrintVault\PrintVault.csproj'
$commandsPath = Join-Path $Root 'src\PrintVault.Core\Commands.cs'
$xamlPath = Join-Path $Root 'src\PrintVault\MainWindow.xaml'
$issPath = Join-Path $Root 'installer\PrintVault_3MF.iss'
$buildPath = Join-Path $Root 'build_windows.bat'
$solutionPath = Join-Path $Root 'PrintVault.sln'

Write-Output 'VERSION PREFLIGHT'
Write-Output '================='
Write-Output "Root: $Root"
Write-Output "Expected version argument: $ExpectedVersion"
Write-Output ''

try {
    $version = (ReadText $versionPath).Trim()
    Check 'VERSION file exists and is valid' ($version -match '^\d+\.\d+\.\d+$') "Found: '$version'"
    if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
        $ExpectedVersion = $version
        Write-Output "Expected version source: VERSION file ($ExpectedVersion)"
    } else {
        Check 'VERSION matches requested release' ($version -eq $ExpectedVersion) "Expected: $ExpectedVersion | Found: $version"
    }

    $props = ReadText $propsPath
    Check 'Directory.Build.props uses authoritative VERSION' ($props -match 'PrintVaultReleaseVersion' -and $props -match 'MSBuildThisFileDirectory.*VERSION') 'Expected a VERSION-derived PrintVaultReleaseVersion.'
    Check 'Directory.Build.props propagates Version' ($props -match '<Version>\$\(PrintVaultReleaseVersion\)</Version>') 'Missing Version propagation.'
    Check 'Directory.Build.props propagates AssemblyVersion' ($props -match '<AssemblyVersion>\$\(PrintVaultReleaseVersion\)</AssemblyVersion>') 'Missing AssemblyVersion propagation.'
    Check 'Directory.Build.props propagates FileVersion' ($props -match '<FileVersion>\$\(PrintVaultReleaseVersion\)</FileVersion>') 'Missing FileVersion propagation.'

    $project = ReadText $projectPath
    Check 'Application project has no hard-coded Version' ($project -notmatch '<Version>') 'Remove hard-coded project Version; it must inherit Directory.Build.props.'

    $commands = ReadText $commandsPath
    Check 'AppVersion derives from assembly metadata' ($commands -match 'Assembly.*GetName\(\)\.Version') 'AppVersion must derive from the compiled assembly version.'

    $xaml = ReadText $xamlPath
    Check 'Main window displays AppVersion' ($xaml -match 'core:AppVersion\.Version') 'MainWindow.xaml is not using AppVersion.'

    $iss = ReadText $issPath
    $build = ReadText $buildPath
    Check 'Installer template consumes MyAppVersion' ($iss -match 'AppVersion=\{#MyAppVersion\}') 'Installer must use the MyAppVersion preprocessor value.'
    Check 'Build script passes MyAppVersion to Inno Setup' ($build -match '/DMyAppVersion=%VERSION%') 'build_windows.bat must pass /DMyAppVersion=%VERSION% to ISCC.'

    Check 'Solution exists' (Test-Path -LiteralPath $solutionPath -PathType Leaf) 'PrintVault.sln is missing.'

    $uiRoot = Join-Path $Root 'src\PrintVault'
    $uiFiles = @(Get-ChildItem -LiteralPath $uiRoot -File | Where-Object { $_.Extension -in @('.xaml', '.cs') })
    foreach ($uiFile in $uiFiles) {
        $uiText = ReadText $uiFile.FullName
        $hasStaleUiVersion = $uiText -match 'BUILD:\s*\d+\.\d+\.\d+|PrintVault 3MF\s+\d+\.\d+\.\d+|Build\s+\d+\.\d+\.\d+'
        Check "No stale user-facing version: $($uiFile.Name)" (-not $hasStaleUiVersion) 'User-facing UI text must not embed a historical challenge version.'
    }

    $challengeRoot = Join-Path $Root 'src\PrintVault.Infrastructure'
    $challengeServices = Get-ChildItem -LiteralPath $challengeRoot -Filter '*ChallengeService.cs' -File
    Check 'Challenge services discovered' ($challengeServices.Count -gt 0) 'No *ChallengeService.cs files were found.'
    foreach ($service in $challengeServices) {
        $challengeText = ReadText $service.FullName
        $hasDynamicVersion = $challengeText -match 'AppVersion\.Version'
        Check "Challenge service uses authoritative runtime version: $($service.Name)" $hasDynamicVersion 'Challenge reports must derive BUILD/title version from AppVersion.Version.'
        $hasHardCodedBuild = $challengeText -match 'BUILD:\s*\d+\.\d+\.\d+'
        Check "No hard-coded BUILD version: $($service.Name)" (-not $hasHardCodedBuild) 'Remove hard-coded BUILD: x.y.z strings.'
        $hasHardCodedTitle = $challengeText -match 'PrintVault 3MF\s+\d+\.\d+\.\d+'
        Check "No hard-coded report title version: $($service.Name)" (-not $hasHardCodedTitle) 'Remove hard-coded PrintVault 3MF x.y.z report titles.'
    }

    $nested = Join-Path $Root (Split-Path $Root -Leaf)
    Check 'No nested duplicate source root' (-not (Test-Path -LiteralPath $nested -PathType Container)) "Unexpected nested source root: $nested"
} catch {
    Write-Output "[FAIL] PREFLIGHT EXCEPTION"
    Write-Output "       $($_.Exception.Message)"
    $Failures++
}

Write-Output ''
if ($Failures -eq 0) {
    Write-Output "RESULT: PASS ($ExpectedVersion)"
    exit 0
}

Write-Output "RESULT: FAIL ($Failures check(s))"
exit 1
