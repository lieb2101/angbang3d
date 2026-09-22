import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from bridge import Bridge
from collections import deque

with Bridge(savefile='smoke_stairs_test') as b:
    b.birth()
    m = b.frame['map']
    stairs = []
    for y in range(m['h']):
        for x in range(m['w']):
            feat = int(m['rows'][y]['f'][x*2:x*2+2], 16)
            if feat in (5, 6):
                stairs.append((x, y, feat))
    px, py = b.frame['player']['x'], b.frame['player']['y']
    print(f"Found {len(stairs)} stairs. Player at ({px}, {py}).")
    if stairs:
        sx, sy, sf = stairs[0]
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
        print(f"Arrived at stairs: ({b.frame['player']['x']}, {b.frame['player']['y']}), phase={b.frame.get('phase')}")
        b.key('>')
        print(f"After descend (>): phase={b.frame.get('phase')}, depth={b.frame.get('player', {}).get('depth')}, ui={b.frame.get('ui')}")
        b.key('space')
        print(f"After space: phase={b.frame.get('phase')}, depth={b.frame.get('player', {}).get('depth')}, ui={b.frame.get('ui')}")
