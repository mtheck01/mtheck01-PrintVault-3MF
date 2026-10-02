from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
svc = (ROOT / "src/PrintVault.Infrastructure/MultilingualEntityService.cs").read_text(encoding="utf-8")
proj = (ROOT / "src/PrintVault/PrintVault.csproj").read_text(encoding="utf-8")
cmd = (ROOT / "src/PrintVault.Core/Commands.cs").read_text(encoding="utf-8")
main = (ROOT / "src/PrintVault/MainWindow.xaml.cs").read_text(encoding="utf-8")
checks = {
    "version 8.7.8": "<Version>8.7.8</Version>" in proj and 'Version = "8.7.8"' in cmd,
    "colonial viper retained": "殖民地毒蛇号" in svc and "Colonial Viper" in svc,
    "colonial viper variant substring discovery": "殖民地毒蛇" in svc,
    "colonial raptor multilingual": "殖民地猛禽" in svc and "Colonial Raptor" in svc,
    "star wars millennium falcon": "千年隼" in svc and "Millennium Falcon" in svc,
    "star wars x wing": "x翼战机" in svc and "X-wing" in svc,
    "star destroyer": "歼星舰" in svc and "Star Destroyer" in svc,
    "at at": "帝国步行机" in svc and "AT-AT" in svc,
    "at st": "侦察步行机" in svc and "AT-ST" in svc,
    "star trek bird of prey": "Klingon Bird-of-Prey" in svc,
    "star trek enterprise": "USS Enterprise" in svc and "ncc-1701" in svc,
    "battlestar galactica": "战星卡拉狄加" in svc and "Battlestar Galactica" in svc,
    "firefly serenity": "Serenity" in svc and "firefly serenity" in svc,
    "expanse rocinante": "Rocinante" in svc,
    "doctor who tardis": "TARDIS" in svc,
    "ghostbusters ecto 1": "Ecto-1" in svc,
    "knight rider kitt": "KITT" in svc,
    "mystery machine": "Mystery Machine" in svc,
    "general lee": "General Lee" in svc,
    "inspector still displays entity separately": "Entity: {entity.EntityName}" in main,
    "offline/no rename design retained": "never renames or modifies" in svc,
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit("MULTILINGUAL ENTITY EXPANSION 8.7.8 TEST FAILED: " + ", ".join(failed))
print(f"MULTILINGUAL ENTITY EXPANSION 8.7.8 TEST PASSED — {len(checks)} checks")
