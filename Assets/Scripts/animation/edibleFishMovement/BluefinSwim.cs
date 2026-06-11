using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// bluefin_rigged_xplus.glb 用の手続き的な遊泳スクリプト。
///
/// 仕組み:
///   ・尾の連鎖（bone_5 → bone_6 → bone_7 → bone_8）に位相をずらした
///     進行波（サイン波）を流し、尾ほど大きく振る = マグロ型(thunniform)の自然な推進。
///   ・各ボーンの「振り軸」は起動時にワールド上方向をボーンのローカル空間へ
///     変換して自動算出。リグ内部の回転に左右されず、尾は必ず水平にスイープする。
///   ・前進速度を尾ビートに同期させて脈動させる（蹴る→滑空の繰り返し）。
///   ・Perlinノイズで緩やかに方向を変え（ヨー/ピッチ）、旋回時はわずかにバンク。
///
/// 使い方:
///   1. インポートした魚のルート GameObject にこのスクリプトを付ける。
///   2. 再生するだけ。ボーンは名前(bone_5..bone_8 等)で自動検出される。
///      ※ 名前が違う／手動指定したい場合はインスペクタの配列に入れる。
///   3. もし横向きに泳いだら ForwardAxis を切り替える
///      （このモデルはモデル座標 +X が正面なので既定は PositiveX）。
/// </summary>
[DisallowMultipleComponent]
public class BluefinSwim : MonoBehaviour
{
    public enum LocalAxis { PositiveX, NegativeX, PositiveY, NegativeY, PositiveZ, NegativeZ }

    [Header("=== ボーン設定 ===")]
    [Tooltip("背骨/尾の連鎖を「胴体側 → 尾の先端」の順に。空なら名前で自動検出します。")]
    public Transform[] spineBones;

    [Tooltip("ヒレなど、補助的に揺らすボーン。空なら bone_3 / bone_4 / bone_1 を自動検出。")]
    public Transform[] finBones;

    [Tooltip("自動検出に使う尾ボーンの名前（胴体側 → 尾先）。")]
    public string[] spineBoneNames = { "bone_5", "bone_6", "bone_7", "bone_8" };

    [Tooltip("自動検出に使うヒレボーンの名前。")]
    public string[] finBoneNames = { "bone_3", "bone_4", "bone_1" };

    [Header("=== 尾の波（推進） ===")]
    [Tooltip("尾を振る速さ（1秒あたりのビート数）。")]
    public float swimFrequency = 1.6f;

    [Tooltip("尾先端の最大振り角(度)。")]
    public float maxSwayAngle = 22f;

    [Tooltip("ボーン1本ごとの位相差(度)。進行波の波長を決める。大きいほど波が細かい。")]
    public float phaseLagPerBone = 55f;

    [Tooltip("胴体側→尾先での振り幅の重み。左=胴体側(0)/右=尾先(1)。マグロは尾だけ大きく振る。")]
    public AnimationCurve tailWeightCurve = new AnimationCurve(
        new Keyframe(0f, 0.05f), new Keyframe(0.5f, 0.25f), new Keyframe(1f, 1f));

    [Header("=== ヒレ ===")]
    public bool animateFins = true;
    [Tooltip("ヒレを揺らす速さ（尾より速め）。")]
    public float finFrequency = 2.4f;
    [Tooltip("ヒレの振り角(度)。")]
    public float finAngle = 9f;

    [Header("=== 前進（移動） ===")]
    public bool enableLocomotion = true;
    [Tooltip("正面方向（このモデルはモデル座標 +X が顔）。横に泳ぐなら変更。")]
    public LocalAxis forwardAxis = LocalAxis.PositiveX;
    [Tooltip("基本の遊泳速度(m/s)。")]
    public float swimSpeed = 1.2f;
    [Tooltip("尾ビートに合わせた速度脈動の強さ(0=一定速)。")]
    [Range(0f, 1f)] public float thrustPulse = 0.35f;

    [Header("=== 緩やかな方向変化（任意） ===")]
    public bool enableWander = true;
    [Tooltip("方向がゆらぐ速さ。")]
    public float wanderFrequency = 0.25f;
    [Tooltip("最大旋回速度(度/秒)。")]
    public float maxTurnSpeed = 35f;
    [Tooltip("最大上下旋回速度(度/秒)。")]
    public float maxPitchSpeed = 18f;
    [Tooltip("水平へ戻ろうとする強さ（潜りっぱなしを防ぐ）。")]
    public float levelingStrength = 1.0f;
    [Tooltip("旋回時に内側へ傾く最大角(度)。")]
    public float maxBankAngle = 18f;

    [Header("=== その他 ===")]
    [Tooltip("画面外でもボーンの動きで消えないようにする。")]
    public bool keepVisibleOffscreen = true;

    // --- 内部キャッシュ ---
    private Quaternion[] _spineRest;
    private Vector3[] _spineSwayAxis;   // 各ボーンのローカル空間で表したワールド上方向
    private Quaternion[] _finRest;
    private Vector3[] _finSwayAxis;

    private float _wavePhase;           // 尾の波の位相(回転数)
    private float _currentBank;         // 現在のバンク角
    private float _wanderSeed;

    void Start()
    {
        AutoFindBones();
        CacheRig();

        if (keepVisibleOffscreen)
        {
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.updateWhenOffscreen = true;
        }

        _wanderSeed = Random.value * 1000f;
        _wavePhase = Random.value;       // 個体ごとに位相をずらす
    }

