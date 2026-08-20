using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;

public class SetupSceneController : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField]
    private string gameSceneName = "WaterScene";

    [Header("UI")]
    [SerializeField]
    private TMP_Text preparationText;

    [Header("Head Calibration")]
    [SerializeField]
    private Transform headTransform;

    [Header("Table Height Calibration")]
    [SerializeField]
    private TableHeightCalibration tableHeightCalibration;

    private int phase = 0;
    private bool isLoading = false;

    private void Start()
    {
        ShowPreparationMessage();
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

        if (phase == 0)
        {
            phase = 1;

            ShowTableCalibrationMessage();
        }
        else if (phase == 1)
        {
            SaveTableHeightCalibration();

            phase = 2;

            ShowHeadCalibrationMessage();
        }
        else if (phase == 2)
        {
            SaveHeadCalibration();

            StartGame();
        }
    }

    private void ShowPreparationMessage()
    {
        if (preparationText == null)
            return;

        preparationText.text =
            "\n\n準備中です\n\n" +
            "そのままお待ちください";
    }

    private void ShowTableCalibrationMessage()
    {
        if (preparationText == null)
            return;

        preparationText.text =
            "\n\n机の上面に指先を触れてください\n\n" +
            "そのまま姿勢を保ってください";
    }

    private void ShowHeadCalibrationMessage()
    {
        if (preparationText == null)
            return;

        preparationText.text =
            "\n\n正面を向いてください\n\n" +
            "そのまま姿勢を保ってください";
    }

    private void SaveTableHeightCalibration()
    {
        if (tableHeightCalibration == null)
        {
            Debug.LogWarning(
                "[SetupScene] Table Height Calibration が設定されていません"
            );

            return;
        }

        tableHeightCalibration.SaveTableHeight();
    }

    private void SaveHeadCalibration()
    {
        if (headTransform == null)
        {
            Debug.LogWarning(
                "[Calibration] Head Transform が設定されていません"
            );

            return;
        }

        CalibrationData.HeadYaw =
            headTransform.eulerAngles.y;

        CalibrationData.HeadPosition =
            headTransform.position;

        CalibrationData.HasCalibration = true;

        Debug.Log(
            $"[Calibration] " +
            $"Head Position = {CalibrationData.HeadPosition}, " +
            $"Head Yaw = {CalibrationData.HeadYaw}"
        );
    }

    private void StartGame()
    {
        isLoading = true;

        Debug.Log(
            "[SetupScene] WaterSceneへ移動します"
        );

        SceneManager.LoadScene(gameSceneName);
    }
}