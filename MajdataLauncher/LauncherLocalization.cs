using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MajdataLauncher;

internal static class LauncherLocalization
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["LaunchEditor"] = "Launch editor",
        ["OpenLauncherFolder"] = "Open launcher folder",
        ["ExitPet"] = "Exit pet",
        ["Ready"] = "Idle",
        ["LaunchHint"] = "Right-click me to launch the editor!",
        ["Starting"] = "Starting",
        ["LaunchFailed"] = "Launch failed",
        ["AssetError"] = "Asset error",
        ["PetAtlasError"] = "The pet sprite atlas is missing or invalid.",
        ["ConnectionError"] = "Connection error",
        ["PetConnectionUnavailable"] = "Pet connection is unavailable: {0}",
        ["Reviewing"] = "Reviewing",
        ["Waiting"] = "Waiting",
        ["Recording"] = "Recording",
        ["Playing"] = "Playing",
        ["Charting"] = "Charting",
        ["SyntaxError"] = "Syntax error",
        ["Completed"] = "Completed",
        ["NeedsAttention"] = "This needs attention.",
        ["Complete"] = "Complete.",
        ["CheckingChart"] = "Checking the current chart…",
        ["CheckingStarCombo"] = "Checking the star combination…",
        ["OrganizingIdeas"] = "Organizing chart ideas…",
        ["ReadyToChart"] = "Ready to chart?",
        ["RememberSave"] = "Remember to save maidata.txt.",
        ["CheckBeforeExport"] = "Check the chart once more before exporting.",
        ["IdleWaveform"] = "Drag the waveform to inspect the section you just wrote.",
        ["IdleContextMenu"] = "Right-click the editor to insert measures quickly.",
        ["IdleView"] = "Click a judgment area in View to create a note.",
        ["IdleUndo"] = "Right-click in View to undo the previous action.",
        ["IdleStar"] = "How about trying a new star combination?",
        ["IdleSave"] = "Remember to save the chart occasionally.",
        ["PlayingChart"] = "Playing the chart…",
        ["ContinuingChart"] = "Continuing playback…",
        ["PlaybackPaused"] = "Playback paused",
        ["RecordingChart"] = "Recording video…",
        ["PreviewingNote"] = "Previewing a note",
        ["RefreshingDisplay"] = "Refreshing display settings",
        ["ViewAwake"] = "View is ready",
        ["WaitingCue"] = "Waiting for the next action…",
        ["ChartHasSyntaxErrors"] = "The chart has syntax errors that need attention.",
        ["ChartingMessage"] = "Editing the chart…",
        ["ActionUndone"] = "The previous action was undone.",
        ["MissingEdit"] = "MajdataEdit.exe was not found. Set editPath in launcher.json.",
        ["MissingView"] = "MajdataView.exe was not found and Unity Editor is not serving View. Set viewPath in launcher.json.",
        ["UnityViewDetected"] = "Unity Editor is serving View…",
        ["ViewAlreadyRunning"] = "View is already running…",
        ["StartingView"] = "Starting View…",
        ["WaitingForView"] = "Waiting for View…",
        ["ViewTimeout"] = "View has not opened its port yet…",
        ["UsingUnityView"] = "Using Unity Editor as View. Enter Play mode when needed…",
        ["StartingEdit"] = "Starting Edit…",
        ["ReadyWithUnity"] = "Edit is ready; Unity Editor is being used as View.",
        ["ReadyAll"] = "View and Edit are ready.",
        ["ViewPortTimeout"] = "View did not open port {0} within {1:0} seconds."
    };

    private static readonly IReadOnlyDictionary<string, string> Chinese = new Dictionary<string, string>
    {
        ["LaunchEditor"] = "启动编辑器", ["OpenLauncherFolder"] = "打开启动器目录", ["ExitPet"] = "退出桌宠",
        ["Ready"] = "待机", ["LaunchHint"] = "右键我可以启动编辑器！", ["Starting"] = "启动中",
        ["LaunchFailed"] = "启动失败", ["AssetError"] = "资源错误", ["PetAtlasError"] = "桌宠图集缺失或格式错误",
        ["ConnectionError"] = "连接错误", ["PetConnectionUnavailable"] = "桌宠连接不可用：{0}",
        ["Reviewing"] = "检查中", ["Waiting"] = "等待中", ["Recording"] = "录制中", ["Playing"] = "播放中",
        ["Charting"] = "写谱中", ["SyntaxError"] = "语法错误", ["Completed"] = "已完成",
        ["NeedsAttention"] = "这里需要处理一下。", ["Complete"] = "完成。", ["CheckingChart"] = "正在检查当前谱面……",
        ["CheckingStarCombo"] = "正在检查星星组合……", ["OrganizingIdeas"] = "正在整理写谱思路……",
        ["ReadyToChart"] = "准备写谱了吗？", ["RememberSave"] = "记得保存 maidata.txt",
        ["CheckBeforeExport"] = "导出前再检查一次谱面", ["IdleWaveform"] = "可以拖动波形图检查刚写好的段落。",
        ["IdleContextMenu"] = "右键文本框可以快速添加小节。", ["IdleView"] = "点击 View 的判定区可以生成音符。",
        ["IdleUndo"] = "在 View 中点击右键可以撤销上一步操作。", ["IdleStar"] = "要不要试试新的星星组合？",
        ["IdleSave"] = "记得偶尔保存一下谱面。", ["PlayingChart"] = "正在播放谱面……",
        ["ContinuingChart"] = "继续播放谱面……", ["PlaybackPaused"] = "播放已暂停",
        ["RecordingChart"] = "正在录制视频……", ["PreviewingNote"] = "正在预览音符",
        ["RefreshingDisplay"] = "正在刷新显示设置", ["ViewAwake"] = "View 已启动",
        ["WaitingCue"] = "正在等待下一步操作……", ["ChartHasSyntaxErrors"] = "谱面里有需要处理的语法错误。",
        ["ChartingMessage"] = "正在写谱……", ["ActionUndone"] = "已撤销上一步操作。",
        ["MissingEdit"] = "没有找到 MajdataEdit.exe，请在 launcher.json 中设置 editPath。",
        ["MissingView"] = "没有找到 MajdataView.exe，请在 launcher.json 中设置 viewPath。",
        ["UnityViewDetected"] = "检测到 Unity Editor 正在作为 View 运行……", ["ViewAlreadyRunning"] = "View 已经在运行……",
        ["StartingView"] = "正在启动 View……", ["WaitingForView"] = "正在等待 View 准备完成……",
        ["ViewTimeout"] = "View 端口仍未就绪……", ["UsingUnityView"] = "正在使用 Unity Editor 作为 View，需要时请进入播放模式……",
        ["StartingEdit"] = "正在启动 Edit……", ["ReadyWithUnity"] = "Edit 已准备完成，当前使用 Unity Editor 作为 View。",
        ["ReadyAll"] = "View 和 Edit 都已准备完成。", ["ViewPortTimeout"] = "View 在 {1:0} 秒内没有打开端口 {0}。"
    };

    private static readonly IReadOnlyDictionary<string, string> Japanese = new Dictionary<string, string>
    {
        ["LaunchEditor"] = "エディターを起動", ["OpenLauncherFolder"] = "ランチャーフォルダーを開く", ["ExitPet"] = "ペットを終了",
        ["Ready"] = "待機", ["LaunchHint"] = "右クリックでエディターを起動できます！", ["Starting"] = "起動中",
        ["LaunchFailed"] = "起動失敗", ["AssetError"] = "素材エラー", ["PetAtlasError"] = "ペットの画像が見つからないか、形式が正しくありません。",
        ["ConnectionError"] = "接続エラー", ["PetConnectionUnavailable"] = "ペット接続を利用できません：{0}",
        ["Reviewing"] = "確認中", ["Waiting"] = "待機中", ["Recording"] = "録画中", ["Playing"] = "再生中",
        ["Charting"] = "譜面編集中", ["SyntaxError"] = "構文エラー", ["Completed"] = "完了",
        ["NeedsAttention"] = "ここを修正してください。", ["Complete"] = "完了しました。", ["CheckingChart"] = "現在の譜面を確認中…",
        ["CheckingStarCombo"] = "星の組み合わせを確認中…", ["OrganizingIdeas"] = "譜面のアイデアを整理中…",
        ["ReadyToChart"] = "譜面を作りますか？", ["RememberSave"] = "maidata.txt の保存を忘れずに。",
        ["CheckBeforeExport"] = "書き出す前にもう一度譜面を確認しましょう。", ["IdleWaveform"] = "波形をドラッグして直前の部分を確認できます。",
        ["IdleContextMenu"] = "エディターを右クリックすると小節をすばやく追加できます。", ["IdleView"] = "View の判定エリアをクリックするとノーツを作成できます。",
        ["IdleUndo"] = "View で右クリックすると直前の操作を元に戻せます。", ["IdleStar"] = "新しい星の組み合わせを試してみませんか？",
        ["IdleSave"] = "譜面はこまめに保存しましょう。", ["PlayingChart"] = "譜面を再生中…",
        ["ContinuingChart"] = "再生を再開中…", ["PlaybackPaused"] = "一時停止中",
        ["RecordingChart"] = "動画を録画中…", ["PreviewingNote"] = "ノーツをプレビュー中",
        ["RefreshingDisplay"] = "表示設定を更新中", ["ViewAwake"] = "View の準備完了",
        ["WaitingCue"] = "次の操作を待っています…", ["ChartHasSyntaxErrors"] = "譜面に修正が必要な構文エラーがあります。",
        ["ChartingMessage"] = "譜面を編集中…", ["ActionUndone"] = "直前の操作を元に戻しました。",
        ["MissingEdit"] = "MajdataEdit.exe が見つかりません。launcher.json の editPath を設定してください。",
        ["MissingView"] = "MajdataView.exe が見つからず、Unity Editor も View を提供していません。launcher.json の viewPath を設定してください。",
        ["UnityViewDetected"] = "Unity Editor を View として使用します…", ["ViewAlreadyRunning"] = "View はすでに起動しています…",
        ["StartingView"] = "View を起動中…", ["WaitingForView"] = "View の準備を待っています…",
        ["ViewTimeout"] = "View のポートがまだ開いていません。Edit の起動を続行します…", ["UsingUnityView"] = "Unity Editor を View として使用します。必要に応じて Play モードにしてください…",
        ["StartingEdit"] = "Edit を起動中…", ["ReadyWithUnity"] = "Edit の準備が完了しました。Unity Editor を View として使用中です。",
        ["ReadyAll"] = "View と Edit の準備が完了しました。", ["ViewPortTimeout"] = "View は {1:0} 秒以内にポート {0} を開きませんでした。"
    };

    private static readonly IReadOnlyDictionary<string, string> Strings = SelectLanguage();

    internal static string Text(string key, params object[] args)
    {
        var format = Strings.TryGetValue(key, out var value)
            ? value
            : English.TryGetValue(key, out var fallback) ? fallback : key;
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }

    private static IReadOnlyDictionary<string, string> SelectLanguage()
    {
        var language = ReadEditorLanguage();
        if (string.IsNullOrWhiteSpace(language))
            language = CultureInfo.CurrentUICulture.Name;
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return Chinese;
        if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            return Japanese;
        return English;
    }

    private static string? ReadEditorLanguage()
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "App", "MajdataEdit", "EditorSetting.json"),
                Path.Combine(AppContext.BaseDirectory, "MajdataEdit", "EditorSetting.json"),
                Path.Combine(AppContext.BaseDirectory, "EditorSetting.json")
            };
            var path = candidates.FirstOrDefault(File.Exists);
            if (path == null)
                return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.TryGetProperty("Language", out var value) ? value.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
