using System;

namespace MealBeBack.Tracking
{
    /// <summary>
    /// Vive トラッカーのロール。値は HTCViveTrackerProfile の
    /// commonUsages / TrackerUserPaths と 1:1 で対応する。
    /// デバイス名 (HTCViveTrackerOpenXR8 等) ではなくロールで参照するための型。
    /// </summary>
    public enum TrackerRole
    {
        None = 0,
        LeftFoot,
        RightFoot,
        LeftShoulder,
        RightShoulder,
        LeftElbow,
        RightElbow,
        LeftKnee,
        RightKnee,
        Waist,
        Chest,
        Camera,
        Keyboard,
    }

    public static class TrackerRoleExtensions
    {
        /// <summary>
        /// InputSystem のデバイス usage 文字列。
        /// HTCViveTrackerProfile.XRViveTracker の commonUsages と一致させること。
        /// </summary>
        public static string ToUsage(this TrackerRole role) => role switch
        {
            TrackerRole.LeftFoot      => "Left Foot",
            TrackerRole.RightFoot     => "Right Foot",
            TrackerRole.LeftShoulder  => "Left Shoulder",
            TrackerRole.RightShoulder => "Right Shoulder",
            TrackerRole.LeftElbow     => "Left Elbow",
            TrackerRole.RightElbow    => "Right Elbow",
            TrackerRole.LeftKnee      => "Left Knee",
            TrackerRole.RightKnee     => "Right Knee",
            TrackerRole.Waist         => "Waist",
            TrackerRole.Chest         => "Chest",
            TrackerRole.Camera        => "Camera",
            TrackerRole.Keyboard      => "Keyboard",
            _ => null,
        };

        internal static readonly TrackerRole[] All =
            (TrackerRole[])Enum.GetValues(typeof(TrackerRole));
    }
}
