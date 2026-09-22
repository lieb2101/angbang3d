import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from bridge import Bridge

b = Bridge(savefile='smoke_birth_review')
print('Initial frame phase:', b.frame.get('phase'))
for step in range(25):
    screen = '\n'.join(r.get('g', '') for r in b.frame.get('term', {}).get('rows', []))
    ui = b.frame.get('ui', {})
    print(f"=== STEP {step} (phase={b.frame.get('phase')}, more={ui.get('more')}, awaiting={ui.get('awaiting_command')}) ===")
    for line in screen.split('\n'):
        l = line.strip()
        if any(w in l.lower() for w in ['choose', 'race', 'class', 'roller', 'use as is', 'start over', 'reroll', 'press', '[']):
            print('  MATCH:', l)
    if 'use as is' in screen.lower() or 'start over' in screen.lower() or 'r to reroll' in screen.lower():
        print('REACHED REVIEW SCREEN at step', step)
        print('Full screen:\n', screen)
        break
    if ui.get('more'):
        b.key('enter')
    else:
        b.key('@')

b.close()
