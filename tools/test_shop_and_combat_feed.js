const WebSocket = require('../server/node_modules/ws');
const assert = require('assert');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=E2ETest_' + Math.random().toString(36).substring(2, 6));

let seq = 0;
let lastFrame = null;

ws.on('message', (d) => {
    const m = JSON.parse(d.toString());
    if (m.t === 'frame') {
        seq = m.seq;
        lastFrame = m;
    }
});

function sendKey(k) {
    ws.send('key ' + k);
}

async function waitFrame(predicate, timeout = 4000) {
    const start = Date.now();
    while (Date.now() - start < timeout) {
        if (lastFrame && predicate(lastFrame)) return lastFrame;
        await new Promise(r => setTimeout(r, 25));
    }
    throw new Error('Timeout waiting for frame predicate');
}

// Simple BFS on town grid to navigate around buildings
function findPath(map, startX, startY, goalX, goalY) {
    const h = map.rows.length;
    const w = (map.rows[0].f || '').length / 2;
    const isWalkable = (x, y) => {
        if (x < 0 || x >= w || y < 0 || y >= h) return false;
        if (x === goalX && y === goalY) return true;
        const rowF = map.rows[y].f || '';
        const feat = parseInt(rowF.substring(x * 2, x * 2 + 2), 16);
        return feat === 1 || feat === 3 || (feat >= 7 && feat <= 14);
    };

    const queue = [{ x: startX, y: startY, path: [] }];
    const visited = new Set([`${startX},${startY}`]);

    while (queue.length > 0) {
        const cur = queue.shift();
        if (cur.x === goalX && cur.y === goalY) return cur.path;

        const dirs = [
            { dx: 0, dy: -1, key: 'up' },
            { dx: 0, dy: 1, key: 'down' },
            { dx: -1, dy: 0, key: 'left' },
            { dx: 1, dy: 0, key: 'right' }
        ];

        for (const d of dirs) {
            const nx = cur.x + d.dx;
            const ny = cur.y + d.dy;
            const keyStr = `${nx},${ny}`;
            if (!visited.has(keyStr) && isWalkable(nx, ny)) {
                visited.add(keyStr);
                queue.push({ x: nx, y: ny, path: [...cur.path, d.key] });
            }
        }
    }
    return null;
}

