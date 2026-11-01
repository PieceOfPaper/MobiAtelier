// 자동 저장 (SaveLoad.md 제안 채택 범위)
//   변경을 표시하면 짧은 유휴 시간 뒤 한 번에 저장한다. 종료 때는 즉시 저장한다.
//   손상된 자동 저장 파일은 새 작업을 쓰기 전에 따로 보존한다.
using System;
using System.IO;
using UnityEngine;

public sealed class Autosave
{
    public const float IdleSeconds = 1.0f;

    float timer = -1;
    Func<EditState> source;
    public string LastError { get; private set; }
    public event Action<string> Failed;

    public Autosave(Func<EditState> source) { this.source = source; }

    public bool Pending => timer >= 0;

    public void MarkDirty() => timer = IdleSeconds;

    public void Tick(float dt)
    {
        if (timer < 0) return;
        timer -= dt;
        if (timer < 0) Flush();
    }

    /// <summary>대기 중인 변경을 바로 쓴다. 실패하면 false(메모리의 작업은 유지).</summary>
    public bool Flush()
    {
        timer = -1;
        var s = source?.Invoke();
        if (s == null) return true;
        try
        {
            s.appVersion = Application.version;
            JsonStore.WriteAtomic(JsonStore.AutosavePath, JsonUtility.ToJson(s, true));
            LastError = null;
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            LastError = e.Message;
            Failed?.Invoke(e.Message);
            return false;
        }
    }

    /// <summary>읽지 못한 자동 저장 파일을 덮어쓰기 전에 다른 이름으로 보존한다. 보존한 경로를 돌려준다.</summary>
    public static string PreserveBroken()
    {
        string src = JsonStore.AutosavePath;
        if (!File.Exists(src)) return null;
        string dst = Path.Combine(JsonStore.DataDir, $"autosave.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(src, dst, false);
        return dst;
    }
}
