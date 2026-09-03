using UnityEngine;

namespace MealBeBack.Tracking
{
    public enum ChoiceSide
    {
        Left,
        Right,
    }

    /// <summary>
    /// 「左の選択肢 / 右の選択肢」を、どのトラッカーロールで追従・判定するかの対応表。
    ///
    /// プロジェクトに 1 つだけ作成し (Assets 右クリック → Create → MealBeBack → Tracker Choice Map)、
    /// トラッカーを扱う prefab / コンポーネントは全てこのアセットを「参照」で共有する。
    /// 本番でトラッカーの割り当てが変わったら、このアセットの 2 フィールドだけ直せばよい。
    /// </summary>
    [CreateAssetMenu(
        menuName = "MealBeBack/Tracker Choice Map",
        fileName = "TrackerChoiceMap")]
    public class TrackerChoiceMap : ScriptableObject
    {
        [Tooltip("左の選択肢を追従させるトラッカーのロール")]
        public TrackerRole leftChoice = TrackerRole.LeftElbow;

        [Tooltip("右の選択肢を追従させるトラッカーのロール")]
        public TrackerRole rightChoice = TrackerRole.RightElbow;

        public TrackerRole Resolve(ChoiceSide side) =>
            side == ChoiceSide.Left ? leftChoice : rightChoice;
    }
}
