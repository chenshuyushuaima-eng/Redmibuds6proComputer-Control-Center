// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace BudsControl
{
    public sealed class DeviceChoice
    {
        public string Id, Name;
        public override string ToString() { return Name; }
    }
    internal sealed class Pending
    {
        public byte Opcode;
        public TaskCompletionSource<Packet> Completion = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class FitResult
    {
        public int Left, Right;
    }
    public sealed class BudsClient : IDisposable
    {
        private readonly SemaphoreSlim writeGate = new SemaphoreSlim(1,1);
        private readonly SemaphoreSlim commandGate = new SemaphoreSlim(1,1);
        private readonly object sync = new object();
        private readonly Dictionary<byte,Pending> pending = new Dictionary<byte,Pending>();
        private BluetoothDevice device;
        private StreamSocket socket;
        private DataWriter writer;
        private DataReader reader;
        private CancellationTokenSource lifetime;
        private Task receiver;
        private byte sequence;
        private bool verified;
        private TaskCompletionSource<bool> authComplete;
        private DeviceState state = new DeviceState();
        private TaskCompletionSource<bool> fitReady;
        private TaskCompletionSource<FitResult> fitResult;
        private bool fitAccept, fitHasReady;
        public bool Connected { get; private set; }
        public string DeviceId { get; private set; }
        public event Action Changed;
        public event Action<string> Disconnected;
        public event Action<string> Log;
        public DeviceState State { get { lock (sync) return state.Clone(); } }

        public static async Task<List<DeviceChoice>> Discover()
        {
            var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)).ToTask().ConfigureAwait(false);
            return devices.Where(d => d.Name.IndexOf("Buds",StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(d => new DeviceChoice { Id = d.Id, Name = d.Name }).ToList();
        }
        private void Trace(string message) { var handler = Log; if (handler != null) handler(message); }
        private void Notify() { var handler = Changed; if (handler != null) handler(); }

        public async Task Connect(string id)
        {
            await Disconnect().ConfigureAwait(false);
            lifetime = new CancellationTokenSource();
            sequence = 0; verified = false;
            authComplete = new TaskCompletionSource<bool>();
            lock (sync) state = new DeviceState();
            Exception connectionFailure = null;
            try
            {
                device = await BluetoothDevice.FromIdAsync(id).ToTask().ConfigureAwait(false);
                if (device == null) throw new IOException("找不到耳机，请先在 Windows 蓝牙设置中配对。");
                Trace("DEVICE " + device.Name + " status=" + device.ConnectionStatus + " address=" + device.BluetoothAddress.ToString("X12"));
                DeviceId = id;
                lock (sync) state.Name = device.Name;
                var services = await device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).ToTask().ConfigureAwait(false);
                try
                {
                    if (services.Error != BluetoothError.Success) throw new IOException("读取蓝牙服务失败：" + services.Error);
                    var service = services.Services.FirstOrDefault(s => s.ServiceId.Uuid == new Guid("0000fd2d-0000-1000-8000-00805f9b34fb"));
                    if (service == null) throw new IOException("没有找到耳机控制服务，请确认耳机已经连接电脑。");
                    Trace("SERVICE " + service.ConnectionServiceName);
                    socket = new StreamSocket();
                    try
                    {
                        await socket.ConnectAsync(service.ConnectionHostName,service.ConnectionServiceName,
                            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).ToTask().ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        throw new IOException("耳机控制连接暂时不可用。请保持耳机连接电脑、关闭手机上的小米耳机应用后重试。",error);
                    }
                }
                finally { foreach (var service in services.Services) service.Dispose(); }
                writer = new DataWriter(socket.OutputStream);
                reader = new DataReader(socket.InputStream);
                reader.InputStreamOptions = InputStreamOptions.Partial;
                receiver = Receive(lifetime.Token);
                byte[] challenge = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(challenge);
                var response = await Request(0x50,new byte[] { 1 }.Concat(challenge).ToArray()).ConfigureAwait(false);
                byte[] expected = BudsAuthentication.Response(challenge);
                if (response.Payload.Length != 17 || response.Payload[0] != 1) throw new IOException("耳机认证数据不完整。");
                int mismatch = 0;
                for (int i = 0; i < 16; i++) mismatch |= expected[i] ^ response.Payload[i + 1];
                if (mismatch != 0) throw new IOException("耳机认证校验失败。");
                verified = true;
                await Request(0x51,new byte[] { 1,0 }).ConfigureAwait(false);
                if (await Task.WhenAny(authComplete.Task,Task.Delay(8000)).ConfigureAwait(false) != authComplete.Task)
                    throw new TimeoutException("耳机认证超时，请关闭手机上的小米耳机应用后重试。");
                await authComplete.Task.ConfigureAwait(false);
                Connected = true;
                await Refresh().ConfigureAwait(false);
                Notify();
            }
            catch (Exception error) { connectionFailure = error; }
            if (connectionFailure != null)
            {
                await Disconnect().ConfigureAwait(false);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(connectionFailure).Throw();
            }
        }

        private async Task Receive(CancellationToken token)
        {
            var decoder = new Decoder();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    uint count = await reader.LoadAsync(4096).ToTask(token,0).ConfigureAwait(false);
                    if (count == 0) throw new IOException("耳机已断开。");
                    byte[] bytes = new byte[count];
                    reader.ReadBytes(bytes);
                    Trace("RX " + BitConverter.ToString(bytes));
                    foreach (var packet in decoder.Feed(bytes))
                    {
                        if (packet.IsRequest && packet.Opcode == 0x50)
                        {
                            if (packet.Payload.Length != 17 || packet.Payload[0] != 1) throw new IOException("耳机认证挑战无效。");
                            byte[] answer = BudsAuthentication.Response(packet.Payload.Skip(1).ToArray());
                            await Send(new Packet { Kind = 4,Opcode = 0x50,Sequence = packet.Sequence,
                                Payload = new byte[] { 1 }.Concat(answer).ToArray() }).ConfigureAwait(false);
                        }
                        else if (packet.IsRequest && packet.Opcode == 0x51)
                        {
                            if (!verified || packet.Payload.Length < 2 || packet.Payload[0] != 1 || packet.Payload[1] != 0)
                                throw new IOException("耳机没有确认认证成功。");
                            await Send(new Packet { Kind = 4,Opcode = 0x51,Sequence = packet.Sequence,Payload = new byte[] { 1 } }).ConfigureAwait(false);
                            authComplete.TrySetResult(true);
                        }
                        else
                        {
                            if (packet.Status == 0) Apply(packet);
                            if (packet.HasStatus && !packet.IsRequest)
                            {
                                Pending match;
                                lock (sync)
                                {
                                    if (pending.TryGetValue(packet.Sequence,out match) && match.Opcode == packet.Opcode)
                                        match.Completion.TrySetResult(packet);
                                }
                            }
                            if (packet.IsRequest && (packet.Opcode == 0x0E || packet.Opcode == 0xF4))
                                await Send(new Packet { Kind = 4,Opcode = packet.Opcode,Sequence = packet.Sequence,Payload = new byte[0] }).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception error)
            {
                if (token.IsCancellationRequested) return;
                Connected = false;
                authComplete.TrySetException(error);
                lock (sync) foreach (var item in pending.Values) item.Completion.TrySetException(error);
                lock (sync) if (fitResult != null) fitResult.TrySetException(error);
                Trace("DISCONNECTED " + error.Message);
                var handler = Disconnected;
                if (handler != null) handler(error.Message);
                Notify();
            }
        }

        private void Apply(Packet packet)
        {
            if (packet.Opcode != 2 && packet.Opcode != 9 && packet.Opcode != 0x0E && packet.Opcode != 0xF3 && packet.Opcode != 0xF4) return;
            lock (sync)
            {
                foreach (var entry in Protocol.Entries(packet.Payload))
                {
                    byte[] data = entry.Data;
                    if (packet.Opcode == 2)
                    {
                        if (entry.Index == 0) state.Name = Encoding.UTF8.GetString(data);
                        if (entry.Index == 1 && data.Length >= 2) state.Firmware = Protocol.Version(data[0],data[1]);
                        if (entry.Index == 3 && data.Length >= 4)
                        {
                            state.VendorId = (data[0] << 8) | data[1];
                            state.ProductId = (data[2] << 8) | data[3];
                        }
                        if (entry.Index == 7) SetBattery(data);
                    }
                    else if (packet.Opcode == 9)
                    {
                        if (entry.Index == 9 && data.Length > 0) state.NoiseMode = data[0];
                        if (entry.Index == Protocol.WearingRunInfoIndex && data.Length > 0) state.WearDetection = data[0];
                    }
                    else if (packet.Opcode == 0x0E)
                    {
                        if (entry.Index == 0) SetBattery(data);
                        if (entry.Index == 4 && data.Length > 0) state.NoiseMode = data[0];
                    }
                    else if (entry.Index == 0 && data.Length > 0)
                    {
                        byte code = data[0];
                        byte[] value = data.Skip(1).ToArray();
                        state.Config[code] = value;
                        if (code == Protocol.WearingConfigCode && value.Length >= 2 && value[0] == 0)
                        {
                            state.ConfigWearDetection = value[1];
                            // A push is a new setting; config reads must not replace fresh run-info.
                            if (packet.Opcode == 0xF4) state.WearDetection = value[1];
                        }
                        // The official app controls config 0x05 but receives fit results in 0x06.
                        if (code == Protocol.FitResultCode && packet.Opcode == 0xF4 && fitAccept && value.Length >= 2)
                        {
                            int left = value[0], right = value[1];
                            Trace("FIT left=" + left + " right=" + right);
                            if (left == 9 || right == 9)
                                fitResult.TrySetException(new IOException("请佩戴好双耳后重新检查贴合度。"));
                            else if (left == 10 || right == 10)
                                fitResult.TrySetException(new IOException("通话期间无法检查贴合度，请结束通话后重试。"));
                            else if (left == 3 || right == 3)
                            {
                                fitHasReady = true; fitReady.TrySetResult(true);
                            }
                            else if (fitHasReady && left >= 1 && left <= 2 && right >= 1 && right <= 2)
                                fitResult.TrySetResult(new FitResult { Left = left, Right = right });
                        }
                        if (code == 0x0B && value.Length >= 2)
                        {
                            state.Strength[value[0]] = value[1];
                            if (packet.Opcode == 0xF4) state.NoiseMode = value[0];
                        }
                    }
                }
            }
            Notify();
        }
        private void SetBattery(byte[] data)
        {
            if (data.Length >= 1) { state.Left = Protocol.Battery(data[0]); state.LeftCharging = data[0] != 255 && (data[0] & 128) != 0; }
            if (data.Length >= 2) { state.Right = Protocol.Battery(data[1]); state.RightCharging = data[1] != 255 && (data[1] & 128) != 0; }
            if (data.Length >= 3) { state.Case = Protocol.Battery(data[2]); state.CaseCharging = data[2] != 255 && (data[2] & 128) != 0; }
        }
        private async Task Send(Packet packet)
        {
            await writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (writer == null) throw new IOException("耳机未连接。");
                byte[] bytes = packet.Encode();
                Trace("TX " + BitConverter.ToString(bytes));
                writer.WriteBytes(bytes);
                await writer.StoreAsync().ToTask().ConfigureAwait(false);
            }
            finally { writeGate.Release(); }
        }
        private async Task<Packet> Request(byte opcode,byte[] payload)
        {
            var item = new Pending { Opcode = opcode };
            byte seq;
            lock (sync)
            {
                int remaining = 256;
                while (pending.ContainsKey(sequence) && remaining-- > 0) unchecked { sequence++; }
                if (remaining <= 0) throw new IOException("耳机请求过多，请稍后重试。");
                seq = sequence; unchecked { sequence++; }
                pending.Add(seq,item);
            }
            try
            {
                await Send(new Packet { Kind = 0xC4,Opcode = opcode,Sequence = seq,Payload = payload }).ConfigureAwait(false);
                if (await Task.WhenAny(item.Completion.Task,Task.Delay(6000)).ConfigureAwait(false) != item.Completion.Task)
                    throw new TimeoutException("耳机没有响应，请重连后重试。");
                var answer = await item.Completion.Task.ConfigureAwait(false);
                if (answer.Status != 0)
                {
                    if (opcode == 0xF2 && payload.Length >= 4 && payload[1] == 0 && payload[2] == Protocol.FitControlCode && payload[3] == 1 && answer.Status == 9)
                        throw new IOException("请佩戴好双耳后重新检查贴合度。");
                    throw new IOException("耳机拒绝了这项操作（" + answer.Status + "）。");
                }
                return answer;
            }
            finally { lock (sync) pending.Remove(seq); }
        }
        public async Task Refresh()
        {
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                await Request(2,new byte[] { 255,255,255,255 }).ConfigureAwait(false);
                await Request(9,new byte[] { 255,255,255,255 }).ConfigureAwait(false);
                byte[] codes = State.HasBuds6ProSpatialProfile ? Protocol.ConfigCodes.Concat(Protocol.SpatialConfigCodes).ToArray() : Protocol.ConfigCodes;
                foreach (byte code in codes)
                {
                    RequireConnected();
                    try { await QueryConfig(code).ConfigureAwait(false); }
                    catch (IOException error)
                    {
                        if (!Connected) throw;
                        Trace("CONFIG " + code.ToString("X2") + ": " + error.Message);
                    }
                    catch (TimeoutException error)
                    {
                        if (!Connected) throw;
                        // Optional configuration reads must not tear down an authenticated connection.
                        Trace("CONFIG " + code.ToString("X2") + " skipped after timeout: " + error.Message);
                    }
                }
                RequireConnected();
            }
            finally { commandGate.Release(); }
        }
        private void RequireConnected() { if (!Connected) throw new IOException("请先连接耳机。"); }
        private async Task QueryConfig(byte code) { await Request(0xF3,new byte[] { 0,code }).ConfigureAwait(false); }
        private async Task<byte[]> ReadConfigValue(byte code)
        {
            Packet response = await Request(0xF3,new byte[] { 0,code }).ConfigureAwait(false);
            var entry = Protocol.Entries(response.Payload).FirstOrDefault(e => e.Index == 0 && e.Data.Length > 0 && e.Data[0] == code);
            if (entry == null || entry.Data.Length < 2) throw new IOException("耳机没有返回配置 " + code.ToString("X2") + " 的有效状态，请刷新后重试。");
            return entry.Data.Skip(1).ToArray();
        }
        public Task SetSpatialAudio(bool enabled) { return SetSpatialFlag(1,enabled); }
        public Task SetHeadTracking(bool enabled) { return SetSpatialFlag(8,enabled); }
        private async Task SetSpatialFlag(byte mask,bool enabled)
        {
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                if (!State.HasBuds6ProSpatialProfile) throw new IOException("当前耳机型号尚未适配空间音频。");
                byte[] current = await ReadConfigValue(0x1E).ConfigureAwait(false);
                if (mask == 8 && (current[0] & 1) == 0) throw new IOException("请先开启空间音频，再设置头部追踪。");
                // Official write 0x1D: surround bit 4, tracking bit 3, preference 1 in bits 1-2, enable bit 0.
                // Read 0x1E before each change to preserve the other switches.
                byte value = (byte)((current[0] & 0x19) | 2);
                value = enabled ? (byte)(value | mask) : (byte)(value & ~mask);
                await Request(0xF2,Protocol.Set(0x1D,value)).ConfigureAwait(false);
                byte[] actual = await ReadConfigValue(0x1E).ConfigureAwait(false);
                if (((actual[0] & mask) != 0) != enabled)
                    throw new IOException("空间音频设置未通过耳机回读确认，请刷新后重试。");
                // Scene state can change with the master switch; its failure must not hide a confirmed master setting.
                try { await ReadConfigValue(0x36).ConfigureAwait(false); }
                catch (IOException error) { Trace("SPATIAL scene refresh: " + error.Message); }
            }
            finally { commandGate.Release(); }
        }
        public async Task SetSpatialScene(byte scene)
        {
            if (scene < 1 || scene > 5) throw new ArgumentOutOfRangeException("scene");
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                if (!State.HasBuds6ProSpatialProfile) throw new IOException("当前耳机型号尚未适配场景渲染。");
                byte[] spatial = await ReadConfigValue(0x1E).ConfigureAwait(false);
                if ((spatial[0] & 1) == 0) throw new IOException("请先开启空间音频，再选择场景。");
                await Request(0xF2,Protocol.Set(0x36,1,scene)).ConfigureAwait(false);
                byte[] actual = await ReadConfigValue(0x36).ConfigureAwait(false);
                if (actual.Length < 2 || actual[0] != 1 || actual[1] != scene)
                    throw new IOException("场景设置未通过耳机回读确认，请刷新后重试。");
                await ReadConfigValue(0x1E).ConfigureAwait(false);
            }
            finally { commandGate.Release(); }
        }
        public async Task SetNoise(byte mode)
        {
            if (mode > 2) throw new ArgumentOutOfRangeException("mode");
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                await Request(8,new byte[] { 2,4,mode }).ConfigureAwait(false);
                await Request(9,new byte[] { 255,255,255,255 }).ConfigureAwait(false);
                await QueryConfig(0x0B).ConfigureAwait(false);
                if (State.NoiseMode != mode) throw new IOException("耳机返回的模式与选择不一致，请刷新后重试。");
            }
            finally { commandGate.Release(); }
        }
        public async Task SetValue(byte code,byte value)
        {
            if (!new byte[] {3,4,7,0x25,0x29,0x3B}.Contains(code)) throw new ArgumentOutOfRangeException("code");
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                await Request(0xF2,Protocol.Set(code,value)).ConfigureAwait(false);
                await QueryConfig(code).ConfigureAwait(false);
                if (State.First(code) != value) throw new IOException("设置未通过耳机回读确认，请刷新后重试。");
            }
            finally { commandGate.Release(); }
        }
        public async Task SetWearing(bool enabled)
        {
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                byte value = enabled ? (byte)0 : (byte)1;
                await Request(8,new byte[] { 2,6,value }).ConfigureAwait(false);
                // Setter type 6 returns autoPlay at run-info index 0x0A.
                // Wearing config 0x7E and fit results 0x06 are separate.
                Packet response = await Request(9,new byte[] {255,255,255,255}).ConfigureAwait(false);
                var entry = Protocol.Entries(response.Payload).FirstOrDefault(e => e.Index == Protocol.WearingRunInfoIndex && e.Data.Length > 0);
                if (entry == null || entry.Data[0] != value)
                    throw new IOException("佩戴检测设置未通过耳机实时状态确认，请刷新后重试。");
            }
            finally { commandGate.Release(); }
        }
        public async Task<FitResult> CheckFit(Action playAudio,Action stopAudio,CancellationToken token)
        {
            await commandGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Exception failure = null;
                FitResult result = null;
                bool startAttempted = false;
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (token.Register(delegate { cancelled.TrySetResult(true); }))
                {
                    try
                    {
                        RequireConnected();
                        if (!State.HasBuds6ProStrengthProfile) throw new IOException("当前耳机型号尚未适配贴合度检查。");
                        // Stop any previous phone/desktop measurement before accepting new pushes.
                        await Request(0xF2,Protocol.Set(Protocol.FitControlCode,0)).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        lock (sync)
                        {
                            fitReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            fitResult = new TaskCompletionSource<FitResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                            fitHasReady = false; fitAccept = true;
                        }
                        Task deadline = Task.Delay(10000);
                        startAttempted = true;
                        await Request(0xF2,Protocol.Set(Protocol.FitControlCode,1)).ConfigureAwait(false);
                        Task first = await Task.WhenAny(fitReady.Task,fitResult.Task,deadline,cancelled.Task).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (first == deadline) throw new TimeoutException("贴合度检查超时，请确认双耳已佩戴、没有通话，然后重试。");
                        if (!fitResult.Task.IsCompleted)
                        {
                            // Official app starts the signal on readiness code 3, not the command ACK.
                            playAudio();
                            Task finished = await Task.WhenAny(fitResult.Task,deadline,cancelled.Task).ConfigureAwait(false);
                            token.ThrowIfCancellationRequested();
                            if (finished == deadline) throw new TimeoutException("贴合度检查超时，请调整佩戴位置并确认检测音频输出到耳机后重试。");
                        }
                        result = await fitResult.Task.ConfigureAwait(false);
                    }
                    catch (Exception error) { failure = error; }
                    // C# 5 cannot await in finally. Always stop playback before stopping the device.
                    stopAudio();
                    lock (sync) fitAccept = false;
                    if (startAttempted && Connected)
                    {
                        try { await Request(0xF2,Protocol.Set(Protocol.FitControlCode,0)).ConfigureAwait(false); }
                        catch (Exception error)
                        {
                            Trace("FIT stop failed: " + error.Message);
                            if (failure == null) failure = new IOException("检测已结束，但无法确认耳机停止检测：" + error.Message,error);
                        }
                    }
                    lock (sync) { fitReady = null; fitResult = null; }
                    if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                    return result;
                }
            }
            finally { commandGate.Release(); }
        }
        public async Task SetStrength(byte mode,byte value)
        {
            if ((mode != 1 && mode != 2) || (mode == 1 && value > 19) || (mode == 2 && value > 2))
                throw new ArgumentOutOfRangeException("value");
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                if (State.NoiseMode != mode) throw new IOException("请先切换到对应的降噪或通透模式。");
                if (!State.HasBuds6ProStrengthProfile) throw new IOException("当前耳机型号尚未适配强度调节。");
                if (mode == 1 && State.First(0x25) != 0) throw new IOException("请先关闭自适应降噪，再手动调节强度。");
                await Request(0xF2,Protocol.Set(0x0B,mode,value)).ConfigureAwait(false);
                await QueryConfig(0x0B).ConfigureAwait(false);
                byte[] actual;
                if (!State.Config.TryGetValue(0x0B,out actual) || actual.Length < 2 || actual[0] != mode || actual[1] != value)
                    throw new IOException("强度设置未通过回读确认。");
            }
            finally { commandGate.Release(); }
        }
        public async Task SetGesture(byte tap,int ear,byte action)
        {
            if (ear < 0 || ear > 1 || tap < 1 || tap > 5) throw new ArgumentOutOfRangeException("tap");
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                byte[] payload = {5,0,2,tap,255,255};
                payload[4 + ear] = action;
                await Request(0xF2,payload).ConfigureAwait(false);
                await QueryConfig(2).ConfigureAwait(false);
                if (State.Gesture(tap,ear) != action) throw new IOException("手势设置未通过回读确认。");
            }
            finally { commandGate.Release(); }
        }
        public async Task SetCycle(int ear,byte value)
        {
            if (ear < 0 || ear > 1 || !new byte[] {3,5,6,7}.Contains(value)) throw new ArgumentOutOfRangeException("value");
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                byte[] payload = {4,0,0x0A,255,255};
                payload[3 + ear] = value;
                await Request(0xF2,payload).ConfigureAwait(false);
                await QueryConfig(0x0A).ConfigureAwait(false);
                byte[] actual;
                if (!State.Config.TryGetValue(0x0A,out actual) || actual.Length < 2 || actual[ear] != value)
                    throw new IOException("长按循环设置未通过回读确认。");
            }
            finally { commandGate.Release(); }
        }
        public async Task SetEqualizer(int[] gains)
        {
            if (gains.Length != 10 || gains.Any(g => g < -6 || g > 6)) throw new ArgumentOutOfRangeException("gains");
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                int[] frequencies = State.Frequencies();
                if (frequencies == null) throw new IOException("耳机尚未返回有效的均衡器曲线。");
                var value = new List<byte>(new byte[] {5,1,1,10});
                for (int i = 0; i < 10; i++)
                {
                    value.Add((byte)(frequencies[i] >> 8)); value.Add((byte)frequencies[i]);
                    value.Add(gains[i] < 0 ? (byte)(128 | -gains[i]) : (byte)gains[i]);
                }
                await Request(0xF2,Protocol.Set(0x37,value.ToArray())).ConfigureAwait(false);
                await QueryConfig(0x37).ConfigureAwait(false);
                int[] actual = State.Gains();
                if (actual == null || !actual.SequenceEqual(gains)) throw new IOException("均衡器曲线未通过回读确认。");
                await Request(0xF2,Protocol.Set(7,10)).ConfigureAwait(false);
                await QueryConfig(7).ConfigureAwait(false);
                if (State.First(7) != 10) throw new IOException("曲线已保存，但耳机没有切换到自定义音效。");
            }
            finally { commandGate.Release(); }
        }
        public async Task Find(byte target,bool start)
        {
            if (target > 2) throw new ArgumentOutOfRangeException("target");
            await commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RequireConnected();
                await Request(0xF2,Protocol.Set(9,0,0)).ConfigureAwait(false);
                if (start) await Request(0xF2,Protocol.Set(9,1,target)).ConfigureAwait(false);
            }
            finally { commandGate.Release(); }
        }
        public async Task Disconnect()
        {
            Connected = false;
            if (lifetime != null) lifetime.Cancel();
            if (socket != null) { try { socket.Dispose(); } catch { } }
            if (receiver != null) { try { await receiver.ConfigureAwait(false); } catch { } receiver = null; }
            if (reader != null) { try { reader.Dispose(); } catch { } reader = null; }
            if (writer != null) { try { writer.Dispose(); } catch { } writer = null; }
            if (device != null) { device.Dispose(); device = null; }
            socket = null;
            if (lifetime != null) { lifetime.Dispose(); lifetime = null; }
            lock (sync)
            {
                fitAccept = false;
                if (fitResult != null) fitResult.TrySetException(new IOException("连接已关闭，贴合度检查已停止。"));
                foreach (var item in pending.Values) item.Completion.TrySetException(new IOException("连接已关闭。"));
                pending.Clear();
            }
            Notify();
        }
        public void Dispose() { Disconnect().GetAwaiter().GetResult(); }
    }
}
