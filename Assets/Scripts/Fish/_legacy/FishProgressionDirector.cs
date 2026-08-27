using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 「正解するたびに魚が自然に合流して増えていく」進行管理。
///
/// 仕組み：
///   ・フェーズ配列を持ち、正解 = OnCorrectAnswer() のたびに次フェーズへ進む。
///   ・各フェーズは「この回で追加する鯛・ツナ・マグロの数」を持つ（混合方式）。
///     → 数を増やしつつ、途中フェーズから新種(ツナ→マグロ)を解放できる。
///   ・追加は AquariumSceneSetup.SpawnOne(fromOutside:true) を少し間隔を空けて呼ぶだけ。
///     合流の動き自体は FishSwimAI のエリア引力＋合流ダッシュが自動でやってくれる。
///
/// クイズ本体はまだ無いので、テスト用にキー入力／インスペクタの右クリックメニューで
/// OnCorrectAnswer() を叩けるようにしてある。クイズ実装後は正解処理から
/// この OnCorrectAnswer() を呼ぶだけで連携できる。
/// </summary>
public class FishProgressionDirector : MonoBehaviour
{
    [System.Serializable]
    public class Phase
    {
        [Tooltip("インスペクタ表示用の覚え書き。挙動には影響しない。")]
        public string label;
        [Min(0)] public int seabream;   // この回で追加する鯛の数
        [Min(0)] public int tuna;       // この回で追加するツナの数
        [Min(0)] public int bluefin;    // この回で追加するマグロの数
    }

    [Header("参照")]
    [Tooltip("空なら同じ／子オブジェクトから自動取得。")]
    public AquariumSceneSetup aquarium;

    [Header("フェーズ進行（正解1回 = 1フェーズ）")]
    [Tooltip("混合方式：数を増やしつつ、途中から新種を解放する。")]
    public Phase[] phases =
    {
        new Phase { label = "鯛が2匹合流",        seabream = 2 },
        new Phase { label = "鯛が2匹合流",        seabream = 2 },
        new Phase { label = "ツナ登場(+鯛1)",     seabream = 1, tuna = 1 },
        new Phase { label = "鯛2・ツナ1",         seabream = 2, tuna = 1 },
        new Phase { label = "マグロ登場",          bluefin = 1 },
        new Phase { label = "ツナ1・マグロ1",      tuna = 1, bluefin = 1 },
        new Phase { label = "大群(鯛3・ツナ1)",   seabream = 3, tuna = 1 },
    };

    [Tooltip("最終フェーズ到達後も正解で最後のフェーズ内容を繰り返す。")]
    public bool repeatLastPhase = true;

    [Header("投入テンポ")]
    [Tooltip("1匹ごとの投入間隔 (s)。少しずつ来るほど自然。")]
    public float spawnInterval = 0.5f;

    [Header("テスト用フック（クイズ未実装のうちの確認用）")]
    [Tooltip("このキーで OnCorrectAnswer() を発火（テスト）。None で無効。")]
    public KeyCode testKey = KeyCode.Space;

    int _phaseIndex;

    void Awake()
    {
        if (aquarium == null)
            aquarium = GetComponent<AquariumSceneSetup>() ?? GetComponentInChildren<AquariumSceneSetup>();
    }

    void Update()
    {
        if (testKey != KeyCode.None && Input.GetKeyDown(testKey))
            OnCorrectAnswer();
    }

    /// <summary>
    /// クイズで正解したときに呼ぶ唯一の窓口。次フェーズの魚を投入する。
    /// </summary>
    public void OnCorrectAnswer()
    {
        if (aquarium == null)
        {
            Debug.LogWarning("[FishProgressionDirector] AquariumSceneSetup が未設定です。", this);
            return;
        }
        if (phases == null || phases.Length == 0) return;

        if (_phaseIndex >= phases.Length)
        {
            if (!repeatLastPhase) return;
            _phaseIndex = phases.Length - 1;   // 最終フェーズを繰り返す
        }

        Phase p = phases[_phaseIndex];
        _phaseIndex++;
        StartCoroutine(SpawnBatch(p));
    }

    IEnumerator SpawnBatch(Phase p)
    {
        // 種をまとめず交互に出すと、より「色々な魚が集まってくる」感じになる
        var queue = new List<AquariumSceneSetup.FishConfig>();
        for (int i = 0; i < p.seabream; i++) queue.Add(aquarium.seabream);
        for (int i = 0; i < p.tuna;     i++) queue.Add(aquarium.tuna);
        for (int i = 0; i < p.bluefin;  i++) queue.Add(aquarium.bluefin);

        // 投入順をシャッフル（種が偏って一斉に来ないように）
        for (int i = queue.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (queue[i], queue[j]) = (queue[j], queue[i]);
        }

        foreach (var cfg in queue)
        {
            aquarium.SpawnOne(cfg, fromOutside: true);
            if (spawnInterval > 0f)
                yield return new WaitForSeconds(spawnInterval);
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Test: 正解（次フェーズを投入）")]
    void TestAdvance() => OnCorrectAnswer();

    [ContextMenu("Test: フェーズを最初に戻す")]
    void TestReset() => _phaseIndex = 0;
#endif
}
