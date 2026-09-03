using System.Collections;
using UnityEngine;

public class QuizManager : MonoBehaviour
{
    private enum QuestionPhase
    {
        WaitingForSelection, // 左右どちらの食材かを選ぶ
        WaitingForHold,      // 手に持つ
        WaitingForMouth,     // 口元へ運ぶ → くわえた瞬間に正誤判定
        Chewing,             // 咀嚼シーケンスをキー1回ごとに1ステップ進める
        ShowingFeedback      // 結果表示 → 次の問題へ
    }

    public enum AnswerSide
    {
        Left,
        Right
    }

    [System.Serializable]
    public class QuestionData
    {
        public GameObject questionObject;

        [Header("Correct Answer")]
        public AnswerSide correctAnswer;
    }

    [Header("Questions")]
    [SerializeField]
    private QuestionData[] questions;

    [Header("Flow")]
    [SerializeField]
    private ExperienceFlowController gameFlow;

    [Header("Fish")]
    [SerializeField]
    private FishSystem fishSystem;

    [Header("Fog")]
    [SerializeField]
    private FogSystem fogSystem;

    [Header("Serial (デバイス制御)")]
    [SerializeField]
    private SerialSystem serialSystem;

    [Tooltip("咀嚼シーケンス中、デバイス動作中 (処理状態 = 1) の間はキー入力を無視する。")]
    [SerializeField]
    private bool respectDeviceBusy = true;

    [Header("Debug Score UI")]
    [SerializeField]
    private TMPro.TMP_Text scoreText;

    [Header("Instruction UI")]
    [SerializeField]
    private GameObject instructionRoot;

    [SerializeField]
    private TMPro.TMP_Text instructionText;

    [Header("Feedback")]
    [SerializeField]
    private float feedbackDuration = 2.0f;

    [Header("Quiz SE")]
    [SerializeField]
    private AudioSource seSource;

    [SerializeField]
    private AudioClip correctSE;

    [SerializeField]
    private AudioClip incorrectSE;

    [Header("Instruction Messages")]
    [SerializeField]
    private string chooseMessage =
        "どちらかを取ってください";

    [SerializeField]
    private string holdMessage =
        "手に持ってください";

    [SerializeField]
    private string bringToMouthMessage =
        "口元へ運んでください";

    [SerializeField]
    private string chewingMessage =
        "そのまま噛み続けてください";

    [SerializeField]
    private string correctMessage =
        "お見事！正解！";

    [SerializeField]
    private string incorrectMessage =
        "残念！不正解！";

    // ── 咀嚼シーケンス定義 ───────────────────────────────────────────────────
    //
    // 問題開始時、デバイスは必ず 100%。
    //   減衰フェーズ (正解・不正解共通) : 100 → 0 → 80 → 0 → 60 → 0 → 40 → 0 → 20 → 0
    //   回復フェーズ (正解時のみ)       : → 20 → 0 → 40 → 0 → 60 → 0 → 80 → 100
    // 各要素は「そのステップで到達する充填率 (%)」。キー入力 1 回で 1 ステップ進む。
    private static readonly int[] DecayTargets = { 0, 80, 0, 60, 0, 40, 0, 20, 0 };
    private static readonly int[] RecoveryTargets = { 20, 0, 40, 0, 60, 0, 80, 100 };

    // #45 ①: 本来はトラッカー位置から特定する。当面は KeyboardManager /
    // ExperienceFlowController のキー入力 (G / H) で NotifyDeviceSelected 経由で設定する。
    private SerialSystem.SerialDevice selectedDevice = SerialSystem.SerialDevice.None;

    // Unity 側が把握しているデバイスの現在充填率 (%)。
    // 充填 / 吸引コマンドを送るたびに更新し、Arduino からの報告値で随時補正する (同期)。
    private int expectedFillPercent = 0;

    private int[] chewTargets;   // 現在の問題の咀嚼ステップ列 (正誤で長さが変わる)
    private int chewStep;        // 次に実行するステップのインデックス
    private bool answerWasCorrect;

