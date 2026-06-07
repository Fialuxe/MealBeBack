using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SquidSwimAnimator
/// ──────────────────────────────────────────────────────────────────────────
/// SkinTokens 等で自動生成されたイカ（squid_rigged_*.glb）のアーマチュアを、
/// クリップ無しで「泳いでいる」ように手続き的に動かすコンポーネント。
///
/// 検証済みリグ構造（squid_rigged_1780823536.glb）:
///   ・ルート          : bone_0（直下に 14 本のチェーンが分岐する中心ハブ）
///   ・外套膜(胴)→尾    : bone_59 → 60 → 61 → 62（先端に尾ヒレの扇 63〜69）
///   ・左右のヒレ        : bone_70-71-72 / bone_73-74-75（左右対称・bone_59 配下）
///   ・腕/触腕 ×13      : bone_1,6,11,14,19,24,29,34,39,44,48,53,56 の各チェーン
///   ・全ボーン scale=1.0、単一ルート、アニメ未収録 → 手続き制御が妥当
///
/// 設計方針:
///   glTF→Unity インポートで軸が反転し得るため、ワールド向きには依存しない。
///   各ボーンを「静止ローカル姿勢 × 微小回転」で曲げる。曲げ軸は実行時に
///   “子ボーンへ向かう方向に垂直” な軸として自動計算するので、どんな
///   ボーンロール／向きでも自然な波（うねり）になる。最大の部分ツリー＝
///   外套膜と判定して胴は控えめ＆ジェット拍動、それ以外（腕）は大きくなびく。
///
/// 使い方:
///   1. .glb をインポートしてシーンに配置。
///   2. SkinnedMeshRenderer を含むそのモデルのルート GameObject に本スクリプトを付与。
///   3. 再生。前進させたい場合は Locomotion を ON にし、Swim Forward 等を調整。
///   ※ rootBone を自動検出できない場合のみ Root Bone Override を手動指定。
/// ──────────────────────────────────────────────────────────────────────────
/// </summary>
[DisallowMultipleComponent]
public class SquidSwimAnimator : MonoBehaviour
{
    public enum AxisDir { Forward, Back, Up, Down, Right, Left }

    [Header("Rig (auto-detected)")]
    [Tooltip("空なら子から SkinnedMeshRenderer を自動取得します。")]
    public SkinnedMeshRenderer skinnedRenderer;
    [Tooltip("自動検出が誤る場合のみ、根本のボーン(bone_0 相当)を指定。")]
    public Transform rootBoneOverride;

    [Header("Global Motion")]
    [Tooltip("全体の動きの速さ（拍動・うねりの周波数）。")]
    [Range(0f, 4f)] public float speed = 1.2f;
    [Tooltip("波が根本から先端へ伝わる量。大きいほどチェーンが S 字にうねる。")]
    [Range(0f, 3f)] public float wavePropagation = 1.0f;

    [Header("Arms / Tentacles（腕・触腕）")]
    [Tooltip("腕の曲げ振幅(度)。先端ほど大きく曲がる。")]
    [Range(0f, 60f)] public float armAmplitude = 22f;
    [Tooltip("主軸と直交する方向への副振幅。立体的な漂いを出す。")]
    [Range(0f, 40f)] public float armSecondaryAmplitude = 10f;
    [Tooltip("腕の波の速さ倍率（全体 speed に対する）。")]
    [Range(0.1f, 3f)] public float armSpeedScale = 0.85f;

    [Header("Mantle / Fins（外套膜・ヒレ）")]
    [Tooltip("胴のうねり振幅(度)。腕より控えめが自然。")]
    [Range(0f, 40f)] public float mantleAmplitude = 9f;
    [Tooltip("ジェット推進の拍動の強さ（前進速度を脈動させる）。")]
    [Range(0f, 1f)] public float jetPulseStrength = 0.45f;

    [Header("Locomotion（前進・遊泳）")]
    [Tooltip("ON で本オブジェクトを前進させる。OFF ならその場で泳ぐ。")]
    public bool enableLocomotion = false;
    [Tooltip("前進する向き（このオブジェクトのローカル基準）。イカの頭/尾の向きに合わせて選ぶ。")]
    public AxisDir swimDirection = AxisDir.Forward;
    [Tooltip("平均前進速度（ワールド単位/秒）。")]
    [Range(0f, 5f)] public float swimForwardSpeed = 0.6f;
    [Tooltip("上下のゆらぎ量。")]
    [Range(0f, 0.5f)] public float bobAmount = 0.05f;
    [Tooltip("左右へのゆらぎ（ヨー揺れ）の角度。")]
    [Range(0f, 20f)] public float swaySway = 6f;

