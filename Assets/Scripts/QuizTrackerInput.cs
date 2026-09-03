using UnityEngine;
using MealBeBack.Tracking;

/// <summary>
/// クイズ中、左右の選択肢トラッカーと頭 (HMD) との距離を毎フレーム測り、
/// 近い方を <see cref="QuizManager.NotifyTrackerDistance"/> へ流す。
/// 旧 TrackerDistanceManager（デバイス名固定）の置き換え。
///
/// 「口元近くに来たら選択される」判定は QuizManager 側が持つ:
///   selectionPreviewDistance 以下 → 仮選択
///   selectionConfirmedDistance 以下 かつ 同じ側を継続 → 確定（口へ運んだ扱い）
/// このコンポーネントは「どちらのトラッカーが / 何 m か」を渡すだけ。
/// </summary>
public class QuizTrackerInput : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private QuizManager quizManager;
    [SerializeField] private TrackerChoiceMap choiceMap;

    [Tooltip("距離の基準点 (HMD)。未設定なら Camera.main を使う")]
    [SerializeField] private Transform head;

    [Header("デバッグ")]
    [SerializeField] private bool debugLog = false;

    private void Reset()
    {
        quizManager = GetComponentInParent<QuizManager>();
    }

    private void Awake()
    {
        if (quizManager == null)
        {
            quizManager = GetComponentInParent<QuizManager>();
            if (quizManager == null)
                quizManager = FindAnyObjectByType<QuizManager>();
        }
    }

    private void Update()
    {
        if (quizManager == null || !quizManager.IsQuizRunning) return;

        if (choiceMap == null)
        {
            Debug.LogWarning("[QuizTrackerInput] choiceMap 未設定", this);
            return;
        }

        var rig = TrackerRig.Instance;
        if (rig == null) return;

        Vector3 headPos =
            head != null ? head.position :
            Camera.main != null ? Camera.main.transform.position :
            transform.position;

        float dLeft  = rig.Distance(choiceMap.leftChoice, headPos);
        float dRight = rig.Distance(choiceMap.rightChoice, headPos);

        bool leftNearer = dLeft <= dRight;
        float best = leftNearer ? dLeft : dRight;

        SerialSystem.SerialDevice device =
            float.IsInfinity(best) ? SerialSystem.SerialDevice.None
            : leftNearer ? SerialSystem.SerialDevice.A     // A = 左の選択肢
            : SerialSystem.SerialDevice.B;                  // B = 右の選択肢

        if (debugLog)
        {
            Debug.Log(
                $"[QuizTrackerInput] L({choiceMap.leftChoice})={dLeft:F2}m " +
                $"R({choiceMap.rightChoice})={dRight:F2}m -> {device} {best:F2}m");
        }

        quizManager.NotifyTrackerDistance(device, best);
    }
}
