using ETS2LA.Logging;
using ETS2LA.Shared;
using ETS2LA.Game.SDK;
using ETS2LA.Backend.Events;

using System.Net.WebSockets;
using System.Text.Json.Serialization;
using System.Text.Json;
using TruckLib;

namespace VisualizationSockets.Channels;

public class TrafficChannel : IWebsocketChannel
{
    private Plugin? _plugin;
    private WebSocket? _socket;
    public string Name => "Traffic";
    public string Description => "Sends traffic data, includes all traffic vehicles and objects (TODO!).";
    public int Channel => 4;
    public WebSocketChannelType ChannelType => WebSocketChannelType.Continuous;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    TrafficData? _data;

    public void Init(Plugin plugin, WebSocket socket)
    {
        _plugin = plugin;
        _socket = socket;
        Events.Current.Subscribe<TrafficData>("ETS2LASDK.Traffic", OnTrafficUpdate);
        Task.Run(() => SenderThread());
    }

    public void OnTrafficUpdate(TrafficData data)
    {
        _data = data;
    }

    private void SenderThread()
    {
        while (_socket != null && _socket.State == WebSocketState.Open)
        {
            int start = Environment.TickCount;
            SendData(_socket);
            int end = Environment.TickCount;
            int elapsed = end - start;
            System.Threading.Thread.Sleep(Math.Max(50 - elapsed, 0)); // 20 FPS
        }
    }

    public async void SendData(WebSocket socket)
    {
        if (_plugin == null) return;
        if (_data == null) return;

        var output = new
        {
            channel = Channel,
            data = new
            {
                vehicles = _data?.vehicles.Select(v => new
                {
                    position = new
                    {
                        x = v.Position.X,
                        y = v.Position.Y,
                        z = v.Position.Z,
                    },
                    rotation = new
                    {
                        x = v.Rotation.X,
                        y = v.Rotation.Y,
                        z = v.Rotation.Z,
                        w = v.Rotation.W,
                        yaw = v.Rotation.ToEulerDeg().Y,
                        pitch = v.Rotation.ToEulerDeg().X,
                        roll = v.Rotation.ToEulerDeg().Z,
                    },
                    size = new
                    {
                        width = v.Size.X,
                        height = v.Size.Y,
                        length = v.Size.Z,
                    },
                    speed = v.speed,
                    acceleration = v.acceleration,
                    trailer_count = v.trailer_count,
                    id = v.id,
                    trailers = v.trailers.Select(t => new
                    {
                        position = new
                        {
                            x = t.Position.X,
                            y = t.Position.Y,
                            z = t.Position.Z,
                        },
                        rotation = new
                        {
                            x = t.Rotation.X,
                            y = t.Rotation.Y,
                            z = t.Rotation.Z,
                            w = t.Rotation.W,
                            yaw = t.Rotation.ToEulerDeg().Y,
                            pitch = t.Rotation.ToEulerDeg().X,
                            roll = t.Rotation.ToEulerDeg().Z,
                        },
                        size = new
                        {
                            width = t.Size.X,
                            height = t.Size.Y,
                            length = t.Size.Z,
                        },
                    }).ToArray(),
                    is_tmp = v.isTMP,
                    is_trailer = v.isTrailer,
                })
            }
        };

        var json = JsonSerializer.Serialize(output, options: JsonOptions);
        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
        try
        {
            if (socket.State != WebSocketState.Open)
                return;

            await WebsocketLock.Current.Semaphore.WaitAsync();
            await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        } 
        catch (WebSocketException) 
        {
            Logger.Info($"Client [dim]{socket.GetHashCode()}[/] disconnected.");
        }
        finally
        {
            WebsocketLock.Current.Semaphore.Release();
        }
    }

    public void Shutdown()
    {
        _socket = null;
        Events.Current.Unsubscribe<TrafficData>("ETS2LASDK.Traffic", OnTrafficUpdate);
    }
}
