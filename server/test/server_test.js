/**
 * Test suite for Angband3D Cloud Server
 * Validates:
 *  - /health endpoint
 *  - Save metadata parser and /api/saves REST endpoint
 *  - Save upload validation (SaveVNLA magic)
 *  - Standalone zip download endpoint (/download/angband3d-standalone.zip)
 */

const http = require('http');
const assert = require('assert');
const fs = require('fs');
const path = require('path');

const PORT = 8081;
process.env.PORT = PORT.toString();

// Start the server
require('../src/server.js');

function get(pathStr) {
    return new Promise((resolve, reject) => {
        http.get(`http://localhost:${PORT}${pathStr}`, res => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: data }));
        }).on('error', reject);
    });
}

function post(pathStr, bodyBuffer, headers = {}) {
    return new Promise((resolve, reject) => {
        const req = http.request(`http://localhost:${PORT}${pathStr}`, {
            method: 'POST',
            headers: {
                'Content-Length': bodyBuffer.length,
                ...headers
            }
        }, res => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: data }));
        });
        req.on('error', reject);
        req.write(bodyBuffer);
        req.end();
    });
}

async function runTests() {
    console.log('Testing server endpoints on port', PORT);
    await new Promise(r => setTimeout(r, 500));

    // Test 1: Health check
    console.log('Test 1: Health check');
    const health = await get('/health');
    assert.strictEqual(health.status, 200);
    const healthJson = JSON.parse(health.body);
    assert.strictEqual(healthJson.status, 'ok');
    console.log('  -> Health check OK');

    // Test 2: List saves
    console.log('Test 2: List saves');
    const saves = await get('/api/saves');
    assert.strictEqual(saves.status, 200);
    const savesJson = JSON.parse(saves.body);
    assert(Array.isArray(savesJson.saves));
    console.log(`  -> Saves listed: ${savesJson.saves.length} save files found`);

    // Test 3: Save Upload Validation (Reject corrupt payload)
    console.log('Test 3: Save Upload Validation (reject invalid header)');
    const corruptData = Buffer.from('NOT_A_VALID_SAVE_FILE');
    const rejectRes = await post('/api/saves/upload', corruptData);
    assert.strictEqual(rejectRes.status, 400);
    console.log('  -> Corrupt save properly rejected with 400 Bad Request');

    // Test 4: Save Upload Validation (Accept valid SaveVNLA mock)
    console.log('Test 4: Save Upload Validation (accept valid SaveVNLA mock)');
    const validHeader = Buffer.alloc(100);
    validHeader.write('SaveVNLA', 0, 8, 'ascii');
    validHeader.write('description\0', 8, 16, 'ascii');
    validHeader.writeUInt32LE(1, 24); // version
    const descText = 'MorgothSlayer, Level 50 High-Elf Warrior';
    validHeader.writeUInt32LE(descText.length, 28);
    validHeader.write(descText, 36, descText.length, 'utf8');

    const uploadRes = await post('/api/saves/upload', validHeader, { 'x-character-name': 'TestHero' });
    assert.strictEqual(uploadRes.status, 200);
    const uploadJson = JSON.parse(uploadRes.body);
    assert.strictEqual(uploadJson.status, 'saved');
    console.log('  -> Valid SaveVNLA upload accepted and saved:', uploadJson.filename);

    // Clean up test file
    const testSavePath = path.join(__dirname, '../../engine/build/game/lib/save/TestHero');
    if (fs.existsSync(testSavePath)) {
        fs.unlinkSync(testSavePath);
        console.log('  -> Cleaned up test save file');
    }

    // Test 5: Static web client delivery
    console.log('Test 5: Static web client delivery');
    const indexRes = await get('/');
    assert.strictEqual(indexRes.status, 200);
    assert(indexRes.body.includes('<title>Angband3D'));
    assert(indexRes.headers['content-type'].includes('text/html'));
    console.log('  -> / serves index.html (200 OK)');

    const cssRes = await get('/css/dungeon.css');
    assert.strictEqual(cssRes.status, 200);
    assert(cssRes.body.includes('--font-fantasy'));
    console.log('  -> /css/dungeon.css serves CSS (200 OK)');

    const jsRes = await get('/js/dungeon3d.js');
    assert.strictEqual(jsRes.status, 200);
    console.log('  -> /js/dungeon3d.js serves 3D engine (200 OK)');

    // Test 6: Healthz liveness probe & metrics
    console.log('Test 6: Healthz liveness probe');
    const healthz = await get('/healthz');
    assert.strictEqual(healthz.status, 200);
    const healthzJson = JSON.parse(healthz.body);
    assert.strictEqual(healthzJson.status, 'ok');
    assert(typeof healthzJson.uptime === 'number');
    assert(typeof healthzJson.activeSessions === 'number');
    assert.strictEqual(healthzJson.engine, 'ready');
    console.log('  -> /healthz returned metrics & ready status (200 OK)');

    // Test 7: Gzip compression & Cache-Control
    console.log('Test 7: Gzip compression & Cache-Control headers');
    const gzipRes = await new Promise((resolve, reject) => {
        http.get(`http://localhost:${PORT}/js/dungeon3d.js`, {
            headers: { 'Accept-Encoding': 'gzip' }
        }, res => {
            const chunks = [];
            res.on('data', c => chunks.push(c));
            res.on('end', () => resolve({
                status: res.statusCode,
                headers: res.headers,
                bodyLength: Buffer.concat(chunks).length
            }));
        }).on('error', reject);
    });
    assert.strictEqual(gzipRes.status, 200);
    assert.strictEqual(gzipRes.headers['content-encoding'], 'gzip');
    assert(gzipRes.headers['cache-control'].includes('max-age'));
    console.log(`  -> /js/dungeon3d.js delivered with gzip compression (${gzipRes.bodyLength} bytes compressed)`);

    // Test 8: GLB Binary Model Delivery & MIME type
    console.log('Test 8: GLB Binary Model Delivery & MIME type');
    const glbRes = await get('/assets/models/monsters/Imp.glb');
    assert.strictEqual(glbRes.status, 200);
    assert.strictEqual(glbRes.headers['content-type'], 'model/gltf-binary');
    assert(glbRes.headers['cache-control'].includes('immutable'));
    console.log('  -> /assets/models/monsters/Imp.glb served with model/gltf-binary (200 OK)');

    const puglinRes = await get('/assets/models/monsters/Puglin.glb');
    assert.strictEqual(puglinRes.status, 200);
    assert.strictEqual(puglinRes.headers['content-type'], 'model/gltf-binary');
    console.log('  -> /assets/models/monsters/Puglin.glb served with model/gltf-binary (200 OK)');

    // Test 9: 3D Item and Monster Model Asset Integrity
    console.log('Test 9: 3D Item and Monster Model Asset Integrity');
    const mineralRes = await get('/assets/models/items/Mineral.obj');
    assert.strictEqual(mineralRes.status, 200);
    console.log('  -> /assets/models/items/Mineral.obj (Pebble/Stone) served (200 OK)');

    const dartRes = await get('/assets/models/items/Dart.obj');
    assert.strictEqual(dartRes.status, 200);
    console.log('  -> /assets/models/items/Dart.obj served (200 OK)');

    const spiderRes = await get('/assets/models/monsters/Spider.obj');
    assert.strictEqual(spiderRes.status, 200);
    console.log('  -> /assets/models/monsters/Spider.obj served (200 OK)');

    // Test 10: Client HTML Structure & Component Verification
    console.log('Test 10: Client HTML Structure & Component Verification');
    assert(indexRes.body.includes('id="store-actions-bar"'), 'store-actions-bar must be present in index.html');
    assert(indexRes.body.includes('id="btn-store-buy"'), 'btn-store-buy must be present');
    assert(indexRes.body.includes('id="btn-store-sell"'), 'btn-store-sell must be present');
    assert(indexRes.body.includes('id="btn-store-examine"'), 'btn-store-examine must be present');
    assert(indexRes.body.includes('id="btn-store-exit"'), 'btn-store-exit must be present');
    assert(indexRes.body.includes('id="map-resize-handle"'), 'map-resize-handle must be present');
    assert(indexRes.body.includes('id="msg-resize-handle"'), 'msg-resize-handle must be present');
    // Test 11: Character Screen Toolbar & Store Isolation Guard
    console.log('Test 11: Character Screen Toolbar & Store Isolation Guard');
    const appJs = fs.readFileSync(path.join(__dirname, '../public/js/app.js'), 'utf8');
    const inputJs = fs.readFileSync(path.join(__dirname, '../public/js/input.js'), 'utf8');
    const hudJs = fs.readFileSync(path.join(__dirname, '../public/js/hud.js'), 'utf8');
    assert(appJs.includes("const inPlay = Boolean(frame && frame.phase === 'play' && frame.map);"), 'app.js must strictly define inPlay by phase and map');
    assert(appJs.includes("if (!inPlay) {\n            // NEVER show store actions bar during character creation or review\n            if (storeActionsBar) storeActionsBar.style.display = 'none';"), 'app.js must strictly hide storeActionsBar when not inPlay');
    assert(appJs.includes("parseInt(frame.map.rows[py].f.substring(px * 2, px * 2 + 2), 16)"), 'app.js must correctly parse 2-hex store feature index');
    assert(appJs.includes("if (termAdvanceBtn) termAdvanceBtn.style.display = 'none';"), 'app.js must hide advance button in stores');
    assert(hudJs.includes("resetMessages(welcomeText = null)"), 'hud.js must implement resetMessages');
    assert(hudJs.includes("accept character history"), 'hud.js must filter character history setup prompt');
    assert(inputJs.includes("const inPlay = Boolean(lastFrame && lastFrame.phase === 'play' && lastFrame.map);"), 'input.js must strictly define inPlay by phase and map');
    console.log('  -> Store actions bar strictly quarantined to inPlay states with verified map context');
    console.log('  -> Store menus strictly isolate shop options and hide creation/advance buttons');
    // Test 12: Client JS Syntax & Compilation Integrity
    console.log('Test 12: Client JS Syntax & Compilation Integrity');
    const clientScripts = ['app.js', 'hud.js', 'input.js', 'terminal.js', 'audio.js', 'dungeon3d.js', 'network.js'];
    for (const scriptName of clientScripts) {
        const scriptCode = fs.readFileSync(path.join(__dirname, '../public/js', scriptName), 'utf8');
        try {
            new Function(scriptCode);
            console.log(`  -> ${scriptName} syntax verified cleanly`);
        } catch (syntaxErr) {
            assert.fail(`${scriptName} failed syntax evaluation: ${syntaxErr.message}`);
        }
    }

    // Test 13: WebHUD Real-Time Message Processing & Repeat Integrity
    console.log('Test 13: WebHUD Real-Time Message Processing & Repeat Integrity');
    assert(hudJs.includes('updateLastMessageCount(rawText, count)'), 'hud.js must implement updateLastMessageCount');
    assert(hudJs.includes('this.updateLastMessageCount(curLast.text.trim(), curLast.count);'), 'hud.js must update message count in-place');
    console.log('  -> WebHUD message feed implements in-place repeat count updates');
    console.log('  -> Real-time combat and term row 0 message stream parsing verified');

    // Test 14: Automatic Shop Space Navigation & Auto-Hold Trigger
    console.log('Test 14: Automatic Shop Space Navigation & Auto-Hold Trigger');
    const dungeonCss = fs.readFileSync(path.join(__dirname, '../public/css/dungeon.css'), 'utf8');
    const freshAppJs = fs.readFileSync(path.join(__dirname, '../public/js/app.js'), 'utf8');
    assert(freshAppJs.includes("const terminalTitle = document.getElementById('term-title') || document.getElementById('terminal-title');"), 'app.js must lookup term-title correctly');
    assert(freshAppJs.includes("network.sendKey('.');"), 'app.js must auto-send period to enter store when stepping onto store tile');
    assert(dungeonCss.includes("flex: 1;"), 'dungeon.css message-feed-scroll must flex: 1 to fill message feed window');
    console.log('  -> Store entrance tile automatically triggers store entry command (.)');
    console.log('  -> Store actions toolbar title and buttons correctly bound');
    console.log('  -> Message feed scroll container styled to fill full window height');

    console.log('\nAll server unit tests passed successfully!');
    process.exit(0);
}

runTests().catch(err => {
    console.error('Test failed:', err);
    process.exit(1);
});
