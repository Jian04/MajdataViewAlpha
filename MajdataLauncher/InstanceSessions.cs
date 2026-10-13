using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MajdataCore;
using MajdataSessionUi;

namespace MajdataLauncher;

public partial class MainWindow
{
    private void MultiControl_Click(object sender, RoutedEventArgs e) => new MultiControlWindow().Show();
    private void JoinRoom_Click(object sender, RoutedEventArgs e) => new JoinRoomWindow().Show();
    private readonly Dictionary<string, PetControlServer> sessionPetServers = new();
    private bool multipleEditors;
    private readonly DispatcherTimer sessionTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private void StartSessionDiscovery()
    {
        sessionTimer.Tick += (_, _) => RefreshSessionServers();
        sessionTimer.Start(); RefreshSessionServers();
    }
    private void RefreshSessionServers()
    {
        if (petDragActive) return;
        CheckDockOwner();
        try
        {
            var sessions = AlphaSession.List();
            multipleEditors = sessions.Count(item => AlphaSession.IsProcess(item.EditPid, item.EditStart)) > 1;
            foreach (var key in sessionPetServers.Keys.Except(sessions.Select(s => s.Id)).ToArray())
            {
                sessionPetServers[key].Dispose(); sessionPetServers.Remove(key);
            }
            foreach (var s in sessions)
            {
                if (sessionPetServers.ContainsKey(s.Id)) continue;
                var server = new PetControlServer(s.PetPort, request => Dispatcher.BeginInvoke(() =>
                {
                    StatusBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.ColorHex));
                    StatusBorder.BorderThickness = new Thickness(multipleEditors ? 2 : 0);
                    HandlePetControlRequest(new PetControlRequest { Action = request.Action,
                        Message = request.Action.Equals("skin", StringComparison.OrdinalIgnoreCase)
                            ? request.Message : (multipleEditors ? "[" + s.Label + "] " : "") + request.Message,
                        Angle = request.Angle });
                }));
                try { server.Start(); sessionPetServers.Add(s.Id, server); }
                catch { server.Dispose(); } // Another pet may own it; retry discovery later.
            }
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
    }
    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        try { launcher.LaunchIndependent(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "独立实例", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void Sessions_Click(object sender, RoutedEventArgs e) =>
        new SessionManagerWindow(launcher.LaunchIndependent).Show();
    private void StopSessionDiscovery()
    {
        sessionTimer.Stop();
        foreach (var server in sessionPetServers.Values) server.Dispose();
        sessionPetServers.Clear();
    }
}
