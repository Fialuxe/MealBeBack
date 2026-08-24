using UnityEngine;
using Valve.VR;
using System.Text;

public class ViveTrackerDirect : MonoBehaviour
{
    [SerializeField] private string targetSerial = "";  // 空なら最初に見つかったトラッカー
    private CVRSystem vrSystem;
    private uint deviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
    private TrackedDevicePose_t[] poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

    void Start()
    {
        var err = EVRInitError.None;
        vrSystem = OpenVR.Init(ref err, EVRApplicationType.VRApplication_Background);
        if (err != EVRInitError.None)
        {
            Debug.LogError($"OpenVR init failed: {err}");
            return;
        }
        ScanDevices();
    }

    void ScanDevices()
    {
        for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
        {
            if (vrSystem.GetTrackedDeviceClass(i) != ETrackedDeviceClass.GenericTracker) continue;

            var e = ETrackedPropertyError.TrackedProp_Success;
            var sb = new StringBuilder(64);
            vrSystem.GetStringTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_SerialNumber_String, sb, 64, ref e);
            Debug.Log($"Tracker found: index={i} serial={sb}");

            if (string.IsNullOrEmpty(targetSerial) || sb.ToString() == targetSerial)
            {
                deviceIndex = i;
                return;
            }
        }
        Debug.LogWarning("No tracker found.");
    }

    void Update()
    {
        if (vrSystem == null) return;
        if (deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid) { ScanDevices(); return; }

        vrSystem.GetDeviceToAbsoluteTrackingPose(
            ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);

        if (!poses[deviceIndex].bPoseIsValid) return;

        var rt = new SteamVR_Utils.RigidTransform(poses[deviceIndex].mDeviceToAbsoluteTracking);
        transform.localPosition = rt.pos;
        transform.localRotation = rt.rot;
    }

    void OnDestroy()
    {
        OpenVR.Shutdown();
    }
}