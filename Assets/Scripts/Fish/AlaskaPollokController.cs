// ============================================================================
//  AlaskaPollokController.cs
//  スケトウダラ（AlaskaPollok）用 リアル遊泳コントローラ
//
//  ・1コンポーネントで「移動 / 旋回(ヨー) / ロール(進行方向軸まわり) / ピッチ」
//    と「リグ(背骨)のうねり・胸びれの羽ばたき」をまとめて駆動します。
//  ・外部から操作するための public API を一通り用意しています。
//  ・Wandering は乱数(Random)ではなく Perlin ノイズ + レート制限ステアリングで
//    実装し、魚が“あらぶる”（カクカク・急変する）のを防いでいます。
//
//  【取り付け方】
//   1. AlaskaPollok プレハブのルート（顔が +Z を向く GameObject）に本コンポーネントを付ける。
//   2. インスペクタの「Find Bones Automatically」を実行（右クリックメニュー）するか、
//      spineBones に bone_1,bone_2,bone_3,bone_4 を、pectoralBones に
//      bone_13,bone_16 などを手動で割り当てる。
//   3. 再生すると自動で泳ぎ出します（wander = true の場合）。
//
//  ※ Prefab が Y軸-90°回転・Scale 0.07 を掛けてモデルの顔(+X)を Unity 前方(+Z)へ
//    整列させているため、本スクリプトは「ルートの +Z = 前進」を前提にしています。
//    もし横向き／後ろ向きに泳ぐ場合は、モデル子オブジェクトのローカル回転を調整してください。
// ============================================================================

using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Fish/Fish Swim Controller")]
public class AlaskaPollokController : MonoBehaviour
{
    // =====================================================================
    #region 1. 移動 (Locomotion)
    // =====================================================================
    [Header("■ 移動 (Locomotion)")]
    [Tooltip("通常巡航速度 [m/s]。モデルは Prefab で 0.07 倍されているため小さめが自然。")]
    public float cruiseSpeed = 0.35f;

    [Tooltip("最大速度 [m/s]。バースト(突進)時の上限にも使う。")]
    public float maxSpeed = 0.9f;

    [Tooltip("速度変化のなめらかさ(秒)。大きいほど加減速がゆっくり。")]
    public float accelerationSmoothTime = 0.6f;
    #endregion

    // =====================================================================
    #region 2. 旋回・向き (Steering : Yaw / Pitch)
    // =====================================================================
    [Header("■ 旋回 (Steering)")]
    [Tooltip("ヨー(高さ軸まわりの向き転換)の最大角速度 [deg/s]。")]
    public float maxYawRate = 90f;

    [Tooltip("ピッチ(上下の傾き)の最大角速度 [deg/s]。")]
    public float maxPitchRate = 45f;

    [Tooltip("ピッチの可動範囲 [deg]。これを超えて頭を上下に振らない(宙返り防止)。")]
    [Range(0f, 89f)] public float maxPitchAngle = 45f;

    [Tooltip("目標の向きへ追従するなめらかさ(秒)。大きいほどゆったり旋回。")]
    public float turnSmoothTime = 0.5f;
    #endregion

    // =====================================================================
    #region 3. ロール / バンク (Roll / Bank : 進行方向軸まわり)
    // =====================================================================
    [Header("■ ロール / バンク (進行方向軸まわり)")]
    [Tooltip("旋回時に自動で内側へ傾ける(バンクする)。")]
    public bool autoBank = true;

    [Tooltip("自動バンクの最大傾き角 [deg]。")]
    public float maxBankAngle = 25f;

    [Tooltip("ロールのなめらかさ(秒)。")]
    public float bankSmoothTime = 0.4f;

    [Tooltip("バンク方向の符号。傾きが逆なら -1 にする。")]
    public float bankSign = 1f;
    #endregion

    // =====================================================================
    #region 4. 徘徊 (Wandering : Perlin ノイズ制御)
    // =====================================================================
    [Header("■ 徘徊 (Wandering)")]
    [Tooltip("自律徘徊を有効にする。外部から操作指示を受けると一定時間自動で停止する。")]
    public bool wander = true;

