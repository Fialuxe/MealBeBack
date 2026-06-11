using UnityEngine;

/// <summary>
/// 魚プレハブをスポーンし、FishSwimAI で種別ごとのパラメータを設定する。
///
///   鯛  (seabream)  — 近距離・俊敏・群れ泳ぎ
///   マグロ (bluefin) — 遠距離・高速・大きなカーブ
///   ツナ  (tuna)    — 中遠距離・中速
///
/// 空の GameObject に付けて Player を設定するだけで動く。
/// </summary>
public class AquariumSceneSetup : MonoBehaviour
{
    [System.Serializable]
    public class FishConfig
    {
        [Tooltip("Resources/ 以下のパス（拡張子なし）。")]
        public string prefabPath;
        [Min(1)] public int count = 4;

        [Header("速度")]
        [Tooltip("巡航速度 (m/s)")]
        public float cruiseSpeed  = 1.2f;
        [Tooltip("最大速度 (m/s)")]
        public float maxSpeed     = 3.0f;
        [Tooltip("最低速度 (m/s)")]
        public float minSpeed     = 0.2f;
        [Tooltip("加速度 (m/s²)")]
        public float acceleration = 1.0f;
        [Tooltip("減速度 (m/s²)")]
        public float deceleration = 0.7f;
        [Tooltip("旋回中の速度低下係数")]
        [Range(0f, 1f)] public float turnSpeedPenalty = 0.55f;

        [Header("旋回")]
        [Tooltip("最大旋回角速度 (deg/s)")]
        public float maxTurnRate  = 40f;
        [Tooltip("旋回慣性時定数 (s)")]
        public float turnInertia  = 0.3f;

        [Header("エリア")]
        [Tooltip("プレイヤーから好む距離 (m)")]
        public float preferredDist  = 7f;
        [Tooltip("許容幅 (±m)")]
        public float distTolerance  = 3f;
        [Tooltip("好みの高度オフセット (m)")]
        public float heightOffset   = 0f;

        [Header("ワンダリング")]
        [Tooltip("左右揺れの強さ (0–0.8)")]
        [Range(0f, 0.8f)] public float lateralAmp = 0.25f;
        [Tooltip("揺れの半周期 (s)")]
        public float lateralPeriod = 12f;

        [Header("群れ")]
        public bool schooling = false;
    }

    // ─────────────────────────────────────────────
    [Header("プレイヤー")]
    [Tooltip("空のとき Camera.main を自動使用。")]
    public Transform player;

    [Header("種別")]
    // 鯛: 近距離・小型。慣性はそれほど大きくないが、頻繁には向き変えしない。
    public FishConfig seabream = new FishConfig
    {
        prefabPath    = "prefabs/seabream_rigged_xplus",
        count         = 6,
        cruiseSpeed   = 0.8f,   maxSpeed  = 2.2f,   minSpeed     = 0.15f,
        acceleration  = 0.6f,   deceleration = 0.4f, turnSpeedPenalty = 0.45f,
        maxTurnRate   = 20f,    turnInertia  = 0.6f,
        preferredDist = 5f,     distTolerance = 2f,   heightOffset = 0f,
        lateralAmp    = 0.12f,  lateralPeriod = 18f,
        schooling     = true
    };

    // マグロ: 遠距離・大型・高速。大きな魚体のため旋回慣性が大きい。
    public FishConfig bluefin = new FishConfig
    {
        prefabPath    = "prefabs/bluefin_rigged_xplus",
        count         = 3,
        cruiseSpeed   = 2.0f,   maxSpeed  = 5.0f,   minSpeed     = 0.5f,
        acceleration  = 1.8f,   deceleration = 1.0f, turnSpeedPenalty = 0.55f,
        maxTurnRate   = 12f,    turnInertia  = 1.2f,
        preferredDist = 11f,    distTolerance = 3.5f, heightOffset = -0.5f,
        lateralAmp    = 0.06f,  lateralPeriod = 32f,
        schooling     = false
    };

    // ツナ: 中遠距離・中速。
    public FishConfig tuna = new FishConfig
    {
        prefabPath    = "prefabs/tuna_rigged_xplus",
        count         = 2,
        cruiseSpeed   = 1.5f,   maxSpeed  = 4.0f,   minSpeed     = 0.4f,
        acceleration  = 1.4f,   deceleration = 0.8f, turnSpeedPenalty = 0.5f,
        maxTurnRate   = 15f,    turnInertia  = 0.9f,
        preferredDist = 9f,     distTolerance = 3f,   heightOffset = 0.5f,
        lateralAmp    = 0.08f,  lateralPeriod = 24f,
        schooling     = false
    };

