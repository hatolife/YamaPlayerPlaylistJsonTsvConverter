#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class YamaPlayerPlaylistJsonTsvConverterWindow : EditorWindow
{
    private const string WindowTitle = "YamaPlayer JSON <-> TSV";
    private const string MenuPath = "Tools/PoppoWorks/YamaPlayer Playlist Json TSV Converter";
    private const string PrefKeyInputJsonPath = "PoppoWorks.YamaPlayerJsonTsv.InputJsonPath";
    private const string PrefKeyOutputTsvPath = "PoppoWorks.YamaPlayerJsonTsv.OutputTsvPath";
    private const string PrefKeyOutputJsonPath = "PoppoWorks.YamaPlayerJsonTsv.OutputJsonPath";
    private const int ProgressUpdateInterval = 100;

    private static readonly string[] ExpectedHeader =
    {
        "playlist_name",
        "playlist_active",
        "youtube_list_id",
        "playlist_is_edit",
        "track_mode",
        "track_title",
        "track_url",
    };

    [Serializable]
    private class PlaylistRoot
    {
        public List<PlaylistData> playlists = new List<PlaylistData>();
    }

    [Serializable]
    private class PlaylistData
    {
        public bool Active = true;
        public string Name = string.Empty;
        public List<TrackData> Tracks = new List<TrackData>();
        public string YoutubeListId = string.Empty;
        public bool IsEdit;
    }

    [Serializable]
    private class TrackData
    {
        public int Mode = 1;
        public string Title = string.Empty;
        public string Url = string.Empty;
    }

    private sealed class ConversionReport
    {
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public bool HasError
        {
            get { return Errors.Count > 0; }
        }
    }

    private sealed class UrlNormalizationResult
    {
        public string Value;
        public bool Changed;
        public bool LooksLikeHttpUrl;
    }

    private sealed class PlaylistBuilder
    {
        public bool Initialized;
        public string Name;
        public bool Active;
        public string YoutubeListId;
        public bool IsEdit;
        public bool HasZeroTrackMarker;
        public readonly List<TrackData> Tracks = new List<TrackData>();
    }

    private sealed class DiffSummary
    {
        public int AddedPlaylists;
        public int RemovedPlaylists;
        public int ChangedPlaylistMeta;
        public int AddedTracks;
        public int RemovedTracks;
        public int ChangedTracks;
        public readonly List<string> Details = new List<string>();

        public bool HasAnyChange
        {
            get
            {
                return AddedPlaylists > 0
                       || RemovedPlaylists > 0
                       || ChangedPlaylistMeta > 0
                       || AddedTracks > 0
                       || RemovedTracks > 0
                       || ChangedTracks > 0;
            }
        }
    }

    private string _inputJsonPath = string.Empty;
    private string _outputTsvPath = string.Empty;
    private string _outputJsonPath = string.Empty;
    private Vector2 _logScroll;
    private readonly List<string> _latestLogLines = new List<string>();

    [MenuItem(MenuPath)]
    public static void OpenWindow()
    {
        YamaPlayerPlaylistJsonTsvConverterWindow window = GetWindow<YamaPlayerPlaylistJsonTsvConverterWindow>(WindowTitle);
        window.Show();
    }

    private void OnEnable()
    {
        minSize = Vector2.zero;
        _inputJsonPath = EditorPrefs.GetString(PrefKeyInputJsonPath, string.Empty);
        _outputTsvPath = EditorPrefs.GetString(PrefKeyOutputTsvPath, string.Empty);
        _outputJsonPath = NormalizeOutputJsonPath(EditorPrefs.GetString(PrefKeyOutputJsonPath, string.Empty));
    }

    private void OnDisable()
    {
        EditorPrefs.SetString(PrefKeyInputJsonPath, _inputJsonPath ?? string.Empty);
        EditorPrefs.SetString(PrefKeyOutputTsvPath, _outputTsvPath ?? string.Empty);
        EditorPrefs.SetString(PrefKeyOutputJsonPath, _outputJsonPath ?? string.Empty);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("YamaPlayer Playlist JSON <-> TSV Converter", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        DrawPathSection("Input JSON", ref _inputJsonPath, SelectInputJsonPath);
        DrawPathSection("Output TSV", ref _outputTsvPath, SelectOutputTsvPath);
        DrawPathSection("Output JSON", ref _outputJsonPath, SelectOutputJsonPath);

        EditorGUILayout.Space(8f);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_inputJsonPath) || !File.Exists(_inputJsonPath) || string.IsNullOrEmpty(_outputTsvPath)))
            {
                if (GUILayout.Button("JSON -> TSV", GUILayout.Height(30f)))
                {
                    ExecuteJsonToTsv();
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_outputTsvPath) || !File.Exists(_outputTsvPath) || string.IsNullOrEmpty(_outputJsonPath)))
            {
                if (GUILayout.Button("TSV -> JSON", GUILayout.Height(30f)))
                {
                    ExecuteTsvToJson();
                }
            }
        }

        EditorGUILayout.Space(8f);
        DrawWinMergeSection();

        EditorGUILayout.Space(10f);
        DrawLogSection();
    }

    private void DrawWinMergeSection()
    {
        EditorGUILayout.LabelField("Diff Tool (WinMerge)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!CanRunWinMergeDiff()))
            {
                if (GUILayout.Button("Diff with WinMerge", GUILayout.Height(24f)))
                {
                    OpenDiffWithWinMerge();
                }
            }

            using (new EditorGUI.DisabledScope(!IsWindows()))
            {
                if (GUILayout.Button("Install WinMerge (winget)", GUILayout.Height(24f)))
                {
                    InstallWinMergeViaWinget();
                }
            }
        }

        if (!IsWindows())
        {
            EditorGUILayout.HelpBox("WinMerge integration is available on Windows only.", MessageType.Info);
        }
    }

    private void DrawPathSection(string label, ref string path, Action selector)
    {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            path = EditorGUILayout.TextField(path ?? string.Empty);

            if (GUILayout.Button("Select", GUILayout.Width(70f)))
            {
                selector();
            }

            if (label == "Output JSON")
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_inputJsonPath)))
                {
                    if (GUILayout.Button("Input +1", GUILayout.Width(80f)))
                    {
                        SetOutputJsonPathByInputIncrement();
                    }
                }
            }
        }
    }

    private void SetOutputJsonPathByInputIncrement()
    {
        if (string.IsNullOrEmpty(_inputJsonPath))
        {
            _latestLogLines.Add("[Output JSON]");
            _latestLogLines.Add("- Input JSON が未設定のため、Input +1 を実行できません。");
            _latestLogLines.Add(string.Empty);
            return;
        }

        string dir = Path.GetDirectoryName(_inputJsonPath) ?? Application.dataPath;
        string fileName = Path.GetFileNameWithoutExtension(_inputJsonPath);

        System.Text.RegularExpressions.Match m = Regex.Match(fileName, @"^(.*)_([0-9]+)$");
        string nextName;
        if (m.Success)
        {
            int n;
            if (int.TryParse(m.Groups[2].Value, out n))
            {
                nextName = m.Groups[1].Value + "_" + (n + 1);
            }
            else
            {
                nextName = fileName + "_1";
            }
        }
        else
        {
            nextName = fileName + "_1";
        }

        _outputJsonPath = NormalizeOutputJsonPath(Path.Combine(dir, nextName + ".json"));
    }

    private void DrawLogSection()
    {
        EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            if (_latestLogLines.Count == 0)
            {
                EditorGUILayout.HelpBox("実行結果はここに表示されます。", MessageType.Info);
                return;
            }

            string logText = string.Join(Environment.NewLine, _latestLogLines);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.MinHeight(240f));
            float textHeight = Mathf.Max(220f, EditorStyles.textArea.CalcHeight(new GUIContent(logText), EditorGUIUtility.currentViewWidth - 48f));
            EditorGUILayout.SelectableLabel(logText, EditorStyles.textArea, GUILayout.MinHeight(textHeight), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }

    private void SelectInputJsonPath()
    {
        string baseDir = GetInitialDirectory(_inputJsonPath);
        string selected = EditorUtility.OpenFilePanel("Select JSON", baseDir, "json");
        if (!string.IsNullOrEmpty(selected))
        {
            _inputJsonPath = selected;
            if (string.IsNullOrEmpty(_outputTsvPath))
            {
                _outputTsvPath = Path.ChangeExtension(_inputJsonPath, ".tsv");
            }

            if (string.IsNullOrEmpty(_outputJsonPath))
            {
                _outputJsonPath = NormalizeOutputJsonPath(Path.Combine(
                    Path.GetDirectoryName(_inputJsonPath) ?? Application.dataPath,
                    Path.GetFileNameWithoutExtension(_inputJsonPath) + ".converted.json"));
            }
        }
    }

    private void SelectOutputTsvPath()
    {
        string current = string.IsNullOrEmpty(_outputTsvPath) ? "playlists.tsv" : Path.GetFileName(_outputTsvPath);
        string baseDir = GetInitialDirectory(_outputTsvPath);
        string selected = EditorUtility.SaveFilePanel("Select Output TSV", baseDir, current, "tsv");
        if (!string.IsNullOrEmpty(selected))
        {
            _outputTsvPath = selected;
        }
    }

    private void SelectOutputJsonPath()
    {
        string current = string.IsNullOrEmpty(_outputJsonPath) ? "playlists.converted.json" : Path.GetFileName(_outputJsonPath);
        string baseDir = GetInitialDirectory(_outputJsonPath);
        string selected = EditorUtility.SaveFilePanel("Select Output JSON", baseDir, current, "json");
        if (!string.IsNullOrEmpty(selected))
        {
            _outputJsonPath = NormalizeOutputJsonPath(selected);
        }
    }

    private static string NormalizeOutputJsonPath(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }

    private static string GetInitialDirectory(string currentPath)
    {
        if (!string.IsNullOrEmpty(currentPath))
        {
            if (Directory.Exists(currentPath))
            {
                return currentPath;
            }

            string parent = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                return parent;
            }
        }

        return Application.dataPath;
    }

    private bool CanRunWinMergeDiff()
    {
        return IsWindows()
               && !string.IsNullOrEmpty(_inputJsonPath)
               && File.Exists(_inputJsonPath)
               && !string.IsNullOrEmpty(_outputJsonPath)
               && File.Exists(_outputJsonPath);
    }

    private static bool IsWindows()
    {
        return Application.platform == RuntimePlatform.WindowsEditor;
    }

    private void OpenDiffWithWinMerge()
    {
        if (!CanRunWinMergeDiff())
        {
            _latestLogLines.Add("[WinMerge]");
            _latestLogLines.Add("- Input JSON / Output JSON の両方が必要です。");
            _latestLogLines.Add(string.Empty);
            return;
        }

        string winMergeExe = FindWinMergeExecutable();
        if (string.IsNullOrEmpty(winMergeExe))
        {
            _latestLogLines.Add("[WinMerge]");
            _latestLogLines.Add("- WinMerge が見つかりません。");
            _latestLogLines.Add("- 公式サイト: https://winmerge.org/?lang=ja");
            _latestLogLines.Add("- このウィンドウの「Install WinMerge (winget)」ボタンからインストールできます。");
            _latestLogLines.Add("- 公式サイトから手動インストールしても問題ありません。");
            _latestLogLines.Add(string.Empty);
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = winMergeExe;
            startInfo.Arguments = "\"" + _inputJsonPath + "\" \"" + _outputJsonPath + "\"";
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);

            _latestLogLines.Add("[WinMerge]");
            _latestLogLines.Add("- WinMerge を起動しました。");
            _latestLogLines.Add("- Left : " + _inputJsonPath);
            _latestLogLines.Add("- Right: " + _outputJsonPath);
            _latestLogLines.Add(string.Empty);
        }
        catch (Exception ex)
        {
            _latestLogLines.Add("[WinMerge]");
            _latestLogLines.Add("- 起動失敗: " + ex.Message);
            _latestLogLines.Add(string.Empty);
        }
    }

    private void InstallWinMergeViaWinget()
    {
        if (!IsWindows())
        {
            return;
        }

        int choice = EditorUtility.DisplayDialogComplex(
            "WinMerge インストール確認",
            "WinMerge を winget でインストールします。\n\n" +
            "- 外部コマンド（winget）を実行します\n" +
            "- 環境によっては管理者権限の確認（UAC）が表示されます\n\n" +
            "公式サイト: https://winmerge.org/?lang=ja\n" +
            "公式サイトから手動でインストールすることもできます。\n\n" +
            "インストールしますか？",
            "キャンセル",
            "公式サイトを開く",
            "インストール");

        // 0: キャンセル(左), 1: 公式サイトを開く(中央), 2: インストール(右)
        if (choice == 0)
        {
            _latestLogLines.Add("[WinMerge]");
            _latestLogLines.Add("- インストールはキャンセルされました。");
            _latestLogLines.Add(string.Empty);
            return;
        }

        if (choice == 1)
        {
            Application.OpenURL("https://winmerge.org/?lang=ja");
            _latestLogLines.Add("[WinMerge]");
            _latestLogLines.Add("- 公式サイトをブラウザで開きました。手動インストールを続けてください。");
            _latestLogLines.Add(string.Empty);
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "winget";
            startInfo.Arguments = "install --id WinMerge.WinMerge -e --accept-package-agreements --accept-source-agreements";
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);

            _latestLogLines.Add("[WinMerge]");
            _latestLogLines.Add("- winget install を開始しました。完了後に Diff with WinMerge を実行してください。");
            _latestLogLines.Add(string.Empty);
        }
        catch (Exception ex)
        {
            _latestLogLines.Add("[WinMerge]");
            _latestLogLines.Add("- winget 起動失敗: " + ex.Message);
            _latestLogLines.Add(string.Empty);
        }
    }

    private static string FindWinMergeExecutable()
    {
        string[] candidates =
        {
            @"C:\Program Files\WinMerge\WinMergeU.exe",
            @"C:\Program Files (x86)\WinMerge\WinMergeU.exe",
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            if (File.Exists(candidates[i]))
            {
                return candidates[i];
            }
        }

        try
        {
            ProcessStartInfo whereInfo = new ProcessStartInfo();
            whereInfo.FileName = "where";
            whereInfo.Arguments = "WinMergeU.exe";
            whereInfo.RedirectStandardOutput = true;
            whereInfo.RedirectStandardError = true;
            whereInfo.UseShellExecute = false;
            whereInfo.CreateNoWindow = true;

            using (Process process = Process.Start(whereInfo))
            {
                if (process == null)
                {
                    return string.Empty;
                }

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0 && File.Exists(lines[0]))
                    {
                        return lines[0];
                    }
                }
            }
        }
        catch
        {
            // Ignore lookup failures and return empty.
        }

        return string.Empty;
    }

    private void ExecuteJsonToTsv()
    {
        ConversionReport report = new ConversionReport();
        _latestLogLines.Clear();

        try
        {
            string jsonText = File.ReadAllText(_inputJsonPath, Encoding.UTF8);
            PlaylistRoot root = JsonUtility.FromJson<PlaylistRoot>(jsonText);
            if (root == null)
            {
                report.Errors.Add("JSON parse failed: root is null.");
            }
            else if (root.playlists == null)
            {
                root.playlists = new List<PlaylistData>();
            }

            if (!report.HasError)
            {
                string tsvText = BuildTsv(root, report);
                File.WriteAllText(_outputTsvPath, tsvText, new UTF8Encoding(false));
                AssetDatabase.Refresh();
                AppendSummary(
                    direction: "JSON -> TSV",
                    outputPath: _outputTsvPath,
                    warningCount: report.Warnings.Count,
                    errorCount: report.Errors.Count,
                    additional: "変換完了");
            }
        }
        catch (OperationCanceledException)
        {
            report.Warnings.Add("JSON -> TSV was canceled by user.");
            AppendSummary(
                direction: "JSON -> TSV",
                outputPath: _outputTsvPath,
                warningCount: report.Warnings.Count,
                errorCount: report.Errors.Count,
                additional: "キャンセル");
        }
        catch (Exception ex)
        {
            report.Errors.Add("Exception: " + ex.Message);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AppendReportDetails(report);
        ShowResultDialog("JSON -> TSV", report);
    }

    private void ExecuteTsvToJson()
    {
        ConversionReport report = new ConversionReport();
        _latestLogLines.Clear();

        try
        {
            string tsvText = File.ReadAllText(_outputTsvPath, Encoding.UTF8);
            PlaylistRoot root = ParseTsv(tsvText, report);

            if (!report.HasError)
            {
                string jsonText = JsonUtility.ToJson(root, true);
                File.WriteAllText(_outputJsonPath, jsonText, new UTF8Encoding(false));
                AssetDatabase.Refresh();
                AppendInputOutputDiff(root, report);
                AppendSummary(
                    direction: "TSV -> JSON",
                    outputPath: _outputJsonPath,
                    warningCount: report.Warnings.Count,
                    errorCount: report.Errors.Count,
                    additional: "変換完了");
            }
        }
        catch (OperationCanceledException)
        {
            report.Warnings.Add("TSV -> JSON was canceled by user.");
            AppendSummary(
                direction: "TSV -> JSON",
                outputPath: _outputJsonPath,
                warningCount: report.Warnings.Count,
                errorCount: report.Errors.Count,
                additional: "キャンセル");
        }
        catch (Exception ex)
        {
            report.Errors.Add("Exception: " + ex.Message);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AppendReportDetails(report);
        ShowResultDialog("TSV -> JSON", report);
    }

    private void AppendInputOutputDiff(PlaylistRoot outputRoot, ConversionReport report)
    {
        if (string.IsNullOrEmpty(_inputJsonPath) || !File.Exists(_inputJsonPath))
        {
            _latestLogLines.Add("[Diff]");
            _latestLogLines.Add("- Input JSON が未指定または存在しないため差分比較をスキップしました。");
            _latestLogLines.Add(string.Empty);
            return;
        }

        PlaylistRoot inputRoot = null;
        try
        {
            string inputJsonText = File.ReadAllText(_inputJsonPath, Encoding.UTF8);
            inputRoot = JsonUtility.FromJson<PlaylistRoot>(inputJsonText);
        }
        catch (Exception ex)
        {
            report.Warnings.Add("Diff compare skipped: failed to read/parse input json: " + ex.Message);
        }

        if (inputRoot == null)
        {
            _latestLogLines.Add("[Diff]");
            _latestLogLines.Add("- Input JSON の読み込みに失敗したため差分比較をスキップしました。");
            _latestLogLines.Add(string.Empty);
            return;
        }

        DiffSummary diff = ComparePlaylistRoots(inputRoot, outputRoot);
        _latestLogLines.Add("[Diff]");
        _latestLogLines.Add("Input:  " + _inputJsonPath);
        _latestLogLines.Add("Output: " + _outputJsonPath);
        _latestLogLines.Add("Added playlists: " + diff.AddedPlaylists);
        _latestLogLines.Add("Removed playlists: " + diff.RemovedPlaylists);
        _latestLogLines.Add("Changed playlist meta: " + diff.ChangedPlaylistMeta);
        _latestLogLines.Add("Added tracks: " + diff.AddedTracks);
        _latestLogLines.Add("Removed tracks: " + diff.RemovedTracks);
        _latestLogLines.Add("Changed tracks: " + diff.ChangedTracks);

        if (!diff.HasAnyChange)
        {
            _latestLogLines.Add("No structural/content differences detected.");
        }
        else
        {
            int detailCount = Mathf.Min(200, diff.Details.Count);
            _latestLogLines.Add("Details (" + detailCount + "/" + diff.Details.Count + "):");
            for (int i = 0; i < detailCount; i++)
            {
                _latestLogLines.Add("- " + diff.Details[i]);
            }

            if (diff.Details.Count > detailCount)
            {
                _latestLogLines.Add("- ... truncated ...");
            }
        }

        _latestLogLines.Add(string.Empty);
    }

    private static DiffSummary ComparePlaylistRoots(PlaylistRoot inputRoot, PlaylistRoot outputRoot)
    {
        DiffSummary diff = new DiffSummary();

        List<PlaylistData> inPlaylists = inputRoot.playlists ?? new List<PlaylistData>();
        List<PlaylistData> outPlaylists = outputRoot.playlists ?? new List<PlaylistData>();
        int maxPlaylistCount = Mathf.Max(inPlaylists.Count, outPlaylists.Count);

        for (int playlistIndex = 0; playlistIndex < maxPlaylistCount; playlistIndex++)
        {
            bool hasIn = playlistIndex < inPlaylists.Count;
            bool hasOut = playlistIndex < outPlaylists.Count;

            if (!hasIn && hasOut)
            {
                diff.AddedPlaylists++;
                diff.Details.Add(string.Format("playlist[{0}] added.", playlistIndex));
                diff.AddedTracks += (outPlaylists[playlistIndex]?.Tracks?.Count ?? 0);
                continue;
            }

            if (hasIn && !hasOut)
            {
                diff.RemovedPlaylists++;
                diff.Details.Add(string.Format("playlist[{0}] removed.", playlistIndex));
                diff.RemovedTracks += (inPlaylists[playlistIndex]?.Tracks?.Count ?? 0);
                continue;
            }

            PlaylistData inPlaylist = inPlaylists[playlistIndex] ?? new PlaylistData();
            PlaylistData outPlaylist = outPlaylists[playlistIndex] ?? new PlaylistData();

            if (inPlaylist.Active != outPlaylist.Active)
            {
                diff.ChangedPlaylistMeta++;
                diff.Details.Add(string.Format("playlist[{0}].Active: {1} -> {2}", playlistIndex, inPlaylist.Active, outPlaylist.Active));
            }

            if (!string.Equals(inPlaylist.Name ?? string.Empty, outPlaylist.Name ?? string.Empty, StringComparison.Ordinal))
            {
                diff.ChangedPlaylistMeta++;
                diff.Details.Add(string.Format("playlist[{0}].Name: '{1}' -> '{2}'", playlistIndex, inPlaylist.Name ?? string.Empty, outPlaylist.Name ?? string.Empty));
            }

            if (!string.Equals(inPlaylist.YoutubeListId ?? string.Empty, outPlaylist.YoutubeListId ?? string.Empty, StringComparison.Ordinal))
            {
                diff.ChangedPlaylistMeta++;
                diff.Details.Add(string.Format("playlist[{0}].YoutubeListId: '{1}' -> '{2}'", playlistIndex, inPlaylist.YoutubeListId ?? string.Empty, outPlaylist.YoutubeListId ?? string.Empty));
            }

            if (inPlaylist.IsEdit != outPlaylist.IsEdit)
            {
                diff.ChangedPlaylistMeta++;
                diff.Details.Add(string.Format("playlist[{0}].IsEdit: {1} -> {2}", playlistIndex, inPlaylist.IsEdit, outPlaylist.IsEdit));
            }

            List<TrackData> inTracks = inPlaylist.Tracks ?? new List<TrackData>();
            List<TrackData> outTracks = outPlaylist.Tracks ?? new List<TrackData>();
            int maxTrackCount = Mathf.Max(inTracks.Count, outTracks.Count);

            for (int trackIndex = 0; trackIndex < maxTrackCount; trackIndex++)
            {
                bool hasInTrack = trackIndex < inTracks.Count;
                bool hasOutTrack = trackIndex < outTracks.Count;

                if (!hasInTrack && hasOutTrack)
                {
                    diff.AddedTracks++;
                    diff.Details.Add(string.Format("playlist[{0}].track[{1}] added.", playlistIndex, trackIndex));
                    continue;
                }

                if (hasInTrack && !hasOutTrack)
                {
                    diff.RemovedTracks++;
                    diff.Details.Add(string.Format("playlist[{0}].track[{1}] removed.", playlistIndex, trackIndex));
                    continue;
                }

                TrackData inTrack = inTracks[trackIndex] ?? new TrackData();
                TrackData outTrack = outTracks[trackIndex] ?? new TrackData();

                bool modeChanged = inTrack.Mode != outTrack.Mode;
                bool titleChanged = !string.Equals(inTrack.Title ?? string.Empty, outTrack.Title ?? string.Empty, StringComparison.Ordinal);
                bool urlChanged = !string.Equals(inTrack.Url ?? string.Empty, outTrack.Url ?? string.Empty, StringComparison.Ordinal);

                if (!modeChanged && !titleChanged && !urlChanged)
                {
                    continue;
                }

                diff.ChangedTracks++;
                if (modeChanged)
                {
                    diff.Details.Add(string.Format("playlist[{0}].track[{1}].Mode: {2} -> {3}", playlistIndex, trackIndex, inTrack.Mode, outTrack.Mode));
                }

                if (titleChanged)
                {
                    diff.Details.Add(string.Format("playlist[{0}].track[{1}].Title: '{2}' -> '{3}'", playlistIndex, trackIndex, inTrack.Title ?? string.Empty, outTrack.Title ?? string.Empty));
                }

                if (urlChanged)
                {
                    diff.Details.Add(string.Format("playlist[{0}].track[{1}].Url: '{2}' -> '{3}'", playlistIndex, trackIndex, inTrack.Url ?? string.Empty, outTrack.Url ?? string.Empty));
                }
            }
        }

        return diff;
    }

    private static string BuildTsv(PlaylistRoot root, ConversionReport report)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(string.Join("\t", ExpectedHeader));

        int rowCount = 0;
        int totalRows = 0;
        for (int i = 0; i < root.playlists.Count; i++)
        {
            PlaylistData p = root.playlists[i];
            int trackCount = p != null && p.Tracks != null ? p.Tracks.Count : 0;
            totalRows += Mathf.Max(1, trackCount);
        }
        int processedRows = 0;

        for (int playlistIndex = 0; playlistIndex < root.playlists.Count; playlistIndex++)
        {
            PlaylistData playlist = root.playlists[playlistIndex] ?? new PlaylistData();
            string playlistName = NormalizeTextForTsv(playlist.Name);
            string youtubeListId = NormalizeTextForTsv(playlist.YoutubeListId);
            List<TrackData> tracks = playlist.Tracks ?? new List<TrackData>();

            if (tracks.Count == 0)
            {
                string[] zeroTrackRow =
                {
                    playlistName,
                    ToBoolString(playlist.Active),
                    youtubeListId,
                    ToBoolString(playlist.IsEdit),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                };

                sb.AppendLine(string.Join("\t", zeroTrackRow));
                rowCount++;
                processedRows++;
                UpdateCancelableProgress("JSON -> TSV", "Exporting playlists...", processedRows, totalRows);
                continue;
            }

            for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
            {
                TrackData track = tracks[trackIndex] ?? new TrackData();
                string title = NormalizeTextForTsv(track.Title);
                UrlNormalizationResult urlResult = NormalizeUrl(track.Url ?? string.Empty);
                if (urlResult.Changed)
                {
                    report.Warnings.Add(
                        string.Format(
                            "JSON row normalized URL at playlist_index={0}, track_index={1}: '{2}' -> '{3}'",
                            playlistIndex,
                            trackIndex,
                            track.Url ?? string.Empty,
                            urlResult.Value));
                }

                if (!string.IsNullOrEmpty(urlResult.Value) && !urlResult.LooksLikeHttpUrl)
                {
                    report.Warnings.Add(
                        string.Format(
                            "JSON row URL looks invalid at playlist_index={0}, track_index={1}: '{2}'",
                            playlistIndex,
                            trackIndex,
                            urlResult.Value));
                }

                string[] row =
                {
                    playlistName,
                    ToBoolString(playlist.Active),
                    youtubeListId,
                    ToBoolString(playlist.IsEdit),
                    track.Mode.ToString(),
                    title,
                    urlResult.Value,
                };

                sb.AppendLine(string.Join("\t", row));
                rowCount++;
                processedRows++;
                if (processedRows % ProgressUpdateInterval == 0 || processedRows == totalRows)
                {
                    UpdateCancelableProgress("JSON -> TSV", "Exporting playlists...", processedRows, totalRows);
                }
            }
        }

        if (rowCount == 0)
        {
            report.Warnings.Add("No playlist rows exported.");
        }

        return sb.ToString();
    }

    private static PlaylistRoot ParseTsv(string tsvText, ConversionReport report)
    {
        PlaylistRoot root = new PlaylistRoot();
        if (string.IsNullOrEmpty(tsvText))
        {
            report.Errors.Add("TSV is empty.");
            return root;
        }

        string[] lines = tsvText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int headerLineIndex = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                headerLineIndex = i;
                break;
            }
        }

        if (headerLineIndex < 0)
        {
            report.Errors.Add("TSV has no header line.");
            return root;
        }

        string[] headerCells = lines[headerLineIndex].Split('\t');
        if (!IsValidHeader(headerCells))
        {
            report.Errors.Add("TSV header mismatch. Expected fixed header order.");
            return root;
        }

        List<PlaylistBuilder> builders = new List<PlaylistBuilder>();
        PlaylistBuilder currentBuilder = null;

        for (int lineIndex = headerLineIndex + 1; lineIndex < lines.Length; lineIndex++)
        {
            if ((lineIndex - headerLineIndex) % ProgressUpdateInterval == 0 || lineIndex == lines.Length - 1)
            {
                UpdateCancelableProgress(
                    "TSV -> JSON",
                    "Parsing TSV rows...",
                    lineIndex - headerLineIndex,
                    lines.Length - headerLineIndex - 1);
            }

            string rawLine = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            ParseDataLine(rawLine, lineIndex + 1, builders, ref currentBuilder, report);
        }

        for (int i = 0; i < builders.Count; i++)
        {
            PlaylistBuilder builder = builders[i];
            PlaylistData playlist = new PlaylistData();
            playlist.Name = builder.Name ?? string.Empty;
            playlist.Active = builder.Active;
            playlist.YoutubeListId = builder.YoutubeListId ?? string.Empty;
            playlist.IsEdit = builder.IsEdit;
            playlist.Tracks = new List<TrackData>(builder.Tracks);

            if (playlist.Tracks.Count == 0 && !builder.HasZeroTrackMarker)
            {
                report.Warnings.Add(
                    string.Format(
                        "Playlist block #{0} has zero tracks without explicit zero-track row. Treated as zero-track playlist.",
                        i));
            }

            root.playlists.Add(playlist);
        }

        return root;
    }

    private static void ParseDataLine(
        string rawLine,
        int lineNumber,
        List<PlaylistBuilder> builders,
        ref PlaylistBuilder currentBuilder,
        ConversionReport report)
    {
        string[] rawCells = rawLine.Split('\t');
        if (rawCells.Length == ExpectedHeader.Length + 1)
        {
            int legacyPlaylistIndex;
            if (int.TryParse(rawCells[0], out legacyPlaylistIndex))
            {
                // Backward compatibility for legacy rows that still contain playlist_index.
                string[] shifted = new string[ExpectedHeader.Length];
                Array.Copy(rawCells, 1, shifted, 0, ExpectedHeader.Length);
                rawCells = shifted;
                report.Warnings.Add(
                    string.Format(
                        "Line {0}: legacy playlist_index column detected and ignored ({1}).",
                        lineNumber,
                        legacyPlaylistIndex));
            }
        }

        string[] cells = NormalizeColumns(rawCells);
        if (cells.Length < ExpectedHeader.Length)
        {
            report.Errors.Add(string.Format("Line {0}: too few columns.", lineNumber));
            return;
        }

        bool playlistActive;
        if (!TryParseBool(cells[1], out playlistActive))
        {
            report.Errors.Add(string.Format("Line {0}: playlist_active is invalid: '{1}'", lineNumber, cells[1]));
            return;
        }

        bool playlistIsEdit;
        if (!TryParseBool(cells[3], out playlistIsEdit))
        {
            report.Errors.Add(string.Format("Line {0}: playlist_is_edit is invalid: '{1}'", lineNumber, cells[3]));
            return;
        }

        string playlistName = NormalizeTextFromTsv(cells[0]);
        string youtubeListId = NormalizeTextFromTsv(cells[2]);

        bool hasTrackMode = !string.IsNullOrEmpty(cells[4]);
        bool hasTrackTitle = !string.IsNullOrEmpty(cells[5]);
        bool hasTrackUrl = !string.IsNullOrEmpty(cells[6]);
        bool isZeroTrackRow = !hasTrackMode && !hasTrackTitle && !hasTrackUrl;

        bool needNewPlaylist = currentBuilder == null
                               || currentBuilder.HasZeroTrackMarker
                               || !string.Equals(currentBuilder.Name ?? string.Empty, playlistName ?? string.Empty, StringComparison.Ordinal)
                               || currentBuilder.Active != playlistActive
                               || !string.Equals(currentBuilder.YoutubeListId ?? string.Empty, youtubeListId ?? string.Empty, StringComparison.Ordinal)
                               || currentBuilder.IsEdit != playlistIsEdit;

        if (needNewPlaylist)
        {
            currentBuilder = new PlaylistBuilder();
            currentBuilder.Initialized = true;
            currentBuilder.Name = playlistName;
            currentBuilder.Active = playlistActive;
            currentBuilder.YoutubeListId = youtubeListId;
            currentBuilder.IsEdit = playlistIsEdit;
            builders.Add(currentBuilder);
        }

        if (isZeroTrackRow)
        {
            currentBuilder.HasZeroTrackMarker = true;
            return;
        }

        int trackMode;
        if (!int.TryParse(cells[4], out trackMode))
        {
            report.Errors.Add(string.Format("Line {0}: track_mode is invalid: '{1}'", lineNumber, cells[4]));
            return;
        }

        string trackTitle = NormalizeTextFromTsv(cells[5]);
        string rawUrl = cells[6] ?? string.Empty;
        UrlNormalizationResult urlResult = NormalizeUrl(rawUrl);

        if (urlResult.Changed)
        {
            report.Warnings.Add(
                string.Format(
                    "Line {0}: URL normalized: '{1}' -> '{2}'",
                    lineNumber,
                    rawUrl,
                    urlResult.Value));
        }

        if (!string.IsNullOrEmpty(urlResult.Value) && !urlResult.LooksLikeHttpUrl)
        {
            report.Warnings.Add(string.Format("Line {0}: URL looks invalid: '{1}'", lineNumber, urlResult.Value));
        }

        currentBuilder.Tracks.Add(
            new TrackData
            {
                Mode = trackMode,
                Title = trackTitle,
                Url = urlResult.Value,
            });
    }

    private static string[] NormalizeColumns(string[] rawCells)
    {
        if (rawCells.Length == ExpectedHeader.Length)
        {
            return rawCells;
        }

        if (rawCells.Length > ExpectedHeader.Length)
        {
            string[] fixedCells = new string[ExpectedHeader.Length];
            for (int i = 0; i < ExpectedHeader.Length - 1; i++)
            {
                fixedCells[i] = rawCells[i];
            }

            // When tabs leak into URL text, join tail parts by removing tab separators.
            StringBuilder tail = new StringBuilder();
            for (int i = ExpectedHeader.Length - 1; i < rawCells.Length; i++)
            {
                tail.Append(rawCells[i]);
            }

            fixedCells[ExpectedHeader.Length - 1] = tail.ToString();
            return fixedCells;
        }

        string[] padded = new string[ExpectedHeader.Length];
        for (int i = 0; i < rawCells.Length; i++)
        {
            padded[i] = rawCells[i];
        }

        for (int i = rawCells.Length; i < ExpectedHeader.Length; i++)
        {
            padded[i] = string.Empty;
        }

        return padded;
    }

    private static bool IsValidHeader(string[] header)
    {
        if (header.Length < ExpectedHeader.Length)
        {
            return false;
        }

        for (int i = 0; i < ExpectedHeader.Length; i++)
        {
            if (!string.Equals(header[i], ExpectedHeader[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseBool(string value, out bool result)
    {
        if (bool.TryParse(value, out result))
        {
            return true;
        }

        if (value == "1")
        {
            result = true;
            return true;
        }

        if (value == "0")
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static string ToBoolString(bool value)
    {
        return value ? "true" : "false";
    }

    private static string NormalizeTextForTsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\t", " ")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    private static string NormalizeTextFromTsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    private static UrlNormalizationResult NormalizeUrl(string raw)
    {
        if (raw == null)
        {
            raw = string.Empty;
        }

        StringBuilder sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                continue;
            }

            sb.Append(c);
        }

        string normalized = sb.ToString();
        UrlNormalizationResult result = new UrlNormalizationResult();
        result.Value = normalized;
        result.Changed = !string.Equals(raw, normalized, StringComparison.Ordinal);
        result.LooksLikeHttpUrl = normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                  || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static void UpdateCancelableProgress(string title, string info, int current, int total)
    {
        int safeTotal = Mathf.Max(1, total);
        float progress = Mathf.Clamp01((float)current / safeTotal);
        if (EditorUtility.DisplayCancelableProgressBar(title, string.Format("{0} ({1}/{2})", info, current, total), progress))
        {
            throw new OperationCanceledException();
        }
    }

    private void AppendSummary(string direction, string outputPath, int warningCount, int errorCount, string additional)
    {
        _latestLogLines.Add(direction);
        _latestLogLines.Add("Output: " + outputPath);
        _latestLogLines.Add("Warnings: " + warningCount);
        _latestLogLines.Add("Errors: " + errorCount);
        _latestLogLines.Add(additional);
        _latestLogLines.Add(string.Empty);
    }

    private void AppendReportDetails(ConversionReport report)
    {
        if (report.Errors.Count > 0)
        {
            _latestLogLines.Add("[Errors]");
            for (int i = 0; i < report.Errors.Count; i++)
            {
                _latestLogLines.Add("- " + report.Errors[i]);
            }
        }

        if (report.Warnings.Count > 0)
        {
            _latestLogLines.Add("[Warnings]");
            for (int i = 0; i < report.Warnings.Count; i++)
            {
                _latestLogLines.Add("- " + report.Warnings[i]);
            }
        }

        if (report.Errors.Count == 0 && report.Warnings.Count == 0)
        {
            _latestLogLines.Add("No warnings or errors.");
        }
    }

    private static void ShowResultDialog(string title, ConversionReport report)
    {
        if (report.HasError)
        {
            EditorUtility.DisplayDialog(title, "変換失敗: エラーがあります。詳細は Result を確認してください。", "OK");
            return;
        }

        if (report.Warnings.Count > 0)
        {
            EditorUtility.DisplayDialog(title, "変換完了（警告あり）。詳細は Result を確認してください。", "OK");
            return;
        }

        EditorUtility.DisplayDialog(title, "変換完了。", "OK");
    }
}
#endif
