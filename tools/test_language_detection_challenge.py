import sys
import tempfile
from pathlib import Path
import subprocess
import shutil
import hashlib
import os

ROOT = Path(__file__).resolve().parents[1]

# GitHub Actions Windows runners may use cp1252 for stdout; the challenge emits
# multilingual diagnostics, so force UTF-8 output as well as subprocess decoding.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

infra = (ROOT / "src/PrintVault.Infrastructure/PrintVault.Infrastructure.csproj").resolve()

program = r'''
using System;
using PrintVault.Infrastructure;

var cases = new[]
{
    ("ZH-SIMPLIFIED", "吊车 模型", "Chinese", false),
    ("ZH-TRADITIONAL", "蝙蝠俠 模型", "Chinese", false),
    ("JA-KANA", "ドラゴン フィギュア", "Japanese", false),
    ("JA-KANJI-ONLY", "飛行機", "Japanese", false),
    ("KO", "자동차 모델", "Korean", false),
    ("MIXED", "汽车 자동차", "Chinese", true),
    ("EN-UNSUPPORTED", "Batmobile model", "Unknown", false),
    ("NUMERIC-UNSUPPORTED", "3D_12345", "Unknown", false),
    ("ZH-PRINT", "3D打印模型", "Chinese", false),
};

var loadedAssembly = typeof(LanguageIntelligenceService).Assembly;
var loadedPath = loadedAssembly.Location;
var loadedHash = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(loadedPath))).ToLowerInvariant();
var expectedHash = Environment.GetEnvironmentVariable("PRINTVAULT_EXPECTED_INFRA_HASH") ?? "";
var expectedPath = Environment.GetEnvironmentVariable("PRINTVAULT_EXPECTED_INFRA_PATH") ?? "";
Console.WriteLine($"LANGUAGE_CHALLENGE_EXPECTED_ASSEMBLY={expectedPath}");
Console.WriteLine($"LANGUAGE_CHALLENGE_EXPECTED_ASSEMBLY_HASH={expectedHash}");
Console.WriteLine($"LANGUAGE_CHALLENGE_LOADED_ASSEMBLY={loadedPath}");
Console.WriteLine($"LANGUAGE_CHALLENGE_LOADED_ASSEMBLY_HASH={loadedHash}");
Console.WriteLine($"LANGUAGE_CHALLENGE_LOADED_ASSEMBLY_VERSION={loadedAssembly.GetName().Version}");
Console.WriteLine($"LANGUAGE_CHALLENGE_LOADED_ASSEMBLY_MVID={loadedAssembly.ManifestModule.ModuleVersionId}");
if (!string.IsNullOrWhiteSpace(expectedHash) && !string.Equals(loadedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("LANGUAGE_CHALLENGE_ASSEMBLY_PROVENANCE=FAIL_HASH_MISMATCH");
    Environment.Exit(2);
}
if (!string.IsNullOrWhiteSpace(expectedPath) && !string.Equals(Path.GetFullPath(loadedPath), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("LANGUAGE_CHALLENGE_ASSEMBLY_PROVENANCE=FAIL_PATH_MISMATCH");
    Environment.Exit(2);
}
Console.WriteLine("LANGUAGE_CHALLENGE_ASSEMBLY_PROVENANCE=PASS");
var service = new LanguageIntelligenceService();
var failures = 0;

foreach (var c in cases)
{
    var result = service.Analyze(c.Item2);
    var languageOk = result.Language == c.Item3;
    var mixedOk = result.MixedLanguage == c.Item4;
    var pass = languageOk && mixedOk;

    Console.WriteLine($"{c.Item1}|expected={c.Item3}|actual={result.Language}|mixedExpected={c.Item4}|mixedActual={result.MixedLanguage}|confidence={result.Confidence}|pass={pass}");

    if (!pass)
    {
        Console.WriteLine($"  normalized={result.NormalizedText}");
        Console.WriteLine($"  translated={result.TranslatedText}");
        Console.WriteLine($"  evidence={result.Evidence}");
        failures++;
    }
}

if (failures != 0)
{
    Console.WriteLine($"LANGUAGE DETECTION CHALLENGE FAILED — {failures} case(s)");
    Environment.Exit(1);
}

Console.WriteLine($"LANGUAGE DETECTION CHALLENGE PASSED — {cases.Length} executable cases");
'''

with tempfile.TemporaryDirectory(prefix="printvault-language-challenge-") as td:
    td = Path(td)
    project = td / "Challenge.csproj"
    project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{infra.as_posix()}" />
  </ItemGroup>
</Project>
''', encoding="utf-8")
    (td / "Program.cs").write_text(program, encoding="utf-8")

    # The runner is self-hosted. Remove every source/tool bin and obj tree,
    # then perform a true Rebuild so no prior assembly can participate.
    for project_root in (ROOT / "src", ROOT / "tools"):
        if not project_root.exists():
            continue
        for output in project_root.rglob("bin"):
            shutil.rmtree(output, ignore_errors=True)
        for output in project_root.rglob("obj"):
            shutil.rmtree(output, ignore_errors=True)

    build = subprocess.run(
        ["dotnet", "build", str(project), "--nologo", "-t:Rebuild", "-p:BuildProjectReferences=true", "-p:UseSharedCompilation=false", "-v:minimal"],
        cwd=ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
    )
    print("LANGUAGE_CHALLENGE_BUILD_EXIT=" + str(build.returncode))
    print(build.stdout, end="")
    print(build.stderr, end="")
    if build.returncode != 0:
        raise SystemExit(build.returncode)

    challenge_dll = td / "bin" / "Debug" / "net8.0-windows" / "Challenge.dll"
    if not challenge_dll.is_file():
        raise RuntimeError(f"Challenge output missing after rebuild: {challenge_dll}")

    infra_dll = ROOT / "src" / "PrintVault.Infrastructure" / "bin" / "Debug" / "net8.0-windows" / "PrintVault.Infrastructure.dll"
    if not infra_dll.is_file():
        raise RuntimeError(f"Infrastructure output missing after rebuild: {infra_dll}")

    expected_hash = hashlib.sha256(infra_dll.read_bytes()).hexdigest()
    print("LANGUAGE_CHALLENGE_EXPECTED_INFRA_DLL=" + str(infra_dll))
    print("LANGUAGE_CHALLENGE_EXPECTED_INFRA_HASH=" + expected_hash)

    # Execute the exact rebuilt challenge DLL.
    result = subprocess.run(
        ["dotnet", str(challenge_dll)],
        cwd=ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        env={**os.environ,
             "PRINTVAULT_EXPECTED_INFRA_HASH": expected_hash,
             "PRINTVAULT_EXPECTED_INFRA_PATH": str(infra_dll)},
    )
    print("LANGUAGE_CHALLENGE_RUN_EXIT=" + str(result.returncode))
    print(result.stdout, end="")
    print(result.stderr, end="")
    raise SystemExit(result.returncode)