    [Header("Debug")]
    public bool drawBendGizmos = false;

    // ── 内部データ ──────────────────────────────────────────────
    class BoneInfo
    {
        public Transform t;
        public Quaternion restLocal;     // 静止ローカル回転
        public Vector3 bendAxisA;        // 子方向に垂直な曲げ軸(主)
        public Vector3 bendAxisB;        // それと直交する曲げ軸(副)
        public int depthFromRoot;        // ルートからのボーン距離（位相用）
        public float chainFraction;      // 自グループ内 0(根)→1(先端) のテーパ用
        public bool isMantle;            // 外套膜系か（腕系でないか）
        public bool hasChild;
    }

    readonly List<BoneInfo> _bones = new List<BoneInfo>();
    Transform _root;
    Vector3 _homePos;
    Quaternion _homeRot;
    bool _ready;
    float _phase;

    void Reset()
    {
        skinnedRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
    }

    void Start()
    {
        Build();
    }

    /// <summary>骨格を解析して BoneInfo を構築。</summary>
    public void Build()
    {
        _ready = false;
        _bones.Clear();

        if (skinnedRenderer == null)
            skinnedRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        if (skinnedRenderer == null || skinnedRenderer.bones == null || skinnedRenderer.bones.Length == 0)
        {
            Debug.LogError("[SquidSwimAnimator] SkinnedMeshRenderer / bones が見つかりません。", this);
            return;
        }

        Transform[] bones = skinnedRenderer.bones;
        var boneSet = new HashSet<Transform>(bones);

        // ルート決定: override > rootBone > 親が bones に含まれない最上位ボーン
        _root = rootBoneOverride;
        if (_root == null) _root = skinnedRenderer.rootBone;
        if (_root == null || !boneSet.Contains(_root))
        {
            foreach (var b in bones)
                if (b != null && (b.parent == null || !boneSet.Contains(b.parent))) { _root = b; break; }
        }
        if (_root == null) { Debug.LogError("[SquidSwimAnimator] ルートボーン特定不可。", this); return; }

        // 子マップ（bones 集合内の親子関係）
        var children = new Dictionary<Transform, List<Transform>>();
        foreach (var b in bones) if (b != null) children[b] = new List<Transform>();
        foreach (var b in bones)
            if (b != null && b.parent != null && children.ContainsKey(b.parent))
                children[b.parent].Add(b);

        // 各部分ツリー(ルート直下の各チェーン)のボーン数を数え、最大＝外套膜と判定
        var subtreeCount = new Dictionary<Transform, int>();
        int CountSubtree(Transform b)
        {
            int c = 1;
            foreach (var ch in children[b]) c += CountSubtree(ch);
            subtreeCount[b] = c;
            return c;
        }
        Transform mantleTop = null; int maxCount = -1;
        foreach (var topChild in children[_root])
        {
            int c = CountSubtree(topChild);
            if (c > maxCount) { maxCount = c; mantleTop = topChild; }
        }

        // どの top-level 祖先に属するか & 深さを計算しながら BoneInfo 構築
        var info = new Dictionary<Transform, BoneInfo>();
        var groupMaxDepth = new Dictionary<Transform, int>();

        void Traverse(Transform b, Transform topAncestor, int depthFromRoot, int depthInGroup)
        {
            var bi = new BoneInfo
            {
                t = b,
                restLocal = b.localRotation,
                depthFromRoot = depthFromRoot,
                isMantle = (topAncestor == mantleTop),
                hasChild = children[b].Count > 0
            };

            // 曲げ軸: 主たる子へ向かうローカル方向に垂直な 2 軸を作る
            Vector3 fwd;
            if (children[b].Count > 0)
            {
                // 最も遠い(末端へ続く)子をメイン方向とする
                Transform main = children[b][0];
                int best = -1;
                foreach (var ch in children[b])
                    if (subtreeCount[ch] > best) { best = subtreeCount[ch]; main = ch; }
                fwd = main.localPosition;
            }
            else
            {
                // 末端ボーン: 子が無いので便宜上ローカル up を主軸代わりにする
                fwd = Vector3.up;
            }
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.up;
            fwd.Normalize();

            Vector3 axA = Vector3.Cross(fwd, Vector3.up);
            if (axA.sqrMagnitude < 1e-5f) axA = Vector3.Cross(fwd, Vector3.right);
            axA.Normalize();
            Vector3 axB = Vector3.Cross(fwd, axA).normalized;
            bi.bendAxisA = axA;
            bi.bendAxisB = axB;

            info[b] = bi;
            _bones.Add(bi);

            if (!groupMaxDepth.ContainsKey(topAncestor) || depthInGroup > groupMaxDepth[topAncestor])
                groupMaxDepth[topAncestor] = depthInGroup;

            // 後でテーパに使うため一旦 depthInGroup を保存（chainFraction は二周目で）
            bi.chainFraction = depthInGroup; // 仮置き（後で割る）

            foreach (var ch in children[b])
                Traverse(ch, topAncestor, depthFromRoot + 1, depthInGroup + 1);
        }

        // ルート自身は動かさない（基準）。直下チェーンごとに走査。
        foreach (var topChild in children[_root])
            Traverse(topChild, topChild, 1, 0);

        // chainFraction を 0..1 に正規化
        var topOf = new Dictionary<Transform, Transform>();
        foreach (var topChild in children[_root])
        {
            void mark(Transform b, Transform top) { topOf[b] = top; foreach (var ch in children[b]) mark(ch, top); }
            mark(topChild, topChild);
        }
        foreach (var bi in _bones)
        {
            var top = topOf[bi.t];
            int md = Mathf.Max(1, groupMaxDepth[top]);
            bi.chainFraction = Mathf.Clamp01(bi.chainFraction / md);
        }

        _homePos = transform.position;
        _homeRot = transform.rotation;
        _ready = true;
    }

