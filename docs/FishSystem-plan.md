# FishSystem 実装計画 (GitHub issue #29 + #27 連動)

## Context

担当: Fialuxe。issue #29「FishSystemの実装」。魚アニメの軽量化は issue #27 の方針に沿って本作業に含める（#27 コメントで作者本人が「Fish 関連で絡む」と明記）。

**なぜ必要か**
- クイズ正解時の「口から生き物が飛び出す」演出（`QuizManager.HandleCorrectAnswer()` の TODO）が未実装。
- 現状の魚まわりは荒削り:
  - `FishSwimAI.Update` に全個体総当たりループが 3 つ（近隣回避 / 群れ / 位置分離）＝ **O(N²)**、魚が増えると急に重い。
  - `SeabreamSwim` / `TunaSwim` / `BluefinSwim` が **個体数 × 最大 9 ボーンの三角関数**を毎 `LateUpdate` で回す。
  - GLB インポートが `skinUpdateWhenOffscreen: 1`＝画面外でも毎フレーム スキニング。
  - 旧 `fishWithAnimation`（`FishSpineAnimator` + `FishSwimController`）が二重にボーンを書いている可能性。
  - フレームレート依存 `Lerp` と毎フレーム乱数によるジッタ。

**達成したい状態**
- 正解ごとに **スケトウダラ (AlaskaPollok) がユーザーの口から現れて泳ぎ去る**。口から出るのはスケトウダラのみ。出現位置は常に XR カメラ相対で計算。
- その他の環境魚 (鯛 / マグロ / ツナ) は「水中を適切に泳ぐ」簡素な AI があればよい。
- **魚 1 体あたりの毎フレームコストを O(1)（全体 O(N)）**にする。全ペアループなし。C# の毎フレームボーン三角関数ゼロ。
- 中央マネージャが **maxFish 上限**を持ち、超過時は最古の環境魚をその場リサイクル。
- **旧魚スクリプト / 旧プレハブは使わない**（新規に作り直す）。`GameManager.cs` / `FogColorChange.cs` は触らない。

## 対象ブランチ / シーン

- 最新 `origin/main` から `features/FishSystem` を切る（`features/<Name>` 慣習）。
- シーン: `Assets/Scenes/WaterScene_SteamRuntime.unity` を複製した新シーン（例 `WaterScene_FishSystem.unity`）。**シーンのヒエラルキー / プレハブ配線・複製はユーザーがエディタで実施。** 本計画は目標状態を仕様として提示する。
- XR カメラ（`XR Origin (XR Rig) → Camera Offset → Main Camera`, tag `MainCamera`）は常にアクティブ。よって `Camera.main` タイミングの防御的処理は不要 —— `headAnchor` は必須シリアライズ参照とし `Start()` で 1 回解決する。

---

## アーキテクチャ概要

```
FishSystem (MonoBehaviour, シーンに1つ)
  ├─ Update() … 唯一の O(N) tick
  │     for each AmbientFish: fish.Tick(dt, headPos)      // 移動・向き O(1)/匹
  │     for each pollock: controller.SetBoundsCenter(headPos)  // ≤maxPollock
  ├─ 環境魚プール + maxFish 上限（最古その場リサイクル）
  ├─ 初期スポーン
  └─ EmitPollockFromMouth() + EmergeRoutine コルーチン（口出し演出）

AmbientFish (プレーン C# クラス、MonoBehaviour ではない)
  Tick(): Perlin 徘徊 + ユーザー周辺の遊泳帯 + 深度キープ + 見た目バンク
          → transform を1回書く / Animator.speed を1回セット
          （近傍クエリなし・ボーン計算なし）

環境魚プレハブ（新規）
  リグ付きメッシュ + Animator（ループ swim クリップ, CullCompletely）
  ※ SeabreamSwim 等・FishSwimAI は付けない

スケトウダラ = AlaskaPollok.prefab + AlaskaPollokController（現状維持）
  ヒーロー個体・≤maxPollock・口出しに繊細なロール制御が要るため手続き型のまま
```

**O(N) の保証:** 魚システムで `MonoBehaviour.Update` は `FishSystem.Update` の 1 つだけ。環境魚は `List` 上の tight `for` で `Tick`。骨アニメは Animator（エンジン側 C++・ジョブ化・カリング可能）に移し、C# の per-fish `LateUpdate` を全廃。`AmbientFish` をプレーンクラスにするのは、誰かが後から `Update` を生やせない構造にするため。

