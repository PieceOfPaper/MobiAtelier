// 게임 설치 경로 검사 (Startup.md "정상 구성의 의미").
//   경로 → 필수 index → index 형식 → 필수 데이터 → 실제 읽기 → 시트 해석
// 검사에 성공하면 읽은 index 와 시트를 그대로 넘겨 다시 읽지 않게 한다. UnityEngine 에 의존하지 않는다.
using System;
using System.IO;

public enum InstallCheckStep { None, Path, RequiredIndex, IndexFormat, RequiredData, ActualRead, SheetFormat, Done }

public sealed class InstallCheckResult
{
    public bool Ok => Step == InstallCheckStep.Done;
    public InstallCheckStep Step;
    public string Message;
    public string Detail;
    public InstallFiles Files;
    public GameData Data;
}

public static class InstallValidator
{
    /// <summary>입력 앞뒤 공백과 경로를 감싼 따옴표를 정리한다. 경로 중간 공백은 보존한다.</summary>
    public static string Normalize(string input)
    {
        if (input == null) return "";
        string s = input.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2).Trim();
        return s;
    }

    static InstallCheckResult Fail(InstallCheckStep step, string message, string detail = null) =>
        new InstallCheckResult { Step = step, Message = message, Detail = detail };

    public static InstallCheckResult Check(string path, Action<string> progress = null)
    {
        path = Normalize(path);
        progress?.Invoke("설치 폴더 확인 중");
        if (path.Length == 0) return Fail(InstallCheckStep.Path, "설치된 마비노기 모바일 폴더를 선택해 주세요.");
        try
        {
            if (!Directory.Exists(path)) return Fail(InstallCheckStep.Path, "폴더를 찾을 수 없습니다.", path);
            Directory.GetFileSystemEntries(path);
        }
        catch (Exception e) when (e is UnauthorizedAccessException || e is IOException || e is ArgumentException || e is NotSupportedException)
        {
            return Fail(InstallCheckStep.Path, "폴더에 접근할 수 없습니다.", e.Message);
        }

        string assets = Path.Combine(path, InstallFiles.AssetPackagesDir), res = Path.Combine(path, InstallFiles.ResourcesPackageDir);
        foreach (var f in new[] { PackageIndex.IndexPath(assets), PackageIndex.StarterIndexPath(assets), PackageIndex.IndexPath(res), PackageIndex.StarterIndexPath(res) })
            if (!File.Exists(f))
                return Fail(InstallCheckStep.RequiredIndex, "이 폴더에서 필요한 파일을 찾지 못했습니다.", "없는 파일: " + f.Substring(path.Length).TrimStart('\\', '/'));

        progress?.Invoke("파일 목록 읽는 중");
        InstallFiles files;
        try { files = new InstallFiles(path); }
        catch (InvalidDataException e) { return Fail(InstallCheckStep.IndexFormat, "파일 목록을 해석하지 못했습니다. 지원하지 않는 게임 버전일 수 있습니다.", e.Message); }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        { return Fail(InstallCheckStep.IndexFormat, "파일을 읽지 못했습니다. 게임 패치가 끝난 뒤 다시 시도해 주세요.", e.Message); }

        string set = SheetSet.DefaultSet;
        foreach (var p in new[] { SheetSet.MetaPath(set), SheetSet.TextPath(set), SheetSet.DataPath(set), SymbolTables.StringsPath })
            if (!files.HasResource(p)) return Fail(InstallCheckStep.RequiredData, "필요한 게임 데이터를 찾지 못했습니다.", "없는 데이터: " + p);
        if (files.ModelCount == 0) return Fail(InstallCheckStep.RequiredData, "필요한 게임 데이터를 찾지 못했습니다.", "모델 정의가 없습니다.");

        progress?.Invoke("게임 데이터 읽는 중");
        GameData data;
        try { data = GameData.Load(files); }
        catch (SheetFormatException e) { return Fail(InstallCheckStep.SheetFormat, "게임 데이터 형식이 지원 범위와 다릅니다. 지원하지 않는 게임 버전일 수 있습니다.", e.Message); }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException || e is FileNotFoundException)
        { return Fail(InstallCheckStep.ActualRead, "파일을 읽지 못했습니다. 게임 패치가 끝난 뒤 다시 시도해 주세요.", e.Message); }

        return new InstallCheckResult { Step = InstallCheckStep.Done, Files = files, Data = data };
    }
}
