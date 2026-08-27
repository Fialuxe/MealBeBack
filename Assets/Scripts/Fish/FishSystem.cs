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
            }
        }

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

        // 唯一の O(N) tick。
        for (int i = 0; i < _ambient.Count; i++)
        {
            _ambient[i].Tick(dt, anchorPos);
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

            AmbientSpecies cfg = PickSpecies();
            if (cfg == null)
                break;

            GameObject go = Instantiate(cfg.prefab, _fishParent);
            go.name = $"{cfg.prefab.name}_{_phase:000}";
            PlaceOnSpawnRing(go.transform);

            Animator animator = go.GetComponentInChildren<Animator>();
            if (animator != null)
                animator.cullingMode = AnimatorCullingMode.CullCompletely;

            AmbientFish fish = new AmbientFish();
            fish.Init(go.transform, animator, cfg, _phase++);
            _ambient.Add(fish);
            done++;
        }
        return done;
    }

    private AmbientSpecies PickSpecies()
    {
        int roll = Random.Range(0, _totalWeight);
        foreach (AmbientSpecies s in species)
        {
            if (s == null || s.prefab == null)
                continue;
            int w = Mathf.Max(0, s.weight);
            if (roll < w)
                return s;
            roll -= w;
        }
        return null;
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