---

## 1. アニメ軽量化（issue #27 の「やること」を実装）

### 1.1 方式 — ベイク済みループクリップ + Animator

1. **泳ぎ 1 周期のボーン角を事前計算 → ループ `AnimationClip`。** 実行時は Animator が phase を引くだけ。三角関数を毎フレーム回さない。
2. **`Animator.speed` を遊泳速度に比例**させて再生スピードを変える（`AmbientFish.Tick` で float を 1 回セット、O(1)）。クリップは公称ビートの「形」だけ持ち、周波数は speed 倍率で表現。
3. **`Animator.cullingMode = AnimatorCullingMode.CullCompletely`** + `SkinnedMeshRenderer.updateWhenOffscreen = false`。画面外の魚はアニメ評価もスキニングも停止＝ほぼ 0 コスト。
4. **魚 1 体＝アニメ経路 1 本。** 旧 `fishWithAnimation` 系統は新シーンに持ち込まない。二重ボーン書き込みを排除。
5. フレームレート非依存: `AmbientFish` の追従は `SmoothDampAngle`（レート制限付き）、徘徊は Perlin（連続関数＝急変なし）。毎フレーム乱数を使わない。

（#27 のうち「遠い魚を 2〜3 フレームに 1 回」「AI をグループ round-robin 分散」「Jobs/Burst」「Optimize Game Objects」は**本作業では入れない**。CullCompletely + O(N) tick で当面の不安定さは解消できるため。必要になれば follow-up。）

### 1.2 ビルドツール（エディタ専用・1 クリック生成）

`Assets/Scripts/Fish/Editor/FishBuildTool.cs` — `[MenuItem("Tools/Fish/Rebuild Fish Assets")]`

各種 (seabream / tuna / bluefin) について:
1. GLB リグ（`Assets/models/fishes/*_rigged_xplus.glb`）を一時インスタンス化。
2. 背骨ボーンを名前検索（`bone_0..8` 等、既存 `*Swim` と同じ規則）。
3. `t ∈ [0, 1)`（公称 1 ビート）を 48〜60 サンプル。各サンプルで進行波の式
   `angle_i = ampCurve(i/n) * bendAngle * sin(2π t − i·phaseLag)` から各ボーンの `localRotation` を算出。
   パラメータ（`bendAngle` / `phaseLag` / `ampCurve` / 曲げ軸）は現行 `*Swim` の調整値をツール内の定数として取り込む（「テーブル化」= #27 の指示そのもの）。
4. `AnimationClip` を生成（各ボーンに `localEulerAnglesRaw` の 3 カーブ、`settings.loopTime = true`）→ `Assets/Animations/fish/<species>_swim.anim`。
5. 単一ループステートだけの `AnimatorController` を生成 → `Assets/Animations/fish/<species>.controller`。
6. **プレハブ変種を生成**: GLB モデルプレハブの variant に `Animator`（上記 controller、`cullingMode = CullCompletely`, `updateMode = Normal`）を追加、`SkinnedMeshRenderer.updateWhenOffscreen = false` を設定、`*Swim` は付けない → `Assets/Resources/prefabs/fish/<Species>.prefab`。
7. 一時インスタンスを破棄。

→ ユーザーはメニューを 1 回叩くだけ。手作業のアセット作成なし。再ベイクも同じメニュー。

**手動調整可能:** 生成物は通常の `AnimationClip` / `AnimatorController` アセット。
ベイク後に Unity の Animation ウィンドウでカーブを直接編集してもよいし、`FishBuildTool` のパラメータを変えて再ベイクしてもよい。
（再ベイクは同名アセットを上書きするので、手編集を残したい場合は別名保存すること。）

### 1.3 スケトウダラは対象外（意図的な例外）

`AlaskaPollokController` は自己完結・O(1)/frame（他個体参照なし）・API 完備でプレハブ配線済み。個体数 ≤ `maxPollock`（〜12）・ヒーロー個体・口出し演出に `SetRollImmediate` / `SnapTo` の繊細な制御が要る。ここを手続き型のまま残しても総コストへの影響は無視できる。`AlaskaPollok.prefab` はそのまま利用。

---

