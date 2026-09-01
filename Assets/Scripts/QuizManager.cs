using System.Collections;
using UnityEngine;

public class QuizManager : MonoBehaviour
{
    private enum QuestionPhase
    {
        WaitingForSelection,
        WaitingForHold,
        WaitingForMouth,
        WaitingForBite,
        WaitingForOpen,
        Deflating,
        ShowingFeedback
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

    [Tooltip("噛むごとにデバイスを充填する量 (%)。20 なら 5 回で 100% (#42)。")]
    [SerializeField, Range(1, 100)]
    private int fillStepPercent = 20;

    [Tooltip("最後に口を開いたときにデバイスを吸引する量 (%)。")]
    [SerializeField, Range(1, 100)]
    private int deflatePercent = 100;

    [Tooltip("吸引を送ってから、しぼみ切り信号を待つ最大秒数。超えたら手動で判定へ進む。")]
    [SerializeField, Min(0.5f)]
    private float deflateTimeoutSeconds = 3f;

    [Tooltip("噛む/開く信号を受けても、デバイスが動作中 (処理状態 = 1) の間は無視する (#42)。")]
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
    private string biteMessage =
        "噛んでください";

    [SerializeField]
    private string openMessage =
        "開いてください";

    [SerializeField]
    private string finalOpenMessage =
        "口を大きく開けてください";

    [SerializeField]
    private string deflatingMessage =
        "そのまま少し待ってください";

    [SerializeField]
    private string correctMessage =
        "お見事！正解！";

    [SerializeField]
    private string incorrectMessage =
        "残念！不正解！";

    // #45 ①: 本来はトラッカー位置から特定する。当面は KeyboardManager /
    // ExperienceFlowController のキー入力 (G / H) で NotifyDeviceSelected 経由で設定する。
    private SerialSystem.SerialDevice selectedDevice = SerialSystem.SerialDevice.None;

