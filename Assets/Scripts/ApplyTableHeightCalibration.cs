using UnityEngine;

public class ApplyTableHeightCalibration : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Transform trackingSpace;

    [SerializeField]
    private Transform tableTopCalibrationPoint;

    [SerializeField]
    private Transform heightAdjustmentRoot;

    [Header("Venue Fine Adjustment")]
    [SerializeField]
    private float heightOffset = 0f;

    private void Start()
    {
        Debug.Log("[TableCalibration] WaterSceneで高さ適用を開始します");
        ApplyHeight();
    }

    private void ApplyHeight()
    {
        if (!CalibrationData.HasTableHeightCalibration)
        {
            Debug.LogWarning(
                "[TableCalibration] 保存された机高さがありません"
            );

            return;
        }

        if (trackingSpace == null ||
            tableTopCalibrationPoint == null ||
            heightAdjustmentRoot == null)
        {
            Debug.LogWarning(
                "[TableCalibration] Inspectorの参照が不足しています"
            );

            return;
        }

        // VR机天板の現在位置をTrackingSpace基準へ変換
        Vector3 currentTableLocalPosition =
            trackingSpace.InverseTransformPoint(
                tableTopCalibrationPoint.position
            );

        float targetHeight =
            CalibrationData.TableHeight + heightOffset;

        float heightDifference =
            targetHeight - currentTableLocalPosition.y;

        // 今回はY方向だけ補正
        heightAdjustmentRoot.position +=
            Vector3.up * heightDifference;

        Debug.Log(
            $"[TableCalibration] " +
            $"保存高さ = {CalibrationData.TableHeight:F3} m, " +
            $"VR机高さ = {currentTableLocalPosition.y:F3} m, " +
            $"補正 = {heightDifference:F3} m"
        );
    }
}