    private int currentQuestionIndex = -1;
    private int score = 0;
    private bool quizRunning = false;
    private bool answerLocked = false;
    private bool instructionWarningLogged = false;
    private bool hasSelectedAnswer = false;
    private string currentInstructionMessage = "";
    private AnswerSide selectedAnswer;
    private QuestionPhase currentPhase =
        QuestionPhase.WaitingForSelection;

    public bool IsQuizRunning => quizRunning;
    public int CurrentQuestionIndex => currentQuestionIndex;
    public int Score => score;
    public SerialSystem.SerialDevice SelectedDevice => selectedDevice;

    private void Start()
    {
        SetInstructionVisible(false);
        HideAllQuestions();
        UpdateScoreDisplay();

        if (fishSystem == null)
        {
            fishSystem = FindAnyObjectByType<FishSystem>();
        }

        if (fogSystem == null)
        {
            fogSystem = FindAnyObjectByType<FogSystem>();
        }

        if (serialSystem == null)
        {
            serialSystem = FindAnyObjectByType<SerialSystem>();
        }

        if (serialSystem != null)
        {
            serialSystem.OnFillPercentChanged += HandleDeviceFillPercentChanged;
        }
    }

    private void OnDestroy()
    {
        if (serialSystem != null)
        {
            serialSystem.OnFillPercentChanged -= HandleDeviceFillPercentChanged;
        }
    }

    /// <summary>
    /// #45 ①: どちらのデバイス (トラッカー) を選んだかを通知する。
    /// 当面はキー入力から呼ばれる。
    /// </summary>
    public void NotifyDeviceSelected(SerialSystem.SerialDevice device)
    {
        selectedDevice = device;
        Debug.Log($"[Quiz] 使用デバイス = {device}");

        // 選択直後に Unity の推定値を実機へ書き込んで状態を一致させる。
        // (問題開始時は 100% 想定。Arduino 起動時の値と食い違う場合の保険)
        if (quizRunning && DeviceConnected)
        {
            serialSystem.Calibrate(selectedDevice, expectedFillPercent);
            Debug.Log($"[Quiz] デバイス状態を同期: {expectedFillPercent}%");
        }
    }

    // Arduino が報告する実際の充填率で Unity の推定値を補正する (状態同期)。
    private void HandleDeviceFillPercentChanged(
        SerialSystem.SerialDevice device, int reportedPercent)
    {
        if (!quizRunning || device != selectedDevice)
            return;

        // 駆動中の報告は途中値で当てにならない。停止中のみ扱う。
        if (serialSystem.IsBusy(device))
            return;

        if (reportedPercent == expectedFillPercent)
            return;

        // 咀嚼中は、送ったコマンドがまだ実機に反映されていない可能性があるため
        // ログだけ出す。噛み始める前後の待機フェーズでのみ推定値を実機値へ合わせる。
        bool safeToReconcile =
            currentPhase == QuestionPhase.WaitingForSelection ||
            currentPhase == QuestionPhase.WaitingForHold ||
            currentPhase == QuestionPhase.WaitingForMouth ||
            currentPhase == QuestionPhase.ShowingFeedback;

        if (safeToReconcile)
        {
            Debug.LogWarning(
                $"[Quiz] 充填率のズレを検出: 推定 {expectedFillPercent}% → 実機 {reportedPercent}%。実機値へ補正"
            );
            expectedFillPercent = reportedPercent;
        }
        else
        {
            Debug.Log(
                $"[Quiz] 充填率の差 (推定 {expectedFillPercent}% / 実機 {reportedPercent}%) — 咀嚼中のため補正保留"
            );
        }
    }

    public void StartQuiz()
    {
        if (questions == null || questions.Length == 0)
        {
            Debug.LogWarning(
                "[Quiz] 問題が設定されていません"
            );

            return;
        }

        score = 0;
        quizRunning = true;
        expectedFillPercent = 0;

        if (fishSystem != null)
        {
            fishSystem.ResetToInitial();
        }

        if (fogSystem != null)
        {
            fogSystem.ResetToClean();
        }

        if (serialSystem != null)
        {
            serialSystem.StopAll();
        }

        ShowQuestion(0);

        UpdateScoreDisplay();

        Debug.Log(
            $"[Quiz] 全 {questions.Length} 問で開始"
        );
    }

