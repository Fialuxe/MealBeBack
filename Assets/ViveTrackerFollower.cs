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
/// </summary>
public class ViveTrackerFollower : MonoBehaviour
{
    [Header("この選択肢の役割")]
    [SerializeField] private ChoiceSide side = ChoiceSide.Left;

    [Tooltip("Left/Right → トラッカーロールの対応表 (共有アセット)")]
    [SerializeField] private TrackerChoiceMap choiceMap;

    [Tooltip("設定すると choiceMap を無視してこのロールを直接使う")]
    [SerializeField] private TrackerRole roleOverride = TrackerRole.None;

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
            transform.SetPositionAndRotation(pos, rot);
        }
        else if (!holdLastPoseOnLoss)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
    }
}
