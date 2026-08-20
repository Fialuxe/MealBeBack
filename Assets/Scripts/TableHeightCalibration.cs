using UnityEngine;

public class TableHeightCalibration : MonoBehaviour
{
    [Header("Tracking")]
    [SerializeField]
    private Transform trackingSpace;

    [SerializeField]
    private Transform tableTouchPoint;

    [Header("Calibration Offset")]
    [Tooltip("測定点から実際の机表面までの高さ補正")]
    [SerializeField]
    private float touchPointHeightOffset = 0f;

    public bool SaveTableHeight()
    {
        if (trackingSpace == null)
        {
            Debug.LogWarning(
                "[TableCalibration] Tracking Space が設定されていません"
            );

            return false;
        }

        if (tableTouchPoint == null)
        {
            Debug.LogWarning(
                "[TableCalibration] Table Touch Point が設定されていません"
            );

            return false;
        }

        Vector3 localPosition =
            trackingSpace.InverseTransformPoint(
                tableTouchPoint.position
            );

        CalibrationData.TableHeight =
            localPosition.y + touchPointHeightOffset;

        CalibrationData.HasTableHeightCalibration = true;

        Debug.Log(
            $"[TableCalibration] " +
            $"測定点Y = {localPosition.y:F3} m, " +
            $"Offset = {touchPointHeightOffset:F3} m, " +
            $"机高さ = {CalibrationData.TableHeight:F3} m"
        );

        return true;
    }
}