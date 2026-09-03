using UnityEngine;
using MealBeBack.Tracking;

/// <summary>
/// 選択肢オブジェクトを Vive トラッカーに追従させる。
///
/// このコンポーネントは「自分が左右どちらの選択肢か (side)」しか持たない。
/// 実際に追従するトラッカーのロールは共有アセット <see cref="TrackerChoiceMap"/> から引く:
///
///   ・side            … Left / Right。選択肢テンプレート prefab で 1 度だけ設定する。
///   ・choiceMap       … ScriptableObject 参照。全 prefab インスタンスが自動で同じ物を共有する。
///                        トラッカー割り当てが変わったら、このアセット 1 個を直すだけ。
///   ・roleOverride    … 例外的に 1 インスタンスだけ別ロールにしたい時のみ使う。
///
/// pose の取得と OpenXR の locate は <see cref="TrackerRig"/> が一括で行う。
///
/// デバイスオフセット (#85):
///   トラッカーの原点は実際に持つデバイスの中心とは一致しない (ベルト/マウント分ずれる)。
///   positionOffset / rotationOffset に「トラッカーローカル空間での ずれ」を入れると、
///   追従先をデバイス実体の位置・向きへ補正する。アイテムごとに異なるので各インスタンスで持つ。
/// </summary>
public class ViveTrackerFollower : MonoBehaviour
{
    [Header("この選択肢の役割")]
    [SerializeField] private ChoiceSide side = ChoiceSide.Left;

    [Tooltip("Left/Right → トラッカーロールの対応表 (共有アセット)")]
    [SerializeField] private TrackerChoiceMap choiceMap;

    [Tooltip("設定すると choiceMap を無視してこのロールを直接使う")]
    [SerializeField] private TrackerRole roleOverride = TrackerRole.None;

    [Header("デバイスオフセット (#85)")]
    [Tooltip("トラッカー原点 → デバイス実体への位置ずれ (トラッカーローカル空間, m)")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Tooltip("トラッカー姿勢に加える回転 (トラッカーローカル空間, オイラー deg)")]
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    [Header("挙動")]
    [Tooltip("トラッキングロスト中は最後の姿勢を保持する (false: 原点へ戻す)")]
    [SerializeField] private bool holdLastPoseOnLoss = true;

    private TrackerRig _rig;

    /// <summary>今このフォロワーが参照しているトラッカーロール。</summary>
    public TrackerRole ActiveRole =>
        roleOverride != TrackerRole.None ? roleOverride
        : choiceMap != null ? choiceMap.Resolve(side)
        : TrackerRole.None;

    /// <summary>対象トラッカーが今 Track 出来ているか。</summary>
    public bool IsTracking => _rig != null && _rig.IsTracked(ActiveRole);

    public ChoiceSide Side
    {
        get => side;
        set => side = value;
    }

    /// <summary>トラッカーローカル空間でのデバイス位置オフセット [m]。</summary>
    public Vector3 PositionOffset
    {
        get => positionOffset;
        set => positionOffset = value;
    }

    /// <summary>トラッカー姿勢に加える回転 (オイラー deg)。</summary>
    public Vector3 RotationOffset
    {
        get => rotationOffset;
        set => rotationOffset = value;
    }

    /// <summary>
    /// 素のトラッカー pose にデバイスオフセットを合成してワールド pose を返す。
    /// Track 出来ていなければ false。QuizTrackerInput 等が「デバイス実体の位置」を
    /// 距離判定に使いたい場合の窓口。
    /// </summary>
    public bool TryGetDevicePose(out Vector3 worldPos, out Quaternion worldRot)
    {
        worldPos = default;
        worldRot = Quaternion.identity;
        if (_rig == null || !_rig.TryGetPose(ActiveRole, out Vector3 pos, out Quaternion rot))
            return false;
        ApplyOffset(ref pos, ref rot);
        worldPos = pos;
        worldRot = rot;
        return true;
    }

    private void OnEnable()
    {
        _rig = TrackerRig.EnsureExists();

        if (choiceMap == null && roleOverride == TrackerRole.None)
        {
            Debug.LogWarning(
                $"[ViveTrackerFollower] {name}: choiceMap も roleOverride も未設定。追従しません。",
                this);
        }
    }

    private void LateUpdate()
    {
        if (_rig == null) return;

        if (_rig.TryGetPose(ActiveRole, out Vector3 pos, out Quaternion rot))
        {
            ApplyOffset(ref pos, ref rot);
            transform.SetPositionAndRotation(pos, rot);
        }
        else if (!holdLastPoseOnLoss)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
    }

    /// <summary>
    /// トラッカーの素の pose に positionOffset / rotationOffset を合成する。
    /// オフセットはトラッカーローカル空間で解釈するので、トラッカーが回れば一緒に回る
    /// (デバイスがトラッカーに剛体固定されている前提)。
    /// </summary>
    private void ApplyOffset(ref Vector3 pos, ref Quaternion rot)
    {
        if (positionOffset != Vector3.zero)
            pos += rot * positionOffset;
        if (rotationOffset != Vector3.zero)
            rot *= Quaternion.Euler(rotationOffset);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || _rig == null) return;
        if (!_rig.TryGetPose(ActiveRole, out Vector3 raw, out Quaternion rawRot)) return;

        Vector3 dev = raw + rawRot * positionOffset;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(raw, dev);
        Gizmos.DrawWireSphere(raw, 0.02f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(dev, 0.02f);
    }
#endif
}
