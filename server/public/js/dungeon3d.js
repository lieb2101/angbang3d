/**
 * Angband3D WebGL Renderer — High-Fidelity Three.js 3D Dungeon Crawler
 * Features:
 * - 1:1 Parity with Godot C# client's wire protocol (decodes map.rows[y].f and map.rows[y].l)
 * - Procedural PBR stone textures, tangent-space normal maps, and dynamic lighting
 * - Dynamic Molten Lava: glowing emissive when in LOS, cooled dark basalt in memory (0 glow-through)
 * - InstancedMesh batching with 8-neighbor rock culling, radial horizon culling, and memory shading
 * - Dynamic flickering torchlight, smooth camera kinematics, and head-bobbing
 * - Monster billboards, rotating 3D item pickups, and animated first-person viewmodel hands/torch
 */

class Dungeon3D {
    constructor(canvasId) {
        this.canvas = document.getElementById(canvasId);
        this.cellSize = 2.0; // 2.0m per grid cell matching Godot DungeonWorld
        this.wallHeight = 3.0;
        this.eyeHeight = 1.62;

        this.facing = 0; // 0=S, 1=W, 2=N, 3=E
        this.targetFacing = 0;
        this.targetCamPos = new THREE.Vector3(0, this.eyeHeight, 0);
        this.currentCamPos = new THREE.Vector3(0, this.eyeHeight, 0);
        this.cameraBobPhase = 0;

        this.stepDuration = 0.14; // 140ms step tween
        this.stepTime = 0;
        this.isStepping = false;

        this.monsters = new Map();
        this.items = new Map();

        this.initThree();
        this.initTextures();
        this.initMeshes();
        this.initViewmodel();
        this.animate = this.animate.bind(this);
        requestAnimationFrame(this.animate);
    }

    initThree() {
        this.scene = new THREE.Scene();
        this.scene.background = new THREE.Color(0x06070a);
        this.scene.fog = new THREE.FogExp2(0x06070a, 0.045);

        this.camera = new THREE.PerspectiveCamera(72, window.innerWidth / window.innerHeight, 0.1, 120);
        this.camera.position.set(0, this.eyeHeight, 0);

        this.renderer = new THREE.WebGLRenderer({
            canvas: this.canvas,
            antialias: true,
            powerPreference: 'high-performance'
        });
        this.renderer.setSize(window.innerWidth, window.innerHeight);
        this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
        this.renderer.toneMappingExposure = 1.15;

        // Ambient darkness with deep blue tinge
        this.ambientLight = new THREE.AmbientLight(0x1a2030, 0.35);
        this.scene.add(this.ambientLight);

        // Player Torch PointLight
        this.torchLight = new THREE.PointLight(0xff9933, 2.5, 26, 1.3);
        this.torchLight.position.set(0, this.eyeHeight + 0.2, 0);
        this.scene.add(this.torchLight);

        window.addEventListener('resize', () => {
            this.camera.aspect = window.innerWidth / window.innerHeight;
            this.camera.updateProjectionMatrix();
            this.renderer.setSize(window.innerWidth, window.innerHeight);
        });
    }

