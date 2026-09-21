using System;
using System.Collections.Generic;
using Godot;

namespace Angband3D;

public enum SoundEffect
{
    Footstep,
    FootstepOutdoor,
    MeleeSwing,
    MeleeHit,
    MeleeCrit,
    MonsterGrunt,
    MonsterDeath,
    PlayerHurt,
    SpellCast,
    BowShoot,
    DoorOpen,
    DoorClose,
    DoorBreak,
    StairsDown,
    StairsUp,
    ItemPickup,
    GoldPickup,
    ButtonClick,
    LevelEnter,
    PlayerDeath,
    MenuNav,
    MenuSelect,
    MenuOpen,

    // Environmental & interactions
    WallBump,
    ShieldBlock,
    ArmorDeflect,
    EquipWeapon,
    EquipArmor,
    ItemDrop,
    Quaff,
    Scroll,
    Eat,
    ChestOpen,
    TrapDisarm,
    TrapTrigger,
    Teleport,

    // Status condition warnings
    Poison,
    Confused,
    Blind,
    Paralyzed,
    Afraid,
    Hunger,

    // Elemental spells
    SpellFire,
    SpellCold,
    SpellLightning,
    SpellPoison,

    // Monster vocalization families
    MonsterGrowl,
    MonsterHiss,
    GhostWail,
    DragonRoar,
    RodentSqueak,
    InsectChitin
}

