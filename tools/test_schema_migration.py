import os
import sqlite3
import tempfile
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_SOURCE = (ROOT / 'src/PrintVault.Infrastructure/LibraryRepository.cs').read_text(encoding='utf-8')

MIGRATIONS = [
    ('Path', "TEXT NOT NULL DEFAULT ''"), ('Name', "TEXT NOT NULL DEFAULT ''"),
    ('Category', "TEXT NOT NULL DEFAULT 'Uncategorized'"), ('Size', 'INTEGER NOT NULL DEFAULT 0'),
    ('ModifiedUtc', "TEXT NOT NULL DEFAULT ''"), ('ThumbnailPath', 'TEXT'), ('Favorite', 'INTEGER NOT NULL DEFAULT 0'),
    ('Tags', "TEXT DEFAULT ''"), ('Family', "TEXT DEFAULT ''"), ('Slicer', "TEXT DEFAULT ''"),
    ('Materials', "TEXT DEFAULT ''"), ('ObjectCount', 'INTEGER DEFAULT 0'), ('Dimensions', "TEXT DEFAULT ''"),
    ('IntelligenceScore', 'REAL DEFAULT 0'), ('PrintReady', 'INTEGER DEFAULT 0'),
    ('DuplicateGroup', "TEXT DEFAULT ''"), ('Hash', "TEXT DEFAULT ''"),
    ('SemanticType', "TEXT DEFAULT ''"), ('Subtype', "TEXT DEFAULT ''"),
    ('SuggestedTags', "TEXT DEFAULT ''"), ('IntelligenceReason', "TEXT DEFAULT ''"),
    ('RiskFlags', "TEXT DEFAULT ''"), ('PrintMethod', "TEXT DEFAULT 'Unknown'"),
    ('PrintMethodConfidence', 'REAL DEFAULT 0'), ('PrintMethodEvidence', "TEXT DEFAULT ''"),
    ('SpecialType', "TEXT DEFAULT ''"), ('CategoryOverride', 'INTEGER DEFAULT 0'),
    ('PrintMethodOverride', 'INTEGER DEFAULT 0'), ('OriginalLanguage', "TEXT DEFAULT 'Unknown'"),
    ('TranslatedTitle', "TEXT DEFAULT ''"), ('TranslationConfidence', 'INTEGER DEFAULT 0'),
    ('TranslationEvidence', "TEXT DEFAULT ''")
]

# Keep this SQLite migration fixture aligned with the production migration declaration.
for name, _ in MIGRATIONS:
    assert re.search(r'\\(\s*"' + re.escape(name) + r'"\s*,', REPOSITORY_SOURCE), f"production migration declaration missing: {name}"
assert 'private const int CurrentSchemaVersion = 5;' in REPOSITORY_SOURCE

# Windows-safe SQLite temp handling: close the temp file before SQLite opens it.
with tempfile.TemporaryDirectory(prefix='PrintVaultSchemaTest_') as temp_dir:
    db_path = os.path.join(temp_dir, 'schema.db')
    con = sqlite3.connect(db_path)
    try:
        con.execute("CREATE TABLE Models(Id INTEGER PRIMARY KEY, Path TEXT UNIQUE NOT NULL, Name TEXT NOT NULL, Category TEXT NOT NULL, Size INTEGER NOT NULL, ModifiedUtc TEXT NOT NULL)")
        con.execute("INSERT INTO Models(Path,Name,Category,Size,ModifiedUtc) VALUES(?,?,?,?,?)", ('C:/library/test.3mf','test.3mf','Vehicles',123,'2026-09-21T12:00:00Z'))
        con.commit()
        cols={r[1] for r in con.execute('PRAGMA table_info(Models)')}
        for name,definition in MIGRATIONS:
            if name not in cols:
                con.execute(f'ALTER TABLE Models ADD COLUMN {name} {definition}')
                cols.add(name)
        con.execute('CREATE INDEX IF NOT EXISTS IX_Models_Category ON Models(Category)')
        con.execute('CREATE INDEX IF NOT EXISTS IX_Models_Hash ON Models(Hash)')
        con.execute('CREATE INDEX IF NOT EXISTS IX_Models_Name ON Models(Name)')
        row=con.execute('SELECT Id,Path,Name,Category,Size,ModifiedUtc,ThumbnailPath,Favorite,Tags,Family,Slicer,Materials,ObjectCount,Dimensions,IntelligenceScore,PrintReady,DuplicateGroup,Hash FROM Models').fetchone()
        assert row[1]=='C:/library/test.3mf' and row[2]=='test.3mf' and row[3]=='Vehicles'
        assert row[17]==''
        con.execute('PRAGMA user_version=2')
        assert con.execute('PRAGMA user_version').fetchone()[0]==2
        cols2={r[1] for r in con.execute('PRAGMA table_info(Models)')}
        for name,_ in MIGRATIONS: assert name in cols2
        print('SCHEMA MIGRATION TEST PASSED')
    finally:
        con.close()
