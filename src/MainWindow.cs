// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace BudsControl
{
    public sealed class Option
    {
        public int Code; public string Label;
        public Option(int code,string label) { Code = code; Label = label; }
        public override string ToString() { return Label; }
    }
    internal sealed class ChoiceBinding
    {
        public ComboBox Control;
        public Func<DeviceState,int?> Value;
        public Func<DeviceState,bool> Enabled;
        public bool RequiresValue = true;
    }
    internal sealed class ToggleBinding
    {
        public CheckBox Control;
        public Func<DeviceState,int?> Value;
        public bool Inverted;
        public Func<DeviceState,bool> Enabled;
    }
    internal sealed class StrengthBinding
    {
        public byte Mode;
        public Slider Control;
        public TextBlock Label;
        public StackPanel Panel;
        public DispatcherTimer CommitTimer;
        public bool Editing, Dragging;
    }
    public sealed class MainWindow : Window
    {
        private readonly BudsClient client;
        private readonly Preferences settings;
        private readonly bool preview;
        private readonly List<ChoiceBinding> choices = new List<ChoiceBinding>();
        private readonly List<ToggleBinding> toggles = new List<ToggleBinding>();
        private readonly List<StrengthBinding> strengths = new List<StrengthBinding>();
        private readonly List<FrameworkElement> pages = new List<FrameworkElement>();
        private readonly List<Button> nav = new List<Button>();
        private readonly List<Button> noiseButtons = new List<Button>();
        private readonly List<Button> findButtons = new List<Button>();
        private readonly Grid pageHost = new Grid();
        private readonly ComboBox devicePicker = new ComboBox { MinWidth = 215, MaxWidth = 350 };
        private readonly TextBlock connection = Text("未连接",13,"#9AA8BD");
        private readonly TextBlock message = Text("正在查找已配对的耳机…",13,"#9AA8BD");
        private readonly TextBlock firmware = Text("—",14,"#9AA8BD");
        private readonly TextBlock mode = Text("—",16,"#8FB8FF");
        private readonly TextBlock spatialStatus = Text("等待耳机返回空间音频状态",12,"#9AA8BD");
        private readonly TextBlock fitStatus = Text("佩戴好双耳后开始检查",13,"#9AA8BD");
        private readonly TextBlock fitLeft = Text("左耳：—",16), fitRight = Text("右耳：—",16);
        private readonly ComboBox fitOutput = new ComboBox { MinWidth = 240,MaxWidth = 390 };
        private readonly ProgressBar fitProgress = new ProgressBar { Height = 5,IsIndeterminate = true,Margin = new Thickness(0,14,0,4),Visibility = Visibility.Collapsed };
        private Button fitStart, fitStop, fitRefresh;
        private FitAudio fitAudio;
        private CancellationTokenSource fitCancellation;
        private Task fitTask;
        private bool fitRunning;
        private readonly TextBlock deviceTitle = Text("REDMI Buds 6 Pro",22);
        private readonly TextBlock[] battery = { Text("—",32),Text("—",32),Text("—",32) };
        private readonly ProgressBar[] batteryBars = new ProgressBar[3];
        private readonly TextBlock[] charging = { Text("",12,"#9AA8BD"),Text("",12,"#9AA8BD"),Text("",12,"#9AA8BD") };
        private readonly Slider[] bands = new Slider[10];
        private readonly TextBlock[] gainLabels = new TextBlock[10];
        private readonly TextBlock[] freqLabels = new TextBlock[10];
        private readonly Button reconnectButton = new Button { Content = "连接耳机" };
        private Button eqApply, findStop;
        private Forms.NotifyIcon tray;
        private System.Drawing.Icon trayWhiteIcon, trayDarkIcon;
        private bool? trayUsesLightTheme;
        private Window closeChoiceDialog;
        private enum CloseAction { Cancel, Tray, Exit }
        private DispatcherTimer timer;
        private bool busy, updating, shuttingDown, eqDirty, ringing;
        private int tick;
        private DateTime nextReconnect = DateTime.MinValue;
        private int findGeneration;
        public FrameworkElement PreviewRoot { get; private set; }
        public void PreviewPage(int index) { SelectPage(index); }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);
        public MainWindow(BudsClient client,Preferences settings,bool previewMode)
        {
            this.client = client; this.settings = settings; preview = previewMode;
            Title = "Mi Buds Control"; Width = 1140; Height = 830; MinWidth = 970; MinHeight = 690;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brush("#0E1117"); Foreground = Brush("#EDF2FA"); FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))
                Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
            Build();
            SourceInitialized += delegate {
                int enabled = 1;
                try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,20,ref enabled,4); } catch { }
            };
            if (!preview)
            {
                client.Changed += delegate { Dispatcher.BeginInvoke(new Action(Render)); };
                client.Disconnected += delegate(string error) { Dispatcher.BeginInvoke(new Action(delegate { CancelFit(); SetMessage(error,true); nextReconnect = DateTime.UtcNow.AddSeconds(15); })); };
                Loaded += async delegate { await Initialize(); };
                Closing += OnClosing;
                CreateTray();
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += async delegate { await Tick(); };
                timer.Start();
            }
            Render();
        }
        private static SolidColorBrush Brush(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        private static TextBlock Text(string value,double size = 14,string color = "#EDF2FA")
        {
            return new TextBlock { Text = value,FontSize = size,Foreground = Brush(color),TextWrapping = TextWrapping.Wrap,VerticalAlignment = VerticalAlignment.Center };
        }
        private static StackPanel Stack() { return new StackPanel(); }
        private static Border Card(UIElement content)
        {
            return new Border { Background = Brush("#171D28"),CornerRadius = new CornerRadius(15),Padding = new Thickness(24),Margin = new Thickness(0,0,0,18),Child = content };
        }
        private static StackPanel Heading(string title,string subtitle)
        {
            var panel = Stack();
            var name = Text(title,25); name.FontWeight = FontWeights.SemiBold; panel.Children.Add(name);
            var sub = Text(subtitle,13,"#9AA8BD"); sub.Margin = new Thickness(0,8,0,22); panel.Children.Add(sub);
            return panel;
        }
        private void Build()
        {
            var root = new Grid(); PreviewRoot = root; root.Background = Background;
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(206) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            var sidebar = new DockPanel { Background = Brush("#121722"),LastChildFill = true,Margin = new Thickness(0) };
            Grid.SetColumn(sidebar,0); root.Children.Add(sidebar);
            var brand = Stack(); brand.Margin = new Thickness(24,30,20,24);
            brand.Children.Add(Text("◉  Mi Buds",25));
            var product = Text("耳机控制中心",12,"#9AA8BD"); product.Margin = new Thickness(0,9,0,0); brand.Children.Add(product);
            DockPanel.SetDock(brand,Dock.Top); sidebar.Children.Add(brand);
            var sideBottom = Stack(); sideBottom.Margin = new Thickness(24,18,20,22);
            sideBottom.Children.Add(Text("Windows · 0.1.7",12,"#8B99B0"));
            sideBottom.Children.Add(Text("ccsy.qn制作",11,"#64738E"));
            DockPanel.SetDock(sideBottom,Dock.Bottom); sidebar.Children.Add(sideBottom);
            var navPanel = Stack(); navPanel.Margin = new Thickness(14,10,14,0); sidebar.Children.Add(navPanel);
            string[] labels = { "◉   我的耳机","☝   手势设置","♫   音效设置","⚙   更多设置" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var button = new Button { Content = labels[i],Margin = new Thickness(0,0,0,8),HorizontalContentAlignment = HorizontalAlignment.Left,Padding = new Thickness(12,14,12,14) };
                button.Click += delegate { SelectPage(index); }; nav.Add(button); navPanel.Children.Add(button);
            }
            var main = new Grid { Margin = new Thickness(30,25,30,20) }; Grid.SetColumn(main,1); root.Children.Add(main);
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition());
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new Grid { Margin = new Thickness(0,0,0,22) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Stack(); title.Children.Add(deviceTitle); connection.Margin = new Thickness(0,8,0,0); title.Children.Add(connection); header.Children.Add(title);
            var actions = new StackPanel { Orientation = Orientation.Horizontal,VerticalAlignment = VerticalAlignment.Center };
            devicePicker.Margin = new Thickness(0,0,10,0); actions.Children.Add(devicePicker);
            reconnectButton.Click += async delegate { await ConnectSelected(); }; actions.Children.Add(reconnectButton);
            Grid.SetColumn(actions,1); header.Children.Add(actions); main.Children.Add(header);
            pages.Add(DevicePage()); pages.Add(GesturePage()); pages.Add(SoundPage()); pages.Add(SettingsPage());
            foreach (var page in pages) pageHost.Children.Add(page);
            Grid.SetRow(pageHost,1); main.Children.Add(pageHost);
            var footer = new Grid { Margin = new Thickness(0,12,0,0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            message.MaxHeight = 46; footer.Children.Add(message);
            var refresh = new Button { Content = "刷新状态",Padding = new Thickness(12,7,12,7),Margin = new Thickness(10,0,0,0) };
            refresh.Click += async delegate { await Run(delegate { return client.Refresh(); },"状态已刷新"); };
            Grid.SetColumn(refresh,1); footer.Children.Add(refresh); Grid.SetRow(footer,2); main.Children.Add(footer);
            Content = root; SelectPage(0);
        }
        private FrameworkElement Scroll(StackPanel panel) { return new ScrollViewer { Content = panel,Padding = new Thickness(0,0,8,0) }; }
        private void SelectPage(int index)
        {
            for (int i = 0; i < pages.Count; i++) { pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed; nav[i].Background = Brush(i == index ? "#233958" : "#121722"); }
        }
        private FrameworkElement DevicePage()
        {
            var page = Stack(); page.Children.Add(Heading("我的耳机","电量和设置由耳机实时返回。"));
            var batteryGrid = new Grid();
            string[] names = { "左耳机","右耳机","充电盒" };
            for (int i = 0; i < 3; i++)
            {
                batteryGrid.ColumnDefinitions.Add(new ColumnDefinition());
                var panel = Stack(); panel.Children.Add(Text(names[i],13,"#9AA8BD")); battery[i].Margin = new Thickness(0,10,0,10); panel.Children.Add(battery[i]);
                batteryBars[i] = new ProgressBar { Height = 5,Maximum = 100,Foreground = Brush("#71D5BB"),Background = Brush("#293243"),BorderThickness = new Thickness(0) };
                panel.Children.Add(batteryBars[i]); charging[i].Margin = new Thickness(0,8,0,0); panel.Children.Add(charging[i]);
                var card = Card(panel); card.Margin = new Thickness(i == 0 ? 0 : 7,0,i == 2 ? 0 : 7,18); Grid.SetColumn(card,i); batteryGrid.Children.Add(card);
            }
            page.Children.Add(batteryGrid);
            var noise = Stack(); var top = new DockPanel(); var label = Text("噪声控制",18); top.Children.Add(label); mode.HorizontalAlignment = HorizontalAlignment.Right; top.Children.Add(mode); noise.Children.Add(top);
            var modes = new Grid { Margin = new Thickness(0,18,0,20) };
            string[] modeNames = { "◌  关闭","◉  降噪","◎  通透" };
            for (int i = 0; i < 3; i++)
            {
                modes.ColumnDefinitions.Add(new ColumnDefinition()); byte value = (byte)i;
                var button = new Button { Content = modeNames[i],Margin = new Thickness(i == 0 ? 0 : 5,0,i == 2 ? 0 : 5,0),Padding = new Thickness(12,18,12,18) };
                button.Click += async delegate { await Run(delegate { return client.SetNoise(value); },"已切换到" + Protocol.NoiseName(value)); };
                noiseButtons.Add(button); Grid.SetColumn(button,i); modes.Children.Add(button);
            }
            noise.Children.Add(modes);
            AddToggle(noise,"自适应降噪","根据环境自动调整降噪强度。",0x25,false);
            AddStrengthSlider(noise,1);
            AddToggle(noise,"个性化降噪","使用耳机保存的个性化降噪设置。",0x3B,false);
            AddStrengthSlider(noise,2);
            page.Children.Add(Card(noise));
            var find = Stack(); find.Children.Add(Text("查找耳机",18));
            var hint = Text("请先摘下耳机。响铃将在 10 秒后自动停止，也可以随时手动停止。",13,"#9AA8BD"); hint.Margin = new Thickness(0,8,0,15); find.Children.Add(hint);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            string[] targets = { "查找双耳","查找左耳","查找右耳" };
            for (int i = 0; i < 3; i++)
            {
                byte target = (byte)i;
                var button = new Button { Content = targets[i],Margin = new Thickness(0,0,10,0) };
                button.Click += async delegate { await StartFind(target); }; findButtons.Add(button); buttons.Children.Add(button);
            }
            findStop = new Button { Content = "停止响铃",Background = Brush("#713A45") };
            findStop.Click += async delegate { await StopFind(); }; buttons.Children.Add(findStop); find.Children.Add(buttons); page.Children.Add(Card(find));
            return Scroll(page);
        }
        private FrameworkElement GesturePage()
        {
            var page = Stack(); page.Children.Add(Heading("手势设置","左右耳独立设置。修改后会保存到耳机。"));
            var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
            for (int ear = 0; ear < 2; ear++)
            {
                int side = ear; var panel = Stack(); panel.Children.Add(Text(ear == 0 ? "左耳机" : "右耳机",18));
                byte[] taps = {4,1,2,3,5}; string[] labels = { "单击","双击","三击","长按","滑动" };
                for (int i = 0; i < taps.Length; i++)
                {
                    byte tap = taps[i];
                    Option[] options = tap == 3 ? new[] {new Option(6,"噪声控制"),new Option(0,"语音助手")} :
                        tap == 5 ? new[] {new Option(11,"音量加减")} :
                        new[] {new Option(1,"播放 / 暂停"),new Option(2,"上一首"),new Option(3,"下一首"),new Option(4,"音量加"),new Option(5,"音量减")};
                    if (tap == 4) options = new[] {new Option(8,"无")}.Concat(options).ToArray();
                    AddChoice(panel,labels[i],options,s => s.Gesture(tap,side),async v => await client.SetGesture(tap,side,(byte)v),null,true);
                }
                AddChoice(panel,"长按循环",new[] {new Option(7,"降噪 / 通透 / 关闭"),new Option(6,"降噪 / 通透"),new Option(3,"降噪 / 关闭"),new Option(5,"通透 / 关闭")},
                    s => { byte[] data; return s.Config.TryGetValue(0x0A,out data) && data.Length >= 2 ? (int?)data[side] : null; },
                    async v => await client.SetCycle(side,(byte)v),s => s.Gesture(3,side) == 6,true);
                var card = Card(panel); card.Margin = new Thickness(ear == 0 ? 0 : 8,0,ear == 0 ? 8 : 0,18); Grid.SetColumn(card,ear); columns.Children.Add(card);
            }
            page.Children.Add(columns); page.Children.Add(Text("单击容易误触；语音助手的可用性取决于当前连接设备。",13,"#9AA8BD")); return Scroll(page);
        }
        private FrameworkElement SoundPage()
        {
            var page = Stack(); page.Children.Add(Heading("音效设置","设置保存在耳机中，切换设备后仍可使用。"));
            var sound = Stack(); AddToggle(sound,"自适应听感","根据耳道形状及佩戴情况调整听感。",0x29,false);
            AddChoice(sound,"音效模式",new[] {new Option(0,"标准"),new Option(5,"低音增强"),new Option(6,"高音增强"),new Option(1,"人声增强"),new Option(10,"自定义")},
                s => s.First(7),async v => await client.SetValue(7,(byte)v)); page.Children.Add(Card(sound));
            var spatial = Stack(); spatial.Children.Add(Text("空间音频",18));
            spatialStatus.Margin = new Thickness(0,8,0,0); spatial.Children.Add(spatialStatus);
            AddBoundToggle(spatial,"开启空间音频","使用耳机的空间音频效果。",s => s.SpatialFlag(1),client.SetSpatialAudio,s => s.HasBuds6ProSpatialProfile);
            AddBoundToggle(spatial,"头部追踪","开启后，声场随头部转动调整。",s => s.SpatialFlag(8),client.SetHeadTracking,s => s.HasBuds6ProSpatialProfile && s.SpatialFlag(1) == 1);
            AddChoice(spatial,"场景渲染",new[] {new Option(1,"经典"),new Option(2,"音乐"),new Option(3,"视频"),new Option(4,"游戏"),new Option(5,"有声书")},
                s => s.SpatialScene(),async v => await client.SetSpatialScene((byte)v),
                s => s.HasBuds6ProSpatialProfile && s.SpatialFlag(1) == 1 && (s.First(0x36) == 0 || s.First(0x36) == 1),false,false);
            var spatialHint = Text("建议播放音乐或视频试听。Windows 的空间音效可在系统声音设置中单独调整。",12,"#9AA8BD");
            spatialHint.Margin = new Thickness(0,16,0,0); spatial.Children.Add(spatialHint); page.Children.Add(Card(spatial));
            var eq = Stack(); eq.Children.Add(Text("自定义均衡器",18));
            var hint = Text("每个频段可调节 −6 至 +6 dB。保存后自动切换到自定义音效。",13,"#9AA8BD"); hint.Margin = new Thickness(0,8,0,18); eq.Children.Add(hint);
            var bandGrid = new Grid { Height = 215 };
            for (int i = 0; i < 10; i++)
            {
                bandGrid.ColumnDefinitions.Add(new ColumnDefinition()); var panel = Stack();
                gainLabels[i] = Text("0",12,"#8FB8FF"); gainLabels[i].HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(gainLabels[i]);
                bands[i] = new Slider { Orientation = Orientation.Vertical,Minimum = -6,Maximum = 6,TickFrequency = 1,IsSnapToTickEnabled = true,Height = 150,Width = 34,HorizontalAlignment = HorizontalAlignment.Center };
                int index = i;
                bands[i].ValueChanged += delegate { gainLabels[index].Text = ((int)bands[index].Value).ToString("+0;-0;0"); if (!updating) eqDirty = true; };
                panel.Children.Add(bands[i]); freqLabels[i] = Text("—",11,"#9AA8BD"); freqLabels[i].HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(freqLabels[i]);
                Grid.SetColumn(panel,i); bandGrid.Children.Add(panel);
            }
            eq.Children.Add(bandGrid);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            eqApply = new Button { Content = "保存并启用",Style = (Style)FindResource("Primary"),Margin = new Thickness(0,0,12,0) };
            eqApply.Click += async delegate { int[] gains = bands.Select(b => (int)b.Value).ToArray(); await Run(async delegate { await client.SetEqualizer(gains); eqDirty = false; },"均衡器已保存并启用"); }; buttons.Children.Add(eqApply);
            var flat = new Button { Content = "重置为平直" }; flat.Click += delegate { foreach (var band in bands) band.Value = 0; eqDirty = true; }; buttons.Children.Add(flat);
            eq.Children.Add(buttons); page.Children.Add(Card(eq));
            return Scroll(page);
        }
        private FrameworkElement SettingsPage()
        {
            var page = Stack(); page.Children.Add(Heading("更多设置","耳机功能和 Windows 应用设置。"));
            var device = Stack(); device.Children.Add(Text("耳机设置",18));
            AddBoundToggle(device,"佩戴检测","耳机端的摘下暂停、佩戴继续开关；播放行为取决于播放器支持。",s => s.WearingValue,client.SetWearing,null,true);
            AddToggle(device,"双设备连接","允许耳机同时连接两台设备。",4,false);
            AddToggle(device,"自动接听电话","来电后佩戴耳机自动接听；取决于连接设备的通话支持。",3,false); page.Children.Add(Card(device));
            var fit = Stack(); fit.Children.Add(Text("耳机贴合度检查",18));
            var fitHint = Text("请佩戴好双耳，暂停其他音频并保持周围安静。开始后会播放约 10 秒的检测音频，请确认下方输出是这副耳机。",13,"#9AA8BD");
            fitHint.Margin = new Thickness(0,8,0,12); fit.Children.Add(fitHint);
            fit.Children.Add(Row("检测音频输出",null,fitOutput));
            var fitResults = new Grid { Margin = new Thickness(0,20,0,8) };
            fitResults.ColumnDefinitions.Add(new ColumnDefinition()); fitResults.ColumnDefinitions.Add(new ColumnDefinition());
            fitResults.Children.Add(fitLeft); Grid.SetColumn(fitRight,1); fitResults.Children.Add(fitRight); fit.Children.Add(fitResults);
            fitStatus.Margin = new Thickness(0,8,0,14); fit.Children.Add(fitStatus); fit.Children.Add(fitProgress);
            var fitButtons = new StackPanel { Orientation = Orientation.Horizontal,Margin = new Thickness(0,10,0,0) };
            fitStart = new Button { Content = "开始检查",Margin = new Thickness(0,0,10,0) };
            fitStart.Click += async delegate { fitTask = StartFit(); await fitTask; };
            fitStop = new Button { Content = "取消检查",Margin = new Thickness(0,0,10,0) };
            fitStop.Click += delegate { CancelFit(); };
            fitRefresh = new Button { Content = "刷新音频输出" };
            fitRefresh.Click += delegate { LoadFitOutputs(); Render(); };
            fitButtons.Children.Add(fitStart); fitButtons.Children.Add(fitStop); fitButtons.Children.Add(fitRefresh);
            fit.Children.Add(fitButtons); page.Children.Add(Card(fit)); LoadFitOutputs();
            var app = Stack(); app.Children.Add(Text("应用设置",18));
            AddLocalToggle(app,"关闭窗口时询问退出方式",settings.AskBeforeClose,v => { settings.AskBeforeClose = v; settings.Save(); });
            AddLocalToggle(app,"不询问时缩小到托盘",settings.CloseToTray,v => { settings.CloseToTray = v; settings.Save(); });
            AddLocalToggle(app,"断线后自动重连",settings.AutoReconnect,v => { settings.AutoReconnect = v; settings.Save(); });
            AddLocalToggle(app,"登录 Windows 时启动",Preferences.StartupEnabled(),Preferences.Startup);
            var bluetooth = new Button { Content = "打开 Windows 蓝牙设置",HorizontalAlignment = HorizontalAlignment.Left,Margin = new Thickness(0,12,0,0) };
            bluetooth.Click += delegate { try { Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); } catch (Exception error) { SetMessage(error.Message,true); } }; app.Children.Add(bluetooth); page.Children.Add(Card(app));
            var about = Stack(); about.Children.Add(Text("设备信息",18)); firmware.Margin = new Thickness(0,12,0,12); about.Children.Add(firmware);
            about.Children.Add(Text("设备重命名和固件更新尚未接入，请暂时在手机应用中使用。",13,"#9AA8BD")); page.Children.Add(Card(about)); return Scroll(page);
        }
        private Grid Row(string label,string subtitle,UIElement control)
        {
            var row = new Grid { Margin = new Thickness(0,16,0,2) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var names = Stack(); names.Margin = new Thickness(0,0,18,0); names.Children.Add(Text(label,15));
            if (!string.IsNullOrEmpty(subtitle)) { var sub = Text(subtitle,12,"#9AA8BD"); sub.Margin = new Thickness(0,5,0,0); names.Children.Add(sub); }
            row.Children.Add(names); Grid.SetColumn(control,1); row.Children.Add(control); return row;
        }
        private void AddToggle(StackPanel panel,string label,string subtitle,byte code,bool inverted)
        {
            AddBoundToggle(panel,label,subtitle,s => s.First(code),enabled => code == 6 ? client.SetWearing(enabled) : client.SetValue(code,enabled ? (byte)1 : (byte)0),null,inverted);
        }
        private void AddBoundToggle(StackPanel panel,string label,string subtitle,Func<DeviceState,int?> getter,Func<bool,Task> setter,Func<DeviceState,bool> allowed = null,bool inverted = false)
        {
            var toggle = new CheckBox(); toggles.Add(new ToggleBinding { Control = toggle,Value = getter,Inverted = inverted,Enabled = allowed }); panel.Children.Add(Row(label,subtitle,toggle));
            RoutedEventHandler changed = async delegate {
                if (updating || busy) return; bool enabled = toggle.IsChecked == true;
                await Run(delegate { return setter(enabled); },label + "已更新");
            };
            toggle.Checked += changed; toggle.Unchecked += changed;
        }
        private void AddLocalToggle(StackPanel panel,string label,bool value,Action<bool> save)
        {
            var toggle = new CheckBox { IsChecked = value }; panel.Children.Add(Row(label,null,toggle));
            RoutedEventHandler changed = delegate { try { save(toggle.IsChecked == true); } catch (Exception error) { SetMessage("应用设置保存失败：" + error.Message,true); } };
            toggle.Checked += changed; toggle.Unchecked += changed;
        }
        private void AddChoice(StackPanel panel,string label,Option[] options,Func<DeviceState,int?> getter,Func<int,Task> setter,Func<DeviceState,bool> enabled = null,bool vertical = false,bool requiresValue = true)
        {
            var combo = new ComboBox { MinWidth = vertical ? 180 : 165,MaxWidth = 280,VerticalAlignment = VerticalAlignment.Center };
            foreach (var option in options) combo.Items.Add(option);
            choices.Add(new ChoiceBinding { Control = combo,Value = getter,Enabled = enabled,RequiresValue = requiresValue });
            if (vertical)
            {
                var text = Text(label,13,"#9AA8BD"); text.Margin = new Thickness(0,18,0,7); panel.Children.Add(text); combo.HorizontalAlignment = HorizontalAlignment.Stretch; combo.MaxWidth = double.PositiveInfinity; panel.Children.Add(combo);
            }
            else panel.Children.Add(Row(label,null,combo));
            combo.SelectionChanged += async delegate {
                if (updating || busy) return; var selected = combo.SelectedItem as Option;
                if (selected != null) await Run(delegate { return setter(selected.Code); },label + "已更新");
            };
        }
        private static string StrengthLabel(byte noiseMode,int value)
        {
            return noiseMode == 2 ? new[] { "通透","人声增强","环境增强" }[value] : "手动调节";
        }
        private void AddStrengthSlider(StackPanel parent,byte noiseMode)
        {
            var panel = Stack(); panel.Margin = new Thickness(0,18,0,4);
            var label = Text("等待耳机返回强度",13,"#9AA8BD");
            panel.Children.Add(Row(noiseMode == 1 ? "降噪强度" : "通透强度",null,label));
            var slider = new Slider { Minimum = 0,Maximum = noiseMode == 1 ? 19 : 2,
                TickFrequency = 1,IsSnapToTickEnabled = noiseMode == 2,SmallChange = 1,LargeChange = 1,
                IsMoveToPointEnabled = true,Height = 30,Margin = new Thickness(2,12,2,4) };
            System.Windows.Automation.AutomationProperties.SetName(slider,noiseMode == 1 ? "降噪强度，轻到深" : "通透强度");
            panel.Children.Add(slider);
            var ends = new Grid();
            ends.Children.Add(Text(noiseMode == 1 ? "轻" : "通透",12,"#9AA8BD"));
            if (noiseMode == 2) { var middle = Text("人声增强",12,"#9AA8BD"); middle.HorizontalAlignment = HorizontalAlignment.Center; ends.Children.Add(middle); }
            var right = Text(noiseMode == 1 ? "深" : "环境增强",12,"#9AA8BD"); right.HorizontalAlignment = HorizontalAlignment.Right; ends.Children.Add(right);
            panel.Children.Add(ends); parent.Children.Add(panel);
            var binding = new StrengthBinding { Mode = noiseMode,Control = slider,Label = label,Panel = panel,
                CommitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) } };
            strengths.Add(binding);
            binding.CommitTimer.Tick += async delegate {
                binding.CommitTimer.Stop();
                if (binding.Dragging) return;
                if (busy) { binding.CommitTimer.Start(); return; }
                if (!client.Connected || shuttingDown || ringing || fitRunning || client.State.NoiseMode != noiseMode ||
                    (noiseMode == 1 && client.State.First(0x25) != 0)) { binding.Editing = false; Render(); return; }
                byte value = (byte)Math.Round(slider.Value,MidpointRounding.AwayFromZero);
                try { await Run(delegate { return client.SetStrength(noiseMode,value); },noiseMode == 1 ? "降噪强度已更新" : "通透强度已更新"); }
                finally { binding.Editing = false; Render(); }
            };
            slider.ValueChanged += delegate {
                if (updating || busy || !slider.IsEnabled) return;
                binding.Editing = true;
                label.Text = StrengthLabel(noiseMode,(int)Math.Round(slider.Value,MidpointRounding.AwayFromZero));
                binding.CommitTimer.Stop();
                if (!binding.Dragging) binding.CommitTimer.Start();
            };
            slider.AddHandler(Thumb.DragStartedEvent,new DragStartedEventHandler(delegate {
                binding.Dragging = true; binding.Editing = true; binding.CommitTimer.Stop();
            }));
            slider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler(delegate(object sender,DragCompletedEventArgs e) {
                binding.Dragging = false;
                if (e.Canceled) { binding.Editing = false; Render(); }
                else binding.CommitTimer.Start();
            }));
        }
        public void Render()
        {
            if (shuttingDown) return;
            updating = true;
            try
            {
                var state = client.State;
                deviceTitle.Text = state.Name;
                connection.Text = fitRunning ? "正在检查耳机贴合度…" : busy ? "正在处理…" : client.Connected ? "● 已连接" : "○ 未连接";
                connection.Foreground = Brush(client.Connected ? "#71D5BB" : "#9AA8BD");
                reconnectButton.Content = client.Connected ? "重新连接" : "连接耳机";
                reconnectButton.IsEnabled = !busy && !fitRunning; devicePicker.IsEnabled = !busy && !fitRunning;
                foreach (var page in pages) page.IsEnabled = !busy;
                int?[] values = {state.Left,state.Right,state.Case}; bool[] flags = {state.LeftCharging,state.RightCharging,state.CaseCharging};
                for (int i = 0; i < 3; i++)
                {
                    battery[i].Text = values[i].HasValue ? values[i].Value + "%" : "—";
                    batteryBars[i].Value = values[i] ?? 0;
                    charging[i].Text = !client.Connected ? "等待连接" : !values[i].HasValue ? "暂未返回电量" : flags[i] ? "充电中" : "";
                    batteryBars[i].Opacity = values[i].HasValue ? 1 : 0.3;
                }
                mode.Text = Protocol.NoiseName(state.NoiseMode);
                for (int i = 0; i < noiseButtons.Count; i++)
                {
                    noiseButtons[i].Background = Brush(state.NoiseMode == i && client.Connected ? "#3B82F6" : "#252E3E");
                    noiseButtons[i].IsEnabled = client.Connected && !busy && !ringing && !fitRunning;
                }
                foreach (var binding in choices)
                {
                    int? value = binding.Value(state);
                    Option selected = value.HasValue ? binding.Control.Items.Cast<Option>().FirstOrDefault(o => o.Code == value.Value) : null;
                    if (value.HasValue && selected == null) { selected = new Option(value.Value,"保留当前设置"); binding.Control.Items.Add(selected); }
                    binding.Control.SelectedItem = selected;
                    binding.Control.IsEnabled = client.Connected && !busy && !ringing && !fitRunning && (!binding.RequiresValue || value.HasValue) && (binding.Enabled == null || binding.Enabled(state));
                }
                foreach (var binding in toggles)
                {
                    int? value = binding.Value(state); binding.Control.IsChecked = value.HasValue ? (bool?)(binding.Inverted ? value == 0 : value == 1) : null;
                    binding.Control.IsEnabled = client.Connected && !busy && !ringing && !fitRunning && value.HasValue && (value == 0 || value == 1) && (binding.Enabled == null || binding.Enabled(state));
                }
                spatialStatus.Text = !client.Connected ? "连接耳机后可设置空间音频" : !state.HasBuds6ProSpatialProfile ? "当前耳机型号尚未适配空间音频" :
                    !state.SpatialFlag(1).HasValue ? "尚未读取到空间音频状态，请刷新或重连" : state.SpatialFlag(1) == 0 ? "空间音频已关闭" :
                    state.SpatialFlag(8) == 1 ? "空间音频已开启 · 头部追踪已开启" : "空间音频已开启 · 头部追踪已关闭";
                foreach (var binding in strengths)
                {
                    int value;
                    bool known = state.Strength.TryGetValue(binding.Mode,out value) && value >= 0 && value <= binding.Control.Maximum;
                    bool active = state.NoiseMode == binding.Mode;
                    bool automatic = binding.Mode == 1 && state.First(0x25) == 1;
                    binding.Panel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                    binding.Control.IsEnabled = client.Connected && state.HasBuds6ProStrengthProfile && !busy && !ringing && !fitRunning && active && known &&
                        (binding.Mode != 1 || state.First(0x25) == 0);
                    if (!binding.Editing)
                    {
                        if (known) binding.Control.Value = value;
                        binding.Label.Text = automatic ? "自适应调节" : known ? StrengthLabel(binding.Mode,value) : "等待耳机返回强度";
                    }
                    if (!client.Connected || !active || automatic)
                    {
                        binding.CommitTimer.Stop(); binding.Editing = false;
                    }
                }
                int[] frequencies = state.Frequencies(), gains = state.Gains();
                for (int i = 0; i < bands.Length; i++)
                {
                    if (frequencies != null) freqLabels[i].Text = frequencies[i] >= 1000 ? (frequencies[i] / 1000.0).ToString("0.#") + "k" : frequencies[i].ToString();
                    if (gains != null && !eqDirty) bands[i].Value = gains[i];
                    if (gains == null && !eqDirty) gainLabels[i].Text = "—";
                    bands[i].IsEnabled = client.Connected && !busy && !ringing && !fitRunning && gains != null;
                }
                eqApply.IsEnabled = client.Connected && !busy && !ringing && !fitRunning && gains != null;
                foreach (var button in findButtons) button.IsEnabled = client.Connected && !busy && !ringing && !fitRunning;
                findStop.IsEnabled = client.Connected && ringing && !busy;
                fitStart.IsEnabled = client.Connected && state.HasBuds6ProStrengthProfile && !busy && !ringing && !fitRunning && fitOutput.SelectedItem != null;
                fitStop.IsEnabled = fitRunning;
                fitOutput.IsEnabled = !busy && !fitRunning;
                fitRefresh.IsEnabled = !busy && !fitRunning;
                fitProgress.Visibility = fitRunning ? Visibility.Visible : Visibility.Collapsed;
                firmware.Text = "固件版本：" + state.Firmware + "  ·  Mi Buds Control 0.1.7";
                if (tray != null) tray.Text = "Mi Buds · " + (client.Connected ? Protocol.NoiseName(state.NoiseMode) + " · 左 " + (state.Left.HasValue ? state.Left + "%" : "—") + " / 右 " + (state.Right.HasValue ? state.Right + "%" : "—") : "未连接");
            }
            finally { updating = false; }
        }
        private void LoadFitOutputs()
        {
            var previous = fitOutput.SelectedItem as FitAudioOutput;
            try
            {
                var outputs = FitAudio.Outputs(); fitOutput.ItemsSource = outputs;
                fitOutput.SelectedItem = outputs.FirstOrDefault(o => previous != null && o.Name == previous.Name) ?? outputs.FirstOrDefault();
                if (outputs.Count == 0) fitStatus.Text = "未找到耳机立体声音频输出，请连接耳机后点击“刷新音频输出”。";
                else if (!fitRunning) fitStatus.Text = "佩戴好双耳后开始检查。";
            }
            catch (Exception error) { fitStatus.Text = "读取音频输出失败：" + error.Message; }
        }
        private void StopFitAudio()
        {
            if (fitAudio != null) { fitAudio.Dispose(); fitAudio = null; }
        }
        private void CancelFit()
        {
            if (!fitRunning) return;
            if (fitCancellation != null) fitCancellation.Cancel();
            StopFitAudio();
            fitStatus.Text = "正在停止检查…";
        }
        private async Task StartFit()
        {
            if (!client.Connected || busy || ringing || fitRunning || shuttingDown) return;
            var output = fitOutput.SelectedItem as FitAudioOutput;
            if (output == null) { SetMessage("请先连接耳机的音频输出，并刷新音频输出列表。",true); return; }
            fitRunning = true; fitCancellation = new CancellationTokenSource();
            var token = fitCancellation.Token;
            fitLeft.Text = "左耳：等待检测"; fitRight.Text = "右耳：等待检测";
            fitLeft.Foreground = fitRight.Foreground = Brush("#9AA8BD");
            fitStatus.Text = "正在等待耳机进入检测状态…"; SetMessage("贴合度检查期间请保持双耳佩戴。其他耳机设置暂时暂停。"); Render();
            try
            {
                fitAudio = new FitAudio(output); fitAudio.Prepare();
                FitResult result = await client.CheckFit(delegate {
                    Dispatcher.Invoke(new Action(delegate {
                        token.ThrowIfCancellationRequested();
                        if (fitAudio == null) throw new OperationCanceledException();
                        fitAudio.Play(); fitStatus.Text = "正在播放检测音频并测量，请保持佩戴…";
                    }));
                },delegate { Dispatcher.Invoke(new Action(StopFitAudio)); },token);
                token.ThrowIfCancellationRequested();
                fitLeft.Text = result.Left == 1 ? "左耳：贴合良好" : "左耳：需要调整";
                fitRight.Text = result.Right == 1 ? "右耳：贴合良好" : "右耳：需要调整";
                fitLeft.Foreground = Brush(result.Left == 1 ? "#71D5BB" : "#FFB18A");
                fitRight.Foreground = Brush(result.Right == 1 ? "#71D5BB" : "#FFB18A");
                fitStatus.Text = result.Left == 1 && result.Right == 1 ? "双耳贴合良好。" : "请调整提示耳机的佩戴位置，或更换合适尺寸的耳塞，再重新检查。";
                SetMessage("贴合度检查已完成，检测音频已停止。");
            }
            catch (OperationCanceledException)
            {
                fitLeft.Text = "左耳：—"; fitRight.Text = "右耳：—";
                fitStatus.Text = "检查已取消。"; SetMessage("贴合度检查已取消，检测音频已停止。");
            }
            catch (Exception error)
            {
                fitLeft.Text = "左耳：未取得结果"; fitRight.Text = "右耳：未取得结果";
                fitStatus.Text = error.Message; SetMessage(error.Message,true);
            }
            finally
            {
                StopFitAudio(); fitCancellation.Dispose(); fitCancellation = null;
                fitRunning = false; nextReconnect = DateTime.UtcNow.AddSeconds(15); Render();
            }
        }
        private void SetMessage(string text,bool error = false) { message.Text = text; message.Foreground = Brush(error ? "#FFB18A" : "#9AA8BD"); }
        private async Task Run(Func<Task> action,string success)
        {
            if (busy || shuttingDown || fitRunning) return;
            busy = true; SetMessage("正在与耳机同步…"); Render();
            try { await action(); SetMessage(success); }
            catch (Exception error) { SetMessage(error.Message,true); }
            finally { busy = false; Render(); }
        }
        private async Task Initialize()
        {
            await Run(async delegate {
                var devices = await BudsClient.Discover(); devicePicker.ItemsSource = devices;
                devicePicker.SelectedItem = devices.FirstOrDefault(d => d.Id == settings.DeviceId) ?? devices.FirstOrDefault(d => d.Name.IndexOf("6 Pro",StringComparison.OrdinalIgnoreCase) >= 0) ?? devices.FirstOrDefault();
                var selected = devicePicker.SelectedItem as DeviceChoice;
                if (selected == null) throw new InvalidOperationException("没有找到已配对耳机，请先在 Windows 蓝牙设置中连接。");
                await client.Connect(selected.Id); settings.DeviceId = selected.Id; settings.Save();
            },"耳机已连接，设置已同步");
        }
        private async Task ConnectSelected()
        {
            if (devicePicker.SelectedItem == null) { await Initialize(); return; }
            await Run(async delegate {
                if (ringing) await client.Find(0,false);
                ringing = false; findGeneration++;
                var selected = (DeviceChoice)devicePicker.SelectedItem;
                await client.Connect(selected.Id); settings.DeviceId = selected.Id; settings.Save(); eqDirty = false;
            },"耳机已连接，设置已同步");
            nextReconnect = DateTime.UtcNow.AddSeconds(15);
        }
        private async Task Tick()
        {
            UpdateTrayTheme();
            if (busy || shuttingDown || fitRunning) return;
            tick++;
            if (!client.Connected && settings.AutoReconnect && DateTime.UtcNow >= nextReconnect && devicePicker.SelectedItem != null)
            {
                nextReconnect = DateTime.UtcNow.AddSeconds(30); await ConnectSelected();
            }
            else if (client.Connected && tick % 30 == 0 && !ringing)
                await Run(delegate { return client.Refresh(); },"状态已同步");
        }
        private async Task StartFind(byte target)
        {
            if (!client.Connected || busy || fitRunning) return;
            if (MessageBox.Show(this,"请先摘下耳机。继续后耳机会播放查找提示音，10 秒后停止。","查找耳机",MessageBoxButton.OKCancel,MessageBoxImage.Information) != MessageBoxResult.OK) return;
            int generation = ++findGeneration;
            await Run(async delegate { await client.Find(target,true); ringing = true; },"正在响铃，10 秒后自动停止");
            if (!ringing) return;
            await Task.Delay(10000);
            if (generation == findGeneration && ringing) await StopFind();
        }
        private async Task StopFind()
        {
            findGeneration++;
            // Stopping a locating sound must also work while another UI action is busy.
            try { await client.Find(0,false); ringing = false; SetMessage("响铃已停止"); }
            catch (Exception error) { SetMessage("无法确认停止响铃：" + error.Message,true); }
            Render();
        }
        private void CreateTray()
        {
            trayWhiteIcon = LoadTrayIcon("TrayWhite.ico");
            trayDarkIcon = LoadTrayIcon("TrayDark.ico");
            tray = new Forms.NotifyIcon { Text = "Mi Buds Control" };
            UpdateTrayTheme(); tray.Visible = true;
            SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
            var menu = new Forms.ContextMenuStrip(); menu.Items.Add("打开控制中心",null,delegate { ShowWindow(); });
            menu.Items.Add("降噪",null,async delegate { await Run(delegate { return client.SetNoise(1); },"已切换到降噪"); });
            menu.Items.Add("通透",null,async delegate { await Run(delegate { return client.SetNoise(2); },"已切换到通透"); });
            menu.Items.Add("关闭降噪",null,async delegate { await Run(delegate { return client.SetNoise(0); },"已关闭降噪"); });
            menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("完全退出应用",null,async delegate { await Quit(); }); tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowWindow(); };
        }
        private static System.Drawing.Icon LoadTrayIcon(string resource)
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidOperationException("缺少托盘图标资源：" + resource);
                using (var icon = new System.Drawing.Icon(stream,Forms.SystemInformation.SmallIconSize))
                    return (System.Drawing.Icon)icon.Clone();
            }
        }
        private void OnSystemThemeChanged(object sender,UserPreferenceChangedEventArgs args)
        {
            if (shuttingDown || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(UpdateTrayTheme));
        }
        private void UpdateTrayTheme()
        {
            if (tray == null || shuttingDown) return;
            bool light = false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                    if (value is int) light = (int)value != 0;
                    else if (trayUsesLightTheme.HasValue) return;
                }
            }
            catch (Exception error)
            {
                if (!trayUsesLightTheme.HasValue) Program.Log("TRAY theme read failed: " + error.Message);
                else return;
            }
            if (trayUsesLightTheme == light) return;
            tray.Icon = light ? trayDarkIcon : trayWhiteIcon;
            trayUsesLightTheme = light;
        }
        private void ShowWindow()
        {
            if (shuttingDown) return;
            Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate();
            if (closeChoiceDialog != null) closeChoiceDialog.Activate();
        }
        private CloseAction AskCloseAction()
        {
            var action = CloseAction.Cancel;
            var dialog = new Window { Owner = this,Title = "关闭 Mi Buds Control",Width = 500,
                SizeToContent = SizeToContent.Height,ResizeMode = ResizeMode.NoResize,ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,Background = Background,
                Foreground = Foreground,FontFamily = FontFamily,FontSize = 14 };
            dialog.Resources.MergedDictionaries.Add(Resources);
            var panel = Stack(); panel.Margin = new Thickness(24);
            panel.Children.Add(Text("请选择关闭方式",20));
            var hint = Text("缩小到托盘会继续保持耳机连接；完全退出会停止检测并结束应用。",13,"#9AA8BD");
            hint.Margin = new Thickness(0,12,0,22); panel.Children.Add(hint);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal,HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消",IsCancel = true,Margin = new Thickness(0,0,10,0) };
            var minimize = new Button { Content = "缩小到托盘",IsDefault = true,Margin = new Thickness(0,0,10,0),Style = (Style)Resources["Primary"] };
            var exit = new Button { Content = "完全退出" };
            minimize.Click += delegate { action = CloseAction.Tray; dialog.DialogResult = true; };
            exit.Click += delegate { action = CloseAction.Exit; dialog.DialogResult = true; };
            buttons.Children.Add(cancel); buttons.Children.Add(minimize); buttons.Children.Add(exit);
            panel.Children.Add(buttons); dialog.Content = panel;
            closeChoiceDialog = dialog;
            try { dialog.ShowDialog(); }
            finally { closeChoiceDialog = null; }
            return action;
        }
        private async void OnClosing(object sender,System.ComponentModel.CancelEventArgs args)
        {
            if (shuttingDown) return;
            args.Cancel = true;
            if (closeChoiceDialog != null) { closeChoiceDialog.Activate(); return; }
            CloseAction action = settings.AskBeforeClose ? AskCloseAction() : (settings.CloseToTray ? CloseAction.Tray : CloseAction.Exit);
            if (shuttingDown || action == CloseAction.Cancel) return;
            if (action == CloseAction.Tray)
            {
                CancelFit();
                Hide(); SetMessage("应用已留在系统托盘，右键托盘图标可完全退出。");
                try { tray.ShowBalloonTip(2500,"Mi Buds Control","已缩小到系统托盘。双击图标可重新打开，右键可完全退出。",Forms.ToolTipIcon.Info); } catch { }
            }
            else await Quit();
        }
        private async Task CleanUpConnection()
        {
            if (fitTask != null) { try { await fitTask; } catch { } }
            try { if (ringing && client.Connected) await client.Find(0,false); } catch { }
            await client.Disconnect();
        }
        private async Task Quit()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            if (timer != null) timer.Stop();
            foreach (var strength in strengths) strength.CommitTimer.Stop();
            SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
            if (closeChoiceDialog != null) closeChoiceDialog.Close();
            CancelFit();
            IsEnabled = false; SetMessage("正在完全退出应用…");
            // Bound cleanup so a stuck Bluetooth request cannot keep Exit pending indefinitely.
            try
            {
                Task cleanup = CleanUpConnection();
                if (await Task.WhenAny(cleanup,Task.Delay(5000)) == cleanup) await cleanup;
                else Program.Log("EXIT Bluetooth cleanup timed out");
            }
            catch (Exception error) { Program.Log("EXIT cleanup failed: " + error.Message); }
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            if (trayWhiteIcon != null) { trayWhiteIcon.Dispose(); trayWhiteIcon = null; }
            if (trayDarkIcon != null) { trayDarkIcon.Dispose(); trayDarkIcon = null; }
            Application.Current.Shutdown();
            Environment.Exit(0);
        }
    }
}
