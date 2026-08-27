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
///   2. 背骨ボーンを名前検索。
///   3. 泳ぎ 1 周期のボーン角を進行波の式で事前計算し、ループ AnimationClip にベイク。
///      → 実行時は Animator が phase を引くだけ。三角関数を毎フレーム回さない (issue #27)。
///   4. 単一ループステートの AnimatorController を生成。
///   5. Animator (CullCompletely) + updateWhenOffscreen=false を設定したプレハブ変種を保存。
///      *Swim / FishSwimAI は付けない。
///
/// 生成物は通常の .anim / .controller / .prefab。ベイク後に Animation ウィンドウで
/// 手編集してもよい (再ベイクは上書きするので、残したい場合は別名保存)。
/// </summary>
public static class FishBuildTool
{
    private const string ModelDir = "Assets/models/fishes";
    private const string AnimDir = "Assets/Animations/fish";
    private const string PrefabDir = "Assets/Resources/prefabs/fish";
    private const int Samples = 48;

    private class SpeciesDef
    {
        public string niceName;
        public string glb;
        public float rootScale;
        public string bonePrefix;
        public int firstBone;
        public int lastBone;
        public float bendAngleDeg;   // 尾の最大振れ角
        public float phaseLagTotal;  // 頭→尾の位相遅れ (rad, チェーン全体)
        public float clipLength;     // 公称 1 ビートの秒数
        public Keyframe[] ampKeys;   // 頭(0)→尾(1) の振幅分布 (体型で変える)
    }

    private static readonly SpeciesDef[] Species =
    {
        // 鯛: carangiform。体全体をゆるく S 字にくねらせる。波長長め・振幅は中盤から立ち上がる。
        new SpeciesDef
        {
            niceName = "Seabream", glb = "seabream_rigged_xplus.glb", rootScale = 0.1f,
            bonePrefix = "bone_", firstBone = 0, lastBone = 8,
            bendAngleDeg = 15f, phaseLagTotal = 1.9f, clipLength = 0.95f,
            ampKeys = new[]
            {
                new Keyframe(0f, 0.12f), new Keyframe(0.5f, 0.45f), new Keyframe(1f, 1f)
            }
        },
        // ツナ: subcarangiform。前半は硬め、後半〜尾で振る。
        new SpeciesDef
        {
            niceName = "Tuna", glb = "tuna_rigged_xplus.glb", rootScale = 0.2f,
            bonePrefix = "bone_", firstBone = 1, lastBone = 8,
            bendAngleDeg = 11f, phaseLagTotal = 1.35f, clipLength = 0.8f,
            ampKeys = new[]
            {
                new Keyframe(0f, 0.05f), new Keyframe(0.55f, 0.3f), new Keyframe(1f, 1f)
            }
        },
        // マグロ: thunniform。胴体はほぼ剛体、尾柄〜尾ビレだけを強く速く打つ。
        new SpeciesDef
        {
            niceName = "Bluefin", glb = "bluefin_rigged_xplus.glb", rootScale = 0.2f,
            bonePrefix = "bone_", firstBone = 1, lastBone = 8,
            bendAngleDeg = 10f, phaseLagTotal = 0.7f, clipLength = 0.62f,
            ampKeys = new[]
            {
                new Keyframe(0f, 0.02f), new Keyframe(0.65f, 0.1f),
                new Keyframe(0.85f, 0.45f), new Keyframe(1f, 1f)
            }
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

            List<Transform> bones = new List<Transform>();
            for (int i = sp.firstBone; i <= sp.lastBone; i++)
            {
                Transform b = FindDeep(inst.transform, sp.bonePrefix + i);
                if (b != null)
                    bones.Add(b);
            }
            if (bones.Count < 2)
            {
                Debug.LogError($"[FishBuildTool] {sp.niceName}: 背骨ボーン ('{sp.bonePrefix}{sp.firstBone}'..) が {bones.Count} 本しか見つかりません。");
                return false;
            }

            AnimationClip clip = BakeSwimClip(inst.transform, bones, sp);
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
            Debug.Log($"[FishBuildTool] {sp.niceName}: {bones.Count} ボーン → {clipPath} / {prefabPath}");
            return true;
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }
    }

    private static AnimationClip BakeSwimClip(Transform root, List<Transform> bones, SpeciesDef sp)
    {
        int n = bones.Count;
        Quaternion[] rest = new Quaternion[n];
        Vector3[] axis = new Vector3[n];
        string[] path = new string[n];

        for (int i = 0; i < n; i++)
        {
            rest[i] = bones[i].localRotation;
            // 「体の上方向」をボーンローカルへ変換したものを横うねりの回転軸にする。
            Vector3 a = bones[i].InverseTransformDirection(root.up);
            axis[i] = a.sqrMagnitude < 1e-6f ? Vector3.up : a.normalized;
            path[i] = AnimationUtility.CalculateTransformPath(bones[i], root);
        }

        AnimationClip clip = new AnimationClip { frameRate = 30f };
        AnimationCurve ampCurve = new AnimationCurve(sp.ampKeys);

        for (int i = 0; i < n; i++)
        {
            float frac = n > 1 ? (float)i / (n - 1) : 1f;
            float amp = ampCurve.Evaluate(frac) * sp.bendAngleDeg;

            AnimationCurve cx = new AnimationCurve();
            AnimationCurve cy = new AnimationCurve();
            AnimationCurve cz = new AnimationCurve();
            AnimationCurve cw = new AnimationCurve();

            for (int s = 0; s <= Samples; s++)
            {
                float u = (float)s / Samples;                       // 0..1 (両端で同値 = シームレスループ)
                float time = u * sp.clipLength;
                float angle = amp * Mathf.Sin(u * Mathf.PI * 2f - frac * sp.phaseLagTotal);
                Quaternion q = rest[i] * Quaternion.AngleAxis(angle, axis[i]);
                cx.AddKey(time, q.x);
                cy.AddKey(time, q.y);
                cz.AddKey(time, q.z);
                cw.AddKey(time, q.w);
            }

            clip.SetCurve(path[i], typeof(Transform), "m_LocalRotation.x", cx);
            clip.SetCurve(path[i], typeof(Transform), "m_LocalRotation.y", cy);
            clip.SetCurve(path[i], typeof(Transform), "m_LocalRotation.z", cz);
            clip.SetCurve(path[i], typeof(Transform), "m_LocalRotation.w", cw);
        }

        clip.EnsureQuaternionContinuity();

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        return clip;
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
