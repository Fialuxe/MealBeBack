using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 魚のマテリアルを「シーンのフォグを受け取るシェーダ」へ実行時に差し替える。
///
/// 背景 (issue #66):
///   環境魚 (鯛/マグロ/ツナ) の glb は KHR_materials_unlit、スケトウダラは alphaMode:BLEND。
///   どちらも glTFast が専用シェーダ (glTF-unlit / 半透明 glTF-pbr) を割り当てるため、
///   <see cref="RenderSettings"/> のフォグ (= FogSystem が触る値) が乗らず、遠近感が壊れる。
///
///   URP 標準の "Universal Render Pipeline/Unlit" は multi_compile_fog + MixFog を含むので、
///   ベースカラー/テクスチャだけ引き継いで不透明 (半透明だったものは α クリップ) に寄せて差し替える。
///   見た目のフラット感 (ライティングを受けない) は維持される。
///
/// ・変換済みマテリアルは元マテリアル単位でキャッシュし共有する (バッチングを壊さない)。
/// ・シェーダ差し替え済みか (shader == URP/Unlit) で冪等。プール再利用で二重変換しない。
/// ・実行時 (Play) 専用。アセットには触れない。
/// </summary>
public static class FishMaterialConformer
{
    private static Shader s_urpUnlit;
    private static bool s_queried;

    // 元マテリアルの InstanceID → フォグ対応へ変換した共有マテリアル
    private static readonly Dictionary<int, Material> s_cache = new Dictionary<int, Material>();

    // glTFast のプロパティ名 → URP のフォールバック
    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
    private static readonly int Surface = Shader.PropertyToID("_Surface");
    private static readonly int Blend = Shader.PropertyToID("_Blend");
    private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
    private static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");
    private static readonly int Cull = Shader.PropertyToID("_Cull");

    /// <summary>root 以下の全 Renderer のマテリアルをフォグ対応へ寄せる。冪等。</summary>
    public static void Conform(GameObject root)
    {
        if (root == null || !Application.isPlaying)
            return;

        Shader unlit = GetUrpUnlit();
        if (unlit == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer rend = renderers[r];
            Material[] shared = rend.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < shared.Length; i++)
            {
                Material src = shared[i];
                if (src == null || src.shader == unlit)
                    continue;

                shared[i] = GetConformed(src, unlit);
                changed = true;
            }

            if (changed)
                rend.sharedMaterials = shared;
        }
    }

    private static Shader GetUrpUnlit()
    {
        if (!s_queried)
        {
            s_urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
            s_queried = true;
            if (s_urpUnlit == null)
                Debug.LogWarning("[FishMaterialConformer] 'Universal Render Pipeline/Unlit' が見つかりません。フォグ差し替えをスキップします。");
        }
        return s_urpUnlit;
    }

    private static Material GetConformed(Material src, Shader unlit)
    {
        int key = src.GetInstanceID();
        if (s_cache.TryGetValue(key, out Material cached) && cached != null)
            return cached;

        var dst = new Material(unlit) { name = src.name + " (Fog)" };

        Texture tex = FindTexture(src, "baseColorTexture", "_BaseMap", "_MainTex");
        if (tex != null)
            dst.SetTexture(BaseMap, tex);

        dst.SetColor(BaseColor, FindColor(src, "baseColorFactor", "_BaseColor", "_Color"));

        // glb は doubleSided:true。両面表示を維持する。
        dst.SetFloat(Cull, src.HasProperty("_Cull") ? src.GetFloat("_Cull") : (float)CullMode.Off);

        bool wasTransparent = src.renderQueue >= 2900
                              || (src.HasProperty(Surface) && src.GetFloat(Surface) >= 0.5f)
                              || src.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");

        if (wasTransparent)
        {
            // 半透明 (スケトウダラ) は α クリップに寄せる。ヒレの抜きは残しつつ深度も書く → フォグが乗る。
            dst.SetFloat(Surface, 0f);
            dst.SetFloat(Blend, 0f);
            dst.SetFloat(ZWrite, 1f);
            dst.SetFloat(AlphaClip, 1f);
            dst.SetFloat(Cutoff, 0.5f);
            dst.EnableKeyword("_ALPHATEST_ON");
            dst.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dst.SetOverrideTag("RenderType", "TransparentCutout");
            dst.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else
        {
            dst.SetFloat(Surface, 0f);
            dst.SetFloat(Blend, 0f);
            dst.SetFloat(ZWrite, 1f);
            dst.SetFloat(AlphaClip, 0f);
            dst.SetOverrideTag("RenderType", "Opaque");
            dst.renderQueue = (int)RenderQueue.Geometry;
        }

        s_cache[key] = dst;
        return dst;
    }

    private static Texture FindTexture(Material m, params string[] names)
    {
        foreach (string n in names)
        {
            if (m.HasProperty(n))
            {
                Texture t = m.GetTexture(n);
                if (t != null)
                    return t;
            }
        }
        return null;
    }

    private static Color FindColor(Material m, params string[] names)
    {
        foreach (string n in names)
        {
            if (m.HasProperty(n))
                return m.GetColor(n);
        }
        return Color.white;
    }
}