    public void AnswerLeft()
    {
        SubmitAnswer(AnswerSide.Left);
    }

    public void AnswerRight()
    {
        SubmitAnswer(AnswerSide.Right);
    }

    private void SubmitAnswer(AnswerSide answer)
    {
        if (!quizRunning)
            return;

        if (answerLocked)
            return;

        if (currentQuestionIndex < 0 ||
            currentQuestionIndex >= questions.Length)
            return;

        answerLocked = true;

        selectedAnswer = answer;
        hasSelectedAnswer = true;
        currentPhase = QuestionPhase.WaitingForHold;

        ShowInstruction(holdMessage);

        Debug.Log(
            $"[Quiz] {(answer == AnswerSide.Left ? "左" : "右")}を選択"
        );
    }

    private void ShowQuestion(int index)
    {
        if (index < 0 || index >= questions.Length)
            return;

        HideAllQuestions();

        currentQuestionIndex = index;
        chewTargets = null;
        chewStep = 0;

        // 問題開始時は必ずデバイスを 100% に戻す。
        //   正解直後 : 回復フェーズで既に 100%。Calibrate で同期のみ。
        //   不正解直後 / 初回 : 0% (または不定) から Fill(100) で膨らませる。
        if (SerialActive)
        {
            if (DeviceConnected)
            {
                if (expectedFillPercent >= 100)
                    serialSystem.Calibrate(selectedDevice, 100);
                else
                    serialSystem.Fill(selectedDevice, 100);
            }
            else
            {
                StopSelectedDevice();
            }
        }
        expectedFillPercent = 100;

        answerLocked = false;
        hasSelectedAnswer = false;
        currentPhase = QuestionPhase.WaitingForSelection;

        if (questions[index].questionObject != null)
        {
            questions[index].questionObject.SetActive(true);
        }

        ShowInstruction(chooseMessage);

        Debug.Log(
            $"[Quiz] 問題 {currentQuestionIndex + 1} / {questions.Length}"
        );
    }

    public void DebugNextQuestion()
    {
        if (!quizRunning)
            return;

        if (currentQuestionIndex < questions.Length - 1)
        {
            ShowQuestion(currentQuestionIndex + 1);
        }
        else
        {
            FinishQuiz();
        }
    }

    public void DebugPreviousQuestion()
    {
        if (!quizRunning)
            return;

        if (currentQuestionIndex > 0)
        {
            ShowQuestion(currentQuestionIndex - 1);
        }
    }

    private void GoToNextQuestion()
    {
        int nextIndex =
            currentQuestionIndex + 1;

        if (nextIndex < questions.Length)
        {
            ShowQuestion(nextIndex);
        }
        else
        {
            FinishQuiz();
        }
    }

    private void FinishQuiz()
    {
        quizRunning = false;
        expectedFillPercent = 0;

        if (serialSystem != null)
        {
            serialSystem.StopAll();
        }

        selectedDevice = SerialSystem.SerialDevice.None;

        SetInstructionVisible(false);
        HideAllQuestions();

        Debug.Log(
            $"[Quiz] 全問題終了 最終Score = {score}/{questions.Length}"
        );

        UpdateScoreDisplay();

        if (gameFlow != null)
        {
            gameFlow.ShowResult();
        }
        else
        {
            Debug.LogWarning(
                "[Quiz] GameFlow が設定されていません"
            );
        }
    }

    private void HideAllQuestions()
    {
        if (questions == null)
            return;

        foreach (QuestionData question in questions)
        {
            if (question.questionObject != null)
            {
                question.questionObject.SetActive(false);
            }
        }
    }

    // 単一の「送り」キーから呼ばれる。現在のフェーズに応じて 1 歩進める。
    [ContextMenu("Instruction: Show Next")]
    public void AdvanceInstruction()
    {
        if (!quizRunning)
            return;

        switch (currentPhase)
        {
            case QuestionPhase.WaitingForSelection:
                Debug.Log("[Quiz] 先に左右を選択してください");
                break;

            case QuestionPhase.WaitingForHold:
                NotifyHeldInHand();
                break;

            case QuestionPhase.WaitingForMouth:
                NotifyMovedToMouth();
                break;

            case QuestionPhase.Chewing:
                NotifyChewAdvance();
                break;

            case QuestionPhase.ShowingFeedback:
                break;
        }
    }

