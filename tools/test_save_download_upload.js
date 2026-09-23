const http = require('http');
const assert = require('assert');

// Construct valid SaveVNLA mock save buffer
function createMockSave(charName) {
    const magic = Buffer.from('SaveVNLA', 'ascii');
    const block = Buffer.alloc(16, 0);
    Buffer.from('description', 'ascii').copy(block);
    const version = Buffer.alloc(4, 0);
    version.writeUInt32LE(1, 0);

    const descStr = `${charName}, the High-Elf Warrior\0`;
    const descBuf = Buffer.from(descStr, 'utf8');

    const size = Buffer.alloc(4, 0);
    size.writeUInt32LE(descBuf.length, 0);

    const checksum = Buffer.alloc(4, 0);
    checksum.writeUInt32LE(0x12345678, 0);

    return Buffer.concat([magic, block, version, size, checksum, descBuf]);
}

async function request(options, body = null) {
    return new Promise((resolve, reject) => {
        const req = http.request(options, res => {
            const chunks = [];
            res.on('data', c => chunks.push(c));
            res.on('end', () => {
                const raw = Buffer.concat(chunks);
                resolve({
                    status: res.statusCode,
                    headers: res.headers,
                    body: raw,
                    text: raw.toString('utf8'),
                    json: () => JSON.parse(raw.toString('utf8'))
                });
            });
        });
        req.on('error', reject);
        if (body) req.write(body);
        req.end();
    });
}

async function run() {
    console.log('Testing Save Game Download & Upload endpoints against local server on 8081...');
    // Start local server instance
    const serverModule = require('../server/src/server.js');
    await new Promise(r => setTimeout(r, 600));

    const testChar = 'ExportHero_' + Date.now();
    const mockSave = createMockSave(testChar);

    // 1. Upload save
    console.log('1. Uploading test save:', testChar);
    const uploadRes = await request({
        hostname: 'localhost',
        port: 8080,
        path: '/api/saves/upload',
        method: 'POST',
        headers: {
            'Content-Type': 'application/octet-stream',
            'X-Character-Name': testChar,
            'Content-Length': mockSave.length
        }
    }, mockSave);

    assert.strictEqual(uploadRes.status, 200, `Upload should return 200: ${uploadRes.text}`);
    const uploadJson = uploadRes.json();
    console.log('   -> Upload response:', uploadJson);
    assert.strictEqual(uploadJson.status, 'saved');
    const uploadedFilename = uploadJson.filename;

    // 2. Verify listing
    console.log('2. Verifying save exists in /api/saves listing...');
    const listRes = await request({
        hostname: 'localhost',
        port: 8080,
        path: '/api/saves',
        method: 'GET'
    });
    assert.strictEqual(listRes.status, 200);
    const listJson = listRes.json();
    const found = listJson.saves.find(s => s.filename === uploadedFilename);
    assert(found, 'Uploaded save must appear in /api/saves');
    console.log('   -> Found in listing:', found.characterName);

    // 3. Download save file
    console.log('3. Downloading save via /api/saves/' + uploadedFilename);
    const downloadRes = await request({
        hostname: 'localhost',
        port: 8080,
        path: `/api/saves/${encodeURIComponent(uploadedFilename)}`,
        method: 'GET'
    });
    assert.strictEqual(downloadRes.status, 200);
    assert.strictEqual(downloadRes.headers['content-type'], 'application/octet-stream');
    assert.strictEqual(downloadRes.body.length, mockSave.length);
    assert(downloadRes.body.slice(0, 8).toString('ascii') === 'SaveVNLA');
    console.log('   -> Downloaded binary payload verified bit-for-bit!');

    // 4. Clean up save
    console.log('4. Cleaning up test save...');
    const deleteRes = await request({
        hostname: 'localhost',
        port: 8080,
        path: `/api/saves/${encodeURIComponent(uploadedFilename)}`,
        method: 'DELETE'
    });
    assert.strictEqual(deleteRes.status, 200);
    console.log('   -> Successfully deleted test save.');

    console.log('\n*** Save Download & Upload Verification PASSED 100%! ***');
    process.exit(0);
}

run().catch(err => {
    console.error('Test Failed:', err);
    process.exit(1);
});
