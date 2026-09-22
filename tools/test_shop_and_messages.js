const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=ShopTest_' + Math.random().toString(36).substring(2, 6));

let seq = 0;
let lastFrame = null;

function sendKey(key) {
    ws.send(`key ${key}`);
}

function waitForFrame(timeout = 4000) {
    const startSeq = seq;
    return new Promise((resolve) => {
        const timer = setTimeout(() => resolve(lastFrame), timeout);
        const check = () => {
            if (seq > startSeq) {
                clearTimeout(timer);
                resolve(lastFrame);
            } else {
                setTimeout(check, 40);
            }
        };
        check();
    });
}

ws.on('open', () => {
    console.log('[ShopTest] Connected to WebSocket');
});

ws.on('message', (data) => {
    const msg = JSON.parse(data.toString());
    if (msg.t === 'frame') {
        seq = msg.seq;
        lastFrame = msg;
    }
});

async function run() {
    while (!lastFrame) await new Promise(r => setTimeout(r, 100));

    // Fast-advance birth
    for (let step = 0; step < 20; step++) {
        const screenText = (lastFrame.term && lastFrame.term.rows ? lastFrame.term.rows.map(r => r.g || '').join('\n') : '').toLowerCase();
        if (screenText.includes("use as is") || screenText.includes("start over") || screenText.includes("r to reroll")) {
            break;
        }
        if (lastFrame.ui && lastFrame.ui.more) {
            sendKey('enter');
        } else {
            sendKey('@');
        }
        await waitForFrame();
    }

    sendKey('enter');
    await waitForFrame();

    console.log(`[ShopTest] In play: phase=${lastFrame.phase}, player at (${lastFrame.player.x}, ${lastFrame.player.y})`);
    console.log(`[ShopTest] Initial messages count: ${(lastFrame.messages || []).length}`);
    (lastFrame.messages || []).forEach((m, i) => console.log(`   msg[${i}]: "${m.text}" (count=${m.count}, attr=${m.attr})`));

    // Find all store tiles in town map
    const stores = [];
    for (let y = 0; y < lastFrame.map.h; y++) {
        const featRow = lastFrame.map.rows[y].f;
        const glyphRow = lastFrame.map.rows[y].g;
        for (let x = 0; x < lastFrame.map.w; x++) {
            const feat = parseInt(featRow.substring(x * 2, x * 2 + 2), 16);
            if (feat >= 7 && feat <= 14) {
                stores.push({ x, y, feat, glyph: glyphRow[x] });
            }
        }
    }
    console.log(`[ShopTest] Found ${stores.length} store entrances in town:`);
    stores.forEach(s => console.log(`   Store feat=${s.feat} '${s.glyph}' at (${s.x}, ${s.y})`));

    // Pick closest store
    const px = lastFrame.player.x;
    const py = lastFrame.player.y;
    stores.sort((a, b) => (Math.abs(a.x - px) + Math.abs(a.y - py)) - (Math.abs(b.x - px) + Math.abs(b.y - py)));
    const targetStore = stores[0];
    console.log(`[ShopTest] Target closest store at (${targetStore.x}, ${targetStore.y}) feat=${targetStore.feat} '${targetStore.glyph}'`);

    // Pathfind / navigate step by step towards targetStore
    let steps = 0;
    while ((lastFrame.player.x !== targetStore.x || lastFrame.player.y !== targetStore.y) && steps < 100) {
        steps++;
        const curX = lastFrame.player.x;
        const curY = lastFrame.player.y;
        const dx = targetStore.x - curX;
        const dy = targetStore.y - curY;

        let key = null;
        if (Math.abs(dx) > Math.abs(dy)) {
            key = dx > 0 ? 'right' : 'left';
        } else {
            key = dy > 0 ? 'down' : 'up';
        }

        sendKey(key);
        await waitForFrame(500);

        if (lastFrame.player.x === curX && lastFrame.player.y === curY) {
            // Blocked, try alternate axis
            const altKey = (key === 'right' || key === 'left')
                ? (dy >= 0 ? 'down' : 'up')
                : (dx >= 0 ? 'right' : 'left');
            sendKey(altKey);
            await waitForFrame(500);
        }
    }

    console.log(`[ShopTest] Player now at (${lastFrame.player.x}, ${lastFrame.player.y}), target was (${targetStore.x}, ${targetStore.y})`);
    const playerFeat = parseInt(lastFrame.map.rows[lastFrame.player.y].f.substring(lastFrame.player.x * 2, lastFrame.player.x * 2 + 2), 16);
    console.log(`[ShopTest] Current player tile feat: ${playerFeat}, ui.overlay: ${lastFrame.ui.overlay}, awaiting_command: ${lastFrame.ui.awaiting_command}`);

    // Check term row 0 and 1
    const r0 = lastFrame.term.rows[0] ? lastFrame.term.rows[0].g : '';
    const r1 = lastFrame.term.rows[1] ? lastFrame.term.rows[1].g : '';
    console.log(`[ShopTest] Term row 0: "${r0}"`);
    console.log(`[ShopTest] Term row 1: "${r1}"`);

    // Now test sending '.' while on store tile
    console.log("[ShopTest] Sending '.' to enter store...");
    sendKey('.');
    await waitForFrame(1000);

    console.log(`[ShopTest] After '.': ui.overlay: ${lastFrame.ui.overlay}, awaiting_command: ${lastFrame.ui.awaiting_command}`);
    for (let r = 0; r < 5; r++) {
        console.log(`   Term row ${r}: "${lastFrame.term.rows[r] ? lastFrame.term.rows[r].g : ''}"`);
    }

    // Check messages after actions
    console.log(`[ShopTest] Total messages now: ${(lastFrame.messages || []).length}`);
    (lastFrame.messages || []).forEach((m, i) => console.log(`   msg[${i}]: "${m.text}" (count=${m.count}, attr=${m.attr})`));

    ws.close();
    process.exit(0);
}

run().catch(err => {
    console.error('[ShopTest Error]', err);
    process.exit(1);
});
