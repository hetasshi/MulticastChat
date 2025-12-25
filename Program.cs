using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MulticastChat
{
    class Program
    {
        private static readonly string MulticastAddress = "239.0.0.1";
        private static readonly int Port = 12345;
        private static readonly string SessionId = Guid.NewGuid().ToString().Substring(0, 4);
        private static string _userName;
        private static UdpClient _udpClient;
        private static readonly HashSet<string> ReceivedMessagesIds = new HashSet<string>();
        private static readonly Dictionary<string, DateTime> OnlineUsers = new Dictionary<string, DateTime>();
        private static readonly object LockObj = new object();

        static async Task Main()
        {
            Console.Write("Имя: ");
            _userName = Console.ReadLine() ?? "User";

            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            _udpClient.JoinMulticastGroup(IPAddress.Parse(MulticastAddress));

            _ = Task.Run(ReceiveMessages);
            _ = Task.Run(SendHeartbeat);

            while (true)
            {
                Console.Write($"[{_userName}]: ");
                string txt = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(txt)) continue;

                if (txt == "/list")
                {
                    ShowOnline();
                }
                else
                {
                    Send("MSG", txt);
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                    Console.WriteLine($"[{_userName}]: {txt}"); 
                }
            }
        }

        private static void Send(string type, string content)
        {
            string msgId = Guid.NewGuid().ToString().Substring(0, 8);
            string packet = $"{type}|{msgId}|{_userName}|{content}";
            byte[] data = Encoding.UTF8.GetBytes(packet);
            _udpClient.Send(data, data.Length, new IPEndPoint(IPAddress.Parse(MulticastAddress), Port));
        }

        private static async Task ReceiveMessages()
        {
            while (true)
            {
                var res = await _udpClient.ReceiveAsync();
                string[] p = Encoding.UTF8.GetString(res.Buffer).Split('|');
                if (p.Length < 4) continue;

                string type = p[0], msgId = p[1], user = p[2], text = p[3];

                lock (LockObj)
                {
                    if (user == _userName || ReceivedMessagesIds.Contains(msgId)) continue;
                    ReceivedMessagesIds.Add(msgId);
                    
                    _ = Task.Delay(5000).ContinueWith(_ => { lock(LockObj) ReceivedMessagesIds.Remove(msgId); });
                }

                if (type == "MSG") Console.WriteLine($"\n[{user}]: {text}");
                else if (type == "ALIVE") lock(LockObj) OnlineUsers[user] = DateTime.Now;
            }
        }

        private static async Task SendHeartbeat()
        {
            while (true) { Send("ALIVE", ""); await Task.Delay(5000); }
        }

        private static void ShowOnline()
        {
            lock (LockObj) 
            {
                Console.WriteLine("\n-- Онлайн: " + string.Join(", ", OnlineUsers.Where(u => u.Value > DateTime.Now.AddSeconds(-15)).Select(u => u.Key)));
            }
        }
    }
}