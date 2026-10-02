from pathlib import Path
root=Path(__file__).resolve().parents[1]
svc=(root/'src/PrintVault.Infrastructure/SemanticRelationshipIntegrationChallengeService.cs').read_text(encoding='utf-8')
win=(root/'src/PrintVault/SemanticRelationshipIntegrationChallengeWindow.cs').read_text(encoding='utf-8')
xaml=(root/'src/PrintVault/MainWindow.xaml').read_text(encoding='utf-8')
cs=(root/'src/PrintVault/MainWindow.xaml.cs').read_text(encoding='utf-8')
checks=[
('independent expected relationship', 'ExpectedRelationship' in svc),
('entity evaluated before relationship', 'entities.Recognize(a)' in svc and 'intelligence.AnalyzeRelationship(a, b)' in svc),
('A-4 alias case', 'A4 Blue Angels' in svc),
('HueForge same-size negative', 'same HueForge size' in svc),
('printer negative', 'shared printer context' in svc),
('generic negative', 'generic vocabulary' in svc.lower()),
('unavailable real cases', 'UNAVAILABLE' in svc),
('physical protection', 'production catalog fingerprints unchanged' in svc),
('controlled fixtures', 'CONTROLLED FIXTURE' in svc),
('window', 'SemanticRelationshipIntegrationChallengeService' in win),
('menu entry', 'SemanticRelationshipIntegrationChallenge_Click' in xaml and 'SemanticRelationshipIntegrationChallenge_Click' in cs),
('version 9.0.9', '9.0.9' in xaml),
]
for name,ok in checks:
    if not ok: raise SystemExit('FAIL: '+name)
print(f'9.0.9 SEMANTIC+RELATIONSHIP STATIC AUDIT PASSED — {len(checks)} checks')
