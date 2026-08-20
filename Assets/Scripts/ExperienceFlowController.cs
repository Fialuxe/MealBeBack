using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class ExperienceFlowController : MonoBehaviour
{
    private enum FlowState
    {
        Ready,
        Quiz,
        Result
    }

    [Header("Scene")]
    [SerializeField]
    private string setupSceneName = "SetupScene";

    [Header("Flow Roots")]
    [SerializeField]
    private GameObject readyRoot;

    [SerializeField]
    private GameObject quizRoot;

    [SerializeField]
    private GameObject resultRoot;

    [Header("Quiz")]
    [SerializeField]
    private QuizManager quizManager;

    private FlowState currentState = FlowState.Ready;
    private bool isLoading = false;

    private void Start()
    {
        ShowReady();
    }

    private void Update()
    {
        if (isLoading)
            return;

        if (Keyboard.current == null)
            return;

        bool enterPressed =
            Keyboard.current.enterKey.wasPressedThisFrame ||
            Keyboard.current.numpadEnterKey.wasPressedThisFrame;

        if (!enterPressed)
            return;

        switch (currentState)
        {
            case FlowState.Ready:
                StartQuiz();
                break;

            case FlowState.Quiz:
                // 今はデバッグ用。
                // 後でTrackerによる回答処理に置き換える。
                if (quizManager != null)
                {
                    quizManager.DebugAdvanceQuestion();
                }
                break;

            case FlowState.Result:
                ReturnToSetup();
                break;
        }
    }

    private void ShowReady()
    {
        currentState = FlowState.Ready;

        SetFlowState(
            ready: true,
            quiz: false,
            result: false
        );

        Debug.Log("[GameFlow] Ready");
    }

    private void StartQuiz()
    {
        if (quizManager == null)
        {
            Debug.LogWarning(
                "[GameFlow] QuizManager が設定されていません"
            );
            return;
        }

        currentState = FlowState.Quiz;

        SetFlowState(
            ready: false,
            quiz: true,
            result: false
        );

        Debug.Log("[GameFlow] Quiz開始");

        quizManager.StartQuiz();
    }

    public void ShowResult()
    {
        currentState = FlowState.Result;

        SetFlowState(
            ready: false,
            quiz: false,
            result: true
        );

        Debug.Log("[GameFlow] Result");
    }

    private void ReturnToSetup()
    {
        isLoading = true;

        Debug.Log("[GameFlow] SetupSceneへ戻ります");

        SceneManager.LoadScene(setupSceneName);
    }

    private void SetFlowState(
        bool ready,
        bool quiz,
        bool result)
    {
        if (readyRoot != null)
            readyRoot.SetActive(ready);

        if (quizRoot != null)
            quizRoot.SetActive(quiz);

        if (resultRoot != null)
            resultRoot.SetActive(result);
    }
}