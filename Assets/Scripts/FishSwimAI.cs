using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 魚の自律移動AIコントローラー。
///
/// 設計方針：
///   ・速度ベース移動 + 加速度 / 減速度で実際の魚らしいメリハリを出す
///   ・旋回は角加速度付き（慣性があるため急に曲がれない）
///   ・旋回中は速度が落ちる（物理的自然さ）
///   ・目標方向 = ワンダリング + エリア引力 + 群れ（seabream のみ）の合成
///   ・ワンダリングはサイン波ベース。フレームごとの乱数は一切使わない。
///
/// パフォーマンス方針：
///   ・各個体はフレーム先頭で自分の位置・前方を cachedPos / cachedFwd に保存する。
///     近隣探索（回避・群れ・分離）の O(N²) ループはこのキャッシュを読むだけで、
///     Transform のネイティブ呼び出しを完全に排除する（1フレーム遅れは boids では無視できる）。
///   ・transform.position はフレーム中ローカル変数で扱い、最後に一度だけ書き戻す。
///   ・水平維持の eulerAngles 変換は、実際に傾いている時だけ実行する。
///
/// 全 _xplus モデルの前進軸はローカル -X 固定。
/// swim スクリプトは externalControl = true / enableLocomotion = false でアニメ専任。
/// AquariumSceneSetup が AddComponent 後に Init() を呼ぶ。
/// </summary>
[DisallowMultipleComponent]
public class FishSwimAI : MonoBehaviour
{
    // ──────────────────────────────────────────
    [Header("プレイヤー")]
    [Tooltip("魚が周辺に留まる基準点。空のとき Camera.main を自動使用。")]
    public Transform player;

    // ──────────────────────────────────────────
    [Header("速度 (m/s)")]
    [Tooltip("通常時の巡航速度。加減速はここを目標に行われる。")]
    public float cruiseSpeed   = 1.2f;
    [Tooltip("最大速度（瞬間加速など）。")]
    public float maxSpeed      = 3.0f;
    [Tooltip("最低速度。完全に止まらないようにする。")]
    public float minSpeed      = 0.2f;

    [Header("加減速 (m/s²)")]
    public float acceleration  = 1.0f;
    public float deceleration  = 0.7f;
    [Tooltip("旋回中のペナルティ係数。0=速度変化なし / 1=最大旋回時にminSpeedまで落とす。")]
    [Range(0f, 1f)] public float turnSpeedPenalty = 0.55f;

    // ──────────────────────────────────────────
    [Header("旋回")]
    [Tooltip("最大旋回角速度 (deg/s)。大きいほど機敏。")]
    public float maxTurnRate    = 40f;
    [Tooltip("旋回の慣性時定数 (s)。大きいほど旋回変化がゆっくり。")]
    public float turnInertia    = 0.3f;
    [Tooltip("ピッチ・ロールを水平に戻す速度 (deg/s)。")]
    public float levelingSpeed  = 45f;

    // ──────────────────────────────────────────
    [Header("遊泳エリア（プレイヤー中心）")]
    [Tooltip("プレイヤーから好む距離 (m)。")]
    public float preferredDist  = 7f;
    [Tooltip("好み距離の許容帯 (±m)。この範囲は自由遊泳。")]
    public float distTolerance  = 3f;
    [Tooltip("これより近いと逃げる (m)。")]
    public float avoidDist      = 2f;
    [Tooltip("好みの高度オフセット (プレイヤーY + m)。")]
    public float heightOffset   = 0f;
    [Tooltip("目標高度から離れたとき Y を引き戻す強さ（速度比率）。")]
    [Range(0f, 1f)] public float heightPullStrength = 0.35f;
    [Tooltip("高度の許容幅 (±m)。この範囲を超えると強制引き戻し。")]
    public float verticalRange  = 2f;

    // ──────────────────────────────────────────
    [Header("ワンダリング（サイン波）")]
    [Tooltip("左右揺れの振れ幅（0=直進 / 1=常に最大旋回）。")]
    [Range(0f, 0.8f)] public float lateralAmp    = 0.25f;
    [Tooltip("左右揺れの半周期 (s)。大きいほどゆったり蛇行。")]
    public float lateralPeriod  = 12f;