/// <summary>
/// Positional and procedural sound manager for Angband3D.
/// Generates rich 16-bit PCM sound effects dynamically without external audio dependencies,
/// providing instantaneous, zero-latency feedback for all movement, combat, spells, and dungeon interactions.
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    private const int SampleRate = 44100;
    private readonly Dictionary<SoundEffect, AudioStreamWav> _sfxLibrary = new();
    private readonly AudioStreamWav[] _stoneFootsteps = new AudioStreamWav[4];
    private readonly AudioStreamWav[] _outdoorFootsteps = new AudioStreamWav[4];
    private int _stoneStepIdx = 0;
    private int _outdoorStepIdx = 0;

    private readonly List<AudioStreamPlayer> _2dPlayers = new();
    private readonly List<AudioStreamPlayer3D> _3dPlayers = new();
    private const int Max2DPlayers = 8;
    private const int Max3DPlayers = 12;

    public AudioManager()
    {
        Instance ??= this;
    }

    public override void _Ready()
    {
        Instance = this;
        InitPlayersAndLibrary();
    }

    public static void EnsureInitialized(Node parent = null)
    {
        if (Instance != null && Instance.IsInsideTree()) return;
        var mgr = Instance ?? new AudioManager();
        if (parent != null)
        {
            if (mgr.GetParent() == null) parent.AddChild(mgr);
        }
        else if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
        {
            if (mgr.GetParent() == null) tree.Root.AddChild(mgr);
        }
        Instance = mgr;
    }

    private void InitPlayersAndLibrary()
    {
        if (_sfxLibrary.Count == 0)
        {
            GenerateSfxLibrary();
        }

        if (_2dPlayers.Count == 0)
        {
            for (var i = 0; i < Max2DPlayers; i++)
            {
                var p = new AudioStreamPlayer { Bus = "Master", VolumeDb = -2.0f };
                AddChild(p);
                _2dPlayers.Add(p);
            }
        }

        if (_3dPlayers.Count == 0)
        {
            for (var i = 0; i < Max3DPlayers; i++)
            {
                var p = new AudioStreamPlayer3D
                {
                    Bus = "Master",
                    VolumeDb = 0.0f,
                    UnitSize = 3.0f,
                    MaxDistance = 35.0f,
                    AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance,
                    DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
                };
                AddChild(p);
                _3dPlayers.Add(p);
            }
        }
    }

    public static void Play(SoundEffect effect, float pitchScale = 1.0f, float volumeDb = 0f)
    {
        EnsureInitialized();
        Instance?.PlayInternal(effect, pitchScale, volumeDb);
    }

    public static void PlayAt(SoundEffect effect, Vector3 position, float pitchScale = 1.0f, float volumeDb = 0f)
    {
        EnsureInitialized();
        Instance?.PlayAtInternal(effect, position, pitchScale, volumeDb);
    }

    private AudioStreamWav ResolveStream(SoundEffect effect)
    {
        if (effect == SoundEffect.Footstep)
        {
            var idx = _stoneStepIdx++ % _stoneFootsteps.Length;
            return _stoneFootsteps[idx] ?? (_sfxLibrary.TryGetValue(effect, out var s) ? s : null);
        }
        if (effect == SoundEffect.FootstepOutdoor)
        {
            var idx = _outdoorStepIdx++ % _outdoorFootsteps.Length;
            return _outdoorFootsteps[idx] ?? (_sfxLibrary.TryGetValue(effect, out var s) ? s : null);
        }
        return _sfxLibrary.TryGetValue(effect, out var stream) ? stream : null;
    }

    private void PlayInternal(SoundEffect effect, float pitchScale, float volumeDb)
    {
        var stream = ResolveStream(effect);
        if (stream == null) return;

        foreach (var player in _2dPlayers)
        {
            if (!player.Playing)
            {
                player.Stream = stream;
                player.PitchScale = Mathf.Clamp(pitchScale + (GD.Randf() * 0.08f - 0.04f), 0.5f, 2.0f);
                player.VolumeDb = -4.0f + volumeDb;
                player.Play();
                return;
            }
        }

        // Steal first player if all busy
        if (_2dPlayers.Count > 0)
        {
            var p = _2dPlayers[0];
            p.Stream = stream;
            p.PitchScale = pitchScale;
            p.VolumeDb = -2.0f + volumeDb;
            p.Play();
        }
    }

    private void PlayAtInternal(SoundEffect effect, Vector3 position, float pitchScale, float volumeDb)
    {
        var stream = ResolveStream(effect);
        if (stream == null) return;

        foreach (var player in _3dPlayers)
        {
            if (!player.Playing)
            {
                player.Position = position;
                player.Stream = stream;
                player.PitchScale = Mathf.Clamp(pitchScale + (GD.Randf() * 0.08f - 0.04f), 0.5f, 2.0f);
                player.VolumeDb = volumeDb;
                player.Play();
                return;
            }
        }

        if (_3dPlayers.Count > 0)
        {
            var p = _3dPlayers[0];
            p.Position = position;
            p.Stream = stream;
            p.PitchScale = pitchScale;
            p.VolumeDb = volumeDb;
            p.Play();
        }
    }

    #region Procedural Sound Synthesizers

    private void GenerateSfxLibrary()
    {
        for (var i = 0; i < _stoneFootsteps.Length; i++)
        {
            _stoneFootsteps[i] = SynthFootstepVariation(stone: true, i);
        }
        for (var i = 0; i < _outdoorFootsteps.Length; i++)
        {
            _outdoorFootsteps[i] = SynthFootstepVariation(stone: false, i);
        }

        _sfxLibrary[SoundEffect.Footstep] = _stoneFootsteps[0];
        _sfxLibrary[SoundEffect.FootstepOutdoor] = _outdoorFootsteps[0];
        _sfxLibrary[SoundEffect.MeleeSwing] = SynthWhoosh();
        _sfxLibrary[SoundEffect.MeleeHit] = SynthHit();
        _sfxLibrary[SoundEffect.MeleeCrit] = SynthCrit();
        _sfxLibrary[SoundEffect.MonsterGrunt] = SynthMonsterGrunt();
        _sfxLibrary[SoundEffect.MonsterDeath] = SynthMonsterDeath();
        _sfxLibrary[SoundEffect.PlayerHurt] = SynthPlayerHurt();
        _sfxLibrary[SoundEffect.SpellCast] = SynthSpellCast();
        _sfxLibrary[SoundEffect.BowShoot] = SynthBowShoot();
        _sfxLibrary[SoundEffect.DoorOpen] = SynthDoor(open: true);
        _sfxLibrary[SoundEffect.DoorClose] = SynthDoor(open: false);
        _sfxLibrary[SoundEffect.DoorBreak] = SynthDoorBreak();
        _sfxLibrary[SoundEffect.StairsDown] = SynthStairs(down: true);
        _sfxLibrary[SoundEffect.StairsUp] = SynthStairs(down: false);
        _sfxLibrary[SoundEffect.ItemPickup] = SynthChime(isGold: false);
        _sfxLibrary[SoundEffect.GoldPickup] = SynthGoldPickup();
        _sfxLibrary[SoundEffect.ButtonClick] = SynthMenuSelect();
        _sfxLibrary[SoundEffect.PlayerDeath] = SynthPlayerDeath();
        _sfxLibrary[SoundEffect.MenuNav] = SynthMenuNav();
        _sfxLibrary[SoundEffect.MenuSelect] = SynthMenuSelect();
        _sfxLibrary[SoundEffect.MenuOpen] = SynthMenuOpen();
        _sfxLibrary[SoundEffect.LevelEnter] = SynthDungeonHorn();

        // New Physical Acoustic Models
        _sfxLibrary[SoundEffect.WallBump] = SynthWallBump();
        _sfxLibrary[SoundEffect.ShieldBlock] = SynthShieldBlock();
        _sfxLibrary[SoundEffect.ArmorDeflect] = SynthArmorDeflect();
        _sfxLibrary[SoundEffect.EquipWeapon] = SynthEquipWeapon();
        _sfxLibrary[SoundEffect.EquipArmor] = SynthEquipArmor();
        _sfxLibrary[SoundEffect.ItemDrop] = SynthItemDrop();
        _sfxLibrary[SoundEffect.Quaff] = SynthQuaff();
        _sfxLibrary[SoundEffect.Scroll] = SynthScroll();
        _sfxLibrary[SoundEffect.Eat] = SynthEat();
        _sfxLibrary[SoundEffect.ChestOpen] = SynthChestOpen();
        _sfxLibrary[SoundEffect.TrapDisarm] = SynthTrapDisarm();
        _sfxLibrary[SoundEffect.TrapTrigger] = SynthTrapTrigger();
        _sfxLibrary[SoundEffect.Teleport] = SynthTeleport();
        _sfxLibrary[SoundEffect.Poison] = SynthPoison();
        _sfxLibrary[SoundEffect.Confused] = SynthConfused();
        _sfxLibrary[SoundEffect.Blind] = SynthBlind();
        _sfxLibrary[SoundEffect.Paralyzed] = SynthParalyzed();
        _sfxLibrary[SoundEffect.Afraid] = SynthAfraid();
        _sfxLibrary[SoundEffect.Hunger] = SynthHunger();
        _sfxLibrary[SoundEffect.SpellFire] = SynthElementalSpell("fire");
        _sfxLibrary[SoundEffect.SpellCold] = SynthElementalSpell("cold");
        _sfxLibrary[SoundEffect.SpellLightning] = SynthElementalSpell("lightning");
        _sfxLibrary[SoundEffect.SpellPoison] = SynthElementalSpell("poison");
        _sfxLibrary[SoundEffect.MonsterGrowl] = SynthMonsterGrowl();
        _sfxLibrary[SoundEffect.MonsterHiss] = SynthMonsterHiss();
        _sfxLibrary[SoundEffect.GhostWail] = SynthGhostWail();
        _sfxLibrary[SoundEffect.DragonRoar] = SynthDragonRoar();
        _sfxLibrary[SoundEffect.RodentSqueak] = SynthRodentSqueak();
        _sfxLibrary[SoundEffect.InsectChitin] = SynthInsectChitin();
    }

    private static AudioStreamWav CreateWav(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Mathf.Clamp(samples[i], -1.0f, 1.0f);
            var shortVal = (short)(clamped * 32767.0f);
            pcm[i * 2] = (byte)(shortVal & 0xFF);
            pcm[i * 2 + 1] = (byte)((shortVal >> 8) & 0xFF);
        }

        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            Data = pcm
        };
        return wav;
    }

    private static AudioStreamWav SynthFootstepVariation(bool stone, int variation)
    {
        var duration = stone ? 0.13f : 0.15f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        var rng = new Random(stone ? (1007 + variation * 37) : (2011 + variation * 43));

        // Multi-stage acoustic physical model:
        // Stage 1: Heel impact transient (0 to 20ms)
        // Stage 2: Forefoot toe contact (18ms to 65ms)
        // Stage 3: Granular surface texture / grit friction
        var fHeel = stone ? (115f + variation * 6f) : (85f + variation * 5f);
        var fToe = stone ? (210f + variation * 10f) : (140f + variation * 8f);
        var toeDelay = (int)(SampleRate * 0.018f);

        var lpNoise1 = 0f;
        var lpNoise2 = 0f;
        var bpState1 = 0f;
        var bpState2 = 0f;

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;

            // Heel impact envelope
            var envHeel = Mathf.Exp(-t * (stone ? 65f : 48f));
            var heelPunch = Mathf.Sin(2f * Mathf.Pi * fHeel * t) * envHeel * (stone ? 0.55f : 0.65f);

            // Toe impact envelope (slightly delayed)
            var toePunch = 0f;
            if (i >= toeDelay)
            {
                var tToe = (i - toeDelay) / (float)SampleRate;
                var envToe = Mathf.Exp(-tToe * (stone ? 75f : 55f));
                toePunch = Mathf.Sin(2f * Mathf.Pi * fToe * tToe) * envToe * (stone ? 0.35f : 0.40f);
            }

            // Surface texture filtered noise (grit / scrape)
            var rawNoise = (float)rng.NextDouble() * 2f - 1f;
            var coeff = stone ? 0.18f : 0.12f;
            lpNoise1 += (rawNoise - lpNoise1) * coeff;
            lpNoise2 += (lpNoise1 - lpNoise2) * coeff;

            // Simple 2nd-order resonator for stone/dirt crunch
            var centerFreq = stone ? 1600f : 850f;
            var q = 1.4f;
            var omega = 2f * Mathf.Pi * centerFreq / SampleRate;
            var alpha = Mathf.Sin(omega) / (2f * q);
            bpState1 = bpState1 + alpha * (rawNoise - bpState1);
            bpState2 = bpState2 + alpha * (bpState1 - bpState2);

            var frictionEnv = Mathf.Exp(-t * (stone ? 45f : 35f));
            var friction = (lpNoise2 * 0.4f + bpState2 * 0.6f) * frictionEnv * (stone ? 0.28f : 0.38f);

            samples[i] = (heelPunch + toePunch + friction) * 0.52f;
        }

        return CreateWav(samples);
    }

    private static AudioStreamWav SynthWhoosh()
    {
        var duration = 0.16f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(101);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var progress = t / duration;
            // Bell curve envelope
            var env = Mathf.Sin(progress * Mathf.Pi);

            // Frequency swept filtered noise
            var noise = ((float)rng.NextDouble() * 2f - 1f);
            var freq = 200f + Mathf.Sin(progress * Mathf.Pi) * 450f;
            var whistle = Mathf.Sin(2f * Mathf.Pi * freq * t) * 0.4f;

            samples[i] = (noise * 0.6f + whistle) * env * 0.7f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthHit()
    {
        var duration = 0.22f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(202);
        var f0 = 460f;

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var clang = Mathf.Sin(2f * Mathf.Pi * f0 * t) * Mathf.Exp(-t * 12f) * 0.35f
                      + Mathf.Sin(2f * Mathf.Pi * (f0 * 2.76f) * t) * Mathf.Exp(-t * 22f) * 0.25f
                      + Mathf.Sin(2f * Mathf.Pi * (f0 * 5.40f) * t) * Mathf.Exp(-t * 38f) * 0.18f
                      + Mathf.Sin(2f * Mathf.Pi * (f0 * 8.93f) * t) * Mathf.Exp(-t * 55f) * 0.12f;
            var punch = Mathf.Sin(2f * Mathf.Pi * (125f - t * 240f) * t) * Mathf.Exp(-t * 32f) * 0.60f;
            var spark = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 90f) * 0.40f;
            samples[i] = (float)Math.Tanh((clang + punch + spark) * 0.88f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthCrit()
    {
        var duration = 0.38f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var bass = Mathf.Sin(2f * Mathf.Pi * (160f - t * 280f) * t) * Mathf.Exp(-t * 14f) * 0.75f;
            var sub = Mathf.Sin(2f * Mathf.Pi * 48f * t) * Mathf.Exp(-t * 8f) * 0.55f;
            var gong = (Mathf.Sin(2f * Mathf.Pi * 392f * t) * 0.35f
                      + Mathf.Sin(2f * Mathf.Pi * 784f * t) * 0.30f
                      + Mathf.Sin(2f * Mathf.Pi * 1175f * t) * 0.22f) * Mathf.Exp(-t * 7f);
            samples[i] = (float)Math.Tanh((bass + sub + gong) * 0.92f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthMonsterGrunt()
    {
        var duration = 0.20f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(303);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 18f);

            // Guttural pitch-dropping growl
            var freq = Math.Max(40f, 180f - t * 450f);
            var roar = Mathf.Sin(2f * Mathf.Pi * freq * t) * 0.6f;
            var rasp = ((float)rng.NextDouble() * 2f - 1f) * 0.4f;

            samples[i] = (roar + rasp) * env * 0.75f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthMonsterDeath()
    {
        var duration = 0.45f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(404);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 8f);

            // Low crumbling rumble + ethereal dissolution
            var rumble = Mathf.Sin(2f * Mathf.Pi * (90f - t * 120f) * t) * 0.5f;
            var hiss = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Sin(t * 12f) * 0.45f;
            var sub = Mathf.Sin(2f * Mathf.Pi * 45f * t) * 0.5f;

            samples[i] = (rumble + hiss + sub) * env * 0.85f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthPlayerHurt()
    {
        var duration = 0.25f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 16f);

            // Visceral thud / low impact shock
            var impact = Mathf.Sin(2f * Mathf.Pi * (130f - t * 250f) * t) * 0.8f;
            var shock = Mathf.Sin(2f * Mathf.Pi * 65f * t) * 0.5f;

            samples[i] = (impact + shock) * env * 0.90f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthBowShoot()
    {
        var duration = 0.20f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(505);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 24f);

            // Bowstring twang + whoosh
            var twang = Mathf.Sin(2f * Mathf.Pi * (380f + Mathf.Sin(t * 120f) * 40f) * t) * 0.65f;
            var snap = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 90f) * 0.45f;

            samples[i] = (twang + snap) * env * 0.85f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthSpellCast()
    {
        var duration = 0.35f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var progress = t / duration;
            var env = Mathf.Sin(progress * Mathf.Pi);

            // Rising magic resonant frequency chirp + shimmer
            var f = 300f + progress * 650f;
            var shimmerF = 1200f + Mathf.Sin(t * 50f) * 300f;
            var wave1 = Mathf.Sin(2f * Mathf.Pi * f * t) * 0.5f;
            var wave2 = Mathf.Sin(2f * Mathf.Pi * shimmerF * t) * 0.35f;
            var wave3 = Mathf.Sin(2f * Mathf.Pi * (f * 1.5f) * t) * 0.25f;

            samples[i] = (wave1 + wave2 + wave3) * env * 0.80f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthDoor(bool open)
    {
        var duration = 0.30f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(505);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * (open ? 9f : 15f));

            // Squeaking hinge friction + wooden latch click
            var squeakF = open ? (240f + t * 400f) : (350f - t * 300f);
            var squeak = Mathf.Sin(2f * Mathf.Pi * squeakF * t) * 0.45f;
            var wood = Mathf.Sin(2f * Mathf.Pi * 95f * t) * 0.5f;
            var friction = ((float)rng.NextDouble() * 2f - 1f) * 0.2f;

            samples[i] = (squeak + wood + friction) * env * 0.70f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthDoorBreak()
    {
        var duration = 0.38f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(606);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 12f);

            var shatter = ((float)rng.NextDouble() * 2f - 1f) * 0.7f;
            var splinter = Mathf.Sin(2f * Mathf.Pi * (220f - t * 300f) * t) * 0.5f;
            var heavy = Mathf.Sin(2f * Mathf.Pi * 80f * t) * 0.6f;

            samples[i] = (shatter + splinter + heavy) * env * 0.85f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthStairs(bool down)
    {
        var duration = 0.40f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 7f);

            // Descending/ascending reverberant stone steps
            var f1 = down ? (160f - t * 140f) : (110f + t * 140f);
            var f2 = down ? (80f - t * 60f) : (55f + t * 60f);
            var step1 = Mathf.Sin(2f * Mathf.Pi * f1 * t) * 0.5f;
            var step2 = Mathf.Sin(2f * Mathf.Pi * f2 * t) * 0.5f;

            samples[i] = (step1 + step2) * env * 0.80f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthChime(bool isGold)
    {
        var duration = 0.28f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 14f);

            // Sparkling dual-tone crystal chime
            var f1 = isGold ? 1046.50f : 880f; // C6 or A5
            var f2 = isGold ? 1318.51f : 1174.66f; // E6 or D6
            var f3 = isGold ? 2093f : 1760f; // C7 or A6
            var tone = Mathf.Sin(2f * Mathf.Pi * f1 * t) * 0.45f
                     + Mathf.Sin(2f * Mathf.Pi * f2 * t) * 0.35f
                     + Mathf.Sin(2f * Mathf.Pi * f3 * t) * 0.20f;

            samples[i] = tone * env * 0.75f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthClick()
    {
        return SynthMenuSelect();
    }

    private static AudioStreamWav SynthMenuNav()
    {
        var duration = 0.022f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var attackSamples = (int)(SampleRate * 0.0015f);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var attack = i < attackSamples ? (i / (float)attackSamples) : 1.0f;
            var env = attack * Mathf.Exp(-t * 160f);

            // Soft tactile wooden/leather UI click with low resonance
            var body = Mathf.Sin(2f * Mathf.Pi * 320f * t) * 0.40f
                     + Mathf.Sin(2f * Mathf.Pi * 540f * t) * 0.25f;

            samples[i] = body * env * 0.40f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthMenuSelect()
    {
        var duration = 0.28f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var attackSamples = (int)(SampleRate * 0.003f);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var attack = i < attackSamples ? (i / (float)attackSamples) : 1.0f;
            var env = attack * Mathf.Exp(-t * 15f);

            // Ascending bright crystal affirmation chime (E5 -> B5 -> E6)
            var tone1 = Mathf.Sin(2f * Mathf.Pi * 659.25f * t) * 0.40f;
            var tone2 = Mathf.Sin(2f * Mathf.Pi * 987.77f * t) * 0.35f;
            var tone3 = Mathf.Sin(2f * Mathf.Pi * 1318.51f * t) * 0.20f;
            var bell = Mathf.Sin(2f * Mathf.Pi * 1819.5f * t) * Mathf.Exp(-t * 30f) * 0.15f;

            samples[i] = (tone1 + tone2 + tone3 + bell) * env * 0.55f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthMenuOpen()
    {
        var duration = 1.8f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var attackSamples = (int)(SampleRate * 0.05f);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var attack = i < attackSamples ? (i / (float)attackSamples) : 1.0f;

            // Frequency-dependent damping for natural acoustic resonance
            var envBass = attack * Mathf.Exp(-t * 2.2f);
            var envMid = attack * Mathf.Exp(-t * 3.4f);
            var envHigh = attack * Mathf.Exp(-t * 5.2f);

            // Shimmer chorus modulation
            var shimmer = 1.0f + 0.08f * Mathf.Sin(2f * Mathf.Pi * 0.45f * t);

            // Open fantasy harmony chord (D-minor add9: D2, A2, D3, F3, A3, E4)
            var d2 = Mathf.Sin(2f * Mathf.Pi * 73.42f * t) * 0.35f * envBass;
            var a2 = Mathf.Sin(2f * Mathf.Pi * 110.00f * t) * 0.30f * envBass;
            var d3 = Mathf.Sin(2f * Mathf.Pi * 146.83f * t) * 0.28f * envMid;
            var f3 = Mathf.Sin(2f * Mathf.Pi * 174.61f * t) * 0.24f * envMid;
            var a3 = Mathf.Sin(2f * Mathf.Pi * 220.00f * t) * 0.20f * envMid * shimmer;
            var e4 = Mathf.Sin(2f * Mathf.Pi * 329.63f * t) * 0.16f * envHigh * shimmer;

            // Sparkling introductory crystal chime
            var chimePing = Mathf.Sin(2f * Mathf.Pi * 1318.51f * t) * Mathf.Exp(-t * 12f) * 0.22f;

            samples[i] = (d2 + a2 + d3 + f3 + a3 + e4 + chimePing) * 0.48f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthDungeonHorn()
    {
        var duration = 0.90f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var attackSamples = (int)(SampleRate * 0.06f);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var attack = i < attackSamples ? (i / (float)attackSamples) : 1.0f;
            var env = attack * Mathf.Exp(-t * 3.8f);

            // Resonant fantasy dungeon horn (brassy A2 fundamental with warm harmonics)
            var f0 = 110.0f;
            var h1 = Mathf.Sin(2f * Mathf.Pi * f0 * t) * 0.45f;
            var h2 = Mathf.Sin(2f * Mathf.Pi * (f0 * 1.5f) * t) * 0.30f; // Fifth (E3)
            var h3 = Mathf.Sin(2f * Mathf.Pi * (f0 * 2.0f) * t) * 0.22f; // Octave (A3)
            var h4 = Mathf.Sin(2f * Mathf.Pi * (f0 * 3.0f) * t) * 0.14f; // Twelfth (E4)
            var sub = Mathf.Sin(2f * Mathf.Pi * (f0 * 0.5f) * t) * 0.25f; // Sub-bass (A1)

            samples[i] = (h1 + h2 + h3 + h4 + sub) * env * 0.65f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthPlayerDeath()
    {
        var duration = 2.6f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var attackSamples = (int)(SampleRate * 0.006f);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var attack = i < attackSamples ? (i / (float)attackSamples) : 1.0f;
            var env = attack * Mathf.Exp(-t * 1.5f);

            // Somber resonant funeral toll: D2 fundamental (73.4Hz) with minor 3rd (87.3Hz), 5th (110Hz), and chime overtones
            var fundamental = Mathf.Sin(2f * Mathf.Pi * 73.416f * t) * 0.55f;
            var minorThird = Mathf.Sin(2f * Mathf.Pi * 87.307f * t) * 0.35f;
            var fifth = Mathf.Sin(2f * Mathf.Pi * 110.00f * t) * 0.25f;
            var octave = Mathf.Sin(2f * Mathf.Pi * 146.83f * t) * 0.20f;
            var chime = Mathf.Sin(2f * Mathf.Pi * 293.66f * t) * Mathf.Exp(-t * 4.0f) * 0.25f;
            var highChime = Mathf.Sin(2f * Mathf.Pi * 587.33f * t) * Mathf.Exp(-t * 8.0f) * 0.15f;

            samples[i] = (fundamental + minorThird + fifth + octave + chime + highChime) * env * 0.85f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthGoldPickup()
    {
        var duration = 0.34f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(707);
        var coins = new (float t, float f1, float f2)[]
        {
            (0.0f, 2093.0f, 3136.0f),
            (0.042f, 2349.3f, 3520.0f),
            (0.088f, 2793.8f, 4186.0f)
        };

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var cAcc = 0f;
            foreach (var c in coins)
            {
                var dt = t - c.t;
                if (dt >= 0 && dt < 0.18f)
                {
                    var env = Mathf.Exp(-dt * 16f);
                    var tone = Mathf.Sin(2f * Mathf.Pi * c.f1 * dt) * 0.45f + Mathf.Sin(2f * Mathf.Pi * c.f2 * dt) * 0.30f;
                    var click = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-dt * 120f) * 0.25f;
                    cAcc += (tone + click) * env;
                }
            }
            samples[i] = (float)Math.Tanh(cAcc * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthWallBump()
    {
        var duration = 0.13f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(808);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 38f);
            var thud = Mathf.Sin(2f * Mathf.Pi * (68f - t * 180f) * t) * 0.85f;
            var grit = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 65f) * 0.35f;
            samples[i] = (float)Math.Tanh((thud + grit) * env * 0.90f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthShieldBlock()
    {
        var duration = 0.22f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 22f);
            var clang = Mathf.Sin(2f * Mathf.Pi * 580f * t) * 0.45f + Mathf.Sin(2f * Mathf.Pi * 1160f * t) * 0.35f;
            var ping = Mathf.Sin(2f * Mathf.Pi * 2800f * t) * Mathf.Exp(-t * 45f) * 0.30f;
            var punch = Mathf.Sin(2f * Mathf.Pi * 120f * t) * Mathf.Exp(-t * 60f) * 0.40f;
            samples[i] = (float)Math.Tanh((clang + ping + punch) * env * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthArmorDeflect()
    {
        var duration = 0.18f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 28f);
            var ping = Mathf.Sin(2f * Mathf.Pi * 640f * t) * 0.50f + Mathf.Sin(2f * Mathf.Pi * 1480f * t) * 0.35f;
            var thud = Mathf.Sin(2f * Mathf.Pi * 90f * t) * 0.45f;
            samples[i] = (float)Math.Tanh((ping + thud) * env * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthEquipWeapon()
    {
        var duration = 0.26f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(909);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var scrape = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Sin((t / duration) * Mathf.Pi) * 0.35f;
            var ring = Mathf.Sin(2f * Mathf.Pi * 1760f * t) * 0.30f + Mathf.Sin(2f * Mathf.Pi * 2640f * t) * 0.20f;
            var env = Mathf.Exp(-t * 14f);
            samples[i] = (float)Math.Tanh((scrape + ring * env) * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthEquipArmor()
    {
        var duration = 0.24f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(1010);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var rattle = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 18f) * 0.45f;
            var snap = Mathf.Sin(2f * Mathf.Pi * 340f * t) * Mathf.Exp(-t * 40f) * 0.45f;
            samples[i] = (float)Math.Tanh((rattle + snap) * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthItemDrop()
    {
        var duration = 0.15f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env1 = Mathf.Exp(-t * 35f);
            var tap1 = Mathf.Sin(2f * Mathf.Pi * 190f * t) * env1 * 0.65f;
            var t2 = t - 0.045f;
            var env2 = t2 > 0 ? Mathf.Exp(-t2 * 45f) : 0f;
            var tap2 = t2 > 0 ? Mathf.Sin(2f * Mathf.Pi * 230f * t2) * env2 * 0.35f : 0f;
            samples[i] = (float)Math.Tanh((tap1 + tap2) * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthQuaff()
    {
        var duration = 0.36f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(1111);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var pop = Mathf.Sin(2f * Mathf.Pi * (1200f - t * 3000f) * t) * Mathf.Exp(-t * 90f) * 0.55f;
            var t1 = t - 0.035f;
            var g1 = t1 > 0 && t1 < 0.14f ? Mathf.Sin(2f * Mathf.Pi * (480f - t1 * 1200f) * t1) * Mathf.Sin((t1 / 0.14f) * Mathf.Pi) * 0.55f : 0f;
            var t2 = t - 0.17f;
            var g2 = t2 > 0 && t2 < 0.16f ? Mathf.Sin(2f * Mathf.Pi * (580f - t2 * 1400f) * t2) * Mathf.Sin((t2 / 0.16f) * Mathf.Pi) * 0.60f : 0f;
            var bubble = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 18f) * 0.20f;
            samples[i] = (float)Math.Tanh((pop + g1 + g2 + bubble) * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthScroll()
    {
        var duration = 0.36f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(1212);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 8f);
            var rustle = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 22f) * 0.35f;
            var rune1 = Mathf.Sin(2f * Mathf.Pi * 523.25f * t) * 0.32f;
            var rune2 = Mathf.Sin(2f * Mathf.Pi * 783.99f * t) * 0.26f;
            var rune3 = Mathf.Sin(2f * Mathf.Pi * 1318.51f * t) * 0.20f;
            samples[i] = (float)Math.Tanh((rustle + (rune1 + rune2 + rune3) * env) * 0.80f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthEat()
    {
        var duration = 0.28f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(1313);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var bite1 = t < 0.12f ? ((float)rng.NextDouble() * 2f - 1f) * Mathf.Sin((t / 0.12f) * Mathf.Pi) * 0.55f : 0f;
            var t2 = t - 0.12f;
            var bite2 = t2 > 0 && t2 < 0.14f ? ((float)rng.NextDouble() * 2f - 1f) * Mathf.Sin((t2 / 0.14f) * Mathf.Pi) * 0.50f : 0f;
            var crunch = Mathf.Sin(2f * Mathf.Pi * 480f * t) * Mathf.Exp(-t * 20f) * 0.30f;
            samples[i] = (float)Math.Tanh((bite1 + bite2 + crunch) * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthChestOpen()
    {
        var duration = 0.40f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var creakF = 180f + t * 240f + Mathf.Sin(t * 70f) * 35f;
            var creak = Mathf.Sin(2f * Mathf.Pi * creakF * t) * Mathf.Exp(-t * 7f) * 0.45f;
            var latch = Mathf.Sin(2f * Mathf.Pi * 920f * t) * Mathf.Exp(-t * 60f) * 0.40f;
            var wood = Mathf.Sin(2f * Mathf.Pi * 75f * t) * Mathf.Exp(-t * 12f) * 0.40f;
            samples[i] = (float)Math.Tanh((creak + latch + wood) * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthTrapDisarm()
    {
        var duration = 0.32f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var click = Mathf.Sin(2f * Mathf.Pi * 1400f * t) * Mathf.Exp(-t * 90f) * 0.50f;
            var t2 = t - 0.05f;
            var chime1 = t2 > 0 ? Mathf.Sin(2f * Mathf.Pi * 1046.5f * t2) * Mathf.Exp(-t2 * 12f) * 0.40f : 0f;
            var chime2 = t2 > 0.08f ? Mathf.Sin(2f * Mathf.Pi * 1318.5f * (t - 0.13f)) * Mathf.Exp(-(t - 0.13f) * 10f) * 0.40f : 0f;
            samples[i] = (float)Math.Tanh((click + chime1 + chime2) * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthTrapTrigger()
    {
        var duration = 0.35f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var snap = Mathf.Sin(2f * Mathf.Pi * 980f * t) * Mathf.Exp(-t * 70f) * 0.60f;
            var spring = Mathf.Sin(2f * Mathf.Pi * (340f + Mathf.Sin(t * 80f) * 80f) * t) * Mathf.Exp(-t * 15f) * 0.45f;
            var thud = Mathf.Sin(2f * Mathf.Pi * 75f * t) * Mathf.Exp(-t * 12f) * 0.55f;
            samples[i] = (float)Math.Tanh((snap + spring + thud) * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthTeleport()
    {
        var duration = 0.38f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var f = 180f + Mathf.Pow(t / duration, 2f) * 1400f;
            var env = Mathf.Sin((t / duration) * Mathf.Pi);
            var warp = Mathf.Sin(2f * Mathf.Pi * f * t) * 0.65f;
            var pop = Mathf.Sin(2f * Mathf.Pi * 60f * t) * Mathf.Exp(-(t - 0.28f) * 40f) * (t > 0.28f ? 0.6f : 0f);
            samples[i] = (float)Math.Tanh((warp * env + pop) * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthPoison()
    {
        var duration = 0.42f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(1414);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 6f);
            var hiss = ((float)rng.NextDouble() * 2f - 1f) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.Pi * 24f * t)) * 0.35f;
            var tone = Mathf.Sin(2f * Mathf.Pi * (660f - t * 80f) * t) * 0.45f;
            samples[i] = (float)Math.Tanh((hiss + tone) * env * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthConfused()
    {
        var duration = 0.45f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Sin((t / duration) * Mathf.Pi);
            var w1 = Mathf.Sin(2f * Mathf.Pi * 220f * t) * 0.45f;
            var w2 = Mathf.Sin(2f * Mathf.Pi * 226f * t) * 0.45f;
            samples[i] = (float)Math.Tanh((w1 + w2) * env * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthBlind()
    {
        var duration = 0.55f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 4f);
            var gong = Mathf.Sin(2f * Mathf.Pi * 85f * t) * 0.70f + Mathf.Sin(2f * Mathf.Pi * 130f * t) * 0.30f;
            samples[i] = (float)Math.Tanh(gong * env * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthParalyzed()
    {
        var duration = 0.28f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(1515);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 16f);
            var iron = Mathf.Sin(2f * Mathf.Pi * 840f * t) * 0.55f + Mathf.Sin(2f * Mathf.Pi * 1680f * t) * 0.35f;
            var frost = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 30f) * 0.30f;
            samples[i] = (float)Math.Tanh((iron + frost) * env * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthAfraid()
    {
        var duration = 0.45f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var tremolo = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.Pi * 14f * t);
            var tritone1 = Mathf.Sin(2f * Mathf.Pi * 587.33f * t) * 0.45f;
            var tritone2 = Mathf.Sin(2f * Mathf.Pi * 830.61f * t) * 0.40f;
            var env = Mathf.Sin((t / duration) * Mathf.Pi);
            samples[i] = (float)Math.Tanh((tritone1 + tritone2) * tremolo * env * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthHunger()
    {
        var duration = 0.50f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Sin((t / duration) * Mathf.Pi);
            var rumble = Mathf.Sin(2f * Mathf.Pi * (110f - t * 45f) * t) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.Pi * 8f * t)) * 0.70f;
            var hollow = Mathf.Sin(2f * Mathf.Pi * 185f * t) * 0.35f;
            samples[i] = (float)Math.Tanh((rumble + hollow) * env * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthElementalSpell(string element)
    {
        var duration = 0.38f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(1616 + element.GetHashCode());

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var p = t / duration;
            float v;

            if (element == "fire")
            {
                var blast = Mathf.Sin(2f * Mathf.Pi * (120f - t * 140f) * t) * Mathf.Exp(-t * 8f) * 0.65f;
                var crackle = ((float)rng.NextDouble() * 2f - 1f) * (0.5f + 0.5f * Mathf.Sin(t * 30f)) * Mathf.Exp(-t * 6f) * 0.45f;
                v = blast + crackle;
            }
            else if (element == "cold" || element == "frost")
            {
                var crack = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 60f) * 0.40f;
                var chime1 = Mathf.Sin(2f * Mathf.Pi * 2093f * t) * 0.40f + Mathf.Sin(2f * Mathf.Pi * 3136f * t) * 0.25f;
                v = crack + chime1 * Mathf.Exp(-t * 10f);
            }
            else if (element == "lightning")
            {
                var zap = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 90f) * 0.70f;
                var hum = Mathf.Sin(2f * Mathf.Pi * 180f * t) * Mathf.Exp(-t * 25f) * 0.40f;
                var thunder = Mathf.Sin(2f * Mathf.Pi * 55f * t) * Mathf.Exp(-t * 5f) * 0.55f;
                v = zap + hum + thunder;
            }
            else if (element == "poison")
            {
                var sizzle = ((float)rng.NextDouble() * 2f - 1f) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.Pi * 28f * t)) * 0.50f;
                var bubble = Mathf.Sin(2f * Mathf.Pi * (520f - t * 400f) * t) * Mathf.Exp(-t * 8f) * 0.40f;
                v = sizzle + bubble;
            }
            else
            {
                var sweep = Mathf.Sin(2f * Mathf.Pi * (320f + p * 700f) * t) * 0.50f;
                var shimmer = Mathf.Sin(2f * Mathf.Pi * (1200f + Mathf.Sin(t * 40f) * 280f) * t) * 0.35f;
                v = (sweep + shimmer) * Mathf.Sin(p * Mathf.Pi);
            }
            samples[i] = (float)Math.Tanh(v * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthMonsterGrowl()
    {
        var duration = 0.32f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(1717);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 8f);
            var vocal = Mathf.Sin(2f * Mathf.Pi * (115f + Mathf.Sin(t * 60f) * 20f) * t) * 0.60f;
            var rasp = ((float)rng.NextDouble() * 2f - 1f) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.Pi * 35f * t)) * 0.45f;
            samples[i] = (float)Math.Tanh((vocal + rasp) * env * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthMonsterHiss()
    {
        var duration = 0.36f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(1818);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Sin((t / duration) * Mathf.Pi);
            var hiss = ((float)rng.NextDouble() * 2f - 1f) * 0.65f;
            var sibilance = Mathf.Sin(2f * Mathf.Pi * 3400f * t) * 0.30f;
            samples[i] = (float)Math.Tanh((hiss + sibilance) * env * 0.80f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthGhostWail()
    {
        var duration = 0.60f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Sin((t / duration) * Mathf.Pi);
            var f = 340f + Mathf.Sin(t * 8f) * 90f;
            var w1 = Mathf.Sin(2f * Mathf.Pi * f * t) * 0.55f;
            var w2 = Mathf.Sin(2f * Mathf.Pi * (f * 2.01f) * t) * 0.35f;
            samples[i] = (float)Math.Tanh((w1 + w2) * env * 0.75f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthDragonRoar()
    {
        var duration = 0.65f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(1919);

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 4f);
            var sub = Mathf.Sin(2f * Mathf.Pi * (52f - t * 25f) * t) * 0.80f;
            var throat = Mathf.Sin(2f * Mathf.Pi * 105f * t) * 0.50f;
            var flame = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Sin(t * 22f) * 0.45f;
            samples[i] = (float)Math.Tanh((sub + throat + flame) * env * 0.90f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthRodentSqueak()
    {
        var duration = 0.20f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var chirp1 = t < 0.09f ? Mathf.Sin(2f * Mathf.Pi * (1900f + t * 9000f) * t) * Mathf.Sin((t / 0.09f) * Mathf.Pi) * 0.60f : 0f;
            var t2 = t - 0.09f;
            var chirp2 = t2 > 0 && t2 < 0.10f ? Mathf.Sin(2f * Mathf.Pi * (2200f + t2 * 8000f) * t2) * Mathf.Sin((t2 / 0.10f) * Mathf.Pi) * 0.55f : 0f;
            samples[i] = (float)Math.Tanh((chirp1 + chirp2) * 0.85f);
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthInsectChitin()
    {
        var duration = 0.22f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];
        var rng = new Random(2020);
        var clicks = new[] { 0.0f, 0.04f, 0.09f, 0.15f };

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var cVal = 0f;
            foreach (var c in clicks)
            {
                var dt = t - c;
                if (dt >= 0 && dt < 0.025f)
                {
                    var snap = Mathf.Sin(2f * Mathf.Pi * 3200f * dt) * Mathf.Exp(-dt * 180f) * 0.55f;
                    var noise = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-dt * 140f) * 0.35f;
                    cVal += (snap + noise);
                }
            }
            samples[i] = (float)Math.Tanh(cVal * 0.85f);
        }
        return CreateWav(samples);
    }

    #endregion
}
