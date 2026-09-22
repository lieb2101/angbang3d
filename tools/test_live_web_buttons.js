const http = require('https');

function fetch(url) {
    return new Promise((resolve, reject) => {
        http.get(url, (res) => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve({ statusCode: res.statusCode, body: data }));
        }).on('error', reject);
    });
}

async function testWeb() {
    const baseUrl = 'https://angband3d-web-564958309282.us-central1.run.app';
    console.log(`Checking ${baseUrl}...`);
    
    const indexRes = await fetch(baseUrl + '/');
    console.log(`index.html: status ${indexRes.statusCode}, size ${indexRes.body.length}`);
    if (indexRes.statusCode !== 200) throw new Error('index.html returned non-200');

    // Check button IDs in index.html
    const requiredButtons = [
        'btn-splash-start', 'btn-splash-menu', 'btn-splash-guide', 'btn-splash-credits',
        'btn-menu-continue', 'btn-menu-load', 'btn-menu-random', 'btn-menu-custom'
    ];
    for (const btnId of requiredButtons) {
        if (!indexRes.body.includes(`id="${btnId}"`)) {
            throw new Error(`Missing button #${btnId} in index.html`);
        }
        console.log(`  -> Button #${btnId} present in HTML`);
    }

    const scripts = ['js/app.js', 'js/hud.js', 'js/input.js', 'js/terminal.js', 'js/audio.js', 'js/dungeon3d.js', 'js/network.js'];
    for (const script of scripts) {
        const res = await fetch(`${baseUrl}/${script}`);
        console.log(`${script}: status ${res.statusCode}, size ${res.body.length}`);
        if (res.statusCode !== 200) throw new Error(`${script} returned non-200`);
        
        try {
            new Function(res.body);
            console.log(`  -> ${script} syntax valid`);
        } catch (e) {
            console.error(`  -> ERROR in ${script}: ${e.message}`);
            throw e;
        }
    }

    console.log('\nAll web client scripts verified successfully online!');
}

testWeb().catch(err => {
    console.error('Validation failed:', err.message);
    process.exit(1);
});
