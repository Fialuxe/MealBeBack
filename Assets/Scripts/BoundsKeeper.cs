using UnityEngine;

/// <summary>
/// Steers a fish back toward the center of a box volume when it approaches the edge.
/// Added automatically by AquariumSceneSetup to BluefinSwim instances, which have
/// no built-in bounds support.
///
/// Works by applying an extra yaw rotation each frame; this is compatible with
/// BluefinSwim's incremental-rotation locomotion and does not conflict with banking.
/// </summary>
public class BoundsKeeper : MonoBehaviour
{
    [Tooltip("Center of the permitted swim area.")]
    public Vector3 boundsCenter = Vector3.zero;
    [Tooltip("Full extent of the permitted swim area.")]
    public Vector3 boundsSize = new Vector3(30f, 8f, 30f);
    [Tooltip("Fraction of each half-extent inside which the fish is free; beyond this it steers back.")]
    [Range(0f, 0.6f)] public float softEdgeFraction = 0.25f;
    [Tooltip("Maximum correction yaw speed in degrees/second.")]
    public float returnTurnSpeed = 55f;

    Vector3 _localForward = Vector3.right;   // BluefinSwim default (+X)

    void Start()
    {
        var bf = GetComponent<BluefinSwim>();
        if (bf != null)
            _localForward = LocalAxisToVector3(bf.forwardAxis);
    }

    void Update()
    {
        Vector3 offset = transform.position - boundsCenter;
        Vector3 softHalf = boundsSize * 0.5f * (1f - softEdgeFraction);

        bool nearEdge = Mathf.Abs(offset.x) > softHalf.x
                     || Mathf.Abs(offset.z) > softHalf.z
                     || Mathf.Abs(offset.y) > softHalf.y;

        if (!nearEdge) return;

        Vector3 toCenter = Vector3.ProjectOnPlane(boundsCenter - transform.position, Vector3.up);
        if (toCenter.sqrMagnitude < 0.001f) return;
        toCenter.Normalize();

        Vector3 fwd = Vector3.ProjectOnPlane(transform.TransformDirection(_localForward), Vector3.up);
        if (fwd.sqrMagnitude < 0.001f) return;
        fwd.Normalize();

        float angle = Vector3.SignedAngle(fwd, toCenter, Vector3.up);
        float step = Mathf.Clamp(angle, -returnTurnSpeed * Time.deltaTime, returnTurnSpeed * Time.deltaTime);
        transform.Rotate(Vector3.up * step, Space.World);
    }

    // ---------------------------------------------------------------
    static Vector3 LocalAxisToVector3(BluefinSwim.LocalAxis axis)
    {
        switch (axis)
        {
            case BluefinSwim.LocalAxis.PositiveX: return Vector3.right;
            case BluefinSwim.LocalAxis.NegativeX: return Vector3.left;
            case BluefinSwim.LocalAxis.PositiveY: return Vector3.up;
            case BluefinSwim.LocalAxis.NegativeY: return Vector3.down;
            case BluefinSwim.LocalAxis.PositiveZ: return Vector3.forward;
            default: return Vector3.back;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.15f, 0.30f);
        Gizmos.DrawWireCube(boundsCenter, boundsSize * (1f - softEdgeFraction));
    }
}
