using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MajdataEdit;

public partial class MainWindow
{
    private readonly Point? startupPresentationOrigin = ReadStartupOrigin();
    private bool startupPresentationStarted;

    private void StartupPresentationFailed(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // The application's existing handler reports the error. Reveal both windows
        // first so a failed chart load cannot leave an invisible editor running.
        RevealStartupEditor(); RestoreStartupView();
        startupPresentationStarted = startupViewAligned = true;
        Dispatcher.UnhandledException -= StartupPresentationFailed;
    }

    private static Point? ReadStartupOrigin()
    {
        var text = Environment.GetEnvironmentVariable("MAJDATA_STARTUP_ORIGIN");
        if (string.IsNullOrEmpty(text)) return null;
        var values = text.Split(';');
        if (values.Length != 2 ||
            !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !double.IsFinite(x) || !double.IsFinite(y))
            throw new FormatException("启动动画坐标无效：" + text);
        return new Point(x, y);
    }

    private void RestoreStartupView()
    {
        var handle = GetPairedViewWindow();
        if (handle != IntPtr.Zero && (startupPresentationOrigin == null || startupViewAligned)) StartupShowWindow(handle, 4);
        if (startupPresentationOrigin != null && !startupViewAligned && MajdataCore.AlphaSession.Current is { } session)
            File.WriteAllText(Path.Combine(session.DirectoryPath, "startup-show"), "1");
    }

