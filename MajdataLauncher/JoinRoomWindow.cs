using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MajdataCollaboration;
using MajdataCore;
using MajdataSessionUi;

namespace MajdataLauncher;

internal sealed class JoinRoomWindow : Window
{
    private sealed record Target(int Pid, long Start, string Label) { public override string ToString() => Label; }
    public JoinRoomWindow()
    {
        Title = "连接联机写谱服务器"; MinWidth = 520; MinHeight = 540;
        SessionTheme.Apply(this, 600, 640);
        var panel = new StackPanel { Margin = new Thickness(24) };
        var fields = new StackPanel();
        var targets = new ComboBox();
        var address = new TextBox { Text = "192.168.1.10:18100" };
        var password = new PasswordBox();
        var displayName = new TextBox { Text = "Guest", MaxLength = 40 };
        var status = SessionTheme.Text("选择已打开歌曲的 Edit，然后连接房间。", true);
        panel.Children.Add(SessionTheme.Heading("加入写谱房间", "把房间连接交给指定的 Edit，多组窗口也能各自写谱。"));
        SessionTheme.Field(fields, "目标 Edit", targets);
        SessionTheme.Field(fields, "显示名字", displayName);
        SessionTheme.Field(fields, "服务器地址 · IP:端口", address);
        SessionTheme.Field(fields, "房间密码", password);
        var buttons = new WrapPanel();
        var refresh = SessionTheme.Button("刷新列表");
        var connect = SessionTheme.Button("连接房间", true);
        buttons.Children.Add(connect); buttons.Children.Add(refresh); fields.Children.Add(buttons);
        panel.Children.Add(SessionTheme.Card(fields)); panel.Children.Add(SessionTheme.Card(status));
        panel.Children.Add(SessionTheme.Text("连接前先保存当前歌曲。加入后会备份当前稿，下载主机歌曲与素材，并切换到房间谱面；同步状态显示在所选 Edit 的侧栏中。", true));
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        void Refresh()
        {
            targets.Items.Clear();
            var sessions = AlphaSession.List();
            foreach (var name in new[] { "MajdataEdit", "MajdataEditAlpha" })
            foreach (var process in Process.GetProcessesByName(name))
            using (process)
            {
                try
                {
                    var session = sessions.FirstOrDefault(s => s.EditPid == process.Id && AlphaSession.IsProcess(s.EditPid, s.EditStart));
                    targets.Items.Add(new Target(process.Id, process.StartTime.ToUniversalTime().Ticks,
                        (session?.Label ?? "默认组") + " · " + process.MainWindowTitle + " · PID " + process.Id));
                }
                catch { }
            }
            if (targets.Items.Count > 0) { targets.SelectedIndex = 0; status.Text = $"找到 {targets.Items.Count} 个 Edit，请确认目标歌曲。"; }
            else status.Text = "请先启动新版 Edit 并打开歌曲，再刷新列表。";
        }
        refresh.Click += (_, _) => Refresh();
        connect.Click += async (_, _) =>
        {
            if (targets.SelectedItem is not Target target) { status.Text = "请选择 Edit。"; return; }
            if (!AlphaSession.IsProcess(target.Pid, target.Start)) { status.Text = "该 Edit 已退出，请刷新。"; return; }
            fields.IsEnabled = false;
            try { status.Text = "正在连接…"; status.Text = await RoomHandoff.Join(target.Pid, address.Text, password.Password, displayName.Text); password.Clear(); }
            catch (Exception ex) { status.Text = "连接失败，请确认所选 Edit 为新版且已打开谱面：" + ex.Message; }
            finally { fields.IsEnabled = true; }
        };
        Loaded += (_, _) => Refresh();
    }
}
