const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=MKeyTest_' + Math.random().toString(36).substring(2, 6));

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
                setTimeout(check, 30);
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
    while (!lastFrame) await new Promise(r => setTimeout(r, 50));

    // Fast-birth to play
    for (let i = 0; i < 20; i++) {
        if (lastFrame.phase === 'play' && lastFrame.map && lastFrame.player) break;
        const screenText = (lastFrame.term && lastFrame.term.rows ? lastFrame.term.rows.map(r => r.g || '').join('\n') : '').toLowerCase();
        if (screenText.includes("use as is") || screenText.includes("start over") || screenText.includes("r to reroll")) {
            sendKey('enter');
        } else if (lastFrame.ui && lastFrame.ui.more) {
            sendKey('enter');
        } else {
            sendKey('@');
        }
        await waitForFrame();
    }

    console.log('Class:', lastFrame.player.class);
    // Press 'm'
    sendKey('m');
    await waitForFrame();
    console.log('After m: UI:', JSON.stringify(lastFrame.ui));
    console.log('Term row 0:', lastFrame.term.rows[0].g);
    console.log('Term row 1:', lastFrame.term.rows[1].g);
    console.log('Term row 2:', lastFrame.term.rows[2].g);

    ws.close();
    process.exit(0);
}

run().catch(err => {
    console.error(err);
    process.exit(1);
});
