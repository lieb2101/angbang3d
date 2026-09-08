using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// Owns the Angband child process and speaks the bridge protocol to it.
/// </summary>
/// <remarks>
/// Uses System.Diagnostics.Process rather than Godot's OS.ExecuteWithPipe:
/// that returns a single bidirectional FileAccess, and a blocking read from a
/// reader thread stalls writes on the same handle, which deadlocks.
/// </remarks>
public partial class BridgeClient : Node
{
    /// <summary>Raised on the main thread when a new frame arrives.</summary>
    [Signal]
    public delegate void FrameReceivedEventHandler();

    [Signal]
    public delegate void DisconnectedEventHandler(string reason);

    public JsonElement? Frame { get; private set; }
    public JsonElement? Hello { get; private set; }

    /// <summary>Terrain table sent once by the engine, keyed by feature index.</summary>
    public readonly System.Collections.Generic.Dictionary<int, (string Name, bool Passable)>
        Features = new();
    public bool Connected { get; private set; }

    /// <summary>True while a command has been sent but its frame has not arrived.</summary>
    public bool Busy { get; private set; }

    private Process _proc;
    private readonly ConcurrentQueue<string> _incoming = new();

    /// <summary>
    /// Frames are emitted whenever the game blocks for input, including once
    /// before any command is sent, so commands wait for seq to advance rather
    /// than assuming one frame per command.
    /// </summary>
    private long _seq = -1;

    public long Seq => _seq;

    public override void _Process(double delta)
    {
        while (_incoming.TryDequeue(out var line))
        {
            HandleLine(line);
        }
    }

    public void Start(string exePath, string saveName = "angband3d", bool newCharacter = true)
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
        info.ArgumentList.Add("-mbridge");
        if (!string.IsNullOrEmpty(saveName))
        {
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
                    _incoming.Enqueue(e.Data);
                }
            };
            _proc.Exited += (_, _) =>
                _incoming.Enqueue("{\"t\":\"bye\",\"detail\":\"process exited\"}");
            _proc.Start();
            _proc.BeginOutputReadLine();
            Connected = true;
        }
        catch (Exception e)
        {
            EmitSignal(SignalName.Disconnected, $"could not launch {exePath}: {e.Message}");
        }
    }

    private void HandleLine(string line)
    {
        JsonElement msg;
        try
        {
            msg = JsonDocument.Parse(line).RootElement.Clone();
        }
        catch (JsonException e)
        {
            GD.PushWarning($"bridge: bad JSON ({e.Message}): {Truncate(line)}");
            return;
        }

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
                GD.PushWarning($"bridge error: {Truncate(line)}");
                break;
        }
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] + "..." : s;

    public void SendKey(string spec)
    {
        if (Busy)
        {
            return;
        }
        Busy = true;
        Send($"key {spec}");
    }

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
