using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// tuna_rigged_xplus.glb 用の手続き型・自然な遊泳スクリプト。
///
/// 【前提】
///  ・モデルローカルで X+ が正面（顔の向き）、Y+ が上。
///  ・ボーン名は bone_0 ... bone_12。bone_1〜bone_7 が頭→尾の背骨チェーン、
///    bone_8/bone_9 が尾びれ（上下葉）、bone_11/bone_12 が左右の胸びれ。
///  ・GLB は glTFast / UniGLTF などのインポータで読み込み、SkinnedMeshRenderer と
///    ボーン階層がある状態で使ってください（Unity 標準では .glb を直接読めません）。
///
/// 【使い方】
///  1. インポートしたモデルのルート（魚オブジェクト全体の親）にこのスクリプトを付ける。
///  2. ボーンは名前で自動検出されます。検出されない場合だけ手動で割り当ててください。
///  3. forwardAxis / upAxis はルートのローカル軸での「魚の正面・上」です。
///     モデル X+ 正面・Y+ 上なら既定値（右=+X / 上=+Y）のままでOK。
///     もし上下にバタついたり進む向きがおかしい場合はここを調整します。
///
/// 体のうねりと前進は独立しているので、振幅・周波数・遊泳速度は自由に調整できます。
/// </summary>
[DisallowMultipleComponent]
public class TunaSwim : MonoBehaviour
{
    // ------------------------------------------------------------------
    [Header("向きの定義（ルートのローカル軸）")]
    [Tooltip("魚の正面方向。モデル X+ が正面なので既定は右(+X)。")]
    public Vector3 forwardAxis = Vector3.right;     // +X = 正面
    [Tooltip("魚の上方向。既定は上(+Y)。尾は通常この軸まわりに左右へ振れる。")]
    public Vector3 upAxis = Vector3.up;             // +Y = 上

    // ------------------------------------------------------------------
    [Header("ボーン（空なら名前で自動検出）")]
    [Tooltip("頭→尾の順に並んだ背骨ボーン（bone_1 .. bone_7）。")]
    public Transform[] spineBones;
    [Tooltip("尾びれボーン（bone_8 / bone_9）。")]
    public Transform[] caudalBones;
    [Tooltip("胸びれボーン（bone_11 / bone_12）。")]
    public Transform[] pectoralBones;

    // ------------------------------------------------------------------
    [Header("遊泳（前進）")]
    [Tooltip("巡航速度（ワールド単位/秒）。モデルが大きいので最初は大きめに。")]
    public float cruiseSpeed = 3.0f;
    [Tooltip("速度の揺らぎ幅（巡航速度に対する割合）。")]
    [Range(0f, 1f)] public float speedVariation = 0.25f;
    [Tooltip("速度・向きの変化の滑らかさ。大きいほどゆっくり変わる。")]
    public float speedSmoothing = 1.5f;

    // ------------------------------------------------------------------
    [Header("体のうねり（進行波）")]
    [Tooltip("尾の基準ビート数（回/秒）。速度でさらに増える。")]
    public float baseBeatFrequency = 1.2f;
    [Tooltip("速度1あたりに加算されるビート周波数。")]
    public float beatFreqPerSpeed = 0.25f;
    [Tooltip("各背骨ボーンの最大曲げ角（度）。尾に向けてカーブで増幅される。")]
    public float bodyAmplitudeDeg = 9f;
    [Tooltip("頭(0)→尾(1)の振幅分布。マグロ系は頭が固く尾で大きく振る。")]
    public AnimationCurve amplitudeAlongBody = null;
    [Tooltip("ボーン1本ごとの位相ずれ（rad）。大きいほど波長が短くS字が強くなる。")]
    public float phaseOffsetPerBone = 0.55f;
    [Tooltip("速度が上がると振幅も少し増える割合。")]
    [Range(0f, 1f)] public float amplitudeSpeedGain = 0.35f;

    // ------------------------------------------------------------------
    [Header("ひれ")]
    public bool animateCaudal = true;
    [Tooltip("尾びれの追従振幅（度）。胴体の尾より少し遅れて振れる。")]
    public float caudalAmplitudeDeg = 7f;
    [Tooltip("尾びれの位相遅れ（rad）。むち打ちのような“しなり”を出す。")]
    public float caudalPhaseLag = 0.5f;

