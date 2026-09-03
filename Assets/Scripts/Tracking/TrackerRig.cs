using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace MealBeBack.Tracking
{
    /// <summary>
    /// シーンに 1 つ存在し、全 Vive トラッカーの「pose アクション」を保持する中枢。
    ///
    /// OpenXR では pose にバインドされた有効な InputAction が無いとランタイムが
    /// トラッカーを locate しない。ここで全ロール分のアクションを 1 度だけ張って
    /// Enable し、各 follower はロールを指定して pose を受け取るだけにする。
    ///
    /// - デバイス名に依存しない (ロール = usage で解決)
    /// - isTracked を見て「本物のトラッカーが刺さっているロール」だけ pose を返す
    /// - ホットプラグは InputSystem.onDeviceChange + 定期再走査で拾う
    /// - シーンに置き忘れても follower 側が EnsureExists() で自動生成する
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TrackerRig : MonoBehaviour
    {
        public static TrackerRig Instance { get; private set; }

        [Tooltip("トラッキング空間の原点 (XR Origin)。未設定なら自動検索。")]
        [SerializeField] private Transform trackingOrigin;

        [Tooltip("デバイス再走査の間隔 (秒)")]
        [SerializeField] private float rescanInterval = 1f;

        [Tooltip("解決状況をログに出す")]
        [SerializeField] private bool verbose = true;

        private sealed class Slot
        {
            public InputDevice device;
            public InputAction poseAction;
            public ButtonControl isTracked;
            public Vector3Control position;
            public QuaternionControl rotation;
            public bool wasTracked;
        }

        private readonly Dictionary<TrackerRole, Slot> _slots = new();
        private float _nextScan;

        public Transform TrackingOrigin => trackingOrigin;

        public static TrackerRig EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("TrackerRig (auto)");
            return go.AddComponent<TrackerRig>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            if (trackingOrigin == null)
            {
                var xr = GameObject.Find("XR Origin (XR Rig)")
                         ?? GameObject.Find("XR Origin")
                         ?? GameObject.Find("XR Rig");
                if (xr != null) trackingOrigin = xr.transform;
            }
        }

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            Rescan();
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            foreach (var s in _slots.Values) s.poseAction?.Dispose();
            _slots.Clear();
            if (Instance == this) Instance = null;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change) => _nextScan = 0f;

        private void Update()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + rescanInterval;
                Rescan();
            }

            if (!verbose) return;

            // isTracked の遷移だけを出す。どのロールに実トラッカーが居るかが分かる。
            foreach (var kv in _slots)
            {
                bool now = IsTracked(kv.Key);
                if (now == kv.Value.wasTracked) continue;
                kv.Value.wasTracked = now;

                if (now)
                {
                    int st = kv.Value.device.TryGetChildControl<IntegerControl>("trackingState")?.ReadValue() ?? -1;
                    Vector3 p = kv.Value.position != null ? kv.Value.position.ReadValue() : Vector3.zero;
                    Debug.Log($"[TrackerRig] ★ {kv.Key}: トラッキング開始 (trackingState={st}, pos={p:F2})");
                }
                else
                {
                    Debug.Log($"[TrackerRig] {kv.Key}: ロスト");
                }
            }
        }

        private void Rescan()
        {
            foreach (var d in InputSystem.devices)
            {
                var pose = d.TryGetChildControl<InputControl>("devicePose");
                var posC = d.TryGetChildControl<Vector3Control>("devicePosition");
                if (pose == null && posC == null) continue;

                foreach (var role in TrackerRoleExtensions.All)
                {
                    string usage = role.ToUsage();
                    if (usage == null || !HasUsage(d, usage)) continue;

                    if (_slots.TryGetValue(role, out var existing) && existing.device == d)
                        break; // 既に同じデバイスで解決済み

                    existing?.poseAction?.Dispose();

                    // devicePose を優先。読み値ではなく "Enable されている" ことが
                    // ランタイムに locate させる条件なので、バインド先の型は問わない。
                    string bindingPath = (pose ?? (InputControl)posC).path;
                    var action = new InputAction($"pose::{role}", InputActionType.Value, bindingPath);
                    action.Enable();

                    _slots[role] = new Slot
                    {
                        device = d,
                        poseAction = action,
                        isTracked = d.TryGetChildControl<ButtonControl>("isTracked"),
                        position = posC,
                        rotation = d.TryGetChildControl<QuaternionControl>("deviceRotation"),
                    };

                    if (verbose)
                        Debug.Log($"[TrackerRig] {role} <- {d.name}  ({bindingPath})");
                    break;
                }
            }
        }

        private static bool HasUsage(InputDevice d, string usage)
        {
            foreach (var u in d.usages)
                if (u == usage) return true;
            return false;
        }

        /// <summary>
        /// 指定ロールのトラッカーが今 Track 出来ているか
        /// (デバイスは在るが物理トラッカー未割り当ての「幽霊枠」は false)。
        /// </summary>
        public bool IsTracked(TrackerRole role)
        {
            if (role == TrackerRole.None) return false;
            if (!_slots.TryGetValue(role, out var s) || s.position == null) return false;
            return s.isTracked == null || s.isTracked.isPressed;
        }

        /// <summary>
        /// 指定ロールのトラッカーの現在姿勢をワールド座標で取得する。
        /// Track 出来ていなければ false。
        /// </summary>
        public bool TryGetPose(TrackerRole role, out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = default;
            worldRot = Quaternion.identity;

            if (!IsTracked(role)) return false;
            var s = _slots[role];

            Vector3 local = s.position.ReadValue();
            Quaternion localRot = s.rotation != null ? s.rotation.ReadValue() : Quaternion.identity;

            if (trackingOrigin != null)
            {
                worldPos = trackingOrigin.TransformPoint(local);
                worldRot = trackingOrigin.rotation * localRot;
            }
            else
            {
                worldPos = local;
                worldRot = localRot;
            }
            return true;
        }

        /// <summary>ロールのトラッカーとワールド座標 point の距離 (m)。未トラッキングなら +∞。</summary>
        public float Distance(TrackerRole role, Vector3 point) =>
            TryGetPose(role, out var p, out _) ? Vector3.Distance(p, point) : float.PositiveInfinity;
    }
}