## 2. `Assets/Scripts/Fish/FishSystem.cs`（新規）

`[DisallowMultipleComponent] public class FishSystem : MonoBehaviour`

### Inspector フィールド

```
[Header("ユーザー基準点")]
[SerializeField] private Transform headAnchor;                 // 必須。Main Camera 直下の空 "MouthAnchor"
[SerializeField] private Vector3 mouthLocalOffset = new Vector3(0f, -0.02f, -0.05f);  // headAnchor ローカルの出現位置（口の奥）

[Header("環境魚")]
[SerializeField] private AmbientSpecies[] species;            // 既定3種
[SerializeField, Min(0)] private int maxFish = 40;
[SerializeField, Min(0)] private int initialFishCount = 18;
[SerializeField] private float spawnRingMin = 6f;
[SerializeField] private float spawnRingMax = 14f;
[SerializeField] private float spawnDepthMin = -3f;          // headAnchor.y からの相対
[SerializeField] private float spawnDepthMax = 2f;

[Header("スケトウダラ（口から）")]
[SerializeField] private AlaskaPollokController pollockPrefab;   // Assets/Resources/prefabs/AlaskaPollok
[SerializeField, Min(1)] private int maxPollock = 12;
[SerializeField] private float emergeRollAngle  = 90f;
[SerializeField] private float emergeSpeed      = 0.4f;
[SerializeField] private float emergeDistance   = 0.7f;
[SerializeField] private float emergeMinTime    = 1.2f;
[SerializeField] private float emergeSettleTime = 0.4f;
[SerializeField] private float pollockWanderRadius = 6f;

[Header("デバッグ")]
[SerializeField] private bool logEvents = true;
[SerializeField] private bool drawGizmos = true;
```

`AmbientSpecies` — ネスト `[System.Serializable]`:
`label`, `GameObject prefab`（`Assets/Resources/prefabs/fish/<Species>.prefab` を直接参照）, `int weight`,
速度: `cruiseSpeed`, `speedVariation`, `animSpeedMin=0.35`, `animSpeedMax=2.2`,
旋回: `maxYawRate`, `maxPitchRate`, `turnSmoothTime`, `maxPitchAngle`, `maxBankAngle`,
徘徊: `wanderYawAmplitude`, `wanderNoiseSpeed`,
遊泳帯: `bandInner`, `bandOuter`, `bandPull`, `depthOffset`, `depthRange`, `depthPull`,
向き補正: `modelYawOffset = 90f`（`_xplus` は前進軸ローカル -X。逆走/鏡像なら -90。再生時に目視確認）。

### 公開 API

```
public void EmitPollockFromMouth();   // 正解時に QuizManager から呼ぶ唯一の窓口
public int  SpawnAmbient(int count);  // 実際に増えた数（maxFish でクランプ）
public int  AmbientCount { get; }
public int  PollockCount { get; }
```

### 内部構造

- `List<AmbientFish> _ambient`（挿入順＝古い順）+ `int _recycleCursor`
- `List<AlaskaPollokController> _pollocks` + `HashSet<AlaskaPollokController> _emerging`
- `Dictionary<GameObject, Stack<GameObject>> _pool`
- `Transform _anchor`, `Transform _fishParent = transform`, `int _phase`

### ライフサイクル

- `Awake()`: `_fishParent = transform`。
- `Start()`: `_anchor = headAnchor`（null なら `Debug.LogError("[Fish] headAnchor 未設定")` して以降 no-op）→ `species` 検証 → `SpawnAmbient(Mathf.Min(initialFishCount, maxFish))`。
- `Update()` — **唯一の O(N) tick**:
  ```
  float dt = Time.deltaTime; if (dt <= 0f || _anchor == null) return;
  Vector3 a = _anchor.position;
  for (int i = 0; i < _ambient.Count; i++) _ambient[i].Tick(dt, a);
  for (int i = _pollocks.Count - 1; i >= 0; i--) {
      AlaskaPollokController p = _pollocks[i];
      if (p == null) { _pollocks.RemoveAt(i); continue; }
      p.SetBoundsCenter(a);
  }
  ```
  `LateUpdate` なし。

### スポーン (`SpawnAmbient`)