    Vector3 DirOf(AxisDir d)
    {
        switch (d)
        {
            case AxisDir.Forward: return transform.forward;
            case AxisDir.Back:    return -transform.forward;
            case AxisDir.Up:      return transform.up;
            case AxisDir.Down:    return -transform.up;
            case AxisDir.Right:   return transform.right;
            default:              return -transform.right;
        }
    }

    void Update()
    {
        if (!_ready) return;

        float dt = Time.deltaTime;
        _phase += dt * speed * Mathf.PI * 2f;

        // ジェット拍動（0..1）。外套膜の収縮タイミング＝前進加速のタイミング。
        float pulse = 0.5f * (1f + Mathf.Sin(_phase));      // 0..1
        float jet = 1f + jetPulseStrength * (pulse - 0.5f) * 2f; // 1±jet

        for (int i = 0; i < _bones.Count; i++)
        {
            var b = _bones[i];

            float amp     = b.isMantle ? mantleAmplitude : armAmplitude;
            float amp2    = b.isMantle ? mantleAmplitude * 0.4f : armSecondaryAmplitude;
            float spd     = b.isMantle ? 1f : armSpeedScale;

            // 先端ほど大きく曲がるテーパ（根本 0.25 → 先端 1.0）
            float taper = Mathf.Lerp(0.25f, 1f, b.chainFraction);

            // 根本→先端へ伝播する位相
            float ph  = _phase * spd - b.depthFromRoot * (0.6f * wavePropagation);
            float ph2 = ph + Mathf.PI * 0.5f; // 副軸は 90°ずらして円を描かせる

            float a1 = amp  * taper * Mathf.Sin(ph);   // 度
            float a2 = amp2 * taper * Mathf.Sin(ph2);  // 度

            Quaternion delta =
                Quaternion.AngleAxis(a1, b.bendAxisA) *
                Quaternion.AngleAxis(a2, b.bendAxisB);

            b.t.localRotation = b.restLocal * delta;
        }

        if (enableLocomotion)
{
            // ジェット同期の前進
            Vector3 fwd = DirOf(swimDirection);
            transform.position += fwd * (swimForwardSpeed * jet) * dt;

            // 上下バブ（Y は出発点基準）
            float bob = Mathf.Sin(_phase * 0.5f) * bobAmount;
            transform.position = new Vector3(
                transform.position.x,
                _homePos.y + bob,
                transform.position.z);

            // ✅ 修正：静止回転を基準に sway を合成（累積しない）
            float sway = Mathf.Sin(_phase * 0.5f) * swaySway;
            transform.rotation = _homeRot * Quaternion.AngleAxis(sway, Vector3.up);
        }
    }

    void OnDisable()
    {
        // 無効化時は静止姿勢へ戻す
        if (!_ready) return;
        foreach (var b in _bones)
            if (b.t != null) b.t.localRotation = b.restLocal;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawBendGizmos || !_ready) return;
        foreach (var b in _bones)
        {
            if (b.t == null) continue;
            Gizmos.color = b.isMantle ? Color.cyan : Color.yellow;
            Vector3 p = b.t.position;
            Gizmos.DrawLine(p, p + b.t.TransformDirection(b.bendAxisA) * 0.05f);
        }
    }
#endif
}