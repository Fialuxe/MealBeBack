using UnityEngine;

/// <summary>
/// 魚の遊泳（移動と旋回）を制御するスクリプト。
///
/// ■ 前方軸の設定方法
///   Inspector の「モデルローカル前方軸」を頭の向きに合わせてください。
///   分からない場合は右クリック → "骨格から前方軸を自動検出" を一度だけ実行すると
///   bone_0(頭)〜bone_5(尾) から算出して値を書き込みます（ランタイムでは何もしません）。
///   逆走する場合は「前方軸を反転」にチェック。
///
/// ■ Inspector の値はランタイムで書き換えません。
/// </summary>
[DisallowMultipleComponent]
public class FishSwimController : MonoBehaviour
{
    public enum Mode { Wander, SeekTarget }

    [Header("移動")]
    [Tooltip("巡航速度 (units/sec)")]
    public float cruiseSpeed = 1.2f;
    [Tooltip("最高速度 (units/sec)")]
    public float maxSpeed = 2.5f;
    [Tooltip("加減速のなめらかさ")]
    public float acceleration = 2f;
    [Tooltip("最大旋回速度 (deg/sec)")]
    public float maxTurnDegPerSec = 120f;

    [Header("行動モード")]
    public Mode mode = Mode.Wander;
    [Tooltip("SeekTarget 時に追いかける対象。Null なら原点へ向かう")]
    public Transform target;

    [Header("ワンダー（うろつき）")]
    [Tooltip("方向転換の頻度・大きさ")]
    public float wanderStrength = 1.0f;
    [Tooltip("上下の動きの許容量（0 = 水平のみ）")]
    [Range(0f, 1f)] public float verticalFreedom = 0.35f;

    [Header("遊泳範囲（球）")]
    [Tooltip("遊泳範囲の中心（未指定なら開始位置）")]
    public Transform boundsCenter;
    [Tooltip("この半径に近づくと中心へ向き直す")]
    public float boundsRadius = 8f;

    [Header("バンク（旋回時の傾き）")]
    [Tooltip("旋回時に体を内側へ傾ける最大角度")]
    public float maxBankDeg = 25f;
    [Tooltip("バンクのなめらかさ")]
    public float bankSmoothing = 4f;

    [Header("前方軸（Inspector で設定 / ランタイム変更なし）")]
    [Tooltip("モデルのローカル空間での「頭の向き」。" +
             "右クリック→骨格から自動検出 で算出できます。")]
    public Vector3 modelForwardLocal = Vector3.forward;
    [Tooltip("逆走するときにチェックすると前方軸を反転します")]
    public bool invertForward = false;
    [Tooltip("自動検出で使うボーン名")]
    public string headBoneName = "bone_0";
    public string tailBoneName = "bone_5";

    // ---- FishSpineAnimator から参照される読み取り専用プロパティ ----
    public float CurrentSpeed  { get; private set; }
    public float Speed01       => maxSpeed > 0.0001f ? Mathf.Clamp01(CurrentSpeed / maxSpeed) : 0f;
    public float SignedTurnRate { get; private set; }
    public Vector3 FishUp  => transform.up;
    public Vector3 HeadDir => transform.TransformDirection(ForwardLocal);

    // 実際に使う前方ベクトル（invertForward 適用済み）
    Vector3 ForwardLocal => invertForward ? -modelForwardLocal.normalized
                                          :  modelForwardLocal.normalized;

    Vector3 velocity;
    float wanderYawDeg;
    float wanderPitchDeg;
    Vector3 prevHeadDir;
    float currentBank;
    Vector3 centerPos;

    void Start()
    {
        centerPos = boundsCenter ? boundsCenter.position : transform.position;

        Vector3 fwd = HeadDir;
        velocity       = fwd * cruiseSpeed;
        prevHeadDir    = fwd;
        CurrentSpeed   = cruiseSpeed;
        wanderYawDeg   = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        wanderPitchDeg = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 1) 目標方向
        Vector3 desiredDir = ComputeDesiredDirection(dt);

