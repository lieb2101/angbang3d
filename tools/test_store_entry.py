import sys, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from bridge import Bridge
from collections import deque

b = Bridge(savefile='smoke_store_test')
b.birth()
m = b.frame['map']
stores = [(x, y, int(m['rows'][y]['f'][x*2:x*2+2], 16)) for y in range(m['h']) for x in range(m['w']) if 7 <= int(m['rows'][y]['f'][x*2:x*2+2], 16) <= 14]
px, py = b.frame['player']['x'], b.frame['player']['y']
stores.sort(key=lambda s: abs(s[0]-px) + abs(s[1]-py))
sx, sy, sf = stores[0]
queue = deque([(px, py, [])])
visited = {(px, py)}
path = []
while queue:
    cx, cy, cpath = queue.popleft()
    if cx == sx and cy == sy:
        path = cpath
        break
    for dx, dy, mk in [(0, -1, 'up'), (0, 1, 'down'), (-1, 0, 'left'), (1, 0, 'right')]:
        nx, ny = cx + dx, cy + dy
        if 0 <= nx < m['w'] and 0 <= ny < m['h'] and (nx, ny) not in visited:
            nfeat = int(m['rows'][ny]['f'][nx*2:nx*2+2], 16)
            if nfeat in (1, 2, 3, 4, 5, 6) or (nx == sx and ny == sy):
                visited.add((nx, ny))
                queue.append((nx, ny, cpath + [mk]))

for k in path:
    b.key(k)
    ui = b.frame.get('ui', {})
    print(f"Moved {k}: overlay={ui.get('overlay')}, awaiting={ui.get('awaiting_command')}, more={ui.get('more')}, line0={b.screen()[:50].strip()}")
    if ui.get('overlay', 0) > 0:
        print('IN STORE! Overlay > 0')
        print(b.screen())
        break
b.close()