    // 現在の問題で、これまでに送った充填量の合計 (%)。フロー判断の基準。
    private int expectedFillPercent = 0;
    private Coroutine deflateWatchdogCo;

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
            serialSystem.OnFullyDeflated += HandleDeviceFullyDeflated;
        }
    }

    private void OnDestroy()
    {
        if (serialSystem != null)
        {
            serialSystem.OnFullyDeflated -= HandleDeviceFullyDeflated;
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
    }

    // SerialSystem がデバイスの「停止 かつ 充填率 0」を検知したら呼ばれる。
    private void HandleDeviceFullyDeflated(SerialSystem.SerialDevice device)
    {
        if (selectedDevice != SerialSystem.SerialDevice.None &&
            device != selectedDevice)
            return;

        NotifyDeviceFullyDeflated();
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

        StopDeflateWatchdog();
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

    private void SubmitAnswer(AnswerSide selectedAnswer)
    {
        if (!quizRunning)
            return;

        if (answerLocked)
            return;

        if (currentQuestionIndex < 0 ||
            currentQuestionIndex >= questions.Length)
            return;

        answerLocked = true;

        this.selectedAnswer = selectedAnswer;
        hasSelectedAnswer = true;
        currentPhase = QuestionPhase.WaitingForHold;

        ShowInstruction(holdMessage);

        Debug.Log(
            $"[Quiz] {(selectedAnswer == AnswerSide.Left ? "左" : "右")}を選択"
        );
    }

    private void JudgeSelectedAnswer()
    {
        if (!quizRunning || !hasSelectedAnswer)
            return;

        if (currentQuestionIndex < 0 ||
            currentQuestionIndex >= questions.Length)
            return;

        QuestionData currentQuestion =
            questions[currentQuestionIndex];

        bool isCorrect =
            this.selectedAnswer == currentQuestion.correctAnswer;

        if (isCorrect)
        {
            HandleCorrectAnswer();
            BeginCorrectFeedback();
        }
        else
        {
            HandleIncorrectAnswer();
            BeginIncorrectFeedback();
        }

        UpdateScoreDisplay();
        currentPhase = QuestionPhase.ShowingFeedback;
    }

    private void HandleCorrectAnswer()
    {
        score++;

        Debug.Log(
            $"[Quiz] 正解！ Score = {score}"
        );

        if (fishSystem != null)
        {
            fishSystem.OnCorrect();
            fishSystem.PlayMouthBurst();
        }
        else
        {
            Debug.LogWarning(
                "[Quiz] FishSystem が設定されていません"
            );
        }
    }

    private void BeginCorrectFeedback()
    {
        // 正解時の実行順。
        // 1. デバイスの充填/吸引は噛む/開く信号 (NotifyBiteDetected /
        //    NotifyOpenDetected) 側で SerialSystem へ送っている。ここでは何もしない。

        // 2. 正解UIを表示する。
        ShowInstruction(correctMessage);
        StartCoroutine(ContinueAfterFeedbackDelay());

        // 3. FishSystemを呼び、口元と体の周囲の演出を同時に開始する。
        // fishSystem.OnCorrect();
        // fishSystem.PlayMouthBurst();
    }

    private void HandleIncorrectAnswer()
    {
        Debug.Log(
            $"[Quiz] 不正解 Score = {score}"
        );

        if (fishSystem != null)
        {
            fishSystem.OnIncorrect();
        }

        if (fogSystem != null)
        {
            fogSystem.StepDirtier();
        }
        else
        {
            Debug.LogWarning(
                "[Quiz] FogSystem が設定されていません"
            );
        }
    }

    private void BeginIncorrectFeedback()
    {
        // 不正解時の実行順。
        // 1. 不正解UIを表示する。
        ShowInstruction(incorrectMessage);
        StartCoroutine(ContinueAfterFeedbackDelay());

        // 2. FogSystemを呼び、海中を1段階汚す。
        // fishSystem.OnIncorrect();
        // fogSystem.StepDirtier();
    }

    private void ShowQuestion(int index)
    {
        if (index < 0 || index >= questions.Length)
            return;

        HideAllQuestions();

        // 前の問題でデバイスが膨らんだままなら、止めてしぼませてから次へ (#42)。
        StopDeflateWatchdog();
        if (SerialActive && expectedFillPercent > 0)
        {
            if (DeviceConnected)
                serialSystem.Suck(selectedDevice, 100);
            else
                StopSelectedDevice();
        }
        expectedFillPercent = 0;

        currentQuestionIndex = index;
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

        StopDeflateWatchdog();
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

    [ContextMenu("Instruction: Show Next")]
    public void AdvanceInstruction()
    {
        if (!quizRunning)
            return;

        switch (currentPhase)
        {
            case QuestionPhase.WaitingForSelection:
                Debug.Log(
                    "[Quiz] 先にA/Dで左右を選択してください"
                );
                break;

            case QuestionPhase.WaitingForHold:
                NotifyHeldInHand();
                break;

            case QuestionPhase.WaitingForMouth:
                NotifyMovedToMouth();
                break;

            case QuestionPhase.WaitingForBite:
                NotifyBiteDetected();
                break;

            case QuestionPhase.WaitingForOpen:
                NotifyOpenDetected();
                break;

            case QuestionPhase.Deflating:
                // 吸引完了 (OnFullyDeflated) 待ち。手動操作では進めない。
                break;

            case QuestionPhase.ShowingFeedback:
                break;
        }
    }

    // 以下はシリアル通信側から呼ぶための受け口。
    // 現在はExperienceFlowControllerのデバッグ入力からも呼べる。
    public void NotifyHeldInHand()
    {
        if (!quizRunning ||
            currentPhase != QuestionPhase.WaitingForHold)
            return;

        currentPhase = QuestionPhase.WaitingForMouth;
        ShowInstruction(bringToMouthMessage);

        Debug.Log("[Quiz] 手持ちを確認");
    }

    public void NotifyMovedToMouth()
    {
        if (!quizRunning ||
            currentPhase != QuestionPhase.WaitingForMouth)
            return;

        currentPhase = QuestionPhase.WaitingForBite;
        ShowInstruction(biteMessage);

        Debug.Log("[Quiz] 口元への移動を確認");
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

    // これまでの充填指示の合計が 100% に達したか。
    private bool DeviceIsFull => expectedFillPercent >= 100;

    public void NotifyBiteDetected()
    {
        if (!quizRunning ||
            currentPhase != QuestionPhase.WaitingForBite)
            return;

        // #42: 動作中はロック。噛む信号を受け付けない。
        if (respectDeviceBusy && DeviceBusy)
        {
            Debug.Log("[Quiz] デバイス動作中のため噛む信号を無視");
            return;
        }

        // #42 / #45 ②: 噛むごとに 1 段階だけ充填する (相対量。5 回で 100%)。
        if (SerialActive && !DeviceIsFull)
        {
            if (DeviceConnected)
            {
                serialSystem.Fill(selectedDevice, fillStepPercent);
            }
            else
            {
                Debug.LogWarning(
                    "[Quiz] デバイス未接続。充填せず進行します (F で手動判定)"
                );
            }
        }
        else if (!SerialActive)
        {
            Debug.LogWarning(
                "[Quiz] デバイス未選択のまま噛みました (G / H で選択)"
            );
        }

        expectedFillPercent =
            Mathf.Min(100, expectedFillPercent + fillStepPercent);

        currentPhase = QuestionPhase.WaitingForOpen;
        ShowInstruction(DeviceIsFull ? finalOpenMessage : openMessage);

        Debug.Log($"[Quiz] 噛んだ信号を受信 (充填 {expectedFillPercent}%)");
    }

    public void NotifyOpenDetected()
    {
        if (!quizRunning ||
            currentPhase != QuestionPhase.WaitingForOpen)
            return;

        if (respectDeviceBusy && DeviceBusy)
        {
            Debug.Log("[Quiz] デバイス動作中のため開く信号を無視");
            return;
        }

        if (DeviceIsFull)
        {
            // 最終開放: 吸引してしぼませ、しぼみ切りで判定へ。
            currentPhase = QuestionPhase.Deflating;
            ShowInstruction(deflatingMessage);
            DeflateDevice();
            StartDeflateWatchdog();

            Debug.Log("[Quiz] 開いた信号を受信 (最終 → 吸引開始)");
        }
        else
        {
            // まだ途中。次の一噛みへ。
            currentPhase = QuestionPhase.WaitingForBite;
            ShowInstruction(biteMessage);

            Debug.Log("[Quiz] 開いた信号を受信 (継続)");
        }
    }

    private void DeflateDevice()
    {
        if (!SerialActive)
            return;

        if (!DeviceConnected)
        {
            Debug.LogWarning("[Quiz] デバイス未接続。吸引をスキップします");
            return;
        }

        serialSystem.Suck(selectedDevice, deflatePercent);
    }

    // 中断・問題切り替え時にデバイスを止める (#42: 'i',0)。
    private void StopSelectedDevice()
    {
        if (SerialActive)
            serialSystem.Stop(selectedDevice);
    }

    private void StartDeflateWatchdog()
    {
        StopDeflateWatchdog();
        deflateWatchdogCo = StartCoroutine(DeflateWatchdogRoutine());
    }

    private void StopDeflateWatchdog()
    {
        if (deflateWatchdogCo != null)
        {
            StopCoroutine(deflateWatchdogCo);
            deflateWatchdogCo = null;
        }
    }

    private IEnumerator DeflateWatchdogRoutine()
    {
        yield return new WaitForSeconds(deflateTimeoutSeconds);

        deflateWatchdogCo = null;

        if (quizRunning && currentPhase == QuestionPhase.Deflating)
        {
            Debug.LogWarning(
                "[Quiz] しぼみ切り信号のタイムアウト。手動で判定に進みます"
            );
            NotifyDeviceFullyDeflated();
        }
    }

    public void NotifyDeviceFullyDeflated()
    {
        if (!quizRunning || !hasSelectedAnswer)
            return;

        bool isChewing =
            currentPhase == QuestionPhase.WaitingForBite ||
            currentPhase == QuestionPhase.WaitingForOpen ||
            currentPhase == QuestionPhase.Deflating;

        if (!isChewing)
            return;

        StopDeflateWatchdog();

        Debug.Log("[Quiz] デバイスが完全にしぼんだ信号を受信");

        JudgeSelectedAnswer();
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
