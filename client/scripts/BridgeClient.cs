using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// Universal interface for communicating with a backend roguelike/turn-based game engine.
/// Decouples the 3D client rendering, view routing, and input layer from the specific IPC transport
/// (standard streams child process, TCP socket, WebSocket, WebAssembly, or in-memory native library).
/// </summary>
public interface IGameEngineBridge
{
    /// <summary>Most recently received authoritative game state frame.</summary>
    JsonElement? Frame { get; }

    /// <summary>Engine handshake payload received upon connection.</summary>
    JsonElement? Hello { get; }

    /// <summary>Dynamic terrain and feature dictionary negotiated at startup.</summary>
    IReadOnlyDictionary<int, (string Name, bool Passable)> Features { get; }

    /// <summary>Whether the communication channel with the engine is active.</summary>
    bool Connected { get; }

    /// <summary>True while a command is in flight and awaiting an authoritative frame response.</summary>
    bool Busy { get; }

    /// <summary>Monotonically increasing sequence number of the current frame.</summary>
    long Seq { get; }

    /// <summary>Send a virtual key event (e.g. "left", "up", "enter", "escape", "C-s").</summary>
    void SendKey(string spec);

    /// <summary>Send a raw bridge protocol line (e.g. "map on", "frame", "quit").</summary>
    void Send(string command);

    /// <summary>Disconnect and terminate the engine session cleanly.</summary>
    void Stop();
}

/// <summary>
/// Owns the game engine child process and communicates via the line-delimited JSON Bridge Protocol v1.
/// </summary>
/// <remarks>
/// <para>
/// <b>IPC Architecture Rationale:</b><br/>
/// Uses <see cref="System.Diagnostics.Process"/> with asynchronous stdout redirection instead of Godot's
/// <c>OS.ExecuteWithPipe()</c>. In Godot Mono, <c>OS.ExecuteWithPipe()</c> creates a single bidirectional
/// <c>FileAccess</c> handle. Blocking reads on background reader threads stall concurrent writes on the
/// same handle, producing unrecoverable deadlocks. Asynchronous event-driven stdout reading completely
/// eliminates thread contention and handles high-frequency JSON frames (~10-50 KB/line) with zero latency.
/// </para>
/// <para>
/// <b>Engine Abstraction for Non-Angband Roguelikes:</b><br/>
/// Any grid-based turn-based engine (e.g. NetHack, DCSS, Moria, ADOM, Brogue, Rogue, Sil, or a custom engine)
/// can be substituted without modifying any 3D rendering or UI code. The engine only needs to implement
/// the Bridge Protocol by serializing JSON lines to stdout and consuming key commands from stdin.
/// </para>
/// <para>
/// <b>Synchronization Contract (<c>seq</c>):</b><br/>
/// The engine emits an initial frame when waiting for input *before* any command is sent.
/// Clients must consume that initial frame upon connect, and after each command wait for <c>seq</c>
/// to advance before sending subsequent input.
/// </para>
/// </remarks>
public partial class BridgeClient : Node, IGameEngineBridge
{
    /// <summary>Raised on the main Godot thread when a new authoritative game state frame arrives.</summary>
    [Signal]
    public delegate void FrameReceivedEventHandler();

    /// <summary>Raised when the engine process terminates or the stream is severed.</summary>
    [Signal]
    public delegate void DisconnectedEventHandler(string reason);

    /// <summary>Current authoritative game state frame from the engine.</summary>
    public JsonElement? Frame { get; private set; }

    /// <summary>Initial handshake message emitted by the engine containing build and save info.</summary>
    public JsonElement? Hello { get; private set; }

    /// <summary>Dynamic terrain table sent once by the engine, keyed by feature index.</summary>
    public readonly Dictionary<int, (string Name, bool Passable)> Features = new();

    /// <summary>Read-only feature dictionary implementing <see cref="IGameEngineBridge"/>.</summary>
    IReadOnlyDictionary<int, (string Name, bool Passable)> IGameEngineBridge.Features => Features;

    /// <summary>Whether the engine child process is running and IPC streams are open.</summary>
    public bool Connected { get; private set; }

    /// <summary>True while a command has been dispatched and the corresponding frame is pending.</summary>
    public bool Busy { get; private set; }

    private Process _proc;
    private readonly ConcurrentQueue<byte[]> _incoming = new();

    /// <summary>
    /// Monotonically increasing frame sequence counter from the engine.
    /// Commands wait for seq to advance rather than assuming one frame per command.
    /// </summary>
    private long _seq = -1;

    /// <summary>Current frame sequence number.</summary>
    public long Seq => _seq;

