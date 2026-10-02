from pathlib import Path
import re, sys

root = Path(__file__).resolve().parents[1]
source = root / 'src' / 'PrintVault.Infrastructure' / 'MultilingualEntityService.cs'
s = source.read_text(encoding='utf-8')
required = [
    'A-10 Thunderbolt II','Airbus A400M','AH-64 Apache','AH-6 Little Bird',
    'MH-6 Little Bird','AV-8B Harrier','F-16 Fighting Falcon','P-51 Mustang',
    'PBY Catalina','P-38 Lightning','T-6 Texan','Boeing 747','Airbus A380',
    'Airbus A318','Antonov An-225','F-111 Aardvark','M3A3 Bradley','TIE Advanced',
    'Wall-E','Wile E. Coyote','Pac-Man','Zelda Master Sword','Luffy','Nami','Sanji',
    'Cinderwing3D Tiny Horse'
]
missing=[x for x in required if f'new("{x}"' not in s]
checks=[
    ('coverage anchors', not missing),
    ('numeric-prefix guard preserved', 'only extra prefix' in s and 'prefix.All(char.IsDigit)' in s),
    ('CJK matching preserved', 'IsCjkLike' in s),
    ('variant/context comments preserved', 'printer names and dimensions remain context' in s),
]
for name, ok in checks: print(('PASS' if ok else 'FAIL')+': '+name)
if missing: print('Missing:', ', '.join(missing))

# Lightweight normalization parity for representative filenames from the 9.0.20
# full-library report. This mirrors ContainsPhrase's ASCII token behavior and checks
# that the new anchors can actually fire on the observed catalog naming patterns.
def norm(x):
    return re.sub(r'[^A-Za-z0-9]+', ' ', x.lower()).split()
def contains(text, phrase):
    t,p=norm(text),norm(phrase)
    if not p or len(t)<len(p): return False
    for i in range(len(t)-len(p)+1):
        if t[i:i+len(p)]==p: return True
    if len(p)==1:
        q=p[0]
        for tok in t:
            if len(tok)>len(q) and tok.endswith(q) and tok[:-len(q)].isdigit(): return True
    return False
cases={
    'A-10 Thunderbolt II':['A-10+Thunderbolt+V2.3mf','A-10 Thunderbolt'],
    'Airbus A400M':['A400 M V3.3mf','Airbus A400M - without kit card frame.3mf'],
    'AH-64 Apache':['Apache-AH64.3mf'],
    'AH-6 Little Bird':['AH-6+Little+Bird-P2S.3mf'],
    'AV-8B Harrier':['AV-8B Harrier, without kit card.3mf'],
    'F-16 Fighting Falcon':['F-16 Fighting Falcon.3mf'],
    'P-51 Mustang':['P-51+No+Gear.3mf'],
    'PBY Catalina':['PBY-5A_Final_v1_7_A1.3mf'],
    'P-38 Lightning':['P-38+Finalv2 (1).3mf'],
    'T-6 Texan':['t-6+texan_withoutframe.3mf'],
    'Boeing 747':['747+-+8.3mf'],
    'TIE Advanced':['TIE_ADVANCED-XPA.3mf'],
    'Wall-E':['Wall-E+Articulated.3mf'],
    'Wile E. Coyote':['Wile+E.+Coyote.3mf'],
    'Pac-Man':['Pac-Man.3mf'],
    'Zelda Master Sword':['Zelda+Mastersword.3mf'],
    'Cinderwing3D Tiny Horse':['Cinderwing3D Tiny Horse Skeleton Version.3mf'],
}
rule_map={}
for entity, body in re.findall(r'new\("([^"]+)",\s*"[^"]+",\s*"[^"]+",\s*"[^"]+",\s*\d+,\s*\n\s*new\[] \{([^}]*)\}\)',s):
    phrases=re.findall(r'"([^"]+)"',body)
    rule_map[entity]=phrases
for entity, names in cases.items():
    phrases=rule_map.get(entity,[])
    ok=any(contains(name,ph) for name in names for ph in phrases)
    print(('PASS' if ok else 'FAIL')+f': representative filename -> {entity}')
    checks.append((f'representative {entity}',ok))

sys.exit(0 if all(ok for _,ok in checks) else 1)
