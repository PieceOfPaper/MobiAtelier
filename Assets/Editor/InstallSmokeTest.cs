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