    void AutoFindBones()
    {
        if (spineBones == null || spineBones.Length == 0)
        {
            var list = new List<Transform>();
            foreach (var n in spineBoneNames)
            {
                var t = FindDeep(transform, n);
                if (t != null) list.Add(t);
            }
            spineBones = list.ToArray();
        }

        if (animateFins && (finBones == null || finBones.Length == 0))
        {
            var list = new List<Transform>();
            foreach (var n in finBoneNames)
            {
                var t = FindDeep(transform, n);
                if (t != null) list.Add(t);
            }
            finBones = list.ToArray();
        }

        if (spineBones == null || spineBones.Length == 0)
            Debug.LogWarning("[FishSwim] 尾ボーンが見つかりません。spineBones を手動で割り当ててください。", this);
    }

    void CacheRig()
    {
        int n = spineBones != null ? spineBones.Length : 0;
        _spineRest = new Quaternion[n];
        _spineSwayAxis = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            _spineRest[i] = spineBones[i].localRotation;
            // ワールド上方向を、このボーンの「自分のローカル空間」へ。
            // restRot * AngleAxis(θ, この軸) でちょうどワールド垂直まわりの水平スイープになる。
            _spineSwayAxis[i] = spineBones[i].InverseTransformDirection(Vector3.up);
        }

        int m = (animateFins && finBones != null) ? finBones.Length : 0;
        _finRest = new Quaternion[m];
        _finSwayAxis = new Vector3[m];
        for (int i = 0; i < m; i++)
        {
            _finRest[i] = finBones[i].localRotation;
            _finSwayAxis[i] = finBones[i].InverseTransformDirection(Vector3.up);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _wavePhase += dt * swimFrequency;

        AnimateSpine();
        if (animateFins) AnimateFins();
        if (enableLocomotion) Locomote(dt);
    }

    void AnimateSpine()
    {
        if (_spineRest == null) return;
        int n = _spineRest.Length;
        float lagRad = phaseLagPerBone * Mathf.Deg2Rad;

        for (int i = 0; i < n; i++)
        {
            float t = (n > 1) ? (float)i / (n - 1) : 1f;
            float weight = tailWeightCurve.Evaluate(t);
            float angle = Mathf.Sin(_wavePhase * 2f * Mathf.PI - i * lagRad)
                          * maxSwayAngle * weight;
            spineBones[i].localRotation =
                _spineRest[i] * Quaternion.AngleAxis(angle, _spineSwayAxis[i]);
        }
    }

    void AnimateFins()
    {
        if (_finRest == null) return;
        float phase = Time.time * finFrequency * 2f * Mathf.PI;
        for (int i = 0; i < _finRest.Length; i++)
        {
            // 左右で位相を反転させて対称に見せる
            float sign = (i % 2 == 0) ? 1f : -1f;
            float angle = Mathf.Sin(phase + i * 0.6f) * finAngle * sign;
            finBones[i].localRotation =
                _finRest[i] * Quaternion.AngleAxis(angle, _finSwayAxis[i]);
        }
    }

    void Locomote(float dt)
    {
        // --- 向きの変化 ---
        if (enableWander)
        {
            float ny = (Mathf.PerlinNoise(_wanderSeed + Time.time * wanderFrequency, 0f) - 0.5f) * 2f;
            float np = (Mathf.PerlinNoise(0f, _wanderSeed + Time.time * wanderFrequency) - 0.5f) * 2f;

            float yawDelta = ny * maxTurnSpeed * dt;
            transform.Rotate(Vector3.up * yawDelta, Space.World);

            Vector3 fwd = GetForwardWorld();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            // ピッチ + 水平へ戻す力
            float pitchDeviation = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            float pitchDelta = np * maxPitchSpeed * dt - pitchDeviation * levelingStrength * dt;
            transform.Rotate(right * pitchDelta, Space.World);

            // バンク（旋回内側へ傾ける）。差分で適用してドリフトを防ぐ。
            float targetBank = -ny * maxBankAngle;
            float bankDelta = targetBank - _currentBank;
            transform.Rotate(GetForwardLocal() * bankDelta, Space.Self);
            _currentBank = targetBank;
        }

        // --- 前進（尾ビートに同期して脈動）---
        float pulse = 1f + thrustPulse * Mathf.Sin(_wavePhase * 2f * Mathf.PI * 2f);
        float speed = swimSpeed * Mathf.Max(0f, pulse);
        transform.position += GetForwardWorld() * speed * dt;
    }

    Vector3 GetForwardLocal()
    {
        switch (forwardAxis)
        {
            case LocalAxis.PositiveX: return Vector3.right;
            case LocalAxis.NegativeX: return Vector3.left;
            case LocalAxis.PositiveY: return Vector3.up;
            case LocalAxis.NegativeY: return Vector3.down;
            case LocalAxis.PositiveZ: return Vector3.forward;
            case LocalAxis.NegativeZ: return Vector3.back;
        }
        return Vector3.right;
    }

    Vector3 GetForwardWorld() => transform.TransformDirection(GetForwardLocal()).normalized;

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindDeep(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    // シーンビューで正面方向を可視化
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + GetForwardWorld() * 1.5f);
    }
}