    // 以下はシリアル通信側 / デバッグ入力から呼ぶための受け口。
    public void NotifyHeldInHand()
    {
        if (!quizRunning ||
            currentPhase != QuestionPhase.WaitingForHold)
            return;

        currentPhase = QuestionPhase.WaitingForMouth;
        ShowInstruction(bringToMouthMessage);

        Debug.Log("[Quiz] 手持ちを確認");
    }

    // くわえた瞬間に正誤判定し、咀嚼シーケンスへ入る。
    public void NotifyMovedToMouth()
    {
        if (!quizRunning ||
            currentPhase != QuestionPhase.WaitingForMouth)
            return;

        // 問題開始時の Fill(100) がまだ終わっていない場合は待たせる。
        if (respectDeviceBusy && DeviceBusy)
        {
            Debug.Log("[Quiz] デバイス充填中。くわえる操作を無視");
            return;
        }

        Debug.Log("[Quiz] 口元への移動を確認 → 正誤判定");

        JudgeSelectedAnswer();          // answerWasCorrect を決定し、fish / fog / score を反映
        BuildChewSequence(answerWasCorrect);

        currentPhase = QuestionPhase.Chewing;
        ShowInstruction(chewingMessage);
    }

    // 咀嚼シーケンスを 1 ステップ進める。
    public void NotifyChewAdvance()
    {
        if (!quizRunning ||
            currentPhase != QuestionPhase.Chewing)
            return;

        if (respectDeviceBusy && DeviceBusy)
        {
            Debug.Log("[Quiz] デバイス動作中。次のキーを無視");
            return;
        }

        PerformChewStep();
    }

    private void JudgeSelectedAnswer()
    {
        if (!hasSelectedAnswer ||
            currentQuestionIndex < 0 ||
            currentQuestionIndex >= questions.Length)
        {
            answerWasCorrect = false;
            return;
        }

        answerWasCorrect =
            selectedAnswer == questions[currentQuestionIndex].correctAnswer;

        if (answerWasCorrect)
        {
            score++;

            if (fishSystem != null)
            {
                fishSystem.OnCorrect();
                fishSystem.PlayMouthBurst();
            }
            else
            {
                Debug.LogWarning("[Quiz] FishSystem が設定されていません");
            }
        }
        else
        {
            if (fishSystem != null)
                fishSystem.OnIncorrect();

            if (fogSystem != null)
                fogSystem.StepDirtier();
            else
                Debug.LogWarning("[Quiz] FogSystem が設定されていません");
        }

        UpdateScoreDisplay();

        Debug.Log(
            $"[Quiz] 判定: {(answerWasCorrect ? "正解" : "不正解")} Score = {score}"
        );
    }

    // 減衰 (共通) + 回復 (正解時のみ) を連結したステップ列を組み立てる。
    private void BuildChewSequence(bool correct)
    {
        int len = DecayTargets.Length + (correct ? RecoveryTargets.Length : 0);
        chewTargets = new int[len];

        for (int i = 0; i < DecayTargets.Length; i++)
            chewTargets[i] = DecayTargets[i];

        if (correct)
        {
            for (int i = 0; i < RecoveryTargets.Length; i++)
                chewTargets[DecayTargets.Length + i] = RecoveryTargets[i];
        }

        chewStep = 0;
    }

    // 次の目標充填率へ 1 手動かす。
    private void PerformChewStep()
    {
        if (chewTargets == null || chewStep >= chewTargets.Length)
        {
            FinishChewSequence();
            return;
        }

        int target = chewTargets[chewStep];

        if (SerialActive && DeviceConnected)
        {
            // 値は「目標充填率」。増やすなら f、減らすなら s。
            if (target > expectedFillPercent)
                serialSystem.Fill(selectedDevice, target);
            else if (target < expectedFillPercent)
                serialSystem.Suck(selectedDevice, target);
            // 同値なら何も送らない
        }
        else if (SerialActive)
        {
            Debug.LogWarning("[Quiz] デバイス未接続。咀嚼は空進行します");
        }

        expectedFillPercent = target;
        chewStep++;

        Debug.Log(
            $"[Quiz] 咀嚼ステップ {chewStep}/{chewTargets.Length} → {target}%"
        );

        if (chewStep >= chewTargets.Length)
            FinishChewSequence();
    }