1. `_ambient.Count >= maxFish` → `RecycleOldestAmbient()`（その場再利用、`Destroy`/`Instantiate` なし）。
2. それ以外: `Rent(cfg.prefab)` → `PlaceOnSpawnRing(go)`（ランダム角・`dist∈[ringMin,ringMax]`・`y = a.y + Random(depthMin,depthMax)`・初期ヘディング=接線+ジッタ）→ `CacheAnimator(go)`（`GetComponentInChildren<Animator>()`、`cullingMode=CullCompletely` 再確認）→ `new AmbientFish().Init(go.transform, animator, cfg, _phase++)` → `_ambient.Add`。

`*Swim` の locomotion 無効化処理は**不要**（新プレハブに `*Swim` は付いていない）。

### maxFish リサイクル意味論

| 項目 | 決定 |
|---|---|
| コンテナ | `List<AmbientFish>` 挿入順 + `_recycleCursor` リング index |
| 最古 | `_ambient[_recycleCursor % _ambient.Count]`、選んで `_recycleCursor++` |
| despawn 動作 | **その場リサイクル**: 既存 GameObject を `PlaceOnSpawnRing` で再配置 + `AmbientFish.ResetState()`。GC ゼロ |
| 上限到達前の増加 | `_pool` から `Rent`（`Dictionary<prefab, Stack>`） |
| 種別 | リサイクル時に変更しない |

### スケトウダラ (`EmitPollockFromMouth` + `EmergeRoutine`)

`AlaskaPollokMouthEntrance.Sequence()` を `FishSystem` の private コルーチンに畳み込む。`AlaskaPollokController` の公開 API のみ使用。

```
public void EmitPollockFromMouth() {
    if (pollockPrefab == null || _anchor == null) { Debug.LogWarning("[Fish] 口出し演出をスキップ"); return; }
    if (_pollocks.Count >= maxPollock) RecycleOldestPollock();   // _emerging 中は除外。全て emerging なら1匹オーバーフロー許容
    AlaskaPollokController fish = RentPollock();                  // プール or Instantiate(pollockPrefab, _fishParent)
    _pollocks.Add(fish);
    StartCoroutine(EmergeRoutine(fish, _anchor));
    if (logEvents) Debug.Log($"[Fish] スケトウダラを口から放出 ({_pollocks.Count}/{maxPollock})");
}
```

`EmergeRoutine(fish, anchor)` — 世代 int + `IsStale(fish, gen)` を各 `yield` 後にチェック:
1. `_emerging.Add(fish)`; `boundsBackup = fish.useBounds`; `fish.useBounds = false`; `fish.SetWandering(false)`
2. `mouthPos = anchor.TransformPoint(mouthLocalOffset)`; `yaw = YawOf(Flatten(anchor.forward))`
3. `SetActive(true)`; `fish.SnapTo(mouthPos, yaw)`; `fish.SetRollImmediate(emergeRollAngle)`; `fish.MoveForward(emergeSpeed)`
4. ループ: `dist = Distance(fish.pos, anchor.pos)`; `p = clamp01(dist/emergeDistance)`; `fish.SetRollImmediate(Lerp(emergeRollAngle, 0, SmoothStep(p)))`; `dist >= emergeDistance && t >= emergeMinTime` で break
5. `fish.SetRoll(0)`; `WaitForSeconds(emergeSettleTime)`
6. `fish.Cruise()`; `fish.boundsSize = (pollockWanderRadius*2, boundsSize.y, pollockWanderRadius*2)`; `fish.useBounds = true`; `fish.SetBoundsCenter(anchor.position)`; `fish.SetWandering(true)`; `fish.ClearManualOverride()`; `_emerging.Remove(fish)`

`Flatten` / `YawOf` は `AlaskaPollokMouthEntrance` から static 2 メソッドをコピー。
スケトウダラは `maxFish` に**加算しない**（別 `maxPollock` 上限）。

---

## 3. `Assets/Scripts/Fish/AmbientFish.cs`（新規）

`public class AmbientFish`（**プレーンクラス**）

状態: `Transform _tf`, `Animator _animator`, `AmbientSpecies _cfg`, `_heading/_targetHeading/_headingVel`, `_pitch/_targetPitch/_pitchVel`, `_roll/_rollVel`, `_speed`, `_wanderSeed`。