    // ──────────────────────────────────────────
    [Header("近隣回避（全種共通）")]
    [Tooltip("この距離以内の魚を避ける (m)。種を問わず全魚に適用。")]
    public float neighborAvoidDist   = 3.0f;
    [Tooltip("回避力の強さ。大きいほど素早く離れる。")]
    [Range(0f, 6f)] public float neighborAvoidWeight = 3.5f;
    [Tooltip("位置ベース分離の最小間隔 (m)。これ以下には絶対に近づかない。")]
    public float minFishSeparation   = 1.2f;

    // ──────────────────────────────────────────
    [Header("群れ (schooling — 鯛向け)")]
    public bool   schooling        = false;
    public float  schoolRadius     = 5f;
    [Range(0f, 1f)] public float cohesionWeight  = 0.25f;
    [Range(0f, 1f)] public float alignWeight     = 0.35f;
    [Range(0f, 2f)] public float separationWeight = 1.0f;
    public float  separationDist   = 1.5f;

    // ──────────────────────────────────────────
    // 近隣探索用のフレームスナップショット（他個体はこれを読むだけ）
    [System.NonSerialized] public Vector3 cachedPos;
    [System.NonSerialized] public Vector3 cachedFwd;

    // 内部状態
    Transform _tf;      // transform プロパティのルックアップを避けるキャッシュ
    float _speed;
    float _yawRate;
    float _latPhase;    // 個体ごとの位相（Init で設定）
    float _entryTimer;  // 合流ダッシュの残り時間 (s)
    float _entryMult = 1f; // 合流ダッシュ中の速度倍率

    // 全 FishSwimAI の共有リスト（近隣探索用）
    static readonly List<FishSwimAI> _all = new List<FishSwimAI>();

    void Awake()
    {
        _tf = transform;
        _all.Add(this);
    }

    void OnDestroy() => _all.Remove(this);

    /// <summary>
    /// AquariumSceneSetup が AddComponent 直後に呼ぶ。
    /// latPhase で個体ごとにワンダリングの位相をずらす。
    /// </summary>
    public void Init(float latPhase)
    {
        _latPhase = latPhase;
    }

    /// <summary>
    /// 生成直後に呼ぶと、一定時間だけ巡航・最大速度が上がる「合流ダッシュ」。
    /// 外側からスポーンした魚が素早く群れに泳ぎ込み、時間経過で自然に通常速度へ戻る。
    /// </summary>
    public void TriggerEntryDash(float duration, float speedMult)
    {
        _entryTimer = duration;
        _entryMult  = Mathf.Max(1f, speedMult);
    }

