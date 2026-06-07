using UnityEngine;

/// <summary>
/// 背骨ボーンを sin 波で順番にヨーさせ、頭→尾へ伝わる「うねり」を作る procedural アニメーション。
///
/// 軸の考え方:
/// ・魚の遊泳は体を左右にくねらせる = 「魚の上方向(Y)まわりのヨー回転」。
/// ・ただし各ボーンのローカル軸は体軸と一致していない(例: bone_1 は X 軸まわりに -90°)。
///   そこで静止ポーズの時点で「魚の上方向を各ボーンのローカル空間で表した軸 axisLocal[i]」を
///   求めておき、その軸まわりに回す。これで体が縦に振れたりねじれたりせず、水平にうねる。
/// ・各ボーンの位相を尾に向かって少しずつ遅らせる(wavePhasePerSegmentDeg)ことで進行波になる。
///
/// FishSwimController と同じオブジェクトに付けると、速度・旋回量に応じて振りが自動で変化する。
/// 単体でも一定リズムで泳ぐ。LateUpdate で適用するので、移動制御の後に確実に上書きされる。
/// </summary>
[DisallowMultipleComponent]
public class FishSpineAnimator : MonoBehaviour
{
    [Header("背骨ボーン（頭→尾の順）")]
    [Tooltip("空欄なら下の boneNames を名前検索して自動取得")]
    public Transform[] spineBones;
    [Tooltip("自動取得に使うボーン名（頭→尾の順）")]
    public string[] boneNames = { "bone_1", "bone_2", "bone_3", "bone_4", "bone_5" };

    [Header("うねりの基本パラメータ")]
    [Tooltip("基準の振り幅(度)。1ボーンあたりの最大ヨー角")]
    public float baseAmplitudeDeg = 8f;
    [Tooltip("尾に向かって振り幅を増やす倍率(頭:1 → 尾:この値)")]
    public float tailAmplitudeMultiplier = 2.2f;
    [Tooltip("基準の尾びれ振り周波数(Hz)")]
    public float baseFrequencyHz = 1.2f;
    [Tooltip("1セグメントあたりの位相遅れ(度)。大きいほど波が細かくS字になる")]
    public float wavePhasePerSegmentDeg = 45f;

    [Header("速度との連動 (FishSwimController があるとき)")]
    [Tooltip("停止時でも残す最小の振り(0..1)")]
    [Range(0f, 1f)] public float idleAmplitude01 = 0.25f;
    [Tooltip("速いほど周波数が上がる量(Hz)")]
    public float speedFrequencyGain = 1.5f;

    [Header("旋回との連動")]
    [Tooltip("旋回時に体を曲げ込む強さ(度 / (deg/sec))")]
    public float turnBendGain = 0.15f;
    [Tooltip("体の曲げ込みの最大角(度/ボーン)")]
    public float maxTurnBendDeg = 12f;

    FishSwimController controller;
    Quaternion[] restRotations;   // 各ボーンの静止ローカル回転
    Vector3[] axisLocal;          // 各ボーンのローカル空間で表した「魚の上方向」
    float[] segT;                 // 頭0→尾1 の正規化位置
    float phase;
    float smoothedTurn;

    void Start()
    {
        controller = GetComponent<FishSwimController>();

        if (spineBones == null || spineBones.Length == 0)
            ResolveBonesByName();

        if (spineBones == null || spineBones.Length == 0)
        {
            Debug.LogError("[FishSpineAnimator] 背骨ボーンが取得できませんでした。", this);
            enabled = false;
            return;
        }

        int n = spineBones.Length;
        restRotations = new Quaternion[n];
        axisLocal = new Vector3[n];
        segT = new float[n];

        // うねりのヨー軸 = 魚の上方向(ワールド)
        Vector3 fishUpWorld = controller != null ? controller.FishUp : transform.up;

        for (int i = 0; i < n; i++)
        {
            Transform b = spineBones[i];
            restRotations[i] = b.localRotation;
            // 魚の上方向を、このボーンの静止ワールド回転のローカル空間へ変換した軸
            Vector3 a = Quaternion.Inverse(b.rotation) * fishUpWorld;
            axisLocal[i] = a.sqrMagnitude > 1e-8f ? a.normalized : Vector3.up;
            segT[i] = (n > 1) ? (float)i / (n - 1) : 1f;
        }
    }

    void ResolveBonesByName()
    {
        if (boneNames == null || boneNames.Length == 0) return;
        spineBones = new Transform[boneNames.Length];
        for (int i = 0; i < boneNames.Length; i++)
            spineBones[i] = FindDeep(transform, boneNames[i]);
    }

    void LateUpdate()
    {
        if (restRotations == null) return;

        float dt = Time.deltaTime;

        // 速度・旋回量を取得（コントローラが無ければ一定値）
        float speed01 = controller != null ? controller.Speed01 : 0.6f;
        float turnRate = controller != null ? controller.SignedTurnRate : 0f;
        smoothedTurn = Mathf.Lerp(smoothedTurn, turnRate, 1f - Mathf.Exp(-6f * dt));

        // 周波数・振り幅は速度で変化
        float freq = baseFrequencyHz + speedFrequencyGain * speed01;
        float ampScale = Mathf.Lerp(idleAmplitude01, 1f, speed01);
        phase += freq * 2f * Mathf.PI * dt;

        float phasePerSeg = wavePhasePerSegmentDeg * Mathf.Deg2Rad;

        // 旋回による曲げ込み（全体を一方向へ）
        float turnBend = Mathf.Clamp(-smoothedTurn * turnBendGain, -maxTurnBendDeg, maxTurnBendDeg);

        for (int i = 0; i < spineBones.Length; i++)
        {
            Transform b = spineBones[i];
            if (b == null) continue;

            // 尾に向かって振り幅増加
            float ampHere = baseAmplitudeDeg * Mathf.Lerp(1f, tailAmplitudeMultiplier, segT[i]) * ampScale;

            // 頭→尾へ位相を遅らせた sin 波（進行波）
            float wave = Mathf.Sin(phase - i * phasePerSeg);
            float angle = ampHere * wave + turnBend * segT[i];

            // 静止ポーズに、魚の上方向まわりの回転を加える
            b.localRotation = restRotations[i] * Quaternion.AngleAxis(angle, axisLocal[i]);
        }
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
}
