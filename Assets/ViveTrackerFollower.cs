using UnityEngine;
using UnityEngine.InputSystem;

public class ViveTrackerFollower : MonoBehaviour
{
    [SerializeField] private InputActionReference positionAction;
    [SerializeField] private InputActionReference rotationAction;

    void OnEnable()
    {
        positionAction?.action.Enable();
        rotationAction?.action.Enable();
    }

    void OnDisable()
    {
        positionAction?.action.Disable();
        rotationAction?.action.Disable();
    }

    void Update()
    {
        if (positionAction != null && positionAction.action.enabled)
            transform.localPosition = positionAction.action.ReadValue<Vector3>();

        if (rotationAction != null && rotationAction.action.enabled)
            transform.localRotation = rotationAction.action.ReadValue<Quaternion>();
    }
}