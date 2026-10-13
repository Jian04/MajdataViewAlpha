using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MajdataCore
{
    // Shared by Unity, Edit and Launcher. No Unity or WPF dependencies.
    public sealed class AlphaSession
    {
        public string Id = "";
        public int Slot;
        public int ViewPort;
        public int EditPort;
        public int PetPort;
        public int EditPid;
        public long EditStart;
        public int ViewPid;
        public long ViewStart;
        public long Created;
        public string Label { get { return Slot.ToString("00"); } }
        public string ColorHex { get { return Colors[(Slot - 1) % Colors.Length]; } }
        private static readonly string[] Colors = { "#3DA5FF", "#C779EF", "#36C9A0", "#F3AA43", "#F57391", "#65CEDB", "#A7C957", "#B39DDB" };
        public string DirectoryPath { get { return Path.Combine(Root, Id); } }
        public static string Root
        {
            get
            {
#if ALPHA_SESSION_TESTS
                return Environment.GetEnvironmentVariable("MAJDATA_SESSION_TEST_ROOT");
#else
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MajdataAlpha", "Sessions");
#endif
            }
        }
        public static bool IsRegisteredProcess(int pid, bool view)
        {
            return List().Any(s => view ? s.ViewPid == pid && IsProcess(s.ViewPid, s.ViewStart)
                : s.EditPid == pid && IsProcess(s.EditPid, s.EditStart));
        }
        private static readonly Lazy<AlphaSession> current = new Lazy<AlphaSession>(() => FromArguments(Environment.GetCommandLineArgs()));
        private static AlphaSession automaticSession;
        public static AlphaSession Current { get { return automaticSession ?? current.Value; } }
        public static void EnsureEditorSession()
        {
            if (Current == null) automaticSession = Create(false);
        }
        public bool ShowIdentity { get { return File.Exists(Path.Combine(DirectoryPath, "identity")); } }
        public bool IsBackgroundWorker { get { return File.Exists(Path.Combine(DirectoryPath, "background-worker")); } }
        public static void EnableIdentities()
        {
            foreach (var session in List())
                File.WriteAllText(Path.Combine(session.DirectoryPath, "identity"), "1");
        }
        public static string ViewEndpoint { get { return "http://localhost:" + (Current == null ? 8013 : Current.ViewPort) + "/"; } }
        public static string EditEndpoint { get { return "http://127.0.0.1:" + (Current == null ? 8014 : Current.EditPort) + "/"; } }
        public static string PetEndpoint { get { return "http://127.0.0.1:" + (Current == null ? 8015 : Current.PetPort) + "/pet"; } }
        public static string FileName(string name)
        {
            return Current == null ? name : Path.GetFileNameWithoutExtension(name) + "-" + Current.Id + Path.GetExtension(name);
        }
        public static string ChartWorkFile(string chartFolder, string name)
        {
            var folder = Path.Combine(chartFolder, ".majdata");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, FileName(name));
        }
        public static string PipeName { get { return Current == null ? "majdataRec" : "majdataRec-" + Current.Id; } }
        public static string SettingsPath(string baseline)
        {
            if (Current == null) return baseline;
            Directory.CreateDirectory(Current.DirectoryPath);
            var path = Path.Combine(Current.DirectoryPath, Path.GetFileName(baseline));
            if (!File.Exists(path) && File.Exists(baseline)) File.Copy(baseline, path, false);
            return path;
        }
        public static AlphaSession FromArguments(string[] args)
        {
            var index = Array.IndexOf(args, "--alpha-session");
            if (index < 0) return null;
            Guid guid;
            if (index + 1 >= args.Length || !Guid.TryParseExact(args[index + 1], "N", out guid))
                throw new ArgumentException("Invalid --alpha-session identifier.");
            return Read(guid.ToString("N"));
        }
        public static AlphaSession Create(bool identified = true)
        {
            return WithRegistryLock(() =>
            {
                Directory.CreateDirectory(Root);
                var occupied = new HashSet<int>(List().Select(s => s.Slot));
                for (int slot = 1; slot <= 1000; slot++)
                {
                    if (occupied.Contains(slot)) continue;
                    int port = 18010 + slot * 3;
                    if (!PortsAvailable(port)) continue;
                    var session = new AlphaSession { Id = Guid.NewGuid().ToString("N"), Slot = slot,
                        ViewPort = port, EditPort = port + 1, PetPort = port + 2, Created = DateTime.UtcNow.Ticks };
                    Directory.CreateDirectory(session.DirectoryPath);
                    if (identified) File.WriteAllText(Path.Combine(session.DirectoryPath, "identity"), "1");
                    session.Write();
                    return session;
                }
                throw new IOException("No free Edit/View port group is available.");
            });
        }
        private static bool PortsAvailable(int port)
        {
            var listeners = new List<TcpListener>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var listener = new TcpListener(IPAddress.Loopback, port + i);
                    listener.Server.ExclusiveAddressUse = true;
                    listeners.Add(listener); listener.Start();
                }
                return true;
            }
            catch (SocketException) { return false; }
            finally { foreach (var listener in listeners) listener.Stop(); }
        }
        public static List<AlphaSession> List()
        {
            var result = new List<AlphaSession>();
            if (!Directory.Exists(Root)) return result;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                try
                {
                    var item = Read(Path.GetFileName(dir));
                    if (item.IsAlive || DateTime.UtcNow.Ticks - item.Created < TimeSpan.FromMinutes(2).Ticks)
                        result.Add(item);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException || e is ArgumentException) { }
            }
            return result.OrderBy(s => s.Slot).ToList();
        }
        public bool IsAlive { get { return IsProcess(EditPid, EditStart) || IsProcess(ViewPid, ViewStart); } }
        public void PublishViewState(bool recording, long generation, int dynamicMuri = 0, string[] details = null)
        {
            using (var process = Process.GetCurrentProcess())
            {
                var path = Path.Combine(DirectoryPath, "view-state.txt");
                var temporary = path + ".tmp";
                var values = new List<string> {
                    process.Id.ToString(), process.StartTime.ToUniversalTime().Ticks.ToString(),
                    DateTime.UtcNow.Ticks.ToString(), recording ? "1" : "0", generation.ToString(), dynamicMuri.ToString() };
                if (details != null) values.AddRange(details);
                File.WriteAllLines(temporary, values);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
        }
        public bool? ReadViewRecording()
        {
            long generation;
            return ReadViewRecording(out generation);
        }
        public bool? ReadViewRecording(out long generation)
        {
            generation = -1;
            try
            {
                var values = File.ReadAllLines(Path.Combine(DirectoryPath, "view-state.txt"));
                var latest = Read(Id);
                if (values.Length < 5 || int.Parse(values[0]) != latest.ViewPid || long.Parse(values[1]) != latest.ViewStart ||
                    !IsProcess(latest.ViewPid, latest.ViewStart) || DateTime.UtcNow.Ticks - long.Parse(values[2]) > TimeSpan.FromSeconds(5).Ticks)
                    return null;
                generation = long.Parse(values[4]);
                return values[3] == "1";
            }
            catch { return null; }
        }
        public int ReadDynamicMuri()
        {
            if (!ReadViewRecording().HasValue) return 0;
            try
            {
                var values = File.ReadAllLines(Path.Combine(DirectoryPath, "view-state.txt"));
                return values.Length >= 6 && int.TryParse(values[5], out var count) ? Math.Max(0, count) : 0;
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }
        public string[] ReadDynamicMuriDetails()
        {
            if (!ReadViewRecording().HasValue) return new string[0];
            try { return File.ReadAllLines(Path.Combine(DirectoryPath, "view-state.txt")).Skip(6).ToArray(); }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }
        public static bool IsProcess(int pid, long start)
        {
            if (pid <= 0 || start <= 0) return false;
            try { using (var p = Process.GetProcessById(pid)) return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == start; }
            catch { return false; }
        }
        public Process GetProcess(bool view)
        {
            var latest = Read(Id);
            int pid = view ? latest.ViewPid : latest.EditPid;
            long start = view ? latest.ViewStart : latest.EditStart;
            return IsProcess(pid, start) ? Process.GetProcessById(pid) : null;
        }
        public void Register(bool view, Process process)
        {
            WithRegistryLock(() =>
            {
                var latest = Read(Id);
                int oldPid = view ? latest.ViewPid : latest.EditPid;
                long oldStart = view ? latest.ViewStart : latest.EditStart;
                if (IsProcess(oldPid, oldStart) && oldPid != process.Id)
                    throw new InvalidOperationException("This group already has a " + (view ? "View" : "Edit") + " process.");
                if (view) { latest.ViewPid = process.Id; latest.ViewStart = process.StartTime.ToUniversalTime().Ticks; }
                else { latest.EditPid = process.Id; latest.EditStart = process.StartTime.ToUniversalTime().Ticks; }
                latest.Write();
                return true;
            });
        }
        public Process StartProcess(string executable, bool view, bool batchWorker = false, string playbackViewSession = null)
        {
            if (batchWorker && !view)
                File.WriteAllText(Path.Combine(DirectoryPath, "background-worker"), "1");
            var info = new ProcessStartInfo(executable, "--alpha-session " + Id + (batchWorker ? " --batch-worker" : "")) {
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false,
                CreateNoWindow = true, WindowStyle = batchWorker ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal };
            if (batchWorker && !view && playbackViewSession != null)
                info.EnvironmentVariables["MAJDATA_PLAYBACK_VIEW_SESSION"] = playbackViewSession;
            var process = Process.Start(info);
            if (process == null) throw new IOException("Unable to start " + executable);
            Register(view, process);
            return process;
        }
        public static AlphaSession Read(string id)
        {
            return WithRegistryLock(() => ReadUnlocked(id));
        }
        private static AlphaSession ReadUnlocked(string id)
        {
            Guid guid;
            if (!Guid.TryParseExact(id, "N", out guid)) throw new FormatException("Invalid group identifier.");
            var path = Path.Combine(Root, id, "session.txt");
            string[] values = null;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    values = File.ReadAllLines(path);
                    break;
                }
                catch (IOException) when (attempt < 5) { Thread.Sleep(20); }
                catch (UnauthorizedAccessException) when (attempt < 5) { Thread.Sleep(20); }
            }
            if (values.Length != 10) throw new FormatException("Invalid group manifest.");
            var s = new AlphaSession { Id = id, Slot = int.Parse(values[0]), ViewPort = int.Parse(values[1]),
                EditPort = int.Parse(values[2]), PetPort = int.Parse(values[3]), EditPid = int.Parse(values[4]),
                EditStart = long.Parse(values[5]), ViewPid = int.Parse(values[6]), ViewStart = long.Parse(values[7]), Created = long.Parse(values[8]) };
            if (values[9] != "1" || s.Slot < 1 || s.Slot > 1000 || s.ViewPort != 18010 + 3 * s.Slot || s.EditPort != s.ViewPort + 1 || s.PetPort != s.ViewPort + 2)
                throw new FormatException("Unsupported group manifest.");
            return s;
        }
        private void Write()
        {
            var path = Path.Combine(DirectoryPath, "session.txt");
            var temporary = path + ".tmp";
            File.WriteAllLines(temporary, new[] { Slot.ToString(CultureInfo.InvariantCulture), ViewPort.ToString(), EditPort.ToString(),
                PetPort.ToString(), EditPid.ToString(), EditStart.ToString(), ViewPid.ToString(), ViewStart.ToString(), Created.ToString(), "1" });
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        private static T WithRegistryLock<T>(Func<T> action)
        {
            using (var mutex = new Mutex(false, "Local\\MajdataAlpha.SessionRegistry"))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new TimeoutException("The instance registry is busy.");
                try { return action(); } finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
