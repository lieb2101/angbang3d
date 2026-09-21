/**
 * Angband3D WebGL Renderer — High-Fidelity Three.js 3D Dungeon Crawler
 * Features:
 * - 1:1 Parity with Godot C# client's wire protocol (decodes map.rows[y].f and map.rows[y].l)
 * - Authentic 2K PBR stone, uneven brick, rock trim, and wood trim textures with normal & roughness maps
 * - ACESFilmic tone mapping, warm organic torchlight attenuation, and atmospheric distance fog
 * - Dynamic Molten Lava: glowing emissive when in LOS, cooled dark basalt in memory (0 glow-through)
 * - InstancedMesh batching with 8-neighbor rock culling, radial horizon culling, and memory shading
 * - 3D Steel Weapon (OBJLoader + PBR materials) and animated 3D Torch in first-person viewmodel
 * - High-DPI 512x512 Runic Monster Medallions with health bars, glyphs, and nameplates
 * - Distinct 3D pickups (Gold stacks, Potions, Scrolls, Weapons, Rings) with hovering bobbing kinematics
 * - 1:1 Camera Facing and Turning (0=North, 1=East, 2=South, 3=West) with smooth spring yaw rotation
 */

class Dungeon3D {
    constructor(canvasId) {
        this.canvas = document.getElementById(canvasId);
        this.cellSize = 2.0; // 2.0m per grid cell matching Godot DungeonWorld
        this.wallHeight = 3.0;
        this.eyeHeight = 1.62;

        // Facing: 0=North (-Z), 1=East (+X), 2=South (+Z), 3=West (-X)
        // 1:1 mathematical match with Godot DungeonWorld._facing and camera convention
        this.facing = 0;
        this.targetYaw = 0;
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
        this.scene.background = new THREE.Color(0x07090e);
        this.scene.fog = new THREE.FogExp2(0x07090e, 0.038);

        this.camera = new THREE.PerspectiveCamera(72, window.innerWidth / window.innerHeight, 0.1, 140);
        this.camera.position.set(0, this.eyeHeight, 0);
        this.camera.rotation.y = 0; // Starts looking North (-Z)

        this.renderer = new THREE.WebGLRenderer({
            canvas: this.canvas,
            antialias: true,
            powerPreference: 'high-performance'
        });
        this.renderer.setSize(window.innerWidth, window.innerHeight);
        this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
        this.renderer.toneMappingExposure = 1.25;
        this.renderer.outputEncoding = THREE.sRGBEncoding;

        // Ambient dungeon light with cool slate blue tint
        this.ambientLight = new THREE.AmbientLight(0x182030, 0.55);
        this.scene.add(this.ambientLight);

        // Player Torch PointLight: warm amber glow with realistic falloff
        this.torchLight = new THREE.PointLight(0xff9e42, 2.8, 22, 1.8);
        this.torchLight.position.set(0, this.eyeHeight + 0.2, 0);
        this.scene.add(this.torchLight);

        // Secondary warm bounce fill light near player feet
        this.torchFillLight = new THREE.PointLight(0xff5511, 0.8, 8, 2.0);
        this.torchFillLight.position.set(0, this.eyeHeight - 0.2, 0);
        this.scene.add(this.torchFillLight);

        window.addEventListener('resize', () => {
            this.camera.aspect = window.innerWidth / window.innerHeight;
            this.camera.updateProjectionMatrix();
            this.renderer.setSize(window.innerWidth, window.innerHeight);
        });
    }

