import tempfile
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
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

    result = subprocess.run(
        ["dotnet", "run", "--project", str(project)],
        cwd=ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
    )
    print(result.stdout, end="")
    print(result.stderr, end="")
    raise SystemExit(result.returncode)
