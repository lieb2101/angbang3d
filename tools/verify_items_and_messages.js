const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=ItemMessageVerify_' + Math.random().toString(36).substring(2, 6));

let seq = 0;
let lastFrame = null;

function sendKey(key) {
    ws.send(`key ${key}`);
}

function waitForFrame(timeout = 3000) {
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
    console.log('[Test] Connected to Live Cloud Run WebSocket');
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

    // Birth
    sendKey('enter');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('space');
    await waitForFrame();

    console.log(`[Test] Birth complete. Phase: ${lastFrame.phase}, Depth: ${lastFrame.player ? lastFrame.player.depth : 'N/A'}`);

    // Walk down into the dungeon
    sendKey('>');
    await waitForFrame();
    if (lastFrame.ui && lastFrame.ui.more) {
        sendKey('space');
        await waitForFrame();
    }

    console.log(`[Test] In Dungeon DL: ${lastFrame.player ? lastFrame.player.depth : 'N/A'}`);

    // Inspect items and messages
    console.log('[Test] Items in current level:', lastFrame.items ? lastFrame.items.length : 0);
    if (lastFrame.items && lastFrame.items.length > 0) {
        lastFrame.items.slice(0, 10).forEach(it => {
            console.log(`   - Item: "${it.name}" (glyph: '${it.glyph}', attr: ${it.attr}) at (${it.x}, ${it.y})`);
        });
    }

    // Take several steps to trigger turns and messages
    for (let i = 0; i < 5; i++) {
        sendKey('5'); // rest a turn
        await waitForFrame();
    }

    console.log(`[Test] Final turn: ${lastFrame.player ? lastFrame.player.turn : 'N/A'}`);
    if (lastFrame.messages) {
        console.log('[Test] Engine Messages received:', lastFrame.messages.map(m => m.text));
    }

    ws.close();
    process.exit(0);
}

run().catch(err => {
    console.error(err);
    process.exit(1);
});