    void Start()
    {
        if (player == null && Camera.main != null)
            player = Camera.main.transform;
        _speed = cruiseSpeed;

        // 初期スナップショット（フレーム1の近隣探索に備える）
        cachedPos = _tf.position;
        cachedFwd = ComputeForward();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Transform tf = _tf;

        // フレーム先頭でスナップショット取得（以降 Transform のネイティブ読みは最小化）
        Vector3 pos = tf.position;
        Vector3 fwd = ComputeForward();
        cachedPos = pos;
        cachedFwd = fwd;

        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        bool    hasPlayer = player != null;
        Vector3 playerPos = hasPlayer ? player.position : Vector3.zero;

        // ─ 1. 目標方向を合成 ─
        Vector3 desired = DesiredHeading(pos, fwd, right, hasPlayer, playerPos);

        // ─ 2. ヨー旋回（角加速度付き） ─
        float yawErr    = Vector3.SignedAngle(fwd, desired, Vector3.up);
        float targetYaw = Mathf.Clamp(yawErr * 2f, -maxTurnRate, maxTurnRate);
        float yawAccel  = maxTurnRate / Mathf.Max(turnInertia, 0.001f);
        _yawRate = Mathf.MoveTowards(_yawRate, targetYaw, yawAccel * dt);
        tf.Rotate(Vector3.up, _yawRate * dt, Space.World);

        // 旋回後の前方を再取得（前進に使う）
        fwd = ComputeForward();
        cachedFwd = fwd;

        // ─ 3. 水平維持：傾いている時だけ eulerAngles 変換を行う（天井飛び防止の保険） ─
        Vector3 worldUp = tf.rotation * Vector3.up;
        if (worldUp.y < 0.9999f)
        {
            Vector3 e = tf.eulerAngles;
            float ex = e.x > 180f ? e.x - 360f : e.x;
            float ez = e.z > 180f ? e.z - 360f : e.z;
            tf.eulerAngles = new Vector3(
                Mathf.MoveTowards(ex, 0f, levelingSpeed * dt),
                e.y,
                Mathf.MoveTowards(ez, 0f, levelingSpeed * dt)
            );
            fwd = ComputeForward();
            cachedFwd = fwd;
        }

        // ─ 4. 高度補正（ローカル pos を直接引き戻す。書き戻しは最後に1回） ─
        if (hasPlayer)
        {
            float targetY = playerPos.y + heightOffset;
            float yErr    = targetY - pos.y;
            float yMove   = Mathf.Clamp(yErr * 3f, -_speed * heightPullStrength, _speed * heightPullStrength);
            pos.y = Mathf.Clamp(pos.y + yMove * dt, targetY - verticalRange, targetY + verticalRange);
        }

        // ─ 5. 速度（旋回ペナルティ + 加減速 + 合流ダッシュ） ─
        float boost = 1f;
        if (_entryTimer > 0f)
        {
            _entryTimer -= dt;
            boost = _entryMult;   // タイマー終了で 1 に戻り、自然に減速していく
        }
        float turnRatio   = Mathf.Abs(_yawRate) / Mathf.Max(maxTurnRate, 0.001f);
        float targetSpeed = Mathf.Lerp(cruiseSpeed, minSpeed, turnRatio * turnSpeedPenalty) * boost;
        targetSpeed = Mathf.Clamp(targetSpeed, minSpeed, maxSpeed * boost);
        float accel = (_speed < targetSpeed) ? acceleration : deceleration;
        _speed = Mathf.MoveTowards(_speed, targetSpeed, accel * dt);

        // ─ 6. 前進 ─
        pos += fwd * (_speed * dt);

        // ─ 7. 位置ベース分離（ステアリングで間に合わない場合の最終防衛） ─
        // 各魚が自分を押し出す。相手も同じ処理をするので実質 0.5 ずつ分担。
        if (minFishSeparation > 0f)
        {
            float sepSqr = minFishSeparation * minFishSeparation;
            for (int i = 0; i < _all.Count; i++)
            {
                var f = _all[i];
                if (f == this) continue;
                Vector3 fp = f.cachedPos;            // 水平のみ — Y は高度補正と競合させない
                float dx = pos.x - fp.x;
                float dz = pos.z - fp.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < sepSqr && d2 > 1e-6f)
                {
                    float dist = Mathf.Sqrt(d2);
                    float push = (minFishSeparation - dist) * 0.5f / dist;
                    pos.x += dx * push;
                    pos.z += dz * push;
                }
            }
        }

