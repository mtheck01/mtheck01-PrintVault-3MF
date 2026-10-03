param(
    [Parameter(Mandatory=$true)][string]$Root,
    [Parameter(Mandatory=$true)][string]$DistributionToken,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$SourceSha
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$distributionRepo = "mtheck01/PrintVault-Updates"
$rootPath = (Resolve-Path $Root).Path
$zipName = "PrintVault_3MF_v${Version}_PATCH.zip"
$zipPath = Join-Path $rootPath $zipName
$packageRoot = Join-Path $rootPath "PrintVault_3MF_v${Version}_PATCH"
$installer = Join-Path $rootPath "dist\installer\PrintVault_3MF_v${Version}_Setup.exe"
$verifyRoot = Join-Path $rootPath "_release_verify"
$publishedPath = Join-Path $rootPath "_published_$zipName"

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Invalid release version: $Version" }
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Tested installer artifact is missing: $installer" }

$headers = @{
    Authorization = "Bearer $DistributionToken"
    Accept = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2026-03-10"
    "User-Agent" = "PrintVault-Release-Automation"
}
Write-Host "=== PRINTVAULT PRODUCTION RELEASE ==="
Write-Host "Version: $Version"
Write-Host "Source SHA: $SourceSha"

$releaseApi = "https://api.github.com/repos/$distributionRepo/releases/tags/v$Version"
$release = $null
try {
    $release = Invoke-RestMethod -Uri $releaseApi -Headers $headers -Method Get
} catch {
    $response = $_.Exception.Response
    $status = if ($null -ne $response) { [int]$response.StatusCode } else { 0 }
    if ($status -ne 404) { throw }
}

if ($null -eq $release) {
    $releaseBody = @{
        tag_name = "v$Version"
        target_commitish = "main"
        name = "PrintVault 3MF v$Version"
        body = "Automated PrintVault 3MF release. Source commit: $SourceSha. Package built and validated by the canonical Windows workflow."
    } | ConvertTo-Json -Depth 5
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$distributionRepo/releases" -Headers $headers -Method Post -Body $releaseBody -ContentType "application/json"
    Write-Host "RELEASE_CREATED=PASS"
} else {
    Write-Host "RELEASE_EXISTS=PASS ID=$($release.id)"
}

$manifestApi = "https://api.github.com/repos/$distributionRepo/contents/manifest.json"
$current = Invoke-RestMethod -Uri "${manifestApi}?ref=main" -Headers $headers -Method Get
$manifestText = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($current.content -replace '\s','')))
$currentManifest = $manifestText | ConvertFrom-Json
if ($currentManifest.version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Existing manifest version is invalid: $($currentManifest.version)" }
if ([version]$currentManifest.version -ge [version]$Version) { throw "Manifest promotion rejected. Existing=$($currentManifest.version), Candidate=$Version" }

Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $verifyRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $publishedPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot "dist\installer") | Out-Null
Copy-Item -LiteralPath $installer -Destination (Join-Path $packageRoot "dist\installer\PrintVault_3MF_v${Version}_Setup.exe") -Force
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal

$localSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToUpperInvariant()
Expand-Archive -LiteralPath $zipPath -DestinationPath $verifyRoot -Force
$expectedInstaller = Join-Path $verifyRoot "PrintVault_3MF_v${Version}_PATCH\dist\installer\PrintVault_3MF_v${Version}_Setup.exe"
if (-not (Test-Path -LiteralPath $expectedInstaller -PathType Leaf)) { throw "Package structure validation failed: $expectedInstaller" }
Write-Host "LOCAL_PACKAGE_SHA256=$localSha"
Write-Host "PACKAGE_STRUCTURE=PASS"