    [Tooltip("徘徊時の左右の振れ(ヨー)の強さ [deg/s]。")]
    public float wanderYawRate = 35f;

    [Tooltip("徘徊時の上下の傾き(ピッチ)の振れ幅 [deg]。")]
    public float wanderPitchRange = 12f;

    [Tooltip("徘徊ノイズの進行速度。小さいほどゆったり、大きいほど落ち着きがなくなる。")]
    public float wanderNoiseSpeed = 0.25f;

    [Tooltip("徘徊時の最小・最大巡航速度 [m/s]。ノイズでこの範囲を滑らかに行き来する。")]
    public float wanderMinSpeed = 0.15f;
    public float wanderMaxSpeed = 0.5f;

    [Tooltip("外部から操作指示を受けたとき、徘徊を抑制する時間(秒)。")]
    public float manualOverrideDuration = 3f;
    #endregion

    // =====================================================================
    #region 5. 遊泳範囲 (Bounds : 水槽からはみ出させない)
    // =====================================================================
    [Header("■ 遊泳範囲 (Bounds)")]
    [Tooltip("遊泳範囲(箱)で囲い込み、壁に近づくと滑らかに中央へ向き直る。")]
    public bool useBounds = true;

    [Tooltip("範囲の中心。開始位置からのオフセット(ワールド)として扱う。")]
    public Vector3 boundsCenterOffset = Vector3.zero;

    [Tooltip("範囲(箱)のサイズ。")]
    public Vector3 boundsSize = new Vector3(6f, 3f, 6f);

    [Tooltip("壁の手前どれくらいから向き直りを始めるか(マージン)。")]
    public float boundsMargin = 1.2f;
    #endregion

    // =====================================================================
    #region 6. 障害物回避 (Obstacle Avoidance : 任意)
    // =====================================================================
    [Header("■ 障害物回避 (任意・Collider が必要)")]
    public bool avoidObstacles = false;
    public LayerMask obstacleMask = ~0;
    [Tooltip("前方をどれだけ先まで見るか。")]
    public float avoidDistance = 1.0f;
    [Tooltip("当たり判定の太さ(SphereCast 半径)。")]
    public float avoidRadius = 0.2f;
    #endregion

    // =====================================================================
    #region 7. リグ : 背骨のうねり (Spine Undulation)
    // =====================================================================
    [Header("■ リグ : 背骨のうねり")]
    [Tooltip("前→尾の順に並べた背骨ボーン。スケトウダラは bone_1, bone_2, bone_3, bone_4。")]
    public Transform[] spineBones;

    [Tooltip("体に沿ったうねり振幅の分布(0=前, 1=尾先)。尾に向かって大きくする。")]
    public AnimationCurve amplitudeAlongBody = new AnimationCurve(
        new Keyframe(0f, 0.15f), new Keyframe(0.6f, 0.45f), new Keyframe(1f, 1f));

    [Tooltip("停止時/最大遊泳時の尾の振り角 [deg]。速度に応じて補間される。")]
    public float idleBeatAmplitude = 4f;
    public float maxBeatAmplitude = 16f;

    [Tooltip("停止時/最大遊泳時の尾の振り周波数 [Hz]。")]
    public float idleBeatFrequency = 0.8f;
    public float maxBeatFrequency = 3.0f;

    [Tooltip("頭→尾への進行波の位相遅れ[rad](チェーン全体で)。大きいほど S 字が深くなる。")]
    public float phaseLagAlongBody = 1.4f;

    [Tooltip("旋回中、体全体を旋回方向へ曲げる量 [deg]。")]
    public float turnBodyCurvature = 10f;

    [Tooltip("うねり方向の符号。左右が逆なら -1 にする。")]
    public float bendSign = 1f;
    #endregion

    // =====================================================================
    #region 8. リグ : 胸びれ (Pectoral Fins)
    // =====================================================================
    [Header("■ リグ : 胸びれの羽ばたき(任意)")]
    [Tooltip("胸びれボーン。スケトウダラは bone_13, bone_16 など。")]
    public Transform[] pectoralBones;
    public float pectoralFrequency = 1.5f;
    public float pectoralAmplitude = 8f;
    #endregion

