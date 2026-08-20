using UnityEngine;

public class QuizManager : MonoBehaviour
{
    [Header("Questions")]
    [SerializeField]
    private GameObject[] questions;

    [Header("Flow")]
    [SerializeField]
    private ExperienceFlowController gameFlow;

    private int currentQuestionIndex = -1;
    private bool quizRunning = false;

    public bool IsQuizRunning => quizRunning;

    public int CurrentQuestionIndex => currentQuestionIndex;

    public int QuestionCount =>
        questions != null ? questions.Length : 0;

    private void Start()
    {
        HideAllQuestions();
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

        quizRunning = true;

        ShowQuestion(0);

        Debug.Log(
            $"[Quiz] 全 {questions.Length} 問で開始"
        );
    }

    public void DebugAdvanceQuestion()
    {
        if (!quizRunning)
            return;

        GoToNextQuestion();
    }

    private void ShowQuestion(int index)
    {
        if (index < 0 || index >= questions.Length)
            return;

        for (int i = 0; i < questions.Length; i++)
        {
            if (questions[i] != null)
            {
                questions[i].SetActive(i == index);
            }
        }

        currentQuestionIndex = index;

        Debug.Log(
            $"[Quiz] 問題 {currentQuestionIndex + 1} / {questions.Length}"
        );
    }

    private void GoToNextQuestion()
    {
        int nextIndex = currentQuestionIndex + 1;

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

        Debug.Log("[Quiz] 全問題終了");

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

        foreach (GameObject question in questions)
        {
            if (question != null)
            {
                question.SetActive(false);
            }
        }
    }
}