// ============================================================================
//  AlaskaPollokMouthEntrance.cs
//  「口の中から魚が出てくる」登場演出
//
//  流れ:
//    1) OVRCameraRig の視点(口元)に、横向きで瞬間配置する
//    2) 横向きのまま泳いで口から出る
//    3) ユーザーから一定距離だけ離れたら 90度 回頭する
//    4) 通常の泳ぎ(徘徊)へ移行する
//
//  ・魚の制御は AlaskaPollokController の公開API だけで行う(直接 Transform を触らない)。
//  ・VR のボタン/ジェスチャ等から Play() を呼んで開始できる。
//
//  【取り付け方】
//    1. 空の GameObject にこのコンポーネントを付ける。
//    2. headAnchor に OVRCameraRig/TrackingSpace/CenterEyeAnchor を割り当てる。
//    3. fish に AlaskaPollokController 付きの魚を割り当てる
//       (または fishPrefab を割り当てれば Play() 時に生成する)。
//    4. 再生 or Play() 呼び出しで登場。
// ============================================================================

using System.Collections;
using UnityEngine;

[AddComponentMenu("Fish/Alaska Pollok Mouth Entrance")]
public class AlaskaPollokMouthEntrance : MonoBehaviour
{
    // ---- 参照 ----------------------------------------------------------
    [Header("■ 参照")]
    [Tooltip("動かす魚のコントローラ。未設定で fishPrefab がある場合は Play() 時に生成する。")]
    public AlaskaPollokController fish;

    [Tooltip("魚を生成する場合のプレハブ(AlaskaPollokController 付き)。")]
    public AlaskaPollokController fishPrefab;

    [Tooltip("OVRCameraRig の CenterEyeAnchor(頭/視点)。未設定なら Camera.main を使う。")]
    public Transform headAnchor;

    // ---- 口の位置 ------------------------------------------------------
    [Header("■ 口の位置(視点からのオフセット)")]
    [Tooltip("CenterEyeAnchor ローカルでの出現位置。既定は『目線と同じ高さ・5cm後ろ』。")]
    public Vector3 mouthLocalOffset = new Vector3(0f, 0f, -0.05f);

    // ---- 登場演出 ------------------------------------------------------
    [Header("■ 登場演出")]
    [Tooltip("出現時の体のロール角 [deg]。90 で横倒し＝尾びれが水平に見える。左右逆なら -90。")]
    public float emergeRollAngle = 90f;

    [Tooltip("前方へ泳ぎ出す速度 [m/s]。鼻=進行方向なので尾も水平に振られる。")]
    public float emergeSpeed = 0.4f;

    [Tooltip("ユーザーからこの距離だけ離れる間に、横倒し→直立へロールし切る [m]。")]
    public float emergeDistance = 0.7f;

    [Tooltip("最低でもこの秒数は登場演出を続ける(近距離で即終了しないよう保険)。")]
    public float minEmergeTime = 1.2f;

    [Tooltip("直立復帰後、通常遊泳へ移るまでの整定待ち [s]。")]
    public float settleTime = 0.4f;

    [Tooltip("登場中は遊泳範囲(Bounds)による囲い込みを一時的に切る。")]
    public bool disableBoundsDuringEmerge = true;

    // ---- 登場後 --------------------------------------------------------
    [Header("■ 登場後")]
    [Tooltip("登場後に自律徘徊へ移行する。")]
    public bool wanderAfter = true;

    [Tooltip("登場後の遊泳範囲の中心をユーザー周辺へ再設定する。")]
    public bool recenterBoundsOnUser = true;

    // ---- 起動 ----------------------------------------------------------
    [Header("■ 起動")]
    [Tooltip("Start で自動的に演出を始める。false なら Play() を呼ぶまで待機。")]
    public bool playOnStart = true;

    [Tooltip("Play() まで魚を非表示にしておく。")]
    public bool hideUntilPlay = true;

    [Tooltip("回頭が終わり通常遊泳へ移った瞬間に呼ばれる。")]
    public UnityEngine.Events.UnityEvent onEmergeComplete;

    bool _playing;

    // =====================================================================
    void Start()
    {
        if (hideUntilPlay && fish != null) fish.gameObject.SetActive(false);
        if (playOnStart) Play();
    }

