using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Kursovoi.Models
{
    public static class UdpClientHelper
    {
        private const int Port = 12345;
        private const string ServerAddress = "127.0.0.1";
        private const int ReceiveTimeoutMs = 3000; // 3 seconds

        public static string SendUdpMessage(string message)
        {
            using (UdpClient udpClient = new UdpClient())
            {
                try
                {
                    udpClient.EnableBroadcast = true;
                    udpClient.Client.ReceiveTimeout = ReceiveTimeoutMs;

                    byte[] messageByte = Encoding.UTF8.GetBytes(message);
                    udpClient.Send(messageByte, messageByte.Length, ServerAddress, Port);

                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    try
                    {
                        byte[] resultByte = udpClient.Receive(ref remote);
                        return Encoding.UTF8.GetString(resultByte);
                    }
                    catch (SocketException sex)
                    {
                        // timeout or network error
                        return "ERROR|UDP receive timeout or network error: " + sex.Message;
                    }
                }
                catch (System.Exception ex)
                {
                    return "ERROR|UDP send error: " + ex.Message;
                }
            }
        }
    }
}
