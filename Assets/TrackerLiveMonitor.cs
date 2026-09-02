using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// トラッカー系 InputSystem デバイス（VIVE Ultimate Tracker のダミーを含む）を
/// 毎フレーム走査し、「どれが今トラッキングできているか」を画面と Console に出す開発用ツール。
///
/// ・キー入力不要。空の GameObject に付けて Play するだけ。
/// ・`devicePosition` を持つデバイス = トラッカー扱い。名前でフィルタしたいときは
///   <see cref="nameFilter"/>（部分一致、空なら全部）。
/// ・isTracked が false→true / true→false に変わったときだけ Console にログ（スパムしない）。
/// ・生きているデバイス名が分かったら <c>ViveTrackerFollower.deviceName</c> にそれを入れる。
/// </summary>
public class TrackerLiveMonitor : MonoBehaviour
{
    [Tooltip("この文字列を名前に含むデバイスだけ表示（空なら devicePosition を持つ全デバイス）。")]
    [SerializeField] private string nameFilter = "Tracker";

    [Tooltip("画面オーバーレイを出す。")]
    [SerializeField] private bool showOverlay = true;

    [Tooltip("状態が変わったときに Console へログする。")]
    [SerializeField] private bool logChanges = true;

    private readonly StringBuilder _sb = new StringBuilder(512);
    // デバイス名 -> 直近の isTracked。変化検出用。
    private readonly System.Collections.Generic.Dictionary<string, bool> _lastTracked =
        new System.Collections.Generic.Dictionary<string, bool>();

    private string _overlayText = "(scanning...)";

    private void Update()
    {
        _sb.Clear();
        int shown = 0;
        string aliveName = null;

        foreach (InputDevice d in InputSystem.devices)
        {
            var pos = d.TryGetChildControl<Vector3Control>("devicePosition");
            if (pos == null)
                continue;

            if (!string.IsNullOrEmpty(nameFilter) &&
                d.name.IndexOf(nameFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var trackedCtrl = d.TryGetChildControl<ButtonControl>("isTracked");
            var stateCtrl = d.TryGetChildControl<IntegerControl>("trackingState");

            bool isTracked = trackedCtrl != null && trackedCtrl.isPressed;
            Vector3 p = pos.ReadValue();
            int state = stateCtrl != null ? stateCtrl.ReadValue() : -1;

            // 「生きている」= isTracked。isTracked コントロールが無い機種向けに
            // pos が非ゼロなことも併記する。
            bool alive = isTracked || p.sqrMagnitude > 1e-6f;
            if (alive && aliveName == null)
                aliveName = d.name;

            shown++;
            _sb.AppendLine(
                $"{(alive ? "●" : "○")} {d.name}\n" +
                $"    added={d.added} isTracked={isTracked} state={state} pos={p:F2}");

            if (logChanges)
            {
                bool had = _lastTracked.TryGetValue(d.name, out bool prev);
                if (!had || prev != isTracked)
                {
                    _lastTracked[d.name] = isTracked;
                    Debug.Log($"[TrackerLiveMonitor] {d.name} isTracked {(had ? prev.ToString() : "-")} -> {isTracked} (pos={p:F2})");
                }
            }
        }

        if (shown == 0)
            _sb.AppendLine($"(devicePosition を持つデバイスなし / filter=\"{nameFilter}\")");
        else if (aliveName != null)
            _sb.AppendLine($"\n=> 生きている: {aliveName}");
        else
            _sb.AppendLine("\n=> 生きているデバイスなし");

        _overlayText = _sb.ToString();
    }

    private void OnGUI()
    {
        if (!showOverlay)
            return;

        var style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 13,
            richText = false,
        };

        GUI.Label(new Rect(10, 10, 560, 320), _overlayText, style);
    }
}
