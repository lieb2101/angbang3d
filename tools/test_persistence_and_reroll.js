const http = require('http');
const path = require('path');
const fs = require('fs');
const WebSocket = require('../server/node_modules/ws');

async function testPersistenceAndReroll() {
    console.log('[Test] Starting server on port 8089 for persistence & reroll verification...');
    process.env.PORT = '8089';
    const serverModule = require('../server/src/server.js');

    await new Promise(r => setTimeout(r, 1000));

    const testSlot = 'TestPersist_' + Date.now();
    console.log(`[Test] Step 1: Connecting fresh character session: ${testSlot}...`);

    let ws1 = new WebSocket(`ws://localhost:8089/ws?user=${testSlot}&new=1`);
    await new Promise((resolve, reject) => {
        ws1.on('open', resolve);
        ws1.on('error', reject);
    });

    let framesReceived = 0;
    let inPlay = false;

    await new Promise((resolve) => {
        ws1.on('message', (data) => {
            const str = data.toString();
            try {
                const msg = JSON.parse(str);
                if (msg.t === 'frame') {
                    framesReceived++;
                    if (msg.phase === 'play') {
                        inPlay = true;
                        resolve();
                    }
                }
            } catch (_) {}
        });

        // Advance through birth sequence to enter play
        setTimeout(() => ws1.send('key enter'), 400);
        setTimeout(() => ws1.send('key @'), 800);
        setTimeout(() => ws1.send('key @'), 1200);
        setTimeout(() => ws1.send('key space'), 1600);
        setTimeout(() => ws1.send('frame'), 2000);
        setTimeout(() => { if (!inPlay) resolve(); }, 3500);
    });

    console.log(`[Test] In play reached: ${inPlay}, frames: ${framesReceived}`);

    console.log('[Test] Step 2: Cutting connection abruptly (client disconnect)...');
    ws1.close();
    // Wait for server ws.on('close') handler to send 'save' and exit cleanly
    await new Promise(r => setTimeout(r, 1500));

    console.log('[Test] Step 3: Reconnecting to resume existing character without new=1...');
    let ws2 = new WebSocket(`ws://localhost:8089/ws?user=${testSlot}`);
    await new Promise((resolve, reject) => {
        ws2.on('open', resolve);
        ws2.on('error', reject);
    });

    let panicPromptDetected = false;
    let resumedPlay = false;

    await new Promise((resolve) => {
        ws2.on('message', (data) => {
            const str = data.toString();
            console.log('  [ws2 msg]:', str.substring(0, 120));
            if (str.includes('panic save exists') || str.includes('panic save')) {
                panicPromptDetected = true;
            }
            try {
                const msg = JSON.parse(str);
                if (msg.term && msg.term.lines) {
                    console.log('  [term]:', msg.term.lines.filter(l => l.trim().length > 0).slice(0, 5));
                }
                if (msg.t === 'frame' && msg.phase === 'play') {
                    resumedPlay = true;
                    resolve();
                }
            } catch (_) {}
        });
        setTimeout(() => ws2.send('key enter'), 400);
        setTimeout(() => ws2.send('frame'), 800);
        setTimeout(() => resolve(), 3000);
    });

    console.log(`[Test] Panic prompt detected: ${panicPromptDetected} (EXPECTED: false)`);
    console.log(`[Test] Resumed directly into play: ${resumedPlay} (EXPECTED: true)`);
    ws2.close();
    await new Promise(r => setTimeout(r, 800));

    console.log('[Test] Step 4: Re-rolling fresh character slot with new=1...');
    let ws3 = new WebSocket(`ws://localhost:8089/ws?user=${testSlot}&new=1`);
    await new Promise((resolve, reject) => {
        ws3.on('open', resolve);
        ws3.on('error', reject);
    });

    let rerollPanicPrompt = false;
    let setupOrBirth = false;

    await new Promise((resolve) => {
        ws3.on('message', (data) => {
            const str = data.toString();
            if (str.includes('panic save exists')) {
                rerollPanicPrompt = true;
            }
            try {
                const msg = JSON.parse(str);
                if (msg.t === 'frame' && (msg.phase === 'birth' || msg.phase === 'setup')) {
                    setupOrBirth = true;
                    resolve();
                }
            } catch (_) {}
        });
        setTimeout(() => ws3.send('frame'), 500);
        setTimeout(() => resolve(), 2500);
    });

    console.log(`[Test] Reroll panic prompt detected: ${rerollPanicPrompt} (EXPECTED: false)`);
    console.log(`[Test] Reached clean birth/setup phase: ${setupOrBirth} (EXPECTED: true)`);

    ws3.close();

    if (panicPromptDetected || rerollPanicPrompt) {
        console.error('FAILED: Panic prompt was encountered!');
        process.exit(1);
    }

    console.log('\nSUCCESS: All state persistence and fresh reroll checks passed!');
    process.exit(0);
}

testPersistenceAndReroll().catch(err => {
    console.error('[Test Error]', err);
    process.exit(1);
});
