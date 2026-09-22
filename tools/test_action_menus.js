const WebSocket = require('../server/node_modules/ws');

const ws = new WebSocket('wss://angband3d-cloud-564958309282.us-east1.run.app/ws?user=ActionMenuTest_' + Math.random().toString(36).substring(2, 6));

let seq = 0;
let lastFrame = null;

function sendKey(key) {
    ws.send(`key ${key}`);
}

function waitForFrame(predicate, timeout = 5000) {
    const start = Date.now();
    return new Promise((resolve, reject) => {
        const check = () => {
            if (lastFrame && predicate(lastFrame)) {
                resolve(lastFrame);
            } else if (Date.now() - start > timeout) {
                reject(new Error(`Timeout waiting for frame condition after ${timeout}ms. Last phase: ${lastFrame?.phase}, seq: ${seq}`));
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
    console.log('[Test] Connecting to Cloud WebSocket...');
    while (!lastFrame) await new Promise(r => setTimeout(r, 80));

    // Advance birth
    for (let step = 0; step < 25; step++) {
        const screenText = (lastFrame.term?.rows?.map(r => r.g || '').join('\n') || '').toLowerCase();
        if (screenText.includes('use as is') || screenText.includes('to start over') || screenText.includes('r to reroll')) {
            break;
        }
        if (lastFrame.ui?.more) sendKey('enter');
        else sendKey('@');
        await new Promise(r => setTimeout(r, 120));
    }

    sendKey('enter');
    await waitForFrame(f => f.phase === 'play' && f.map && f.player && f.ui?.awaiting_command);
    console.log(`[Test] 3D World Entered! Character: ${lastFrame.player.name}`);

    // Test 1: Inventory Pack ('i')
    console.log('[Test 1] Testing Inventory Pack (i)...');
    sendKey('i');
    await waitForFrame(f => f.ui && (f.ui.overlay || 0) > 0);
    const invRow0 = (lastFrame.term?.rows?.[0]?.g || '').trim();
    console.log(`  -> Prompt: "${invRow0}"`);
    if (!invRow0.toLowerCase().includes('inven:')) {
        throw new Error(`Inventory prompt missing "inven:": ${invRow0}`);
    }

    // Extract item pills
    const invItems = [];
    for (let r = 0; r < lastFrame.term.rows.length; r++) {
        const m = (lastFrame.term.rows[r].g || '').match(/\b([a-z])\)\s+([^\n\r]+)/);
        if (m) invItems.push({ letter: m[1], name: m[2].trim() });
    }
    console.log(`  -> Parsed ${invItems.length} inventory items:`, invItems.map(i => `[${i.letter}] ${i.name.slice(0, 20)}`).join(', '));
    if (invItems.length === 0) throw new Error('No items parsed from inventory screen');

    // Close menu with Esc
    sendKey('escape');
    await waitForFrame(f => f.ui && f.ui.awaiting_command);
    console.log('  -> Esc returned cleanly to 3D exploration');

    // Test 2: Throw Item ('v')
    console.log('[Test 2] Testing Throw Item (v)...');
    sendKey('v');
    await waitForFrame(f => f.ui && (f.ui.overlay || 0) > 0);
    const throwRow0 = (lastFrame.term?.rows?.[0]?.g || '').trim();
    console.log(`  -> Prompt: "${throwRow0}"`);
    if (!throwRow0.toLowerCase().includes('throw which item?')) {
        throw new Error(`Throw prompt missing: ${throwRow0}`);
    }

    // Close menu with Esc
    sendKey('escape');
    await waitForFrame(f => f.ui && f.ui.awaiting_command);
    console.log('  -> Esc returned cleanly to 3D exploration');

    // Test 3: Equipment Gear ('e')
    console.log('[Test 3] Testing Equipment Gear (e)...');
    sendKey('e');
    await waitForFrame(f => f.ui && (f.ui.overlay || 0) > 0);
    const equipRow0 = (lastFrame.term?.rows?.[0]?.g || '').trim();
    console.log(`  -> Prompt: "${equipRow0}"`);
    if (!equipRow0.toLowerCase().includes('equip:')) {
        throw new Error(`Equipment prompt missing "equip:": ${equipRow0}`);
    }

    // Close menu with Esc
    sendKey('escape');
    await waitForFrame(f => f.ui && f.ui.awaiting_command);
    console.log('  -> Esc returned cleanly to 3D exploration');

    console.log('\n[SUCCESS] All action menus verified seamlessly!');
    ws.close();
    process.exit(0);
}

run().catch(err => {
    console.error('[FAILED]', err);
    process.exit(1);
});
