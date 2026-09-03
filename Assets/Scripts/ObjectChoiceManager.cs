using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// 位置ベースのデバイス・オブジェクト選択マネージャー（#45 ②）
///
/// ViveTrackerFollower（VIVE Ultimate Tracker）の位置情報をもとに、
/// カメラから一定距離以内の近い方のデバイス/オブジェクトを選択状態にする。
///
/// 確認事項の前提:
/// ・QuizManager.NotifyDeviceSelected / AnswerLeft / AnswerRight は咀嚼開始まで選択切り替え対応済み
/// ・ViveTrackerFollower は左右 2 個（Ultimate Tracker）が GameObjectにアタッチされている
/// ・AnswerLeft / AnswerRight で使用するオブジェクトと
///   ViveTrackerFollower の GameObject は紐づけられている（同じGameObjectまたは親階層）
/// ・XROrigin（CameraOffset のカメラ）がシーンに存在する
/// </summary>
public class ObjectChoiceManager : MonoBehaviour
{
    [Header("トラッカー/オブジェクト参照")]
    [SerializeField]
    private ViveTrackerFollower trackerLeft;

    [SerializeField]
    private ViveTrackerFollower trackerRight;

    [SerializeField]
    private GameObject objectLeft;

    [SerializeField]
    private GameObject objectRight;

    [Header("カメラ参照")]
    [Tooltip("XROrigin の Camera。通常は Main Camera。")]
    [SerializeField]
    private GameObject xrOrigin;

    [Header("距離制御")]
    [Tooltip("この距離 (m) 以下にあるトラッカーを選択候補にする。")]
    [SerializeField]
    private float selectedDistance = 0.5f;

    private QuizManager quizManager;
    private GameObject selectedDevice = null;

    private void Start()
    {
        if (xrOrigin == null)
        {
            xrOrigin = GameObject.Find("XR Origin (XR Rig)");
        }

        quizManager = FindAnyObjectByType<QuizManager>();

        if (trackerLeft == null || trackerRight == null)
        {
            Debug.LogError(
                "[ObjectChoiceManager] トラッカーが設定されていません。"
            );
        }

        if (objectLeft == null || objectRight == null)
        {
            Debug.LogWarning(
                "[ObjectChoiceManager] オブジェクトが設定されていません。"
            );
        }

        if (xrOrigin == null)
        {
            Debug.LogError(
                "[ObjectChoiceManager] XR カメラが見つかりません。"
            );
        }
    }

    private void Update()
    {
        if (quizManager != null && quizManager.IsQuizRunning)
        {
            SelectedObject();
        }
    }

    /// <summary>
    /// XROrigin（カメラ）の座標を取得する。
    /// </summary>
    public Vector3 GetCameraPosition()
    {
        if (xrOrigin == null)
        {
            Debug.LogWarning("[ObjectChoiceManager] Camera が null です。");
            return Vector3.zero;
        }

        return xrOrigin.transform.position;
    }

    /// <summary>
    /// 指定したゲームオブジェクト（トラッカー）とカメラ間の距離を計算する。
    /// </summary>
    public float DistanceDeviceToCamera(GameObject deviceObject)
    {
        if (deviceObject == null)
        {
            return float.MaxValue;
        }

        Vector3 cameraPos = GetCameraPosition();
        return Vector3.Distance(deviceObject.transform.position, cameraPos);
    }

    /// <summary>
    /// selectedDistance 以下のデバイスのうち、カメラに最も近い方を
    /// selectedDevice として選択し、対応する Answer を通知する。
    /// 該当デバイスがなければ selectedDevice = null。
    /// </summary>
    public void SelectedObject()
    {
        if (xrOrigin == null)
        {
            return;
        }

        float distLeft = DistanceDeviceToCamera(trackerLeft?.gameObject);
        float distRight = DistanceDeviceToCamera(trackerRight?.gameObject);

        bool isLeftValid = distLeft <= selectedDistance;
        bool isRightValid = distRight <= selectedDistance;

        GameObject newSelected = null;
        SerialSystem.SerialDevice newDevice = SerialSystem.SerialDevice.None;

        if (isLeftValid && isRightValid)
        {
            if (distLeft <= distRight)
            {
                newSelected = objectLeft;
                newDevice = SerialSystem.SerialDevice.A;
            }
            else
            {
                newSelected = objectRight;
                newDevice = SerialSystem.SerialDevice.B;
            }
        }
        else if (isLeftValid)
        {
            newSelected = objectLeft;
            newDevice = SerialSystem.SerialDevice.A;
        }
        else if (isRightValid)
        {
            newSelected = objectRight;
            newDevice = SerialSystem.SerialDevice.B;
        }
        else
        {
            newSelected = null;
            newDevice = SerialSystem.SerialDevice.None;
        }

        if (newSelected != selectedDevice)
        {
            selectedDevice = newSelected;

            if (newDevice != SerialSystem.SerialDevice.None && quizManager != null)
            {
                quizManager.NotifyDeviceSelected(newDevice);

                if (newDevice == SerialSystem.SerialDevice.A)
                {
                    quizManager.AnswerLeft();
                }
                else if (newDevice == SerialSystem.SerialDevice.B)
                {
                    quizManager.AnswerRight();
                }

                Debug.Log(
                    $"[ObjectChoiceManager] デバイス/オブジェクト選択: " +
                    $"{(newDevice == SerialSystem.SerialDevice.A ? "左 (A)" : "右 (B)")} " +
                    $"距離 = {(newDevice == SerialSystem.SerialDevice.A ? distLeft : distRight):F3} m"
                );
            }
        }
    }

    /// <summary>
    /// 現在選択されているオブジェクトを返す。
    /// </summary>
    public GameObject GetSelectedObject()
    {
        return selectedDevice;
    }
}