        // 2) 速さを目標へなめらかに近づける（急旋回時は少し減速）
        float targetSpeed = (mode == Mode.SeekTarget) ? maxSpeed * 0.9f : cruiseSpeed;
        targetSpeed *= Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(Mathf.Abs(SignedTurnRate) / maxTurnDegPerSec));
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, acceleration * dt);

        // 3) 向きを目標方向へ旋回（旋回速度上限あり）
        //    「transform.TransformDirection(ForwardLocal) == desiredDir」となる回転を LookRotation で作る
        Quaternion look = Quaternion.LookRotation(desiredDir, Vector3.up);
        Quaternion axisCorrection = Quaternion.FromToRotation(ForwardLocal, Vector3.forward);
        Quaternion targetRot = look * axisCorrection;
        Quaternion newRot    = Quaternion.RotateTowards(transform.rotation, targetRot, maxTurnDegPerSec * dt);

        // 4) 符号付き旋回速度を計測
        Vector3 headDir = newRot * ForwardLocal;
        SignedTurnRate  = Vector3.SignedAngle(prevHeadDir, headDir, transform.up) / dt;
        prevHeadDir     = headDir;

        // 5) バンク（旋回方向へ傾ける）
        float targetBank = maxTurnDegPerSec > 0.0001f
            ? Mathf.Clamp(-SignedTurnRate / maxTurnDegPerSec, -1f, 1f) * maxBankDeg
            : 0f;
        currentBank      = Mathf.Lerp(currentBank, targetBank, 1f - Mathf.Exp(-bankSmoothing * dt));
        newRot           = Quaternion.AngleAxis(currentBank, headDir) * newRot;

        transform.rotation  = newRot;
        transform.position += HeadDir * CurrentSpeed * dt;
    }

    Vector3 ComputeDesiredDirection(float dt)
    {
        Vector3 pos = transform.position;
        Vector3 desired;

        if (mode == Mode.SeekTarget)
        {
            Vector3 tp = target ? target.position : centerPos;
            desired = (tp - pos);
            if (desired.sqrMagnitude < 1e-6f) desired = HeadDir;
            desired.Normalize();
        }
        else
        {
            // ワンダー: 水平角・垂直角をフレームごとにランダムドリフト
            float maxDrift = wanderStrength * maxTurnDegPerSec * 0.6f * dt;
            wanderYawDeg   += Random.Range(-maxDrift, maxDrift);
            wanderPitchDeg += Random.Range(-maxDrift, maxDrift) * verticalFreedom;
            wanderPitchDeg  = Mathf.Clamp(wanderPitchDeg, -40f, 40f);

            float yaw   = wanderYawDeg   * Mathf.Deg2Rad;
            float pitch = wanderPitchDeg * Mathf.Deg2Rad;
            desired = new Vector3(
                Mathf.Sin(yaw) * Mathf.Cos(pitch),
                Mathf.Sin(pitch),
                Mathf.Cos(yaw) * Mathf.Cos(pitch));
        }

        // 遊泳範囲の境界に近づいたら中心へ向き直す
        Vector3 toCenter = centerPos - pos;
        float dist = toCenter.magnitude;
        if (dist > boundsRadius * 0.8f)
        {
            float w = Mathf.InverseLerp(boundsRadius * 0.8f, boundsRadius, dist);
            desired        = Vector3.Slerp(desired, toCenter.normalized, Mathf.Clamp01(w)).normalized;
            wanderYawDeg   = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
            wanderPitchDeg = Mathf.Asin(Mathf.Clamp(desired.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        if (desired.sqrMagnitude < 1e-6f) desired = HeadDir;
        return desired.normalized;
    }

    // ---- エディター補助（右クリックメニューからのみ実行） ----

    /// <summary>
    /// 骨格（headBoneName → tailBoneName）から前方軸を計算して
    /// modelForwardLocal に書き込む。実行はエディターの右クリックメニューから。
    /// </summary>
    [ContextMenu("骨格から前方軸を自動検出")]
    void AutoDetectForwardFromSpine()
    {
        Transform head = FindDeep(transform, headBoneName);
        Transform tail = FindDeep(transform, tailBoneName);
        if (head == null || tail == null)
        {
            Debug.LogWarning($"[FishSwimController] ボーン '{headBoneName}' または '{tailBoneName}' が見つかりません。", this);
            return;
        }
        Vector3 headL = transform.InverseTransformPoint(head.position);
        Vector3 tailL = transform.InverseTransformPoint(tail.position);
        Vector3 fwd   = headL - tailL;
        if (fwd.sqrMagnitude < 1e-10f)
        {
            Debug.LogWarning("[FishSwimController] 頭/尾ボーンが同位置です。", this);
            return;
        }
        modelForwardLocal = fwd.normalized;
        Debug.Log($"[FishSwimController] 前方軸を検出しました: {modelForwardLocal}  " +
                  $"逆走する場合は invertForward にチェックを。", this);
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

    void OnDrawGizmosSelected()
    {
        Vector3 c = boundsCenter ? boundsCenter.position
                  : Application.isPlaying ? centerPos : transform.position;
        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.35f);
        Gizmos.DrawWireSphere(c, boundsRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, transform.TransformDirection(ForwardLocal) * boundsRadius * 0.15f);
    }
}