using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MajdataCollaboration;

namespace MajdataSessionUi;

public sealed class ChartRoomWindow : Window
{
    private readonly Func<string> capture;
    private readonly Action<string> apply;
    private readonly Func<string, string> backup;
    private readonly TextBox address = new();
    private readonly PasswordBox password = new();
    private readonly TextBox roomName = new();
    private readonly CheckBox requirePassword = new() { Content = "设置房间密码", Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBox displayName = new();
    private readonly ProgressBar progress = new() { Height = 6, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 6, 0, 6) };
    public string DisplayName { get => displayName.Text.Trim(); set => displayName.Text = value?.Trim() ?? ""; }
    public Action<int>? SelectDifficulty { get; set; }
    public Action<string?>? IdentityChanged { get; set; }
    public Action? GuestDisconnected { get; set; }
    public Action? HostRoomClosed { get; set; }
    private RoomPresence? Presence() => CapturePresence?.Invoke() is { } p ? p with { Name = DisplayName } : null;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) };
    private readonly TextBox hostInfo = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    public Action? BeforeConnect { get; set; }
    public Func<bool>? BeforeSongSwitch { get; set; }
    public Func<string>? MediaRoot { get; set; }
    public Func<string[]>? MediaReuseFolders { get; set; }
    public Func<bool>? IsApplicationClosing { get; set; }
    public Func<bool>? CloseConfirmation { get; set; }
    private bool closingConfirmed;
    private int localChangeSequence;
    public Action<string, string>? InstallSong { get; set; }
    public Func<string, string, System.Threading.CancellationToken, Task>? InstallSongAsync { get; set; }
    public Func<RoomPresence>? CapturePresence { get; set; }
    public Action<RoomPresence[]>? ShowPresence { get; set; }
    public async Task SyncNow()
    {
        pendingLocalSync = true;
        var sequence = ++localChangeSequence;
        // Combine a burst of keystrokes, without waiting for a polling interval.
        await Task.Delay(80);
        if (sequence == localChangeSequence) await Sync();
    }
    private RoomServer? server;
    private RoomPublicTunnel? internetTunnel;
    private RoomPublicLobbyHost? publicLobbyHost;
    private string? hostMediaRoot, activeRoomId;
    private readonly StackPanel lobbyRows = new();
    private readonly TextBox lobbySearch = new();
    private readonly PasswordBox lobbyPassword = new();
    private readonly TextBlock lobbyHint = SessionTheme.Text("", true);
    private readonly StackPanel lobbySelection = new() { Visibility = Visibility.Collapsed };
    private RoomListing[] lobbyRooms = Array.Empty<RoomListing>();
    private RoomListing? selectedRoom;
    private readonly CheckBox searchable = new() { Content = "允许在房间大厅搜索到此房间", IsChecked = true, Margin = new Thickness(0, 2, 0, 10) };
    private bool preparingHost;
    public bool IsHost => server != null || preparingHost;
    public bool IsGuest => client != null && server == null;
    private RoomClient? client;
    private RoomClient? joiningClient;
    private RoomClient? videoClient;
    private RoomClient? watchClient;
    private string lastSynchronizedText = "";
    private string connectedSecret = "";
    private long revision;
    private int generation;
    private bool busy, paused, pendingLocalSync;
    private bool connectionHealthy = true;
    private RoomPresence[] knownMembers = Array.Empty<RoomPresence>();
    private string memberNotice = "";
    private DateTime memberNoticeUntil;
    private readonly ComboBox mode = new() { ItemsSource = new[] { "房间大厅", "通过地址加入", "创建房间" }, SelectedIndex = 0 };
    private readonly Button connectButton = SessionTheme.Button("连接房间", true);
    private readonly Button disconnectButton = SessionTheme.Button("断开连接");
    private readonly Button resolveButton = SessionTheme.Button("载入房间版本");
    private readonly StackPanel setup = new();
    public bool Connected => client != null && connectionHealthy;
    public ChartRoomWindow(Func<string> capture, Action<string> apply, Func<string, string> backup)
    {
        this.capture = capture; this.apply = apply; this.backup = backup;
        try { displayName.Text = File.ReadAllText(ProfilePath).Trim(); } catch (IOException) { }
        displayName.LostKeyboardFocus += (_, _) => SaveDisplayName();
        Title = "联机写谱"; MinWidth = 540; MinHeight = 560;
        SessionTheme.Apply(this, 620, 720);
        progress.Background = SessionTheme.Brush("ButtonsBackground", "#343A43");
        progress.Foreground = SessionTheme.Brush("HelperForeground", "#72B4D4");
        status.SetResourceReference(TextBlock.ForegroundProperty, "HelperForeground");
        progress.BorderThickness = new Thickness(0);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(10) };
        SessionTheme.Field(setup, "连接方式", mode);
        SessionTheme.Field(setup, "你的名字", displayName);
        var lobbyFields = new StackPanel();
        var lobbyHeader = SessionTheme.Heading("房间大厅", "找到好友的房间，加入后自动同步谱面。");
        lobbyFields.Children.Add(lobbyHeader);
        var searchRow = new DockPanel();
        SessionTheme.Input(lobbySearch);
        lobbySearch.Margin = new Thickness(0, 0, 8, 0);
        lobbySearch.ToolTip = "按房主或房间名称搜索";
        var refresh = SessionTheme.Button("刷新房间");
        refresh.Margin = new Thickness(0); DockPanel.SetDock(refresh, Dock.Right);
        refresh.Click += async (_, _) => await RefreshLobby();
        searchRow.Children.Add(refresh); searchRow.Children.Add(lobbySearch);
        lobbyFields.Children.Add(searchRow);
        lobbySearch.TextChanged += (_, _) => RenderLobby();
        lobbyHint.Margin = new Thickness(0, 8, 0, 8); lobbyFields.Children.Add(lobbyHint);
        lobbyRows.Margin = new Thickness(0, 4, 0, 0); lobbyFields.Children.Add(lobbyRows);
        lobbySelection.Children.Add(SessionTheme.Text("房间密码"));
        SessionTheme.Input(lobbyPassword); lobbyPassword.Margin = new Thickness(0, 6, 0, 8);
        lobbySelection.Children.Add(lobbyPassword);
        var enter = SessionTheme.Button("加入选中房间", true);
        enter.Click += async (_, _) =>
        {
            if (selectedRoom != null) await RunAction(() => Join(selectedRoom.Address, lobbyPassword.Password));
        };
        lobbySelection.Children.Add(enter); lobbyFields.Children.Add(lobbySelection);
        setup.Children.Add(lobbyFields);
        var joinFields = new StackPanel { Visibility = Visibility.Collapsed }; var hostFields = new StackPanel { Visibility = Visibility.Collapsed };
        SessionTheme.Field(joinFields, "房间地址", address);
        SessionTheme.Field(hostFields, "房间名称", roomName);
        hostFields.Children.Add(searchable);
        hostFields.Children.Add(requirePassword);
        setup.Children.Add(joinFields); setup.Children.Add(hostFields);
        var passwordFields = new StackPanel(); SessionTheme.Field(passwordFields, "房间密码", password);
        passwordFields.Visibility = Visibility.Collapsed;
        connectButton.Visibility = Visibility.Collapsed;
        setup.Children.Add(passwordFields);
        mode.SelectionChanged += (_, _) =>
        {
            bool hosting = mode.SelectedIndex == 2;
            lobbyFields.Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            joinFields.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            hostFields.Visibility = hosting ? Visibility.Visible : Visibility.Collapsed;
            passwordFields.Visibility = mode.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
            password.IsEnabled = !hosting || requirePassword.IsChecked == true;
            connectButton.Visibility = mode.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
            connectButton.Content = hosting ? "创建房间" : "连接房间";
            if (mode.SelectedIndex == 0) _ = RefreshLobby();
        };
        requirePassword.Checked += (_, _) => { password.IsEnabled = true; password.Focus(); };
        requirePassword.Unchecked += (_, _) => { password.Clear(); password.IsEnabled = mode.SelectedIndex != 2; };
        connectButton.HorizontalAlignment = HorizontalAlignment.Left;
        connectButton.Click += async (_, _) => await RunAction(() => mode.SelectedIndex == 2 ? Host() : Join(address.Text, password.Password));
        setup.Children.Add(connectButton); panel.Children.Add(SessionTheme.Card(setup));
        var connection = new StackPanel();
        connection.Children.Add(SessionTheme.Text("连接状态", size: 15));
        status.LineHeight = 21;
        hostInfo.Foreground = Foreground; hostInfo.Margin = new Thickness(0, 8, 0, 0);
        connection.Children.Add(hostInfo); connection.Children.Add(progress); connection.Children.Add(status);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        disconnectButton.Click += (_, _) => Disconnect();
        resolveButton.Click += async (_, _) => await RunAction(Resolve);
        resolveButton.ToolTip = "以房间内容替换当前编辑内容，保存仍由你决定。";
        actions.Children.Add(disconnectButton); actions.Children.Add(resolveButton);
        connection.Children.Add(actions); panel.Children.Add(SessionTheme.Card(connection));
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        timer.Tick += async (_, _) => await Sync();
        Loaded += async (_, _) => await RefreshLobby();
        Closing += (_, e) => e.Cancel = !PrepareClose();
        Closed += (_, _) => { SaveDisplayName(); Disconnect(); };
        SetStatus("未连接");
    }
    public bool PrepareClose()
    {
        if (closingConfirmed || IsApplicationClosing?.Invoke() == true ||
            (client == null && joiningClient == null && server == null && !preparingHost)) return true;
        var approved = CloseConfirmation?.Invoke() ??
            MessageBox.Show(Owner ?? this, "关闭联机页面会断开连接。是否断开？", "联机写谱",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (approved) closingConfirmed = true;
        return approved;
    }
    private static string ProfilePath
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            var root = folder.Name.Equals("MajdataEdit", StringComparison.OrdinalIgnoreCase) &&
                folder.Parent?.Name.Equals("App", StringComparison.OrdinalIgnoreCase) == true
                ? folder.Parent.Parent! : folder;
            return Path.Combine(root.FullName, "RoomName.txt");
        }
    }
    private void SaveDisplayName()
    {
        if (DisplayName.Length == 0) return;
        try { File.WriteAllText(ProfilePath, DisplayName); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { SetStatus("昵称未能保存到程序根目录：" + error.Message); }
    }
    private async Task RefreshLobby()
    {
        if (mode.SelectedIndex != 0 || Connected) return;
        lobbyHint.Text = "正在搜索房间…";
        try
        {
            var result = await RoomLobby.Discover();
            lobbyRooms = result.Rooms;
            lobbyHint.Text = result.Hint.Length > 0 ? result.Hint : $"找到 {lobbyRooms.Length} 个可加入的房间";
            RenderLobby();
        }
        catch (Exception error) { lobbyHint.Text = "搜索失败：" + ExplainConnectionError(error); }
    }
    private void RenderLobby()
    {
        lobbyRows.Children.Clear();
        var filter = lobbySearch.Text.Trim();
        foreach (var room in lobbyRooms.Where(room => filter.Length == 0 ||
            room.Host.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            room.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            var content = new DockPanel();
            var enter = SessionTheme.Button("加入", true);
            enter.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(enter, Dock.Right);
            enter.Click += async (_, _) =>
            {
                selectedRoom = room;
                if (room.HasPassword)
                {
                    lobbyPassword.Clear();
                    lobbySelection.Visibility = Visibility.Visible;
                    lobbyPassword.Focus();
                }
                else await RunAction(() => Join(room.Address, ""));
            };
            content.Children.Add(enter);
            var labels = new StackPanel();
            var name = SessionTheme.Text(room.Name, size: 15); name.FontWeight = FontWeights.SemiBold;
            labels.Children.Add(name);
            labels.Children.Add(SessionTheme.Text($"房主 {room.Host}   ·   {room.Players} 人在线   ·   {(room.HasPassword ? "需要密码" : "公开加入")}", true));
            content.Children.Add(labels);
            lobbyRows.Children.Add(SessionTheme.Card(content));
        }
        if (lobbyRows.Children.Count == 0)
            lobbyRows.Children.Add(SessionTheme.Text("没有匹配的房间。可刷新或通过地址加入隐藏房间。", true));
    }
    private async Task RunAction(Func<Task> action)
    {
        try { await action(); } catch (Exception e) { SetStatus(ExplainConnectionError(e)); }
        finally { if (!busy) progress.IsIndeterminate = false; RefreshControls(); }
    }
    private void RefreshControls()
    {
        setup.IsEnabled = !Connected && !busy;
        disconnectButton.IsEnabled = Connected || busy || server != null;
        resolveButton.IsEnabled = Connected && paused && !busy;
    }
    private void SetStatus(string text) { if (status.Text != text) RoomNetworkTrace.Write(text); status.Text = text; RefreshControls(); }
    private async Task Host()
    {
        if (Connected || busy) throw new InvalidOperationException("请先断开当前连接。");
        if (DisplayName.Length == 0) throw new InvalidOperationException("请先填写你的名字。");
        if (roomName.Text.Trim().Length == 0) throw new InvalidOperationException("请先填写房间名称。");
        SaveDisplayName();
        if (string.IsNullOrWhiteSpace(MediaRoot?.Invoke())) throw new InvalidOperationException("请先打开要共享的歌曲，再创建房间。");
        preparingHost = true;
        try { BeforeConnect?.Invoke(); }
        finally { preparingHost = false; }
        var initial = capture(); var mediaRoot = MediaRoot?.Invoke();
        var secret = requirePassword.IsChecked == true ? password.Password : "";
        hostMediaRoot = mediaRoot;
        busy = true; SetStatus("正在准备主机歌曲素材…");
        progress.IsIndeterminate = true;
        int hostEpoch=generation;
        try {
            var prepared=await Task.Run(() => new RoomServer(0, secret, initial, loopbackOnly: true, mediaRoot: mediaRoot));
            if(hostEpoch!=generation) { prepared.Dispose(); return; }
            server=prepared;
        }
        finally { busy = false; }
        server.HostDifficulty = CapturePresence?.Invoke()?.Difficulty ?? -1;
        try { await Join("127.0.0.1:" + server.Port, secret); }
        catch { server?.Dispose(); server = null; throw; }
        if (!Connected) return;
        hostInfo.Text = "正在连接公网房间…";
        hostInfo.Visibility = Visibility.Visible;
        var hostedServer=server;
        hostEpoch=generation;
        try
        {
            var created=await RoomPublicTunnel.Open(hostedServer.Port, text => Dispatcher.InvokeAsync(() => {
                if(hostEpoch==generation) SetStatus(text);
            }));
            if(hostEpoch!=generation || !Connected || !ReferenceEquals(server,hostedServer)) { created.Dispose(); return; }
            internetTunnel=created;
            var roomAddress=created.Address;
            if (searchable.IsChecked == true)
            {
                // Directory requests are received on a worker thread. Snapshot
                // the UI fields here instead of touching WPF controls there.
                var hostName = DisplayName;
                var roomTitle = roomName.Text.Trim();
                publicLobbyHost = new RoomPublicLobbyHost(() => new RoomListing(hostName, roomTitle,
                    roomAddress, secret.Length > 0, hostedServer.PeerCount));
            }
            hostInfo.Text = $"公网房间：{internetTunnel.Address}";
            if (publicLobbyHost != null)
            {
                SetStatus("房间地址已就绪，正在连接大厅…");
                var lobby = publicLobbyHost;
                try { await lobby.Ready.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException)
                {
                    if (hostEpoch == generation)
                        SetStatus("房间地址已就绪，大厅暂未连接；可以复制地址加入，大厅正在自动重连。");
                    return;
                }
            }
            if (hostEpoch == generation) SetStatus("房间已创建，可从大厅加入或复制公网房间地址。");
        }
        catch (Exception error)
        {
            Disconnect();
            throw new IOException("公网房间创建失败：" + error.Message, error);
        }
    }
    private Task<string> DownloadRequiredMedia(RoomClient connection)
    {
        var epoch=generation;
        var root=Path.Combine(MajdataCore.AlphaSession.Root,"RoomCache");
        var localFolders = MediaReuseFolders?.Invoke() ?? new[] { MediaRoot?.Invoke() ?? "" };
        return Task.Run(() => RoomMediaCatalog.Download(connection,root,
            text => Dispatcher.InvokeAsync(() => { if(epoch==generation) SetStatus(text); }),
            value => Dispatcher.InvokeAsync(() => { if(epoch==generation) { progress.IsIndeterminate=false; progress.Value=value; } }),
            includeVideo:false, localFolders:localFolders));
    }
    public async Task Join(string endpoint, string secret)
    {
        endpoint = endpoint.Trim();
        if (Connected || busy) throw new InvalidOperationException("请先断开当前连接。");
        if (DisplayName.Length == 0) throw new InvalidOperationException("请先填写你的名字。");
        if (server == null && !RoomLobby.ValidPublicAddress(endpoint))
            throw new ArgumentException("请输入房主分享的公网房间地址。");
        SaveDisplayName();
        if (server == null) BeforeConnect?.Invoke();
        busy = true; int epoch = ++generation; SetStatus("正在连接房间…");
        progress.IsIndeterminate = true;
        RoomClient? next = null;
        try
        {
            next = new RoomClient(endpoint, secret);
            joiningClient = next;
            var result = await next.Exchange(0, "", true, Presence());
            if (epoch != generation) return;
            if (result.Closed) throw new IOException("房主已关闭房间。");
            if (server == null && (InstallSongAsync != null || InstallSong != null))
            {
                var folder = await DownloadRequiredMedia(next);
                if (epoch != generation) return;
                SetStatus("素材已校验，正在读取最新谱面…");
                progress.IsIndeterminate = true;
                result = await next.Exchange(0, "", true, Presence());
                if (result.Closed) throw new IOException("房主已关闭房间。");
                if (result.Room != new DirectoryInfo(folder).Name) throw new IOException("房主在同步期间切换了歌曲，请重新连接。");
                SetStatus("正在加载歌曲音频和波形…");
                await InstallDownloadedSong(folder, result.Text, next);
                if (epoch != generation) return;
                if (result.HostDifficulty is >= 0 and < 7) SelectDifficulty?.Invoke(result.HostDifficulty);
                StartVideoSync(endpoint, secret, folder, epoch);
            }
            // The host already owns this exact text. Reapplying it marks a clean chart dirty.
            if (server == null || !string.Equals(capture(), result.Text, StringComparison.Ordinal))
                apply(result.Text);
            revision = result.Revision; activeRoomId = result.Room; client = next;
            lastSynchronizedText = result.Text;
            UpdateMembers(result);
            connectedSecret = secret;
            address.Text = endpoint; password.Password = ""; paused = false;
            lobbySelection.Visibility = Visibility.Collapsed;
            progress.IsIndeterminate = false; progress.Value = 100;
            SetStatus("已连接");
            timer.Interval = TimeSpan.FromMilliseconds(server == null ? 2000 : 250);
            timer.Start();
            if (server == null)
            {
                // Local reuse can skip the media download's P2P setup entirely.
                // Text edits still need the same chance to use a direct channel.
                _ = next.TryDirectPeer(result.Room);
                StartWatch(endpoint, secret, next.PeerId, epoch);
            }
        }
        catch (Exception error)
        {
            LogRoomError("加入房间", error);
            if (!ReferenceEquals(client, next)) next?.Dispose();
            if (epoch == generation) throw;
        }
        finally
        {
            if (ReferenceEquals(joiningClient, next)) joiningClient = null;
            if (!ReferenceEquals(client, next)) next?.Dispose();
            if (epoch == generation) { progress.IsIndeterminate = false; busy = false; RefreshControls(); }
        }
    }
    private Task InstallDownloadedSong(string folder, string text, RoomClient connection)
    {
        if (InstallSongAsync != null) return InstallSongAsync(folder, text, connection.Cancellation);
        InstallSong?.Invoke(folder, text);
        return Task.CompletedTask;
    }
    private static void LogRoomError(string stage, Exception error)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "collaboration-error.log"),
            $"[{DateTime.Now:O}] {stage}{Environment.NewLine}{error}{Environment.NewLine}"); }
        catch { }
    }
    private void StartVideoSync(string endpoint, string secret, string folder, int epoch)
    {
        videoClient?.Dispose();
        videoClient = new RoomClient(endpoint, secret, client?.PeerId ?? joiningClient?.PeerId);
        var transfer = videoClient;
        _ = SyncVideoInBackground(transfer, folder, epoch);
    }
    private void StartWatch(string endpoint, string secret, string peerId, int epoch)
    {
        watchClient?.Dispose();
        watchClient = new RoomClient(endpoint, secret, peerId);
        _ = WatchForChanges(watchClient, epoch);
    }
    private async Task WatchForChanges(RoomClient watcher, int epoch)
    {
        // Each watch uses its own channel; it must not block edits behind a
        // long-poll, nor needlessly keep using a slow relay after P2P is available.
        await watcher.TryDirectPeer(activeRoomId ?? "");
        while (epoch == generation && ReferenceEquals(watchClient, watcher))
        {
            try
            {
                var watchedRevision = revision;
                var watchedBasis = lastSynchronizedText;
                var result = await watcher.WatchText(watchedRevision, activeRoomId ?? "", watchedBasis);
                if (epoch != generation || !ReferenceEquals(watchClient, watcher)) break;
                if (HandleRoomClosed(result)) break;
                UpdateMembers(result);
                if (busy || paused || watchedRevision != revision)
                {
                    await Task.Delay(300);
                    continue;
                }
                if (result.Room != activeRoomId || capture() != lastSynchronizedText)
                {
                    await Sync();
                    continue;
                }
                if (result.Revision == revision) continue;
                // A clean local editor can install the watched snapshot directly,
                // without spending another public-tunnel round trip on a poll.
                busy = true;
                try
                {
                    apply(result.Text);
                    revision = result.Revision;
                    lastSynchronizedText = result.Text;
                    pendingLocalSync = false;
                    progress.Value = 100;
                    SetStatus(ConnectedStatus(result));
                }
                finally { busy = false; RefreshControls(); }
            }
            catch (Exception)
            {
                if (epoch != generation || !ReferenceEquals(watchClient, watcher)) break;
                MarkConnectionLost();
                await Task.Delay(750);
            }
        }
    }
    private async Task SyncVideoInBackground(RoomClient transfer, string folder, int epoch)
    {
        var localFolders = MediaReuseFolders?.Invoke() ?? new[] { MediaRoot?.Invoke() ?? "" };
        try
        {
            await Task.Run(() => RoomMediaCatalog.DownloadVideos(transfer, folder,
                _ => { }, null, localFolders));
        }
        catch (Exception error)
        {
            if (epoch == generation && ReferenceEquals(videoClient, transfer))
                SetStatus("已连接；视频素材同步失败：" + ExplainConnectionError(error));
        }
        finally
        {
            if (ReferenceEquals(videoClient, transfer)) videoClient = null;
            transfer.Dispose();
        }
    }
    private async Task Sync()
    {
        if (busy || paused || client == null) return;
        busy = true; pendingLocalSync = false;
        var current = client; int epoch = generation;
        try
        {
            if (server != null && internetTunnel is { IsRunning: false })
            {
                // Do not keep advertising an address whose helper has exited.
                publicLobbyHost?.Dispose(); publicLobbyHost = null;
                SetStatus("公网连接已中断，正在重新建立房间连接…");
                var hostedServer = server;
                var replacement = await RoomPublicTunnel.Open(hostedServer.Port);
                if (epoch != generation || !ReferenceEquals(server, hostedServer))
                { replacement.Dispose(); return; }
                internetTunnel.Dispose(); internetTunnel = replacement;
                if (searchable.IsChecked == true)
                {
                    var name = DisplayName; var title = roomName.Text.Trim(); var endpoint = replacement.Address;
                    var hasPassword = connectedSecret.Length > 0;
                    publicLobbyHost = new RoomPublicLobbyHost(() => new RoomListing(name, title, endpoint, hasPassword, hostedServer.PeerCount));
                }
                hostInfo.Text = $"公网房间：{replacement.Address}";
                SetStatus("公网房间连接已恢复，房间地址已更新。");
            }
            string sent = capture();
            if (server != null)
            {
                var currentRoot = MediaRoot?.Invoke();
                if (!string.IsNullOrWhiteSpace(currentRoot) &&
                    !string.Equals(currentRoot, hostMediaRoot, StringComparison.OrdinalIgnoreCase))
                {
                    server.SwitchSong(sent, currentRoot, CapturePresence?.Invoke()?.Difficulty ?? -1);
                    hostMediaRoot = currentRoot;
                }
                else server.HostDifficulty = CapturePresence?.Invoke()?.Difficulty ?? -1;
            }
            var result = await current.SyncText(revision, lastSynchronizedText, sent, Presence());
            if (epoch != generation) return;
            if (HandleRoomClosed(result)) return;
            UpdateMembers(result);
            if (result.Room != activeRoomId)
            {
                if (server == null)
                {
                    await InstallSwitchedSong(current, result, epoch);
                    return;
                }
                activeRoomId = result.Room;
                revision = result.Revision;
                lastSynchronizedText = result.Text;
                progress.Value = 100;
                SetStatus(server == null ? "房主已切换歌曲，已同步新谱面。" : "已切换共享歌曲。");
                return;
            }
            if (result.Conflict || !TextMerge.TryMerge(sent, capture(), result.Text, out var merged))
            {
                paused = true;
                progress.Value = 0;
                SetStatus("同一段内容发生冲突。当前修改仍在编辑器内，可复制后载入房间版本。");
                return;
            }
            if (merged != capture()) apply(merged);
            // This is the server's acknowledged basis. Keystrokes entered while
            // the request was in flight still need their own subsequent patch.
            lastSynchronizedText = result.Text;
            pendingLocalSync |= merged != result.Text;
            revision = result.Revision;
            progress.Value = 100;
            SetStatus(ConnectedStatus(result));
        }
        catch (Exception e)
        {
            if (epoch != generation) return;
            MarkConnectionLost();
        }
        finally
        {
            busy = false; RefreshControls();
            if (pendingLocalSync && !paused && client != null && capture() != lastSynchronizedText)
            {
                pendingLocalSync = false;
                Dispatcher.BeginInvoke(new Action(() => _ = Sync()));
            }
        }
    }
    private async Task<bool> InstallSwitchedSong(RoomClient current, RoomReply result, int epoch)
    {
        if (BeforeSongSwitch?.Invoke() == false)
        {
            paused = true; SetStatus("已暂缓切换歌曲。保存当前谱面后，点击“载入房间版本”继续。");
            return false;
        }
        progress.IsIndeterminate = true;
        try
        {
            videoClient?.Dispose(); videoClient = null;
            var folder = await DownloadRequiredMedia(current);
            if (epoch != generation) return false;
            SetStatus("素材已校验，正在读取最新谱面…");
            result = await current.Exchange(0, "", true, Presence());
            if (result.Room != new DirectoryInfo(folder).Name) throw new IOException("房主在同步期间切换了歌曲，请重试。");
            SetStatus("正在加载歌曲音频和波形…");
            await InstallDownloadedSong(folder, result.Text, current);
            if (epoch != generation) return false;
            if (result.HostDifficulty is >= 0 and < 7) SelectDifficulty?.Invoke(result.HostDifficulty);
            apply(result.Text);
            revision = result.Revision; activeRoomId = result.Room;
            lastSynchronizedText = result.Text;
            paused = false;
            SetStatus("房主已切换歌曲，已同步新谱面。");
            StartVideoSync(address.Text, connectedSecret, folder, epoch);
            return true;
        }
        finally { progress.IsIndeterminate = false; }
    }
    private async Task Resolve()
    {
        if (busy || client == null) return;
        busy = true; int epoch = generation;
        try
        {
            var result = await client.Exchange(0, "", true);
            if (epoch != generation) return;
            if (result.Room != activeRoomId) { await InstallSwitchedSong(client, result, epoch); return; }
            apply(result.Text); revision = result.Revision; lastSynchronizedText = result.Text; paused = false;
            progress.Value = 100; SetStatus("已载入房间版本，尚未保存到本地。");
        }
        finally { busy = false; RefreshControls(); }
    }
    private Task disconnectCompletion = Task.CompletedTask;
    public Task DisconnectAndWaitAsync()
    {
        Disconnect(suppressSavePrompt: true);
        return disconnectCompletion;
    }
    public void Disconnect(bool roomClosed = false, bool suppressSavePrompt = false)
    {
        var guest = client != null && server == null;
        var departing = client;
        var closingServer = server;
        var closingTunnel = internetTunnel;
        generation++; timer.Stop(); joiningClient?.Dispose(); joiningClient = null;
        busy = false; watchClient?.Dispose(); watchClient = null;
        videoClient?.Dispose(); videoClient = null; client = null;
        publicLobbyHost?.Dispose(); publicLobbyHost = null;
        internetTunnel = null;
        server = null; paused = false; activeRoomId = null; hostMediaRoot = null; connectedSecret = "";
        knownMembers = Array.Empty<RoomPresence>(); memberNotice = ""; connectionHealthy = false;
        if (roomClosed && closingServer == null) departing?.Dispose();
        else if (departing != null || closingServer != null || closingTunnel != null)
            disconnectCompletion = CompleteDisconnect(departing, closingServer, closingTunnel);
        IdentityChanged?.Invoke(null);
        progress.IsIndeterminate = false; progress.Value = 0;
        ShowPresence?.Invoke(Array.Empty<RoomPresence>());
        hostInfo.Text = ""; hostInfo.Visibility = Visibility.Collapsed;
        SetStatus("已断开。本地谱面仍保留，可继续编辑和保存。");
        if (guest && !roomClosed && !suppressSavePrompt) GuestDisconnected?.Invoke();
    }
    private static async Task CompleteDisconnect(RoomClient? departing, RoomServer? closingServer, RoomPublicTunnel? tunnel)
    {
        try
        {
            if (closingServer != null) await closingServer.CloseRoomAsync(departing?.PeerId).ConfigureAwait(false);
            else if (departing != null) await departing.Leave().WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
        }
        catch (Exception error) { LogRoomError("disconnect notification", error); }
        finally { departing?.Dispose(); closingServer?.Dispose(); tunnel?.Dispose(); }
    }
    private bool HandleRoomClosed(RoomReply reply)
    {
        if (!reply.Closed) return false;
        Disconnect(roomClosed: true);
        SetStatus("房主已关闭房间，连接已断开。本地谱面仍保留，可继续编辑和保存。");
        HostRoomClosed?.Invoke();
        RefreshControls();
        return true;
    }
    private void UpdateMembers(RoomReply reply)
    {
        connectionHealthy = true;
        progress.IsIndeterminate = false; progress.Value = 100;
        var members = reply.Cursors ?? Array.Empty<RoomPresence>();
        var removed = knownMembers.Where(old => !members.Any(current => current.Peer == old.Peer)).ToArray();
        if (removed.Length > 0)
        {
            memberNotice = string.Join("、", removed.Select(p => p.Name)) + " 已离开房间";
            memberNoticeUntil = DateTime.UtcNow.AddSeconds(5);
        }
        knownMembers = members;
        IdentityChanged?.Invoke(reply.Peers > 1 ? reply.SelfColor : null);
        ShowPresence?.Invoke(members);
        SetStatus(ConnectedStatus(reply));
        RefreshControls();
    }
    private string ConnectedStatus(RoomReply reply) =>
        (DateTime.UtcNow < memberNoticeUntil ? memberNotice + " · " : "") +
        $"{(server == null ? "已连接" : "房间运行中")} · {reply.Peers} 人在线";
    private void MarkConnectionLost()
    {
        connectionHealthy = false; progress.Value = 0;
        ShowPresence?.Invoke(Array.Empty<RoomPresence>());
        SetStatus("连接已中断，正在重试。本地内容已保留。");
        RefreshControls();
    }
    private static string ExplainConnectionError(Exception error)
    {
        if (error.InnerException is { } inner && inner is not IOException)
            return ExplainConnectionError(inner);
        if (error is OperationCanceledException or TimeoutException)
            return "公网连接超时。请检查网络后刷新房间并重试。";
        if (error is WebSocketException or SocketException or HttpRequestException ||
            error.Message.Contains("remote party closed", StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains("connection closed", StringComparison.OrdinalIgnoreCase))
            return "公网连接已断开。请检查网络后重新加入房间。";
        return error.Message;
    }
}
