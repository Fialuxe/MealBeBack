using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// リグ付き魚モデル（seabream_rigged_xplus）を自然に泳がせるスクリプト。
///
/// このモデルの仕様:
///   ・モデル座標の X+ が正面方向（顔のある向き）
///   ・背骨は bone_0(頭) → bone_1 → … → bone_8(尾) と連なる
///   ・各背骨のローカル軸は「ローカルY＝体の長手方向」「ローカルZ≒上方向」
///     → 各背骨を上向きの軸まわりに回すと、自然な左右のうねりになる
///
/// 仕組み:
///   1. 背骨に「頭→尾へ進む正弦波（進行波）」を与えて体をくねらせる
///   2. 振幅は尾に近いほど大きくする（頭はほぼ動かさない）
///   3. 本体を正面(X+)方向へ前進させ、ゆるやかに旋回（ワンダリング）させる
///   4. 旋回時に体を曲げ、わずかにバンク（ロール）させて生き物らしさを出す
///
/// 使い方:
///   GLB をシーンに配置 → ルートにこのスクリプトを追加 → 再生するだけ。
///   背骨は名前(bone_0..bone_8)から自動検出します。
///   もし上下にくねってしまう場合は Bend Axis を切り替えてください。
///   逆向き・横向きに進む場合は Forward Axis を変えてください。
/// </summary>
[DisallowMultipleComponent]
public class SeabreamSwim : MonoBehaviour
{
    public enum Axis { AutoUp, X, Y, Z }

    [Header("背骨ボーン (頭 → 尾 の順)")]
    [Tooltip("空のままだと bonePrefix + 0..maxSpineIndex の名前で自動検出します。")]
    public Transform[] spineBones;
    public string bonePrefix = "bone_";
    [Tooltip("自動検出する背骨の最大インデックス。bone_0..bone_8 なら 8。")]
    public int maxSpineIndex = 8;

