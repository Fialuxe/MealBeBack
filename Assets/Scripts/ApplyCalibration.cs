using UnityEngine;

public class ApplyCalibration : MonoBehaviour
{
    [SerializeField]
    private Transform target;

    private void Start()
    {
        if (!CalibrationData.HasCalibration)
        {
            Debug.LogWarning("[Calibration] キャリブレーション情報がありません");
            return;
        }

        if (target == null)
        {
            Debug.LogWarning("[Calibration] Target が設定されていません");
            return;
        }

        float yaw = CalibrationData.HeadYaw;

        Vector3 rotation = target.eulerAngles;
        rotation.y += yaw;

        target.eulerAngles = rotation;

        Debug.Log($"[Calibration] TargetをY={yaw}度補正しました");
    }
}