// 앱 씬과 UI 설정 에셋을 코드로 만든다(씬·에셋 YAML 을 손으로 고치지 않기 위해).
//   메뉴: MobiAtelier/Setup Project
//   배치: Unity.exe -batchmode -projectPath <프로젝트> -executeMethod ProjectSetup.Run -quit
// 하는 일
//   - Assets/UI/MobiAtelierPanel.asset (PanelSettings, 기본 런타임 테마)
//   - Assets/Scenes/Main.unity (AppController 하나)
//   - Build Settings 의 씬 목록을 Main 하나로 설정
//   - 창 모드·창 크기 변경 허용(WindowAndCustomizationUI.md 확정 사항)
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class ProjectSetup
{
    public const string ScenePath = "Assets/Scenes/Main.unity";
    const string PanelPath = "Assets/UI/MobiAtelierPanel.asset";
    const string ThemePath = "Assets/UI/MobiAtelierTheme.tss";
    const string ShaderPath = "Assets/Shaders/MobiCharacter.shader";
    const string GradientShaderPath = "Assets/Shaders/StudioGradient.shader";

    [MenuItem("MobiAtelier/Setup Project")]
    public static void Run()
    {
        AssetDatabase.Refresh();
        var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        if (!theme) Debug.LogError("[MobiAtelier] 테마 파일을 찾지 못했습니다: " + ThemePath);

        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        if (!panel)
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, PanelPath);
        }
        panel.themeStyleSheet = theme;
        panel.scaleMode = PanelScaleMode.ConstantPhysicalSize;
        panel.referenceDpi = 96;
        panel.fallbackDpi = 96;
        panel.clearColor = false;
        EditorUtility.SetDirty(panel);
        AssetDatabase.SaveAssets();

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var app = new GameObject("MobiAtelier").AddComponent<AppController>();
        app.characterShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        app.gradientShader = AssetDatabase.LoadAssetAtPath<Shader>(GradientShaderPath);
        app.panelSettings = panel;
        if (!app.characterShader) Debug.LogError("[MobiAtelier] 셰이더를 찾지 못했습니다: " + ShaderPath);
        if (!app.gradientShader) Debug.LogError("[MobiAtelier] 셰이더를 찾지 못했습니다: " + GradientShaderPath);
        EditorSceneManager.SaveScene(scene, ScenePath);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 800;
        AssetDatabase.SaveAssets();
        Debug.Log("[MobiAtelier] 프로젝트 설정 완료: " + ScenePath);
    }
}
