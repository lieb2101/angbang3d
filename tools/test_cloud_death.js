const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=DeathTest_' + Math.random().toString(36).substring(2, 6));

let seq = 0;
let lastFrame = null;

function sendKey(key) {
    ws.send(`key ${key}`);
}

function waitForFrame(timeout = 3000) {
    const startSeq = seq;
    return new Promise((resolve, reject) => {
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

ws.on('open', async () => {
    console.log('[DeathTest] Connected to us-east1 WebSocket');
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
    console.log(`[DeathTest] In setup, seq: ${seq}`);

    // Quick birth
    sendKey('enter');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();
    sendKey('space');
    await waitForFrame();

    console.log(`[DeathTest] In game! Depth: ${lastFrame.player ? lastFrame.player.depth : 'none'}, Dead: ${lastFrame.player.dead}`);

    // Send Retire command ('Q', then 'y', then '@')
    sendKey('Q');
    await waitForFrame();
    sendKey('y');
    await waitForFrame();
    sendKey('@');
    await waitForFrame();

    console.log(`[DeathTest] After retire: Dead: ${lastFrame.player.dead}, More: ${lastFrame.ui.more}`);
    if (lastFrame.ui.more) {
        sendKey('space');
        await waitForFrame();
    }

    console.log(`[DeathTest] Tombstone frame: Dead=${lastFrame.player.dead}, Overlay=${lastFrame.ui.overlay}`);
    const screen = (lastFrame.term && lastFrame.term.rows) ? lastFrame.term.rows.map(r => r.g || '').join('\n') : '';
    console.log(`[DeathTest] Screen has Information? ${screen.includes('Information')}`);
    console.log(`[DeathTest] Screen has New Game? ${screen.includes('New Game')}`);
    console.log(`[DeathTest] Screen has Quit? ${screen.includes('Quit')}`);

    if (lastFrame.player.dead && screen.includes('Information')) {
        console.log('[DeathTest] SUCCESS: Death screen and menu correctly triggered on cloud instance!');
        ws.close();
        process.exit(0);
    } else {
        console.error('[DeathTest] FAILED: Missing death state or menu');
        ws.close();
        process.exit(1);
    }
}

run().catch(err => {
    console.error(err);
    process.exit(1);
});
