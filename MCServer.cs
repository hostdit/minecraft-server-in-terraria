using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MinecraftServer
{
    public class MCServer
    {
        private TcpListener _listener;
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _thread;
        private readonly object _sendLock = new object();
        private readonly List<byte> _inbuf = new List<byte>();
        private readonly byte[] _section = MCProtocol.BuildSection();

        private volatile bool _running;
        private volatile int _state;
        private string _username = "";
        private DateTime _lastKeep = DateTime.UtcNow;
        private int _keepId;

        public readonly ConcurrentQueue<MCEvent> Events = new ConcurrentQueue<MCEvent>();

        public static string FaviconStatus { get; private set; } = "not loaded";
        private static string _favicon = "";

        public static IEnumerable<string> FaviconPaths()
        {
            string save = Terraria.Main.SavePath;
            yield return Path.Combine(save, "favicon.png");
            yield return Path.Combine(save, "Mods", "favicon.png");
            yield return Path.Combine(Directory.GetCurrentDirectory(), "favicon.png");
        }

        public static string LoadFavicon()
        {
            foreach (string path in FaviconPaths())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    byte[] raw = File.ReadAllBytes(path);
                    if (raw.Length == 0) continue;
                    _favicon = Convert.ToBase64String(raw);
                    FaviconStatus = $"loaded {raw.Length} bytes from {path}";
                    return FaviconStatus;
                }
                catch (Exception ex)
                {
                    FaviconStatus = "error reading " + path + ": " + ex.Message;
                    return FaviconStatus;
                }
            }
            _favicon = "";
            FaviconStatus = "no favicon.png found";
            return FaviconStatus;
        }

        public bool Running => _running;
        public bool Playing => _state == 3;
        public string Username => _username;
        public int PacketsIn { get; private set; }
        public int PacketsOut { get; private set; }
        public string LastError { get; private set; } = "";

        public bool Start()
        {
            if (_running) return false;
            try
            {
                _listener = new TcpListener(IPAddress.Any, MCProtocol.Port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress, true);
                _listener.Start();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Emit(MCEventKind.Status, "bind failed: " + ex.Message);
                return false;
            }

            LoadFavicon();
            Emit(MCEventKind.Status, "favicon: " + FaviconStatus);

            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "mcserver" };
            _thread.Start();
            Emit(MCEventKind.Status, "listening on port " + MCProtocol.Port);
            return true;
        }

        public void Stop()
        {
            _running = false;
            CloseClient();
            try { _listener?.Stop(); } catch { }
            _listener = null;
            try { _thread?.Join(2000); } catch { }
            _thread = null;
        }

        private void Emit(MCEventKind kind, string text)
        {
            Events.Enqueue(new MCEvent { Kind = kind, Text = text });
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    _client = _listener.AcceptTcpClient();
                }
                catch
                {
                    if (!_running) break;
                    continue;
                }

                _client.ReceiveTimeout = 1000;
                _stream = _client.GetStream();
                _state = 0;
                _inbuf.Clear();
                PacketsIn = 0;
                PacketsOut = 0;
                Emit(MCEventKind.Status, "connection accepted");
                Serve();
                CloseClient();
            }
            Emit(MCEventKind.Status, "stopped");
        }

        private void Serve()
        {
            byte[] chunk = new byte[65536];
            while (_running && _stream != null)
            {
                int read = 0;
                try
                {
                    read = _stream.Read(chunk, 0, chunk.Length);
                }
                catch (IOException)
                {
                    KeepAlive();
                    continue;
                }
                catch
                {
                    break;
                }

                if (read <= 0) break;

                for (int i = 0; i < read; i++) _inbuf.Add(chunk[i]);
                try
                {
                    Drain();
                }
                catch (Exception ex)
                {
                    Emit(MCEventKind.Status, "handler error: " + ex.Message);
                }
                KeepAlive();
            }
        }

        private void Drain()
        {
            while (_stream != null)
            {
                byte[] buffer = _inbuf.ToArray();
                int pos = 0;
                if (!MCProtocol.ReadVarInt(buffer, buffer.Length, ref pos, out int length)) return;
                if (length <= 0 || length > 2097152) return;
                if (buffer.Length < pos + length) return;

                byte[] body = new byte[length];
                Array.Copy(buffer, pos, body, 0, length);
                _inbuf.RemoveRange(0, pos + length);
                PacketsIn++;
                Handle(body);
            }
        }

        private void Handle(byte[] body)
        {
            int pos = 0;
            if (!MCProtocol.ReadVarInt(body, body.Length, ref pos, out int packetId)) return;

            if (_state == 0)
            {
                if (packetId != 0) return;
                MCProtocol.ReadVarInt(body, body.Length, ref pos, out _);
                MCProtocol.ReadVarInt(body, body.Length, ref pos, out int addrLen);
                pos += addrLen + 2;
                MCProtocol.ReadVarInt(body, body.Length, ref pos, out int next);
                _state = next == 1 ? 1 : 2;
                return;
            }

            if (_state == 1)
            {
                if (packetId == 0)
                {
                    var payload = new List<byte>();
                    MCProtocol.WriteString(payload, StatusJson());
                    Send(0x00, payload);
                }
                else if (packetId == 1 && body.Length >= pos + 8)
                {
                    var payload = new List<byte>();
                    for (int i = 0; i < 8; i++) payload.Add(body[pos + i]);
                    Send(0x01, payload);
                }
                return;
            }

            if (_state == 2)
            {
                if (packetId != 0) return;
                MCProtocol.ReadVarInt(body, body.Length, ref pos, out int nameLen);
                _username = Encoding.UTF8.GetString(body, pos, nameLen);
                var payload = new List<byte>();
                MCProtocol.WriteString(payload, MCProtocol.PlayerUuid);
                MCProtocol.WriteString(payload, _username);
                Send(0x02, payload);
                _state = 3;
                SendJoin();
                Events.Enqueue(new MCEvent { Kind = MCEventKind.Joined, Text = _username });
                return;
            }

            HandlePlay(packetId, body, pos);
        }

        private void HandlePlay(int packetId, byte[] body, int pos)
        {
            if (packetId == 0x04 && body.Length >= pos + 24)
            {
                var e = new MCEvent { Kind = MCEventKind.Moved };
                e.X = MCProtocol.ReadDouble(body, ref pos);
                e.Y = MCProtocol.ReadDouble(body, ref pos);
                e.Z = MCProtocol.ReadDouble(body, ref pos);
                Events.Enqueue(e);
            }
            else if (packetId == 0x06 && body.Length >= pos + 32)
            {
                var e = new MCEvent { Kind = MCEventKind.Moved };
                e.X = MCProtocol.ReadDouble(body, ref pos);
                e.Y = MCProtocol.ReadDouble(body, ref pos);
                e.Z = MCProtocol.ReadDouble(body, ref pos);
                Events.Enqueue(e);
            }
            else if (packetId == 0x07 && body.Length >= pos + 9)
            {
                byte status = body[pos++];
                ulong packed = MCProtocol.ReadLong(body, ref pos);
                if (status == 0 || status == 2)
                {
                    MCProtocol.DecodePosition(packed, out int bx, out int by, out int bz);
                    Events.Enqueue(new MCEvent
                    {
                        Kind = MCEventKind.Broke,
                        BlockX = bx, BlockY = by, BlockZ = bz
                    });
                }
            }
            else if (packetId == 0x08 && body.Length >= pos + 11)
            {
                ulong packed = MCProtocol.ReadLong(body, ref pos);
                byte face = body[pos++];
                if (face > 5) return;
                int item = (short)((body[pos] << 8) | body[pos + 1]);
                if (item < 1 || item > 255) return;
                MCProtocol.DecodePosition(packed, out int bx, out int by, out int bz);
                int[] dx = { 0, 0, 0, 0, -1, 1 };
                int[] dy = { -1, 1, 0, 0, 0, 0 };
                int[] dz = { 0, 0, -1, 1, 0, 0 };
                Events.Enqueue(new MCEvent
                {
                    Kind = MCEventKind.Placed,
                    BlockX = bx + dx[face],
                    BlockY = by + dy[face],
                    BlockZ = bz + dz[face],
                    BlockId = item
                });
            }
            else if (packetId == 0x01)
            {
                MCProtocol.ReadVarInt(body, body.Length, ref pos, out int len);
                Events.Enqueue(new MCEvent
                {
                    Kind = MCEventKind.Chat,
                    Text = Encoding.UTF8.GetString(body, pos, len)
                });
            }
        }

        private string StatusJson()
        {
            int online = _state == 3 ? 1 : 0;
            string icon = string.IsNullOrEmpty(_favicon)
                ? ""
                : ",\"favicon\":\"data:image/png;base64," + _favicon + "\"";
            return "{\"version\":{\"name\":\"1.8.9\",\"protocol\":47},"
                 + "\"players\":{\"max\":1,\"online\":" + online + "},"
                 + "\"description\":{\"text\":\"" + MCProtocol.Motd + "\"}" + icon + "}";
        }

        private void SendJoin()
        {
            var join = new List<byte>();
            MCProtocol.WriteInt(join, 1);
            join.Add(1);
            join.Add(0);
            join.Add(0);
            join.Add(1);
            MCProtocol.WriteString(join, "flat");
            join.Add(0);
            Send(0x01, join);

            var spawn = new List<byte>();
            MCProtocol.WriteLong(spawn, MCProtocol.EncodePosition(0, 5, 0));
            Send(0x05, spawn);

            for (int cx = -MCProtocol.ViewDist; cx <= MCProtocol.ViewDist; cx++)
            {
                for (int cz = -MCProtocol.ViewDist; cz <= MCProtocol.ViewDist; cz++)
                {
                    var chunk = new List<byte>();
                    MCProtocol.WriteInt(chunk, cx);
                    MCProtocol.WriteInt(chunk, cz);
                    chunk.Add(1);
                    MCProtocol.WriteUShort(chunk, 1);
                    MCProtocol.WriteVarInt(chunk, _section.Length);
                    chunk.AddRange(_section);
                    Send(0x21, chunk);
                }
            }

            var look = new List<byte>();
            MCProtocol.WriteDouble(look, 0.5);
            MCProtocol.WriteDouble(look, 5.0);
            MCProtocol.WriteDouble(look, 0.5);
            MCProtocol.WriteFloat(look, 0f);
            MCProtocol.WriteFloat(look, 0f);
            look.Add(0);
            Send(0x08, look);

            SendChat("Served from Terraria");
            _lastKeep = DateTime.UtcNow;
        }

        private void KeepAlive()
        {
            if (_state != 3) return;
            if ((DateTime.UtcNow - _lastKeep).TotalSeconds < 10) return;
            _keepId++;
            var payload = new List<byte>();
            MCProtocol.WriteVarInt(payload, _keepId);
            Send(0x00, payload);
            _lastKeep = DateTime.UtcNow;
        }

        public void SendChat(string text)
        {
            if (_state != 3) return;
            var payload = new List<byte>();
            MCProtocol.WriteString(payload, "{\"text\":\"" + text + "\"}");
            payload.Add(0);
            Send(0x02, payload);
        }

        public void SendBlockChange(int x, int y, int z, int blockId)
        {
            if (_state != 3) return;
            var payload = new List<byte>();
            MCProtocol.WriteLong(payload, MCProtocol.EncodePosition(x, y, z));
            MCProtocol.WriteVarInt(payload, blockId << 4);
            Send(0x23, payload);
        }

        private void Send(int packetId, List<byte> payload)
        {
            var body = new List<byte>();
            MCProtocol.WriteVarInt(body, packetId);
            body.AddRange(payload);
            var frame = new List<byte>();
            MCProtocol.WriteVarInt(frame, body.Count);
            frame.AddRange(body);

            lock (_sendLock)
            {
                if (_stream == null) return;
                try
                {
                    byte[] raw = frame.ToArray();
                    _stream.Write(raw, 0, raw.Length);
                    _stream.Flush();
                    PacketsOut++;
                }
                catch
                {
                    _stream = null;
                }
            }
        }

        private void CloseClient()
        {
            lock (_sendLock)
            {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null;
                _client = null;
            }
            if (_state == 3)
            {
                Events.Enqueue(new MCEvent { Kind = MCEventKind.Left, Text = _username });
            }
            _state = 0;
            _username = "";
        }
    }
}