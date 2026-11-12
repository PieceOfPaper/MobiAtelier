// 꾸미기 장면의 카메라·조명. 씬 파일에 의존하지 않고 코드로 만든다.
using UnityEngine;

public static class StageBuilder
{
    public static readonly Color Background = new Color(0.30f, 0.32f, 0.36f);

    /// <summary>장면 방향광. 배경의 환경 볼륨(EnvironmentVolume)이 방향·색·세기를 바꾼다.</summary>
    public static Light MainLight { get; private set; }

    public static Camera CreateStage()
    {
        var cam = Camera.main;
        if (!cam)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = camGo.AddComponent<Camera>();
        }
        cam.fieldOfView = 30;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 100f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Background;
        cam.transform.SetPositionAndRotation(new Vector3(0, 0.9f, 3.6f), Quaternion.Euler(5, 180, 0));

        var existing = Object.FindAnyObjectByType<Light>();
        if (existing && existing.type == LightType.Directional) MainLight = existing;
        if (!existing)
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.shadows = LightShadows.Soft;
            // 캐릭터는 그림자를 받지 않으므로 이 그림자는 바닥(배경)에만 보인다. 게임 스튜디오처럼 옅게 둔다 [근사].
            light.shadowStrength = 0.45f;
            lightGo.transform.rotation = Quaternion.Euler(35, 155, 0);
            MainLight = light;
        }
        return cam;
    }
}
