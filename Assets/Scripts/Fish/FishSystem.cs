using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 魚の中央マネージャ。
///
/// 役割:
///   ・環境魚 (鯛/マグロ/ツナ) を所有し、毎フレーム 1 本の O(N) ループで動かす。
///     1 匹あたりの更新は <see cref="AmbientFish.Tick"/> で O(1)。近傍の総当たりループは一切無い。
///   ・maxFish 上限を持ち、超過時は最古の環境魚をその場でリサイクルする (Instantiate/Destroy しない)。
///   ・骨アニメは各魚プレハブの Animator (ループクリップ + CullCompletely) が担当し、
///     C# 側は Animator.speed を遊泳速度に比例させるだけ。
///   ・クイズ正解時に <see cref="EmitPollockFromMouth"/> を呼ぶと、スケトウダラを
///     ユーザーの口元 (headAnchor 相対) から 1 匹出し、泳ぎ去らせる。
///     口から出るのはスケトウダラのみ。個体は AlaskaPollokController が駆動する。
///
/// シーンに 1 つだけ置き、QuizManager から参照する。
/// </summary>
[DisallowMultipleComponent]
public class FishSystem : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    [System.Serializable]
    public class AmbientSpecies
    {
        [Tooltip("インスペクタ表示用の覚え書き。")]
        public string label = "";
        [Tooltip("環境魚プレハブ (リグ + Animator。*Swim / FishSwimAI は付けない)。")]
        public GameObject prefab;
        [Min(0), Tooltip("初期スポーンの抽選比率。")]
        public int weight = 1;

        [Header("速度")]
        public float cruiseSpeed = 0.9f;
        [Range(0f, 1f)] public float speedVariation = 0.25f;
        [Tooltip("Animator.speed の下限/上限 (巡航速度比)。")]
        public float animSpeedMin = 0.35f;
        public float animSpeedMax = 2.2f;

        [Header("旋回")]
        public float maxYawRate = 35f;      // deg/s
        public float maxPitchRate = 18f;    // deg/s
        public float turnSmoothTime = 0.6f;
        [Tooltip("宙返り防止のピッチ可動域。")]
        public float maxPitchAngle = 25f;
        [Tooltip("旋回時の見た目バンク角。")]
        public float maxBankAngle = 20f;

        [Header("徘徊 (Perlin)")]
        [Range(0f, 1f)] public float wanderYawAmplitude = 1f;
        public float wanderNoiseSpeed = 0.15f;

        [Header("泳ぎ方 (種別の癖)")]
        [Tooltip("進路に乗る横うねり (S字) の振れ角 [deg]。鯛系 = 大、マグロ系 = ほぼ 0。")]
        public float swayAmplitude = 5f;
        [Tooltip("横うねりの周期 [s]。大きいほどゆったり。")]
        public float swayPeriod = 3f;

        [Header("推進のリズム (蹴る → 滑空)")]
        [Range(0f, 1f), Tooltip("前進速度の脈動。0 = 等速。")]
        public float thrustPulse = 0.25f;
        [Tooltip("尾ビートの周期 [s]。ベイククリップ長に合わせる。")]
        public float beatPeriod = 0.7f;
        [Range(0f, 1f), Tooltip("旋回中の減速。1 = 最大旋回で最低速。")]
        public float turnSpeedPenalty = 0.4f;

        [Header("近隣回避 (全種を対象)")]
        [Tooltip("この距離以内の他個体 (種を問わず) を避ける [m]。0 で無効。")]
        public float neighborAvoidDist = 0f;
        [Range(0f, 3f), Tooltip("回避操舵の強さ。")]
        public float neighborAvoidWeight = 1.2f;

        [Header("群れ (同種のみ)")]
        [Tooltip("群れる種か。鯛のみ true 想定。")]
        public bool schooling = false;
        [Tooltip("仲間として意識する半径 [m]。")]
        public float schoolRadius = 4f;
        [Range(0f, 1f), Tooltip("群れの中心へ寄る強さ。")]
        public float cohesion = 0.3f;
        [Range(0f, 1f), Tooltip("仲間と向きを揃える強さ。")]
        public float alignment = 0.4f;
        [Range(0f, 2f), Tooltip("近づきすぎた仲間から離れる強さ。")]
        public float separation = 1f;
        [Tooltip("この距離以内の仲間から離れる [m]。")]
        public float separationDist = 1.4f;

        [Header("ユーザー周辺の遊泳帯")]
        [Tooltip("これより近いと離れる (m)。")]
        public float bandInner = 5f;
        [Tooltip("これより遠いと戻る (m)。")]
        public float bandOuter = 16f;
        [Range(0f, 1f), Tooltip("帯外での操舵の強さ。")]
        public float bandPull = 0.6f;
        [Tooltip("好む深度 = headAnchor.y + これ (m)。")]
        public float depthOffset = 0f;
        [Range(0f, 1f), Tooltip("深度への引き戻しの強さ。")]
        public float depthPull = 0.4f;

        [Header("向き補正")]
        [Tooltip("モデルの前進軸を進行方向へ合わせる Y 回転。_xplus 系 (前進 = ローカル -X) は 90。逆走/鏡像なら -90。")]
        public float modelYawOffset = 90f;
    }

    // ─────────────────────────────────────────────────────────────
    [Header("ユーザー基準点")]
    [SerializeField]
    [Tooltip("XR rig の Main Camera 直下に置いた空 GameObject。必須。")]
    private Transform headAnchor;

    [SerializeField]
    [Tooltip("headAnchor ローカルでのスケトウダラ出現位置 (口の奥 = わずかに下・後ろ)。")]
    private Vector3 mouthLocalOffset = new Vector3(0f, -0.02f, -0.05f);

    [Header("環境魚")]
    [SerializeField]
    private AmbientSpecies[] species;

    [SerializeField, Min(0)]
    private int maxFish = 40;

    [SerializeField, Min(0)]
    private int initialFishCount = 18;

    [SerializeField]
    private float spawnRingMin = 6f;

    [SerializeField]
    private float spawnRingMax = 14f;

    [SerializeField]
    [Tooltip("headAnchor.y からの相対。")]
    private float spawnDepthMin = -3f;

    [SerializeField]
    private float spawnDepthMax = 2f;

    [Header("スケトウダラ (口から)")]
    [SerializeField]
    private AlaskaPollokController pollockPrefab;

    [SerializeField, Min(1)]
    private int maxPollock = 12;

    [SerializeField]
    private float emergeRollAngle = 90f;

    [SerializeField]
    private float emergeSpeed = 0.4f;

    [SerializeField]
    private float emergeDistance = 0.7f;

    [SerializeField]
    private float emergeMinTime = 1.2f;

    [SerializeField]
    private float emergeSettleTime = 0.4f;

    [SerializeField]
    private float pollockWanderRadius = 6f;

    [Header("デバッグ")]
    [SerializeField]
    private bool logEvents = true;

    [SerializeField]
    private bool drawGizmos = true;

    // ─────────────────────────────────────────────────────────────
    private readonly List<AmbientFish> _ambient = new List<AmbientFish>();
    private int _recycleCursor;

    private readonly List<AlaskaPollokController> _pollocks = new List<AlaskaPollokController>();
    private readonly HashSet<AlaskaPollokController> _emerging = new HashSet<AlaskaPollokController>();
    private readonly Stack<AlaskaPollokController> _pollockPool = new Stack<AlaskaPollokController>();
    private readonly Dictionary<AlaskaPollokController, int> _pollockGen =
        new Dictionary<AlaskaPollokController, int>();
    private int _genCounter;

    private Transform _anchor;
    private Transform _fishParent;
    private int _phase;
    private int _totalWeight;

    // 均一空間ハッシュ (毎フレーム O(N) で再構築、近傍参照は平均 O(1))。
    // 群れ (同種) と近隣回避 (全種) の両方がこれを使う。
    private readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();
    private readonly Stack<List<int>> _cellPool = new Stack<List<int>>();
    private float _cellSize = 3f;
    private bool _anySchooling;
    private bool _anyAvoid;
    private bool _useGrid;

    public int AmbientCount => _ambient.Count;
    public int PollockCount => _pollocks.Count;

    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _fishParent = transform;
    }

    private void Start()
    {
        _anchor = headAnchor;
        if (_anchor == null)
        {
            Debug.LogError("[Fish] headAnchor が設定されていません。FishSystem は動作しません。", this);
            return;
        }

        _totalWeight = 0;
        _anySchooling = false;
        _anyAvoid = false;
        _cellSize = 3f;
        if (species != null)
        {
            foreach (AmbientSpecies s in species)
            {
                if (s == null || s.prefab == null)
                {
                    Debug.LogWarning("[Fish] species に prefab 未設定の要素があります。スキップします。", this);
                    continue;
                }
                _totalWeight += Mathf.Max(0, s.weight);
                if (s.schooling)
                {
                    _anySchooling = true;
                    _cellSize = Mathf.Max(_cellSize, s.schoolRadius);
                }
                if (s.neighborAvoidDist > 0f)
                {
                    _anyAvoid = true;
                    _cellSize = Mathf.Max(_cellSize, s.neighborAvoidDist);
                }
            }
        }
        _useGrid = _anySchooling || _anyAvoid;

        SpawnAmbient(Mathf.Min(initialFishCount, maxFish));

        if (logEvents)
        {
            Debug.Log($"[Fish] 起動: 環境魚 {_ambient.Count} 匹 / maxFish {maxFish} / maxPollock {maxPollock}");
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || _anchor == null)
            return;

        Vector3 anchorPos = _anchor.position;

        // 群れ or 近隣回避を使う種がいるときだけ空間ハッシュを張り直す (O(N))。
        if (_useGrid)
            RebuildGrid();

        // 唯一の O(N) tick。
        for (int i = 0; i < _ambient.Count; i++)
        {
            _ambient[i].Tick(dt, anchorPos, this, i);
        }

        // スケトウダラの遊泳範囲をユーザーへ追従 (最大 maxPollock 匹)。
        for (int i = _pollocks.Count - 1; i >= 0; i--)
        {
            AlaskaPollokController p = _pollocks[i];
            if (p == null)
            {
                _pollocks.RemoveAt(i);
                continue;
            }
            p.SetBoundsCenter(anchorPos);
        }
    }

    // ─────────────────────────────────────────────────────────────
    /// <summary>
    /// 環境魚を count 匹追加する (maxFish でクランプ)。実際に追加/リサイクルした数を返す。
    /// </summary>
    public int SpawnAmbient(int count)
    {
        if (_anchor == null || species == null || species.Length == 0 || _totalWeight <= 0)
            return 0;

        int done = 0;
        for (int n = 0; n < count; n++)
        {
            if (_ambient.Count >= maxFish)
            {
                if (_ambient.Count == 0)
                    break;
                RecycleOldestAmbient();
                done++;
                continue;
            }

            int speciesIndex = PickSpeciesIndex();
            if (speciesIndex < 0)
                break;
            AmbientSpecies cfg = species[speciesIndex];

            GameObject go = Instantiate(cfg.prefab, _fishParent);
            go.name = $"{cfg.prefab.name}_{_phase:000}";
            PlaceOnSpawnRing(go.transform);

            Animator animator = go.GetComponentInChildren<Animator>();
            if (animator != null)
                animator.cullingMode = AnimatorCullingMode.CullCompletely;

            AmbientFish fish = new AmbientFish();
            fish.Init(go.transform, animator, cfg, speciesIndex, _phase++);
            _ambient.Add(fish);
            done++;
        }
        return done;
    }

    private int PickSpeciesIndex()
    {
        int roll = Random.Range(0, _totalWeight);
        for (int i = 0; i < species.Length; i++)
        {
            AmbientSpecies s = species[i];
            if (s == null || s.prefab == null)
                continue;
            int w = Mathf.Max(0, s.weight);
            if (roll < w)
                return i;
            roll -= w;
        }
        return -1;
    }

    private void PlaceOnSpawnRing(Transform t)
    {
        float ang = Random.value * Mathf.PI * 2f;
        float dist = Random.Range(spawnRingMin, spawnRingMax);
        Vector3 p = _anchor.position + new Vector3(
            Mathf.Cos(ang) * dist,
            Random.Range(spawnDepthMin, spawnDepthMax),
            Mathf.Sin(ang) * dist);

        // 接線方向 + ジッタを初期ヘディングに。
        float headingYaw = ang * Mathf.Rad2Deg + 90f + Random.Range(-30f, 30f);
        t.SetPositionAndRotation(p, Quaternion.Euler(0f, headingYaw, 0f));
    }

    private void RecycleOldestAmbient()
    {
        if (_ambient.Count == 0)
            return;

        int idx = _recycleCursor % _ambient.Count;
        _recycleCursor++;

        AmbientFish fish = _ambient[idx];
        if (fish == null || fish.Tf == null)
        {
            _ambient.RemoveAt(idx);
            return;
        }

        PlaceOnSpawnRing(fish.Tf);
        fish.ResetState(_phase++);
    }

    // ─────────────────────── 近隣 (空間ハッシュ・全個体を格納) ───────────────────
    private void RebuildGrid()
    {
        foreach (List<int> bucket in _grid.Values)
        {
            bucket.Clear();
            _cellPool.Push(bucket);
        }
        _grid.Clear();

        float inv = 1f / Mathf.Max(_cellSize, 0.01f);
        for (int i = 0; i < _ambient.Count; i++)
        {
            AmbientFish f = _ambient[i];
            if (f == null || !f.Alive)
                continue;

            Vector3 p = f.Pos;
            long key = CellKey(Mathf.FloorToInt(p.x * inv), Mathf.FloorToInt(p.z * inv));
            if (!_grid.TryGetValue(key, out List<int> bucket))
            {
                bucket = _cellPool.Count > 0 ? _cellPool.Pop() : new List<int>(8);
                _grid[key] = bucket;
            }
            bucket.Add(i);
        }
    }

    private static long CellKey(int cx, int cz)
    {
        return ((long)(cx + 0x40000000) << 32) | (uint)(cz + 0x40000000);
    }

    /// <summary>
    /// 自セル + 水平 8 近傍を 1 回だけ走査して、群れ (同種の結合/整列/分離) と
    /// 近隣回避 (全種) の水平ステアベクトルをまとめて返す。平均 O(1)。
    /// </summary>
    public void NeighborSteer(int selfIndex, out Vector3 school, out Vector3 avoid)
    {
        school = Vector3.zero;
        avoid = Vector3.zero;

        if (!_useGrid || selfIndex < 0 || selfIndex >= _ambient.Count)
            return;

        AmbientFish self = _ambient[selfIndex];
        if (self == null || !self.Alive)
            return;

        AmbientSpecies cfg = self.Cfg;
        Vector3 pos = self.Pos;

        bool wantSchool = cfg.schooling;
        bool wantAvoid = cfg.neighborAvoidDist > 0f;
        if (!wantSchool && !wantAvoid)
            return;

        float schoolR2 = cfg.schoolRadius * cfg.schoolRadius;
        float sepDist = cfg.separationDist;
        float avoidDist = cfg.neighborAvoidDist;
        float avoidR2 = avoidDist * avoidDist;

        Vector3 center = Vector3.zero;
        Vector3 align = Vector3.zero;
        Vector3 schoolSep = Vector3.zero;
        int schoolCount = 0;

        float inv = 1f / Mathf.Max(_cellSize, 0.01f);
        int cx = Mathf.FloorToInt(pos.x * inv);
        int cz = Mathf.FloorToInt(pos.z * inv);

        for (int gx = cx - 1; gx <= cx + 1; gx++)
        {
            for (int gz = cz - 1; gz <= cz + 1; gz++)
            {
                if (!_grid.TryGetValue(CellKey(gx, gz), out List<int> bucket))
                    continue;

                for (int k = 0; k < bucket.Count; k++)
                {
                    int j = bucket[k];
                    if (j == selfIndex)
                        continue;
                    AmbientFish o = _ambient[j];
                    if (o == null || !o.Alive)
                        continue;

                    Vector3 dP = o.Pos - pos;
                    dP.y = 0f;
                    float d2 = dP.x * dP.x + dP.z * dP.z;
                    if (d2 < 1e-4f)
                        continue;
                    float d = Mathf.Sqrt(d2);

                    // 近隣回避 (種を問わない)
                    if (wantAvoid && d2 < avoidR2)
                        avoid -= dP / d * ((avoidDist - d) / avoidDist);

                    // 群れ (同種のみ)
                    if (wantSchool && o.SpeciesIndex == self.SpeciesIndex && d2 < schoolR2)
                    {
                        center += o.Pos;
                        align += o.Fwd;
                        schoolCount++;
                        if (d < sepDist)
                            schoolSep -= dP / d * ((sepDist - d) / sepDist);
                    }
                }
            }
        }

        if (wantAvoid && avoid.sqrMagnitude > 1e-4f)
            avoid = avoid.normalized * cfg.neighborAvoidWeight;

        if (wantSchool && schoolCount > 0)
        {
            Vector3 toCenter = center / schoolCount - pos;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 1e-4f)
                school += toCenter.normalized * cfg.cohesion;

            align.y = 0f;
            if (align.sqrMagnitude > 1e-4f)
                school += align.normalized * cfg.alignment;

            school += schoolSep * cfg.separation;
        }
    }

    // ─────────────────────────────────────────────────────────────
    /// <summary>
    /// クイズ正解時に呼ぶ唯一の窓口。スケトウダラをユーザーの口から 1 匹出す。
    /// </summary>
    public void EmitPollockFromMouth()
    {
        if (pollockPrefab == null || _anchor == null)
        {
            Debug.LogWarning("[Fish] pollockPrefab か headAnchor が未設定のため口出し演出をスキップします。", this);
            return;
        }

        if (_pollocks.Count >= maxPollock)
            RecycleOldestPollock();

        AlaskaPollokController fish = RentPollock();
        _pollocks.Add(fish);
        StartCoroutine(EmergeRoutine(fish, _anchor));

        if (logEvents)
            Debug.Log($"[Fish] スケトウダラを口から放出 ({_pollocks.Count}/{maxPollock})", this);
    }

    private AlaskaPollokController RentPollock()
    {
        AlaskaPollokController fish = null;
        while (_pollockPool.Count > 0 && fish == null)
        {
            fish = _pollockPool.Pop();
        }
        if (fish == null)
        {
            fish = Instantiate(pollockPrefab, _fishParent);
        }
        return fish;
    }

    private void RecycleOldestPollock()
    {
        for (int i = 0; i < _pollocks.Count; i++)
        {
            AlaskaPollokController p = _pollocks[i];
            if (p == null)
            {
                _pollocks.RemoveAt(i);
                i--;
                continue;
            }
            if (_emerging.Contains(p))
                continue;

            _pollocks.RemoveAt(i);
            _pollockGen.Remove(p);
            p.gameObject.SetActive(false);
            _pollockPool.Push(p);
            return;
        }
        // 全個体が emerging 中: 今回は 1 匹オーバーフローを許容 (次回の emit で自己修正)。
    }

    private IEnumerator EmergeRoutine(AlaskaPollokController fish, Transform anchor)
    {
        if (fish == null)
            yield break;

        int gen = ++_genCounter;
        _pollockGen[fish] = gen;
        _emerging.Add(fish);

        fish.useBounds = false;
        fish.SetWandering(false);

        Vector3 mouthPos = anchor.TransformPoint(mouthLocalOffset);
        float yaw = YawOf(Flatten(anchor.forward));

        if (!fish.gameObject.activeSelf)
            fish.gameObject.SetActive(true);

        fish.SnapTo(mouthPos, yaw);
        fish.SetRollImmediate(emergeRollAngle);
        fish.MoveForward(emergeSpeed);

        float t = 0f;
        while (!IsStale(fish, gen))
        {
            t += Time.deltaTime;
            float dist = Vector3.Distance(fish.transform.position, anchor.position);
            float p = emergeDistance > 1e-4f ? Mathf.Clamp01(dist / emergeDistance) : 1f;
            fish.SetRollImmediate(Mathf.Lerp(emergeRollAngle, 0f, Mathf.SmoothStep(0f, 1f, p)));

            if (dist >= emergeDistance && t >= emergeMinTime)
                break;
            yield return null;
        }
        if (IsStale(fish, gen))
        {
            _emerging.Remove(fish);
            yield break;
        }

        fish.SetRoll(0f);
        yield return new WaitForSeconds(emergeSettleTime);
        if (IsStale(fish, gen))
        {
            _emerging.Remove(fish);
            yield break;
        }

        fish.Cruise();
        Vector3 bs = fish.boundsSize;
        fish.boundsSize = new Vector3(pollockWanderRadius * 2f, bs.y, pollockWanderRadius * 2f);
        fish.useBounds = true;
        fish.SetBoundsCenter(anchor.position);
        fish.SetWandering(true);
        fish.ClearManualOverride();

        _emerging.Remove(fish);
    }

    private bool IsStale(AlaskaPollokController fish, int gen)
    {
        return fish == null
            || !_pollockGen.TryGetValue(fish, out int g)
            || g != gen;
    }

    // ─────────────────────────────────────────────────────────────
    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
    }

    private static float YawOf(Vector3 dir)
    {
        return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    }

    // ─────────────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Transform a = _anchor != null ? _anchor : headAnchor;
        if (a == null)
            return;

        Vector3 c = a.position;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        DrawRing(c, spawnRingMin);
        DrawRing(c, spawnRingMax);

        if (species != null)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.35f);
            foreach (AmbientSpecies s in species)
            {
                if (s == null)
                    continue;
                DrawRing(c, s.bandInner);
                DrawRing(c, s.bandOuter);
            }
        }

        Gizmos.color = Color.yellow;
        for (int i = 0; i < _ambient.Count; i++)
        {
            if (_ambient[i] == null || _ambient[i].Tf == null)
                continue;
            Transform ft = _ambient[i].Tf;
            Gizmos.DrawRay(ft.position, -ft.right * 0.6f);
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(a.TransformPoint(mouthLocalOffset), 0.03f);
    }

    private static void DrawRing(Vector3 center, float radius, int seg = 48)
    {
        float step = Mathf.PI * 2f / seg;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float ang = i * step;
            Vector3 next = center + new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Test: 口からスケトウダラ")]
    private void TestEmitPollock() => EmitPollockFromMouth();

    [ContextMenu("Test: 環境魚 +1")]
    private void TestSpawnAmbient() => SpawnAmbient(1);
#endif
}
