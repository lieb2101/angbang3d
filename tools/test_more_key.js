const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-central1.run.app/ws?user=MoreKeyTest');

let seq = 0;
let lastFrame = null;

function sendKey(key) {
    console.log(`>>> Sending: key ${key}`);
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
                setTimeout(check, 50);
            }
        };
        check();
    });
}

ws.on('message', (data) => {
    const msg = JSON.parse(data.toString());
    if (msg.t === 'frame') {
        seq = msg.seq;
        lastFrame = msg;
    }
});

async function run() {
    while (!lastFrame) await new Promise(r => setTimeout(r, 100));
    // Quick birth
    sendKey('enter'); await waitForFrame();
    sendKey('@'); await waitForFrame();
    sendKey('@'); await waitForFrame();
    sendKey('space'); await waitForFrame();

    let f = lastFrame;
    console.log(`Birth complete, depth=${f.player.depth}`);

    // Find staircase
    let sx = 0, sy = 0;
    for (let y = 0; y < f.map.h; y++) {
        for (let x = 0; x < f.map.w; x++) {
            const feat = parseInt(f.map.rows[y].f.substr(x * 2, 2), 16);
            if (feat === 6) { sx = x; sy = y; break; }
        }
    }

    // Walk to stair
    while (f.player.x !== sx || f.player.y !== sy) {
        let dx = sx - f.player.x;
        let dy = sy - f.player.y;
        if (dx > 0) sendKey('right');
        else if (dx < 0) sendKey('left');
        else if (dy > 0) sendKey('down');
        else if (dy < 0) sendKey('up');
        f = await waitForFrame();
    }

    console.log(`Standing on stairs (${f.player.x}, ${f.player.y}). Descending with >...`);
    sendKey('>');
    f = await waitForFrame();

    console.log(`Status right after >: depth=${f.player.depth}, ui.more=${f.ui && f.ui.more}, awaiting=${f.ui && f.ui.awaiting_command}`);
    console.log(`Term row 0: "${f.term.rows[0].g}"`);

    console.log('TEST 1: Sending key enter...');
    sendKey('enter');
    const fAfterEnter = await waitForFrame(2000);
    console.log(`After key enter: seq=${fAfterEnter.seq}, ui.more=${fAfterEnter.ui && fAfterEnter.ui.more}, awaiting=${fAfterEnter.ui && fAfterEnter.ui.awaiting_command}`);

    if (fAfterEnter.ui && fAfterEnter.ui.more) {
        console.log('TEST 2: key enter did NOT clear -more-! Now trying key space...');
        sendKey('space');
        const fAfterSpace = await waitForFrame(2000);
        console.log(`After key space: seq=${fAfterSpace.seq}, ui.more=${fAfterSpace.ui && fAfterSpace.ui.more}, awaiting=${fAfterSpace.ui && fAfterSpace.ui.awaiting_command}`);
    }

    ws.close();
    process.exit(0);
}

setTimeout(run, 500);
