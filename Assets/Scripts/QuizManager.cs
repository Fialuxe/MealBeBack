using UnityEngine;

public class QuizManager : MonoBehaviour
{
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

    [Header("Debug Score UI")]
    [SerializeField]
    private TMPro.TMP_Text scoreText;

    private int currentQuestionIndex = -1;
    private int score = 0;
    private bool quizRunning = false;
    private bool answerLocked = false;

    public bool IsQuizRunning => quizRunning;
    public int CurrentQuestionIndex => currentQuestionIndex;
    public int Score => score;

    private void Start()
    {
        HideAllQuestions();
        UpdateScoreDisplay();

        if (fishSystem == null)
        {
            fishSystem = FindAnyObjectByType<FishSystem>();
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

        QuestionData currentQuestion =
            questions[currentQuestionIndex];

        bool isCorrect =
            selectedAnswer == currentQuestion.correctAnswer;

        if (isCorrect)
        {
            HandleCorrectAnswer();
        }
        else
        {
            HandleIncorrectAnswer();
        }

        UpdateScoreDisplay();

        // 今は演出がないため、そのまま次問題へ進む
        GoToNextQuestion();
    }

    private void HandleCorrectAnswer()
    {
        score++;

        Debug.Log(
            $"[Quiz] 正解！ Score = {score}"
        );

        if (fishSystem != null)
        {
            fishSystem.EmitPollockFromMouth();
        }
        else
        {
            Debug.LogWarning(
                "[Quiz] FishSystem が設定されていません"
            );
        }
    }

    private void HandleIncorrectAnswer()
    {
        Debug.Log(
            $"[Quiz] 不正解 Score = {score}"
        );

        // TODO:
        // 海が汚れる演出をここから呼ぶ
    }

    private void ShowQuestion(int index)
    {
        if (index < 0 || index >= questions.Length)
            return;

        HideAllQuestions();

        currentQuestionIndex = index;
        answerLocked = false;

        if (questions[index].questionObject != null)
        {
            questions[index].questionObject.SetActive(true);
        }

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

    private void UpdateScoreDisplay()
    {
        if (scoreText == null)
            return;

        scoreText.text =
            $"Score : {score}";
    }
}