    [Header("体のうねり (Body Undulation)")]
    [Tooltip("尾の最大振れ角(度)。大きいほど激しく泳ぐ。")]
    public float bendAngle = 14f;
    [Tooltip("1秒あたりの尾びれの往復回数。")]
    public float beatFrequency = 1.6f;
    [Tooltip("頭→尾へ波が伝わる量。大きいほど体がS字に。")]
    public float waveLength = 1.1f;
    [Tooltip("頭(0)→尾(1)の振幅の付き方。右肩上がりにすると尾ほど大きく振れる。")]
    public AnimationCurve amplitudeAlongBody =
        new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.5f, 0.35f), new Keyframe(1f, 1f));
    [Tooltip("各ボーンを回す軸。AutoUp は実行時に上方向の軸を自動判定。")]
    public Axis bendAxis = Axis.AutoUp;

    [Header("前進 (Locomotion)")]
    [Tooltip("正面となるローカル軸。このモデルは X+ が顔の向き。")]
    public Vector3 forwardAxis = Vector3.right; // = X+
    public float swimSpeed = 1.2f;
    [Tooltip("尾を振るタイミングに合わせて推進力を脈動させる（滑空感）。")]
    public bool pulseThrust = true;
    [Range(0f, 1f)] public float thrustPulse = 0.35f;

    [Header("旋回・ワンダリング")]
    [Tooltip("ふらふらと向きを変える強さ(度/秒)。")]
    public float turnAmount = 25f;
    [Tooltip("向きが変わるゆっくりさ。小さいほどゆったり蛇行。")]
    public float turnNoiseSpeed = 0.15f;
    [Tooltip("旋回に合わせて体を内側に曲げる量。")]
    public float turnBodyBend = 8f;
    [Tooltip("旋回時に傾く（バンク）量。")]
    public float bankAmount = 18f;

    [Header("上下のゆらぎ")]
    public float pitchBob = 4f;     // 上下に頭を振る角度
    public float verticalBob = 0.04f; // 上下動の量(m)
    public float bobFrequency = 0.5f;

    [Header("遊泳範囲 (任意)")]
    [Tooltip("ONにすると box の中に留まり、端で中心へ戻ろうとする。水槽向け。")]
    public bool useBounds = false;
    public Vector3 boundsCenter = Vector3.zero;
    public Vector3 boundsSize = new Vector3(20f, 6f, 20f);
    [Tooltip("端からこの距離以内で旋回を始める。")]
    public float boundsMargin = 3f;

    // --- 内部状態 ---
    Quaternion[] _initialLocalRot;
    Vector3[] _bendAxisLocal;
    float _wanderSeed;
    float _phaseTime;
    float _currentTurn;     // 平滑化した旋回量
    float _bank;            // 現在のバンク角
    float _yaw;             // 蓄積したヨー角(向き)
    Quaternion _baseRot;    // 起動時の姿勢

    void Start()
    {
        if (spineBones == null || spineBones.Length == 0)
            AutoFindSpine();

        CacheBones();
        _wanderSeed = Random.value * 1000f;
        _baseRot = transform.rotation;
    }

    void AutoFindSpine()
    {
        var list = new List<Transform>();
        for (int i = 0; i <= maxSpineIndex; i++)
        {
            var t = FindDeep(transform, bonePrefix + i);
            if (t != null) list.Add(t);
        }
        spineBones = list.ToArray();

        if (spineBones.Length == 0)
            Debug.LogWarning($"[FishSwim] 背骨ボーンが見つかりませんでした。" +
                             $"'{bonePrefix}0'..'{bonePrefix}{maxSpineIndex}' を確認するか、手動で割り当ててください。", this);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    void CacheBones()
    {
        int n = spineBones.Length;
        _initialLocalRot = new Quaternion[n];
        _bendAxisLocal = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            if (spineBones[i] == null) continue;
            _initialLocalRot[i] = spineBones[i].localRotation;
            _bendAxisLocal[i] = ResolveBendAxis(spineBones[i]);
        }
    }

    // 各ボーンの「回す軸」をローカル空間で決める
    Vector3 ResolveBendAxis(Transform bone)
    {
        switch (bendAxis)
        {
            case Axis.X: return Vector3.right;
            case Axis.Y: return Vector3.up;
            case Axis.Z: return Vector3.forward;
            default:
                // AutoUp: モデルの上方向に最も近いローカル軸を採用
                Vector3 worldUp = transform.up;
                Vector3 localUp = bone.InverseTransformDirection(worldUp).normalized;
                float ax = Mathf.Abs(localUp.x), ay = Mathf.Abs(localUp.y), az = Mathf.Abs(localUp.z);
                if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(localUp.x), 0, 0);
                if (ay >= ax && ay >= az) return new Vector3(0, Mathf.Sign(localUp.y), 0);
                return new Vector3(0, 0, Mathf.Sign(localUp.z));
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _phaseTime += dt * beatFrequency;

        UpdateSteering(dt);
        MoveForward(dt);
    }

    void UpdateSteering(float dt)
    {
        // ゆっくりしたノイズで蛇行する目標旋回量
        float noise = (Mathf.PerlinNoise(_wanderSeed, Time.time * turnNoiseSpeed) - 0.5f) * 2f;
        float targetTurn = noise * turnAmount;

        // 範囲制限がONなら、端に近づくと中心へ向きを切る
        if (useBounds)
            targetTurn += BoundsSteer();

        _currentTurn = Mathf.Lerp(_currentTurn, targetTurn, dt * 1.5f);

        // ヨー（向き）を蓄積
        _yaw += _currentTurn * dt;

        // バンク（旋回方向へ傾ける）
        float targetBank = -_currentTurn / Mathf.Max(turnAmount, 0.001f) * bankAmount;
        _bank = Mathf.Lerp(_bank, targetBank, dt * 2f);

        // 姿勢を組み立て直す（累積しないようにヨー＋バンクから再構築）
        Quaternion headingRot = Quaternion.AngleAxis(_yaw, Vector3.up) * _baseRot;
        Vector3 fwd = headingRot * forwardAxis.normalized;            // ワールド前方
        transform.rotation = Quaternion.AngleAxis(_bank, fwd) * headingRot;
    }

    float BoundsSteer()
    {
        Vector3 toCenter = boundsCenter - transform.position;
        Vector3 half = boundsSize * 0.5f;
        Vector3 local = transform.position - boundsCenter;

        bool nearEdge =
            Mathf.Abs(local.x) > half.x - boundsMargin ||
            Mathf.Abs(local.y) > half.y - boundsMargin ||
            Mathf.Abs(local.z) > half.z - boundsMargin;

        if (!nearEdge) return 0f;

        // 中心方向と現在の向きの角度差から旋回方向を決める
        Vector3 fwd = transform.TransformDirection(forwardAxis.normalized);
        Vector3 flatToCenter = Vector3.ProjectOnPlane(toCenter, Vector3.up).normalized;
        Vector3 flatFwd = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
        float signed = Vector3.SignedAngle(flatFwd, flatToCenter, Vector3.up);
        return Mathf.Sign(signed) * turnAmount * 2.5f;
    }

    void MoveForward(float dt)
    {
        float thrust = 1f;
        if (pulseThrust)
            thrust = 1f + Mathf.Sin(_phaseTime * Mathf.PI * 2f) * thrustPulse;

        Vector3 dir = transform.TransformDirection(forwardAxis.normalized);
        transform.position += dir * swimSpeed * thrust * dt;

        // 上下のゆらぎ（ゆるやかな浮き沈み）
        if (verticalBob > 0f)
        {
            float bob = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * verticalBob;
            transform.position += Vector3.up * bob * dt;
        }
    }

    // ボーン回転はメッシュ更新の直前(LateUpdate)で確定させる
    void LateUpdate()
    {
        if (spineBones == null || _initialLocalRot == null) return;

        int n = spineBones.Length;
        float turnBias = (_currentTurn / Mathf.Max(turnAmount, 0.001f)) * turnBodyBend;

        for (int i = 0; i < n; i++)
        {
            var bone = spineBones[i];
            if (bone == null) continue;

            float p = (n > 1) ? (float)i / (n - 1) : 1f; // 0=頭 1=尾
            float amp = amplitudeAlongBody.Evaluate(p);

            // 頭→尾へ伝わる進行波
            float phase = (_phaseTime - p * waveLength) * Mathf.PI * 2f;
            float swing = Mathf.Sin(phase) * bendAngle * amp;

            // 旋回時に体を内側へ曲げる（尾ほど強く）
            swing += turnBias * amp;

            // 頭の上下振り（ピッチ）を少しだけ
            float pitch = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * pitchBob * amp * 0.3f;

            Quaternion swayRot = Quaternion.AngleAxis(swing, _bendAxisLocal[i]);
            Quaternion pitchRot = Quaternion.AngleAxis(pitch, Vector3.right); // 体の長手はローカルY、X周りで軽い上下
            bone.localRotation = _initialLocalRot[i] * swayRot * pitchRot;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (useBounds)
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.25f);
            Gizmos.DrawWireCube(boundsCenter, boundsSize);
        }
    }
}