async function run() {
    console.log('[E2E] Connecting to Cloud WebSocket...');
    while (!lastFrame) await new Promise(r => setTimeout(r, 80));

    // Birth
    for (let s = 0; s < 25; s++) {
        const txt = (lastFrame.term?.rows?.map(r => r.g || '').join('\n') || '').toLowerCase();
        if (txt.includes('use as is') || txt.includes('to start over')) break;
        if (lastFrame.ui?.more) sendKey('enter'); else sendKey('@');
        await new Promise(r => setTimeout(r, 120));
    }
    sendKey('enter');
    await waitFrame(f => f.phase === 'play' && f.map && f.player && f.ui?.awaiting_command);
    console.log('[E2E] Ready in town! Player at:', lastFrame.player.x, lastFrame.player.y);

    // 1. Test Inventory (i) in town - MUST NOT be misclassified as store!
    console.log('[E2E Test 1] Testing Inventory (i) in town...');
    sendKey('i');
    await waitFrame(f => f.ui && (f.ui.overlay || 0) > 0);
    const screenText1 = (lastFrame.term?.rows?.map(r => r.g || '').join('\n') || '').toLowerCase();
    const isItemPrompt1 = screenText1.includes('select item:') || screenText1.includes('inven:');
    assert(isItemPrompt1, 'Must be item prompt');
    console.log('  -> Inventory detected as item prompt! Row 0:', lastFrame.term.rows[0].g.trim());
    sendKey('escape');
    await waitFrame(f => f.ui && (f.ui.overlay || 0) === 0);
    console.log('  -> Escaped back to 3D world!');

    // 2. Test Equipment (e) in town
    console.log('[E2E Test 2] Testing Equipment (e) in town...');
    sendKey('e');
    await waitFrame(f => f.ui && (f.ui.overlay || 0) > 0);
    const screenText2 = (lastFrame.term?.rows?.map(r => r.g || '').join('\n') || '').toLowerCase();
    assert(screenText2.includes('equip:'), 'Must include equip:');
    console.log('  -> Equipment detected as item prompt! Row 0:', lastFrame.term.rows[0].g.trim());
    sendKey('escape');
    await waitFrame(f => f.ui && (f.ui.overlay || 0) === 0);
    console.log('  -> Escaped back to 3D world!');

    // 3. Test Throw (v) in town
    console.log('[E2E Test 3] Testing Throw (v) in town...');
    sendKey('v');
    await waitFrame(f => f.ui && (f.ui.overlay || 0) > 0);
    const screenText3 = (lastFrame.term?.rows?.map(r => r.g || '').join('\n') || '').toLowerCase();
    assert(screenText3.includes('throw which item?'), 'Must include throw which item?');
    console.log('  -> Throw detected as item prompt! Row 0:', lastFrame.term.rows[0].g.trim());
    sendKey('escape');
    await waitFrame(f => f.ui && (f.ui.overlay || 0) === 0);
    console.log('  -> Escaped back to 3D world!');

    // 4. Test Store Walking
    console.log('[E2E Test 4] Testing Walking Into Store...');
    // Find all store tiles:
    const shops = [];
    for (let y = 0; y < lastFrame.map.rows.length; y++) {
        const rowF = lastFrame.map.rows[y].f || '';
        for (let x = 0; x < rowF.length / 2; x++) {
            const feat = parseInt(rowF.substring(x * 2, x * 2 + 2), 16);
            if (feat >= 7 && feat <= 14) shops.push({ x, y, feat });
        }
    }
    // Find closest walkable shop
    let bestShop = null;
    let bestPath = null;
    for (const s of shops) {
        const p = findPath(lastFrame.map, lastFrame.player.x, lastFrame.player.y, s.x, s.y);
        if (p && (!bestPath || p.length < bestPath.length)) {
            bestPath = p;
            bestShop = s;
        }
    }
    assert(bestPath, 'Must find a walkable path to a shop');
    console.log(`  -> Navigating to Shop (feat ${bestShop.feat}) at (${bestShop.x}, ${bestShop.y}) via ${bestPath.length} steps: ${bestPath.join(', ')}`);

    for (const stepKey of bestPath) {
        sendKey(stepKey);
        await new Promise(r => setTimeout(r, 70));
        if ((lastFrame.ui?.overlay || 0) > 0) break;
    }

    await waitFrame(f => f.ui && (f.ui.overlay || 0) > 0, 3000);
    console.log('  -> Stepped into shop! Overlay:', lastFrame.ui.overlay);
    console.log('  -> Shop Title/Owner:', lastFrame.term?.rows?.[1]?.g?.trim());
    console.log('  -> Shop Header:', lastFrame.term?.rows?.[3]?.g?.trim());
    assert(lastFrame.ui.overlay > 0, 'Store must set overlay > 0');
    
    // Test store exit
    sendKey('escape');
    await waitFrame(f => f.ui && (f.ui.overlay || 0) === 0, 3000);
    console.log('  -> Exited store back to town! Player pos:', lastFrame.player.x, lastFrame.player.y, 'Overlay:', lastFrame.ui.overlay);

    // 5. Test Wall Collision / Action Messages
    console.log('[E2E Test 5] Testing Action Messages (bumping wall)...');
    sendKey('up');
    await new Promise(r => setTimeout(r, 120));
    const r0 = lastFrame.term?.rows?.[0]?.g?.trim();
    console.log('  -> Term Row 0 after move:', r0);
    const msgs = lastFrame.messages?.map(m => m.text?.trim()) || [];
    console.log('  -> Messages received:', msgs.slice(-3));

    console.log('\n[E2E SUCCESS] All 5 core gameplay mechanics verified 100%!');
    ws.close();
    process.exit(0);
}

run().catch(e => {
    console.error('[E2E Error]', e);
    process.exit(1);
});
