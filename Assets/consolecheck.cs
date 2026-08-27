using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class consolecheck : MonoBehaviour
{
    void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.spaceKey.wasPressedThisFrame) return;

        foreach (var d in InputSystem.devices)
        {
            var tracked = d.TryGetChildControl<ButtonControl>("isTracked");
            var state   = d.TryGetChildControl<IntegerControl>("trackingState");
            var pos     = d.TryGetChildControl<Vector3Control>("devicePosition");

            if (tracked == null && state == null && pos == null) continue;

            Debug.Log($"{d.name} | added={d.added} | isTracked={tracked?.ReadValue()} " +
                      $"| state={state?.ReadValue()} | pos={pos?.ReadValue()}");
        }
    }
}