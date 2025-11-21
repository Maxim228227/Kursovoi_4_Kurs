using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Kursovoi.Models
{
    public static class UdpClientHelper
    {
        private const int Port = 12345;
        private const string ServerAddress = "127.0.0.1";

        public static string SendUdpMessage(string message)
        {
            using (UdpClient udpClient = new UdpClient())
            {
                udpClient.EnableBroadcast = true;
                byte[] messageByte = Encoding.UTF8.GetBytes(message);
                udpClient.Send(messageByte, messageByte.Length, ServerAddress, Port);

                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] resultByte = udpClient.Receive(ref remote);
                return Encoding.UTF8.GetString(resultByte);
            }
        }
    }
}
