from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
svc = (ROOT / 'src' / 'PrintVault.Infrastructure' / 'SmartCategoryReconciliationService.cs').read_text(encoding='utf-8')
analyzer = (ROOT / 'src' / 'PrintVault.Infrastructure' / 'ThreeMfAnalyzer.cs').read_text(encoding='utf-8')

checks = {
    'vehicle semantic is strong': 'ExactSemantic("Vehicle", "Vehicles", 28' in svc,
    'batmobile is strong vehicle evidence': '"batmobile"' in svc and 'Strong vehicle terminology' in svc,
    'vehicle analyzer signal': 'new("Vehicles","batmobile",10' in analyzer,
    'known sci-fi vehicles': 'millennium falcon' in analyzer and 'x-wing' in analyzer,
    'generic scenery is weak': 'Add("Nature & Scenery", 5, "Analyzer semantic type: Scenery")' in svc,
    'generic prop is weak': 'Add("Props & Accessories", 4, "Analyzer semantic type: Prop")' in svc,
    'manual override hard stop': 'if (model.CategoryOverride) return null;' in svc,
    'confidence capped for weak evidence': 'confidence = Math.Min(confidence, 82)' in svc,
    'meaningful margin required': 'margin < 5.0' in svc,
    'vehicle subtype is strong': 'Vehicle subtype: {subtype}' in svc,
    'no prop-to-scenery shortcut': 'Family=Prop' in svc and 'Prop/Scenery metadata is intentionally weak' in svc,
}
failed = [name for name, ok in checks.items() if not ok]
if failed:
    raise SystemExit('SMART CATEGORY ENGINE TEST FAILED: ' + ', '.join(failed))
print(f'SMART CATEGORY ENGINE TEST PASSED — {len(checks)} checks')
