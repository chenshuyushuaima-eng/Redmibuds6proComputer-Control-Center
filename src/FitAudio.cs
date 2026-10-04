// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace BudsControl
{
    public sealed class FitAudioOutput
    {
        public uint Id;
        public string Name;
        public override string ToString() { return Name; }
    }

    // Explicitly target the earbuds; never send the measurement signal to the default speakers.
    internal sealed class FitAudio : IDisposable
    {
        [StructLayout(LayoutKind.Sequential,CharSet = CharSet.Unicode)]
        private struct Caps
        {
            public ushort Manufacturer, Product;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst = 32)] public string Name;
            public uint Formats;
            public ushort Channels, Reserved;
            public uint Support;
        }
        [StructLayout(LayoutKind.Sequential,Pack = 2)]
        private struct Format
        {
            public ushort Tag, Channels;
            public uint Rate, BytesPerSecond;
            public ushort Alignment, Bits, Extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Header
        {
            public IntPtr Data;
            public uint Length, Recorded;
            public IntPtr User;
            public uint Flags, Loops;
            public IntPtr Next, Reserved;
        }
        [DllImport("winmm.dll")] private static extern uint waveOutGetNumDevs();
        [DllImport("winmm.dll",CharSet = CharSet.Unicode)] private static extern uint waveOutGetDevCapsW(UIntPtr id,out Caps caps,uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr handle,uint id,ref Format format,IntPtr callback,IntPtr instance,uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr handle);
        private IntPtr handle, header, buffer;
        private bool prepared;
        private readonly byte[] pcm;
        private readonly Format format;
        private readonly FitAudioOutput output;
        private static uint HeaderSize { get { return (uint)Marshal.SizeOf(typeof(Header)); } }
        public static List<FitAudioOutput> Outputs()
        {
            var result = new List<FitAudioOutput>();
            for (uint i = 0; i < waveOutGetNumDevs(); i++)
            {
                Caps caps;
                if (waveOutGetDevCapsW(new UIntPtr(i),out caps,(uint)Marshal.SizeOf(typeof(Caps))) != 0) continue;
                string name = caps.Name ?? "";
                if (name.IndexOf("Buds 6 Pro",StringComparison.OrdinalIgnoreCase) < 0 || caps.Channels < 2 ||
                    name.IndexOf("Hands",StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("免提",StringComparison.OrdinalIgnoreCase) >= 0) continue;
                result.Add(new FitAudioOutput { Id = i, Name = name });
            }
            return result;
        }
        public FitAudio(FitAudioOutput output)
        {
            this.output = output;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fitness_detect.wav");
            if (!File.Exists(path)) throw new IOException("缺少贴合度检测音频，请按使用说明提取 fitness_detect.wav，并放在程序同目录。");
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != 0x46464952) throw new IOException("检测音频不是有效的 WAV 文件。");
                reader.ReadUInt32();
                if (reader.ReadUInt32() != 0x45564157) throw new IOException("检测音频格式错误。");
                byte[] data = null;
                ushort tag = 0, channels = 0, bits = 0;
                uint rate = 0;
                while (stream.Position + 8 <= stream.Length)
                {
                    uint chunk = reader.ReadUInt32(), size = reader.ReadUInt32();
                    long end = stream.Position + size;
                    if (end > stream.Length) throw new IOException("检测音频数据不完整。");
                    if (chunk == 0x20746D66 && size >= 16)
                    {
                        tag = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadUInt32();
                        reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
                    }
                    else if (chunk == 0x61746164) data = reader.ReadBytes(checked((int)size));
                    stream.Position = end + (size & 1);
                }
                if (tag != 1 || channels != 2 || rate != 44100 || bits != 24 || data == null || data.Length % 6 != 0)
                    throw new IOException("检测音频与适配的官方音频格式不一致。");
                // Convert signed 24-bit PCM to 16-bit without changing rate, channel order or gain.
                pcm = new byte[data.Length / 3 * 2];
                for (int i = 0,j = 0; i < data.Length; i += 3,j += 2) { pcm[j] = data[i + 1]; pcm[j + 1] = data[i + 2]; }
                format = new Format { Tag = 1,Channels = 2,Rate = 44100,BytesPerSecond = 176400,Alignment = 4,Bits = 16,Extra = 0 };
            }
        }
        public void Prepare()
        {
            Caps caps;
            if (waveOutGetDevCapsW(new UIntPtr(output.Id),out caps,(uint)Marshal.SizeOf(typeof(Caps))) != 0 || caps.Name != output.Name)
                throw new IOException("耳机音频输出已变化，请刷新音频输出后重试。");
            Format value = format;
            Check(waveOutOpen(out handle,output.Id,ref value,IntPtr.Zero,IntPtr.Zero,0),"打开耳机音频输出");
            buffer = Marshal.AllocHGlobal(pcm.Length); Marshal.Copy(pcm,0,buffer,pcm.Length);
            header = Marshal.AllocHGlobal((int)HeaderSize);
            Marshal.StructureToPtr(new Header { Data = buffer,Length = (uint)pcm.Length,Flags = 12,Loops = uint.MaxValue },header,false);
            Check(waveOutPrepareHeader(handle,header,HeaderSize),"准备检测音频"); prepared = true;
        }
        public void Play()
        {
            if (handle == IntPtr.Zero || !prepared) throw new IOException("检测音频尚未准备好。");
            Check(waveOutWrite(handle,header,HeaderSize),"播放检测音频");
        }
        private static void Check(uint code,string operation)
        {
            if (code != 0) throw new IOException(operation + "失败（音频错误 " + code + "）。请确认耳机音频连接可用且没有通话。");
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                uint reset = waveOutReset(handle);
                uint unprepare = prepared ? waveOutUnprepareHeader(handle,header,HeaderSize) : 0;
                // Preserve native buffers if the driver has not released them.
                if (reset != 0 || unprepare != 0)
                {
                    Program.Log("FIT audio cleanup error reset=" + reset + " unprepare=" + unprepare);
                    return;
                }
                waveOutClose(handle); handle = IntPtr.Zero; prepared = false;
            }
            if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
            if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
        }
    }
}