    generateNormalMap(sourceCanvas, strength = 2.0) {
        const w = sourceCanvas.width;
        const h = sourceCanvas.height;
        const srcCtx = sourceCanvas.getContext('2d');
        const srcData = srcCtx.getImageData(0, 0, w, h).data;

        const normCanvas = document.createElement('canvas');
        normCanvas.width = w;
        normCanvas.height = h;
        const normCtx = normCanvas.getContext('2d');
        const normImg = normCtx.createImageData(w, h);
        const normData = normImg.data;

        const getLum = (x, y) => {
            const px = (x + w) % w;
            const py = (y + h) % h;
            const i = (py * w + px) * 4;
            return (srcData[i] * 0.299 + srcData[i + 1] * 0.587 + srcData[i + 2] * 0.114) / 255.0;
        };

        for (let y = 0; y < h; y++) {
            for (let x = 0; x < w; x++) {
                const idx = (y * w + x) * 4;
                const dx = (getLum(x + 1, y) - getLum(x - 1, y)) * strength;
                const dy = (getLum(x, y + 1) - getLum(x, y - 1)) * strength;

                const len = Math.hypot(dx, dy, 1.0);
                normData[idx]     = Math.floor(((-dx / len) * 0.5 + 0.5) * 255);
                normData[idx + 1] = Math.floor(((-dy / len) * 0.5 + 0.5) * 255);
                normData[idx + 2] = Math.floor(((1.0 / len) * 0.5 + 0.5) * 255);
                normData[idx + 3] = 255;
            }
        }
        normCtx.putImageData(normImg, 0, 0);
        const tex = new THREE.CanvasTexture(normCanvas);
        tex.wrapS = THREE.RepeatWrapping;
        tex.wrapT = THREE.RepeatWrapping;
        return tex;
    }

    initTextures() {
        // Procedural Stone Wall Canvas
        const wallCanvas = document.createElement('canvas');
        wallCanvas.width = 512;
        wallCanvas.height = 512;
        const wCtx = wallCanvas.getContext('2d');
        wCtx.fillStyle = '#22252c';
        wCtx.fillRect(0, 0, 512, 512);

        wCtx.strokeStyle = '#111317';
        wCtx.lineWidth = 6;
        const rowH = 64;
        for (let y = 0; y < 512; y += rowH) {
            wCtx.beginPath();
            wCtx.moveTo(0, y);
            wCtx.lineTo(512, y);
            wCtx.stroke();

            const offset = (y / rowH) % 2 === 0 ? 0 : 64;
            for (let x = offset; x < 512 + offset; x += 128) {
                wCtx.beginPath();
                wCtx.moveTo(x % 512, y);
                wCtx.lineTo(x % 512, y + rowH);
                wCtx.stroke();
            }
        }
        for (let i = 0; i < 4000; i++) {
            const val = Math.floor(Math.random() * 40 + 20);
            wCtx.fillStyle = `rgba(${val}, ${val}, ${val}, 0.15)`;
            wCtx.fillRect(Math.random() * 512, Math.random() * 512, 4, 4);
        }
        this.wallTexture = new THREE.CanvasTexture(wallCanvas);
        this.wallTexture.wrapS = THREE.RepeatWrapping;
        this.wallTexture.wrapT = THREE.RepeatWrapping;
        this.wallNormalMap = this.generateNormalMap(wallCanvas, 2.5);

        // Procedural Flagstone Floor Canvas
        const floorCanvas = document.createElement('canvas');
        floorCanvas.width = 512;
        floorCanvas.height = 512;
        const fCtx = floorCanvas.getContext('2d');
        fCtx.fillStyle = '#1a1c22';
        fCtx.fillRect(0, 0, 512, 512);
        fCtx.strokeStyle = '#0d0f14';
        fCtx.lineWidth = 5;
        for (let y = 0; y < 512; y += 128) {
            for (let x = 0; x < 512; x += 128) {
                fCtx.strokeRect(x, y, 128, 128);
            }
        }
        for (let i = 0; i < 3000; i++) {
            const v = Math.floor(Math.random() * 30 + 15);
            fCtx.fillStyle = `rgba(${v}, ${v}, ${v}, 0.12)`;
            fCtx.fillRect(Math.random() * 512, Math.random() * 512, 3, 3);
        }
        this.floorTexture = new THREE.CanvasTexture(floorCanvas);
        this.floorTexture.wrapS = THREE.RepeatWrapping;
        this.floorTexture.wrapT = THREE.RepeatWrapping;
        this.floorNormalMap = this.generateNormalMap(floorCanvas, 2.0);

        // Procedural Wood Door Canvas
        const doorCanvas = document.createElement('canvas');
        doorCanvas.width = 256;
        doorCanvas.height = 512;
        const dCtx = doorCanvas.getContext('2d');
        dCtx.fillStyle = '#3a2618';
        dCtx.fillRect(0, 0, 256, 512);
        dCtx.strokeStyle = '#22150a';
        dCtx.lineWidth = 4;
        for (let x = 0; x < 256; x += 64) {
            dCtx.beginPath();
            dCtx.moveTo(x, 0);
            dCtx.lineTo(x, 512);
            dCtx.stroke();
        }
        dCtx.fillStyle = '#151515';
        for (let y = 80; y < 450; y += 140) {
            dCtx.fillRect(16, y, 224, 16);
            dCtx.beginPath();
            dCtx.arc(40, y + 8, 8, 0, Math.PI * 2);
            dCtx.arc(216, y + 8, 8, 0, Math.PI * 2);
            dCtx.fill();
        }
        this.doorTexture = new THREE.CanvasTexture(doorCanvas);

        // Materials with PBR Normal Maps
        this.wallMaterial = new THREE.MeshStandardMaterial({
            map: this.wallTexture,
            normalMap: this.wallNormalMap,
            roughness: 0.82,
            metalness: 0.08
        });

        this.floorMaterial = new THREE.MeshStandardMaterial({
            map: this.floorTexture,
            normalMap: this.floorNormalMap,
            roughness: 0.78,
            metalness: 0.04
        });

        this.ceilingMaterial = new THREE.MeshStandardMaterial({
            map: this.wallTexture,
            color: 0x666666,
            roughness: 0.92,
            metalness: 0.0
        });

        this.doorMaterial = new THREE.MeshStandardMaterial({
            map: this.doorTexture,
            roughness: 0.75,
            metalness: 0.2
        });

        // In-View Molten Lava (Glows, pulsates)
        this.lavaMaterial = new THREE.MeshStandardMaterial({
            color: 0xff3300,
            emissive: 0xff4400,
            emissiveIntensity: 1.8,
            roughness: 0.35,
            metalness: 0.1
        });

        // Cooled Basalt (Memory/Fog-of-War Lava: Dark, ZERO emission)
        this.lavaCooledMaterial = new THREE.MeshStandardMaterial({
            color: 0x181512,
            roughness: 0.95,
            metalness: 0.0
        });

        this.stairsMaterial = new THREE.MeshStandardMaterial({
            map: this.floorTexture,
            color: 0x88aacc,
            roughness: 0.7
        });
    }