    public bool animatePectoral = true;
    [Tooltip("胸びれのアイドル揺れ振幅（度）。")]
    public float pectoralAmplitudeDeg = 8f;
    [Tooltip("胸びれの揺れ周波数（回/秒）。")]
    public float pectoralFrequency = 0.7f;

    // ------------------------------------------------------------------
    [Header("回遊（自然な方向転換）")]
    public bool wander = true;
    [Tooltip("ヨー（左右旋回）の最大角速度（度/秒）。")]
    public float maxYawRate = 35f;
    [Tooltip("ピッチ（上下）の最大角速度（度/秒）。")]
    public float maxPitchRate = 18f;
    [Tooltip("向き変化のゆらぎの速さ。小さいほど大きくゆったり旋回。")]
    public float wanderFrequency = 0.15f;
    [Tooltip("ピッチが水平から離れすぎないよう戻す強さ。")]
    public float pitchLevelStrength = 0.6f;
    [Tooltip("旋回時に内側へ傾くバンク量の係数。")]
    public float bankFactor = 0.6f;
    [Tooltip("バンク（ロール）の最大角（度）。")]
    public float maxBankDeg = 30f;

    [Header("外部制御")]
    [Tooltip("ON にすると移動・旋回を止め、ボーンアニメだけ実行します。FishOrbitMover と組み合わせて使います。")]
    public bool externalControl = false;

    [Header("遊泳エリア（任意・球状の囲い）")]
    public bool useBounds = false;
    public float boundsRadius = 30f;
    [Tooltip("境界に近づいたとき中心へ戻し始める割合（0.7=半径70%地点から）。")]
    [Range(0f, 1f)] public float boundsSoftEdge = 0.7f;

    // ------------------------------------------------------------------
    // 内部状態
    Quaternion[] _spineRest, _caudalRest, _pectoralRest;
    Vector3[] _spineBendAxis, _caudalBendAxis, _pectoralFlapAxis;
    float[] _pectoralSide; // +1 / -1（左右の符号）

    [HideInInspector] public Vector3 boundsCenter; // set by AquariumSceneSetup; defaults to spawn position in Awake
    float _currentSpeed;
    float _yawRate, _pitchRate;
    float _currentBank;
    float _noiseSeedYaw, _noiseSeedPitch;
    const float TAU = Mathf.PI * 2f;

    // 軸（ワールド空間）
    Vector3 FwdW => transform.TransformDirection(forwardAxis.normalized);
    Vector3 UpW => transform.TransformDirection(upAxis.normalized);
    Vector3 RightW => Vector3.Cross(UpW, FwdW).normalized; // 体の横（左右）方向

    void Reset()
    {
        // インスペクタで触っていない時のデフォルト振幅カーブ（頭が固く尾で大きい）
        amplitudeAlongBody = new AnimationCurve(
            new Keyframe(0f, 0.05f),
            new Keyframe(0.5f, 0.35f),
            new Keyframe(1f, 1f));
    }

    void Awake()
    {
        if (amplitudeAlongBody == null || amplitudeAlongBody.length == 0)
            amplitudeAlongBody = new AnimationCurve(
                new Keyframe(0f, 0.05f),
                new Keyframe(0.5f, 0.35f),
                new Keyframe(1f, 1f));

        AutoFindBones();
        CacheRestPose();

        boundsCenter = transform.position;
        _currentSpeed = cruiseSpeed;
        _noiseSeedYaw = Random.value * 1000f;
        _noiseSeedPitch = Random.value * 1000f;
    }

    // ------------------------------------------------------------------
    void AutoFindBones()
    {
        // 既に手動割り当て済みなら何もしない
        if (spineBones != null && spineBones.Length > 0) return;

        var map = new Dictionary<string, Transform>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (!map.ContainsKey(t.name)) map[t.name] = t;

        Transform Get(string n) => map.TryGetValue(n, out var t) ? t : null;

        var spine = new List<Transform>();
        for (int i = 1; i <= 7; i++)
        {
            var b = Get("bone_" + i);
            if (b != null) spine.Add(b);
        }
        spineBones = spine.ToArray();

        var caudal = new List<Transform>();
        foreach (var n in new[] { "bone_8", "bone_9" })
        {
            var b = Get(n); if (b != null) caudal.Add(b);
        }
        caudalBones = caudal.ToArray();

        var pec = new List<Transform>();
        foreach (var n in new[] { "bone_11", "bone_12" })
        {
            var b = Get(n); if (b != null) pec.Add(b);
        }
        pectoralBones = pec.ToArray();

        if (spineBones.Length == 0)
            Debug.LogWarning("[FishSwim] 背骨ボーン(bone_1..bone_7)が見つかりませんでした。" +
                             "spineBones を手動で割り当ててください。", this);
    }

