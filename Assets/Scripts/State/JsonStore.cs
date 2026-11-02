// 앱 데이터(Application.persistentDataPath) JSON 읽기·쓰기.
//   쓰기: 임시 파일에 쓴 뒤 교체하고, 직전 정상 파일은 .bak 으로 남긴다.
//   읽기: 없음 / 정상 / 손상 / 지원하지 않는 형식을 구분한다. 손상 파일은 덮어쓰지 않는다(호출 쪽 책임).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public enum LoadStatus { Missing, Ok, Corrupt, Unsupported }

public readonly struct LoadResult<T>
{
    public readonly LoadStatus Status;
    public readonly T Value;
    public readonly string Error;
    public LoadResult(LoadStatus status, T value, string error) { Status = status; Value = value; Error = error; }
}

public static class CommandLine
{
    public static string Value(string key)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, key);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
}

public static class JsonStore
{
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    /// <summary>앱 데이터 폴더. 시험 실행에서는 -maDataDir &lt;폴더&gt; 로 바꿀 수 있다.</summary>
    public static string DataDir => dataDir ?? (dataDir = CommandLine.Value("-maDataDir") ?? Application.persistentDataPath);
    static string dataDir;
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string AutosavePath => Path.Combine(DataDir, "autosave.json");

    public static void WriteAtomic(string path, string json)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json, Utf8);
        if (File.Exists(path))
        {
            string bak = path + ".bak";
            File.Replace(tmp, path, bak, true);
        }
        else File.Move(tmp, path);
    }

    public static LoadResult<T> Read<T>(string path, Func<T, bool> versionOk = null) where T : class
    {
        if (!File.Exists(path)) return new LoadResult<T>(LoadStatus.Missing, null, null);
        try
        {
            string text = File.ReadAllText(path, Utf8);
            if (string.IsNullOrWhiteSpace(text)) return new LoadResult<T>(LoadStatus.Corrupt, null, "빈 파일입니다.");
            var v = JsonUtility.FromJson<T>(text);
            if (v == null) return new LoadResult<T>(LoadStatus.Corrupt, null, "JSON 을 해석하지 못했습니다.");
            if (versionOk != null && !versionOk(v)) return new LoadResult<T>(LoadStatus.Unsupported, v, "지원하지 않는 파일 형식 버전입니다.");
            return new LoadResult<T>(LoadStatus.Ok, v, null);
        }
        catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException)
        {
            return new LoadResult<T>(LoadStatus.Corrupt, null, e.Message);
        }
    }

    public static LoadResult<EditState> ReadEditState(string path) =>
        Read<EditState>(path, s => s.formatVersion >= 1 && s.formatVersion <= EditState.CurrentFormatVersion && s.appearance != null);
}

/// <summary>앱 설정. 장면 상태(EditState)와 분리해 보관한다.</summary>
[Serializable]
public class AppSettings
{
    public const string DefaultInstallPath = @"C:\Nexon\MabinogiMobile";

    /// <summary>마지막으로 검사를 통과한 게임 설치 경로</summary>
    public string installPath;
    /// <summary>마지막으로 JSON 저장에 성공한 폴더</summary>
    public string recentJsonFolder;
    /// <summary>가로로 넓은 앱 창에서 꾸미기 창의 마지막 위치·크기(UI 패널 좌표). 화면에 맞춘 보정 전의 값이다.</summary>
    public PanelLayout customizePanel = new PanelLayout();
    /// <summary>세로로 긴 앱 창에서의 꾸미기 창 배치.</summary>
    public PanelLayout customizePanelTall = new PanelLayout();
    /// <summary>true 면 openSections 가 사용자가 정한 폴드아웃 펼침 상태다. false 면 기본값을 쓴다.</summary>
    public bool sectionsSaved;
    public List<string> openSections = new List<string>();

    public static AppSettings Load()
    {
        var r = JsonStore.Read<AppSettings>(JsonStore.SettingsPath);
        return r.Status == LoadStatus.Ok ? r.Value : new AppSettings();
    }

    public void Save() => JsonStore.WriteAtomic(JsonStore.SettingsPath, JsonUtility.ToJson(this, true));
}

[Serializable]
public class PanelLayout
{
    public bool valid;
    public float x, y, width, height;
}
