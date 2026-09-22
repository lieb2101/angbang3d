const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=MsgTest_' + Math.random().toString(36).substring(2, 6));

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

ws.on('open', () => console.log('[MsgTest] Connected'));
ws.on('message', (d) => {
    const m = JSON.parse(d.toString());
    if (m.t === 'frame') {
        seq = m.seq;
        lastFrame = m;
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
        if (lastFrame.ui && lastFrame.ui.more) sendKey('enter');
        else sendKey('@');
        await waitForFrame();
    }
    sendKey('enter');
    await waitForFrame();

    console.log(`[MsgTest] In town at (${lastFrame.player.x}, ${lastFrame.player.y})`);
    
    // Action 1: Walk into building wall (bump into wall to generate message)
    // Keep walking up until we hit town wall (py reaches 0 or hits wall)
    for (let i = 0; i < 10; i++) {
        sendKey('up');
        await waitForFrame(300);
    }
    console.log(`[MsgTest] After bumping up: at (${lastFrame.player.x}, ${lastFrame.player.y})`);
    console.log(`   Term row 0: "${lastFrame.term.rows[0].g}"`);
    console.log(`   Messages count: ${(lastFrame.messages || []).length}`);
    (lastFrame.messages || []).slice(-5).forEach((m, idx) => {
        console.log(`   msg[${idx}]: "${m.text}" count=${m.count} attr=${m.attr}`);
    });

    // Action 2: Rest 5 turns (key '5' or 'R' then '5')
    console.log("[MsgTest] Resting...");
    sendKey('5');
    await waitForFrame(300);
    console.log(`[MsgTest] After rest: Term row 0: "${lastFrame.term.rows[0].g}"`);
    console.log(`   Messages count: ${(lastFrame.messages || []).length}`);
    (lastFrame.messages || []).slice(-5).forEach((m, idx) => {
        console.log(`   msg[${idx}]: "${m.text}" count=${m.count} attr=${m.attr}`);
    });

    // Action 3: Take stairs down to dungeon Level 1 (stairs are at '>')
    // Find stairs in town map
    let stair = null;
    for (let y = 0; y < lastFrame.map.h; y++) {
        for (let x = 0; x < lastFrame.map.w; x++) {
            if (lastFrame.map.rows[y].g[x] === '>') stair = { x, y };
        }
    }
    console.log(`[MsgTest] Down stairs at:`, stair);

    ws.close();
    process.exit(0);
}

run().catch(err => {
    console.error(err);
    process.exit(1);
});