    initTextures() {
        const loader = new THREE.TextureLoader();
        const maxAniso = this.renderer.capabilities.getMaxAnisotropy();

        const loadPbr = (url, isSRGB, repX = 1, repY = 1) => {
            const tex = loader.load(url);
            tex.wrapS = THREE.RepeatWrapping;
            tex.wrapT = THREE.RepeatWrapping;
            tex.repeat.set(repX, repY);
            if (isSRGB) {
                tex.encoding = THREE.sRGBEncoding;
            }
            tex.anisotropy = maxAniso;
            return tex;
        };

        // Authentic 2K PBR Textures from Godot Client Town/Dungeon Assets
        this.wallTex = loadPbr('/assets/textures/T_Brick_BaseColor.png', true, 1, 1.5);
        this.wallNormal = loadPbr('/assets/textures/T_Brick_Normal.png', false, 1, 1.5);
        this.wallRoughness = loadPbr('/assets/textures/T_Brick_Roughness.png', false, 1, 1.5);

        this.floorTex = loadPbr('/assets/textures/T_UnevenBrick_BaseColor.png', true, 1, 1);
        this.floorNormal = loadPbr('/assets/textures/T_UnevenBrick_Normal.png', false, 1, 1);
        this.floorRoughness = loadPbr('/assets/textures/T_UnevenBrick_Roughness.png', false, 1, 1);

        this.ceilingTex = loadPbr('/assets/textures/T_RockTrim_BaseColor.png', true, 1, 1);
        this.ceilingNormal = loadPbr('/assets/textures/T_RockTrim_Normal.png', false, 1, 1);

        this.doorTex = loadPbr('/assets/textures/T_WoodTrim_BaseColor.png', true, 1, 1.5);
        this.doorNormal = loadPbr('/assets/textures/T_WoodTrim_Normal.png', false, 1, 1.5);
        this.doorRoughness = loadPbr('/assets/textures/T_WoodTrim_Roughness.png', false, 1, 1.5);

        this.storeTex = loadPbr('/assets/textures/T_Plaster_BaseColor.png', true, 1, 1);
        this.storeNormal = loadPbr('/assets/textures/T_Plaster_Normal.png', false, 1, 1);

        // Materials with Authentic PBR Settings matching Godot StandardMaterial3D
        this.wallMaterial = new THREE.MeshStandardMaterial({
            map: this.wallTex,
            normalMap: this.wallNormal,
            normalScale: new THREE.Vector2(0.85, 0.85),
            roughnessMap: this.wallRoughness,
            roughness: 0.80,
            metalness: 0.02
        });

        this.floorMaterial = new THREE.MeshStandardMaterial({
            map: this.floorTex,
            normalMap: this.floorNormal,
            normalScale: new THREE.Vector2(0.80, 0.80),
            roughnessMap: this.floorRoughness,
            roughness: 0.74,
            metalness: 0.04
        });

        this.ceilingMaterial = new THREE.MeshStandardMaterial({
            map: this.ceilingTex,
            normalMap: this.ceilingNormal,
            normalScale: new THREE.Vector2(0.55, 0.55),
            roughness: 0.95,
            metalness: 0.0
        });

        this.doorMaterial = new THREE.MeshStandardMaterial({
            map: this.doorTex,
            normalMap: this.doorNormal,
            normalScale: new THREE.Vector2(0.80, 0.80),
            roughnessMap: this.doorRoughness,
            roughness: 0.75,
            metalness: 0.10
        });

        this.storeMaterial = new THREE.MeshStandardMaterial({
            map: this.storeTex,
            normalMap: this.storeNormal,
            roughness: 0.85,
            metalness: 0.02
        });

        // In-View Molten Lava (Glows, pulsates)
        this.lavaMaterial = new THREE.MeshStandardMaterial({
            color: 0xff3300,
            emissive: 0xff4400,
            emissiveIntensity: 2.2,
            roughness: 0.35,
            metalness: 0.10
        });

        // Cooled Basalt (Memory/Fog-of-War Lava: Dark, ZERO emission)
        this.lavaCooledMaterial = new THREE.MeshStandardMaterial({
            color: 0x141210,
            roughness: 0.95,
            metalness: 0.0
        });

        this.stairsMaterial = new THREE.MeshStandardMaterial({
            map: this.wallTex,
            normalMap: this.wallNormal,
            roughness: 0.82,
            metalness: 0.04
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

        // High-Quality Procedural Base Blade
        const bladeGeo = new THREE.BoxGeometry(0.042, 0.78, 0.012);
        const bladeMat = new THREE.MeshStandardMaterial({
            color: 0xd8e4ed,
            roughness: 0.16,
            metalness: 0.95
        });
        const blade = new THREE.Mesh(bladeGeo, bladeMat);
        blade.position.y = 0.39;
        swordGroup.add(blade);

        // Crossguard
        const guardGeo = new THREE.BoxGeometry(0.20, 0.026, 0.038);
        const guardMat = new THREE.MeshStandardMaterial({
            color: 0xc99c3a,
            roughness: 0.30,
            metalness: 0.88
        });
        const guard = new THREE.Mesh(guardGeo, guardMat);
        guard.position.y = 0.01;
        swordGroup.add(guard);

        // Leather Grip
        const gripGeo = new THREE.CylinderGeometry(0.018, 0.018, 0.15, 8);
        const gripMat = new THREE.MeshStandardMaterial({
            color: 0x3d2618,
            roughness: 0.88
        });
        const grip = new THREE.Mesh(gripGeo, gripMat);
        grip.position.y = -0.075;
        swordGroup.add(grip);

        // Pommel
        const pommelGeo = new THREE.SphereGeometry(0.028, 8, 8);
        const pommel = new THREE.Mesh(pommelGeo, guardMat);
        pommel.position.y = -0.16;
        swordGroup.add(pommel);

        swordGroup.position.set(0.35, -0.30, -0.60);
        swordGroup.rotation.set(0.12, -0.18, 0.28);
        this.weaponMesh = swordGroup;
        this.viewmodelGroup.add(swordGroup);

        // Asynchronously load 3D OBJ model if OBJLoader is present
        if (typeof THREE.OBJLoader !== 'undefined') {
            const objLoader = new THREE.OBJLoader();
            objLoader.load('/assets/models/weapons/Sword.obj', (obj) => {
                obj.traverse((child) => {
                    if (child.isMesh) {
                        const n = (child.name || '').toLowerCase();
                        if (n.includes('guard') || n.includes('pommel') || n.includes('gold')) {
                            child.material = guardMat;
                        } else if (n.includes('grip') || n.includes('handle') || n.includes('wood')) {
                            child.material = gripMat;
                        } else {
                            child.material = bladeMat;
                        }
                    }
                });
                obj.scale.set(0.26, 0.26, 0.26);
                obj.position.set(0, 0.05, 0);
                obj.rotation.set(0, Math.PI / 2, 0);

                while (swordGroup.children.length > 0) {
                    swordGroup.remove(swordGroup.children[0]);
                }
                swordGroup.add(obj);
            }, undefined, () => {});
        }

        // Left Hand: 3D Wooden Torch with Flickering Flame & Embers
        const torchGroup = new THREE.Group();

        const torchStaffGeo = new THREE.CylinderGeometry(0.022, 0.030, 0.55, 8);
        const torchStaffMat = new THREE.MeshStandardMaterial({
            map: this.doorTex,
            roughness: 0.85
        });
        const torchStaff = new THREE.Mesh(torchStaffGeo, torchStaffMat);
        torchStaff.position.y = -0.15;
        torchGroup.add(torchStaff);

        const sconceGeo = new THREE.CylinderGeometry(0.045, 0.035, 0.08, 8);
        const sconceMat = new THREE.MeshStandardMaterial({ color: 0x1e1e1e, metalness: 0.85, roughness: 0.35 });
        const sconce = new THREE.Mesh(sconceGeo, sconceMat);
        sconce.position.y = 0.12;
        torchGroup.add(sconce);

        // Outer Flame Cone
        const flameGeo = new THREE.ConeGeometry(0.058, 0.18, 8);
        const flameMat = new THREE.MeshBasicMaterial({ color: 0xff8811 });
        this.flameMesh = new THREE.Mesh(flameGeo, flameMat);
        this.flameMesh.position.y = 0.23;
        torchGroup.add(this.flameMesh);

        // Inner Core Flame (Bright Yellow/White)
        const innerFlameGeo = new THREE.ConeGeometry(0.032, 0.12, 8);
        const innerFlameMat = new THREE.MeshBasicMaterial({ color: 0xffffaa });
        this.innerFlameMesh = new THREE.Mesh(innerFlameGeo, innerFlameMat);
        this.innerFlameMesh.position.y = 0.21;
        torchGroup.add(this.innerFlameMesh);

        torchGroup.position.set(-0.35, -0.28, -0.56);
        torchGroup.rotation.set(0.15, 0.25, -0.15);
        this.torchHandle = torchGroup;
        this.viewmodelGroup.add(torchGroup);

        this.attackAnimationTime = 0;
    }

    triggerAttackAnimation() {
        this.attackAnimationTime = 0.22; // 220ms swing
    }

    /**
     * Turn camera orientation in place.
     * 1:1 mathematical parity with Godot DungeonWorld.Turn(delta):
     * delta: +1 = turn right 90°, -1 = turn left 90°.
     * Costs 0 game turns (Angband has no facing).
     */
    turn(delta) {
        this.facing = ((this.facing + delta) % 4 + 4) % 4;
        this.targetYaw = -this.facing * (Math.PI / 2);
    }

    getFeatAt(map, x, y) {
        if (!map || !map.rows || !map.rows[y] || !map.rows[y].f) return 0;
        const row = map.rows[y].f;
        return (row.length > x) ? row[x] : 0;
    }

    getFlagAt(map, x, y) {
        if (!map || !map.rows || !map.rows[y] || !map.rows[y].l) return 0;
        const row = map.rows[y].l;
        return (row.length > x) ? row[x] : 0;
    }

    update(frame) {
        if (!frame || !frame.map) return;
        this.updateDungeon(frame);
    }

    updateDungeon(frame) {
        const map = frame.map;
        const w = map.w || (map.rows && map.rows[0] && map.rows[0].f ? map.rows[0].f.length : 0);
        const h = map.h || (map.rows ? map.rows.length : 0);
        if (!w || !h) return;

        const px = frame.player ? frame.player.x : Math.floor(w / 2);
        const py = frame.player ? frame.player.y : Math.floor(h / 2);

        this.targetCamPos.set(px * this.cellSize, this.eyeHeight, py * this.cellSize);

        const dist = this.currentCamPos.distanceTo(this.targetCamPos);
        if (dist > this.cellSize * 1.5) {
            // Level transition / teleport: snap camera instantly
            this.currentCamPos.copy(this.targetCamPos);
            this.isStepping = false;
        } else if (dist > 0.01) {
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

        const maxR = 28; // Radial horizon culling matching Godot DungeonWorld.cs

        const colInView = new THREE.Color(1.0, 1.0, 1.0);
        const colMemory = new THREE.Color(0.28, 0.30, 0.38); // Cool dark slate memory tint

        for (let y = Math.max(0, py - maxR); y < Math.min(h, py + maxR); y++) {
            for (let x = Math.max(0, px - maxR); x < Math.min(w, px + maxR); x++) {
                const feat = this.getFeatAt(map, x, y);
                const flag = this.getFlagAt(map, x, y);

                const known = (flag & 0x1) !== 0;
                const inView = (flag & 0x2) !== 0;
                // Strict Fog-of-War Invariant: Unexplored tiles are skipped completely and remain pure dark void!
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

    createMonsterToken(m) {
        const canvas = document.createElement('canvas');
        canvas.width = 512;
        canvas.height = 512;
        const ctx = canvas.getContext('2d');

        const cx = 256;
        const cy = 230;
        const r = 160;

        // Outer ambient shadow
        const glow = ctx.createRadialGradient(cx, cy, r * 0.8, cx, cy, r * 1.3);
        glow.addColorStop(0, 'rgba(0, 0, 0, 0.6)');
        glow.addColorStop(1, 'rgba(0, 0, 0, 0)');
        ctx.fillStyle = glow;
        ctx.beginPath();
        ctx.arc(cx, cy, r * 1.3, 0, Math.PI * 2);
        ctx.fill();

        // Metallic Rim (Antique gold for uniques/bosses, heavy steel for regular creatures)
        const isUnique = (m.name || '').includes('the ') || (m.glyph && m.glyph === m.glyph.toUpperCase());
        const rimGrad = ctx.createLinearGradient(cx - r, cy - r, cx + r, cy + r);
        if (isUnique) {
            rimGrad.addColorStop(0, '#ffe57f');
            rimGrad.addColorStop(0.5, '#b8860b');
            rimGrad.addColorStop(1, '#ffd700');
        } else {
            rimGrad.addColorStop(0, '#9eabb8');
            rimGrad.addColorStop(0.5, '#3a424e');
            rimGrad.addColorStop(1, '#7a8594');
        }

        ctx.fillStyle = rimGrad;
        ctx.beginPath();
        ctx.arc(cx, cy, r, 0, Math.PI * 2);
        ctx.fill();

        // Inner Stone Medallion
        const innerGrad = ctx.createRadialGradient(cx, cy, 10, cx, cy, r - 14);
        innerGrad.addColorStop(0, '#222834');
        innerGrad.addColorStop(0.7, '#131720');
        innerGrad.addColorStop(1, '#0b0d13');
        ctx.fillStyle = innerGrad;
        ctx.beginPath();
        ctx.arc(cx, cy, r - 14, 0, Math.PI * 2);
        ctx.fill();

        // Inner engraved circle
        ctx.strokeStyle = isUnique ? 'rgba(255, 215, 0, 0.45)' : 'rgba(160, 180, 200, 0.28)';
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.arc(cx, cy, r - 24, 0, Math.PI * 2);
        ctx.stroke();

        // Creature Glyph in glowing Cinzel/Roguelike font
        ctx.font = '900 160px "Cinzel", "Fira Code", monospace';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.shadowColor = m.color || '#ff4444';
        ctx.shadowBlur = 24;
        ctx.fillStyle = m.color || '#ffffff';
        ctx.fillText(m.glyph || 'M', cx, cy - 4);
        ctx.shadowBlur = 0;

        // Health Bar overhead
        const barW = 240;
        const barH = 22;
        const barX = cx - barW / 2;
        const barY = 32;

        ctx.fillStyle = 'rgba(0, 0, 0, 0.85)';
        ctx.strokeStyle = '#333e4c';
        ctx.lineWidth = 3;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(barX, barY, barW, barH, 6);
        else ctx.rect(barX, barY, barW, barH);
        ctx.fill();
        ctx.stroke();

        const hp = m.hp !== undefined ? m.hp : 100;
        const maxHp = m.maxhp !== undefined ? m.maxhp : 100;
        const pct = Math.max(0, Math.min(1, maxHp > 0 ? hp / maxHp : 1));
        const hpFillW = Math.max(4, (barW - 4) * pct);

        let hpColor = '#38e078';
        if (pct < 0.3) hpColor = '#ff3b30';
        else if (pct < 0.6) hpColor = '#ffcc00';

        ctx.fillStyle = hpColor;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(barX + 2, barY + 2, hpFillW, barH - 4, 4);
        else ctx.rect(barX + 2, barY + 2, hpFillW, barH - 4);
        ctx.fill();

        // Name Banner at the bottom
        if (m.name) {
            const name = m.name.length > 20 ? m.name.substring(0, 19) + '…' : m.name;
            ctx.font = '700 32px "Cinzel", serif';
            const textMetrics = ctx.measureText(name);
            const bannerW = Math.min(460, textMetrics.width + 44);
            const bannerH = 44;
            const bannerX = cx - bannerW / 2;
            const bannerY = cy + r - 10;

            ctx.fillStyle = 'rgba(10, 12, 18, 0.92)';
            ctx.strokeStyle = isUnique ? '#d4af37' : '#556272';
            ctx.lineWidth = 2;
            ctx.beginPath();
            if (ctx.roundRect) ctx.roundRect(bannerX, bannerY, bannerW, bannerH, 8);
            else ctx.rect(bannerX, bannerY, bannerW, bannerH);
            ctx.fill();
            ctx.stroke();

            ctx.fillStyle = isUnique ? '#ffd700' : '#e6edf5';
            ctx.fillText(name, cx, bannerY + bannerH / 2);
        }

        const tex = new THREE.CanvasTexture(canvas);
        tex.minFilter = THREE.LinearFilter;
        return tex;
    }

    updateMonsters(monsters) {
        const activeIds = new Set();

        monsters.forEach(m => {
            const id = m.id || `${m.x}_${m.y}_${m.glyph}`;
            activeIds.add(id);
            let sprite = this.monsters.get(id);

            if (!sprite) {
                const tex = this.createMonsterToken(m);
                const mat = new THREE.SpriteMaterial({ map: tex, depthWrite: false });
                sprite = new THREE.Sprite(mat);
                sprite.scale.set(1.5, 1.5, 1.5);
                sprite.position.set(m.x * this.cellSize, 0.9, m.y * this.cellSize);
                sprite.targetPos = new THREE.Vector3(m.x * this.cellSize, 0.9, m.y * this.cellSize);
                this.scene.add(sprite);
                this.monsters.set(id, sprite);
            } else {
                sprite.targetPos = new THREE.Vector3(m.x * this.cellSize, 0.9, m.y * this.cellSize);
            }
        });

        for (const [id, sprite] of this.monsters.entries()) {
            if (!activeIds.has(id)) {
                this.scene.remove(sprite);
                this.monsters.delete(id);
            }
        }
    }

    createItemMesh(it) {
        const g = it.glyph || '?';
        const group = new THREE.Group();

        if (g === '$') {
            // Gold pile: stack of gleaming coins
            const coinMat = new THREE.MeshStandardMaterial({
                color: 0xffd700,
                emissive: 0x553300,
                metalness: 0.95,
                roughness: 0.18
            });
            for (let i = 0; i < 3; i++) {
                const coin = new THREE.Mesh(new THREE.CylinderGeometry(0.14, 0.14, 0.04, 12), coinMat);
                coin.position.set((i - 1) * 0.05, i * 0.045, (i % 2) * 0.03);
                group.add(coin);
            }
        } else if (g === '!') {
            // Potion: glass flask with glowing liquid
            const glassMat = new THREE.MeshStandardMaterial({
                color: 0xff3355,
                emissive: 0xaa1122,
                roughness: 0.15,
                metalness: 0.20
            });
            const body = new THREE.Mesh(new THREE.CylinderGeometry(0.08, 0.11, 0.22, 10), glassMat);
            const neck = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 0.08, 8), glassMat);
            neck.position.y = 0.14;
            group.add(body);
            group.add(neck);
        } else if (g === '?') {
            // Scroll: rolled parchment with wax seal
            const scrollMat = new THREE.MeshStandardMaterial({
                color: 0xdfd4a8,
                roughness: 0.85
            });
            const roll = new THREE.Mesh(new THREE.CylinderGeometry(0.055, 0.055, 0.32, 10), scrollMat);
            roll.rotation.z = Math.PI / 4;
            group.add(roll);
        } else if (g === '=' || g === '"') {
            // Ring or Amulet: gleaming torus
            const ringMat = new THREE.MeshStandardMaterial({
                color: 0xffcc33,
                emissive: 0x664400,
                metalness: 0.95,
                roughness: 0.20
            });
            const ring = new THREE.Mesh(new THREE.TorusGeometry(0.12, 0.036, 8, 16), ringMat);
            group.add(ring);
        } else {
            // Weapon, Armor, or Generic Loot
            const gemMat = new THREE.MeshStandardMaterial({
                color: 0x44bbff,
                emissive: 0x114488,
                metalness: 0.85,
                roughness: 0.25
            });
            const gem = new THREE.Mesh(new THREE.OctahedronGeometry(0.20, 0), gemMat);
            group.add(gem);
        }

        return group;
    }

    updateItems(items) {
        const activeIds = new Set();

        items.forEach(it => {
            const id = `${it.x}_${it.y}_${it.name || it.glyph}`;
            activeIds.add(id);
            let mesh = this.items.get(id);

            if (!mesh) {
                mesh = this.createItemMesh(it);
                this.scene.add(mesh);
                this.items.set(id, mesh);
            }

            mesh.position.set(it.x * this.cellSize, 0.35, it.y * this.cellSize);
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
        this.torchFillLight.position.set(this.currentCamPos.x, this.currentCamPos.y - 0.2, this.currentCamPos.z);

        // Torch flame intensity multi-wave organic flicker
        const tNow = Date.now();
        const flicker = 2.6 +
            Math.sin(tNow * 0.011) * 0.22 +
            Math.cos(tNow * 0.024) * 0.15 +
            Math.sin(tNow * 0.037) * 0.08;
        this.torchLight.intensity = flicker;

        // Torch flame mesh scale flicker
        if (this.flameMesh) {
            this.flameMesh.scale.set(
                1.0 + Math.sin(tNow * 0.015) * 0.14,
                1.0 + Math.cos(tNow * 0.022) * 0.22,
                1.0 + Math.sin(tNow * 0.015) * 0.14
            );
        }
        if (this.innerFlameMesh) {
            this.innerFlameMesh.scale.set(
                1.0 + Math.cos(tNow * 0.018) * 0.12,
                1.0 + Math.sin(tNow * 0.025) * 0.18,
                1.0 + Math.cos(tNow * 0.018) * 0.12
            );
        }

        // Facing yaw rotation:
        // facing: 0=N (0), 1=E (-PI/2), 2=S (-PI), 3=W (-3PI/2 or +PI/2)
        const targetYaw = -this.facing * (Math.PI / 2);
        const yawDiff = targetYaw - this.camera.rotation.y;
        const wrappedDiff = Math.atan2(Math.sin(yawDiff), Math.cos(yawDiff));
        if (Math.abs(wrappedDiff) > 0.001) {
            this.camera.rotation.y += wrappedDiff * Math.min(1.0, delta * 22);
        } else {
            this.camera.rotation.y = targetYaw;
        }

        // Attack animation
        if (this.attackAnimationTime > 0) {
            this.attackAnimationTime -= delta;
            this.weaponMesh.position.z = -0.60 - Math.sin((0.22 - this.attackAnimationTime) * Math.PI * 4.5) * 0.25;
            this.weaponMesh.rotation.z = 0.28 - Math.sin((0.22 - this.attackAnimationTime) * Math.PI * 4.5) * 0.45;
        } else {
            this.weaponMesh.position.z = -0.60;
            this.weaponMesh.rotation.z = 0.28;
        }

        // Monsters gentle hover & movement interpolation
        const monTime = tNow * 0.003;
        for (const sprite of this.monsters.values()) {
            if (sprite.targetPos) {
                sprite.position.lerp(sprite.targetPos, 0.22);
            }
            sprite.position.y = (sprite.targetPos ? sprite.targetPos.y : 0.9) + Math.sin(monTime + sprite.position.x) * 0.04;
        }

        // Items gentle hover bob & rotation
        for (const mesh of this.items.values()) {
            mesh.rotation.y += 0.028;
            mesh.position.y = 0.35 + Math.sin(tNow * 0.004 + mesh.position.x) * 0.05;
        }

        this.renderer.render(this.scene, this.camera);
    }
}

window.Dungeon3D = Dungeon3D;
