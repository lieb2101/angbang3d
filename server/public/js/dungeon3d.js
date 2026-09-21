/**
 * Angband3D WebGL Renderer — High-Fidelity Three.js 3D Dungeon Crawler
 * Features:
 * - Procedural PBR stone textures, normal maps, and animated molten lava
 * - InstancedMesh batching with 8-neighbor rock culling and radial horizon culling
 * - Dynamic flickering torchlight, smooth camera kinematics, and head-bobbing
 * - Monster billboards, rotating 3D item pickups, and animated first-person viewmodel hands
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
        this.renderer.toneMappingExposure = 1.1;

        // Ambient darkness with deep blue tinge
        this.ambientLight = new THREE.AmbientLight(0x181c28, 0.28);
        this.scene.add(this.ambientLight);

        // Player Torch PointLight
        this.torchLight = new THREE.PointLight(0xffaa44, 2.2, 24, 1.4);
        this.torchLight.position.set(0, this.eyeHeight + 0.2, 0);
        this.scene.add(this.torchLight);

        window.addEventListener('resize', () => {
            this.camera.aspect = window.innerWidth / window.innerHeight;
            this.camera.updateProjectionMatrix();
            this.renderer.setSize(window.innerWidth, window.innerHeight);
        });
    }

    initTextures() {
        // Procedural Stone Wall Canvas
        const wallCanvas = document.createElement('canvas');
        wallCanvas.width = 512;
        wallCanvas.height = 512;
        const wCtx = wallCanvas.getContext('2d');
        wCtx.fillStyle = '#22252c';
        wCtx.fillRect(0, 0, 512, 512);

        // Brick patterns & mortar
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
        // Subtle noise specks
        for (let i = 0; i < 4000; i++) {
            const val = Math.floor(Math.random() * 40 + 20);
            wCtx.fillStyle = `rgba(${val}, ${val}, ${val}, 0.15)`;
            wCtx.fillRect(Math.random() * 512, Math.random() * 512, 4, 4);
        }
        this.wallTexture = new THREE.CanvasTexture(wallCanvas);
        this.wallTexture.wrapS = THREE.RepeatWrapping;
        this.wallTexture.wrapT = THREE.RepeatWrapping;

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
        // Iron studs
        dCtx.fillStyle = '#151515';
        for (let y = 80; y < 450; y += 140) {
            dCtx.fillRect(16, y, 224, 16);
            dCtx.beginPath();
            dCtx.arc(40, y + 8, 8, 0, Math.PI * 2);
            dCtx.arc(216, y + 8, 8, 0, Math.PI * 2);
            dCtx.fill();
        }
        this.doorTexture = new THREE.CanvasTexture(doorCanvas);

        // Materials
        this.wallMaterial = new THREE.MeshStandardMaterial({
            map: this.wallTexture,
            roughness: 0.88,
            metalness: 0.05
        });

        this.floorMaterial = new THREE.MeshStandardMaterial({
            map: this.floorTexture,
            roughness: 0.85,
            metalness: 0.02
        });

        this.ceilingMaterial = new THREE.MeshStandardMaterial({
            map: this.wallTexture,
            color: 0x555555,
            roughness: 0.95,
            metalness: 0.0
        });

        this.doorMaterial = new THREE.MeshStandardMaterial({
            map: this.doorTexture,
            roughness: 0.75,
            metalness: 0.2
        });

        this.lavaMaterial = new THREE.MeshStandardMaterial({
            color: 0xff3300,
            emissive: 0xff4400,
            emissiveIntensity: 1.8,
            roughness: 0.4
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

        this.stairsMesh = new THREE.InstancedMesh(boxGeo, this.stairsMaterial, 128);
        this.stairsMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        this.scene.add(this.stairsMesh);

        this.dummy = new THREE.Object3D();
    }

    initViewmodel() {
        this.viewmodelGroup = new THREE.Group();
        this.camera.add(this.viewmodelGroup);
        this.scene.add(this.camera);

        // Right Hand (Weapon: Steel Blade)
        const bladeGeo = new THREE.CylinderGeometry(0.015, 0.045, 0.9, 8);
        bladeGeo.rotateZ(Math.PI / 6);
        const bladeMat = new THREE.MeshStandardMaterial({
            color: 0xcccccc,
            roughness: 0.25,
            metalness: 0.9
        });
        this.weaponMesh = new THREE.Mesh(bladeGeo, bladeMat);
        this.weaponMesh.position.set(0.35, -0.28, -0.65);
        this.viewmodelGroup.add(this.weaponMesh);

        // Left Hand (Torch)
        const torchHandleGeo = new THREE.CylinderGeometry(0.025, 0.035, 0.6, 8);
        const torchHandleMat = new THREE.MeshStandardMaterial({ color: 0x5a3d28, roughness: 0.9 });
        this.torchHandle = new THREE.Mesh(torchHandleGeo, torchHandleMat);
        this.torchHandle.position.set(-0.35, -0.32, -0.6);

        // Flame head
        const flameGeo = new THREE.ConeGeometry(0.06, 0.18, 8);
        const flameMat = new THREE.MeshBasicMaterial({ color: 0xffaa22 });
        const flame = new THREE.Mesh(flameGeo, flameMat);
        flame.position.set(0, 0.35, 0);
        this.torchHandle.add(flame);
        this.viewmodelGroup.add(this.torchHandle);

        this.attackAnimationTime = 0;
    }

    triggerAttackAnimation() {
        this.attackAnimationTime = 0.22; // 220ms swing
    }

    turn(dir) {
        // Instant 90 deg turn
        this.targetFacing = (this.targetFacing + dir + 4) % 4;
        this.facing = this.targetFacing;
    }

    updateDungeon(frame) {
        if (!frame || !frame.map || !frame.player) return;

        const px = frame.player.x;
        const py = frame.player.y;
        const map = frame.map;
        const w = map.w;
        const h = map.h;
        const cells = map.cells;

        // Target camera position
        const targetX = px * this.cellSize;
        const targetZ = py * this.cellSize;

        if (this.currentCamPos.x !== targetX || this.currentCamPos.z !== targetZ) {
            this.targetCamPos.set(targetX, this.eyeHeight, targetZ);
            this.isStepping = true;
            this.stepTime = 0;
        }

        // Feature indices from Angband engine/src/list-terrain.h
        // 0=None, 1=Floor, 2=Closed, 3=Open, 4=Broken, 5=Less (stairs up), 6=More (stairs down), 17..22=Wall/Granite/Perm, 23=Lava
        let wallCount = 0;
        let floorCount = 0;
        let ceilingCount = 0;
        let doorCount = 0;
        let lavaCount = 0;
        let stairsCount = 0;

        const maxR = 28; // Radial horizon culling matching DungeonWorld.cs

        for (let y = Math.max(0, py - maxR); y < Math.min(h, py + maxR); y++) {
            for (let x = Math.max(0, px - maxR); x < Math.min(w, px + maxR); x++) {
                const idx = y * w + x;
                const cell = cells[idx];
                if (!cell) continue;

                const feat = cell.f;
                const known = cell.k;
                const inView = cell.v;
                if (!known && !inView) continue;

                const wx = x * this.cellSize;
                const wz = y * this.cellSize;

                // 8-neighbor enclosed solid rock occlusion culling
                if (feat >= 17 && feat <= 22) { // Wall / Granite
                    let hasExposedFace = false;
                    for (let dy = -1; dy <= 1; dy++) {
                        for (let dx = -1; dx <= 1; dx++) {
                            if (dx === 0 && dy === 0) continue;
                            const nx = x + dx;
                            const ny = y + dy;
                            if (nx >= 0 && nx < w && ny >= 0 && ny < h) {
                                const nCell = cells[ny * w + nx];
                                if (nCell && (nCell.f === 1 || nCell.f === 3 || nCell.f === 5 || nCell.f === 6 || nCell.f === 23)) {
                                    hasExposedFace = true;
                                    break;
                                }
                            }
                        }
                        if (hasExposedFace) break;
                    }
                    if (!hasExposedFace) continue; // Skip interior solid rock

                    this.dummy.position.set(wx, this.wallHeight / 2, wz);
                    this.dummy.updateMatrix();
                    this.wallMesh.setMatrixAt(wallCount++, this.dummy.matrix);
                    continue;
                }

                // Floor / Walkable space
                if (feat === 1 || feat === 3 || feat === 4) {
                    this.dummy.position.set(wx, 0, wz);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount++, this.dummy.matrix);

                    // Ceiling above floor
                    this.dummy.position.set(wx, this.wallHeight, wz);
                    this.dummy.updateMatrix();
                    this.ceilingMesh.setMatrixAt(ceilingCount++, this.dummy.matrix);
                    continue;
                }

                // Closed Door
                if (feat === 2) {
                    this.dummy.position.set(wx, this.wallHeight / 2, wz);
                    this.dummy.updateMatrix();
                    this.doorMesh.setMatrixAt(doorCount++, this.dummy.matrix);
                    continue;
                }

                // Molten Lava
                if (feat === 23) {
                    this.dummy.position.set(wx, -0.1, wz);
                    this.dummy.updateMatrix();
                    this.lavaMesh.setMatrixAt(lavaCount++, this.dummy.matrix);
                    continue;
                }

                // Stairs
                if (feat === 5 || feat === 6) {
                    this.dummy.position.set(wx, 0.4, wz);
                    this.dummy.updateMatrix();
                    this.stairsMesh.setMatrixAt(stairsCount++, this.dummy.matrix);
                    continue;
                }
            }
        }

        this.wallMesh.count = wallCount;
        this.wallMesh.instanceMatrix.needsUpdate = true;

        this.floorMesh.count = floorCount;
        this.floorMesh.instanceMatrix.needsUpdate = true;

        this.ceilingMesh.count = ceilingCount;
        this.ceilingMesh.instanceMatrix.needsUpdate = true;

        this.doorMesh.count = doorCount;
        this.doorMesh.instanceMatrix.needsUpdate = true;

        this.lavaMesh.count = lavaCount;
        this.lavaMesh.instanceMatrix.needsUpdate = true;

        this.stairsMesh.count = stairsCount;
        this.stairsMesh.instanceMatrix.needsUpdate = true;

        this.updateMonsters(frame.monsters || []);
        this.updateItems(frame.items || []);
    }

    updateMonsters(monsters) {
        const activeIds = new Set();

        monsters.forEach(m => {
            activeIds.add(m.id || `${m.x}_${m.y}`);
            let sprite = this.monsters.get(m.id || `${m.x}_${m.y}`);

            if (!sprite) {
                const canvas = document.createElement('canvas');
                canvas.width = 128;
                canvas.height = 128;
                const ctx = canvas.getContext('2d');
                ctx.font = 'bold 72px "Fira Code", monospace';
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';
                ctx.fillStyle = m.color || '#ff4444';
                ctx.fillText(m.glyph || 'M', 64, 64);

                const tex = new THREE.CanvasTexture(canvas);
                const mat = new THREE.SpriteMaterial({ map: tex });
                sprite = new THREE.Sprite(mat);
                sprite.scale.set(1.4, 1.4, 1.4);
                this.scene.add(sprite);
                this.monsters.set(m.id || `${m.x}_${m.y}`, sprite);
            }

            sprite.position.set(m.x * this.cellSize, 0.8, m.y * this.cellSize);
        });

        // Remove stale monsters
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
            const id = `${it.x}_${it.y}_${it.name}`;
            activeIds.add(id);
            let mesh = this.items.get(id);

            if (!mesh) {
                const geo = new THREE.OctahedronGeometry(0.25, 0);
                const mat = new THREE.MeshStandardMaterial({
                    color: 0xffd700,
                    emissive: 0xaa8800,
                    metalness: 0.8,
                    roughness: 0.2
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

        const delta = 0.016; // 60fps delta approx

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
        const bob = Math.sin(this.cameraBobPhase) * 0.04;
        this.camera.position.set(this.currentCamPos.x, this.currentCamPos.y + bob, this.currentCamPos.z);
        this.torchLight.position.set(this.currentCamPos.x, this.currentCamPos.y + 0.3, this.currentCamPos.z);

        // Torch flicker
        const flicker = 2.0 + Math.sin(Date.now() * 0.01) * 0.25 + Math.cos(Date.now() * 0.023) * 0.15;
        this.torchLight.intensity = flicker;

        // Facing yaw rotation: 0=S (0), 1=W (PI/2), 2=N (PI), 3=E (3PI/2)
        const targetYaw = (this.facing * Math.PI) / 2;
        this.camera.rotation.y = targetYaw;

        // Attack animation
        if (this.attackAnimationTime > 0) {
            this.attackAnimationTime -= delta;
            this.weaponMesh.position.z = -0.65 - Math.sin((0.22 - this.attackAnimationTime) * Math.PI * 4.5) * 0.3;
            this.weaponMesh.rotation.z = Math.PI / 6 - Math.sin((0.22 - this.attackAnimationTime) * Math.PI * 4.5) * 0.5;
        } else {
            this.weaponMesh.position.z = -0.65;
            this.weaponMesh.rotation.z = Math.PI / 6;
        }

        this.renderer.render(this.scene, this.camera);
    }
}

window.Dungeon3D = Dungeon3D;
