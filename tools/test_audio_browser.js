const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const WebSocket = require('../server/node_modules/ws');

async function testAudioBrowser() {
    const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
    const port = 9227;
    const profileDir = path.join(__dirname, 'temp_audio_test_' + Date.now());

    console.log('[Test] Launching headless Chrome on port ' + port + '...');
    const chrome = spawn(chromePath, [
        '--headless=new',
        '--remote-debugging-port=' + port,
        '--no-first-run',
        '--no-default-browser-check',
        '--user-data-dir=' + profileDir,
        'about:blank'
    ], { stdio: 'ignore' });

    let res = null;
    for (let i = 0; i < 15; i++) {
        try {
            res = await fetch(`http://127.0.0.1:${port}/json/version`);
            if (res.ok) break;
        } catch (e) {
            await new Promise(r => setTimeout(r, 400));
        }
    }
    if (!res) throw new Error('Could not connect to Chrome on port ' + port);
    const ver = await res.json();
    console.log('[Test] Chrome connected:', ver.Browser);

    const targetRes = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json();
    const ws = new WebSocket(targetRes.webSocketDebuggerUrl);
    await new Promise(r => ws.on('open', r));

    let msgId = 1;
    const callbacks = new Map();
    ws.on('message', (d) => {
        const m = JSON.parse(d.toString());
        if (m.id && callbacks.has(m.id)) {
            const cb = callbacks.get(m.id);
            callbacks.delete(m.id);
            cb(m.result);
        }
    });

    function call(method, params = {}) {
        return new Promise(resolve => {
            const id = msgId++;
            callbacks.set(id, resolve);
            ws.send(JSON.stringify({ id, method, params }));
        });
    }

    await call('Runtime.enable');
    const audioJs = fs.readFileSync(path.join(__dirname, '../server/public/js/audio.js'), 'utf8');

    console.log('[Test] Injecting audio.js into browser context...');
    await call('Runtime.evaluate', { expression: audioJs });

    console.log('[Test] Instantiating SoundEngine and evaluating in Chrome Web Audio context...');
    const evalRes = await call('Runtime.evaluate', {
        expression: `(() => {
            const engine = new window.SoundEngine();
            engine.init();
            engine.unlock();
            const keys = Object.keys(engine.buffers);
            const tests = [
                () => engine.playWallBump(),
                () => engine.playShieldBlock(0.2),
                () => engine.playArmorDeflect(-0.2),
                () => engine.playEquipWeapon(),
                () => engine.playEquipArmor(),
                () => engine.playItemDrop(),
                () => engine.playEat(),
                () => engine.playChest(),
                () => engine.playTrapDisarm(),
                () => engine.playTrapTrigger(),
                () => engine.playTeleport(),
                () => engine.playPoison(),
                () => engine.playConfused(),
                () => engine.playBlind(),
                () => engine.playParalyzed(),
                () => engine.playAfraid(),
                () => engine.playHunger(),
                () => engine.playSpell('fire'),
                () => engine.playSpell('cold'),
                () => engine.playSpell('lightning'),
                () => engine.playSpell('poison'),
                () => engine.playMonsterVocal('C', 'grunt', 0.4),
                () => engine.playMonsterVocal('J', 'grunt', -0.4),
                () => engine.playMonsterVocal('G', 'grunt', 0.1),
                () => engine.playMonsterVocal('D', 'grunt', -0.8),
                () => engine.playMonsterVocal('r', 'grunt', 0.0),
                () => engine.playMonsterVocal('s', 'grunt', 0.5),
                () => engine.playMonsterDeath('D', -0.5),
                () => engine.playGoldPickup(),
                () => engine.playQuaff(),
                () => engine.playScroll()
            ];
            let executed = 0;
            for (const t of tests) { t(); executed++; }
            return {
                sampleRate: engine.sampleRate,
                bufferCount: keys.length,
                hasCompressor: !!engine.masterCompressor,
                hasMasterGain: !!engine.masterGain,
                stoneSteps: engine.stoneFootsteps.length,
                outdoorSteps: engine.outdoorFootsteps.length,
                executedTests: executed,
                keys: keys
            };
        })()`,
        returnByValue: true
    });

    console.log('[Test] Result from Chrome Web Audio:', JSON.stringify(evalRes.result.value, null, 2));

    ws.close();
    chrome.kill();
    try { fs.rmSync(profileDir, { recursive: true, force: true }); } catch (e) {}
}

testAudioBrowser().catch(err => {
    console.error('[Test] Error:', err);
    process.exit(1);
});
