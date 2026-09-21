const { spawn } = require('child_process');
const path = require('path');
const WebSocket = require('../server/node_modules/ws');

async function main() {
    const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
    const port = 9223;
    const targetUrl = 'https://angband3d-cloud-564958309282.us-central1.run.app';

    console.log('Launching Chrome...');
    const chrome = spawn(chromePath, [
        '--headless=new',
        `--remote-debugging-port=${port}`,
        '--window-size=1280,800',
        '--no-first-run',
        '--no-default-browser-check',
        '--user-data-dir=' + path.join(__dirname, 'temp_chrome_profile2')
    ], { stdio: 'ignore' });

    await new Promise(r => setTimeout(r, 1500));

    try {
        const res = await fetch(`http://127.0.0.1:${port}/json/new?${encodeURIComponent(targetUrl)}`, { method: 'PUT' });
        const target = await res.json();
        const ws = new WebSocket(target.webSocketDebuggerUrl);
        let id = 1;
        const callbacks = new Map();

        function send(method, params = {}) {
            return new Promise((resolve) => {
                const reqId = id++;
                callbacks.set(reqId, resolve);
                ws.send(JSON.stringify({ id: reqId, method, params }));
            });
        }

        ws.on('open', async () => {
            ws.on('message', (data) => {
                const msg = JSON.parse(data.toString());
                if (msg.id && callbacks.has(msg.id)) {
                    const cb = callbacks.get(msg.id);
                    callbacks.delete(msg.id);
                    cb(msg.result);
                } else if (msg.method === 'Runtime.consoleAPICalled') {
                    const args = msg.params.args.map(a => a.value || JSON.stringify(a)).join(' ');
                    console.log(`[Browser Console ${msg.params.type}]`, args);
                } else if (msg.method === 'Runtime.exceptionThrown') {
                    console.error(`[Browser Exception]`, msg.params.exceptionDetails.text, msg.params.exceptionDetails.exception);
                } else if (msg.method === 'Network.responseReceived') {
                    if (msg.params.response.status >= 400) {
                        console.log(`[HTTP ERROR] ${msg.params.response.status} ${msg.params.response.url}`);
                    }
                }
            });

            await send('Runtime.enable');
            await send('Page.enable');
            await send('Network.enable');

            console.log('Waiting 5s for page and assets to load...');
            await new Promise(r => setTimeout(r, 5000));

            const evalRes = await send('Runtime.evaluate', {
                expression: `(() => {
                    const d = window.__app ? window.__app.dungeon : null;
                    if (!d) return { err: "no dungeon" };
                    return {
                        hasGLTFLoader: Boolean(window.THREE && window.THREE.GLTFLoader),
                        hasOBJLoader: Boolean(window.THREE && window.THREE.OBJLoader),
                        monsterTemplates: Array.from(d.monsterTemplates.keys()),
                        weaponCache: Array.from(d.weaponCache.keys()),
                        currentRightWeapon: d.currentRightWeapon,
                        wallTexLoaded: Boolean(d.wallTex),
                        floorTexLoaded: Boolean(d.floorTex)
                    };
                })()`,
                returnByValue: true
            });

            console.log('Dungeon state:', JSON.stringify(evalRes.result.value, null, 2));

            ws.close();
            chrome.kill();
            process.exit(0);
        });
    } catch (e) {
        console.error('Error:', e);
        chrome.kill();
        process.exit(1);
    }
}

main();