    // =====================================================================
    #region デバッグ表示
    // =====================================================================
    [Header("■ デバッグ")]
    public bool drawGizmos = true;
    #endregion

    // =====================================================================
    //  ▼▼▼  公開API (外部から魚を操作する)  ▼▼▼
    // =====================================================================
    #region Public API

    /// <summary>現在の実速度 [m/s]（読み取り専用）。</summary>
    public float CurrentSpeed => _currentSpeed;
    /// <summary>現在の向き(ヨー) [deg]。</summary>
    public float Heading => _heading;
    /// <summary>現在のピッチ [deg]。</summary>
    public float PitchAngle => _pitch;
    /// <summary>現在のロール(バンク) [deg]。</summary>
    public float RollAngle => _roll;
    /// <summary>現在の実ヨー角速度 [deg/s]。</summary>
    public float YawRate => _yawRateActual;
    /// <summary>徘徊中かどうか。</summary>
    public bool IsWandering => wander && _manualTimer <= 0f && !_hasDestination;

    // ---- 移動 -----------------------------------------------------------

    /// <summary>目標巡航速度を設定する [m/s]。</summary>
    public void SetTargetSpeed(float metersPerSecond)
    {
        _targetSpeed = Mathf.Clamp(metersPerSecond, 0f, maxSpeed);
    }

    /// <summary>指定速度で前進する（目的地指定は解除）。</summary>
    public void MoveForward(float metersPerSecond)
    {
        ClearDestination();
        SetTargetSpeed(metersPerSecond);
    }

    /// <summary>巡航速度で泳ぐ。</summary>
    public void Cruise() => MoveForward(cruiseSpeed);

    /// <summary>なめらかに停止する（惰性で減速）。</summary>
    public void Stop()
    {
        ClearDestination();
        _targetSpeed = 0f;
    }

    /// <summary>短時間の突進(バースト)。驚いたときなどに。</summary>
    /// <param name="speedMultiplier">巡航速度に対する倍率。</param>
    /// <param name="duration">継続時間(秒)。</param>
    public void Burst(float speedMultiplier = 2.5f, float duration = 0.6f)
    {
        _burstMul = Mathf.Max(1f, speedMultiplier);
        _burstTimer = Mathf.Max(0f, duration);
        MarkManual();
    }

    // ---- 旋回(ヨー / 高さ軸まわりの向き転換) ----------------------------

    /// <summary>現在の向きから相対的にヨー回転する [deg]（+で右、-で左）。なめらかに追従。</summary>
    public void Turn(float deltaYawDegrees)
    {
        _targetHeading += deltaYawDegrees;
        MarkManual();
    }

    /// <summary>絶対的な向き(ヨー)を設定する [deg]（ワールド基準）。</summary>
    public void SetHeading(float worldYawDegrees)
    {
        _targetHeading = worldYawDegrees;
        MarkManual();
    }

