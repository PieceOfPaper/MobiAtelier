// 설치 폴더 읽기·조립 시험 (에디터 배치 모드). 결과 촬영본과 로그를 지정한 폴더에 쓴다.
//   Unity.exe -batchmode -projectPath <프로젝트> -executeMethod InstallSmokeTest.Run
//             -maInstall <설치 폴더> -maOut <출력 폴더>
// 출력 폴더는 저장소 밖(또는 git 제외 폴더)을 쓴다. 게임 리소스가 담긴 촬영본을 저장소에 넣지 않는다.
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class InstallSmokeTest
{
    static string Arg(string key)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, key);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    public static void Run()
    {
        int code = 0;
        var log = new StringBuilder();
        string outDir = Arg("-maOut") ?? Path.Combine(Path.GetTempPath(), "MobiAtelierSmoke");
        Directory.CreateDirectory(outDir);
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = StageBuilder.CreateStage();
            var shader = Shader.Find("MobiAtelier/Character");
            if (!shader) throw new Exception("셰이더 MobiAtelier/Character 없음");
            CharacterAssembler.DebugMaterials = Array.IndexOf(Environment.GetCommandLineArgs(), "-maDebugMaterials") >= 0;
            // -maEyeShader original|mobi : 눈 파츠 셰이더 비교용
            var eyeShader = Arg("-maEyeShader");
            if (eyeShader != null) CharacterAssembler.KeepOriginalEyeShader = eyeShader == "original";
            log.AppendLine("눈 셰이더: " + (CharacterAssembler.KeepOriginalEyeShader ? "원본" : "대체"));

            var check = InstallValidator.Check(Arg("-maInstall") ?? AppSettings.DefaultInstallPath, s => log.AppendLine("  " + s));
            log.AppendLine($"검사: {check.Step} {check.Message} {check.Detail}");
            if (!check.Ok) throw new Exception("설치 경로 검사 실패");
            var files = check.Files; var data = check.Data;
            log.AppendLine($"index {files.IndexLoadMs}ms, 시트 {data.LoadMs}ms, 의상 {data.Costumes.Count}, 프리셋 {data.Presets.Count}");

            using (var bundles = new BundleCache(files))
            {
                void Shoot(string name, EditState state)
                {
                    var plan = PlanResolver.Resolve(state, data, files);
                    foreach (var n in plan.Notes) log.AppendLine("  참고: " + n);
                    foreach (var c in plan.Costumes) log.AppendLine($"  의상 {c.Part} {c.CostumeId} {c.DisplayName}: {c.Status} {c.Detail}");
                    log.AppendLine($"[{name}] 파츠: {string.Join(", ", plan.Parts.ConvertAll(p => p.Key + "=" + p.Model))}, 포즈 {plan.Pose}");
                    var view = CharacterAssembler.Build(files, bundles, plan, shader, log);
                    var head = view.FindBone("_Head");
                    Capture(cam, view, Path.Combine(outDir, name + "_front.png"), 180, 5, 3.6f, new Vector3(0, 0.88f, 0));
                    Capture(cam, view, Path.Combine(outDir, name + "_34.png"), 145, 10, 3.6f, new UnityEngine.Vector3(0, 0.88f, 0));
                    if (head) Capture(cam, view, Path.Combine(outDir, name + "_face.png"), 180, 0, 0.9f, head.position + new Vector3(0, 0.08f, 0), 768, 768);
                    UnityEngine.Object.DestroyImmediate(view.gameObject);
                }

                foreach (var preset in StateFactory.DefaultPresets)
                {
                    var s = StateFactory.FromPreset(data, preset);
                    Shoot(preset, s);
                    s.costumes.Add(new CostumeSlotState { part = "Top", costumeId = "Fashion_NewbieSword_Top" });
                    s.costumes.Add(new CostumeSlotState { part = "Bottom", costumeId = "Fashion_NewbieSword_Bottom" });
                    s.costumes.Add(new CostumeSlotState { part = "Shoes", costumeId = "Fashion_NewbieSword_Shoes" });
                    Shoot(preset + "_newbie", s);
                    // 원피스(여성 전용): 남성은 미적용, 여성은 하의를 가린다. 상의 Slot1 만 사용자 색.
                    s.costumes[0] = new CostumeSlotState { part = "Top", costumeId = "Fashion_Healer_OnePiece_F" };
                    s.costumes[0].dyes.Add(new ColorOverride { slot = "Slot1", color = "#FF00FF" });
                    Shoot(preset + "_onepiece", s);
                }
            }
        }
        catch (Exception e) { log.AppendLine(e.ToString()); Debug.LogException(e); code = 1; }
        File.WriteAllText(Path.Combine(outDir, "log.txt"), $"Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}\n{log}", new UTF8Encoding(false));
        EditorApplication.Exit(code);
    }

    static void Capture(Camera cam, CharacterView ch, string file, float yaw, float pitch, float dist, Vector3 target, int w = 768, int h = 1024)
    {
        var rot = Quaternion.Euler(pitch, yaw, 0);
        cam.transform.SetPositionAndRotation(target + rot * new Vector3(0, 0, -dist), rot);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        cam.targetTexture = rt;
        foreach (var (_, r) in ch.Renderers) if (r && r.gameObject.activeInHierarchy) { r.enabled = false; r.enabled = true; }   // 블렌드셰이프 갱신
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
    }
}
