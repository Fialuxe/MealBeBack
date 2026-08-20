using UnityEngine;

public static class CalibrationData
{
    public static float HeadYaw { get; set; } = 0f;

    public static Vector3 HeadPosition { get; set; } = Vector3.zero;

    public static bool HasCalibration { get; set; } = false;

    // 机の高さキャリブレーション
    public static float TableHeight { get; set; } = 0f;

    public static bool HasTableHeightCalibration { get; set; } = false;
}