    private void FinishChewSequence()
    {
        currentPhase = QuestionPhase.ShowingFeedback;

        ShowInstruction(
            answerWasCorrect ? correctMessage : incorrectMessage
        );

        if (seSource != null)
        {
            if (answerWasCorrect && correctSE != null)
            {
                seSource.PlayOneShot(correctSE);
            }
            else if (!answerWasCorrect && incorrectSE != null)
            {
            seSource.PlayOneShot(incorrectSE);
            }
        }

        StartCoroutine(ContinueAfterFeedbackDelay());

        Debug.Log(
            $"[Quiz] 咀嚼シーケンス完了 ({(answerWasCorrect ? "正解" : "不正解")}) → 充填率 {expectedFillPercent}%"
        );
    }

    // serialSystem があり、デバイスが選択済みか。
    private bool SerialActive =>
        serialSystem != null &&
        selectedDevice != SerialSystem.SerialDevice.None;

    // 選択デバイスのシリアルポートが開いているか。
    private bool DeviceConnected =>
        SerialActive && serialSystem.IsConnected(selectedDevice);

    // 選択デバイスが動作中 (Arduino の処理状態 = 1) か。
    private bool DeviceBusy =>
        SerialActive && serialSystem.IsBusy(selectedDevice);

    // 中断・問題切り替え時にデバイスを止める ('i',0)。
    private void StopSelectedDevice()
    {
        if (SerialActive)
            serialSystem.Stop(selectedDevice);
    }

    // 旧「しぼみ切り信号」受け口。新フローではデバッグ用ショートカットとして、
    // 残りの咀嚼ステップを一気に消化してフィードバックへ進める。
    public void NotifyDeviceFullyDeflated()
    {
        if (!quizRunning || currentPhase != QuestionPhase.Chewing)
            return;

        Debug.Log("[Quiz] (デバッグ) 咀嚼シーケンスを最後までスキップ");

        int guard = 0;
        while (currentPhase == QuestionPhase.Chewing && guard++ < 64)
            PerformChewStep();
    }

    // Fish/Fogまたはデバッグ操作から、演出完了後に呼ぶ。
    public void ContinueAfterFeedback()
    {
        if (!quizRunning ||
            currentPhase != QuestionPhase.ShowingFeedback)
            return;

        GoToNextQuestion();
    }

    private void ShowInstruction(string message)
    {
        currentInstructionMessage = message;

        Debug.Log($"[Quiz][Instruction] {message}");

        if (instructionText == null)
        {
            if (scoreText != null)
            {
                UpdateScoreDisplay();
                return;
            }

            if (!instructionWarningLogged)
            {
                Debug.LogWarning(
                    "[Quiz] Instruction Text と Score Text が設定されていないため、指示を表示できません"
                );
                instructionWarningLogged = true;
            }

            return;
        }

        instructionText.text = message;
        SetInstructionVisible(true);
    }

    private void SetInstructionVisible(bool visible)
    {
        if (!visible)
        {
            currentInstructionMessage = "";

            if (instructionText == null)
                UpdateScoreDisplay();
        }

        if (instructionRoot != null)
        {
            instructionRoot.SetActive(visible);
            return;
        }

        if (instructionText != null)
        {
            instructionText.gameObject.SetActive(visible);
        }
    }

    private void UpdateScoreDisplay()
    {
        if (scoreText == null)
            return;

        if (instructionText == null &&
            !string.IsNullOrEmpty(currentInstructionMessage))
        {
            scoreText.text =
                $"Score : {score}\n\n{currentInstructionMessage}";
        }
        else
        {
            scoreText.text =
                $"Score : {score}";
        }
    }

    private IEnumerator ContinueAfterFeedbackDelay()
    {
        yield return new WaitForSeconds(feedbackDuration);

        ContinueAfterFeedback();
    }
}
