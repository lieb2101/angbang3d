/**
 * End-to-End Live Gameplay Verification Script
 * Validates:
 * 1. Connects to WSS endpoint on Cloud Run.
 * 2. Receives hello & initial term frame (splash screen).
 * 3. Sends Enter -> '@' -> Enter (Quick-start random hero).
 * 4. Confirms phase transitions to 'play'.
 * 5. Verifies 3D dungeon map, player stats, and terminal clearing.
 */

const WebSocket = require('../server/node_modules/ws');

const CLOUD_HOST = process.env.CLOUD_HOST || 'angband3d-cloud-564958309282.us-central1.run.app';
const WSS_URL = `wss://${CLOUD_HOST}/ws?user=E2ETestHero_${Date.now()}`;

console.log(`[E2ETest] Connecting to ${WSS_URL}...`);
const ws = new WebSocket(WSS_URL);

let step = 0;
const timeout = setTimeout(() => {
    console.error('[E2ETest] Test timed out after 25 seconds');
    process.exit(1);
}, 25000);

ws.on('open', () => {
    console.log('[E2ETest] WebSocket connected!');
});

ws.on('message', (data) => {
    const str = data.toString().trim();
    if (!str) return;

    try {
        const msg = JSON.parse(str);
        if (msg.t === 'hello') {
            console.log(`[E2ETest] Handshake OK, session: ${msg.sessionId}`);
            return;
        }

        if (msg.t === 'frame') {
            const phase = msg.phase || 'unknown';
            console.log(`[E2ETest] Frame received — phase: ${phase}, term rows: ${msg.term ? msg.term.rows.length : 0}, has map: ${!!msg.map}`);

            if (step === 0) {
                step = 1;
                console.log('[E2ETest] Step 1: Sending enter to clear splash screen...');
                ws.send('key enter');
                setTimeout(() => {
                    console.log('[E2ETest] Step 2: Sending @ for Quick Start Random Hero...');
                    ws.send('key @');
                }, 400);
                setTimeout(() => {
                    console.log('[E2ETest] Step 3: Sending enter to confirm...');
                    ws.send('key enter');
                }, 800);
            }

            if (phase === 'play' && msg.map && msg.player) {
                console.log('---------------------------------------------------------');
                console.log(`[E2ETest] SUCCESS! Hero entered the 3D Dungeon!`);
                console.log(`[E2ETest] Player Name: ${msg.player.name}`);
                console.log(`[E2ETest] Class/Race: ${msg.player.race || ''} ${msg.player.class || ''}`);
                console.log(`[E2ETest] HP: ${msg.player.chp}/${msg.player.mhp}, Level: ${msg.player.lev}, Depth: ${msg.player.depth}`);
                console.log(`[E2ETest] Map dimensions: ${msg.map.width}x${msg.map.height}`);
                console.log('---------------------------------------------------------');
                clearTimeout(timeout);
                ws.close();
                process.exit(0);
            }
        }
    } catch (e) {
        // Non-JSON line
    }
});

ws.on('error', (err) => {
    console.error('[E2ETest] WebSocket error:', err.message);
});
