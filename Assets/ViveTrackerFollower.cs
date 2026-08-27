using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class ViveTrackerFollower : MonoBehaviour
{
    [SerializeField] private string deviceName = "VIVEUltimateTracker0";

    private Vector3Control posControl;
    private QuaternionControl rotControl;
    private ButtonControl trackedControl;

    void FindDevice()
    {
        foreach (var d in InputSystem.devices)
        {
            if (d.name != deviceName) continue;
            posControl     = d.TryGetChildControl<Vector3Control>("devicePosition");
            rotControl     = d.TryGetChildControl<QuaternionControl>("deviceRotation");
            trackedControl = d.TryGetChildControl<ButtonControl>("isTracked");
            return;
        }
    }

   void Update()
{
    if (posControl == null)
    {
        FindDevice();
        Debug.Log($"posControl null -> FindDevice. found={(posControl != null)}");
        return;
    }

    var p = posControl.ReadValue();
    bool tracked = trackedControl != null && trackedControl.isPressed;
    Debug.Log($"tracked={tracked} read={p:F3} local={transform.localPosition:F3} world={transform.position:F3}");

    transform.localPosition = p;
    if (rotControl != null)
        transform.localRotation = rotControl.ReadValue();
}
}