    // ─────────────────────────────────────────────
    void Awake()
    {
        if (player == null && Camera.main != null)
            player = Camera.main.transform;
    }

    void Start()
    {
        SpawnSpecies(seabream);
        SpawnSpecies(bluefin);
        SpawnSpecies(tuna);
    }

    // ─────────────────────────────────────────────
    void SpawnSpecies(FishConfig cfg)
    {
        var prefab = Resources.Load<GameObject>(cfg.prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[AquariumSceneSetup] Resources/{cfg.prefabPath} が見つかりません。", this);
            return;
        }

        Vector3 origin = player != null ? player.position : Vector3.zero;

        for (int i = 0; i < cfg.count; i++)
        {
            // 好み距離付近にランダム配置（円弧上ではなく散在させる）
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist  = cfg.preferredDist + Random.Range(-cfg.distTolerance * 0.5f, cfg.distTolerance * 0.5f);
            Vector3 spawnPos = new Vector3(
                origin.x + Mathf.Cos(angle) * dist,
                origin.y + cfg.heightOffset + Random.Range(-1.0f, 1.0f),
                origin.z + Mathf.Sin(angle) * dist
            );

            // 最初から進行方向を向かせる（ランダムな接線方向）
            float faceAngle = angle + Mathf.PI * 0.5f + Random.Range(-0.3f, 0.3f);
            Vector3 facingDir = new Vector3(-Mathf.Sin(faceAngle), 0f, Mathf.Cos(faceAngle));
            float yaw = Mathf.Atan2(facingDir.z, -facingDir.x) * Mathf.Rad2Deg;

            var go = Instantiate(prefab, spawnPos, Quaternion.Euler(0f, yaw, 0f), transform);
            go.name = $"{prefab.name}_{i:00}";
            Configure(go, cfg, i);
        }
    }

    void Configure(GameObject fish, FishConfig cfg, int index)
    {
        // FishSwimAI を追加・設定
        var ai = fish.AddComponent<FishSwimAI>();
        ai.player          = player;
        ai.cruiseSpeed     = cfg.cruiseSpeed;
        ai.maxSpeed        = cfg.maxSpeed;
        ai.minSpeed        = cfg.minSpeed;
        ai.acceleration    = cfg.acceleration;
        ai.deceleration    = cfg.deceleration;
        ai.turnSpeedPenalty = cfg.turnSpeedPenalty;
        ai.maxTurnRate     = cfg.maxTurnRate;
        ai.turnInertia     = cfg.turnInertia;
        ai.levelingSpeed      = 45f;
        ai.heightPullStrength = 0.35f;
        ai.preferredDist      = cfg.preferredDist;
        ai.distTolerance   = cfg.distTolerance;
        ai.avoidDist       = 2f;
        ai.heightOffset    = cfg.heightOffset;
        ai.verticalRange   = 2f;
        ai.lateralAmp      = cfg.lateralAmp;
        ai.lateralPeriod   = cfg.lateralPeriod;
        ai.schooling       = cfg.schooling;

        // 種ごとの最小間隔（魚体サイズに合わせる）
        bool isBluefin = cfg.prefabPath.Contains("bluefin");
        bool isTuna    = cfg.prefabPath.Contains("tuna");
        ai.minFishSeparation = isBluefin ? 2.2f : isTuna ? 1.6f : 1.0f;

        // 個体ごとにワンダリング位相をずらす（無理数倍で同期を防ぐ）
        ai.Init(index * 1.618f);

        // swim スクリプト：移動・旋回を停止し、ボーンアニメのみ残す
        var bf = fish.GetComponent<BluefinSwim>();
        if (bf != null) { bf.enableLocomotion = false; bf.enableWander = false; return; }

        var sb = fish.GetComponent<SeabreamSwim>();
        if (sb != null) { sb.externalControl = true; return; }

        var tn = fish.GetComponent<TunaSwim>();
        if (tn != null) { tn.externalControl = true; return; }
    }

    // ─────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        if (player == null) return;
        DrawRing(player.position, seabream.preferredDist, new Color(0.5f, 0.9f, 1f,  0.4f));
        DrawRing(player.position, bluefin.preferredDist,  new Color(0.2f, 0.5f, 1f,  0.4f));
        DrawRing(player.position, tuna.preferredDist,     new Color(0.1f, 0.3f, 0.9f, 0.4f));
    }

    static void DrawRing(Vector3 c, float r, Color col, int seg = 48)
    {
        Gizmos.color = col;
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
}
