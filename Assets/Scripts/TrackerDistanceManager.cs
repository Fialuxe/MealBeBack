using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class TrackerDistanceManager : MonoBehaviour
{
    [Header("トラッカー名 (Input System 認識名)")]
    [SerializeField] private string trackerNameA;
    [SerializeField] private string trackerNameB;

    [Header("判定距離")]
    [Tooltip("この距離 (m) 以下であればトラッカーが選択範囲内とみなす。")]
    [SerializeField] private float selectedDistance = 0.5f;

    [Header("XR Origin")]
    [SerializeField] private GameObject xrOrigin;

    private void Start()
    {
        if (xrOrigin == null)
        {
            xrOrigin = GameObject.Find("XR Origin (XR Rig)");
        }

        if (xrOrigin == null)
        {
            Debug.LogError("[TrackerDistanceManager] XR Origin が見つかりません。");
        }
    }

    /// <summary>
    /// InputSystem 上で deviceName と一致するデバイスと xrOrigin との距離 (m) を返す。
    /// デバイスが見つからない場合は float.PositiveInfinity を返す。
    /// </summary>
    public float DistanceToCamera(string deviceName)
    {
        if (xrOrigin == null || string.IsNullOrEmpty(deviceName))
        {
            return float.PositiveInfinity;
        }

        foreach (var device in InputSystem.devices)
        {
            if (device.name != deviceName) continue;

            var posControl = device.TryGetChildControl<Vector3Control>("devicePosition");
            if (posControl == null) continue;

            Vector3 worldPos = xrOrigin.transform.TransformPoint(posControl.ReadValue());
            return Vector3.Distance(worldPos, xrOrigin.transform.position);
        }

        return float.PositiveInfinity;
    }

    /// <summary>
    /// trackerNameA / trackerNameB のうち xrOrigin に近い方のデバイス名を返す。
    /// 両方とも見つからない場合は string.Empty を返す(呼び出し側の null チェック漏れを避けるため null は返さない)。
    /// </summary>
    public string GetSelectedDeviceName()
    {
        float distA = DistanceToCamera(trackerNameA);
        float distB = DistanceToCamera(trackerNameB);

        if (float.IsPositiveInfinity(distA) && float.IsPositiveInfinity(distB))
        {
            return string.Empty;
        }

        return distA <= distB ? trackerNameA : trackerNameB;
    }

    /// <summary>
    /// deviceName のデバイスと xrOrigin との距離が selectedDistance 以下かどうか。
    /// </summary>
    public bool IsDeviceWithinArea(string deviceName)
    {
        return DistanceToCamera(deviceName) <= selectedDistance;
    }
}
