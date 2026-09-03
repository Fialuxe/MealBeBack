using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

/// <summary>
/// トラッカーを「ロール(usage)」で特定できる環境かどうかを実機で確認するための調査スクリプト。
/// Space キーで、現在 InputSystem が見ているトラッキング系デバイスの
///   - name / layout / product / serial
///   - usages          ← "Left Foot" などロール名が出ればロール特定が可能
///   - characteristics  ← usage が空でもロール bit が立っていれば判定に使える
/// をすべて Console へ出す。
/// 追加で「ロール usage からデバイスを引けるか」も試して結果を出す。
/// </summary>
public class consolecheck : MonoBehaviour
{
    // HTCViveTrackerProfile.InputDeviceTrackerCharacteristics のカスタム bit。
    // usage が付いていなくても、この bit を見ればロールが分かる場合がある。
    private static readonly (uint bit, string name)[] TrackerRoleBits =
    {
        (0x1000u,   "LeftFoot"),
        (0x2000u,   "RightFoot"),
        (0x4000u,   "LeftShoulder"),
        (0x8000u,   "RightShoulder"),
        (0x10000u,  "LeftElbow"),
        (0x20000u,  "RightElbow"),
        (0x40000u,  "LeftKnee"),
        (0x80000u,  "RightKnee"),
        (0x100000u, "Waist"),
        (0x200000u, "Chest"),
        (0x400000u, "Camera"),
        (0x800000u, "Keyboard"),
    };

    // ロール特定を試すときに探す usage 候補。
    private static readonly string[] RoleUsageCandidates =
    {
        "Left Foot", "Right Foot", "Waist", "Chest",
        "Left Shoulder", "Right Shoulder",
        "Left Elbow", "Right Elbow", "Left Knee", "Right Knee",
        "Ultimate Tracker 0", "Ultimate Tracker 1", "Ultimate Tracker 2",
    };

    [Tooltip("true なら毎フレーム自動でダンプ(ログが多いので通常は Space 手動)")]
    [SerializeField] private bool dumpEveryFrame = false;

    private void Update()
    {
        bool trigger =
            dumpEveryFrame ||
            (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame);

        if (!trigger) return;

        DumpAllDevices();
        TryResolveByUsage();
    }

    private void DumpAllDevices()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== InputSystem device dump ==========");

        int trackedCount = 0;

        foreach (var d in InputSystem.devices)
        {
            var tracked = d.TryGetChildControl<ButtonControl>("isTracked");
            var state   = d.TryGetChildControl<IntegerControl>("trackingState");
            var pos     = d.TryGetChildControl<Vector3Control>("devicePosition");

            // トラッキング系のコントロールを持つデバイスだけ対象
            if (tracked == null && state == null && pos == null) continue;

            trackedCount++;

            sb.AppendLine("--------------------------------------------");
            sb.AppendLine($"name         : {d.name}");
            sb.AppendLine($"displayName  : {d.displayName}");
            sb.AppendLine($"layout       : {d.layout}");
            sb.AppendLine($"deviceId     : {d.deviceId}  added={d.added} enabled={d.enabled}");
            sb.AppendLine($"product      : {d.description.product}");
            sb.AppendLine($"manufacturer : {d.description.manufacturer}");
            sb.AppendLine($"serial       : {d.description.serial}");
            sb.AppendLine($"interface    : {d.description.interfaceName}  deviceClass={d.description.deviceClass}");

            string usages = d.usages.Count == 0 ? "(なし)" : string.Join(" | ", d.usages);
            sb.AppendLine($"usages       : {usages}   <-- ロール名(\"Left Foot\"等)が出ればロール特定OK");

            // capabilities(XRDeviceDescriptor) から characteristics を取り出す
            TryDumpCharacteristics(d, sb);

            sb.AppendLine($"isTracked    : {tracked?.ReadValue()}");
            sb.AppendLine($"trackingState: {state?.ReadValue()}");
            sb.AppendLine($"devicePos    : {pos?.ReadValue()}");

            // 生 description(取りこぼし防止)
            sb.AppendLine($"description  : {d.description.ToJson()}");
        }

        if (trackedCount == 0)
            sb.AppendLine("トラッキング系デバイスが1つも見つかりません(トラッカー未接続/未認識)。");

        sb.AppendLine("============================================");
        Debug.Log(sb.ToString());
    }

    private static void TryDumpCharacteristics(InputDevice d, StringBuilder sb)
    {
        string caps = d.description.capabilities;
        if (string.IsNullOrEmpty(caps))
        {
            sb.AppendLine("characteristics: (capabilities 空)");
            return;
        }

        try
        {
            var desc = XRDeviceDescriptor.FromJson(caps);
            if (desc == null)
            {
                sb.AppendLine("characteristics: (XRDeviceDescriptor 解析不可)");
                return;
            }

            uint c = (uint)desc.characteristics;
            sb.AppendLine($"characteristics: 0x{c:X}  ({desc.characteristics})");

            var roles = new StringBuilder();
            foreach (var (bit, name) in TrackerRoleBits)
                if ((c & bit) != 0) roles.Append(name).Append(' ');

            sb.AppendLine(roles.Length > 0
                ? $"  -> role bit    : {roles}   <-- usage が空でもこれでロール判定可能"
                : "  -> role bit    : (ロール bit なし)");
        }
        catch (System.Exception e)
        {
            sb.AppendLine($"characteristics: 解析例外 {e.GetType().Name}: {e.Message}");
        }
    }

    private void TryResolveByUsage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---------- usage からの逆引きテスト ----------");

        foreach (var usage in RoleUsageCandidates)
        {
            InputDevice hit = null;
            foreach (var d in InputSystem.devices)
            {
                if (d.TryGetChildControl<Vector3Control>("devicePosition") == null) continue;
                foreach (var u in d.usages)
                {
                    if (u == usage) { hit = d; break; }
                }
                if (hit != null) break;
            }

            if (hit != null)
                sb.AppendLine($"  usage \"{usage}\" -> {hit.name} (layout={hit.layout}, serial={hit.description.serial})");
        }

        if (sb.Length < 60)
            sb.AppendLine("  どの候補 usage にも一致せず。→ ロール未割り当て、または characteristics/serial で特定する必要あり。");

        sb.AppendLine("--------------------------------------------");
        Debug.Log(sb.ToString());
    }
}
