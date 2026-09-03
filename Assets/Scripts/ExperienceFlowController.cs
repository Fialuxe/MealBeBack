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

    [Header("Audio")]
    [SerializeField]
    private AudioSource bgmSource;

    [Header("Input")]
    [Tooltip("true: このクラスが直接キーボードを読む（従来動作）。" +
        "false: KeyboardManager など外部からの呼び出しに任せる。")]
    [SerializeField]
    private bool handleKeyboardInput = true;

    private FlowState currentState = FlowState.Ready;
    private bool isLoading = false;

    public bool IsReady => currentState == FlowState.Ready;
    public bool IsQuiz => currentState == FlowState.Quiz;
    public bool IsResult => currentState == FlowState.Result;
    public bool IsBusy => isLoading;

    /// <summary>Ready 状態ならクイズを開始する。KeyboardManager など外部入力から呼ぶ。</summary>
    public void RequestStartQuiz()
    {
        if (isLoading || currentState != FlowState.Ready)
            return;

        StartQuiz();
    }

    /// <summary>Result 状態なら SetupScene へ戻る。KeyboardManager など外部入力から呼ぶ。</summary>
    public void RequestReturnToSetup()
    {
        if (isLoading || currentState != FlowState.Result)
            return;

        ReturnToSetup();
    }

    private void Start()
    {
        ShowReady();
    }

    private void Update()
    {
        if (!handleKeyboardInput)
            return;

        if (isLoading)
            return;

        if (Keyboard.current == null)
            return;

        switch (currentState)
        {
            case FlowState.Ready:
                HandleReadyInput();
            break;

            case FlowState.Quiz:
                HandleQuizInput();
                break;

            case FlowState.Result:
                HandleResultInput();
                break;
        }
    }

    private void HandleReadyInput()
    {
        bool enterPressed =
            Keyboard.current.enterKey.wasPressedThisFrame ||
            Keyboard.current.numpadEnterKey.wasPressedThisFrame;

        if (enterPressed)
        {
            StartQuiz();
        }
    }

    private void HandleQuizInput()
    {
        if (quizManager == null)
            return;
        
        bool enterPressed =
            Keyboard.current.enterKey.wasPressedThisFrame ||
            Keyboard.current.numpadEnterKey.wasPressedThisFrame;

        if (enterPressed)
        {
            quizManager.AdvanceInstruction();
            return;
        }

        // デバッグ用：前の問題
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            quizManager.DebugPreviousQuestion();
            return;
        }

        // デバッグ用：次の問題
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            quizManager.DebugNextQuestion();
            return;
        }

        // デバッグ：手持ち・口元・噛む/開く信号を順番に模擬
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            quizManager.AdvanceInstruction();
            return;
        }

        // デバッグ：デバイスが完全にしぼんだ信号を模擬
        if (Keyboard.current.fKey.wasPressedThisFrame)
        {
            quizManager.NotifyDeviceFullyDeflated();
            return;
        }

        if (enterPressed)
        {
            quizManager.ContinueAfterFeedback();
            return;
        }

        // 仮：左の食品を食べた
        if (Keyboard.current.aKey.wasPressedThisFrame)
        {
            quizManager.AnswerLeft();
            return;
        }

        // 仮：右の食品を食べた
        if (Keyboard.current.dKey.wasPressedThisFrame)
        {
            quizManager.AnswerRight();
        }
    }

    private void HandleResultInput()
    {
        bool enterPressed =
            Keyboard.current.enterKey.wasPressedThisFrame ||
            Keyboard.current.numpadEnterKey.wasPressedThisFrame;

        if (enterPressed)
        {
            ReturnToSetup();
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

        if (bgmSource != null && !bgmSource.isPlaying)
        {
            bgmSource.Play();
        }

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