    initMeshes() {
        this.maxInstances = 4096;
        const boxGeo = new THREE.BoxGeometry(this.cellSize, this.wallHeight, this.cellSize);
        const floorGeo = new THREE.PlaneGeometry(this.cellSize, this.cellSize);
        floorGeo.rotateX(-Math.PI / 2);

        const ceilingGeo = new THREE.PlaneGeometry(this.cellSize, this.cellSize);
        ceilingGeo.rotateX(Math.PI / 2);

        this.wallMesh = new THREE.InstancedMesh(boxGeo, this.wallMaterial, this.maxInstances);
        this.wallMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.wallMesh);

        this.floorMesh = new THREE.InstancedMesh(floorGeo, this.floorMaterial, this.maxInstances);
        this.floorMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.floorMesh);

        this.ceilingMesh = new THREE.InstancedMesh(ceilingGeo, this.ceilingMaterial, this.maxInstances);
        this.ceilingMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.ceilingMesh);

        this.doorMesh = new THREE.InstancedMesh(boxGeo, this.doorMaterial, 512);
        this.doorMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.doorMesh);

        this.lavaMesh = new THREE.InstancedMesh(floorGeo, this.lavaMaterial, 512);
        this.lavaMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.lavaMesh);

        this.lavaCooledMesh = new THREE.InstancedMesh(floorGeo, this.lavaCooledMaterial, 512);
        this.lavaCooledMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.lavaCooledMesh);

        this.stairsMesh = new THREE.InstancedMesh(boxGeo, this.stairsMaterial, 128);
        this.stairsMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.stairsMesh);

        // Pre-allocate instance color buffers
        const white = new THREE.Color(1, 1, 1);
        this.wallMesh.setColorAt(0, white);
        this.floorMesh.setColorAt(0, white);
        this.ceilingMesh.setColorAt(0, white);
        this.doorMesh.setColorAt(0, white);
        this.stairsMesh.setColorAt(0, white);

        this.dummy = new THREE.Object3D();
    }

    initViewmodel() {
        this.viewmodelGroup = new THREE.Group();
        this.camera.add(this.viewmodelGroup);
        this.scene.add(this.camera);

        // Right Hand Weapon: Steel Broadsword
        const swordGroup = new THREE.Group();

        // Blade
        const bladeGeo = new THREE.BoxGeometry(0.04, 0.75, 0.012);
        const bladeMat = new THREE.MeshStandardMaterial({
            color: 0xe0e6ed,
            roughness: 0.18,
            metalness: 0.95
        });
        const blade = new THREE.Mesh(bladeGeo, bladeMat);
        blade.position.y = 0.38;
        swordGroup.add(blade);

        // Crossguard
        const guardGeo = new THREE.BoxGeometry(0.18, 0.025, 0.035);
        const guardMat = new THREE.MeshStandardMaterial({
            color: 0xc89d3b,
            roughness: 0.35,
            metalness: 0.85
        });
        const guard = new THREE.Mesh(guardGeo, guardMat);
        guard.position.y = 0.01;
        swordGroup.add(guard);

        // Leather-wrapped Grip
        const gripGeo = new THREE.CylinderGeometry(0.018, 0.018, 0.14, 8);
        const gripMat = new THREE.MeshStandardMaterial({
            color: 0x3d2817,
            roughness: 0.9
        });
        const grip = new THREE.Mesh(gripGeo, gripMat);
        grip.position.y = -0.07;
        swordGroup.add(grip);

        // Golden Pommel
        const pommelGeo = new THREE.SphereGeometry(0.026, 8, 8);
        const pommel = new THREE.Mesh(pommelGeo, guardMat);
        pommel.position.y = -0.15;
        swordGroup.add(pommel);

        swordGroup.position.set(0.36, -0.32, -0.62);
        swordGroup.rotation.set(0.1, -0.2, 0.3);
        this.weaponMesh = swordGroup;
        this.viewmodelGroup.add(swordGroup);

        // Left Hand: Wooden Torch with Flame
        const torchGroup = new THREE.Group();

        const torchStaffGeo = new THREE.CylinderGeometry(0.022, 0.03, 0.55, 8);
        const torchStaffMat = new THREE.MeshStandardMaterial({ color: 0x4a3220, roughness: 0.9 });
        const torchStaff = new THREE.Mesh(torchStaffGeo, torchStaffMat);
        torchStaff.position.y = -0.15;
        torchGroup.add(torchStaff);

        const sconceGeo = new THREE.CylinderGeometry(0.045, 0.035, 0.08, 8);
        const sconceMat = new THREE.MeshStandardMaterial({ color: 0x222222, metalness: 0.8, roughness: 0.4 });
        const sconce = new THREE.Mesh(sconceGeo, sconceMat);
        sconce.position.y = 0.12;
        torchGroup.add(sconce);

        const flameGeo = new THREE.ConeGeometry(0.055, 0.16, 8);
        const flameMat = new THREE.MeshBasicMaterial({ color: 0xffaa22 });
        this.flameMesh = new THREE.Mesh(flameGeo, flameMat);
        this.flameMesh.position.y = 0.22;
        torchGroup.add(this.flameMesh);

        torchGroup.position.set(-0.36, -0.28, -0.58);
        torchGroup.rotation.set(0.15, 0.25, -0.15);
        this.torchHandle = torchGroup;
        this.viewmodelGroup.add(torchGroup);

        this.attackAnimationTime = 0;
    }

    triggerAttackAnimation() {
        this.attackAnimationTime = 0.22; // 220ms swing
    }

    turn(dir) {
        this.targetFacing = (this.targetFacing + dir + 4) % 4;
        this.facing = this.targetFacing;
    }

    getFeatAt(map, x, y) {
        if (!map || !map.rows || y < 0 || y >= map.h || x < 0 || x >= map.w) return 0;
        const row = map.rows[y];
        if (!row || !row.f) return 0;
        const idx = x * 2;
        if (idx + 1 >= row.f.length) return 0;
        const hi = parseInt(row.f[idx], 16) || 0;
        const lo = parseInt(row.f[idx + 1], 16) || 0;
        return (hi << 4) | lo;
    }

    getFlagAt(map, x, y) {
        if (!map || !map.rows || y < 0 || y >= map.h || x < 0 || x >= map.w) return 0;
        const row = map.rows[y];
        if (!row || !row.l || x >= row.l.length) return 0;
        return parseInt(row.l[x], 16) || 0;
    }

    updateDungeon(frame) {
        if (!frame || !frame.map || !frame.player) return;

        const px = frame.player.x;
        const py = frame.player.y;
        const map = frame.map;
        const w = map.w;
        const h = map.h;

        // Target camera position
        const targetX = px * this.cellSize;
        const targetZ = py * this.cellSize;

        if (this.currentCamPos.x !== targetX || this.currentCamPos.z !== targetZ) {
            this.targetCamPos.set(targetX, this.eyeHeight, targetZ);
            this.isStepping = true;
            this.stepTime = 0;
        }

        let wallCount = 0;
        let floorCount = 0;
        let ceilingCount = 0;
        let doorCount = 0;
        let lavaCount = 0;
        let lavaCooledCount = 0;
        let stairsCount = 0;

        const maxR = 28; // Radial horizon culling matching DungeonWorld.cs

        const colInView = new THREE.Color(1.0, 1.0, 1.0);
        const colMemory = new THREE.Color(0.28, 0.30, 0.38); // Cool dark slate memory tint

        for (let y = Math.max(0, py - maxR); y < Math.min(h, py + maxR); y++) {
            for (let x = Math.max(0, px - maxR); x < Math.min(w, px + maxR); x++) {
                const feat = this.getFeatAt(map, x, y);
                const flag = this.getFlagAt(map, x, y);

                const known = (flag & 0x1) !== 0;
                const inView = (flag & 0x2) !== 0;
                if (!known && !inView) continue;

                const wx = x * this.cellSize;
                const wz = y * this.cellSize;
                const tileCol = inView ? colInView : colMemory;

                // Solid stone / Granite / Perm wall
                if (feat >= 17 && feat <= 22) {
                    let hasExposedFace = false;
                    for (let dy = -1; dy <= 1; dy++) {
                        for (let dx = -1; dx <= 1; dx++) {
                            if (dx === 0 && dy === 0) continue;
                            const nx = x + dx;
                            const ny = y + dy;
                            if (nx >= 0 && nx < w && ny >= 0 && ny < h) {
                                const nFeat = this.getFeatAt(map, nx, ny);
                                if (nFeat === 1 || nFeat === 2 || nFeat === 3 || nFeat === 4 || nFeat === 5 || nFeat === 6 || nFeat === 23) {
                                    hasExposedFace = true;
                                    break;
                                }
                            }
                        }
                        if (hasExposedFace) break;
                    }
                    if (!hasExposedFace) continue; // Skip interior bedrock

                    this.dummy.position.set(wx, this.wallHeight / 2, wz);
                    this.dummy.updateMatrix();
                    this.wallMesh.setMatrixAt(wallCount, this.dummy.matrix);
                    this.wallMesh.setColorAt(wallCount, tileCol);
                    wallCount++;
                    continue;
                }

                // Floor / Walkable corridor / Store entrance
                if (feat === 1 || feat === 3 || feat === 4 || (feat >= 7 && feat <= 14)) {
                    this.dummy.position.set(wx, 0, wz);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, tileCol);
                    floorCount++;

                    // Ceiling above walkable floor
                    this.dummy.position.set(wx, this.wallHeight, wz);
                    this.dummy.updateMatrix();
                    this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                    this.ceilingMesh.setColorAt(ceilingCount, tileCol);
                    ceilingCount++;
                    continue;
                }

                // Closed Door
                if (feat === 2 || feat === 15) {
                    this.dummy.position.set(wx, this.wallHeight / 2, wz);
                    this.dummy.updateMatrix();
                    this.doorMesh.setMatrixAt(doorCount, this.dummy.matrix);
                    this.doorMesh.setColorAt(doorCount, tileCol);
                    doorCount++;
                    continue;
                }

                // Molten Lava
                if (feat === 23) {
                    this.dummy.position.set(wx, -0.05, wz);
                    this.dummy.updateMatrix();
                    if (inView) {
                        // Molten, glowing, emissive lava in line-of-sight
                        this.lavaMesh.setMatrixAt(lavaCount++, this.dummy.matrix);
                    } else {
                        // Cooled dark basalt in memory: ZERO emission
                        this.lavaCooledMesh.setMatrixAt(lavaCooledCount++, this.dummy.matrix);
                    }
                    continue;
                }

                // Stairs Up/Down
                if (feat === 5 || feat === 6) {
                    this.dummy.position.set(wx, 0.4, wz);
                    this.dummy.updateMatrix();
                    this.stairsMesh.setMatrixAt(stairsCount, this.dummy.matrix);
                    this.stairsMesh.setColorAt(stairsCount, tileCol);
                    stairsCount++;
                    continue;
                }
            }
        }

        this.wallMesh.count = wallCount;
        this.wallMesh.instanceMatrix.needsUpdate = true;
        if (this.wallMesh.instanceColor) this.wallMesh.instanceColor.needsUpdate = true;

        this.floorMesh.count = floorCount;
        this.floorMesh.instanceMatrix.needsUpdate = true;
        if (this.floorMesh.instanceColor) this.floorMesh.instanceColor.needsUpdate = true;

        this.ceilingMesh.count = ceilingCount;
        this.ceilingMesh.instanceMatrix.needsUpdate = true;
        if (this.ceilingMesh.instanceColor) this.ceilingMesh.instanceColor.needsUpdate = true;

        this.doorMesh.count = doorCount;
        this.doorMesh.instanceMatrix.needsUpdate = true;
        if (this.doorMesh.instanceColor) this.doorMesh.instanceColor.needsUpdate = true;

        this.lavaMesh.count = lavaCount;
        this.lavaMesh.instanceMatrix.needsUpdate = true;

        this.lavaCooledMesh.count = lavaCooledCount;
        this.lavaCooledMesh.instanceMatrix.needsUpdate = true;

        this.stairsMesh.count = stairsCount;
        this.stairsMesh.instanceMatrix.needsUpdate = true;
        if (this.stairsMesh.instanceColor) this.stairsMesh.instanceColor.needsUpdate = true;

        this.updateMonsters(frame.monsters || []);
        this.updateItems(frame.objects || frame.items || []);
    }

    updateMonsters(monsters) {
        const activeIds = new Set();

        monsters.forEach(m => {
            const id = m.id || `${m.x}_${m.y}_${m.glyph}`;
            activeIds.add(id);
            let sprite = this.monsters.get(id);

            if (!sprite) {
                const canvas = document.createElement('canvas');
                canvas.width = 128;
                canvas.height = 128;
                const ctx = canvas.getContext('2d');
                ctx.font = 'bold 76px "Fira Code", monospace';
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';
                ctx.fillStyle = m.color || '#ff4444';
                ctx.fillText(m.glyph || 'M', 64, 64);

                const tex = new THREE.CanvasTexture(canvas);
                const mat = new THREE.SpriteMaterial({ map: tex, depthWrite: false });
                sprite = new THREE.Sprite(mat);
                sprite.scale.set(1.4, 1.4, 1.4);
                this.scene.add(sprite);
                this.monsters.set(id, sprite);
            }

            sprite.position.set(m.x * this.cellSize, 0.8, m.y * this.cellSize);
        });

        for (const [id, sprite] of this.monsters.entries()) {
            if (!activeIds.has(id)) {
                this.scene.remove(sprite);
                this.monsters.delete(id);
            }
        }
    }

    updateItems(items) {
        const activeIds = new Set();

        items.forEach(it => {
            const id = `${it.x}_${it.y}_${it.name || it.glyph}`;
            activeIds.add(id);
            let mesh = this.items.get(id);

            if (!mesh) {
                const geo = new THREE.OctahedronGeometry(0.24, 0);
                const mat = new THREE.MeshStandardMaterial({
                    color: 0xffd700,
                    emissive: 0x886600,
                    metalness: 0.85,
                    roughness: 0.25
                });
                mesh = new THREE.Mesh(geo, mat);
                this.scene.add(mesh);
                this.items.set(id, mesh);
            }

            mesh.position.set(it.x * this.cellSize, 0.35, it.y * this.cellSize);
            mesh.rotation.y += 0.03;
        });

        for (const [id, mesh] of this.items.entries()) {
            if (!activeIds.has(id)) {
                this.scene.remove(mesh);
                this.items.delete(id);
            }
        }
    }

    animate() {
        requestAnimationFrame(this.animate);

        const delta = 0.016;

        // Camera movement tween
        if (this.isStepping) {
            this.stepTime += delta;
            const t = Math.min(1.0, this.stepTime / this.stepDuration);
            this.currentCamPos.lerp(this.targetCamPos, t);
            this.cameraBobPhase += 0.35;

            if (t >= 1.0) {
                this.isStepping = false;
                this.currentCamPos.copy(this.targetCamPos);
            }
        }

        // Camera head-bob
        const bob = Math.sin(this.cameraBobPhase) * 0.035;
        this.camera.position.set(this.currentCamPos.x, this.currentCamPos.y + bob, this.currentCamPos.z);
        this.torchLight.position.set(this.currentCamPos.x, this.currentCamPos.y + 0.3, this.currentCamPos.z);

        // Torch flame intensity flicker
        const flicker = 2.2 + Math.sin(Date.now() * 0.012) * 0.28 + Math.cos(Date.now() * 0.027) * 0.15;
        this.torchLight.intensity = flicker;

        // Torch flame mesh scale flicker
        if (this.flameMesh) {
            this.flameMesh.scale.set(
                1.0 + Math.sin(Date.now() * 0.015) * 0.12,
                1.0 + Math.cos(Date.now() * 0.02) * 0.2,
                1.0 + Math.sin(Date.now() * 0.015) * 0.12
            );
        }

        // Facing yaw rotation: 0=S (0), 1=W (PI/2), 2=N (PI), 3=E (3PI/2)
        const targetYaw = (this.facing * Math.PI) / 2;
        this.camera.rotation.y = targetYaw;

        // Attack animation
        if (this.attackAnimationTime > 0) {
            this.attackAnimationTime -= delta;
            this.weaponMesh.position.z = -0.62 - Math.sin((0.22 - this.attackAnimationTime) * Math.PI * 4.5) * 0.25;
            this.weaponMesh.rotation.z = 0.3 - Math.sin((0.22 - this.attackAnimationTime) * Math.PI * 4.5) * 0.45;
        } else {
            this.weaponMesh.position.z = -0.62;
            this.weaponMesh.rotation.z = 0.3;
        }

        this.renderer.render(this.scene, this.camera);
    }
}

window.Dungeon3D = Dungeon3D;
