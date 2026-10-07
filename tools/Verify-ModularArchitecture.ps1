$ErrorActionPreference = "Stop"

$pipeline = Get-Content "src/PrintVault.Infrastructure/ModelIntelligencePipeline.cs" -Raw
$engine = Get-Content "src/PrintVault.Infrastructure/LibraryEngine.cs" -Raw
$mainWindow = Get-Content "src/PrintVault/MainWindow.xaml.cs" -Raw

$requiredPipeline = @(
    "new LanguageNormalizationStage()",
    "new EntityClassificationStage()",
    "new EntityRecognitionStage()",
    "new SemanticFusionStage()",
    "new EvidenceArbitrationStage()"
)
foreach ($token in $requiredPipeline) {
    if ($pipeline -notmatch [regex]::Escape($token)) {
        throw "Modular pipeline contract missing: $token"
    }
}

# Production and forensic analysis must share the same stage graph. Forensic
# analysis may skip only the production classification mutation stage; it must
# not instantiate a second, partial pipeline that can drift independently.
if ($pipeline -notmatch "foreach \(var stage in stages\)") {
    throw "Canonical stage graph is not iterated by the intelligence pipeline."
}
if ($pipeline -notmatch "if \(stage is EntityClassificationStage\)") {
    throw "Forensic analysis does not explicitly isolate the production classification mutation stage."
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

Write-Host "PASS: modular intelligence boundaries are enforced."
Write-Host "PASS: forensic analysis uses the canonical stage graph."