    /// <summary>登場シーケンスを開始する(VRのボタン/ジェスチャ等から呼べる)。</summary>
    public void Play()
    {
        if (_playing) return;

        // 頭(視点)の解決
        if (headAnchor == null)
        {
            if (Camera.main != null) headAnchor = Camera.main.transform;
            else { Debug.LogError("[MouthEntrance] headAnchor が未設定で Camera.main も見つかりません。", this); return; }
        }

        // 魚の解決(必要なら生成)
        if (fish == null)
        {
            if (fishPrefab == null) { Debug.LogError("[MouthEntrance] fish も fishPrefab も未設定です。", this); return; }
            fish = Instantiate(fishPrefab);
        }

        StartCoroutine(Sequence());
    }

    // =====================================================================
    IEnumerator Sequence()
    {
        _playing = true;

        // 登場中は囲い込みを切る(出現直後に中心へ引き戻されるのを防ぐ)
        bool boundsBackup = fish.useBounds;
        if (disableBoundsDuringEmerge) fish.useBounds = false;

        // --- 1) 目線高さ・5cm後ろに、鼻=前方・体は横倒しで瞬間配置 -----------
        // ★狙い: 鼻(進行方向)を前方(+Z)へ向ける → ユーザーには“しっぽ側”が見える。
        //   さらに体を 90度ロール(横倒し)して、尾びれを“水平”に見せる。
        //   鼻=進行方向なので、前進は通常の自走(MoveForward)でよい(ドリフト不要)。
        Vector3 mouthPos = headAnchor.TransformPoint(mouthLocalOffset);
        float   userYaw  = YawOf(Flatten(headAnchor.forward));    // 前方(=出ていく向き)

        if (!fish.gameObject.activeSelf) fish.gameObject.SetActive(true); // Awake→リグ初期化
        fish.SetWandering(false);
        fish.SnapTo(mouthPos, userYaw);                          // 鼻=前方で瞬間配置
        fish.SetRollImmediate(emergeRollAngle);                  // 横倒し(尾が水平)で瞬間配置

        // --- 2) 前方へ泳ぎ出しながら、横倒し→直立へ“回転しつつ”出てくる -------
        // 鼻=進行方向なので尾は水平面を左右に振られる＝しっぽが水平に泳ぐように見える。
        // 出ていく距離に応じて emergeRollAngle → 0 へロールを戻し、直立に復帰する。
        fish.MoveForward(emergeSpeed);

        float t = 0f;
        while (true)
        {
            t += Time.deltaTime;
            float dist = Vector3.Distance(fish.transform.position, headAnchor.position);
            float p = (emergeDistance > 0.0001f) ? Mathf.Clamp01(dist / emergeDistance) : 1f;
            // 出はじめ(顔の正面)は横倒しを保ち、離れるにつれ直立へ(なめらかな S 字補間)
            float roll = Mathf.Lerp(emergeRollAngle, 0f, Mathf.SmoothStep(0f, 1f, p));
            fish.SetRollImmediate(roll);

            if (dist >= emergeDistance && t >= minEmergeTime) break;
            yield return null;
        }

        // --- 3) 直立に整えて通常遊泳へ引き継ぐ ------------------------------
        fish.SetRoll(0f);                 // 以後のロールは自動バンクに委ねる
        yield return new WaitForSeconds(settleTime);

        fish.Cruise();
        fish.useBounds = boundsBackup || recenterBoundsOnUser;
        if (recenterBoundsOnUser) fish.SetBoundsCenter(headAnchor.position);

        if (wanderAfter) fish.SetWandering(true);
        fish.ClearManualOverride();       // 抑制を解除してすぐ通常挙動へ

        _playing = false;
        onEmergeComplete?.Invoke();
    }

    // =====================================================================
    // ユーティリティ
    static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
    }
    static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

    void OnDrawGizmosSelected()
    {
        if (headAnchor == null) return;
        Vector3 mouth = headAnchor.TransformPoint(mouthLocalOffset);
        Vector3 fwd   = Flatten(headAnchor.forward);

        // 出現位置(目線高さ・後ろ)
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(mouth, 0.03f);

        // 出ていく向き=前方(鼻もこの向き)
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(mouth, mouth + fwd * 0.5f);

        // 横倒しの軸(ロールは前方軸まわり)を補助表示
        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.7f);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Gizmos.DrawLine(mouth - right * 0.12f, mouth + right * 0.12f);

        // 直立に戻り切る距離
        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.6f);
        Gizmos.DrawWireSphere(headAnchor.position, emergeDistance);
    }
}