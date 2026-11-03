// Windows 플레이어 빌드.
//   Unity.exe -batchmode -projectPath <프로젝트> -buildTarget Win64 -executeMethod BuildPlayer.Windows -maBuildOut <폴더>
// 빌드 출력은 저장소 밖이나 git 제외 폴더(Builds\)에 둔다.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildPlayer
{
    public static void Windows()
    {
        int code = 1;
        try
        {
            string outDir = CommandLine.Value("-maBuildOut") ?? "Builds/Win64";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = Path.Combine(outDir, "MobiAtelier.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = CommandLine.Value("-maBuildKind") == "development" ? BuildOptions.Development : BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[MobiAtelier] 빌드 {s.result}, 오류 {s.totalErrors}, 경고 {s.totalWarnings}, {s.totalSize / 1048576.0:F1}MB, {s.totalTime.TotalSeconds:F0}초, {Path.GetFullPath(outDir)}");
            code = s.result == BuildResult.Succeeded ? 0 : 1;
        }
        catch (Exception e) { Debug.LogException(e); }
        EditorApplication.Exit(code);
    }
}
