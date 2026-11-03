// 설치 폴더 읽기·조립 시험 (에디터 배치 모드). 결과 촬영본과 로그를 지정한 폴더에 쓴다.
//   Unity.exe -batchmode -projectPath <프로젝트> -executeMethod InstallSmokeTest.Run
//             -maInstall <설치 폴더> -maOut <출력 폴더>
// 출력 폴더는 저장소 밖(또는 git 제외 폴더)을 쓴다. 게임 리소스가 담긴 촬영본을 저장소에 넣지 않는다.
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            // -maHeadShader original|mobi : 머리(얼굴·입) 셰이더 비교용
            var headShader = Arg("-maHeadShader");
            if (headShader != null) CharacterAssembler.KeepOriginalHeadShader = headShader == "original";
            log.AppendLine("눈 셰이더: " + (CharacterAssembler.KeepOriginalEyeShader ? "원본" : "대체") + ", 머리 셰이더: " + (CharacterAssembler.KeepOriginalHeadShader ? "원본" : "대체"));

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
                    // -maMotionTimes 초;초;... : 아이들 모션을 그 시점으로 평가해 전신·얼굴을 촬영한다.
                    var motion = view.GetComponent<CharacterMotion>();
                    string times = Arg("-maMotionTimes");
                    if (motion && times != null)
                        foreach (var ts in times.Split(';'))
                        {
                            if (!float.TryParse(ts, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float t)) continue;
                            motion.Sample(t);
                            Capture(cam, view, Path.Combine(outDir, $"{name}_t{ts}_front.png"), 180, 5, 3.6f, new Vector3(0, 0.88f, 0));
                            if (head) Capture(cam, view, Path.Combine(outDir, $"{name}_t{ts}_face.png"), 180, 0, 0.6f, head.position + new Vector3(0, 0.08f, 0), 512, 512);
                        }
                    UnityEngine.Object.DestroyImmediate(view.gameObject);
                }

                // -maBackgrounds ID1;ID2;... : 앱과 같은 방식(StudioBackground)으로 배경을 적용해 촬영한다. all 이면 고를 수 있는 배경 전부.
                string bgs = Arg("-maBackgrounds");
                if (bgs != null)
                {
                    log.AppendLine($"배경 시트: {data.Backgrounds.Count}행 {(data.BackgroundError ?? "")}");
                    foreach (var b in data.Backgrounds)
                        log.AppendLine($"  {b.Id} {b.DisplayName} 개발용 {b.DevOnly} 선택 가능 {StudioBackground.Selectable(b)} 색 {b.LeftUpper}/{b.LeftBottom} 프리팹 {string.Join("+", b.Prefabs)}");
                    var s = StateFactory.FromPreset(data, Arg("-maPreset") ?? StateFactory.DefaultPresets[1]);
                    s.costumes.Add(new CostumeSlotState { part = "Top", costumeId = "Fashion_NewbieSword_Top" });
                    s.costumes.Add(new CostumeSlotState { part = "Bottom", costumeId = "Fashion_NewbieSword_Bottom" });
                    s.costumes.Add(new CostumeSlotState { part = "Shoes", costumeId = "Fashion_NewbieSword_Shoes" });
                    var view = CharacterAssembler.Build(files, bundles, PlanResolver.Resolve(s, data, files), shader, log);
                    var studio = new StudioBackground(cam, bundles, Shader.Find("MobiAtelier/StudioGradient"));
                    var ids = new System.Collections.Generic.List<string>();
                    if (bgs == "all") { foreach (var b in data.Backgrounds) if (StudioBackground.Selectable(b) && !b.DevOnly) ids.Add(b.Id); }
                    else ids.AddRange(bgs.Split(';'));
                    foreach (var id in ids)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        string err = studio.Apply(StudioBackground.Resolve(data, id));
                        log.AppendLine($"[{id}] 적용 {studio.CurrentId} {sw.ElapsedMilliseconds}ms {err}");
                        Capture(cam, view, Path.Combine(outDir, $"bg_{id}_front.png"), 180, 5, 3.6f, new Vector3(0, 0.88f, 0), 576, 1024);
                        Capture(cam, view, Path.Combine(outDir, $"bg_{id}_34.png"), 145, 10, 3.6f, new Vector3(0, 0.88f, 0), 576, 1024);
                    }
                    studio.Apply(null);
                    UnityEngine.Object.DestroyImmediate(view.gameObject);
                    File.WriteAllText(Path.Combine(outDir, "log.txt"), log.ToString(), new UTF8Encoding(false));
                    EditorApplication.Exit(0);
                    return;
                }

                // -maProbeClips 경로1;경로2;... : 애니메이션 클립(.fbx, Assets/PatchableAssets/ 아래 상대 경로)의 길이·반복·이벤트를 로그에 쓴다.
                string probeClips = Arg("-maProbeClips");
                if (probeClips != null)
                {
                    foreach (var rel in probeClips.Split(';'))
                    {
                        string path = BundleCache.AssetRoot + rel;
                        var bundle = files.HasAsset(path) ? bundles.BundleOf(path) : null;
                        if (!bundle) { log.AppendLine($"[클립] {rel}: 없음"); continue; }
                        foreach (var c in bundle.LoadAssetWithSubAssets<AnimationClip>(path))
                        {
                            if (!c) continue;
                            log.AppendLine($"[클립] {rel}: {c.name} {c.length:F3}s {c.frameRate}fps 반복 {c.isLooping} 레거시 {c.legacy} 이벤트 {c.events.Length}");
                            foreach (var e in c.events) log.AppendLine($"    {e.time:F3}s {e.functionName}({e.stringParameter}, {e.intParameter}, {e.floatParameter}, {(e.objectReferenceParameter ? e.objectReferenceParameter.name : "")})");
                        }
                    }
                    File.WriteAllText(Path.Combine(outDir, "log.txt"), log.ToString(), new UTF8Encoding(false));
                    EditorApplication.Exit(0);
                    return;
                }

                // -maProbePrefab 경로1;경로2;... : 배경 프리팹을 열어 구성(렌더러·셰이더·조명·범위)을 로그에 쓰고 기본 외형과 함께 촬영한다.
                //   경로는 Assets/PatchableAssets/ 아래 상대 경로. 한 항목에 여러 프리팹은 + 로 잇는다.
                string probe = Arg("-maProbePrefab");
                if (probe != null)
                {
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-maBgPass") >= 0) StudioBackgroundRendering.Install();
                    var s = StateFactory.FromPreset(data, Arg("-maPreset") ?? StateFactory.DefaultPresets[1]);
                    var plan = PlanResolver.Resolve(s, data, files);
                    var view = CharacterAssembler.Build(files, bundles, plan, shader, log);
                    int n = 0;
                    foreach (var entry in probe.Split(';'))
                    {
                        n++;
                        var roots = new System.Collections.Generic.List<GameObject>();
                        foreach (var rel in entry.Split('+'))
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            int before = bundles.Count;
                            var prefab = bundles.LoadAsset<GameObject>(BundleCache.AssetRoot + rel);
                            log.AppendLine($"[{n}] {rel}: {(prefab ? "열림" : "실패")} {sw.ElapsedMilliseconds}ms, 번들 {before}→{bundles.Count}");
                            if (!prefab) continue;
                            var go = UnityEngine.Object.Instantiate(prefab);
                            roots.Add(go);
                            DescribePrefab(go, log);
                        }
                        Capture(cam, view, Path.Combine(outDir, $"probe{n}_front.png"), 180, 5, 3.6f, new Vector3(0, 0.88f, 0));
                        Capture(cam, view, Path.Combine(outDir, $"probe{n}_wide.png"), 180, 10, 9f, new Vector3(0, 1.2f, 0), 1280, 800);
                        Capture(cam, view, Path.Combine(outDir, $"probe{n}_back.png"), 0, 10, 6f, new Vector3(0, 1.2f, 0), 1280, 800);
                        foreach (var go in roots) UnityEngine.Object.DestroyImmediate(go);
                    }
                    UnityEngine.Object.DestroyImmediate(view.gameObject);
                    File.WriteAllText(Path.Combine(outDir, "log.txt"), log.ToString(), new UTF8Encoding(false));
                    EditorApplication.Exit(0);
                    return;
                }

                // -maOutfit 이름:Top=ID,Gloves=ID;이름2:... : 지정 조합만 조립해 전신·팔·발을 촬영한다(관통 진단용).
                //   age=14, height=-1, gender=Male, body=Fat, stance=Alert|Combat, job=CombatJob.Id, intro=클립, loop=클립 도 같은 자리에 쓸 수 있다.
                //   social=SocialAction.Id [act=행동 이름] : 앱과 같은 방식(SocialMotionLoader)으로 소셜 모션을 재생한다. 촬영 시점은 행동 시작 기준이다.
                //   -maOutfitVariants 를 주면 몸 자르기 끔·몸 끔·의상 피부 조각 끔 변형도 촬영한다.
                string outfit = Arg("-maOutfit");
                if (outfit != null)
                {
                    // 소셜 모션 소품 일부는 배경 셰이더(MMN/BG, LightMode BG)로 그려진다. 앱과 같이 배경 패스를 건다.
                    StudioBackgroundRendering.Install();
                    bool variants = Array.IndexOf(Environment.GetCommandLineArgs(), "-maOutfitVariants") >= 0;
                    foreach (var entry in outfit.Split(';'))
                    {
                        int colon = entry.IndexOf(':');
                        string tag = colon > 0 ? entry.Substring(0, colon).Trim() : "outfit";
                        var s = StateFactory.FromPreset(data, Arg("-maPreset") ?? StateFactory.DefaultPresets[1]);
                        string prevStance = null, introClip = null, loopClip = null, social = null, socialAct = null;
                        foreach (var kv in entry.Substring(colon + 1).Split(','))
                        {
                            var p = kv.Split('=');
                            if (p.Length != 2) continue;
                            string k = p[0].Trim(), v = p[1].Trim();
                            if (k == "age") s.appearance.age = int.Parse(v);
                            else if (k == "height") s.appearance.heightModifier = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
                            else if (k == "gender") s.appearance.gender = v;
                            else if (k == "body") s.appearance.bodyType = v;
                            else if (k == "stance") s.weaponPose.stance = v;
                            else if (k == "job") s.weaponPose.job = v;
                            else if (k == "prev") prevStance = v;
                            else if (k == "intro") introClip = v;
                            else if (k == "loop") loopClip = v;
                            else if (k == "social") social = v;
                            else if (k == "act") socialAct = v;
                            else s.costumes.Add(new CostumeSlotState { part = k, costumeId = v });
                        }
                        log.AppendLine($"[{tag}]");
                        // prev=자세 : 그 자세에서 바꾼 것으로 보고 직전 계획을 넘긴다(꺼내기·넣기 도입 모션 확인용).
                        AssemblyPlan prevPlan = null;
                        if (prevStance != null)
                        {
                            var ps = s.Clone();
                            ps.weaponPose.stance = prevStance;
                            prevPlan = PlanResolver.Resolve(ps, data, files);
                        }
                        var plan = PlanResolver.Resolve(s, data, files, prevPlan);
                        // intro=클립, loop=클립 : 도입·반복 클립을 바꾼다(Assets/PatchableAssets/ 아래 상대 경로, 행동 모션 재생 확인용).
                        if (introClip != null) { plan.Intro = BundleCache.AssetRoot + introClip; plan.IntroDrawn = plan.WeaponDrawn; plan.IntroToolSpans.Clear(); }
                        if (loopClip != null) plan.Idle = BundleCache.AssetRoot + loopClip;
                        foreach (var c in plan.Costumes) log.AppendLine($"  의상 {c.Part} {c.CostumeId} {c.DisplayName}: {c.Status} {c.Detail}");
                        foreach (var n in plan.Notes) log.AppendLine("  참고: " + n);
                        log.AppendLine($"  자세: 꺼냄 {plan.WeaponDrawn}, 직업 {plan.StanceJob}, 조건 {plan.StanceCondition}, 클립 {plan.Idle}, 도입 {plan.Intro}, 도입 무기 구간 {plan.IntroToolSpans.Count}");
                        var view = CharacterAssembler.Build(files, bundles, plan, shader, log);
                        void Detail(string t)
                        {
                            var arm = view.FindBone("_L Forearm"); var foot = view.FindBone("_L Foot");
                            Capture(cam, view, Path.Combine(outDir, t + "_front.png"), 180, 5, 3.0f, new Vector3(0, 0.85f, 0));
                            if (arm) Capture(cam, view, Path.Combine(outDir, t + "_arm.png"), 120, 5, 0.75f, arm.position, 768, 768);
                            if (foot) Capture(cam, view, Path.Combine(outDir, t + "_foot.png"), 150, 15, 0.6f, foot.position + new Vector3(0, -0.03f, 0), 768, 768);
                        }
                        Detail(tag);
                        var om = view.GetComponent<CharacterMotion>();
                        if (om && social != null)
                        {
                            var sa = data.SocialActions.Find(x => x.Id == social);
                            if (sa == null) log.AppendLine($"  소셜 모션 없음: {social} ({data.SocialError})");
                            else
                            {
                                var am = SocialMotionLoader.Load(files, bundles, data, view, shader, sa, socialAct ?? SocialMotionLoader.PickAction(sa, new System.Random(1)), s2 => log.AppendLine("  " + s2), out string serr);
                                if (am == null) log.AppendLine("  소셜 모션 실패: " + serr);
                                else om.PlayAction(am, 0, false);
                            }
                        }
                        string otimes = Arg("-maMotionTimes");
                        if (om && otimes != null)
                            foreach (var ts in otimes.Split(';'))
                            {
                                if (!float.TryParse(ts, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float t)) continue;
                                om.SampleAll(t);
                                Capture(cam, view, Path.Combine(outDir, $"{tag}_t{ts}_front.png"), 180, 5, 3.6f, new Vector3(0, 0.88f, 0));
                                var act = om.CurrentAction;
                                if (act != null)
                                    foreach (var pr in act.Props)
                                        if (pr.Go && pr.Go.activeInHierarchy)
                                        {
                                            var rs = pr.Go.GetComponentsInChildren<Renderer>();
                                            var bb = rs.Length > 0 ? rs[0].bounds : default;
                                            log.AppendLine($"    t{ts} 소품 {pr.Go.name} 위치 {pr.Go.transform.position} 렌더러 {rs.Length} 범위 {bb.center}/{bb.size} 켜짐 {(rs.Length > 0 && rs[0].enabled)}");
                                        }
                                var hd = social != null ? view.FindBone("_Head") : null;
                                if (hd) Capture(cam, view, Path.Combine(outDir, $"{tag}_t{ts}_upper.png"), 180, 5, 1.6f, hd.position + new Vector3(0, -0.25f, 0), 768, 768);
                            }
                        if (variants)
                        {
                            view.SetBodyCut(false);
                            Detail(tag + "_nocut");
                            view.SetBodyCut(true);
                            var body = view.Renderers.Find(x => x.partKey == "body").renderer;
                            if (body) { body.gameObject.SetActive(false); Detail(tag + "_nobody"); body.gameObject.SetActive(true); }
                            foreach (var (key, r) in view.Renderers)
                                if (key.StartsWith("costume:") && r.name.StartsWith("Mesh_Skin")) r.gameObject.SetActive(false);
                            Detail(tag + "_noclothskin");
                        }
                        UnityEngine.Object.DestroyImmediate(view.gameObject);
                    }
                    File.WriteAllText(Path.Combine(outDir, "log.txt"), log.ToString(), new UTF8Encoding(false));
                    EditorApplication.Exit(0);
                    return;
                }

                // -maAge 나이 : 기본 촬영의 나이를 바꾼다(예: 12 → 어린 외형).
                // -maBodyType 체형 ID : 기본 촬영의 체형을 바꾼다(Default, Fat, Muscular, Thin 등 BodyTypes 시트 ID).
                int age = int.TryParse(Arg("-maAge"), out var a) ? a : 0;
                string bodyType = Arg("-maBodyType");
                if (bodyType != null) log.AppendLine($"체형 ID: {string.Join(", ", data.BodyTypes.Keys)}");
                foreach (var preset in StateFactory.DefaultPresets)
                {
                    var s = StateFactory.FromPreset(data, preset);
                    if (age > 0) s.appearance.age = age;
                    if (bodyType != null) s.appearance.bodyType = bodyType;
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

    /// <summary>
    /// 공개 범위·필수 외형 대체·부분 복원 검사 시험(Equipment.md·Appearance.md·SaveLoad.md).
    ///   Unity.exe -batchmode -projectPath <프로젝트> -executeMethod InstallSmokeTest.Policy -maInstall <설치 폴더> -maOut <출력 폴더>
    /// 에디터에서 Disclosure.SimulatePublic 으로 공개 빌드 판정을 켜고 조립 계획·실제 조립 결과를 확인한다. 실패한 확인은 종료 코드 1.
    /// 결과는 policy.txt, 공개 판정 조립 촬영은 policy_*.png.
    /// </summary>
    public static void Policy()
    {
        var log = new StringBuilder();
        int failed = 0;
        void Check(bool ok, string what) { log.AppendLine((ok ? "[통과] " : "[실패] ") + what); if (!ok) failed++; }
        string outDir = Arg("-maOut") ?? Path.Combine(Path.GetTempPath(), "MobiAtelierPolicy");
        Directory.CreateDirectory(outDir);
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = StageBuilder.CreateStage();
            var shader = Shader.Find("MobiAtelier/Character");
            var check = InstallValidator.Check(Arg("-maInstall") ?? AppSettings.DefaultInstallPath);
            if (!check.Ok) throw new Exception("설치 경로 검사 실패: " + check.Message);
            var files = check.Files; var data = check.Data;

            // 1. 시각 해석
            Check(Disclosure.TryParse("KST", "2026-10-15 06:00:00", out var t1) && t1 == new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc), "KST 시각을 UTC 로 해석");
            Check(Disclosure.TryParse("UTC", "1999-01-01 00:00:00", out var t2) && t2 == new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc), "UTC 시각 해석");
            Check(!Disclosure.TryParse("PST", "2026-10-15 06:00:00", out _), "모르는 시간대는 해석 실패");
            Check(!Disclosure.TryParse("UTC", "2026/10/15", out _), "형식이 다른 시각은 해석 실패");
            Check(!Disclosure.TryParse("KST", "0001-01-01 00:00:00", out _), "UTC 변환 범위를 벗어난 시각은 해석 실패");
            int hidden = 0, unparsed = 0;
            foreach (var c in data.CostumeList) { if (Disclosure.IsHidden(c)) hidden++; if (c.DiscloseUtc == null) unparsed++; }
            log.AppendLine($"Costume {data.CostumeList.Count}행: 공개 시각 미래·해석 실패 {hidden}행(해석 실패 {unparsed}행)");
            var squirrel = data.Costumes["Fashion_Squirrel_OnePiece_00_F"];
            Check(Disclosure.IsHidden(squirrel), $"미래 공개 의상 숨김 판정 ({squirrel.DiscloseText})");
            Check(!Disclosure.IsHidden(data.Costumes["Fashion_NewbieSword_Top"]), "초보자 상의는 공개");
            Disclosure.NowOverride = squirrel.DiscloseUtc.Value.AddSeconds(1);
            Check(!Disclosure.IsHidden(squirrel), "공개 시각이 지나면 공개");
            Disclosure.NowOverride = null;

            string HiddenGroup(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, AppearancePartData>> map, string variant, string fallback)
            {
                foreach (var kv in map)
                {
                    var p = GameData.Resolve(map, kv.Key, variant, fallback);
                    if (p != null && p.HideOnCustomize && p.Model(false, false) != null) return kv.Key;
                }
                return null;
            }
            string hiddenHair = HiddenGroup(data.Hairs, "Default", "Default");
            string hiddenEyes = HiddenGroup(data.Eyes, "Head_00", "Head_00");
            string hiddenSkin = null;
            foreach (var c in data.SkinColors.Values) if (c.HideOnCustomize) { hiddenSkin = c.Id; break; }
            log.AppendLine($"시험용 숨김 항목: 머리 Temp_Head_99, 헤어 {hiddenHair}, 눈 {hiddenEyes}, 피부색 {hiddenSkin}");

            EditState Restricted()
            {
                var s = StateFactory.FromPreset(data, StateFactory.DefaultPresets[1]);
                s.appearance.head = "Temp_Head_99";
                s.appearance.hair = hiddenHair;
                s.appearance.eyes = hiddenEyes;
                s.appearance.skinColor = hiddenSkin;
                s.costumes.Add(new CostumeSlotState { part = "Top", costumeId = "Fashion_Squirrel_OnePiece_00_F" });
                s.costumes.Add(new CostumeSlotState { part = "Bottom", costumeId = "Fashion_NewbieSword_Bottom" });
                s.costumes.Add(new CostumeSlotState { part = "Hat", kind = CostumeSlotState.KindResource, resource = "Female_ObjectHead_Helmet_01" });
                s.costumes[0].dyes.Add(new ColorOverride { slot = "Slot1", color = "#FF00FF" });
                return s;
            }

            using (var bundles = new BundleCache(files))
            {
                CharacterView Build(string name, AssemblyPlan plan, bool shoot)
                {
                    var view = CharacterAssembler.Build(files, bundles, plan, shader);
                    if (shoot) Capture(cam, view, Path.Combine(outDir, "policy_" + name + ".png"), 180, 5, 3.6f, new Vector3(0, 0.88f, 0));
                    return view;
                }
                bool Has(AssemblyPlan p, string key) => p.Parts.Exists(x => x.Key == key);
                bool Rendered(CharacterView v, string key) => v.Renderers.Exists(r => r.partKey == key);
                void Dump(string name, EditState s, AssemblyPlan p)
                {
                    log.AppendLine($"[{name}] 파츠: {string.Join(", ", p.Parts.ConvertAll(x => x.Key + "=" + x.Model))}");
                    foreach (var x in p.Substitutes) log.AppendLine($"  대체 {x.part}: {x.selected} → {x.shown} ({x.reason})");
                    foreach (var line in RestoreCheck.Inspect(s, data, p)) log.AppendLine("  복원 안내: " + line);
                }

                // 2. 공개 빌드 판정: 직접 리소스·숨김 외형·미공개 의상이 든 상태
                Disclosure.SimulatePublic = true;
                var rs = Restricted();
                File.WriteAllText(Path.Combine(outDir, "restricted.json"), JsonUtility.ToJson(rs, true), new UTF8Encoding(false));
                string before = JsonUtility.ToJson(rs);
                var rp = PlanResolver.Resolve(rs, data, files);
                var rv = Build("public_restricted", rp, true);
                Dump("공개 판정", rs, rp);
                Check(rp.Costumes.Find(c => c.Part == "Top").Status == CostumeStatus.Restricted, "미공개 상의는 Restricted");
                Check(rp.Costumes.Find(c => c.Part == "Hat").Status == CostumeStatus.Restricted, "직접 리소스 모자는 Restricted");
                Check(!Has(rp, "costume:Top") && !Has(rp, "costume:Hat") && !Rendered(rv, "costume:Top") && !Rendered(rv, "costume:Hat"), "제한 의상은 조립·렌더러에 없음");
                Check(Rendered(rv, "costume:Bottom"), "미적용 원피스는 하의를 가리지 않음(하의 표시)");
                Check(rp.Parts.Find(x => x.Key == "head").Model == data.Heads["Head_00"].Model(false, false), "숨김 머리 대신 Head_00 모델을 조립");
                Check(rp.Substitute("head")?.shown == "Head_00", "숨김 머리 → 기본 Head_00");
                Check(hiddenHair == null || rp.Substitute("hair") != null, "숨김 헤어 → 공개 기본값");
                Check(hiddenEyes == null || rp.Substitute("eyes") != null, "숨김 눈 → 공개 기본값");
                Check(hiddenSkin == null || rp.Substitute("skinColor") != null, "숨김 피부색 → 공개 기본값");
                Check(Rendered(rv, "head") && Rendered(rv, "hair") && Rendered(rv, "eyes"), "필수 외형 렌더러 존재");
                Check(JsonUtility.ToJson(rs) == before, "선택 ID·염색값은 상태에 그대로 보존");
                Check(RestoreCheck.Inspect(rs, data, rp).Count >= 5, "부분 복원 안내 항목 존재");
                UnityEngine.Object.DestroyImmediate(rv.gameObject);

                // 3. 에디터 판정: 같은 상태가 그대로 출력된다
                Disclosure.SimulatePublic = false;
                var ep = PlanResolver.Resolve(rs, data, files);
                var ev = Build("editor_restricted", ep, true);
                Dump("에디터 판정", rs, ep);
                Check(Has(ep, "costume:Top") && Has(ep, "costume:Hat") && ep.Substitute("head") == null, "에디터에서는 숨김·직접 리소스 출력");
                UnityEngine.Object.DestroyImmediate(ev.gameObject);
                Disclosure.SimulatePublic = true;

                // 4. 필수 외형 호환 대체·복원
                var fs = StateFactory.FromPreset(data, StateFactory.DefaultPresets[1]);
                fs.appearance.head = "Head_04_F";
                var fp = PlanResolver.Resolve(fs, data, files);
                Check(fp.Substitute("head") == null, "여성 Head_04_F 그대로 표시");
                var ms = fs.Clone(); ms.appearance.gender = "Male";
                var mp = PlanResolver.Resolve(ms, data, files);
                var mv = Build("male_from_head04f", mp, true);
                Dump("남성으로 변경", ms, mp);
                Check(mp.Substitute("head")?.selected == "Head_04_F" && ms.appearance.head == "Head_04_F", "남성: 머리 대체, 원래 선택 보관");
                Check(Rendered(mv, "head") && Rendered(mv, "eyes"), "남성: 머리·눈이 빠지지 않음");
                Check(RestoreCheck.Inspect(ms, data, mp).Count == 0, "정상적인 호환 대체는 부분 복원 안내 대상 아님");
                UnityEngine.Object.DestroyImmediate(mv.gameObject);
                var back = ms.Clone(); back.appearance.gender = "Female";
                Check(PlanResolver.Resolve(back, data, files).Substitute("head") == null, "여성으로 되돌리면 원래 머리 복원");
                var young = fs.Clone(); young.appearance.age = 12;
                Check(PlanResolver.Resolve(young, data, files).Substitute("head") != null, "12세: MinAge 17 머리 대체");
                var picked = ms.Clone(); picked.appearance.head = "Head_02";
                var pp = PlanResolver.Resolve(picked, data, files);
                picked.appearance.gender = "Female";
                Check(pp.Substitute("head") == null && PlanResolver.Resolve(picked, data, files).Substitute("head") == null && picked.appearance.head == "Head_02",
                    "직접 새로 고르면 보관값이 이후 편집을 덮어쓰지 않음");

                // 5. 없는 ID·사라진 염색 파트·해석 못 한 염색값
                var ns = StateFactory.FromPreset(data, StateFactory.DefaultPresets[1]);
                ns.appearance.hair = "Hair_NotExist";
                ns.costumes.Add(new CostumeSlotState { part = "Top", costumeId = "Fashion_NotExist" });
                var bottom = new CostumeSlotState { part = "Bottom", costumeId = "Fashion_NewbieSword_Bottom" };
                bottom.dyes.Add(new ColorOverride { slot = "Slot5", color = "#FF0000" });
                bottom.dyes.Add(new ColorOverride { slot = "Slot1", color = "red" });
                bottom.dyes.Add(new ColorOverride { slot = "Skin", color = "#00FF00" });
                ns.costumes.Add(bottom);
                var np = PlanResolver.Resolve(ns, data, files);
                var nv = Build("missing", np, true);
                Dump("누락", ns, np);
                Check(np.Issues.Exists(i => i.Key == "hair" && i.Kind == IssueKind.UnknownId) && np.Substitute("hair") != null, "없는 헤어 → 문제 + 기본값 대체");
                Check(np.Issues.Exists(i => i.Key == "costume:Top" && i.Kind == IssueKind.UnknownId), "없는 의상 ID 문제");
                Check(np.Issues.FindAll(i => i.Key == "costume:Bottom" && i.Kind == IssueKind.DyeUnresolved).Count >= 2, "해석 못 한 염색값·모르는 슬롯 문제");
                bool slot5 = nv.Dyes.Exists(d => d.PartKey == "costume:Bottom" && d.Slot == "Slot5");
                Check(slot5 || np.Issues.Exists(i => i.FromBuild && i.Key == "costume:Bottom" && i.Message.Contains("Slot5")), "모델에 없는 염색 파트는 조립 뒤 문제로 기록");
                Check(!nv.Dyes.Exists(d => d.PartKey == "costume:Bottom" && d.Slot == "Skin"), "외형 슬롯 이름은 의상 파트에 적용하지 않음");
                log.AppendLine($"  하의 염색 파트: {string.Join(", ", nv.DyeParts("costume:Bottom").ConvertAll(d => d.Slot))}");
                UnityEngine.Object.DestroyImmediate(nv.gameObject);

                // 6. 공개 기본값도 없으면 실패(PlanException)
                var saved = new System.Collections.Generic.List<(AppearancePartData, bool)>();
                foreach (var h in data.Heads.Values) { saved.Add((h, h.HideOnCustomize)); h.HideOnCustomize = true; }
                bool threw = false;
                try { PlanResolver.Resolve(StateFactory.FromPreset(data, StateFactory.DefaultPresets[1]), data, files); }
                catch (PlanException e) { threw = true; log.AppendLine("  " + e.Message); }
                foreach (var (h, v) in saved) h.HideOnCustomize = v;
                Check(threw, "공개 머리가 하나도 없으면 PlanException");

                // 7. 새 작업 기본 코디는 문제 없음
                foreach (var preset in StateFactory.DefaultPresets)
                {
                    var s = StateFactory.WithStarterCostumes(data, StateFactory.FromPreset(data, preset));
                    var p = PlanResolver.Resolve(s, data, files);
                    var v = Build("starter_" + preset, p, false);
                    Check(RestoreCheck.CharacterProblems(p).Count == 0, $"기본 코디 {preset} 문제 없음 {string.Join(" / ", RestoreCheck.CharacterProblems(p))}");
                    UnityEngine.Object.DestroyImmediate(v.gameObject);
                }

                PolicyFailuresAndTransactions(files, data, bundles, shader, cam, outDir, Check, log);
            }
        }
        catch (Exception e) { log.AppendLine(e.ToString()); Debug.LogException(e); failed++; }
        Disclosure.SimulatePublic = false;
        Disclosure.NowOverride = null;
        log.AppendLine(failed == 0 ? "모두 통과" : $"실패 {failed}개");
        File.WriteAllText(Path.Combine(outDir, "policy.txt"), log.ToString(), new UTF8Encoding(false));
        EditorApplication.Exit(failed == 0 ? 0 : 1);
    }

    // 설치 파일은 수정하지 않는다. 시험 중 메모리의 index 항목만 가렸다가 finally 에서 복구한다.
    static void PolicyFailuresAndTransactions(InstallFiles files, GameData data, BundleCache bundles, Shader shader,
        Camera cam, string outDir, Action<bool, string> check, StringBuilder log)
    {
        var normal = StateFactory.WithStarterCostumes(data, StateFactory.FromPreset(data, StateFactory.DefaultPresets[1]));
        File.WriteAllText(Path.Combine(outDir, "normal.json"), JsonUtility.ToJson(normal, true), new UTF8Encoding(false));
        string AssetOf(string model) => BundleCache.AssetRoot + files.ReadSjon(files.ModelDefPath(model)).Get("AssetPathToLoad").Str();
        void WithoutAsset(string asset, Action run)
        {
            uint loc = files.Assets.PathToLoc[asset];
            files.Assets.PathToLoc.Remove(asset);
            try { run(); }
            finally { files.Assets.PathToLoc[asset] = loc; }
        }
        CharacterView Valid(EditState s, out AssemblyPlan p) => CharacterAssembler.BuildValidated(files, bundles, s, data, shader, out p);

        foreach (var gender in new[] { "Male", "Female" }) foreach (int age in new[] { 12, 17 })
        {
            var s = normal.Clone(); s.appearance.gender = gender; s.appearance.age = age; s.appearance.head = "Head_04_F";
            s.appearance.bodyType = age == 12 ? "Thin" : "Fat";
            var v = Valid(s, out var p);
            check(v.Renderers.Exists(x => x.partKey == "head") && v.Renderers.Exists(x => x.partKey == "hair") && v.Renderers.Exists(x => x.partKey == "eyes") &&
                !p.Issues.Exists(i => i.IsFailure), $"{gender} {age}세 {s.appearance.bodyType}: 필수 외형 실제 조립 유지");
            CharacterAssembler.Release(v);
        }
        int viewsBefore = UnityEngine.Object.FindObjectsByType<CharacterView>(FindObjectsSortMode.None).Length;
        WithoutAsset(AssetOf("Female_Body_00"), () =>
        {
            bool rejected = false;
            try { CharacterAssembler.Release(Valid(normal, out _)); }
            catch (PlanException) { rejected = true; }
            check(rejected && UnityEngine.Object.FindObjectsByType<CharacterView>(FindObjectsSortMode.None).Length == viewsBefore,
                "필수 몸 조립 실패는 거절하고 임시 캐릭터를 남기지 않음");
        });

        var selected = normal.Clone(); selected.appearance.head = "Head_04_F";
        WithoutAsset(AssetOf(data.Heads["Head_04_F"].FemaleModel), () =>
        {
            var v = Valid(selected, out var p);
            check(p.Substitute("head") != null && v.Renderers.Exists(x => x.partKey == "head"), "필수 머리 프리팹 누락 시 실제 공개 기본 머리로 조립");
            check(p.Issues.Exists(i => i.FromBuild && i.Key == "head" && i.IsFailure), "실제 프리팹 누락을 부분 복원 사유에 보존");
            CharacterAssembler.Release(v);
        });
        var selectedMouth = GameData.Resolve(data.Mouths, normal.appearance.mouth, normal.appearance.head, "Head_00");
        WithoutAsset(AssetOf(selectedMouth.FemaleModel), () =>
        {
            var v = Valid(normal, out var p);
            check(p.Substitute("mouth") != null && p.Issues.Exists(i => i.Key == "mouth" && i.IsFailure), "입 텍스처 누락도 공개 기본 입으로 대체하고 안내");
            CharacterAssembler.Release(v);
        });

        // 한 의상의 메시를 메모리에서 제거한다. 실패한 상의의 하의 가림 효과도 사라져야 한다.
        var coat = data.CostumeList.First(c => c.CostumeType == "Top" && !Disclosure.IsHidden(c) && c.HidingParts.Contains("Bottom") &&
            PlanResolver.CheckCostume(c, false, false, out _, out _) == CostumeStatus.Applied);
        var dressed = normal.Clone(); dressed.Costume("Top").costumeId = coat.Id;
        string model = coat.FemaleModels.First(m => !string.IsNullOrEmpty(m) && m != "Invalid");
        var prefab = bundles.LoadAsset<GameObject>(AssetOf(model));
        var meshes = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => (r, r.sharedMesh)).ToArray();
        string defPath = files.ModelDefPath(model);
        string prefix = defPath.Substring(0, defPath.LastIndexOf('/') + 1) + model + "_";
        var smms = files.Resources.PathToLoc.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Key.EndsWith(".SkinnedModularMesh.bytes", StringComparison.Ordinal)).ToArray();
        var fbxs = files.Assets.PathToLoc.Where(kv => kv.Key.EndsWith("/" + model + ".fbx", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var (r, _) in meshes) r.sharedMesh = null;
        foreach (var kv in smms) files.Resources.PathToLoc.Remove(kv.Key);
        foreach (var kv in fbxs) files.Assets.PathToLoc.Remove(kv.Key);
        try
        {
            var v = Valid(dressed, out var p);
            check(!v.Renderers.Exists(x => x.partKey == "costume:Top") && v.Renderers.Exists(x => x.partKey == "costume:Bottom"), "메시 없는 상의를 제외하고 하의 숨김 효과도 제거");
            check(RestoreCheck.Inspect(dressed, data, p).Any(x => x.Contains("메시")), "누락 메시를 실제 조립 뒤 복원 안내에 포함");
            CharacterAssembler.Release(v);
        }
        finally
        {
            foreach (var (r, mesh) in meshes) r.sharedMesh = mesh;
            foreach (var kv in smms) files.Resources.PathToLoc[kv.Key] = kv.Value;
            foreach (var kv in fbxs) files.Assets.PathToLoc[kv.Key] = kv.Value;
        }

        // 실제 AppController의 편집·부분 복원·이력 경로. 저장은 지정한 시험 폴더로만 보낸다.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var host = new GameObject("PolicyController");
        var app = host.AddComponent<AppController>(); app.enabled = false; app.characterShader = shader;
        void Set(string field, object value) => typeof(AppController).GetField(field, flags).SetValue(app, value);
        object Get(string field) => typeof(AppController).GetField(field, flags).GetValue(app);
        object Call(string method, params object[] args) => typeof(AppController).GetMethod(method, flags).Invoke(app, args);
        var ui = new AppUI(host, AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/UI/MobiAtelierPanel.asset"));
        var camera = cam.gameObject.AddComponent<CameraController>(); camera.Cam = cam;
        var bg = new StudioBackground(cam, bundles, Shader.Find("MobiAtelier/StudioGradient"));
        var autosave = new Autosave(() => app.State);
        var dataField = typeof(JsonStore).GetField("dataDir", BindingFlags.Static | BindingFlags.NonPublic);
        object oldDataDir = dataField.GetValue(null);
        dataField.SetValue(null, Path.Combine(outDir, "transaction-data"));
        Set("files", files); Set("data", data); Set("bundles", bundles); Set("ui", ui); Set("cameraCtl", camera);
        Set("settings", new AppSettings { helpAcknowledged = true }); Set("background", bg); Set("autosave", autosave);
        try
        {
            var initial = Call("Stage", normal);
            Call("StartWith", initial, true, null);
            var history = (EditHistory)Get("history");
            // 한 편집에 외형·색·무기 자세를 넣고 같은 이력 단위로 복원되는지 확인한다.
            Action<EditState> change = s => { s.appearance.head = "Head_04_F"; s.appearance.heightModifier = 0.5f; s.weaponPose.stance = WeaponPoseState.Alert;
                s.Costume("Top").dyes.Add(new ColorOverride { slot = "Slot1", color = "#123456" }); };
            Call("Edit", change);
            check(history.CanUndo, "정상 꾸미기 편집을 이력에 기록");
            camera.Load(new CameraState { valid = true, yaw = 73, pitch = 12, distance = 4, pan = new Vector2(0.2f, 0.1f), lensMultiplier = 3 });
            string cameraBefore = JsonUtility.ToJson(camera.Save());
            string edited = JsonUtility.ToJson(EditHistory.Snapshot(app.State));
            Call("Undo");
            check(app.State.appearance.head == normal.appearance.head && history.CanRedo && JsonUtility.ToJson(camera.Save()) == cameraBefore,
                "실행 취소는 외형을 복원하고 현재 카메라·다시 실행 분기를 유지");
            Call("Redo");
            check(JsonUtility.ToJson(EditHistory.Snapshot(app.State)) == edited && JsonUtility.ToJson(camera.Save()) == cameraBefore,
                "다시 실행은 외형·염색·무기 자세를 함께 복원하고 카메라 유지");
            string originalBg = app.State.background;
            Call("SetBackground", "BG_Gradation2");
            Call("Undo");
            check(app.State.background == originalBg && JsonUtility.ToJson(camera.Save()) == cameraBefore, "배경 실행 취소도 현재 카메라 유지");
            Call("Redo");
            check(app.State.background == "BG_Gradation2" && JsonUtility.ToJson(camera.Save()) == cameraBefore, "배경 다시 실행도 현재 카메라 유지");
            autosave.Flush();
            string stateBefore = JsonUtility.ToJson(app.State), savedBefore = File.ReadAllText(JsonStore.AutosavePath);
            var viewBefore = Get("view");
            var imported = app.State.Clone(); imported.Costume("Top").costumeId = "MissingPolicyCostume";
            imported.Costume("Bottom").dyes.Add(new ColorOverride { slot = "Slot5", color = "not-a-color" });
            Call("PrepareImport", imported, "policy-import.json");
            check(ui.ModalOpen && Get("pending") != null && ReferenceEquals(Get("view"), viewBefore), "문제 있는 가져오기는 기존 장면을 유지하며 부분 복원 승인 대기");
            Call("Update");
            check(File.ReadAllText(JsonStore.AutosavePath) == savedBefore, "승인 대기 중 자동 저장 파일 유지");
            ui.HandleModalEscape();
            check(Get("pending") == null && JsonUtility.ToJson(app.State) == stateBefore && File.ReadAllText(JsonStore.AutosavePath) == savedBefore && history.CanUndo,
                "부분 복원 취소는 준비본만 폐기하고 상태·파일·이력 유지");
            ui.CloseModal();
            WithoutAsset(AssetOf(data.Heads["Head_02"].FemaleModel), () => Call("Edit", (Action<EditState>)(s => s.appearance.head = "Head_02")));
            check(ReferenceEquals(Get("view"), viewBefore) && JsonUtility.ToJson(app.State) == stateBefore && File.ReadAllText(JsonStore.AutosavePath) == savedBefore && history.CanUndo,
                "실제 조립 실패 편집은 기존 장면·상태·파일·이력 유지");
            // 이력 대상의 리소스가 사라진 경우도 동일하게 거절한다.
            // 현재 이력의 맨 위는 배경 편집이다. 표시 중인 머리 읽기 실패도 재평가한다.
            WithoutAsset(AssetOf(data.Heads["Head_04_F"].FemaleModel), () => Call("Undo"));
            check(ReferenceEquals(Get("view"), viewBefore) && JsonUtility.ToJson(app.State) == stateBefore && history.CanUndo && !history.CanRedo,
                "실행 취소 대상 리소스가 사라지면 현재 장면과 이력 분기를 유지");
            WithoutAsset(AssetOf("Female_Body_00"), () => Call("PrepareImport", imported, "policy-import.json"));
            check(ReferenceEquals(Get("view"), viewBefore) && JsonUtility.ToJson(app.State) == stateBefore && File.ReadAllText(JsonStore.AutosavePath) == savedBefore && history.CanUndo,
                "필수 몸을 준비하지 못한 가져오기는 장면·자동 저장·이력을 유지");
            ui.CloseModal();
            var brokenBg = new StudioBackgroundData { Id = "PolicyMissingBackground", Prefabs = new List<string> { "MissingPolicyBackground.prefab" } };
            data.Backgrounds.Add(brokenBg);
            try
            {
                var bgImport = app.State.Clone(); bgImport.background = brokenBg.Id;
                string bgBefore = bg.CurrentId;
                Call("PrepareImport", bgImport, "policy-background.json");
                var prepared = Get("pending");
                var messages = (List<string>)Call("InspectPrepared", prepared);
                check(messages.Any(x => x.Contains("배경 리소스")) && bg.CurrentId == bgBefore, "실제 배경 프리팹 누락도 교체 전 안내하고 기존 배경 유지");
                ui.HandleModalEscape(); ui.CloseModal();
            }
            finally { data.Backgrounds.Remove(brokenBg); }
            Call("PrepareImport", imported, "policy-import.json");
            Call("FinishImport", Get("pending"), "policy-import.json", true);
            check(app.State.Costume("Top").costumeId == "MissingPolicyCostume" && !history.CanUndo && !history.CanRedo,
                "부분 복원 승인 후에만 선택 ID 보존·작업 교체·이력 삭제");
            check(app.State.Costume("Bottom").dyes.Exists(d => d.color == "not-a-color"), "해석되지 않은 염색값은 승인 후에도 그대로 보존");
        }
        finally
        {
            Set("bundles", null); // 상위 using 이 소유한다.
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(camera);
            dataField.SetValue(null, oldDataDir);
        }
    }

    static void DescribePrefab(GameObject go, StringBuilder log)
    {
        var t = go.transform;
        log.AppendLine($"  루트 위치 {t.position} 회전 {t.eulerAngles} 크기 {t.localScale}");
        var comps = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var c in go.GetComponentsInChildren<Component>(true))
        {
            string k = c ? c.GetType().Name : "(없는 스크립트)";
            comps[k] = comps.TryGetValue(k, out var v) ? v + 1 : 1;
        }
        log.AppendLine("  컴포넌트: " + string.Join(", ", System.Linq.Enumerable.Select(comps, kv => kv.Key + "×" + kv.Value)));
        var shaders = new System.Collections.Generic.Dictionary<string, int>();
        Bounds b = default; bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            foreach (var m in r.sharedMaterials)
            {
                string k = !m ? "(재질 없음)" : !m.shader ? m.name + ":(셰이더 없음)" : m.shader.name + (m.shader.isSupported ? "" : " [미지원]");
                shaders[k] = shaders.TryGetValue(k, out var v) ? v + 1 : 1;
            }
        }
        log.AppendLine($"  렌더러 범위 {(any ? b.ToString() : "-")}");
        log.AppendLine("  셰이더: " + string.Join(", ", System.Linq.Enumerable.Select(shaders, kv => kv.Key + "×" + kv.Value)));
        var seen = new System.Collections.Generic.HashSet<Shader>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (!m || !m.shader || !seen.Add(m.shader)) continue;
                var tags = new System.Collections.Generic.List<string>();
                for (int p = 0; p < m.shader.passCount; p++)
                    tags.Add(m.shader.FindPassTagValue(p, new UnityEngine.Rendering.ShaderTagId("LightMode")).name);
                log.AppendLine($"  패스 {m.shader.name}: {string.Join(", ", tags)} (렌더러 {r.name}, 큐 {m.renderQueue})");
            }
        foreach (var l in go.GetComponentsInChildren<Light>(true))
            log.AppendLine($"  조명 {l.name}: {l.type} {l.color} 세기 {l.intensity} 그림자 {l.shadows} 모드 {l.lightmapBakeType} 회전 {l.transform.eulerAngles} 켜짐 {l.isActiveAndEnabled}");
        int lm = 0; foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) if (r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE) lm++;
        log.AppendLine($"  라이트맵 인덱스를 가진 렌더러 {lm}개");
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterials.Length == 0) continue;
            var m = r.sharedMaterial;
            if (go.GetComponentsInChildren<Renderer>(true).Length > 6) break;   // 작은 프리팹만 재질 값을 자세히 쓴다
            log.AppendLine($"  렌더러 {r.name} ({r.GetType().Name}) 위치 {r.transform.position} 크기 {r.transform.lossyScale} 그림자 받기 {r.receiveShadows} 드리우기 {r.shadowCastingMode}");
            if (!m || !m.shader) continue;
            var sh = m.shader;
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                string pn = sh.GetPropertyName(i);
                switch (sh.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color: log.AppendLine($"    {pn} = {m.GetColor(pn)}"); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range: log.AppendLine($"    {pn} = {m.GetFloat(pn)}"); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture: var tx = m.GetTexture(pn); if (tx) log.AppendLine($"    {pn} = {tx.name}"); break;
                }
            }
            log.AppendLine($"    키워드: {string.Join(" ", m.shaderKeywords)} 큐 {m.renderQueue}");
        }
    }

    static void Capture(Camera cam, CharacterView ch, string file, float yaw, float pitch, float dist, Vector3 target, int w = 768, int h = 1024)
    {
        var rot = Quaternion.Euler(pitch, yaw, 0);
        cam.transform.SetPositionAndRotation(target + rot * new Vector3(0, 0, -dist), rot);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        cam.targetTexture = rt;
        // 블렌드셰이프 갱신. 원래 꺼진 렌더러(무기 Mesh_MainBuff 등)는 켜지 않는다.
        foreach (var (_, r) in ch.Renderers) if (r && r.enabled && r.gameObject.activeInHierarchy) { r.enabled = false; r.enabled = true; }
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
    }
}
