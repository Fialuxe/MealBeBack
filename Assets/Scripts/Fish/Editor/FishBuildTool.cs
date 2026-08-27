#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 環境魚アセットを 1 クリックで再生成するエディタツール。
///
///   Tools > Fish > Rebuild Fish Assets
///
/// やること (種別ごとに):
///   1. GLB リグ (Assets/models/fishes/*.glb) を一時インスタンス化。
///   2. 背骨・尾びれ・ヒレのボーンを名前で取得。
///   3. 泳ぎ 1 周期のボーン角を進行波の式で事前計算し、ループ AnimationClip にベイク。
///      → 実行時は Animator が phase を引くだけ。毎フレームの三角関数計算をしない (issue #27)。
///   4. 単一ループステートの AnimatorController を生成。
///   5. Animator (CullCompletely) + SkinnedMeshRenderer.updateWhenOffscreen=false を設定した
///      プレハブ変種を保存。*Swim / FishSwimAI は付けない。
///
/// パラメータは旧 SeabreamSwim / TunaSwim / BluefinSwim の実値に合わせてある
/// (体型・ボーン範囲・位相遅れ・尾びれのしなり)。生成物は通常の .anim / .controller / .prefab
/// なので、ベイク後に Animation ウィンドウで手編集してもよい (再ベイクは上書き。残すなら別名保存)。
/// </summary>
public static class FishBuildTool
{
    private const string ModelDir = "Assets/models/fishes";
    private const string AnimDir = "Assets/Animations/fish";
    private const string PrefabDir = "Assets/Resources/prefabs/fish";
    private const int Samples = 48;
    private const float Tau = Mathf.PI * 2f;

    private class SpeciesDef
    {
        public string niceName;
        public string glb;
        public float rootScale;

        // 背骨チェーン (胴体側 → 尾先)。明示指定。
        public string[] spineNames;
        public float bendAngleDeg;    // 尾の最大振れ角
        public float phaseLagTotal;   // 頭→尾の合計位相遅れ [rad] (= 旧 waveLength*2π や lagPerBone*本数)
        public float clipLength;      // 公称 1 ビートの秒数 (= 1 / beatFrequency)
        public Keyframe[] ampKeys;    // 頭(0)→尾(1) の振幅分布

        // 尾びれ等 (背骨の尾の位相 + tailExtraLag で振る)。null 可。
        public string[] tailExtraNames;
        public float tailExtraAngleDeg;
        public float tailExtraLag;    // [rad] 背骨尾からの追加遅れ (むち打ちのしなり)
        public int tailExtraCycles;   // clip 1 周あたりの往復数 (1 = 背骨と同周期、>1 = 速いはためき。整数でループ維持)
    }