- `Init(Transform tf, Animator animator, AmbientSpecies cfg, int speciesIndex, float phaseSeed)`
- `ResetState(float phaseSeed)` — `_wanderSeed = phaseSeed*1.618f + Random.value*10f`、平滑化速度ゼロ
- `Tick(float dt, Vector3 anchorPos, FishSystem sys, int selfIndex)` — **O(1)（近傍は空間ハッシュ経由）**:
  1. Perlin 徘徊: `_targetHeading += (PerlinNoise(seed + t*noiseSpeed) * 2 - 1) * maxYawRate * wanderYawAmplitude * dt`
  2. 遊泳帯（水平のみソフト操舵）: `d = |flat(anchorPos - pos)|`。`d > bandOuter` → 内向き `LerpAngle`。`d < bandInner` → 外向き(+180°)`LerpAngle`
  3. 群れ（`schooling` の種のみ）: `sys.SchoolingForce(selfIndex)` = 同種の結合+整列+分離を水平ベクトルで返す → yaw に変換して `_targetHeading` を `LerpAngle`
  4. 深度キープ: `targetY = anchorPos.y + depthOffset`; `_targetPitch = Lerp(0, clamp(-yErr*10, ±maxPitchAngle), depthPull)`
  5. `SmoothDampAngle`（`turnSmoothTime` が旋回の慣性）で `_heading` / `_pitch` をレート制限追従
  6. 見た目バンク: `targetRoll = clamp(-yawRate/maxYawRate,±1)*maxBankAngle`; `SmoothDampAngle`
  7. 横うねり（見た目のみ・進路には積分しない）: `sway = swayAmplitude * sin(2π t/swayPeriod + seed)`
  8. 適用: `travel = Euler(_pitch,_heading,0)*fwd`; `look = Euler(_pitch,_heading+sway,0)*fwd`; `rotation = LookRotation(look) * Euler(0, modelYawOffset, _roll)`; `pos += travel * _speed * dt`。`_pos`/`_fwd` をスナップショット
  9. `_animator.speed = clamp(_speed/cruiseSpeed, animSpeedMin, animSpeedMax)`（float 1 回）

### 種別の癖（「その魚らしい」泳ぎ）

`AmbientSpecies` の値で表現。3 種の目安:

| | 鯛 seabream | ツナ tuna | マグロ bluefin |
|---|---|---|---|
| `cruiseSpeed` | 0.8 | 1.4 | 1.9 |
| `bandInner / bandOuter` | 4 / 12 | 8 / 20 | 11 / 24 |
| `depthOffset` | 0 | 1 | 1.5 |
| `maxYawRate` / `turnSmoothTime` | 40 / 0.35（小回り） | 30 / 0.6 | 18 / 1.4（大回り・慣性大） |
| `swayAmplitude` / `swayPeriod` | 7 / 2.6（S 字強め） | 4 / 3 | 1.5 / 4（ほぼ直進） |
| `schooling` ほか | true, `schoolRadius 4`, `cohesion 0.3`, `alignment 0.45`, `separation 1.0` | false | false |

クリップ側の体型差（carangiform / thunniform）は `FishBuildTool.Species` の `ampKeys` / `phaseLagTotal` / `bendAngleDeg` に反映済み。

### 群れ（`schooling` 種のみ・O(N)）

`FishSystem` が均一空間ハッシュ（`Dictionary<long,List<int>>`、セル = `max(3, schoolRadius)`）を、群れる種を含むときだけ毎フレーム O(N) で張り直す。`SchoolingForce()` は自セル + 水平 8 近傍だけ走査（平均 O(1)）、同種のみ対象。結合/整列/分離を合成した水平ベクトルを返す。素朴な全ペアループは使わない。群れない種（ツナ/マグロ）はグリッドに載らずコストゼロ。

---

## 4. `Assets/Scripts/QuizManager.cs` の変更

対象プレハブ: `Assets/Resources/prefabs/GameFlow/QuizManager.prefab`（guid `0ad01d6532e31d54294035fe813f5704`、MonoBehaviour fileID `355216033179769612`）。

