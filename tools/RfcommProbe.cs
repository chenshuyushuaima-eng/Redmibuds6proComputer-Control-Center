using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

// SPDX-License-Identifier: AGPL-3.0-or-later
// Read-only probe: authentication, device information and configuration queries.
public static class RfcommProbe
{
    private static byte sequence;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Provide the paired Bluetooth address.");
            Run(Convert.ToUInt64(args[0].Replace(":", "").Replace("-", ""), 16)).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAILED: " + error.ToString());
            return 1;
        }
    }

    private static async Task<T> Bounded<T>(Task<T> task)
    {
        if (await Task.WhenAny(task, Task.Delay(10000)) != task)
            throw new TimeoutException("Bluetooth operation exceeded 10 seconds.");
        return await task;
    }

    private static async Task Bounded(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(10000)) != task)
            throw new TimeoutException("Bluetooth operation exceeded 10 seconds.");
        await task;
    }

    private static async Task Run(ulong address)
    {
        using (var device = await Bounded(BluetoothDevice.FromBluetoothAddressAsync(address).ToTask()))
        {
            if (device == null) throw new IOException("Paired device not found.");
            Console.WriteLine("DEVICE: " + device.Name + " / " + device.ConnectionStatus);
            var services = await Bounded(device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).ToTask());
            if (services.Error != BluetoothError.Success) throw new IOException("Discovery: " + services.Error);
            try
            {
                foreach (var service in services.Services)
                {
                    if (service.ServiceId.Uuid != new Guid("0000fd2d-0000-1000-8000-00805f9b34fb")) continue;
                    using (var socket = new StreamSocket())
                    {
                        await Bounded(socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName,
                            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).ToTask());
                        Console.WriteLine("CONTROL: connected");
                        using (var writer = new DataWriter(socket.OutputStream))
                        using (var reader = new DataReader(socket.InputStream))
                        {
                            reader.InputStreamOptions = InputStreamOptions.Partial;
                            byte[] challenge = new byte[16];
                            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(challenge);
                            byte[] payload = new byte[17];
                            payload[0] = 1;
                            Array.Copy(challenge, 0, payload, 1, 16);
                            await Write(writer, Frame(0xC4, 0x50, sequence++, payload));
                            var buffer = new List<byte>();
                            bool authenticated = false;
                            bool verified = false;
                            bool deviceInfo = false;
                            bool runInfo = false;
                            var pendingConfigs = new Dictionary<byte, byte>();
                            DateTime deadline = DateTime.UtcNow.AddSeconds(25);
                            while (DateTime.UtcNow < deadline)
                            {
                                uint count = await Bounded(reader.LoadAsync(4096).ToTask());
                                if (count == 0) throw new IOException("Control connection closed by device.");
                                byte[] bytes = new byte[count];
                                reader.ReadBytes(bytes);
                                buffer.AddRange(bytes);
                                byte[] packet;
                                while ((packet = Decode(buffer)) != null)
                                {
                                    Console.WriteLine("RX: " + BitConverter.ToString(packet));
                                    byte kind = packet[3], opcode = packet[4];
                                    bool request = (kind & 0x40) != 0;
                                    int offset = request ? 7 : 8;
                                    byte peerSequence = packet[offset];
                                    byte[] body = new byte[packet.Length - offset - 2];
                                    Array.Copy(packet, offset + 1, body, 0, body.Length);
                                    if (!request && packet[7] != 0)
                                    {
                                        if (opcode == 0xF3 && pendingConfigs.ContainsKey(peerSequence))
                                        {
                                            Console.WriteLine("CONFIG_REJECTED [" + pendingConfigs[peerSequence].ToString("X2") + "]: " + packet[7]);
                                            pendingConfigs.Remove(peerSequence);
                                            continue;
                                        }
                                        throw new IOException("Device rejected opcode " + opcode.ToString("X2") + "/status " + packet[7]);
                                    }
                                    if (opcode == 0x50 && !request)
                                    {
                                        byte[] expected = BudsAuthentication.Response(challenge);
                                        if (body.Length != 17 || body[0] != 1) throw new IOException("Malformed authentication answer.");
                                        int mismatch = 0;
                                        for (int i = 0; i < 16; i++) mismatch |= expected[i] ^ body[i + 1];
                                        if (mismatch != 0) throw new IOException("Authentication answer did not match.");
                                        verified = true;
                                        await Write(writer, Frame(0xC4, 0x51, sequence++, new byte[] { 1, 0 }));
                                    }
                                    else if (opcode == 0x50 && request)
                                    {
                                        if (body.Length < 17) throw new IOException("Malformed peer challenge.");
                                        byte[] peerChallenge = new byte[16];
                                        Array.Copy(body, 1, peerChallenge, 0, 16);
                                        byte[] answer = new byte[17];
                                        answer[0] = 1;
                                        Array.Copy(BudsAuthentication.Response(peerChallenge), 0, answer, 1, 16);
                                        await Write(writer, Frame(0x04, 0x50, peerSequence, answer));
                                    }
                                    else if (opcode == 0x51 && request)
                                    {
                                        if (!verified) throw new IOException("Authentication confirmation arrived before peer verification.");
                                        await Write(writer, Frame(0x04, 0x51, peerSequence, new byte[] { 1 }));
                                        authenticated = true;
                                        Console.WriteLine("AUTH: verified");
                                        await Write(writer, Frame(0xC4, 0x02, sequence++, new byte[] { 255, 255, 255, 255 }));
                                        await Write(writer, Frame(0xC4, 0x09, sequence++, new byte[] { 255, 255, 255, 255 }));
                                        foreach (byte code in new byte[] { 0x02,0x03,0x04,0x06,0x07,0x0A,0x0B,0x0C,0x25,0x29,0x37,0x3B })
                                        {
                                            byte seq = sequence++;
                                            pendingConfigs.Add(seq, code);
                                            await Write(writer, Frame(0xC4, 0xF3, seq, new byte[] { 0, code }));
                                        }
                                    }
                                    else if (authenticated && opcode == 0x02)
                                    {
                                        deviceInfo = true;
                                        PrintEntries("DEVICE_INFO", body);
                                    }
                                    else if (authenticated && opcode == 0x09)
                                    {
                                        runInfo = true;
                                        PrintEntries("RUN_INFO", body);
                                    }
                                    else if (authenticated && opcode == 0xF3 && !request && pendingConfigs.ContainsKey(peerSequence))
                                    {
                                        byte code = pendingConfigs[peerSequence];
                                        pendingConfigs.Remove(peerSequence);
                                        PrintEntries("CONFIG_" + code.ToString("X2"), body);
                                        if (body.Length == 0) Console.WriteLine("CONFIG_EMPTY [" + code.ToString("X2") + "]");
                                    }
                                    if (request && (opcode == 0x0E || opcode == 0xF4))
                                        await Write(writer, Frame(0x04, opcode, peerSequence, new byte[0]));
                                }
                                if (authenticated && deviceInfo && runInfo && pendingConfigs.Count == 0)
                                {
                                    Console.WriteLine("RESULT: authenticated; device, runtime and configuration info received; no settings changed.");
                                    return;
                                }
                            }
                            throw new TimeoutException("Handshake or information query did not complete.");
                        }
                    }
                }
                throw new IOException("miwear RFCOMM service was not advertised.");
            }
            finally { foreach (var service in services.Services) service.Dispose(); }
        }
    }

    private static void PrintEntries(string prefix, byte[] payload)
    {
        for (int i = 0; i + 1 < payload.Length; )
        {
            int length = payload[i];
            if (length < 1 || i + 1 + length > payload.Length) break;
            byte index = payload[i + 1];
            byte[] value = new byte[length - 1];
            Array.Copy(payload, i + 2, value, 0, value.Length);
            Console.WriteLine(prefix + " [" + index.ToString("X2") + "]: " + BitConverter.ToString(value));
            i += length + 1;
        }
    }

    private static async Task Write(DataWriter writer, byte[] packet)
    {
        writer.WriteBytes(packet);
        await Bounded(writer.StoreAsync().ToTask());
    }

    private static byte[] Frame(byte kind, byte opcode, byte seq, byte[] payload)
    {
        bool request = (kind & 0x40) != 0;
        int length = payload.Length + (request ? 1 : 2);
        var bytes = new List<byte>(new byte[] { 0xFE, 0xDC, 0xBA, kind, opcode, (byte)(length >> 8), (byte)length });
        if (!request) bytes.Add(0);
        bytes.Add(seq);
        bytes.AddRange(payload);
        bytes.Add(0xEF);
        return bytes.ToArray();
    }

    private static byte[] Decode(List<byte> bytes)
    {
        while (bytes.Count >= 3)
        {
            if (bytes[0] != 0xFE || bytes[1] != 0xDC || bytes[2] != 0xBA) { bytes.RemoveAt(0); continue; }
            if (bytes.Count < 7) return null;
            int length = (bytes[5] << 8) | bytes[6];
            int minimum = (bytes[3] & 0x40) != 0 ? 1 : 2;
            if (length < minimum || length > 4096) { bytes.RemoveAt(0); continue; }
            int total = 8 + length;
            if (bytes.Count < total) return null;
            if (bytes[total - 1] != 0xEF) { bytes.RemoveAt(0); continue; }
            byte[] packet = bytes.GetRange(0, total).ToArray();
            bytes.RemoveRange(0, total);
            return packet;
        }
        return null;
    }
}
