using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using MajdataCollaboration;
using MajdataCore;
using MajdataSessionUi;

namespace MajdataEdit;

public partial class MainWindow
{
    private bool editorUiReady;
    private bool startupViewAligned;
    private Button? dockedPetButton;
    private int dockedPetPid;
    private long dockedPetStart;
    private Point petDragStart;
    private bool petDragging;
    private GroupReply DockPet(GroupCommand command)
    {
        if (command.Action == "dock-complete")
        {
            if (dockedPetPid != command.PetPid || dockedPetStart != command.PetStart || dockedPetButton == null)
                return new(false, "桌宠停靠状态已改变");
            dockedPetButton.Visibility = Visibility.Visible;
            dockedPetButton.IsHitTestVisible = true;
            dockedPetButton.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            return new(true, "停靠动画已完成");
        }
        if (!editorUiReady || !startupViewAligned || isLoading) return new(false, "编辑器和预览窗口仍在加载");
        if (dockedPetPid != 0 && !AlphaSession.IsProcess(dockedPetPid, dockedPetStart))
        {
            dockedPetPid = 0;
            if (dockedPetButton != null) dockedPetButton.Visibility = Visibility.Collapsed;
            MainMenu.Padding = new Thickness(0, 0, 60, 0);
        }
        if (command.Action == "dock-ready") return new(AlphaSession.IsProcess(command.PetPid, command.PetStart), "编辑器已就绪");
        var targetPoint = new Point(Math.Max(0, MainMenu.ActualWidth - 76), 11);
        var point = MainMenu.PointFromScreen(new Point(command.X, command.Y));
        if (!AlphaSession.IsProcess(command.PetPid, command.PetStart) ||
            (command.Action != "dock-startup" &&
            (Math.Abs(point.X - targetPoint.X) > 40 || Math.Abs(point.Y - targetPoint.Y) > 32)))
            return new(false, "未放到工具栏");
        if (dockedPetPid != 0 && (dockedPetPid != command.PetPid || dockedPetStart != command.PetStart)) return new(false, "工具栏已有桌宠");
        EnsureToolSidebar();
        dockedPetButton ??= CreatePetButton();
        if (dockedPetButton.Parent == null) ((Grid)Content).Children.Add(dockedPetButton);
        var target = MainMenu.PointToScreen(targetPoint);
        if (command.Action == "dock-probe")
        {
            // A probe is only a hit-test. The launcher provides the blurred
            // preview; the toolbar icon belongs exclusively to dock-complete.
            return new(true, "松开以停靠", X: (int)target.X, Y: (int)target.Y);
        }
        dockedPetPid = command.PetPid; dockedPetStart = command.PetStart;
        // The launcher explicitly notifies us after its return flight and Hide().
        // Waiting for that signal keeps the icon from appearing over the pet.
        dockedPetButton.IsHitTestVisible = false;
        dockedPetButton.BeginAnimation(OpacityProperty, null);
        dockedPetButton.Opacity = 0;
        dockedPetButton.Visibility = Visibility.Collapsed;
        MainMenu.Padding = new Thickness(0, 0, 94, 0);
        return new(true, "已停靠", X: (int)target.X, Y: (int)target.Y);
    }
    private Button CreatePetButton()
    {
        var button = SessionTheme.Button("");
        button.Width = 28; button.Height = button.MinHeight = 24;
        button.Padding = new Thickness(2); button.Margin = new Thickness(0, 0, 62, 0);
        button.HorizontalAlignment = HorizontalAlignment.Right; button.VerticalAlignment = VerticalAlignment.Top;
        button.Visibility = Visibility.Collapsed;
        button.FocusVisualStyle = null; button.BorderThickness = new Thickness(0);
        button.Background = Brushes.Transparent;
        button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
 <Border Name='surface' Background='Transparent' CornerRadius='3'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='{DynamicResource MenuSeparator}'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        button.ToolTip = "桌宠 · 点击打开菜单，拖出工具栏恢复桌宠";
        button.Content = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Image/PetDock.png")),
            Stretch = Stretch.Uniform, Width = 18, Height = 18, Margin = new Thickness(0, 0, 0, 0) };
        Grid.SetColumnSpan(button, 3); Panel.SetZIndex(button, 60);
        var menu = new ContextMenu();
        menu.SetResourceReference(FrameworkElement.StyleProperty, "AlphaContextMenu");
        void Add(string title, RoutedEventHandler action)
        {
            var item = new MenuItem { Header = title };
            item.SetResourceReference(FrameworkElement.StyleProperty, "AlphaPopupMenuItem");
            item.Click += action; menu.Items.Add(item);
        }
        Add("新建 Edit + View", NewIndependentPair_Click);
        Add("多窗口控制", MultiControl_Click);
        Add("联机写谱", ChartRoom_Click);
        Add("恢复桌宠", async (_, _) => await UndockPet(button.PointToScreen(new Point(0, 28))));
        button.ContextMenu = menu;
        button.PreviewMouseLeftButtonDown += (_, e) => { petDragStart = e.GetPosition(button); petDragging = true; button.CaptureMouse(); e.Handled = true; };
        button.PreviewMouseMove += async (_, e) =>
        {
            if (!petDragging || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(button);
            if ((position - petDragStart).Length < 12) return;
            petDragging = false; button.ReleaseMouseCapture();
            await UndockPet(button.PointToScreen(position));
        };
        button.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!petDragging) return;
            petDragging = false; button.ReleaseMouseCapture(); menu.PlacementTarget = button; menu.IsOpen = true; e.Handled = true;
        };
        Closed += async (_, _) => { if (dockedPetPid != 0) await PetDock.Restore(dockedPetPid, dockedPetStart, -1e9, -1e9); };
        return button;
    }
    private async Task UndockPet(Point screen)
    {
        if (dockedPetButton == null || dockedPetPid == 0) return;
        dockedPetButton.Visibility = Visibility.Collapsed;
        if (!await PetDock.Restore(dockedPetPid, dockedPetStart, screen.X, screen.Y))
        {
            dockedPetButton.Visibility = Visibility.Visible;
            return;
        }
        dockedPetPid = 0;
        MainMenu.Padding = new Thickness(0, 0, 60, 0);
    }
}
