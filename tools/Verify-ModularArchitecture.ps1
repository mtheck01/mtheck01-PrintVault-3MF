$ErrorActionPreference = "Stop"

$pipeline = Get-Content "src/PrintVault.Infrastructure/ModelIntelligencePipeline.cs" -Raw
$engine = Get-Content "src/PrintVault.Infrastructure/LibraryEngine.cs" -Raw
$discovery = Get-Content "src/PrintVault.Infrastructure/LibraryFileDiscovery.cs" -Raw
$duplicateGroups = Get-Content "src/PrintVault.Infrastructure/DuplicateGroupBuilder.cs" -Raw

$mainWindow = Get-Content "src/PrintVault/MainWindow.xaml.cs" -Raw

$requiredPipeline = @(
    "new LanguageNormalizationStage()",
    "new EntityRecognitionStage()",
    "new SemanticFusionStage()",
    "new EvidenceArbitrationStage()"
)
foreach ($token in $requiredPipeline) {
    if ($pipeline -notmatch [regex]::Escape($token)) {
        throw "Modular pipeline contract missing: $token"
    }
}

# Production and forensic analysis must share the same canonical stage graph.
# There is no separate production classification mutation stage: category mutation
# belongs exclusively to EvidenceArbitrationStage.
if ($pipeline -notmatch "foreach \(var stage in stages\)") {
    throw "Canonical stage graph is not iterated by the intelligence pipeline."
}
if ($pipeline -match "new LanguageNormalizationStage\(\)\.Execute\(context\)") {
    throw "Forensic analysis contains a direct stage instantiation and bypasses the canonical stage graph."
}
if ($pipeline -match "new EntityRecognitionStage\(\)\.Execute\(context\)") {
    throw "Forensic analysis contains a direct stage instantiation and bypasses the canonical stage graph."
}
if ($pipeline -match "new SemanticFusionStage\(\)\.Execute\(context\)") {
    throw "Forensic analysis contains a direct stage instantiation and bypasses the canonical stage graph."
}
if ($pipeline -match "new EvidenceArbitrationStage\(\)\.Execute\(context\)") {
    throw "Forensic analysis contains a direct stage instantiation and bypasses the canonical stage graph."
}

if ($pipeline -match "EntityClassificationService|EntityClassificationStage") {
    throw "Legacy entity classification implementation remains inside the canonical pipeline."
}
if (Test-Path "src/PrintVault.Infrastructure/EntityClassificationService.cs") {
    throw "Legacy EntityClassificationService must be retired after modular cutover."
}
if (Test-Path "src/PrintVault.Infrastructure/Intelligence/EntityClassificationStage.cs") {
    throw "Legacy EntityClassificationStage must be retired after modular cutover."
}
if ($engine -match "entityClassification\.Apply\(") {
    throw "Legacy EntityClassificationService bypass remains in LibraryEngine."
}
if ($engine -match "ApplyHighConfidenceFusion\(") {
    throw "Legacy ApplyHighConfidenceFusion bypass remains in LibraryEngine."
}

$applyCount = ([regex]::Matches($engine, "intelligencePipeline\.Apply\(")).Count
if ($applyCount -lt 3) {
    throw "Expected modular pipeline routing in scan, rebuild, and reconciliation paths; found $applyCount."
}

if ($mainWindow -notmatch "LibraryEngine") {
    throw "Application UI is not wired to LibraryEngine."
}
if ($mainWindow -notmatch "ModelIntelligencePipeline") {
    throw "Manual application analysis is not routed through ModelIntelligencePipeline."
}
if ($mainWindow -match "entityClassification\.Apply\(") {
    throw "Legacy EntityClassificationService bypass remains in MainWindow analysis path."
}


if ($engine -notmatch 'LibraryFileDiscovery fileDiscovery') { throw "LibraryEngine must depend on the canonical LibraryFileDiscovery boundary." }
if ($engine -notmatch 'DuplicateGroupBuilder duplicateGroups') { throw "LibraryEngine must depend on the canonical DuplicateGroupBuilder boundary." }
if ($engine -match 'SafeEnumerate3Mf') { throw "LibraryEngine must not own filesystem traversal; use LibraryFileDiscovery." }
if ($engine -match 'GroupBy\(x => x\.Hash') { throw "LibraryEngine must not own duplicate-group derivation; use DuplicateGroupBuilder." }
if ($discovery -notmatch 'Discover3MfFiles') { throw "LibraryFileDiscovery must expose Discover3MfFiles." }
if ($discovery -notmatch 'ReparsePoint') { throw "LibraryFileDiscovery must retain reparse-point protection." }
if ($duplicateGroups -notmatch 'GroupBy\(x => x\.Hash') { throw "DuplicateGroupBuilder must own deterministic hash grouping." }
if ($duplicateGroups -notmatch 'Math\.Min\(12') { throw "DuplicateGroupBuilder must retain the canonical group-id truncation rule." }

Write-Host "PASS: modular library boundaries are enforced."
Write-Host "PASS: modular intelligence boundaries are enforced."
Write-Host "PASS: forensic analysis uses the canonical stage graph."