$asset = @($release.assets | Where-Object { $_.name -eq $zipName })
if ($asset.Count -eq 0) {
    $uploadUrl = "https://uploads.github.com/repos/$distributionRepo/releases/$($release.id)/assets?name=$zipName"
    $uploadHeaders = @{
        Authorization = "Bearer $DistributionToken"
        Accept = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2026-03-10"
        "User-Agent" = "PrintVault-Release-Automation"
    }
    Invoke-RestMethod -Uri $uploadUrl -Headers $uploadHeaders -Method Post -InFile $zipPath -ContentType "application/zip" | Out-Null
    Write-Host "RELEASE_ASSET_UPLOAD=PASS"
} elseif ($asset.Count -eq 1) {
    $existingAssetPath = Join-Path $rootPath "_existing_$zipName"
    Invoke-WebRequest -Uri $asset[0].browser_download_url -Headers @{ "User-Agent" = "PrintVault-Release-Verification" } -OutFile $existingAssetPath
    $existingSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $existingAssetPath).Hash.ToUpperInvariant()
    Remove-Item -LiteralPath $existingAssetPath -Force -ErrorAction SilentlyContinue
    if ($existingSha -eq $localSha) {
        Write-Host "RELEASE_ASSET_REUSED=PASS"
    } else {
        Write-Host "RELEASE_ASSET_REPLACEMENT=REQUIRED EXISTING=$existingSha LOCAL=$localSha"
        Invoke-RestMethod -Uri "https://api.github.com/repos/$distributionRepo/releases/assets/$($asset[0].id)" -Headers $headers -Method Delete | Out-Null
        $uploadUrl = "https://uploads.github.com/repos/$distributionRepo/releases/$($release.id)/assets?name=$zipName"
        Invoke-RestMethod -Uri $uploadUrl -Headers $uploadHeaders -Method Post -InFile $zipPath -ContentType "application/zip" | Out-Null
        Write-Host "RELEASE_ASSET_REPLACEMENT=PASS"
    }
} else {
    throw "Multiple release assets named $zipName were found."
}

$release = Invoke-RestMethod -Uri $releaseApi -Headers $headers -Method Get
$asset = @($release.assets | Where-Object { $_.name -eq $zipName })
if ($asset.Count -ne 1) { throw "Expected exactly one release asset named $zipName; found $($asset.Count)." }
Invoke-WebRequest -Uri $asset[0].browser_download_url -Headers @{ "User-Agent" = "PrintVault-Release-Verification" } -OutFile $publishedPath
$remoteSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $publishedPath).Hash.ToUpperInvariant()
if ($remoteSha -ne $localSha) { throw "Published package SHA-256 mismatch. Local=$localSha Remote=$remoteSha" }
Write-Host "PUBLISHED_RELEASE=PASS"
Write-Host "PUBLISHED_SHA256=$remoteSha"

$releaseUrl = "https://github.com/$distributionRepo/releases/download/v$Version/$zipName"
$newManifest = [ordered]@{
    version = $Version
    packageUrl = $releaseUrl
    sha256 = $localSha
    installerRelativePath = "PrintVault_3MF_v${Version}_PATCH/dist/installer/PrintVault_3MF_v${Version}_Setup.exe"
    installerArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"
}
$json = $newManifest | ConvertTo-Json -Depth 5
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json + [Environment]::NewLine)
$base64 = [Convert]::ToBase64String($bytes)
$body = @{
    message = "Promote RemotePatch manifest to $Version"
    content = $base64
    sha = $current.sha
    branch = "main"
} | ConvertTo-Json
Invoke-RestMethod -Uri $manifestApi -Headers $headers -Method Put -Body $body -ContentType "application/json"
Write-Host "MANIFEST_PROMOTION=PASS"

$finalResponse = Invoke-RestMethod -Uri "${manifestApi}?ref=main" -Headers $headers -Method Get
$finalText = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($finalResponse.content -replace '\s','')))
$finalManifest = $finalText | ConvertFrom-Json
if ($finalManifest.version -ne $Version) { throw "Final manifest version mismatch." }
if ($finalManifest.sha256 -ne $localSha) { throw "Final manifest SHA-256 mismatch." }
if ($finalManifest.packageUrl -ne $releaseUrl) { throw "Final manifest package URL mismatch." }
Write-Host "FINAL_MANIFEST_VERIFICATION=PASS"
Write-Host "REMOTE_PATCH_READY=PASS"
Write-Host "PRODUCTION_RELEASE=PASS"
