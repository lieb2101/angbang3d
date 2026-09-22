const WebSocket = require('../server/node_modules/ws');
const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=StoreInspect_' + Math.random().toString(36).substring(2,6));
let lastFrame = null;
ws.on('message', (d) => {
    const msg = JSON.parse(d.toString());
    if (msg.t === 'frame') lastFrame = msg;
});
function waitF(n=100) { return new Promise(r => setTimeout(r, n)); }

// BFS to coordinate
function getPathTo(map, startX, startY, targetX, targetY) {
    const isPassable = (x, y) => {
        if (x < 0 || x >= map.w || y < 0 || y >= map.h) return false;
        const feat = parseInt(map.rows[y].f.substring(x * 2, x * 2 + 2), 16);
        return feat === 1 || feat === 2 || feat === 3 || feat === 4 || feat === 5 || feat === 6 || feat === 16 || feat === 23 || feat === 24 || (feat >= 7 && feat <= 14);
    };
    const queue = [{ x: startX, y: startY, path: [] }];
    const visited = new Set([`${startX},${startY}`]);
    while (queue.length > 0) {
        const cur = queue.shift();
        if (cur.x === targetX && cur.y === targetY) return cur.path;
        for (const d of [{ dx: 0, dy: -1, k: 'up' }, { dx: 0, dy: 1, k: 'down' }, { dx: -1, dy: 0, k: 'left' }, { dx: 1, dy: 0, k: 'right' }]) {
            const nx = cur.x + d.dx, ny = cur.y + d.dy;
            if (!visited.has(`${nx},${ny}`) && isPassable(nx, ny)) {
                visited.add(`${nx},${ny}`);
                queue.push({ x: nx, y: ny, path: [...cur.path, d.k] });
            }
        }
    }
    return null;
}

async function run() {
    while (!lastFrame) await waitF(20);
    for (let i = 0; i < 20; i++) {
        if (lastFrame.phase === 'play' && lastFrame.map && lastFrame.player) break;
        ws.send('key enter');
        await waitF(100);
        ws.send('key @');
        await waitF(100);
    }
    console.log('Player at:', lastFrame.player.x, lastFrame.player.y);
    const map = lastFrame.map;
    // Find all store feats
    for (let y = 0; y < map.h; y++) {
        for (let x = 0; x < map.w; x++) {
            const f = parseInt(map.rows[y].f.substring(x*2, x*2+2), 16) || 0;
            if (f === 13) {
                console.log(`Store feat ${f} (Black Market) at ${x},${y}`);
                const path = getPathTo(map, lastFrame.player.x, lastFrame.player.y, x, y);
                if (path) {
                    console.log(`Walking to store feat ${f} (path len ${path.length})...`);
                    for (const k of path) {
                        ws.send('key ' + k);
                        await waitF(100);
                    }
                    // In Angband, store entrance is entered by stepping on it or moving into it
                    console.log('Player pos:', lastFrame.player.x, lastFrame.player.y, 'target:', x, y);
                    if (lastFrame.ui.overlay === 0) {
                        // send one more move or enter
                        console.log('Sending move into store...');
                        ws.send('key enter');
                        await waitF(150);
                    }
                    console.log('Current frame:');
                    console.log('phase:', lastFrame.phase);
                    console.log('hasMap:', !!lastFrame.map);
                    console.log('hasPlayer:', !!lastFrame.player);
                    console.log('ui:', JSON.stringify(lastFrame.ui));
                    const rows = (lastFrame.term && lastFrame.term.rows ? lastFrame.term.rows.map(r => r.g || '').join('\n') : '');
                    console.log('term rows 0-4:\n' + rows.split('\n').slice(0, 5).join('\n'));

                    // Mock DOM elements to run updateTerminalToolbar logic
                    const elements = {
                        'term-title': { textContent: '' },
                        'btn-quick-birth': { style: { display: 'inline-flex' } },
                        'btn-term-reroll': { style: { display: 'inline-flex' } },
                        'btn-term-custom': { style: { display: 'inline-flex' } },
                        'btn-term-advance': { style: { display: 'inline-flex' }, textContent: '' },
                        'btn-term-escape': { style: { display: 'inline-flex' }, textContent: '' },
                        'store-actions-bar': { style: { display: 'none' } },
                        'item-actions-bar': { style: { display: 'none' } },
                        'item-buttons-list': { innerHTML: '', appendChild: () => {} },
                        'btn-item-switch': {},
                        'btn-item-cancel': {},
                        'btn-store-advance': { style: { display: 'none' } }
                    };
                    global.document = {
                        getElementById: (id) => elements[id] || null
                    };

                    const fs = require('fs');
                    const appJs = fs.readFileSync('./server/public/js/app.js', 'utf8');
                    // Extract updateTerminalToolbar function
                    const match = appJs.match(/function updateTerminalToolbar\(frame\)\s*\{([\s\S]*?)\n    \}/);
                    const updateToolbarFn = new Function('frame', match[1]);
                    updateToolbarFn(lastFrame);

                    console.log('Toolbar Title:', elements['term-title'].textContent);
                    console.log('Store Bar Display:', elements['store-actions-bar'].style.display);
                    console.log('Reroll Btn Display:', elements['btn-term-reroll'].style.display);
                    console.log('Custom Btn Display:', elements['btn-term-custom'].style.display);
                    console.log('Advance Btn Display:', elements['btn-term-advance'].style.display);

                    const assert = require('assert');
                    assert.strictEqual(elements['term-title'].textContent, '⚔ BLACK MARKET');
                    assert.strictEqual(elements['store-actions-bar'].style.display, 'flex');
                    assert.strictEqual(elements['btn-term-reroll'].style.display, 'none');
                    assert.strictEqual(elements['btn-term-custom'].style.display, 'none');
                    assert.strictEqual(elements['btn-term-advance'].style.display, 'none');
                    console.log('Store Toolbar Verification: PASSED 100%!');

                    ws.close();
                    process.exit(0);
                }
            }
        }
    }
    ws.close();
    process.exit(0);
}
run().catch(e => { console.error(e); process.exit(1); });
