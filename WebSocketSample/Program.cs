using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WebSocketSample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.tcplistener?redirectedfrom=MSDN&view=net-5.0

            // Create TcpListener server.
            var server = new TcpListener(IPAddress.Parse("127.0.0.1"), 8080);

            // Start listening for client requests.
            server.Start();

            // Buffer for reading data (256 byte)
            var buffer = new byte[256];

            // Enter the listening loop
            while (true)
            {
                // Waits for a TCP connections, accepts it and returns it as a TcpClient object;
                using var client = await server.AcceptTcpClientAsync();
                await using var stream = client.GetStream();

                int receiveLength;
                // Loop to receive all the data sent by the client
                while ((receiveLength = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) != 0)
                {
                    if (receiveLength < 3) throw new InvalidDataException("Fail to read GET method.");

                    var input = new List<byte>();
                    do
                    {
                        // Translate data bytes to a ASCII string
                        input.AddRange(buffer);
                    } while (stream.DataAvailable && await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)) != 0);


                    var bytes = input.ToArray();
                    var data = Encoding.UTF8.GetString(bytes);

                    if (Regex.IsMatch(data, "^GET", RegexOptions.IgnoreCase))
                    {
                        var swk = Regex.Match(data, "Sec-WebSocket-Key:(.*)").Groups[1].Value.Trim();
                        var swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                        var swkaSha1 = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
                        var swkaSha1BAse64 = Convert.ToBase64String(swkaSha1);


                        // HTTP/1.1 defines the sequence CR LF as the end-of-line marker
                        var sb = new StringBuilder();
                        sb.AppendLine("HTTP/1.1 101 Switching Protocols");
                        sb.AppendLine("Connection: Upgrade");
                        sb.AppendLine("Upgrade: websocket");
                        sb.AppendLine($"Sec-WebSocket-Accept: {swkaSha1BAse64}");
                        sb.AppendLine();
                        var response = Encoding.UTF8.GetBytes(sb.ToString());
                        await stream.WriteAsync(response.AsMemory(0, response.Length));
                    }
                    else
                    {
                        var fin = (bytes[0] & 0x80) != 0; // & 0100 0000
                        var mask = (bytes[1] & 0x80) != 0; // & 0100 0000
                        var opcode = bytes[0] & 0x0F; // & 0000 1111
                        var msglen = bytes[1] & 0x7F; // & 0111 1111
                        var offset = 2;

                        switch (msglen)
                        {
                            case 126:
                                msglen = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(bytes, 2, 3));
                                offset = 4;
                                break;
                            case 127:
                                msglen = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(bytes, 2, 7));
                                offset = 10;
                                break;
                        }

                        if (mask)
                        {
                            var decoded = new byte[msglen];

                            for (var i = 0; i < msglen; i++)
                                decoded[i] =
                                    (byte) (bytes[offset + i + 4] ^ bytes.AsSpan().Slice(offset, 4)[i % 4]);

                            var text = Encoding.UTF8.GetString(decoded);

                            Console.WriteLine($"{text}");
                        }
                        else
                        {
                            Console.WriteLine("mask bit not set");
                        }
                    }
                }
            }

            ;
        }
    }
}