    private static readonly byte[] ByeBytes = Encoding.UTF8.GetBytes("{\"t\":\"bye\",\"detail\":\"process exited\"}");

    public override void _Process(double delta)
    {
        // Drain incoming frame queue on Godot's main thread to ensure thread-safe scene graph updates
        while (_incoming.TryDequeue(out var bytes))
        {
            HandleBytes(bytes);
        }
    }

    /// <summary>
    /// Launches the engine executable with bridge arguments and attaches stdio streams.
    /// </summary>
    /// <param name="exePath">Absolute path to the engine binary (e.g. angband.exe).</param>
    /// <param name="saveName">Name of the character/save slot. If null or empty, engine handles character naming.</param>
    /// <param name="newCharacter">Whether to pass -n to create a new character slot.</param>
    public void Start(string exePath, string saveName = null, bool newCharacter = false)
    {
        var info = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? ".",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };

        // -mbridge selects the bridge frontend in main.c
        info.ArgumentList.Add("-mbridge");
        if (!string.IsNullOrEmpty(saveName))
        {
            // Note: -u must be passed as a single token (-uname), not separate tokens
            info.ArgumentList.Add($"-u{saveName}");
        }
        if (newCharacter)
        {
            info.ArgumentList.Add("-n");
        }

        try
        {
            _proc = new Process { StartInfo = info, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _incoming.Enqueue(Encoding.UTF8.GetBytes(e.Data));
                }
            };
            _proc.Exited += (_, _) =>
                _incoming.Enqueue(ByeBytes);
            _proc.Start();
            _proc.BeginOutputReadLine();
            Connected = true;
        }
        catch (Exception e)
        {
            EmitSignal(SignalName.Disconnected, $"could not launch {exePath}: {e.Message}");
        }
    }

    /// <summary>
    /// Parses a JSON frame directly from UTF-8 bytes without string allocation and dispatches state updates.
    /// </summary>
    private void HandleBytes(byte[] bytes)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"bridge: bad JSON ({e.Message}): {Truncate(Encoding.UTF8.GetString(bytes))}");
            return;
        }

        using (doc)
        {
            var msg = doc.RootElement.Clone();
            if (!msg.TryGetProperty("t", out var typeProp))
            {
                return;
            }

            switch (typeProp.GetString())
            {
                case "hello":
                    Hello = msg;
                    GD.Print($"bridge: {msg.GetProperty("build").GetString()} " +
                             $"protocol {msg.GetProperty("protocol").GetInt32()}");
                    break;

                case "features":
                    foreach (var f in msg.GetProperty("features").EnumerateArray())
                    {
                        Features[f.GetProperty("idx").GetInt32()] =
                            (f.GetProperty("name").GetString(), f.GetProperty("passable").GetBoolean());
                    }
                    GD.Print($"bridge: {Features.Count} terrain types");
                    break;

                case "frame":
                    Frame = msg;
                    _seq = msg.GetProperty("seq").GetInt64();
                    Busy = false;
                    EmitSignal(SignalName.FrameReceived);
                    break;

                case "bye":
                    Connected = false;
                    EmitSignal(SignalName.Disconnected, "engine exited");
                    break;

                case "error":
                    GD.PushWarning($"bridge error: {Truncate(Encoding.UTF8.GetString(bytes))}");
                    break;
            }
        }
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] + "..." : s;

    /// <summary>
    /// Dispatches a virtual key specification to the engine (e.g. "up", "left", "escape", "C-s").
    /// Gated on <see cref="Busy"/> to prevent frame queue pile-ups.
    /// </summary>
    public void SendKey(string spec)
    {
        if (Busy)
        {
            return;
        }
        Busy = true;
        Send($"key {spec}");
    }

    /// <summary>
    /// Writes a raw command line to the engine's stdin stream.
    /// </summary>
    public void Send(string command)
    {
        if (_proc is not { HasExited: false })
        {
            return;
        }
        try
        {
            _proc.StandardInput.Write(command + "\n");
            _proc.StandardInput.Flush();
        }
        catch (Exception e)
        {
            GD.PushWarning($"bridge: write failed: {e.Message}");
        }
    }

    /// <summary>
    /// Gracefully closes the engine process via 'quit' command, falling back to process tree kill on timeout.
    /// </summary>
    public void Stop()
    {
        if (_proc == null)
        {
            return;
        }
        try
        {
            if (!_proc.HasExited)
            {
                Send("quit");
                if (!_proc.WaitForExit(2000))
                {
                    _proc.Kill(entireProcessTree: true);
                }
            }
        }
        catch (Exception)
        {
            // Already gone; nothing to clean up.
        }
        _proc.Dispose();
        _proc = null;
        Connected = false;
    }

    public override void _ExitTree() => Stop();
}
