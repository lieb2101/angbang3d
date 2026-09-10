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
    DoorOpen,
    DoorClose,
    DoorBreak,
    StairsDown,
    StairsUp,
    ItemPickup,
    GoldPickup,
    ButtonClick,
    LevelEnter,
    MenuNav,
    MenuSelect,
    MenuOpen
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
        _sfxLibrary[SoundEffect.DoorOpen] = SynthDoor(open: true);
        _sfxLibrary[SoundEffect.DoorClose] = SynthDoor(open: false);
        _sfxLibrary[SoundEffect.DoorBreak] = SynthDoorBreak();
        _sfxLibrary[SoundEffect.StairsDown] = SynthStairs(down: true);
        _sfxLibrary[SoundEffect.StairsUp] = SynthStairs(down: false);
        _sfxLibrary[SoundEffect.ItemPickup] = SynthChime(isGold: false);
        _sfxLibrary[SoundEffect.GoldPickup] = SynthChime(isGold: true);
        _sfxLibrary[SoundEffect.ButtonClick] = SynthMenuSelect();
        _sfxLibrary[SoundEffect.MenuNav] = SynthMenuNav();
        _sfxLibrary[SoundEffect.MenuSelect] = SynthMenuSelect();
        _sfxLibrary[SoundEffect.MenuOpen] = SynthMenuOpen();
        _sfxLibrary[SoundEffect.LevelEnter] = SynthDungeonHorn();
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

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 22f);

            // Sharp metallic clang + impact punch
            var clang = Mathf.Sin(2f * Mathf.Pi * 520f * t) * 0.45f
                      + Mathf.Sin(2f * Mathf.Pi * 1280f * t) * 0.30f
                      + Mathf.Sin(2f * Mathf.Pi * 2450f * t) * 0.20f;
            var punch = Mathf.Sin(2f * Mathf.Pi * (110f - t * 200f) * t) * 0.6f;
            var spark = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 80f) * 0.5f;

            samples[i] = (clang + punch + spark) * env * 0.85f;
        }
        return CreateWav(samples);
    }

    private static AudioStreamWav SynthCrit()
    {
        var duration = 0.32f;
        var totalSamples = (int)(SampleRate * duration);
        var samples = new float[totalSamples];

        for (var i = 0; i < totalSamples; i++)
        {
            var t = i / (float)SampleRate;
            var env = Mathf.Exp(-t * 14f);

            // Heavy resonant ringing blade impact
            var tone = Mathf.Sin(2f * Mathf.Pi * 440f * t) * 0.4f
                     + Mathf.Sin(2f * Mathf.Pi * 880f * t) * 0.35f
                     + Mathf.Sin(2f * Mathf.Pi * 1760f * t) * 0.25f
                     + Mathf.Sin(2f * Mathf.Pi * 3520f * t) * 0.15f;
            var bass = Mathf.Sin(2f * Mathf.Pi * (140f - t * 180f) * t) * 0.8f;

            samples[i] = (tone + bass) * env * 0.95f;
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

    #endregion
}
