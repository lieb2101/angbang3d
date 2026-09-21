const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const WebSocket = require('../server/node_modules/ws');

async function main() {
    console.log('[Test] Starting local Angband3D server on port 8093...');
    const serverProcess = spawn('node', ['src/server.js'], {
        cwd: path.join(__dirname, '..', 'server'),
        env: { ...process.env, PORT: '8093' },
        stdio: 'inherit'
    });

    await new Promise(r => setTimeout(r, 2000));

    const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
    const port = 9228;
    const targetUrl = 'http://localhost:8093/?char=ChamferHero_' + Date.now();

    console.log('[Test] Launching Chrome CDP on port ' + port + '...');
    const profileDir = path.join(__dirname, 'temp_chamfer_profile_' + Date.now());
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

        await send('Page.enable');
        await send('Runtime.enable');

        async function evalJs(expr) {
            const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
            return r && r.result ? r.result.value : null;
        }

        console.log('[Test] Waiting for WebSocket connection in browser...');
        await evalJs(`
            new Promise(resolve => {
                const check = () => {
                    if (window.__app && window.__app.network && window.__app.network.connected) resolve(true);
                    else setTimeout(check, 100);
                };
                check();
            })
        `);

        console.log('[Test] Clicking quick birth...');
        await evalJs('document.getElementById("btn-quick-birth").click()');

        console.log('[Test] Waiting to enter 3D Town...');
        await evalJs(`
            new Promise(resolve => {
                const check = () => {
                    const term = document.getElementById("terminal-container");
                    if (term && term.classList.contains("hidden")) resolve(true);
                    else setTimeout(check, 300);
                };
                check();
            })
        `);

        await new Promise(r => setTimeout(r, 1500));

        // Test 1: Verify Chamfered Geometry & Normal Maps in active 3D world
        const verification1 = await evalJs(`
            (() => {
                const d = window.__app.dungeon;
                const geo = d.wallMesh ? d.wallMesh.geometry : null;
                const posCount = geo && geo.attributes && geo.attributes.position ? geo.attributes.position.count : 0;
                const hasNormals = !!(d.organicNormal && d.scaleNormal && d.chitinNormal && d.furNormal);
                const paletteCount = d.characterPalettes ? d.characterPalettes.size : 0;
                return {
                    posCount,
                    hasNormals,
                    paletteCount,
                    lastMapStored: !!d.lastMap
                };
            })()
        `);
        console.log('[Test 1] Geometry & Normal Map verification:', verification1);

        // Test 2: Walk to stairs down and enter Depth 1
        console.log('[Test 2] Finding stairs > down in Town...');
        const stairsPos = await evalJs(`
            (() => {
                const map = window.__app.lastFrame.map;
                const w = map.w || 66;
                const h = map.h || 22;
                for (let y = 0; y < h; y++) {
                    for (let x = 0; x < w; x++) {
                        const feat = window.__app.dungeon.getFeatAt(map, x, y);
                        if (feat === 6) return { x, y };
                    }
                }
                return null;
            })()
        `);
        console.log('[Test 2] Stairs located at:', stairsPos);

        if (stairsPos) {
            await evalJs(`
                (() => {
                    const p = window.__app.lastFrame.player;
                    const path = [];
                    let cx = p.x, cy = p.y;
                    while (cx !== ${stairsPos.x} || cy !== ${stairsPos.y}) {
                        if (cx < ${stairsPos.x}) { path.push('right'); cx++; }
                        else if (cx > ${stairsPos.x}) { path.push('left'); cx--; }
                        if (cy < ${stairsPos.y}) { path.push('down'); cy++; }
                        else if (cy > ${stairsPos.y}) { path.push('up'); cy--; }
                    }
                    window._path = path;
                })()
            `);
            const pathLen = await evalJs('window._path.length');
            for (let i = 0; i < pathLen; i++) {
                const step = await evalJs(`window._path[${i}]`);
                await evalJs(`window.__app.network.sendKey('${step}')`);
                await new Promise(r => setTimeout(r, 60));
            }
            await new Promise(r => setTimeout(r, 400));
            console.log('[Test 2] Stepping down stairs (>) ...');
            await evalJs("window.__app.network.sendKey('>')");
            await new Promise(r => setTimeout(r, 1500));
        }

        // Test 3: Subterranean Fog & Culling Verification
        const verification3 = await evalJs(`
            (() => {
                const frame = window.__app.lastFrame;
                const d = window.__app.dungeon;
                const depth = frame.player ? frame.player.depth : 0;
                const fogType = d.scene.fog ? d.scene.fog.constructor.name : 'none';
                let totalItems = 0;
                let visibleItems = 0;
                for (const entity of d.items.values()) {
                    totalItems++;
                    if (entity.visible) visibleItems++;
                }
                let totalMonsters = 0;
                let visibleMonsters = 0;
                for (const entity of d.monsters.values()) {
                    totalMonsters++;
                    if (entity.visible) visibleMonsters++;
                }
                const diagHint = document.getElementById('footer-diagonal-hint');
                return {
                    depth,
                    fogType,
                    fogNear: d.scene.fog && d.scene.fog.near,
                    fogFar: d.scene.fog && d.scene.fog.far,
                    totalItems,
                    visibleItems,
                    totalMonsters,
                    visibleMonsters,
                    diagHintVisible: diagHint && diagHint.style.display !== 'none',
                    diagHintText: diagHint ? diagHint.innerText : ''
                };
            })()
        `);
        console.log('[Test 3] Subterranean Fog & Culling State:', verification3);

        // Turn around and take a screenshot in dungeon
        await evalJs('window.__app.dungeon.turn(1)');
        await new Promise(r => setTimeout(r, 300));

        const shot = await send('Page.captureScreenshot', { format: 'png' });
        const outPath = path.join(__dirname, 'chamfer_and_void_capture.png');
        fs.writeFileSync(outPath, Buffer.from(shot.data, 'base64'));
        console.log('[Test] Saved screenshot to ' + outPath);

        ws.close();
    } finally {
        chrome.kill();
        serverProcess.kill();
        try { fs.rmSync(profileDir, { recursive: true, force: true }); } catch (e) {}
    }
}

main().catch(err => {
    console.error('[Test Error]:', err);
    process.exit(1);
});
