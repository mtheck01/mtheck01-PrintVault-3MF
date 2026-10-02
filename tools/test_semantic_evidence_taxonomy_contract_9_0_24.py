from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SERVICE = ROOT / 'src' / 'PrintVault.Infrastructure' / 'SemanticEvidenceFusionChallengeService.cs'
text = SERVICE.read_text(encoding='utf-8')

required = [
    'private enum TaxonomyMatchKind { Exact, Descendant, Mismatch, NotApplicable }',
    'CompareExpectedClassification(',
    'actualPath.Take(expectedPath.Length).SequenceEqual(expectedPath, StringComparer.OrdinalIgnoreCase)',
    'TAXONOMY_MATCH={r.TaxonomyMatch}',
    'Passed,TaxonomyMatch,Basis,Evidence',
]
for marker in required:
    assert marker in text, f'missing required contract marker: {marker}'

cases_start = text.index('private static List<ExpectedCase> BuildCases')
cases_end = text.index('private static ModelRecord Fixture')
cases = text[cases_start:cases_end]
expected = {
    'FIX-01': '"Aircraft", false, true)',
    'FIX-02': '"Aircraft", false, true)',
    'FIX-03': '"Aircraft / Attack Aircraft", false, true)',
    'FIX-07': '"Aircraft", true, true)',
    'FIX-09': '"Aircraft", false, true)',
    'FIX-10': '"Aircraft", false, true)',
}
for case_id, suffix in expected.items():
    line = next(line for line in cases.splitlines() if f'new("{case_id}"' in line)
    assert suffix in line, f'{case_id} does not use corrected minimum taxonomy path: {line}'

# The contract must compare taxonomy path segments, not substring/keyword matches.
assert 'Contains("Aircraft"' not in text
assert 'Contains("Carrier"' not in text
assert 'Contains("Helicopter"' not in text

# The challenge remains read-only and must use the existing fusion engine.
assert 'fusion.Fuse(model)' in text
assert 'production.CreateSnapshot(snapshot)' in text

print('SEMANTIC EVIDENCE TAXONOMY CONTRACT 9.0.24 — PASS')
print('Exact and descendant taxonomy paths are accepted; unrelated paths remain mismatches.')
print('No Aircraft Carrier keyword rule or classifier change was added.')
