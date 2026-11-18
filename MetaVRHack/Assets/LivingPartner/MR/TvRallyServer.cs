using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace LivingPartner.MR
{
    /// <summary>A local second screen. Only the running game's state and ready/replay controls are served.</summary>
    public sealed class TvRallyServer : IDisposable
    {
        readonly TcpListener _listener;
        readonly byte[] _page, _portrait;
        readonly string _path;
        volatile byte[] _state = Encoding.UTF8.GetBytes("{\"phase\":\"Join\",\"score\":0,\"rally\":0,\"side\":-1,\"elapsed\":0,\"notice\":\"\",\"marks\":0}");
        volatile bool _closed;
        long _lastSeen;
        int _ready;
        public string Url { get; }
        public bool TakeReady() => Interlocked.Exchange(ref _ready, 0) != 0;
        public bool ViewerPresent => (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastSeen)) < TimeSpan.FromSeconds(7).Ticks;
        public void Publish(string json) { _state = Encoding.UTF8.GetBytes(json); }

        public TvRallyServer(string page, byte[] portrait)
        {
            IPAddress address = LocalAddress();
            if (address == null) throw new IOException("Connect Quest and TV to the same Wi-Fi.");
            byte[] random = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
            _path = "/" + (100000 + BitConverter.ToUInt32(random, 0) % 900000);
            _page = Encoding.UTF8.GetBytes(page);
            _portrait = portrait;
            _listener = new TcpListener(address, 0);
            _listener.Start(4);
            Url = "http://" + address + ":" + ((IPEndPoint)_listener.LocalEndpoint).Port + _path;
            new Thread(Serve) { IsBackground = true, Name = "TV Rally display" }.Start();
        }

        static IPAddress LocalAddress()
        {
            // Route selection asks the OS for its Wi-Fi address without sending a packet.
            try
            {
                using (var route = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    route.Connect(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9));
                    var ip = ((IPEndPoint)route.LocalEndPoint).Address;
                    if (!IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any)) return ip;
                }
            }
            catch (SocketException) { }
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                if (nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    foreach (var a in nic.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)) return a.Address;
            return null;
        }

        void Serve()
        {
            while (!_closed)
            {
                try
                {
                    using (var client = _listener.AcceptTcpClient())
                    {
                        client.ReceiveTimeout = 1200; client.SendTimeout = 1200;
                        using (var stream = client.GetStream()) Handle(stream);
                    }
                }
                catch (Exception) when (!_closed) { }
                catch (Exception) { return; }
            }
        }
        void Handle(NetworkStream stream)
        {
            var header = new byte[4096];
            int n = 0, end = -1;
            while (n < header.Length && end < 0)
            {
                int read = stream.Read(header, n, header.Length - n);
                if (read == 0) return;
                int from = Math.Max(0, n - 3); n += read;
                for (int i = from; i + 3 < n; i++)
                    if (header[i] == 13 && header[i+1] == 10 && header[i+2] == 13 && header[i+3] == 10) { end = i; break; }
            }
            if (end < 0) return;
            string line = Encoding.ASCII.GetString(header, 0, end).Split('\r')[0];
            string[] parts = line.Split(' ');
            if (parts.Length != 3) return;
            string path = parts[1];
            bool page = parts[0] == "GET" && (path == _path || path == _path + "/");
            bool state = parts[0] == "GET" && path == _path + "/state";
            bool ready = parts[0] == "POST" && path == _path + "/ready";
            bool picture = parts[0] == "GET" && path == _path + "/bambi.png";
            if (state || ready) Interlocked.Exchange(ref _lastSeen, DateTime.UtcNow.Ticks);
            if (ready) Interlocked.Exchange(ref _ready, 1);
            byte[] body = page ? _page : picture ? _portrait : state || ready ? _state : Array.Empty<byte>();
            var prefix = Encoding.ASCII.GetBytes("HTTP/1.1 " + (page || state || ready || picture ? "200 OK" : "404 Not Found")
                + "\r\nContent-Type: " + (page ? "text/html; charset=utf-8" : picture ? "image/png" : "application/json")
                + "\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
            stream.Write(prefix, 0, prefix.Length); stream.Write(body, 0, body.Length);
        }
        public void Dispose() { _closed = true; _listener.Stop(); }
    }
}
