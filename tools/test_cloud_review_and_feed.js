const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=FeedReview_' + Math.random().toString(36).substring(2, 6));

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
    console.log(`[Test] Connected, phase: ${lastFrame.phase}`);

    // Advance birth step-by-step
    let reachedReview = false;
    for (let step = 0; step < 20; step++) {
        const screenText = (lastFrame.term && lastFrame.term.rows ? lastFrame.term.rows.map(r => r.g || '').join('\n') : '').toLowerCase();
        if (screenText.includes("use as is") || screenText.includes("start over") || screenText.includes("r to reroll")) {
            console.log(`[Test] Reached Character Review screen at step ${step}!`);
            reachedReview = true;
            break;
        }
        if (lastFrame.ui && lastFrame.ui.more) {
            sendKey('enter');
        } else {
            sendKey('@');
        }
        await waitForFrame();
    }

    if (!reachedReview) {
        console.error('[Test] FAILED: Did not reach character review screen');
        process.exit(1);
    }

    console.log('[Test] Confirming character birth with Enter...');
    sendKey('enter');
    await waitForFrame();

    console.log(`[Test] After confirmation -> phase: ${lastFrame.phase}, map: ${!!lastFrame.map}, player: ${!!lastFrame.player}`);
    if (lastFrame.phase !== 'play' || !lastFrame.map || !lastFrame.player) {
        console.error('[Test] FAILED: Did not transition to 3D play phase!');
        process.exit(1);
    }

    console.log(`[Test] SUCCESS! Character confirmed: ${lastFrame.player.name} (${lastFrame.player.race} ${lastFrame.player.class})`);
    console.log(`[Test] Location: Town, Depth: ${lastFrame.player.depth}, HP: ${lastFrame.player.hp}/${lastFrame.player.hp_max}`);
    
    // Check messages
    if (lastFrame.messages && lastFrame.messages.length > 0) {
        console.log(`[Test] System Action Messages received (${lastFrame.messages.length} messages):`);
        lastFrame.messages.forEach((m, idx) => {
            console.log(`   [${idx}] "${m.text}" (count=${m.count}, attr=${m.attr})`);
        });
    }

    ws.close();
    process.exit(0);
}

run().catch(err => {
    console.error('[Test Error]', err);
    process.exit(1);
});