**a. フィールド追加**（`[Header("Flow")]` の後 ~L26）:
```
    [Header("Fish")]
    [SerializeField]
    private FishSystem fishSystem;
```
**b. フォールバック**（`Start()` の `UpdateScoreDisplay();` 後 ~L44）:
```
        if (fishSystem == null)
        {
            fishSystem = FindAnyObjectByType<FishSystem>();
        }
```
**c. 呼び出し**（`HandleCorrectAnswer()` の TODO L123-124 を置換）:
```
        if (fishSystem != null)
        {
            fishSystem.EmitPollockFromMouth();
        }
        else
        {
            Debug.LogWarning(
                "[Quiz] FishSystem が設定されていません"
            );
        }
```
null ガードは既存 `FinishQuiz()` の `gameFlow` ガードと同型。`HandleIncorrectAnswer()` の「海が汚れる」TODO は範囲外。

---

## 5. ディレクトリ整理

```
Assets/Scripts/Fish/
  FishSystem.cs                     (新規)
  AmbientFish.cs                    (新規)
  AlaskaPollokController.cs         (git mv で移設。無改修。プレハブ guid 参照は不変)
  Editor/
    FishBuildTool.cs               (新規・エディタ専用)
Assets/Animations/fish/            (新規フォルダ。FishBuildTool が生成)
  seabream_swim.anim / .controller
  tuna_swim.anim     / .controller
  bluefin_swim.anim  / .controller
Assets/Resources/prefabs/fish/     (新規フォルダ。FishBuildTool が生成)
  Seabream.prefab / Tuna.prefab / Bluefin.prefab
```
`AlaskaPollok.prefab` は現状維持（任意で `prefabs/fish/` へ移設可、guid 不変）。

---

## 6. ファイル一覧

**新規**: `Assets/Scripts/Fish/FishSystem.cs`, `Assets/Scripts/Fish/AmbientFish.cs`, `Assets/Scripts/Fish/Editor/FishBuildTool.cs`（+ ツールが生成する `.anim` / `.controller` / `Seabream|Tuna|Bluefin.prefab`）
（`.meta` は Unity が自動生成。asmdef なしで `Assembly-CSharp`、名前空間なし。）

**変更**: `Assets/Scripts/QuizManager.cs`（§4）

**移設（無改修）**: `AlaskaPollokController.cs` → `Assets/Scripts/Fish/`

**無改修で維持**: `GameManager.cs`, `FogColorChange.cs`, `AlaskaPollok.prefab`

**使わない → follow-up コミットで削除**（旧魚系統一式。基本方針は「新規に作り直し、旧は不使用」）:
`FishSwimAI.cs`, `AquariumSceneSetup.cs`, `FishProgressionDirector.cs`, `AlaskaPollokMouthEntrance.cs`,
`SeabreamSwim.cs`, `TunaSwim.cs`, `BluefinSwim.cs`, `FishSpineAnimator.cs`, `FishSwimController.cs`, `fishRotator.cs`,
旧プレハブ `seabream_rigged_xplus.prefab` / `tuna_rigged_xplus.prefab` / `bluefin_rigged_xplus.prefab` / `fishWithAnimation.prefab` / `AquariumManager.prefab`
→ 旧 `WaterScene.unity`（対象外シーン）がこれらを参照するため、新シーン検証後に**別コミット**でまとめて削除。GLB 元ファイル（`Assets/models/fishes/*.glb`）は新プレハブの土台なので残す。

---

## 7. シーン設定仕様（ユーザーが複製シーンにエディタで適用）

基準点（現行 `WaterScene_SteamRuntime.unity`）:
- XR rig: `XR Origin (XR Rig)`（source guid `f6336ac4ac8b4d34bc5072418cdc62a0`）→ `Camera Offset` → `Main Camera`（tag `MainCamera`, GameObject source fileID `1767192433`）。常時アクティブ。
- `QuizManager` PrefabInstance（source guid `0ad01d6532e31d54294035fe813f5704`）。
- 除去対象: `FishAnchor` / `2ndFishAnchor`（`AquariumSceneSetup` 付き）、`AquariumManager` PrefabInstance、旧 `fishWithAnimation` インスタンス。

### 7.1 新規 GameObject `MouthAnchor`
親 = `Main Camera`。`Transform` のみ。ローカル position `(0, -0.07, 0)`、rotation `(0,0,0)`、scale `(1,1,1)`。

