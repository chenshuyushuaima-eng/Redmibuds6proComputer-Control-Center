// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BudsControl
{
    public static class Program
    {
        private static readonly object LogLock = new object();
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Contains("--check") || args.Contains("--verify") || args.Contains("--snapshot"))
                return Diagnostics(args);
            bool owner;
            using (var mutex = new Mutex(true,@"Local\MiBudsControl",out owner))
            {
                if (!owner) { MessageBox.Show("Mi Buds Control 已在运行，请从系统托盘打开。","Mi Buds Control"); return 0; }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var client = new BudsClient(); client.Log += Log;
                app.DispatcherUnhandledException += delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs error) {
                    Log("UNHANDLED " + error.Exception); error.Handled = true;
                    MessageBox.Show(error.Exception.Message,"Mi Buds Control",MessageBoxButton.OK,MessageBoxImage.Error);
                };
                var window = new MainWindow(client,Preferences.Load(),false);
                if (args.Contains("--tray")) window.Loaded += delegate { window.Hide(); };
                app.Run(window);
                client.Dispose();
                return 0;
            }
        }
        public static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(Preferences.Folder);
                    string path = Path.Combine(Preferences.Folder,"bluetooth.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 1048576) File.WriteAllText(path,"",Encoding.UTF8);
                    File.AppendAllText(path,DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine,Encoding.UTF8);
                }
            }
            catch { }
        }
        private static int Diagnostics(string[] args)
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostics");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder,args.Contains("--verify") ? "verification.txt" : "connection.txt");
            Action<string> record = line => File.AppendAllText(path,line + Environment.NewLine,Encoding.UTF8);
            File.WriteAllText(path,DateTimeOffset.Now.ToString("o") + Environment.NewLine,Encoding.UTF8);
            var client = new BudsClient(); client.Log += record;
            try
            {
                Task.Run(async delegate {
                    var devices = await BudsClient.Discover();
                    var selected = devices.FirstOrDefault(d => d.Name.Equals("REDMI Buds 6 Pro",StringComparison.OrdinalIgnoreCase)) ?? devices.FirstOrDefault();
                    if (selected == null) throw new IOException("没有已配对的耳机。");
                    await client.Connect(selected.Id);
                    var state = client.State;
                    record("CONNECTED " + state.Name + " FW=" + state.Firmware + " LEFT=" + state.Left + " RIGHT=" + state.Right + " MODE=" + state.NoiseMode);
                    if (!args.Contains("--verify")) return;
                    if (!state.NoiseMode.HasValue || state.NoiseMode > 2) throw new IOException("原始模式未知，跳过写入验证。");
                    Exception failure = null;
                    try
                    {
                        foreach (byte mode in new byte[] {0,1,2})
                        {
                            await client.SetNoise(mode);
                            record("VERIFIED noise=" + Protocol.NoiseName(mode) + " readback=" + client.State.NoiseMode);
                        }
                        // Preserve values while exercising the relevant encoding and readback paths.
                        foreach (byte code in new byte[] {0x25,0x29,0x3B,7})
                        {
                            int? value = state.First(code);
                            if (value.HasValue)
                            {
                                await client.SetValue(code,(byte)value.Value);
                                record("VERIFIED same-value setting=" + code.ToString("X2") + " value=" + value);
                            }
                        }
                        if (!state.WearingValue.HasValue) throw new IOException("佩戴检测原始状态未知，跳过写入验证。");
                        await client.SetWearing(state.WearingValue == 0);
                        record("VERIFIED wearing detection (original value)");
                        foreach (byte tap in new byte[] {4,1,2,3,5})
                        {
                            for (int ear = 0; ear < 2; ear++)
                            {
                                int? action = state.Gesture(tap,ear);
                                if (!action.HasValue) continue;
                                await client.SetGesture(tap,ear,(byte)action.Value);
                                record("VERIFIED same-value gesture tap=" + tap + " ear=" + ear + " action=" + action);
                            }
                        }
                        int[] gains = state.Gains();
                        if (gains != null)
                        {
                            await client.SetEqualizer(gains);
                            record("VERIFIED original EQ curve and custom preset");
                        }
                    }
                    catch (Exception error) { failure = error; }
                    // Restore after either success or failure (C# 5 cannot await inside finally).
                    {
                        Exception restoration = null;
                        try
                        {
                            if (state.First(7).HasValue && client.State.First(7) != state.First(7)) await client.SetValue(7,(byte)state.First(7).Value);
                            await client.SetNoise((byte)state.NoiseMode.Value);
                            record("RESTORED original noise=" + Protocol.NoiseName(state.NoiseMode) + " EQ=" + state.First(7));
                        }
                        catch (Exception error) { restoration = error; record("RESTORE FAILED " + error); }
                        if (restoration != null) throw new IOException("验证后恢复设置失败，请查看日志。",restoration);
                    }
                    if (failure != null) throw failure;
                    record("PASS: confirmed device acknowledgements and readbacks; original settings restored.");
                }).GetAwaiter().GetResult();
                if (args.Contains("--snapshot"))
                    SaveSnapshot(client,folder,record);
                return 0;
            }
            catch (Exception error)
            {
                record("FAILED " + error);
                if (args.Contains("--snapshot"))
                {
                    try { SaveSnapshot(client,folder,record); }
                    catch (Exception renderError) { record("RENDER FAILED " + renderError); }
                }
                return 1;
            }
            finally { client.Dispose(); }
        }
        private static void SaveSnapshot(BudsClient client,string folder,Action<string> record)
        {
            if (Application.Current == null) new Application();
            var window = new MainWindow(client,Preferences.Load(),true);
            var root = window.PreviewRoot;
            for (int i = 0; i < 4; i++)
            {
                window.PreviewPage(i);
                root.Measure(new Size(1140,795)); root.Arrange(new Rect(0,0,1140,795)); root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1140,795,96,96,PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string name = i == 0 ? "app-preview.png" : "app-preview-" + i + ".png";
                using (var stream = File.Create(Path.Combine(folder,name))) encoder.Save(stream);
                record("SNAPSHOT " + name + " (" + (client.Connected ? "actual connected device state" : "disconnected state; no sample values") + ")");
            }
        }
    }
}
