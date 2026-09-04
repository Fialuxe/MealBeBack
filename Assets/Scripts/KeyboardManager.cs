using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 体験中のキーボード入力を 1 箇所に集約するサブシステム（#32）。
///
/// ・ExperienceFlowController / QuizManager が個別にキーを読むのをやめ、
///   入力の解釈をここへ寄せる。各コンポーネントへは「意味のある操作」だけを呼ぶ。
/// ・将来、キー入力を Arduino 側へ一本化する（#32）際は、この 1 クラスを
///   シリアル受信ドリブンの入力元に差し替えるだけで済むようにする。
///   → 具体的な QuizManager / Flow への呼び出しは <see cref="Dispatch*"/> にまとめてある。
///
/// 使い方:
///   ・シーンに 1 つ置き、flow と quiz を割り当てる。
///   ・ExperienceFlowController の "Handle Keyboard Input" を OFF にする
///     （二重入力防止）。
/// </summary>
public class KeyboardManager : MonoBehaviour
{
    [Header("参照")]
    [SerializeField]
    private ExperienceFlowController flow;

    [SerializeField]
    private QuizManager quiz;

    [Header("有効/無効")]
    [Tooltip("false にするとキーボード入力を一切受け付けない（Arduino へ一本化した場合など）。")]
    [SerializeField]
    private bool inputEnabled = true;

    [Header("キー割り当て : フロー")]
    [SerializeField] private Key startQuizKey = Key.Enter;
    [SerializeField] private Key numpadStartQuizKey = Key.NumpadEnter;

    [Header("キー割り当て : 食材選択")]
    [SerializeField] private Key answerLeftKey = Key.A;
    [SerializeField] private Key answerRightKey = Key.D;

    [Header("キー割り当て : デバイス選択 (#45 ① の暫定対応)")]
    [Tooltip("本来はトラッカー位置で決めるデバイス選択を、当面キーで代用する。")]
    [SerializeField] private Key selectDeviceAKey = Key.G;
    [SerializeField] private Key selectDeviceBKey = Key.H;

    [Header("キー割り当て : 咀嚼進行")]
    [Tooltip("手に持つ→口元→噛む→開く を 1 段階進める。")]
    [SerializeField] private Key advanceKey = Key.Space;
    [Tooltip("デバイスが完全にしぼんだ信号（ハードが無いときの代用）。")]
    [SerializeField] private Key deflatedKey = Key.F;

    [Header("キー割り当て : デバッグ")]
    [SerializeField] private Key prevQuestionKey = Key.LeftArrow;
    [SerializeField] private Key nextQuestionKey = Key.RightArrow;

    /// <summary>外部（インスペクタ/他システム）から入力の有効・無効を切り替える。</summary>
    public bool InputEnabled
    {
        get => inputEnabled;
        set => inputEnabled = value;
    }

    private void Reset()
    {
        flow = FindAnyObjectByType<ExperienceFlowController>();
        quiz = FindAnyObjectByType<QuizManager>();
    }

    private void Start()
    {
        if (flow == null)
            flow = FindAnyObjectByType<ExperienceFlowController>();

        if (quiz == null)
            quiz = FindAnyObjectByType<QuizManager>();
    }

    private void Update()
    {
        if (!inputEnabled)
            return;

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return;

        // ── フロー状態ごとの入力 ──────────────────────────────
        if (flow != null)
        {
            if (flow.IsBusy)
                return;

            if (flow.IsReady)
            {
                if (Pressed(kb, startQuizKey) || Pressed(kb, numpadStartQuizKey))
                    flow.RequestStartQuiz();
                return;
            }

            if (flow.IsResultAnnouncement)
            {
                if (Pressed(kb, startQuizKey) ||
                    Pressed(kb, numpadStartQuizKey))
                {
                    flow.RequestShowFinalResult();
                }

                return;
            }

            if (flow.IsResult)
            {
                if (Pressed(kb, startQuizKey) || Pressed(kb, numpadStartQuizKey))
                    flow.RequestReturnToSetup();
                return;
            }

            if (!flow.IsQuiz)
                return;
        }

        // ── クイズ中の入力 ──────────────────────────────────
        if (quiz == null)
            return;

        HandleQuizKeys(kb);
    }

    private void HandleQuizKeys(Keyboard kb)
    {
        // デバイス選択（#45 ①: 暫定でキー対応）
        if (Pressed(kb, selectDeviceAKey))
        {
            quiz.NotifyDeviceSelected(SerialSystem.SerialDevice.A);
            return;
        }

        if (Pressed(kb, selectDeviceBKey))
        {
            quiz.NotifyDeviceSelected(SerialSystem.SerialDevice.B);
            return;
        }

        // 食材選択
        if (Pressed(kb, answerLeftKey))
        {
            quiz.AnswerLeft();
            return;
        }

        if (Pressed(kb, answerRightKey))
        {
            quiz.AnswerRight();
            return;
        }

        // 咀嚼進行 / フィードバック送り
        if (Pressed(kb, advanceKey)
            || Pressed(kb, startQuizKey)
            || Pressed(kb, numpadStartQuizKey))
        {
            quiz.AdvanceInstruction();
            quiz.ContinueAfterFeedback();
            return;
        }

        // デバイスが完全にしぼんだ信号（代用）
        if (Pressed(kb, deflatedKey))
        {
            quiz.NotifyDeviceFullyDeflated();
            return;
        }

        // デバッグ: 前後の問題
        if (Pressed(kb, prevQuestionKey))
        {
            quiz.DebugPreviousQuestion();
            return;
        }

        if (Pressed(kb, nextQuestionKey))
        {
            quiz.DebugNextQuestion();
        }
    }

    private static bool Pressed(Keyboard kb, Key key)
    {
        if (key == Key.None)
            return false;

        return kb[key].wasPressedThisFrame;
    }
}
