using UnityEngine;

/// <summary>
/// 魚の泳ぎアニメーションコントローラー
/// GLBから読み込んだ bone_0〜bone_4 の5本ボーン構成に対応
/// bone_0 = 胴体ルート、bone_4 = 尾
/// </summary>
public class FishSwimController : MonoBehaviour
{
    [Header("ボーン設定")]
    [Tooltip("Hierarchyから bone_0〜bone_4 をアサイン")]
    public Transform bone0; // 胴体ルート
    public Transform bone1;
    public Transform bone2;
    public Transform bone3;
    public Transform bone4; // 尾

    [Header("泳ぎパラメータ")]
    [Tooltip("尾ひれの振れ幅（度）")]
    public float tailSwingAngle = 25f;

    [Tooltip("泳ぎのサイクル速度")]
    public float swimFrequency = 2.5f;

    [Tooltip("ボーンごとの位相差。大きいほど「くねり」が強調される")]
    public float phaseShift = 0.35f;

    [Tooltip("胴体の揺れ幅（尾の揺れに対する倍率。0=揺れなし）")]
    [Range(0f, 1f)]
    public float bodySwingRatio = 0.15f;

    [Header("移動設定")]
    [Tooltip("魚が前進する速度（0=その場で泳ぐ）")]
    public float forwardSpeed = 0f;

    [Tooltip("魚の前進方向（ローカル座標）")]
    public Vector3 forwardDirection = Vector3.forward;

    // 各ボーンの初期回転（Awakeで保存）
    private Quaternion[] _initialRotations = new Quaternion[5];
    private Transform[] _bones;

    private void Awake()
    {
        _bones = new Transform[] { bone0, bone1, bone2, bone3, bone4 };

        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] != null)
                _initialRotations[i] = _bones[i].localRotation;
        }
    }

    private void Update()
    {
        ApplySwimAnimation();

        if (forwardSpeed > 0f)
            transform.Translate(forwardDirection * forwardSpeed * Time.deltaTime, Space.Self);
    }

    private void ApplySwimAnimation()
    {
        float time = Time.time * swimFrequency * Mathf.PI * 2f;

        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] == null) continue;

            // bone_0 に近いほど揺れが小さく、bone_4（尾）に近いほど大きい
            float influence = (float)i / (_bones.Length - 1); // 0.0〜1.0
            float angle = Mathf.Sin(time - phaseShift * i)
                          * tailSwingAngle
                          * Mathf.Lerp(bodySwingRatio, 1f, influence);

            // Y軸回転でくねり（魚の左右スイング）
            _bones[i].localRotation = _initialRotations[i] * Quaternion.Euler(0f, angle, 0f);
        }
    }

    /// <summary>
    /// 外部からパラメータを一括設定する用
    /// </summary>
    public void SetSwimParameters(float frequency, float angle, float phase)
    {
        swimFrequency = frequency;
        tailSwingAngle = angle;
        phaseShift = phase;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // エディタ上でパラメータを変えたとき初期回転を再キャプチャ
        if (!Application.isPlaying && bone0 != null)
            Awake();
    }
#endif
}
