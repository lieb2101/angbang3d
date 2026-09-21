using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Angband3D;

/// <summary>
/// Cloud/Network implementation of <see cref="IGameEngineBridge"/> communicating with a remote
/// Angband3D containerized engine relay over WebSockets (ws:// or wss://).
/// </summary>
public partial class WebSocketBridgeClient : Node, IGameEngineBridge
{
    /// <summary>Raised on the main Godot thread when a new authoritative game state frame arrives.</summary>
    [Signal]
    public delegate void FrameReceivedEventHandler();

    /// <summary>Raised when the network connection is closed or lost.</summary>
    [Signal]
    public delegate void DisconnectedEventHandler(string reason);

    /// <summary>Current authoritative game state frame from the cloud engine.</summary>
    public JsonElement? Frame { get; private set; }

    /// <summary>Initial handshake message emitted by the engine containing build and save info.</summary>
    public JsonElement? Hello { get; private set; }

    /// <summary>Dynamic terrain table sent once by the engine, keyed by feature index.</summary>
    public readonly Dictionary<int, (string Name, bool Passable)> Features = new();

    /// <summary>Read-only feature dictionary implementing <see cref="IGameEngineBridge"/>.</summary>
    IReadOnlyDictionary<int, (string Name, bool Passable)> IGameEngineBridge.Features => Features;

    /// <summary>Whether the WebSocket connection to the cloud server is open.</summary>
    public bool Connected => _ws?.State == WebSocketState.Open;

    /// <summary>True while a command has been dispatched and the corresponding frame is pending.</summary>
    public bool Busy { get; private set; }

    /// <summary>Monotonically increasing frame sequence counter from the engine.</summary>
    public long Seq { get; private set; } = -1;

    /// <summary>Roundtrip network latency to cloud relay in milliseconds.</summary>
    public int PingMs { get; private set; }

    public string ServerUrl { get; set; } = "ws://localhost:8080/ws";

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private readonly ConcurrentQueue<byte[]> _incoming = new();
    private readonly ConcurrentQueue<string> _outgoing = new();
    private Task _receiveTask;
    private Task _sendTask;
    private long _lastPingSentTick;

    private static readonly byte[] ByeBytes = Encoding.UTF8.GetBytes("{\"t\":\"bye\",\"detail\":\"server connection closed\"}");

    public override void _Process(double delta)
    {
        // Drain incoming frame queue on Godot's main thread to ensure thread-safe scene graph updates
        while (_incoming.TryDequeue(out var bytes))
        {
            HandleBytes(bytes);
        }
    }

    /// <summary>
    /// Connects to the cloud engine relay over WebSockets.
    /// </summary>
    /// <param name="wsUrl">Full WebSocket URL e.g. ws://localhost:8080/ws or wss://game.example.com/ws</param>
    /// <param name="saveName">Character/save slot name to load or initialize.</param>
    public void ConnectToServer(string wsUrl, string saveName = null)
    {
        Stop();

        ServerUrl = wsUrl;
        var uriBuilder = new UriBuilder(wsUrl);
        if (!string.IsNullOrEmpty(saveName))
        {
            var query = uriBuilder.Query;
            if (query.Length > 1) query += "&";
            query += $"save={Uri.EscapeDataString(saveName)}";
            uriBuilder.Query = query.TrimStart('?');
        }

        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();

        Task.Run(async () =>
        {
            try
            {
                GD.Print($"ws-bridge: connecting to {uriBuilder.Uri}...");
                await _ws.ConnectAsync(uriBuilder.Uri, _cts.Token);
                GD.Print("ws-bridge: connected to cloud server.");

                _receiveTask = Task.Run(ReceiveLoop, _cts.Token);
                _sendTask = Task.Run(SendLoop, _cts.Token);
            }
            catch (Exception ex)
            {
                GD.PushError($"ws-bridge: connection failed: {ex.Message}");
                CallDeferred(nameof(EmitDisconnectedDeferred), $"Failed to connect: {ex.Message}");
            }
        });
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[65536];
        using var ms = new MemoryStream();

        try
        {
            while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    _incoming.Enqueue(ByeBytes);
                    break;
                }

                ms.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    var msgBytes = ms.ToArray();
                    ms.SetLength(0);

                    // If ping response measured
                    if (_lastPingSentTick > 0)
                    {
                        var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - _lastPingSentTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        PingMs = (int)elapsed;
                        _lastPingSentTick = 0;
                    }

                    _incoming.Enqueue(msgBytes);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GD.PushWarning($"ws-bridge: receive error: {ex.Message}");
            _incoming.Enqueue(ByeBytes);
        }
    }

    private async Task SendLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                if (_outgoing.TryDequeue(out var cmd))
                {
                    var bytes = Encoding.UTF8.GetBytes(cmd + "\n");
                    _lastPingSentTick = System.Diagnostics.Stopwatch.GetTimestamp();
                    await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
                }
                else
                {
                    await Task.Delay(5, _cts.Token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GD.PushWarning($"ws-bridge: send error: {ex.Message}");
        }
    }

    private void HandleBytes(byte[] bytes)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"ws-bridge: bad JSON ({e.Message}): {Truncate(Encoding.UTF8.GetString(bytes))}");
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
                    GD.Print($"ws-bridge: {msg.GetProperty("build").GetString()} " +
                             $"protocol {msg.GetProperty("protocol").GetInt32()}");
                    break;

                case "features":
                    foreach (var f in msg.GetProperty("features").EnumerateArray())
                    {
                        Features[f.GetProperty("idx").GetInt32()] =
                            (f.GetProperty("name").GetString(), f.GetProperty("passable").GetBoolean());
                    }
                    GD.Print($"ws-bridge: {Features.Count} terrain types");
                    break;

                case "frame":
                    Frame = msg;
                    Seq = msg.GetProperty("seq").GetInt64();
                    Busy = false;
                    EmitSignal(SignalName.FrameReceived);
                    break;

                case "bye":
                    EmitSignal(SignalName.Disconnected, "cloud engine exited");
                    break;

                case "error":
                    GD.PushWarning($"ws-bridge error: {Truncate(Encoding.UTF8.GetString(bytes))}");
                    break;
            }
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
        if (!Connected)
        {
            return;
        }
        _outgoing.Enqueue(command);
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _ws?.Dispose();
        }
        catch { }
        finally
        {
            _ws = null;
            _cts = null;
            Busy = false;
        }
    }

    private void EmitDisconnectedDeferred(string reason)
    {
        EmitSignal(SignalName.Disconnected, reason);
    }

    public override void _ExitTree()
    {
        Stop();
    }
}