### 7.2 新規 GameObject `FishSystem`
親 = シーンルート。scale `(1,1,1)`。`FishSystem` component を追加し設定:
- `headAnchor` → `MouthAnchor`
- `species` size 3:

  | idx | label | prefab | 主な調整値 |
  |---|---|---|---|
  | 0 | seabream | `Assets/Resources/prefabs/fish/Seabream.prefab` | `weight 3`, `cruiseSpeed 0.8`, `bandInner 4`, `bandOuter 12`, `depthOffset 0`, `maxYawRate 40`, `turnSmoothTime 0.35`, `swayAmplitude 7`, `swayPeriod 2.6`, `schooling ✔` (`schoolRadius 4`, `cohesion 0.3`, `alignment 0.45`, `separation 1.0`, `separationDist 1.3`) |
  | 1 | tuna | `Assets/Resources/prefabs/fish/Tuna.prefab` | `weight 1`, `cruiseSpeed 1.4`, `bandInner 8`, `bandOuter 20`, `depthOffset 1`, `maxYawRate 30`, `turnSmoothTime 0.6`, `swayAmplitude 4`, `swayPeriod 3`, `schooling ✘` |
  | 2 | bluefin | `Assets/Resources/prefabs/fish/Bluefin.prefab` | `weight 1`, `cruiseSpeed 1.9`, `bandInner 11`, `bandOuter 24`, `depthOffset 1.5`, `maxYawRate 18`, `turnSmoothTime 1.4`, `swayAmplitude 1.5`, `swayPeriod 4`, `schooling ✘` |

  3種とも `modelYawOffset 90`（再生時に符号確認）。
- `maxFish 40`, `initialFishCount 18`, `spawnRingMin/Max 6/14`, `spawnDepthMin/Max -3/2`
- `pollockPrefab` → `Assets/Resources/prefabs/AlaskaPollok.prefab`
- `maxPollock 12`、emerge 系は既定、`pollockWanderRadius 6`

### 7.3 QuizManager 配線
`QuizManager` PrefabInstance にオーバーライドを追加: `fishSystem` → `FishSystem` の component。

### 7.4 旧配線の除去
Hierarchy 上で（YAML ではなく）`FishAnchor` / `2ndFishAnchor` / `AquariumManager` / 旧 `fishWithAnimation` を削除。

### 7.5 その他
`GameManager` / `FogColorChange` / `Global Volume` / ライティング / 地形 — 変更なし。

---

## 8. 実装順序

1. `features/FishSystem` ブランチ作成。
2. `Assets/Scripts/Fish/` 作成、`AlaskaPollokController.cs` を `git mv`。
3. `FishBuildTool.cs` を書く → Unity で `Tools/Fish/Rebuild Fish Assets` 実行 → クリップ / コントローラ / 新プレハブ生成。
4. `AmbientFish.cs`、`FishSystem.cs` を書く。
5. `QuizManager.cs` を編集。
6. Unity コンパイル確認。
7. ユーザー: シーン複製 + §7 適用。
8. 検証（§9）。
9. 問題なければ follow-up コミットで旧ファイル群を削除。

---

## 9. 検証手順

### コンパイル / アセット
1. Unity 再インポート → Console エラーなし。`QuizManager` に `Fish` ヘッダ + `fishSystem` スロット。
2. `Tools/Fish/Rebuild Fish Assets` 実行 → `Assets/Animations/fish/` と `Assets/Resources/prefabs/fish/` に想定アセットが生成。`Seabream.prefab` を Scene に置いて再生 → Animator でループ遊泳、ボーンが動く、`*Swim` は付いていない。
3. 複製シーンで §7 適用 → `FishSystem` gizmo がスポーンリング + 帯シェルを `MouthAnchor` 周りに描画。

### Play — スケトウダラ口出し
4. Play（XR sim or HMD）。~18 匹がシェル内でスポーン・遊泳。停止個体なし、体内貫通なし。
5. 正解を発火（クイズ経由、または `FishSystem` の一時 `#if UNITY_EDITOR [ContextMenu("Test: 口からスケトウダラ")]`）。
6. スケトウダラがカメラ/口元に横倒しで出現 → 前方へ泳ぎ出て近接面を抜け → ~0.7m で直立 → 頭部付近を徘徊。回答間に頭を回して再テスト → 出現点が頭部姿勢に追従。
7. `maxPollock`+2 回発火 → 12 で頭打ち、最古（非 emerging）がリサイクル、emit 時 GC スパイクなし。

