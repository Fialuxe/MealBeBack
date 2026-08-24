using UnityEngine;
using Valve.VR;
using System.Text;

public class TrackerSerialFinder : MonoBehaviour
{
    void Start()
    {
        for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
        {
            var error = ETrackedPropertyError.TrackedProp_Success;
            var sb = new StringBuilder((int)OpenVR.k_unMaxPropertyStringSize);
            OpenVR.System.GetStringTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_SerialNumber_String, sb, OpenVR.k_unMaxPropertyStringSize, ref error);
            if (sb.Length > 0)
                Debug.Log($"Index {i}: {sb}");
        }
    }
}