    private static readonly SpeciesDef[] Species =
    {
        // 鯛: carangiform。bone_0..8 を 1 本の連続チェーンとして頭→尾へ S 字を流す。
        // 旧 SeabreamSwim: bendAngle 14, waveLength 1.1 (→ 1.1*2π), beatFrequency 1.6,
        //                  ampCurve (0,.05)(.5,.35)(1,1)。
        new SpeciesDef
        {
            niceName = "Seabream", glb = "seabream_rigged_xplus.glb", rootScale = 0.1f,
            spineNames = new[] { "bone_0", "bone_1", "bone_2", "bone_3", "bone_4", "bone_5", "bone_6", "bone_7", "bone_8" },
            bendAngleDeg = 14f, phaseLagTotal = 1.1f * Tau, clipLength = 1f / 1.6f,
            ampKeys = new[] { new Keyframe(0f, 0.05f), new Keyframe(0.5f, 0.35f), new Keyframe(1f, 1f) },
            tailExtraNames = null,
        },
        // ツナ: subcarangiform。背骨 bone_1..7 + 尾びれ bone_8/9 をしならせる。
        // 旧 TunaSwim: bodyAmplitudeDeg 9, phaseOffsetPerBone 0.55 (×6本=3.3), baseBeatFrequency 1.2,
        //             caudalAmplitudeDeg 7, caudalPhaseLag 0.5, ampCurve (0,.05)(.5,.35)(1,1)。
        new SpeciesDef
        {
            niceName = "Tuna", glb = "tuna_rigged_xplus.glb", rootScale = 0.2f,
            spineNames = new[] { "bone_1", "bone_2", "bone_3", "bone_4", "bone_5", "bone_6", "bone_7" },
            bendAngleDeg = 9f, phaseLagTotal = 0.55f * 6f, clipLength = 1f / 1.2f,
            ampKeys = new[] { new Keyframe(0f, 0.05f), new Keyframe(0.5f, 0.35f), new Keyframe(1f, 1f) },
            tailExtraNames = new[] { "bone_8", "bone_9" },
            tailExtraAngleDeg = 7f, tailExtraLag = 0.5f, tailExtraCycles = 1,
        },
        // マグロ: thunniform。胴体はほぼ剛体、尾連鎖 bone_5..8 だけを強く速く打つ。
        //         ヒレ bone_3/4/1 を少し速くはためかせる。
        // 旧 BluefinSwim: maxSwayAngle 22, phaseLagPerBone 55°=0.96rad (×3本=2.88), swimFrequency 1.6,
        //                tailWeightCurve (0,.05)(.5,.25)(1,1), finFrequency 2.4, finAngle 9。
        new SpeciesDef
        {
            niceName = "Bluefin", glb = "bluefin_rigged_xplus.glb", rootScale = 0.2f,
            spineNames = new[] { "bone_5", "bone_6", "bone_7", "bone_8" },
            bendAngleDeg = 22f, phaseLagTotal = (55f * Mathf.Deg2Rad) * 3f, clipLength = 1f / 1.6f,
            ampKeys = new[] { new Keyframe(0f, 0.05f), new Keyframe(0.5f, 0.25f), new Keyframe(1f, 1f) },
            tailExtraNames = new[] { "bone_3", "bone_4", "bone_1" },
            tailExtraAngleDeg = 9f, tailExtraLag = 0f, tailExtraCycles = 2,
        },
    };