    void CacheRestPose()
    {
        Vector3 yawAxisWorld = UpW; // 体は上方向まわりに左右へうねる

        // 背骨
        _spineRest = new Quaternion[spineBones.Length];
        _spineBendAxis = new Vector3[spineBones.Length];
        for (int i = 0; i < spineBones.Length; i++)
        {
            _spineRest[i] = spineBones[i].localRotation;
            // ローカル回転後にワールドで yawAxis まわりに曲がるためのローカル軸
            _spineBendAxis[i] = Quaternion.Inverse(spineBones[i].rotation) * yawAxisWorld;
        }

        // 尾びれ
        if (caudalBones != null)
        {
            _caudalRest = new Quaternion[caudalBones.Length];
            _caudalBendAxis = new Vector3[caudalBones.Length];
            for (int i = 0; i < caudalBones.Length; i++)
            {
                _caudalRest[i] = caudalBones[i].localRotation;
                _caudalBendAxis[i] = Quaternion.Inverse(caudalBones[i].rotation) * yawAxisWorld;
            }
        }

        // 胸びれ（前後方向まわりに上下へはためく）
        if (pectoralBones != null)
        {
            Vector3 flapAxisWorld = FwdW;
            _pectoralRest = new Quaternion[pectoralBones.Length];
            _pectoralFlapAxis = new Vector3[pectoralBones.Length];
            _pectoralSide = new float[pectoralBones.Length];
            for (int i = 0; i < pectoralBones.Length; i++)
            {
                _pectoralRest[i] = pectoralBones[i].localRotation;
                _pectoralFlapAxis[i] = Quaternion.Inverse(pectoralBones[i].rotation) * flapAxisWorld;
                // 左右で逆位相にするための符号（横方向の位置から判定）
                float side = Vector3.Dot(pectoralBones[i].position - transform.position, RightW);
                _pectoralSide[i] = side >= 0f ? 1f : -1f;
            }
        }
    }

    // ------------------------------------------------------------------
    void Update()
    {
        float dt = Time.deltaTime;

        if (!externalControl)
        {
            UpdateSteering(dt);
            UpdateLocomotion(dt);
        }
        UpdateBodyWave();
        UpdateFins();
    }