    /// <summary>指定ワールド方向を向く（ヨー+ピッチ）。</summary>
    public void FaceDirection(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude < 1e-6f) return;
        Vector3 d = worldDirection.normalized;
        _targetHeading = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        _targetPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg,
                                   -maxPitchAngle, maxPitchAngle);
        MarkManual();
    }

    /// <summary>指定ワールド地点の方を向くように操舵する。</summary>
    public void SteerTowards(Vector3 worldPoint) => FaceDirection(worldPoint - transform.position);

    // ---- ピッチ(上下) ---------------------------------------------------

    /// <summary>相対的にピッチを変える [deg]（+で機首下げ）。</summary>
    public void Pitch(float deltaPitchDegrees)
    {
        _targetPitch = Mathf.Clamp(_targetPitch + deltaPitchDegrees, -maxPitchAngle, maxPitchAngle);
        MarkManual();
    }

    /// <summary>絶対的なピッチを設定する [deg]。</summary>
    public void SetPitch(float pitchDegrees)
    {
        _targetPitch = Mathf.Clamp(pitchDegrees, -maxPitchAngle, maxPitchAngle);
        MarkManual();
    }

    // ---- ロール(進行方向軸まわり) --------------------------------------

    /// <summary>手動ロールを相対的に加える [deg]（自動バンクに加算される）。</summary>
    public void Roll(float deltaRollDegrees) => _manualRoll += deltaRollDegrees;

    /// <summary>手動ロールを絶対値で設定する [deg]。</summary>
    public void SetRoll(float rollDegrees) => _manualRoll = rollDegrees;

    /// <summary>ロールを瞬間的に設定する [deg]（なめらか追従を挟まない）。
    /// 横倒しでスポーンさせる等に使う。</summary>
    public void SetRollImmediate(float rollDegrees)
    {
        _manualRoll = rollDegrees;
        _roll = rollDegrees;
        _rollVel = 0f;
    }

    // ---- 目的地・経路 ---------------------------------------------------

    /// <summary>指定ワールド地点へ泳いでいく。近づくと自動で減速・停止する。</summary>
    public void SwimTo(Vector3 worldPoint, float arriveRadius = 0.3f)
    {
        _destination = worldPoint;
        _arriveRadius = Mathf.Max(0.01f, arriveRadius);
        _hasDestination = true;
        MarkManual();
    }

    /// <summary>目的地指定を解除する。</summary>
    public void ClearDestination() => _hasDestination = false;

    // ---- 徘徊の切り替え -------------------------------------------------

    /// <summary>自律徘徊の ON/OFF。</summary>
    public void SetWandering(bool enabled) => wander = enabled;

    // ---- スポーン / 瞬間配置・徘徊制御（登場演出用） -------------------

    /// <summary>位置と向き(ヨー/ピッチ)を瞬間的に設定する。
    /// なめらかな追従を挟まず即反映するのでスポーン時に使う。</summary>
    public void SnapTo(Vector3 position, float headingDegrees, float pitchDegrees = 0f)
    {
        transform.position = position;
        _heading = _targetHeading = headingDegrees;
        _pitch   = _targetPitch   = Mathf.Clamp(pitchDegrees, -maxPitchAngle, maxPitchAngle);
        _roll = _manualRoll = 0f;
        _headingVel = _pitchVel = _rollVel = _speedVel = 0f;
        _yawRateActual = 0f;
        transform.rotation = Quaternion.Euler(_pitch, _heading, _roll);
        _manualTimer = manualOverrideDuration; // 直後は徘徊させない
    }

    /// <summary>向き(ヨー)だけを瞬間設定する。</summary>
    public void SnapHeading(float headingDegrees) => SnapTo(transform.position, headingDegrees, _pitch);

    /// <summary>遊泳範囲の中心をワールド座標で設定し直す。</summary>
    public void SetBoundsCenter(Vector3 worldCenter) => _boundsWorldCenter = worldCenter;

    /// <summary>現在位置を遊泳範囲の中心にする。</summary>
    public void RecenterBoundsHere() => _boundsWorldCenter = transform.position + boundsCenterOffset;

    /// <summary>外部操作による徘徊抑制を即解除する（すぐ徘徊を再開させたいとき）。</summary>
    public void ClearManualOverride() => _manualTimer = 0f;

    /// <summary>ワールド空間のドリフト速度を設定する [m/s]。
    /// 体の向きと無関係に位置を流す（例: 横向きのまま前へ押し出す登場演出、水流など）。</summary>
    public void SetDriftVelocity(Vector3 worldVelocity) => _externalVelocity = worldVelocity;

    /// <summary>ドリフト速度を解除する。</summary>
    public void ClearDrift() => _externalVelocity = Vector3.zero;

    /// <summary>現在のドリフト速度 [m/s]（読み取り専用）。</summary>
    public Vector3 DriftVelocity => _externalVelocity;

    #endregion
    // =====================================================================
    //  ▲▲▲  公開API ここまで  ▲▲▲
    // =====================================================================


    // =====================================================================
    #region 内部状態
    // =====================================================================
    // 向き(スカラ管理)。transform.rotation = Euler(pitch, heading, roll)。
    // ロールは Euler の Z (= 前方+Z まわり) なので進行方向に影響しない＝見た目だけのバンクになる。
    float _heading, _targetHeading, _headingVel;
    float _pitch, _targetPitch, _pitchVel;
    float _roll, _manualRoll, _rollVel;
    float _yawRateActual;

    // ワールド空間の追加速度(ドリフト/漂流/ストレイフ)。向きとは独立に位置を流す。
    Vector3 _externalVelocity;

    // 速度
    float _currentSpeed, _targetSpeed, _speedVel;
    float _burstTimer, _burstMul = 1f;

    // 徘徊
    float _wanderTime, _wanderSeed;
    float _manualTimer; // >0 の間は徘徊を抑制

    // 目的地
    bool _hasDestination;
    Vector3 _destination;
    float _arriveRadius = 0.3f;

    // 遊泳範囲
    Vector3 _boundsWorldCenter;

    // リグ(背骨)
    Quaternion[] _spineRest;
    Vector3[] _spineAxis;   // 各ボーンの「ボディ垂直軸(=横うねりの回転軸)」をローカル空間で保持
    float _beatPhase;

    // リグ(胸びれ)
    Quaternion[] _pecRest;
    Vector3[] _pecAxis;
    float _pecPhase;
    #endregion


    // =====================================================================
    #region Unity ライフサイクル
    // =====================================================================
    void Reset()
    {
        // コンポーネント追加時に自動でボーンを探す。
        AutoFindBones();
    }

    void Awake()
    {
        // 現在の向きを初期ヨー/ピッチとして取り込む。
        Vector3 e = transform.rotation.eulerAngles;
        _heading = _targetHeading = e.y;
        _pitch = _targetPitch = NormalizeAngle(e.x);
        _roll = 0f;

        _currentSpeed = _targetSpeed = cruiseSpeed;
        _wanderSeed = Random.value * 1000f; // 個体ごとに徘徊パターンをずらす
        _boundsWorldCenter = transform.position + boundsCenterOffset;

        CacheRig();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        UpdateSteering(dt);   // 目標の向き・速度を決める
        ApplyMotion(dt);      // 実際に向き・位置を更新
        DriveSpine(dt);       // 背骨のうねり
        DrivePectorals(dt);   // 胸びれ
    }
    #endregion


    // =====================================================================
    #region ステアリング(頭脳)
    // =====================================================================
    void UpdateSteering(float dt)
    {
        // タイマー類
        if (_manualTimer > 0f) _manualTimer -= dt;
        if (_burstTimer > 0f) { _burstTimer -= dt; if (_burstTimer <= 0f) _burstMul = 1f; }

        bool wandering = wander && _manualTimer <= 0f && !_hasDestination;

        // --- 1) 目的地へ向かう ---
        if (_hasDestination)
        {
            Vector3 to = _destination - transform.position;
            float dist = to.magnitude;
            FaceDirectionInternal(to);
            // 到着前は減速、到着で停止
            float slowRadius = _arriveRadius * 4f;
            float spd = cruiseSpeed * Mathf.Clamp01(dist / Mathf.Max(0.001f, slowRadius));
            _targetSpeed = Mathf.Min(spd, maxSpeed);
            if (dist <= _arriveRadius) { _hasDestination = false; _targetSpeed = 0f; }
        }
        // --- 2) 徘徊（Perlin ノイズで滑らかに） ---
        else if (wandering)
        {
            _wanderTime += dt * wanderNoiseSpeed;
            // Perlin ノイズは連続関数なので、フレーム毎に値が滑らかに変化する。
            // → Random.value のような無相関乱数と違い、向き・速度が急変せず“あらぶらない”。
            float nYaw = Mathf.PerlinNoise(_wanderSeed + _wanderTime, 0.137f) * 2f - 1f;     // -1..1
            float nPit = Mathf.PerlinNoise(0.613f, _wanderSeed + _wanderTime) * 2f - 1f;     // -1..1
            float nSpd = Mathf.PerlinNoise(_wanderSeed + _wanderTime * 0.5f, 7.77f);         //  0..1

            // ヨーは「滑らかな目標角を積分」、ピッチ・速度は範囲内に直接マッピング。
            _targetHeading += nYaw * wanderYawRate * dt;
            _targetPitch = nPit * wanderPitchRange;
            _targetSpeed = Mathf.Lerp(wanderMinSpeed, wanderMaxSpeed, nSpd);
        }

        // --- 3) 遊泳範囲で囲い込み（壁に近づくほど中央へ向き直る） ---
        if (useBounds) ApplyBounds();

        // --- 4) 障害物回避（任意） ---
        if (avoidObstacles) ApplyObstacleAvoidance();

        // ピッチは可動域でクランプ
        _targetPitch = Mathf.Clamp(_targetPitch, -maxPitchAngle, maxPitchAngle);
    }

    void ApplyBounds()
    {
        Vector3 local = transform.position - _boundsWorldCenter;
        Vector3 half = boundsSize * 0.5f;

        // 各軸ではみ出し度合い(0..1)を求め、最大値を補正強度とする
        float strength = 0f;
        strength = Mathf.Max(strength, OverEdge(local.x, half.x));
        strength = Mathf.Max(strength, OverEdge(local.y, half.y));
        strength = Mathf.Max(strength, OverEdge(local.z, half.z));

        if (strength <= 0f) return;

        // 中央方向へ目標の向きをブレンド（強いほど大きく向き直る）
        Vector3 inward = (_boundsWorldCenter - transform.position);
        float inwardHeading = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
        float inwardPitch = Mathf.Clamp(
            -Mathf.Asin(Mathf.Clamp(inward.normalized.y, -1f, 1f)) * Mathf.Rad2Deg,
            -maxPitchAngle, maxPitchAngle);

        _targetHeading = Mathf.LerpAngle(_targetHeading, inwardHeading, strength);
        _targetPitch = Mathf.Lerp(_targetPitch, inwardPitch, strength);
    }

    float OverEdge(float pos, float half)
    {
        // 壁の内側 margin 手前から 0→1 に立ち上がる
        float soft = Mathf.Max(0.01f, boundsMargin);
        float over = (Mathf.Abs(pos) - (half - soft)) / soft;
        return Mathf.Clamp01(over);
    }

    void ApplyObstacleAvoidance()
    {
        if (Physics.SphereCast(transform.position, avoidRadius, transform.forward,
                               out RaycastHit hit, avoidDistance, obstacleMask,
                               QueryTriggerInteraction.Ignore))
        {
            // 壁の法線方向＋少し横へ逃げる向きへ操舵
            Vector3 away = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
            away = (away + hit.normal * 0.5f).normalized;
            float strength = 1f - Mathf.Clamp01(hit.distance / avoidDistance);
            float h = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
            _targetHeading = Mathf.LerpAngle(_targetHeading, h, strength);
        }
    }

    // 内部用：目的地/範囲計算からは MarkManual を呼ばずに向きだけ更新する
    void FaceDirectionInternal(Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        Vector3 d = dir.normalized;
        _targetHeading = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        _targetPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg,
                                   -maxPitchAngle, maxPitchAngle);
    }

    void MarkManual() => _manualTimer = manualOverrideDuration;
    #endregion


    // =====================================================================
    #region 運動の適用(向き・速度・位置)
    // =====================================================================
    void ApplyMotion(float dt)
    {
        float prevHeading = _heading;

        // 向き：目標へなめらかに追従（最大角速度でレート制限）
        _heading = Mathf.SmoothDampAngle(_heading, _targetHeading, ref _headingVel,
                                         turnSmoothTime, maxYawRate, dt);
        _pitch = Mathf.SmoothDampAngle(_pitch, _targetPitch, ref _pitchVel,
                                       turnSmoothTime, maxPitchRate, dt);

        // 実ヨー角速度（アニメ連動やバンクに使う）
        _yawRateActual = Mathf.DeltaAngle(prevHeading, _heading) / dt;

        // ロール：旋回に応じた自動バンク + 手動ロール
        float bank = 0f;
        if (autoBank && maxYawRate > 0.001f)
            bank = bankSign * Mathf.Clamp(_yawRateActual / maxYawRate, -1f, 1f) * maxBankAngle;
        float targetRoll = bank + _manualRoll;
        _roll = Mathf.SmoothDampAngle(_roll, targetRoll, ref _rollVel, bankSmoothTime);

        // 速度：目標へなめらかに（バースト中は倍率を掛ける）
        float wanted = Mathf.Min(_targetSpeed * _burstMul, maxSpeed);
        _currentSpeed = Mathf.SmoothDamp(_currentSpeed, wanted, ref _speedVel,
                                         accelerationSmoothTime, maxSpeed, dt);

        // 合成して適用：Euler(pitch=X, heading=Y, roll=Z)。
        // Z(ロール)は前方+Z まわりの回転なので forward を変えない＝進路に影響しない。
        transform.rotation = Quaternion.Euler(_pitch, _heading, _roll);
        // 自走(前方) + 外部ドリフト(ワールド空間)。
        // ドリフトは「体の向きと無関係に押し流す」用途。登場演出で
        // “体は横向きのまま前へ滑り出す”といった分離した動きを作れる。
        transform.position += (transform.forward * _currentSpeed + _externalVelocity) * dt;
    }
    #endregion


    // =====================================================================
    #region リグ駆動(背骨のうねり)
    // =====================================================================
    void DriveSpine(float dt)
    {
        if (_spineRest == null || _spineRest.Length == 0) return;
        int n = _spineRest.Length;

        // 速度に応じて尾びれの振り(周波数・振幅)を変える＝速いほど力強く打つ
        float speed01 = maxSpeed > 0.001f ? Mathf.Clamp01(_currentSpeed / maxSpeed) : 0f;
        float beatFreq = Mathf.Lerp(idleBeatFrequency, maxBeatFrequency, speed01);
        float beatAmp = Mathf.Lerp(idleBeatAmplitude, maxBeatAmplitude, speed01);

        // 旋回バイアス：旋回中は体全体を旋回方向へ曲げ、尾の片振りを強める
        float turnBias = (maxYawRate > 0.001f)
            ? Mathf.Clamp(_yawRateActual / maxYawRate, -1f, 1f) : 0f;

        _beatPhase += beatFreq * Mathf.PI * 2f * dt;

        for (int i = 0; i < n; i++)
        {
            if (spineBones[i] == null) continue;
            float t = (n > 1) ? (float)i / (n - 1) : 1f;   // 0=前, 1=尾先
            float ampProfile = amplitudeAlongBody.Evaluate(t);

            // 頭→尾へ伝わる進行波（各セグメントが少しずつ遅れる）
            float phase = _beatPhase - t * phaseLagAlongBody;
            float wave = Mathf.Sin(phase) * beatAmp * ampProfile;

            // 旋回時の定常カーブ（後半ほど大きく曲げる）
            float curve = turnBias * turnBodyCurvature * ampProfile;

            float angle = bendSign * (wave + curve);

            // 休止時(_spineRest)からの相対回転。回転軸は「ボディ垂直軸」をローカル化したもの
            // なので、Blender 由来のボーン roll に関係なく必ず“左右の横うねり”になる。
            spineBones[i].localRotation = _spineRest[i] * Quaternion.AngleAxis(angle, _spineAxis[i]);
        }
    }

    void DrivePectorals(float dt)
    {
        if (_pecRest == null || _pecRest.Length == 0) return;
        float speed01 = maxSpeed > 0.001f ? Mathf.Clamp01(_currentSpeed / maxSpeed) : 0f;
        // 低速ほど胸びれでこまめに姿勢制御する魚の挙動を再現（遅いほど大きく羽ばたく）
        float amp = pectoralAmplitude * Mathf.Lerp(1.0f, 0.35f, speed01);
        _pecPhase += pectoralFrequency * Mathf.PI * 2f * dt;

        for (int i = 0; i < _pecRest.Length; i++)
        {
            if (pectoralBones[i] == null) continue;
            float offset = i * Mathf.PI; // 左右で逆位相
            float a = Mathf.Sin(_pecPhase + offset) * amp;
            pectoralBones[i].localRotation = _pecRest[i] * Quaternion.AngleAxis(a, _pecAxis[i]);
        }
    }
    #endregion


    // =====================================================================
    #region リグの初期化(休止ポーズ・回転軸の取得)
    // =====================================================================
    void CacheRig()
    {
        // 背骨
        if (spineBones != null && spineBones.Length > 0)
        {
            _spineRest = new Quaternion[spineBones.Length];
            _spineAxis = new Vector3[spineBones.Length];
            for (int i = 0; i < spineBones.Length; i++)
            {
                if (spineBones[i] == null) { _spineAxis[i] = Vector3.up; continue; }
                _spineRest[i] = spineBones[i].localRotation;
                // 魚ボディの「垂直軸(=transform.up)」を各ボーンのローカル空間へ変換し、
                // それを横うねりの回転軸にする。これでボーンの向きの癖に依存しない。
                _spineAxis[i] = spineBones[i].InverseTransformDirection(transform.up).normalized;
                if (_spineAxis[i].sqrMagnitude < 1e-6f) _spineAxis[i] = Vector3.up;
            }
        }

        // 胸びれ：上下に羽ばたかせたいので回転軸は「前後軸(transform.forward)」をローカル化
        if (pectoralBones != null && pectoralBones.Length > 0)
        {
            _pecRest = new Quaternion[pectoralBones.Length];
            _pecAxis = new Vector3[pectoralBones.Length];
            for (int i = 0; i < pectoralBones.Length; i++)
            {
                if (pectoralBones[i] == null) { _pecAxis[i] = Vector3.forward; continue; }
                _pecRest[i] = pectoralBones[i].localRotation;
                _pecAxis[i] = pectoralBones[i].InverseTransformDirection(transform.forward).normalized;
                if (_pecAxis[i].sqrMagnitude < 1e-6f) _pecAxis[i] = Vector3.forward;
            }
        }
    }
    #endregion


    // =====================================================================
    #region ボーン自動検出 (エディタ補助)
    // =====================================================================
    [ContextMenu("Find Bones Automatically")]
    public void AutoFindBones()
    {
        // このモデルの命名規則に合わせて背骨 bone_1..bone_4、胸びれ bone_13/bone_16 を探す。
        var spine = new System.Collections.Generic.List<Transform>();
        for (int i = 1; i <= 4; i++)
        {
            var b = FindDeep(transform, "bone_" + i);
            if (b != null) spine.Add(b);
        }
        if (spine.Count > 0) spineBones = spine.ToArray();

        var pecs = new System.Collections.Generic.List<Transform>();
        foreach (var name in new[] { "bone_13", "bone_16" })
        {
            var b = FindDeep(transform, name);
            if (b != null) pecs.Add(b);
        }
        if (pecs.Count > 0) pectoralBones = pecs.ToArray();

        Debug.Log($"[AlaskaPollokController] 背骨 {spine.Count} 本 / 胸びれ {pecs.Count} 本 を検出しました。", this);
    }

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
    #endregion


    // =====================================================================
    #region ユーティリティ & Gizmos
    // =====================================================================
    static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        // 遊泳範囲
        if (useBounds)
        {
            Vector3 c = Application.isPlaying ? _boundsWorldCenter
                                              : transform.position + boundsCenterOffset;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
            Gizmos.DrawWireCube(c, boundsSize);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.2f);
            Gizmos.DrawWireCube(c, boundsSize - Vector3.one * boundsMargin * 2f);
        }

        // 進行方向
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.6f);

        // 目的地
        if (_hasDestination)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_destination, _arriveRadius);
            Gizmos.DrawLine(transform.position, _destination);
        }
    }
    #endregion
}