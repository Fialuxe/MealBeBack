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

    [Header("Debug Score UI")]
    [SerializeField]
    private TMPro.TMP_Text scoreText;

    [Header("Instruction UI")]
    [SerializeField]
    private GameObject instructionRoot;

    [SerializeField]
    private TMPro.TMP_Text instructionText;

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
    private string correctMessage =
        "お見事！正解！";

    [SerializeField]
    private string incorrectMessage =
        "残念！不正解！";

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

    private void Start()
    {
        SetInstructionVisible(false);
        HideAllQuestions();
        UpdateScoreDisplay();
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

        // TODO:
        // 口から生き物が飛び出す演出をここから呼ぶ
    }

    private void BeginCorrectFeedback()
    {
        // 正解時の実行順。
        // 1. デバイスの膨張を開始する。
        // serialPortManager.BeginInflation();

        // 2. 正解UIを表示する。
        ShowInstruction(correctMessage);

        // 3. FishSystemを呼び、口元と体の周囲の演出を同時に開始する。
        // fishSystem.OnCorrect();
        // fishSystem.PlayMouthBurst();
    }

    private void HandleIncorrectAnswer()
    {
        Debug.Log(
            $"[Quiz] 不正解 Score = {score}"
        );

        // TODO:
        // 海が汚れる演出をここから呼ぶ
    }

    private void BeginIncorrectFeedback()
    {
        // 不正解時の実行順。
        // 1. 不正解UIを表示する。
        ShowInstruction(incorrectMessage);

        // 2. FogSystemを呼び、海中を1段階汚す。
        // fishSystem.OnIncorrect();
        // fogSystem.StepDirtier();
    }

    private void ShowQuestion(int index)
    {
        if (index < 0 || index >= questions.Length)
            return;

        HideAllQuestions();

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

    public void NotifyBiteDetected()
    {
        if (!quizRunning ||
            currentPhase != QuestionPhase.WaitingForBite)
            return;

        currentPhase = QuestionPhase.WaitingForOpen;
        ShowInstruction(openMessage);

        Debug.Log("[Quiz] 噛んだ信号を受信");
    }

    public void NotifyOpenDetected()
    {
        if (!quizRunning ||
            currentPhase != QuestionPhase.WaitingForOpen)
            return;

        currentPhase = QuestionPhase.WaitingForBite;
        ShowInstruction(biteMessage);

        Debug.Log("[Quiz] 開いた信号を受信");
    }

    public void NotifyDeviceFullyDeflated()
    {
        if (!quizRunning || !hasSelectedAnswer)
            return;

        bool isChewing =
            currentPhase == QuestionPhase.WaitingForBite ||
            currentPhase == QuestionPhase.WaitingForOpen;

        if (!isChewing)
            return;

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

    // Issue #35 で FishSystem / FogSystem を接続するときの呼び出し位置。
    // このブランチではシステム側のPRと競合しないよう、呼び出しは有効化しない。
    //
    // StartQuiz():
    // fishSystem.ResetToInitial();
    // fogSystem.ResetToClean();
    //
    // HandleCorrectAnswer():
    // fishSystem.OnCorrect();
    // fishSystem.PlayMouthBurst();
    //
    // HandleIncorrectAnswer():
    // fishSystem.OnIncorrect();
    // fogSystem.StepDirtier();
}
