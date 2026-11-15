// 게임 스튜디오 촬영 카메라 값(Camera.md "게임 스튜디오 카메라"). UnityEngine 에 의존하지 않는다.
//   CameraSettings/CameraPresetSettings.sjon.bin 의 StudioModeCameraSetting(가로 Landscape·세로 Portrait)
//   StageConstantSettings/StageConstants.sjon.bin 의 PhotoModeCameraSettings(휠)·RobeAppearanceHeightSettings(기준 키)
// 파일이 없거나 형식이 다르면 조사 시점(2026-11-15)의 설치본 값으로 대신한다.
using System;

public sealed class StudioCameraParams
{
    public float Fov, InitialPitch, MinPitch, MaxPitch, DefaultDistance, MinDistance;
    public float MinPanningHeightWhenMinDistance, MaxPanningHeightWhenMinDistance;
    public float MinPanningHeightWhenMaxDistance, MaxPanningHeightWhenMaxDistance;
    public float InitialPanningHeight, CameraTargetHeight;

    internal static StudioCameraParams Read(object o, StudioCameraParams fallback)
    {
        if (!(o is System.Collections.Generic.Dictionary<string, object>)) return fallback;
        float F(string k, float def) => o.Get(k) is double d ? (float)d : def;
        return new StudioCameraParams
        {
            Fov = F("Fov", fallback.Fov),
            InitialPitch = F("InitialPitch", fallback.InitialPitch),
            MinPitch = F("MinPitch", fallback.MinPitch),
            MaxPitch = F("MaxPitch", fallback.MaxPitch),
            DefaultDistance = F("DefaultDistance", fallback.DefaultDistance),
            MinDistance = F("MinDistance", fallback.MinDistance),
            MinPanningHeightWhenMinDistance = F("MinPanningHeightWhenMinDistance", fallback.MinPanningHeightWhenMinDistance),
            MaxPanningHeightWhenMinDistance = F("MaxPanningHeightWhenMinDistance", fallback.MaxPanningHeightWhenMinDistance),
            MinPanningHeightWhenMaxDistance = F("MinPanningHeightWhenMaxDistance", fallback.MinPanningHeightWhenMaxDistance),
            MaxPanningHeightWhenMaxDistance = F("MaxPanningHeightWhenMaxDistance", fallback.MaxPanningHeightWhenMaxDistance),
            InitialPanningHeight = F("InitialPanningHeight", fallback.InitialPanningHeight),
            CameraTargetHeight = F("CameraTargetHeight", fallback.CameraTargetHeight),
        };
    }
}

public sealed class StudioCameraData
{
    public const string PresetPath = "CameraSettings/CameraPresetSettings.sjon.bin";
    public const string ConstantsPath = "StageConstantSettings/StageConstants.sjon.bin";

    public StudioCameraParams Landscape, Portrait;
    /// <summary>스튜디오 휠 한 칸(원값 1)당 거리 변화(m). PhotoModeCameraSettings.MouseWheelToDistanceRatioOnStudio.</summary>
    public float WheelToDistance = 0.1f;
    /// <summary>기준 키(성별·연령). 카메라 최대 거리 = clamp(H / 기준 키 × DefaultDistance, MinDistance, DefaultDistance).</summary>
    public float FemaleAdultHeight = 1.685396f, MaleAdultHeight = 1.792207f, FemaleChildHeight = 1.651735f, MaleChildHeight = 1.742354f;
    /// <summary>읽지 못한 항목(없으면 null). 대체값으로 동작한다.</summary>
    public string Error;

    public float StandardHeight(bool male, bool child) =>
        male ? (child ? MaleChildHeight : MaleAdultHeight) : (child ? FemaleChildHeight : FemaleAdultHeight);

    public static StudioCameraData Defaults() => new StudioCameraData
    {
        Landscape = new StudioCameraParams
        {
            Fov = 50, InitialPitch = 3, MinPitch = -1, MaxPitch = 25, DefaultDistance = 3, MinDistance = 1.3f,
            MinPanningHeightWhenMinDistance = 0.2f, MaxPanningHeightWhenMinDistance = 1f,
            MinPanningHeightWhenMaxDistance = 0.25f, MaxPanningHeightWhenMaxDistance = 1.1f,
            InitialPanningHeight = 0.7f, CameraTargetHeight = 0.9f,
        },
        Portrait = new StudioCameraParams
        {
            Fov = 50, InitialPitch = 10, MinPitch = 4, MaxPitch = 25, DefaultDistance = 4.2f, MinDistance = 1.5f,
            MinPanningHeightWhenMinDistance = 0.1f, MaxPanningHeightWhenMinDistance = 1f,
            MinPanningHeightWhenMaxDistance = 0.25f, MaxPanningHeightWhenMaxDistance = 1.1f,
            InitialPanningHeight = 0.6f, CameraTargetHeight = 0.2f,
        },
    };

    public static StudioCameraData Load(InstallFiles files)
    {
        var d = Defaults();
        try
        {
            var s = files.ReadSjon(PresetPath).Get("StudioModeCameraSetting");
            if (s == null) throw new FormatException("StudioModeCameraSetting 이 없습니다");
            d.Landscape = StudioCameraParams.Read(s.Get("Landscape"), d.Landscape);
            d.Portrait = StudioCameraParams.Read(s.Get("Portrait"), d.Portrait);
        }
        catch (Exception e) { d.Error = "스튜디오 카메라 설정: " + e.Message; }
        try
        {
            var c = files.ReadSjon(ConstantsPath);
            if (c.Get("PhotoModeCameraSettings").Get("MouseWheelToDistanceRatioOnStudio") is double w && w > 0) d.WheelToDistance = (float)w;
            var h = c.Get("RobeAppearanceHeightSettings");
            float H(string k, float def) => h.Get(k) is double v && v > 0.5 ? (float)v : def;
            d.FemaleAdultHeight = H("FemaleAdultStandardHeightReference", d.FemaleAdultHeight);
            d.MaleAdultHeight = H("MaleAdultStandardHeightReference", d.MaleAdultHeight);
            d.FemaleChildHeight = H("FemaleChildStandardHeightReference", d.FemaleChildHeight);
            d.MaleChildHeight = H("MaleChildStandardHeightReference", d.MaleChildHeight);
        }
        catch (Exception e) { d.Error = (d.Error == null ? "" : d.Error + " / ") + "스테이지 상수: " + e.Message; }
        return d;
    }
}