    // 自然な方向転換（ヨー・ピッチ・バンク）
    void UpdateSteering(float dt)
    {
        if (!wander) return;

        // ノイズで滑らかに変化する目標角速度（-1..1）
        float yawN = Mathf.PerlinNoise(_noiseSeedYaw, Time.time * wanderFrequency) * 2f - 1f;
        float pitchN = Mathf.PerlinNoise(_noiseSeedPitch, Time.time * wanderFrequency) * 2f - 1f;

        float targetYawRate = yawN * maxYawRate;
        float targetPitchRate = pitchN * maxPitchRate;

        // 水平へ戻そうとするピッチ補正（上を向きすぎ/下を向きすぎを防ぐ）
        float pitchFromLevel = Vector3.SignedAngle(
            Vector3.ProjectOnPlane(FwdW, Vector3.up).normalized, FwdW, RightW);
        targetPitchRate -= pitchFromLevel * pitchLevelStrength;

        // 境界に近づいたら中心へ向けて旋回
        if (useBounds)
        {
            Vector3 toCenter = boundsCenter - transform.position;
            float dist = toCenter.magnitude;
            float soft = boundsRadius * boundsSoftEdge;
            if (dist > soft)
            {
                float w = Mathf.InverseLerp(soft, boundsRadius, dist); // 0→1
                Vector3 dir = toCenter.normalized;
                float yawErr = Vector3.SignedAngle(FwdW, dir, UpW);
                float pitchErr = Vector3.SignedAngle(FwdW, dir, RightW);
                targetYawRate = Mathf.Lerp(targetYawRate, yawErr, w);
                targetPitchRate = Mathf.Lerp(targetPitchRate, -pitchErr, w);
            }
        }

        // なめらかに追従
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, speedSmoothing));
        _yawRate = Mathf.Lerp(_yawRate, targetYawRate, k);
        _pitchRate = Mathf.Lerp(_pitchRate, targetPitchRate, k);

        // 回転を適用（ワールド軸まわり、ピボット＝自身）
        transform.rotation = Quaternion.AngleAxis(_yawRate * dt, UpW) * transform.rotation;
        transform.rotation = Quaternion.AngleAxis(_pitchRate * dt, RightW) * transform.rotation;

        // バンク（旋回方向の内側へ傾ける）。前回ぶんを打ち消して目標へ。
        float targetBank = Mathf.Clamp(-_yawRate * bankFactor, -maxBankDeg, maxBankDeg);
        float deltaBank = targetBank - _currentBank;
        transform.rotation = Quaternion.AngleAxis(deltaBank, FwdW) * transform.rotation;
        _currentBank = targetBank;
    }

    void UpdateLocomotion(float dt)
    {
        // 速度をゆっくり揺らがせる
        float speedNoise = Mathf.PerlinNoise(123.4f, Time.time * 0.2f) * 2f - 1f;
        float targetSpeed = cruiseSpeed * (1f + speedNoise * speedVariation);
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, speedSmoothing));
        _currentSpeed = Mathf.Lerp(_currentSpeed, targetSpeed, k);

        transform.position += FwdW * _currentSpeed * dt;
    }

    // 進行波で胴体をうねらせる
    void UpdateBodyWave()
    {
        if (spineBones == null || spineBones.Length == 0) return;

        int n = spineBones.Length;
        float speedFactor = Mathf.Abs(_currentSpeed) / Mathf.Max(0.0001f, cruiseSpeed);

        float beatFreq = baseBeatFrequency + Mathf.Abs(_currentSpeed) * beatFreqPerSpeed;
        float ampScale = 1f + (speedFactor - 1f) * amplitudeSpeedGain;

        for (int i = 0; i < n; i++)
        {
            float t = (n > 1) ? (float)i / (n - 1) : 1f;          // 0=頭, 1=尾
            float amp = bodyAmplitudeDeg * amplitudeAlongBody.Evaluate(t) * ampScale;
            // 波は頭→尾へ伝播する
            float phase = Time.time * beatFreq * TAU - i * phaseOffsetPerBone;
            float angle = amp * Mathf.Sin(phase);

            spineBones[i].localRotation = _spineRest[i] *
                                          Quaternion.AngleAxis(angle, _spineBendAxis[i]);
        }
    }

    void UpdateFins()
    {
        float beatFreq = baseBeatFrequency + Mathf.Abs(_currentSpeed) * beatFreqPerSpeed;

        // 尾びれ：胴体最後尾の波に少し遅れて振れる
        if (animateCaudal && caudalBones != null && _caudalRest != null)
        {
            int tailIndex = (spineBones != null) ? spineBones.Length : 0;
            for (int i = 0; i < caudalBones.Length; i++)
            {
                float phase = Time.time * beatFreq * TAU
                              - tailIndex * phaseOffsetPerBone
                              - caudalPhaseLag;
                float angle = caudalAmplitudeDeg * Mathf.Sin(phase);
                caudalBones[i].localRotation = _caudalRest[i] *
                                               Quaternion.AngleAxis(angle, _caudalBendAxis[i]);
            }
        }

        // 胸びれ：ゆっくりとしたアイドルのはためき（左右逆位相）
        if (animatePectoral && pectoralBones != null && _pectoralRest != null)
        {
            for (int i = 0; i < pectoralBones.Length; i++)
            {
                float phase = Time.time * pectoralFrequency * TAU;
                float angle = pectoralAmplitudeDeg * Mathf.Sin(phase) * _pectoralSide[i];
                pectoralBones[i].localRotation = _pectoralRest[i] *
                                                 Quaternion.AngleAxis(angle, _pectoralFlapAxis[i]);
            }
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (useBounds)
        {
            Vector3 c = Application.isPlaying ? boundsCenter : transform.position;
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.25f);
            Gizmos.DrawWireSphere(c, boundsRadius);
        }
        // 正面・上の確認用
        Gizmos.color = Color.red;   Gizmos.DrawRay(transform.position, FwdW * 3f);
        Gizmos.color = Color.green; Gizmos.DrawRay(transform.position, UpW * 3f);
    }
#endif
}
