const { spawn } = require('child_process');
const path = require('path');
const WebSocket = require('../server/node_modules/ws');

async function main() {
    console.log('[Test] Starting local Angband3D server for verification...');
    const serverProcess = spawn('node', ['src/server.js'], {
        cwd: path.join(__dirname, '..', 'server'),
        env: { ...process.env, PORT: '8088' },
        stdio: 'inherit'
    });

    await new Promise(r => setTimeout(r, 2000));

    const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
    const port = 9225;
    const targetUrl = 'http://localhost:8088/?char=SfxHero_' + Date.now();

    console.log('[Test] Launching Chrome CDP on port ' + port + '...');
    const profileDir = path.join(__dirname, 'temp_sfx_profile_' + Date.now());
    const chrome = spawn(chromePath, [
        '--headless=new',
        `--remote-debugging-port=${port}`,
        '--window-size=1280,800',
        '--no-first-run',
        '--no-default-browser-check',
        `--user-data-dir=${profileDir}`
    ], { stdio: 'ignore' });

    let res = null;
    for (let attempt = 0; attempt < 10; attempt++) {
        try {
            res = await fetch(`http://127.0.0.1:${port}/json/new?${encodeURIComponent(targetUrl)}`, { method: 'PUT' });
            if (res.ok) break;
        } catch (e) {
            await new Promise(r => setTimeout(r, 500));
        }
    }
    if (!res) throw new Error('Could not connect to Chrome on port ' + port);
    const target = await res.json();
    try {
        const ws = new WebSocket(target.webSocketDebuggerUrl);
        let id = 1;
        const callbacks = new Map();

        await new Promise((resolve) => {
            ws.on('open', resolve);
        });

        function send(method, params = {}) {
            return new Promise((resolve) => {
                const reqId = id++;
                callbacks.set(reqId, resolve);
                ws.send(JSON.stringify({ id: reqId, method, params }));
            });
        }

        ws.on('message', (data) => {
            const msg = JSON.parse(data.toString());
            if (msg.id && callbacks.has(msg.id)) {
                const cb = callbacks.get(msg.id);
                callbacks.delete(msg.id);
                cb(msg.result);
            }
        });

        await send('Runtime.enable');
        await send('Page.enable');

        // Wait for connect
        await send('Runtime.evaluate', {
            expression: 'new Promise(resolve => { const check = () => { if (window.__app && window.__app.network && window.__app.network.connected) resolve(); else setTimeout(check, 100); }; check(); })',
            awaitPromise: true
        });
        console.log('[Test] Connected to web engine!');

        // Quick birth to reach town
        await send('Runtime.evaluate', { expression: 'document.getElementById("btn-quick-birth").click()' });

        // Wait for play phase in 3D
        await send('Runtime.evaluate', {
            expression: 'new Promise(resolve => { const check = () => { if (window.__app && window.__app.lastFrame && window.__app.lastFrame.phase === "play" && window.__app.lastFrame.map) resolve(); else setTimeout(check, 150); }; check(); })',
            awaitPromise: true
        });
        console.log('[Test] Reached 3D Dungeon world!');

        // 1. Test SoundEngine and SFX generation
        const soundTestResult = await send('Runtime.evaluate', {
            expression: `(() => {
                const audio = window.__app.audio;
                if (!audio) return { ok: false, error: 'No audio engine' };
                audio.init();
                const bufferKeys = Object.keys(audio.buffers);
                return {
                    ok: true,
                    sampleRate: audio.sampleRate,
                    bufferCount: bufferKeys.length,
                    bufferKeys,
                    stoneSteps: audio.stoneFootsteps.length,
                    outdoorSteps: audio.outdoorFootsteps.length
                };
            })()`,
            returnByValue: true
        });
        console.log('[Test] Audio Engine Status:', soundTestResult.result.value);

        // 2. Test Minimap Sizes and Zoom
        const minimapTestResult = await send('Runtime.evaluate', {
            expression: `(() => {
                const hud = window.__app.hud;
                const canvas = hud.minimapCanvas;
                const container = hud.minimapContainer;
                const initialSize = { w: canvas.width, h: canvas.height, cls: container.className };

                const results = [initialSize];

                // Cycle through all sizes: 0 -> 1 -> 2 -> 3 -> 4
                for (let i = 0; i < 4; i++) {
                    hud.cycleMinimapSize(1);
                    results.push({
                        idx: hud.minimapSizeIndex,
                        w: canvas.width,
                        h: canvas.height,
                        cls: container.className,
                        name: hud.minimapSizes[hud.minimapSizeIndex].name
                    });
                }

                // Test zoom
                hud.adjustMinimapZoom(-0.5); // Zoom out
                const zoomedOut = hud.minimapZoom;
                hud.adjustMinimapZoom(2.0); // Zoom in
                const zoomedIn = hud.minimapZoom;

                return {
                    sizes: results,
                    zoomedOut,
                    zoomedIn,
                    hint: document.getElementById('minimap-hint-text')?.textContent || null
                };
            })()`,
            returnByValue: true
        });
        if (minimapTestResult.exceptionDetails) {
            console.error('[Test] Minimap Error:', minimapTestResult.exceptionDetails);
        } else {
            console.log('[Test] Minimap Sizing & Zoom Status:', JSON.stringify(minimapTestResult.result.value, null, 2));
        }

        // 3. Test Tab Classic Mode and Escape Key Return
        const classicEscapeResult = await send('Runtime.evaluate', {
            expression: `(() => {
                const term = document.getElementById('terminal-container');
                const beforeTab = {
                    termHidden: term.classList.contains('hidden'),
                    isForceTerm: window.__app.isForceTerminal()
                };

                // Press Tab to enter classic mode
                window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
                const afterTab = {
                    termHidden: term.classList.contains('hidden'),
                    isForceTerm: window.__app.isForceTerminal()
                };

                // Press Escape in Tab classic mode to return to 3D
                window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
                const afterEscape = {
                    termHidden: term.classList.contains('hidden'),
                    isForceTerm: window.__app.isForceTerminal()
                };

                return { beforeTab, afterTab, afterEscape };
            })()`,
            returnByValue: true
        });
        console.log('[Test] Tab Classic Mode & Escape Key Return:', JSON.stringify(classicEscapeResult.result.value, null, 2));

        ws.close();
    } finally {
        chrome.kill();
        serverProcess.kill();
    }
}

main().catch(err => {
    console.error(err);
    process.exit(1);
});
