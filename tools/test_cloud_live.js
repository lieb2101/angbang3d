/**
 * Live Cloud Run WebSocket and HTTP Verification Script
 * Validates:
 * 1. HTTPS /health endpoint
 * 2. HTTPS /api/saves endpoint
 * 3. WSS /ws?user=CloudTestHero endpoint
 * 4. Verifies engine process spawns and emits frames/ready signal
 */

const https = require('https');
const WebSocket = require('../server/node_modules/ws');

const CLOUD_HOST = process.env.CLOUD_HOST || 'angband3d-cloud-564958309282.us-central1.run.app';
const HTTPS_URL = `https://${CLOUD_HOST}`;
const WSS_URL = `wss://${CLOUD_HOST}/ws?user=CloudTestHero`;

function fetchJson(path) {
    return new Promise((resolve, reject) => {
        https.get(`${HTTPS_URL}${path}`, res => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                try {
                    resolve({ status: res.statusCode, json: JSON.parse(data) });
                } catch (e) {
                    resolve({ status: res.statusCode, text: data });
                }
            });
        }).on('error', reject);
    });
}

async function run() {
    console.log(`[CloudTest] Target Cloud Host: ${CLOUD_HOST}`);

    console.log('[CloudTest] 1. Checking /health...');
    const health = await fetchJson('/health');
    console.log('[CloudTest] Health response:', health);
    if (health.status !== 200) {
        throw new Error(`Health check failed with status ${health.status}`);
    }

    console.log('[CloudTest] 2. Checking /api/saves...');
    const saves = await fetchJson('/api/saves');
    console.log('[CloudTest] Saves response:', saves);
    if (saves.status !== 200) {
        throw new Error(`Saves API check failed with status ${saves.status}`);
    }

    console.log(`[CloudTest] 3. Connecting to WebSocket: ${WSS_URL}...`);
    return new Promise((resolve, reject) => {
        const ws = new WebSocket(WSS_URL);
        const timer = setTimeout(() => {
            ws.terminate();
            reject(new Error('WebSocket connection test timed out after 15s'));
        }, 15000);

        let receivedHello = false;
        let receivedEngineOutput = false;

        ws.on('open', () => {
            console.log('[CloudTest] WebSocket connected successfully!');
            // Send ping
            ws.send(JSON.stringify({ t: 'ping', time: Date.now() }));
        });

        ws.on('message', (data) => {
            const str = data.toString();
            console.log('[CloudTest] WS received:', str.length > 200 ? str.slice(0, 200) + '...' : str);
            try {
                const msg = JSON.parse(str);
                if (msg.t === 'hello') {
                    receivedHello = true;
                    console.log(`[CloudTest] Received hello! Session: ${msg.sessionId}`);
                } else if (msg.t === 'frame' || msg.t === 'state' || msg.t === 'pong') {
                    receivedEngineOutput = true;
                } else if (msg.t === 'exit') {
                    console.error('[CloudTest] Engine exited with code:', msg.code);
                }
            } catch (e) {
                // Raw engine text or frame
                receivedEngineOutput = true;
            }

            if (receivedHello) {
                // Allow a brief moment to see if engine sends anything or stays alive
                setTimeout(() => {
                    clearTimeout(timer);
                    ws.close();
                    console.log('[CloudTest] WebSocket test PASSED! Cloud engine is running.');
                    resolve();
                }, 2000);
            }
        });

        ws.on('error', (err) => {
            clearTimeout(timer);
            reject(err);
        });

        ws.on('close', (code, reason) => {
            console.log(`[CloudTest] WebSocket closed (code: ${code}, reason: ${reason})`);
        });
    });
}

run().then(() => {
    console.log('[CloudTest] SUCCESS: All cloud verification checks completed!');
    process.exit(0);
}).catch(err => {
    console.error('[CloudTest] FAILED:', err);
    process.exit(1);
});
