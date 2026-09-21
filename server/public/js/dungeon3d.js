/**
 * Angband3D WebGL Renderer — High-Fidelity Three.js 3D Dungeon Crawler
 * Features:
 * - 1:1 Parity with Godot C# client's wire protocol (decodes 2-hex map.rows[y].f and hex map.rows[y].l)
 * - Authentic 2K PBR stone, uneven brick, rock trim, and wood trim textures with normal & roughness maps
 * - Dynamic Town vs Subterranean Lighting:
 *   - Town (depth 0): Open daylight sky, Directional Sunlight (intensity 1.35), broad horizon, NO ceiling!
 *   - Dungeon (depth > 0): Pitch-black void, atmospheric depth fog, ceilings over tunnels, warm torchlight!
 * - Equipment Rule Parity with Godot ViewModel.cs:
 *   - Carrying a torch in the light slot illuminates the player without cluttering their hands!
 *   - Left hand ONLY shows an equipped shield or wielded torch weapon. Empty by default!
 *   - Right hand ONLY shows an equipped melee weapon or bow, tucked cleanly in lower corner. Empty if unarmed!
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
        this.scene.background = new THREE.Color(0x2d3a50); // Initial town sky
        this.scene.fog = new THREE.FogExp2(0x2d3a50, 0.008);

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

        // Ambient light (dynamically adjusted per depth / town)
        this.ambientLight = new THREE.AmbientLight(0xb0c4de, 0.85);
        this.scene.add(this.ambientLight);

        // Directional Sunlight for outdoors (Town) matching Godot _sunLight
        this.sunLight = new THREE.DirectionalLight(0xfffaed, 1.35);
        this.sunLight.position.set(-20, 35, 15);
        this.scene.add(this.sunLight);

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

        // Right Hand Weapon: Primary melee weapon / bow.
        // Hidden by default when unarmed, matching Godot client!
        this.rightHandGroup = new THREE.Group();
        this.rightHandGroup.position.set(0.25, -0.24, -0.36);
        this.rightHandGroup.rotation.set(0.18, -0.22, 0.15);
        this.rightHandGroup.visible = false;
        this.viewmodelGroup.add(this.rightHandGroup);

        // Left Hand: Shield or wielded torch weapon.
        // Hidden by default when off-hand is empty, matching Godot client!
        this.leftHandGroup = new THREE.Group();
        this.leftHandGroup.position.set(-0.25, -0.24, -0.34);
        this.leftHandGroup.rotation.set(0.12, 0.18, -0.10);
        this.leftHandGroup.visible = false;
        this.viewmodelGroup.add(this.leftHandGroup);

        const objLoader = (typeof THREE.OBJLoader !== 'undefined') ? new THREE.OBJLoader() : null;

        const bladeMat = new THREE.MeshStandardMaterial({ color: 0xd8e4ed, roughness: 0.16, metalness: 0.95 });
        const goldMat = new THREE.MeshStandardMaterial({ color: 0xc99c3a, roughness: 0.30, metalness: 0.88 });
        const gripMat = new THREE.MeshStandardMaterial({ color: 0x3d2618, roughness: 0.88 });

        if (objLoader) {
            // Load 3D Sword for right hand
            objLoader.load('/assets/models/weapons/Sword.obj', (obj) => {
                obj.traverse((child) => {
                    if (child.isMesh) {
                        const n = (child.name || '').toLowerCase();
                        if (n.includes('guard') || n.includes('pommel') || n.includes('gold')) {
                            child.material = goldMat;
                        } else if (n.includes('grip') || n.includes('handle') || n.includes('wood')) {
                            child.material = gripMat;
                        } else {
                            child.material = bladeMat;
                        }
                    }
                });
                obj.scale.set(0.14, 0.14, 0.14);
                obj.position.set(0, -0.02, 0);
                obj.rotation.set(0, Math.PI / 2, 0);
                this.rightHandGroup.add(obj);
                this.swordMesh = obj;
            }, undefined, () => {});

            // Load 3D Shield for left hand
            objLoader.load('/assets/models/weapons/Shield_Round.obj', (obj) => {
                obj.traverse((child) => {
                    if (child.isMesh) {
                        child.material = goldMat;
                    }
                });
                obj.scale.set(0.11, 0.11, 0.11);
                obj.position.set(0, 0, 0);
                this.leftHandGroup.add(obj);
                this.shieldMesh = obj;
                this.shieldMesh.visible = false;
            }, undefined, () => {});
        }

        // Compact handheld torch (ONLY visible if specifically wielding a torch weapon)
        const torchStaffGeo = new THREE.CylinderGeometry(0.015, 0.020, 0.38, 8);
        const torchStaffMat = new THREE.MeshStandardMaterial({ map: this.doorTex, roughness: 0.85 });
        const torchStaff = new THREE.Mesh(torchStaffGeo, torchStaffMat);
        torchStaff.position.y = -0.10;

        const sconceGeo = new THREE.CylinderGeometry(0.030, 0.025, 0.06, 8);
        const sconceMat = new THREE.MeshStandardMaterial({ color: 0x1e1e1e, metalness: 0.85, roughness: 0.35 });
        const sconce = new THREE.Mesh(sconceGeo, sconceMat);
        sconce.position.y = 0.10;

        const flameGeo = new THREE.ConeGeometry(0.035, 0.12, 8);
        const flameMat = new THREE.MeshBasicMaterial({ color: 0xff8811 });
        this.flameMesh = new THREE.Mesh(flameGeo, flameMat);
        this.flameMesh.position.y = 0.18;

        const innerFlameGeo = new THREE.ConeGeometry(0.018, 0.08, 8);
        const innerFlameMat = new THREE.MeshBasicMaterial({ color: 0xffffaa });
        this.innerFlameMesh = new THREE.Mesh(innerFlameGeo, innerFlameMat);
        this.innerFlameMesh.position.y = 0.16;

        this.torchMesh = new THREE.Group();
        this.torchMesh.add(torchStaff);
        this.torchMesh.add(sconce);
        this.torchMesh.add(this.flameMesh);
        this.torchMesh.add(this.innerFlameMesh);
        this.torchMesh.visible = false;
        this.leftHandGroup.add(this.torchMesh);

        this.attackAnimationTime = 0;
    }

    updateViewmodel(player) {
        if (!player) return;

        const weaponItem = player.weapon_item || '';
        const bowItem = player.bow_item || '';
        const shieldItem = player.shield_item || '';

        // LEFT HAND RULE:
        // Carrying a torch/lantern in the equipment light slot illuminates without occupying hands!
        // The left hand only shows an item if wielding a shield or using a torch as a weapon.
        const isWieldingTorch = weaponItem.toLowerCase().includes('torch');
        const hasShield = Boolean(shieldItem);

        if (hasShield && this.shieldMesh) {
            this.leftHandGroup.visible = true;
            this.shieldMesh.visible = true;
            if (this.torchMesh) this.torchMesh.visible = false;
        } else if (isWieldingTorch && this.torchMesh) {
            this.leftHandGroup.visible = true;
            if (this.shieldMesh) this.shieldMesh.visible = false;
            this.torchMesh.visible = true;
        } else {
            this.leftHandGroup.visible = false;
        }

        // RIGHT HAND RULE:
        // Shows primary equipped weapon or bow. If unarmed, hands are clean & empty!
        const hasWeapon = Boolean(weaponItem || bowItem);
        this.rightHandGroup.visible = hasWeapon;
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

    /**
     * Canonical 2-hex feature index decoder matching Angband JSON bridge protocol
     */
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

    /**
     * Canonical 1-hex flag decoder matching Angband JSON bridge protocol
     */
    getFlagAt(map, x, y) {
        if (!map || !map.rows || y < 0 || y >= map.h || x < 0 || x >= map.w) return 0;
        const row = map.rows[y];
        if (!row || !row.l || x >= row.l.length) return 0;
        return parseInt(row.l[x], 16) || 0;
    }

    update(frame) {
        if (!frame || !frame.map) return;
        this.updateDungeon(frame);
    }

    updateDungeon(frame) {
        const map = frame.map;
        const w = map.w || (map.rows && map.rows[0] && map.rows[0].f ? map.rows[0].f.length / 2 : 0);
        const h = map.h || (map.rows ? map.rows.length : 0);
        if (!w || !h) return;

        const px = frame.player ? frame.player.x : Math.floor(w / 2);
        const py = frame.player ? frame.player.y : Math.floor(h / 2);
        const depth = (frame.player && frame.player.depth !== undefined) ? frame.player.depth : 0;
        const outdoors = depth === 0; // Town is outdoors under daylight

        // Update Viewmodel Equipment rules
        this.updateViewmodel(frame.player);

        // Apply Town vs Dungeon Lighting & Atmosphere
        if (outdoors) {
            this.sunLight.visible = true;
            this.sunLight.intensity = 1.35;
            this.ambientLight.intensity = 0.85;
            this.ambientLight.color.setHex(0xb0c4de);
            this.scene.background.setHex(0x2d3a50);
            this.scene.fog.color.setHex(0x2d3a50);
            this.scene.fog.density = 0.008; // Clear vistas in town
            this.torchLight.intensity = 0.5; // Subtle in daylight
            this.torchFillLight.intensity = 0.0;
        } else {
            this.sunLight.visible = false;
            this.sunLight.intensity = 0.0;
            this.ambientLight.intensity = 0.45;
            this.ambientLight.color.setHex(0x182030);
            this.scene.background.setHex(0x06070a);
            this.scene.fog.color.setHex(0x06070a);
            this.scene.fog.density = 0.038; // Atmospheric dungeon fog
            this.torchLight.intensity = 2.8;
            this.torchFillLight.intensity = 0.8;
        }

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

        // Sight bounding box: Town has full sightline, dungeon uses radial horizon culling
        const sightRange = outdoors ? 45 : 28;
        const minX = Math.max(0, px - sightRange);
        const maxX = Math.min(w - 1, px + sightRange);
        const minY = Math.max(0, py - sightRange);
        const maxY = Math.min(h - 1, py + sightRange);

        const colInView = new THREE.Color(1.0, 1.0, 1.0);
        const colMemory = new THREE.Color(0.28, 0.30, 0.38); // Cool dark slate memory tint

        for (let y = minY; y <= maxY; y++) {
            for (let x = minX; x <= maxX; x++) {
                const feat = this.getFeatAt(map, x, y);
                const flag = this.getFlagAt(map, x, y);

                // In Town, all grids are explored & visible
                const known = outdoors || (flag & 0x1) !== 0;
                const inView = outdoors || (flag & 0x2) !== 0;

                // Strict Fog-of-War Invariant: Unexplored tiles are skipped completely and remain pure dark void!
                if (!known && !inView) continue;

                if (!outdoors) {
                    const dx = x - px;
                    const dy = y - py;
                    if (dx * dx + dy * dy > 28 * 28) continue;
                }

                const wx = x * this.cellSize;
                const wz = y * this.cellSize;
                const tileCol = inView ? colInView : colMemory;

                // Solid stone / Granite / Perm / Secret wall
                const isWall = feat === 15 || (feat >= 17 && feat <= 22);
                if (isWall) {
                    let hasExposedFace = false;
                    for (let dy = -1; dy <= 1; dy++) {
                        for (let dx = -1; dx <= 1; dx++) {
                            if (dx === 0 && dy === 0) continue;
                            const nx = x + dx;
                            const ny = y + dy;
                            if (nx >= 0 && nx < w && ny >= 0 && ny < h) {
                                const nFeat = this.getFeatAt(map, nx, ny);
                                // Open space: floor (1, 24), doors (2, 3, 4), stairs (5, 6), stores (7-14), lava (23)
                                if (nFeat === 1 || nFeat === 2 || nFeat === 3 || nFeat === 4 || nFeat === 5 || nFeat === 6 || (nFeat >= 7 && nFeat <= 14) || nFeat === 23 || nFeat === 24) {
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
                if (feat === 1 || feat === 3 || feat === 4 || feat === 24 || (feat >= 7 && feat <= 14)) {
                    this.dummy.position.set(wx, 0, wz);
                    this.dummy.updateMatrix();
                    this.floorMesh.setMatrixAt(floorCount, this.dummy.matrix);
                    this.floorMesh.setColorAt(floorCount, tileCol);
                    floorCount++;

                    // CEILING RULE: Ceilings only exist in subterranean dungeons (!outdoors)!
                    // Town streets are outdoors under the open sky!
                    if (!outdoors) {
                        this.dummy.position.set(wx, this.wallHeight, wz);
                        this.dummy.updateMatrix();
                        this.ceilingMesh.setMatrixAt(ceilingCount, this.dummy.matrix);
                        this.ceilingMesh.setColorAt(ceilingCount, tileCol);
                        ceilingCount++;
                    }
                    continue;
                }

                // Closed Door
                if (feat === 2) {
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
                        this.lavaMesh.setMatrixAt(lavaCount++, this.dummy.matrix);
                    } else {
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

        // Attack animation on right hand
        if (this.attackAnimationTime > 0) {
            this.attackAnimationTime -= delta;
            this.rightHandGroup.position.z = -0.36 - Math.sin((0.22 - this.attackAnimationTime) * Math.PI * 4.5) * 0.18;
            this.rightHandGroup.rotation.z = 0.15 - Math.sin((0.22 - this.attackAnimationTime) * Math.PI * 4.5) * 0.35;
        } else {
            this.rightHandGroup.position.z = -0.36;
            this.rightHandGroup.rotation.z = 0.15;
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
