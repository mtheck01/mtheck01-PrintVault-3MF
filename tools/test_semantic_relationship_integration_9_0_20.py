from pathlib import Path
root=Path(__file__).resolve().parents[1]
entity=(root/"src/PrintVault.Infrastructure/MultilingualEntityService.cs").read_text(encoding="utf-8")
svc=(root/"src/PrintVault.Infrastructure/SemanticRelationshipIntegrationChallengeService.cs").read_text(encoding="utf-8")
checks=[
 ("numeric SKU identity rule", "prefix.All(char.IsDigit)" in entity),
 ("no arbitrary substring rule", "token.Contains(phraseToken" not in entity),
 ("Batmobile entity remains defined", 'new("Batmobile"' in entity),
 ("integration build is dynamic", "AppVersion.Version" in svc and "BUILD: 9.0.20" not in svc),
 ("integration report is dynamic", "AppVersion.Version" in svc and "PrintVault 3MF 9.0.20" not in svc),
 ("real Batmobile pair", '"Batmobile"' in svc and '89batmobile' not in svc),
]
for name,ok in checks:
    if not ok: raise SystemExit("FAIL: "+name)
print(f"SEMANTIC+RELATIONSHIP VERSION-SAFE STATIC AUDIT PASSED — {len(checks)} checks")