### パフォーマンス — O(N)（issue #27）
8. Profiler: `initialFishCount` 18 → `FishSystem.Update` self ms を記録。魚パスに `Update` は `FishSystem` の 1 つだけ、per-fish `LateUpdate` は**なし**（旧 `*Swim` は不在）。
9. `maxFish` / `initialFishCount` を 80 に上げて再生 → `FishSystem.Update` はほぼ線形（約 4 倍、約 16 倍ではない）。`AmbientFish.Tick` の self time が魚数に依らず一定。
10. 魚が全て画面外を向くようカメラを回す → Animator / SkinnedMesh のスキニングコストが Profiler でほぼ消える（`CullCompletely` + `updateWhenOffscreen=false`）。

### maxFish リサイクル
11. `maxFish` 12 / `initialFishCount` 12 + `[ContextMenu]` `SpawnAmbient(1)` を連打 → 生存環境魚は 12 のまま、最古が新リング位置へジャンプ、Profiler に `Instantiate`/`Destroy` なし、`FishSystem` 配下の子数一定。

### 回帰
12. `GameManager` の fog 初期化維持、不正解で問題が進む、`FinishQuiz` → `gameFlow.ShowResult()` 維持。

---

## 10. リスクと対策

| # | リスク | 対策 |
|---|---|---|
| 1 | **前進軸の規約**: `_xplus` は前進軸ローカル -X。`modelYawOffset` を誤ると環境魚が逆走/鏡像。 | 種別シリアライズフィールド（既定 +90）。検証 9-6 で目視、逆なら -90。スケトウダラは `AlaskaPollokController` の +Z 前提（プレハブで正しい）で独立。 |
| 2 | **ベイクしたクリップの曲げ軸/振幅が実機で不自然**。 | `FishBuildTool` のパラメータ（`bendAngle`/`phaseLag`/`ampCurve`/曲げ軸）を種別に露出。1 クリックで再ベイクできるので反復調整が速い。GLB のボーンローカル軸差は、既存 `*Swim` と同じ「体の上方向をボーンローカルへ変換して回転軸にする」方式をツールでも使う。 |
| 3 | **`Optimize Game Objects` と generic リグの相性**（ボーン Transform が消えクリップのバインドが外れる）。 | 本作業では **Optimize Game Objects を有効化しない**。`CullCompletely` + `updateWhenOffscreen=false` だけで十分。 |
| 4 | **スケトウダラ プレハブ scale 0.07 + 焼き込み Y≈-90°**。`SnapTo` が `transform.rotation` を直接設定。 | `FishSystem` transform を scale `(1,1,1)` に維持。`Instantiate` は焼き込みスケール/回転を保持。 |
| 5 | **emerge 中のスケトウダラがリサイクルされコルーチンが死んだ参照を保持**。 | (a) リサイクル選択は `_emerging` を除外。(b) `EmergeRoutine` は controller ごとの世代 int を持ち各 `yield` 後に `IsStale` でバイル。(c) コルーチンは永続 `FishSystem` 上。 |
| 6 | **`AmbientFish` はプレーンクラス → Inspector 非表示**。 | `FishSystem.drawGizmos` が各 `_ambient[i].Tf` のヘディング ray + 帯シェルを描画。`#if UNITY_EDITOR` で `AmbientCount`/`PollockCount` 表示。O(N) 保証との引き換え。 |
| 7 | **旧 `WaterScene.unity` が削除対象スクリプト/プレハブを参照**（follow-up 削除時に missing script）。 | 対象外シーン。build list にも入っていない（`WaterScene_SteamRuntime` のみ）。削除は新シーン検証後の別コミットで、影響を明記した上で実施。 |
| 8 | **`Animator.speed` を毎フレームセット**するコスト。 | float 代入 1 回/匹。無視できる。変化が小さい時はスキップする最適化も可能だが不要。 |
| 9 | **`bandInner` がユーザー実立ち位置より小さい** → 毎フレーム +180° 操舵でジッタ。 | `bandPull ≤ 0.6` + `LerpAngle`（スナップしない）、`bandInner` 既定 ≥ 4m。深度キープは別処理。 |
| 10 | **`FishBuildTool` の GLB ボーン名検索が失敗**（インポート設定 `nodeNameMethod` 依存）。 | ツールで検出ログを出す。既存 `*Swim` の `AutoFindSpine`（`bone_0..N` の深さ優先検索）と同一ロジックを使う＝現行プレハブで動いている実績あり。 |
