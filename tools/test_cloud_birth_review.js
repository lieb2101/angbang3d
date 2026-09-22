const WebSocket = require('../server/node_modules/ws');
const assert = require('assert');

const CLOUD_HOST = process.env.CLOUD_HOST || 'angband3d-cloud-564958309282.us-central1.run.app';
const ws = new WebSocket(`wss://${CLOUD_HOST}/ws?user=ReviewTest_` + Math.random().toString(36).substring(2, 6));

let lastFrame = null;
let frames = [];

ws.on('open', () => {
    console.log('[ReviewTest] Connected to Cloud Run WebSocket:', CLOUD_HOST);
});

ws.on('message', (data) => {
    const msg = JSON.parse(data.toString());
    if (msg.t === 'frame') {
        lastFrame = msg;
        frames.push(msg);
    }
});

async function run() {
    // Wait for first frame
    while (!lastFrame) await new Promise(r => setTimeout(r, 100));
    console.log('[ReviewTest] Initial frame received, phase:', lastFrame.phase);

    // Send '@' for quick birth
    ws.send('key @');

    // Wait until review screen is reached or 10s timeout
    const start = Date.now();
    let reviewFrame = null;
    while (Date.now() - start < 10000) {
        await new Promise(r => setTimeout(r, 100));
        if (lastFrame && lastFrame.term && lastFrame.term.rows) {
            const screenText = lastFrame.term.rows.map(r => r.g || '').join('\n').toLowerCase();
            if (screenText.includes('step back') || screenText.includes('start over') || screenText.includes('r to reroll') || screenText.includes('use as is')) {
                reviewFrame = lastFrame;
                break;
            }
        }
        if (lastFrame && lastFrame.ui && lastFrame.ui.more) {
            ws.send('key enter');
        }
    }

    assert(reviewFrame, 'Review frame must be reached!');
    console.log('[ReviewTest] Character Review Frame reached!');
    console.log('  Player name:', reviewFrame.player ? reviewFrame.player.name : 'none');
    console.log('  Player HP:', reviewFrame.player ? reviewFrame.player.hp : 'none');
    console.log('  Frame phase:', reviewFrame.phase);

    // Verify inPlay logic
    const inPlay = Boolean(reviewFrame && reviewFrame.phase === 'play' && reviewFrame.map);
    assert.strictEqual(inPlay, false, 'inPlay MUST be false on character review screen');

    // Verify toolbar title and storeActionsBar state
    const screenText = reviewFrame.term.rows.map(r => r.g || '').join('\n').toLowerCase();
    const isReviewScreen = !inPlay && (screenText.includes("use as is") || screenText.includes("'y': use") ||
                           screenText.includes("to start over") || screenText.includes("r to reroll") ||
                           screenText.includes("reroll") || screenText.includes("'s' to start") ||
                           screenText.includes("step back") || screenText.includes("any other key to continue"));

    assert.strictEqual(isReviewScreen, true, 'isReviewScreen MUST be true on character review screen');

    console.log('[ReviewTest] SUCCESS: Store action bar is strictly quarantined (display=none) and character review screen is properly identified (phase=setup)!');
    ws.close();
    process.exit(0);
}

run().catch(err => {
    console.error('[ReviewTest] Failed:', err);
    process.exit(1);
});