    [MenuItem("Tools/Fish/Rebuild Fish Assets")]
    public static void Rebuild()
    {
        EnsureFolder(AnimDir);
        EnsureFolder(PrefabDir);

        int ok = 0;
        foreach (SpeciesDef sp in Species)
        {
            if (BuildOne(sp))
                ok++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[FishBuildTool] 完了: {ok}/{Species.Length} 種を生成しました。");
    }

    private static bool BuildOne(SpeciesDef sp)
    {
        string glbPath = $"{ModelDir}/{sp.glb}";
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
        if (model == null)
        {
            Debug.LogError($"[FishBuildTool] {glbPath} が見つかりません。");
            return false;
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            inst.transform.localScale = Vector3.one * sp.rootScale;

            List<Transform> spine = FindBones(inst.transform, sp.spineNames);
            if (spine.Count < 2)
            {
                Debug.LogError($"[FishBuildTool] {sp.niceName}: 背骨ボーンが {spine.Count} 本しか見つかりません ({string.Join(",", sp.spineNames)})。");
                return false;
            }
            List<Transform> tailExtra = FindBones(inst.transform, sp.tailExtraNames);

            AnimationClip clip = BakeSwimClip(inst.transform, spine, tailExtra, sp);
            string clipPath = $"{AnimDir}/{sp.niceName.ToLowerInvariant()}_swim.anim";
            AssetDatabase.DeleteAsset(clipPath);
            AssetDatabase.CreateAsset(clip, clipPath);

            string ctrlPath = $"{AnimDir}/{sp.niceName.ToLowerInvariant()}.controller";
            AssetDatabase.DeleteAsset(ctrlPath);
            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(
                ctrlPath, AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath));

            Animator animator = inst.GetComponent<Animator>();
            if (animator == null)
                animator = inst.AddComponent<Animator>();
            animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
            if (animator.avatar == null)
                animator.avatar = FindAvatar(glbPath);

            foreach (SkinnedMeshRenderer smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = false;

            string prefabPath = $"{PrefabDir}/{sp.niceName}.prefab";
            PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Debug.Log($"[FishBuildTool] {sp.niceName}: 背骨 {spine.Count} / 尾ヒレ {tailExtra.Count} → {clipPath} / {prefabPath}");
            return true;
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }
    }

    private static AnimationClip BakeSwimClip(Transform root, List<Transform> spine, List<Transform> tailExtra, SpeciesDef sp)
    {
        AnimationClip clip = new AnimationClip { frameRate = 30f };
        AnimationCurve ampCurve = new AnimationCurve(sp.ampKeys);
        int n = spine.Count;

        // 背骨: 頭→尾へ伝播する進行波。
        for (int i = 0; i < n; i++)
        {
            float frac = n > 1 ? (float)i / (n - 1) : 1f;
            float amp = ampCurve.Evaluate(frac) * sp.bendAngleDeg;
            float lag = frac * sp.phaseLagTotal;
            BakeBoneRotation(clip, root, spine[i], sp.clipLength, amp, 1, lag);
        }

        // 尾びれ等: 背骨尾の位相 + 追加遅れ (むち打ちのしなり)。
        if (tailExtra.Count > 0)
        {
            float lag = sp.phaseLagTotal + sp.tailExtraLag;
            int cycles = Mathf.Max(1, sp.tailExtraCycles);
            foreach (Transform t in tailExtra)
                BakeBoneRotation(clip, root, t, sp.clipLength, sp.tailExtraAngleDeg, cycles, lag);
        }

        clip.EnsureQuaternionContinuity();

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    /// <summary>
    /// 1 ボーンぶんの回転カーブを焼く。
    /// angle(u) = amp * sin(2π * cycles * u - phaseLag)。cycles が整数なので u:0→1 で必ずループする。
    /// 回転軸は「体の上方向をボーンローカルへ変換」= リグの癖に依らず必ず左右のうねりになる。
    /// </summary>
    private static void BakeBoneRotation(AnimationClip clip, Transform root, Transform bone,
                                         float clipLength, float ampDeg, int cycles, float phaseLag)
    {
        Quaternion rest = bone.localRotation;
        Vector3 a = bone.InverseTransformDirection(root.up);
        Vector3 axis = a.sqrMagnitude < 1e-6f ? Vector3.up : a.normalized;
        string path = AnimationUtility.CalculateTransformPath(bone, root);

        AnimationCurve cx = new AnimationCurve();
        AnimationCurve cy = new AnimationCurve();
        AnimationCurve cz = new AnimationCurve();
        AnimationCurve cw = new AnimationCurve();

        for (int s = 0; s <= Samples; s++)
        {
            float u = (float)s / Samples;                 // 0..1 (両端で同値)
            float time = u * clipLength;
            float angle = ampDeg * Mathf.Sin(Tau * cycles * u - phaseLag);
            Quaternion q = rest * Quaternion.AngleAxis(angle, axis);
            cx.AddKey(time, q.x);
            cy.AddKey(time, q.y);
            cz.AddKey(time, q.z);
            cw.AddKey(time, q.w);
        }

        clip.SetCurve(path, typeof(Transform), "m_LocalRotation.x", cx);
        clip.SetCurve(path, typeof(Transform), "m_LocalRotation.y", cy);
        clip.SetCurve(path, typeof(Transform), "m_LocalRotation.z", cz);
        clip.SetCurve(path, typeof(Transform), "m_LocalRotation.w", cw);
    }

    private static List<Transform> FindBones(Transform root, string[] names)
    {
        List<Transform> list = new List<Transform>();
        if (names == null)
            return list;
        foreach (string nm in names)
        {
            Transform b = FindDeep(root, nm);
            if (b != null)
                list.Add(b);
        }
        return list;
    }

    private static Avatar FindAvatar(string assetPath)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (o is Avatar av)
                return av;
        }
        return null;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform r = FindDeep(root.GetChild(i), name);
            if (r != null)
                return r;
        }
        return null;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
#endif
