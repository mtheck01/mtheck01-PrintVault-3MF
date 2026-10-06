import sys
import tempfile
from pathlib import Path
import subprocess
import shutil

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

    # Never allow an old referenced-project DLL to satisfy this challenge.
    # Git checkout can restore source while leaving build outputs from an earlier
    # revision on a self-hosted runner. Remove referenced outputs before compiling.
    for project_root in (ROOT / "src/PrintVault.Infrastructure", ROOT / "src/PrintVault.Core"):
        for output in ("bin", "obj"):
            shutil.rmtree(project_root / output, ignore_errors=True)

    build = subprocess.run(
        ["dotnet", "build", str(project), "--nologo", "--no-incremental", "-p:BuildProjectReferences=true", "-v:minimal"],
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

    result = subprocess.run(
        ["dotnet", "run", "--project", str(project), "--no-build", "--nologo"],
        cwd=ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
    )
    print("LANGUAGE_CHALLENGE_RUN_EXIT=" + str(result.returncode))
    print(result.stdout, end="")
    print(result.stderr, end="")
    raise SystemExit(result.returncode)