    private void RevealStartupEditor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        Opacity = 1; StartupShowWindow(handle, 4);
        if (startupPresentationOrigin != null) StartupSetCloak(handle, false);
    }

    private static void StartupSetCloak(IntPtr handle, bool hidden)
    {
        var value = hidden ? 1 : 0;
        Marshal.ThrowExceptionForHR(StartupDwmSetAttribute(handle, 13, ref value, sizeof(int)));
    }

    private async void FinishStartupPresentation()
    {
        if (startupPresentationStarted) return;
        startupPresentationStarted = true;
        Window? ghost = null;
        Exception? failure = null;
        try
        {
            SendDisplaySettings(background: true);
            await displaySettingsSend.WaitAsync(TimeSpan.FromSeconds(15));
            if (startupPresentationOrigin == null) return;
            // DWM still cloaks the real window. Paint it before the handoff,
            // rather than changing opacity and doing its first paint at the end.
            Opacity = 1;
            var session = MajdataCore.AlphaSession.Current!;
            var framePath = Path.Combine(session.DirectoryPath, "startup-frame.png");
            File.WriteAllText(Path.Combine(session.DirectoryPath, "startup-capture"), "1");
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(framePath))
            {
                if (!IsLoaded) return;
                using var process = session.GetProcess(true);
                if (process == null || deadline.Elapsed.TotalSeconds > 15)
                    throw new IOException("View 未能提供启动画面，请查看 Player.log。");
                await Task.Delay(25);
            }
            TraceStartup("Entrance: prepared");
            var root = (FrameworkElement)Content;
            var dpi = VisualTreeHelper.GetDpi(root);
            var fromDevice = PresentationSource.FromVisual(this)!.CompositionTarget.TransformFromDevice;
            var editClientAt = fromDevice.Transform(root.PointToScreen(new Point()));
            var origin = fromDevice.Transform(startupPresentationOrigin.Value);
            var work = SystemParameters.WorkArea;
            var view = GetPairedViewWindow();
            GetClientRect(view, out var rect);
            var clientOrigin = new StartupPoint();
            if (!StartupClientToScreen(view, ref clientOrigin)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var viewClientAt = fromDevice.Transform(new Point(clientOrigin.X, clientOrigin.Y));
            var viewClientSize = fromDevice.Transform(new Point(rect.Right - rect.Left, rect.Bottom - rect.Top));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(root); bitmap.Freeze();
            var preview = new BitmapImage();
            using (var input = File.OpenRead(framePath))
            { preview.BeginInit(); preview.CacheOption = BitmapCacheOption.OnLoad; preview.StreamSource = input; preview.EndInit(); preview.Freeze(); }
            var editorImage = CreateStartupFrame(bitmap, new WindowInteropHelper(this).Handle, editClientAt,
                new Size(root.ActualWidth, root.ActualHeight), fromDevice, Title, out var editAt);
            var title = new System.Text.StringBuilder(1024); StartupGetWindowText(view, title, title.Capacity);
            var viewImage = CreateStartupFrame(preview, view, viewClientAt,
                new Size(viewClientSize.X, viewClientSize.Y), fromDevice, title.ToString(), out var viewAt);
            var editSize = new Point(editorImage.Width, editorImage.Height);
            var viewSize = new Point(viewImage.Width, viewImage.Height);
            var center = new Point(work.Left + (work.Width - editSize.X) / 2, editAt.Y);
            const double rebound = 2.4;
            // Reserve the BackEase maximum as well as the final positions.
            // Without this margin the window edges are cropped during recoil.
            var overshoot = 4 * Math.Pow(rebound, 3) / (27 * Math.Pow(rebound + 1, 2));
            var recoilMargin = Math.Ceiling(overshoot * Math.Max(Math.Abs(center.X - viewAt.X), Math.Abs(center.X - editAt.X)));
            var left = Math.Min(center.X, Math.Min(viewAt.X, origin.X - editSize.X / 2)) - recoilMargin;
            var top = Math.Min(viewAt.Y, Math.Min(center.Y, Math.Min(editAt.Y, origin.Y - editSize.Y / 2)));
            var canvas = new Canvas { Width = Math.Max(center.X + editSize.X, Math.Max(viewAt.X + viewSize.X, Math.Max(editAt.X + editSize.X, origin.X + editSize.X / 2))) + recoilMargin - left,
                Height = Math.Max(center.Y + editSize.Y, Math.Max(viewAt.Y + viewSize.Y, Math.Max(editAt.Y + editSize.Y, origin.Y + editSize.Y / 2))) - top };
            editorImage.RenderTransformOrigin = new Point(.5, .5); editorImage.Opacity = 0;
            var scale = new ScaleTransform(.08, .08);
            var editMove = new TranslateTransform(origin.X - editSize.X / 2 - editAt.X, origin.Y - editSize.Y / 2 - editAt.Y);
            var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(editMove);
            editorImage.RenderTransform = transform;
            Canvas.SetLeft(editorImage, editAt.X - left); Canvas.SetTop(editorImage, editAt.Y - top);
            var viewMove = new TranslateTransform(center.X - viewAt.X, 0);
            viewImage.Opacity = 0; viewImage.RenderTransform = viewMove;
            Canvas.SetLeft(viewImage, viewAt.X - left); Canvas.SetTop(viewImage, viewAt.Y - top);
            canvas.Children.Add(viewImage); canvas.Children.Add(editorImage);
            ghost = new Window { Content = canvas, AllowsTransparency = true, WindowStyle = WindowStyle.None,
                Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false, IsHitTestVisible = false,
                ResizeMode = ResizeMode.NoResize, Left = left, Top = top, Width = canvas.Width, Height = canvas.Height };
            ghost.Show();
            TraceStartup("Entrance: Edit from pet");
            var startX = editMove.X; var startY = editMove.Y;
            await AnimateStartup(.76, t =>
            {
                editorImage.Opacity = 1;
                // Finish the arrival before the short pause at the center.
                var arrival = Math.Min(1, t * .76 / .48);
                var progress = arrival * arrival * (3 - 2 * arrival);
                scale.ScaleX = scale.ScaleY = .08 + .92 * progress;
                editMove.X = startX + (center.X - editAt.X - startX) * progress;
                editMove.Y = startY + (center.Y - editAt.Y - startY) * progress;
            });
            TraceStartup("Entrance: View from Edit");
            var squeeze = viewSize.X * .1 / (center.X - viewAt.X);
            await AnimateStartup(.62, t =>
            {
                // Squeeze out the first tenth, then release with a small rebound.
                var flight = Math.Clamp((t - .35) / .65, 0, 1);
                var back = 1 + (rebound + 1) * Math.Pow(flight - 1, 3) + rebound * Math.Pow(flight - 1, 2);
                var progress = t < .35 ? squeeze * Math.Pow(t / .35, 2) : squeeze + (1 - squeeze) * back;
                viewMove.X = (center.X - viewAt.X) * (1 - progress);
                viewImage.Opacity = Math.Min(1, t * 10);
                var retreat = t < .35 ? 0 : back;
                editMove.X = (center.X - editAt.X) * (1 - retreat);
                editMove.Y = 0;
            });
            File.Delete(framePath); File.Delete(Path.Combine(session.DirectoryPath, "startup-capture"));
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                // Both real windows are at their final positions beneath the
                // overlay. Prepare their visible frames together, not in sequence.
                RevealStartupEditor();
                var editorFrame = ghost == null ? Task.CompletedTask : WaitForStartupFrame();
                RestoreStartupView();
                if (startupPresentationOrigin != null && GetPairedViewWindow() != IntPtr.Zero)
                {
                    var showRequest = Path.Combine(MajdataCore.AlphaSession.Current!.DirectoryPath, "startup-show");
                    var reveal = Stopwatch.StartNew();
                    while (File.Exists(showRequest))
                    {
                        if (reveal.Elapsed.TotalSeconds > 3) throw new IOException("View 未能恢复启动窗口，请查看 Player.log。");
                        await Task.Delay(10);
                    }
                }
                await editorFrame;
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            finally
            {
                ghost?.Close();
                startupViewAligned = true;
                Dispatcher.UnhandledException -= StartupPresentationFailed;
                TraceStartup("Entrance: complete, windows restored");
                QueueNotePreview();
            }
        }
        if (failure != null && IsLoaded)
            MessageBox.Show(this, failure.Message, "启动动画", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private Grid CreateStartupFrame(ImageSource image, IntPtr handle, Point clientAt, Size clientSize,
        Matrix fromDevice, string title, out Point frameAt)
    {
        Marshal.ThrowExceptionForHR(StartupGetFrameBounds(handle, 9, out var bounds, Marshal.SizeOf<StartupFrameRect>()));
        frameAt = fromDevice.Transform(new Point(bounds.Left, bounds.Top));
        var end = fromDevice.Transform(new Point(bounds.Right, bounds.Bottom));
        var inset = clientAt - frameAt;
        var frame = new Grid { Width = end.X - frameAt.X, Height = end.Y - frameAt.Y, ClipToBounds = true };
        if (!StartupGetWindowRect(handle, out var windowBounds)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var native = new System.Drawing.Bitmap(windowBounds.Right-windowBounds.Left, windowBounds.Bottom-windowBounds.Top);
        using (var graphics = System.Drawing.Graphics.FromImage(native))
        {
            var dc = graphics.GetHdc();
            try { if (!StartupPrintWindow(handle, dc, 2)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
            finally { graphics.ReleaseHdc(dc); }
        }
        var bitmapHandle = native.GetHbitmap();
        BitmapSource chrome;
        try { chrome = Imaging.CreateBitmapSourceFromHBitmap(bitmapHandle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); chrome.Freeze(); }
        finally { StartupDeleteObject(bitmapHandle); }
        var nativeAt = fromDevice.Transform(new Point(windowBounds.Left, windowBounds.Top));
        var nativeSize = fromDevice.Transform(new Point(native.Width, native.Height));
        var chromeImage = new Image { Source = chrome, Width = nativeSize.X, Height = nativeSize.Y,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(nativeAt.X-frameAt.X, nativeAt.Y-frameAt.Y, 0, 0) };
        frame.Children.Add(chromeImage);
        if (Environment.OSVersion.Version.Build >= 22000)
            frame.Clip = new RectangleGeometry(new Rect(0, 0, frame.Width, frame.Height), 8, 8);
        var content = new Image { Source = image, Width = clientSize.Width, Height = clientSize.Height,
            Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Math.Max(0, inset.X), Math.Max(0, inset.Y), 0, 0) };
        frame.Children.Add(content);
        return frame;
    }

    private static async Task WaitForStartupFrame()
    {
        var done = new TaskCompletionSource<bool>();
        var submitted = false;
        var previous = TimeSpan.MinValue;
        EventHandler frame = (_, args) =>
        {
            var time = ((RenderingEventArgs)args).RenderingTime;
            if (time == previous) return;
            previous = time;
            if (submitted) done.TrySetResult(true);
            submitted = true;
        };
        CompositionTarget.Rendering += frame;
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { CompositionTarget.Rendering -= frame; }
    }

    private async Task AnimateStartup(double seconds, Action<double> frame)
    {
        var done = new TaskCompletionSource<bool>();
        var clock = new Stopwatch();
        var frames = 0; var previous = 0d; var maximumGap = 0d;
        var lastRenderingTime = TimeSpan.MinValue;
        EventHandler? render = null;
        render = (_, args) =>
        {
            try
            {
                if (args is RenderingEventArgs rendering)
                {
                    if (rendering.RenderingTime == lastRenderingTime) return;
                    lastRenderingTime = rendering.RenderingTime;
                }
                if (!clock.IsRunning) clock.Start();
                var time = Math.Min(1, clock.Elapsed.TotalSeconds / seconds);
                var elapsed = clock.Elapsed.TotalMilliseconds;
                if (frames > 0) maximumGap = Math.Max(maximumGap, elapsed - previous);
                previous = elapsed; frames++;
                frame(time);
                if (time >= 1 || !IsLoaded) done.TrySetResult(true);
            }
            catch (Exception error) { done.TrySetException(error); }
        };
        CompositionTarget.Rendering += render;
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(seconds + 3)); }
        finally { CompositionTarget.Rendering -= render; }
        TraceStartup($"Entrance animation: {frames} frames, max gap {maximumGap:0.0}ms");
    }

    [StructLayout(LayoutKind.Sequential)] private struct StartupPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupFrameRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    private static extern bool StartupGetWindowRect(IntPtr window, out StartupFrameRect rect);
    [DllImport("user32.dll", EntryPoint = "PrintWindow", SetLastError = true)]
    private static extern bool StartupPrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    private static extern bool StartupDeleteObject(IntPtr handle);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int StartupGetFrameBounds(IntPtr handle, int attribute, out StartupFrameRect value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int StartupGetWindowText(IntPtr handle, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll", EntryPoint = "ClientToScreen", SetLastError = true)]
    private static extern bool StartupClientToScreen(IntPtr window, ref StartupPoint point);
    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool StartupShowWindow(IntPtr window, int command);
    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int StartupDwmSetAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint StartupWindowProcessId(IntPtr window, out uint processId);
}