        // ─ 位置を一度だけ書き戻す ─
        tf.position = pos;
        cachedPos   = pos;
    }

    // ──────────────────────────────────────────
    Vector3 DesiredHeading(Vector3 pos, Vector3 fwd, Vector3 right, bool hasPlayer, Vector3 playerPos)
    {
        // (a) ワンダリング：サイン波で左右にゆっくり揺れる
        float cycle = Mathf.PI * 2f / Mathf.Max(lateralPeriod, 0.001f);
        float lat   = Mathf.Sin(Time.time * cycle + _latPhase) * lateralAmp;
        Vector3 wander = (fwd + right * lat).normalized;

        // (b) エリア引力・反発：プレイヤーとの距離に応じたソフトな力
        Vector3 area = Vector3.zero;
        if (hasPlayer)
        {
            Vector3 toPlayer = playerPos - pos;
            toPlayer.y = 0f;
            float dist  = toPlayer.magnitude;
            float outer = preferredDist + distTolerance;
            float inner = Mathf.Max(avoidDist, preferredDist - distTolerance);

            if (dist > outer)
            {
                // 遠すぎ → プレイヤー方向へ（dist を再利用して normalized の sqrt を省く）
                float w = Mathf.Clamp01((dist - outer) / preferredDist);
                area = toPlayer * (w / dist);
            }
            else if (dist < inner && dist > 0.01f)
            {
                // 近すぎ → 離れる（逃げ）
                float w = Mathf.Clamp01((inner - dist) / Mathf.Max(inner - avoidDist, 0.01f));
                area = toPlayer * (-w * 1.5f / dist);
            }
        }

        // (c) 近隣回避（種を問わず全魚を対象）
        Vector3 avoid = NeighborAvoidForce(pos);

        // (d) 群れ（seabream 向け boids）
        Vector3 school = schooling ? SchoolForce(pos) : Vector3.zero;

        // 合成：回避が最優先、次にエリア・群れ、ワンダリングは基調
        Vector3 combined = wander + area * 0.8f + school * 0.6f + avoid * neighborAvoidWeight;
        return combined.sqrMagnitude > 0.001f ? combined.normalized : fwd;
    }

    Vector3 NeighborAvoidForce(Vector3 pos)
    {
        Vector3 sep = Vector3.zero;
        float rng    = neighborAvoidDist;
        float rngSqr = rng * rng;

        for (int i = 0; i < _all.Count; i++)
        {
            var f = _all[i];
            if (f == this) continue;

            Vector3 d = f.cachedPos - pos;
            float dist3dSqr = d.x * d.x + d.y * d.y + d.z * d.z;
            if (dist3dSqr > rngSqr || dist3dSqr < 1e-4f) continue;

            // 水平成分のみで操舵（真上/真下の魚を横に押し出さない）
            float distHSqr = d.x * d.x + d.z * d.z;
            if (distHSqr < 1e-4f) continue;

            float dist3d = Mathf.Sqrt(dist3dSqr);
            float distH  = Mathf.Sqrt(distHSqr);

            // 線形減衰: 境界で 0、接触で 1 — 遠い距離から早めに押し始める
            float k = (1f - dist3d / rng) / distH;
            sep.x -= d.x * k;
            sep.z -= d.z * k;
        }
        return sep;
    }

    Vector3 SchoolForce(Vector3 pos)
    {
        Vector3 cohesion = Vector3.zero;
        Vector3 align    = Vector3.zero;
        Vector3 sep      = Vector3.zero;
        int n = 0, sn = 0;

        float radiusSqr = schoolRadius * schoolRadius;
        float sepSqr    = separationDist * separationDist;

        for (int i = 0; i < _all.Count; i++)
        {
            var f = _all[i];
            if (f == this || !f.schooling) continue;

            Vector3 fp    = f.cachedPos;
            Vector3 delta = fp - pos;
            float d2 = delta.sqrMagnitude;
            if (d2 > radiusSqr) continue;

            cohesion += fp;
            align    += f.cachedFwd;
            n++;

            if (d2 < sepSqr && d2 > 1e-4f)
            {
                float dist = Mathf.Sqrt(d2);
                sep -= (delta / dist) * (separationDist / dist);
                sn++;
            }
        }

        Vector3 force = Vector3.zero;
        if (n > 0)
        {
            force += (cohesion / n - pos).normalized * cohesionWeight;
            force += align.normalized * alignWeight;
        }
        if (sn > 0) force += sep.normalized * separationWeight;
        return force;
    }

    // ローカル -X を前進軸とする（全 _xplus モデルの統一仕様）
    Vector3 ComputeForward() => _tf.TransformDirection(Vector3.left).normalized;

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (player == null) return;
        // 好み帯（内円・外円）
        Gizmos.color = new Color(0.2f, 0.9f, 0.5f, 0.3f);
        DrawWireCircle(player.position, preferredDist - distTolerance);
        DrawWireCircle(player.position, preferredDist + distTolerance);
        // 速度ベクトル（エディタ停止中は _tf 未設定のため transform を直接使う）
        Gizmos.color = Color.cyan;
        Vector3 f = transform.TransformDirection(Vector3.left).normalized;
        Gizmos.DrawRay(transform.position, f * _speed);
    }

    static void DrawWireCircle(Vector3 c, float r, int seg = 48)
    {
        float s = Mathf.PI * 2f / seg;
        Vector3 prev = c + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float a = i * s;
            Vector3 next = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif
}
