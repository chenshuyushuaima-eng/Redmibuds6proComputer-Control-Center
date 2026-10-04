// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BudsControl
{
    public sealed class Packet
    {
        public byte Kind, Opcode, Sequence, Status;
        public byte[] Payload;
        public bool IsRequest { get { return (Kind & 0x40) != 0; } }
        public bool HasStatus { get { return (Kind & 0x80) == 0; } }
        public byte[] Encode()
        {
            int length = Payload.Length + (HasStatus ? 2 : 1);
            var bytes = new List<byte>(new byte[] { 0xFE,0xDC,0xBA,Kind,Opcode,(byte)(length >> 8),(byte)length });
            if (HasStatus) bytes.Add(Status);
            bytes.Add(Sequence);
            bytes.AddRange(Payload);
            bytes.Add(0xEF);
            return bytes.ToArray();
        }
    }

    public sealed class Decoder
    {
        private readonly List<byte> buffer = new List<byte>();
        public IEnumerable<Packet> Feed(byte[] bytes)
        {
            buffer.AddRange(bytes);
            while (buffer.Count >= 3)
            {
                if (buffer[0] != 0xFE || buffer[1] != 0xDC || buffer[2] != 0xBA) { buffer.RemoveAt(0); continue; }
                if (buffer.Count < 7) yield break;
                bool hasStatus = (buffer[3] & 0x80) == 0;
                int length = (buffer[5] << 8) | buffer[6];
                if (length < (hasStatus ? 2 : 1) || length > 4096) { buffer.RemoveAt(0); continue; }
                int total = 8 + length;
                if (buffer.Count < total) yield break;
                if (buffer[total - 1] != 0xEF) { buffer.RemoveAt(0); continue; }
                int start = hasStatus ? 8 : 7;
                var packet = new Packet { Kind = buffer[3], Opcode = buffer[4], Status = hasStatus ? buffer[7] : (byte)0,
                    Sequence = buffer[start], Payload = buffer.GetRange(start + 1,total - start - 2).ToArray() };
                buffer.RemoveRange(0,total);
                yield return packet;
            }
        }
    }

    public sealed class Entry { public byte Index; public byte[] Data; }
    public static class Protocol
    {
        public const byte WearingRunInfoIndex = 0x0A, WearingConfigCode = 0x7E;
        public const byte FitControlCode = 0x05, FitResultCode = 0x06;
        // Firmware 1.1.9.9 does not reply to a direct 0x7E query. Read wearing via run-info 0x0A.
        public static readonly byte[] ConfigCodes = { 0x02,0x03,0x04,FitResultCode,0x07,0x0A,0x0B,0x0C,0x25,0x29,0x37,0x3B };
        public static readonly byte[] SpatialConfigCodes = { 0x1E,0x36 };
        public static IEnumerable<Entry> Entries(byte[] payload)
        {
            int i = 0;
            while (i < payload.Length)
            {
                if (i + 1 >= payload.Length) throw new InvalidDataException("耳机返回的数据不完整。");
                int length = payload[i];
                if (length < 1 || i + 1 + length > payload.Length) throw new InvalidDataException("耳机返回的数据长度无效。");
                yield return new Entry { Index = payload[i + 1], Data = payload.Skip(i + 2).Take(length - 1).ToArray() };
                i += length + 1;
            }
        }
        public static byte[] Set(byte code, params byte[] value)
        {
            if (value.Length > 253) throw new ArgumentOutOfRangeException("value");
            return new byte[] { (byte)(value.Length + 2),0,code }.Concat(value).ToArray();
        }
        public static int? Battery(byte value) { return value == 255 || (value & 127) > 100 ? (int?)null : value & 127; }
        public static string Version(byte a, byte b) { return string.Format("{0}.{1}.{2}.{3}",a >> 4,a & 15,b >> 4,b & 15); }
        public static string NoiseName(int? mode) { return mode == 0 ? "关闭" : mode == 1 ? "降噪" : mode == 2 ? "通透" : "未知"; }
    }

    public sealed class DeviceState
    {
        public string Name = "REDMI Buds 6 Pro", Firmware = "—";
        public int? Left, Right, Case, NoiseMode;
        // Run-info autoPlay and config 0x7E/subcommand 00 share inverted 0=enabled semantics.
        public int? WearDetection, ConfigWearDetection;
        public int? WearingValue { get { return WearDetection ?? ConfigWearDetection; } }
        public int? VendorId, ProductId;
        public bool HasBuds6ProStrengthProfile { get { return VendorId == 0x2717 && ProductId == 0x509D; } }
        public bool HasBuds6ProSpatialProfile { get { return VendorId == 0x2717 && ProductId == 0x509D; } }
        public bool LeftCharging, RightCharging, CaseCharging;
        public Dictionary<byte,byte[]> Config = new Dictionary<byte,byte[]>();
        public Dictionary<byte,int> Strength = new Dictionary<byte,int>();
        public DeviceState Clone()
        {
            var copy = (DeviceState)MemberwiseClone();
            copy.Config = Config.ToDictionary(p => p.Key,p => (byte[])p.Value.Clone());
            copy.Strength = new Dictionary<byte,int>(Strength);
            return copy;
        }
        public int? First(byte code)
        {
            byte[] data;
            return Config.TryGetValue(code,out data) && data.Length > 0 ? (int?)data[0] : null;
        }
        public int? SpatialFlag(int mask)
        {
            int? value = First(0x1E);
            return value.HasValue ? (int?)((value.Value & mask) != 0 ? 1 : 0) : null;
        }
        public int? SpatialScene()
        {
            byte[] data;
            return Config.TryGetValue(0x36,out data) && data.Length >= 2 && data[1] >= 1 && data[1] <= 5 ? (int?)data[1] : null;
        }
        public int? Gesture(byte tap,int ear)
        {
            byte[] data;
            if (!Config.TryGetValue(2,out data)) return null;
            for (int i = 0; i + 2 < data.Length; i += 3) if (data[i] == tap) return data[i + 1 + ear];
            return null;
        }
        public int[] Frequencies()
        {
            byte[] data;
            if (!Config.TryGetValue(0x37,out data) || data.Length < 37 || data[6] != 10) return null;
            int[] result = new int[10];
            for (int i = 0; i < 10; i++) result[i] = (data[7 + i * 3] << 8) | data[8 + i * 3];
            return result.All(f => f > 0 && f <= 24000) ? result : null;
        }
        public int[] Gains()
        {
            if (Frequencies() == null) return null;
            byte[] data = Config[0x37];
            int[] result = new int[10];
            for (int i = 0; i < 10; i++)
            {
                byte value = data[9 + i * 3];
                int gain = value & 127;
                if (gain > 6) return null;
                result[i] = (value & 128) == 0 ? gain : -gain;
            }
            return result;
        }
    }
}
