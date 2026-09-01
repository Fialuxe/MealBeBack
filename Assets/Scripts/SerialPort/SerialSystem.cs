using System;
using UnityEngine;

/// <summary>
/// Arduino 制御デバイス (充填 / 吸引) を Unity 側から一元的に扱うファサード。
///
/// ・実際のシリアル送受信は <see cref="ArduinoSerialBridge"/>（専用スレッドで非ブロッキング）が担当する。
///   このクラスはプロトコルの組み立て / 解釈と、上位（QuizManager など）向け API だけを持つ。
/// ・プロトコルは <c>External/arduino_mbb_v1.ino</c> に準拠する。値はすべて「目標充填率」。
///     Unity → Arduino : "(&lt;コマンド&gt;,&lt;値&gt;)\n"
///       'f' = 中の量を 値% まで増やす（既に 値 以上なら何もしない）
///       's' = 中の量を 値% まで減らす（既に 値 以下なら何もしない）
///       'c' = モーターを回さず currentInside を 値% に設定（状態同期用）
///       'i' = 即停止（値は無視）
///       値   : 0-100 の整数。範囲外の f / s / c は送らずに破棄する。
///     Arduino → Unity : "(&lt;処理状態&gt;,&lt;currentInside&gt;)\n"
///       処理状態 : 1 = 駆動中 / 0 = 停止中
///       currentInside : 0-100（現在の充填率）
///
/// ・動作中に次のコマンドを送ると Arduino 側の currentInside がズレる。
///   駆動中 (<see cref="IsBusy"/>) の間は次を送らないこと。
///
/// ・デバイスは 2 台（<see cref="SerialDevice.A"/> = bridge1 / <see cref="SerialDevice.B"/> = bridge2）。
///   シーンに 1 つ置き、2 つの ArduinoSerialBridge を割り当てる。
/// </summary>
public class SerialSystem : MonoBehaviour
{
    /// <summary>対象デバイス。A = bridge1 / B = bridge2。</summary>
    public enum SerialDevice
    {
        None,
        A,
        B,
    }

    /// <summary>Arduino へ送る駆動モード。</summary>
    public enum DeviceMode
    {
        /// <summary>何もしない・全停止（率は無視される）。</summary>
        Stop,
        /// <summary>充填する。</summary>
        Fill,
        /// <summary>吸引する。</summary>
        Suck,
        /// <summary>モーターを回さず、Arduino 側の現在充填率だけを指定値へ合わせる（状態同期用）。</summary>
        Calibrate,
    }

    /// <summary>あるデバイスの最新状態。</summary>
    public struct DeviceStatus
    {
        /// <summary>Arduino から一度でも状態を受信したか。</summary>
        public bool hasData;
        /// <summary>駆動中（Arduino の処理状態 = 1）。</summary>
        public bool busy;
        /// <summary>現在の充填率 0-100（Arduino の currentInside）。</summary>
        public int fillPercent;
    }

    [Header("デバイス割り当て")]
    [Tooltip("SerialDevice.A に対応する ArduinoSerialBridge。")]
    [SerializeField]
    private ArduinoSerialBridge bridgeA;

    [Tooltip("SerialDevice.B に対応する ArduinoSerialBridge。未使用なら空でよい。")]
    [SerializeField]
    private ArduinoSerialBridge bridgeB;

    [Header("デバッグ")]
    [SerializeField]
    private bool logCommands = true;

    private readonly DeviceStatus[] _status = new DeviceStatus[2];

    /// <summary>デバイスが「駆動中 → 停止」へ遷移した瞬間に発火。</summary>
    public event Action<SerialDevice> OnDeviceIdle;

    /// <summary>
    /// デバイスが停止し、かつ充填率が 0 になった瞬間に発火。
    /// QuizManager の「デバイスが完全にしぼんだ」判定に使う。
    /// </summary>
    public event Action<SerialDevice> OnFullyDeflated;

    /// <summary>充填率が変化するたびに発火（デバイス, 新しい率）。</summary>
    public event Action<SerialDevice, int> OnFillPercentChanged;

    private void OnEnable()
    {
        if (bridgeA != null)
            bridgeA.OnLineReceived += HandleLineFromA;

        if (bridgeB != null)
            bridgeB.OnLineReceived += HandleLineFromB;
    }

    private void OnDisable()
    {
        if (bridgeA != null)
            bridgeA.OnLineReceived -= HandleLineFromA;

        if (bridgeB != null)
            bridgeB.OnLineReceived -= HandleLineFromB;
    }

    private void OnApplicationQuit()
    {
        // 終了時にモーターを止める。SendLine は非同期なので送信スレッドが
        // 実際に書き出すまで少しだけ待つ。
        StopAll();
        System.Threading.Thread.Sleep(50);
    }

    // ── QuizManager 向け API ─────────────────────────────────────────────────

    /// <summary>中の量を targetPercent まで増やす（0-100。既にそれ以上なら Arduino 側で無視）。</summary>
    public void Fill(SerialDevice device, int targetPercent)
    {
        SendCommand(device, DeviceMode.Fill, targetPercent);
    }

    /// <summary>中の量を targetPercent まで減らす（0-100。既にそれ以下なら Arduino 側で無視）。</summary>
    public void Suck(SerialDevice device, int targetPercent)
    {
        SendCommand(device, DeviceMode.Suck, targetPercent);
    }

    /// <summary>指定デバイスを即時停止する。</summary>
    public void Stop(SerialDevice device)
    {
        SendCommand(device, DeviceMode.Stop, 0);
    }

    /// <summary>
    /// モーターを回さず、Arduino が持つ現在充填率を <paramref name="percent"/> (0-100) に合わせる。
    /// 電源投入時の実量が既定 (100%) と違うときや、Unity 側の推定値と実機がずれたときに
    /// 状態を一致させるために使う。
    /// </summary>
    public void Calibrate(SerialDevice device, int percent)
    {
        SendCommand(device, DeviceMode.Calibrate, percent);
    }

    /// <summary>両デバイスを即時停止する。</summary>
    public void StopAll()
    {
        Stop(SerialDevice.A);
        Stop(SerialDevice.B);
    }

    /// <summary>指定デバイスの最新状態を返す。</summary>
    public DeviceStatus GetStatus(SerialDevice device)
    {
        int i = DeviceIndex(device);
        return i < 0 ? default : _status[i];
    }

    /// <summary>指定デバイスが駆動中か。</summary>
    public bool IsBusy(SerialDevice device)
    {
        int i = DeviceIndex(device);
        return i >= 0 && _status[i].busy;
    }

    /// <summary>指定デバイスのシリアルポートが開いているか。</summary>
    public bool IsConnected(SerialDevice device)
    {
        ArduinoSerialBridge b = BridgeOf(device);
        return b != null && b.IsConnected;
    }

    // ── 送信 ────────────────────────────────────────────────────────────────

    private void SendCommand(SerialDevice device, DeviceMode mode, int percent)
    {
        ArduinoSerialBridge bridge = BridgeOf(device);
        if (bridge == null)
        {
            Debug.LogWarning($"[SerialSystem] デバイス {device} に bridge が割り当てられていません。");
            return;
        }

        char stateChar;
        switch (mode)
        {
            case DeviceMode.Fill:
                if (percent < 0 || percent > 100)
                {
                    Debug.LogWarning($"[SerialSystem] Fill の率 {percent} が範囲外(0-100)。破棄します。");
                    return;
                }
                stateChar = 'f';
                break;

            case DeviceMode.Suck:
                if (percent < 0 || percent > 100)
                {
                    Debug.LogWarning($"[SerialSystem] Suck の率 {percent} が範囲外(0-100)。破棄します。");
                    return;
                }
                stateChar = 's';
                break;

            case DeviceMode.Calibrate:
                if (percent < 0 || percent > 100)
                {
                    Debug.LogWarning($"[SerialSystem] Calibrate の率 {percent} が範囲外(0-100)。破棄します。");
                    return;
                }
                stateChar = 'c';
                break;

            case DeviceMode.Stop:
            default:
                stateChar = 'i';
                percent = 0;
                break;
        }

        string payload = $"({stateChar},{percent})";
        bridge.SendLine(payload);

        if (logCommands)
            Debug.Log($"[SerialSystem] TX {device} -> \"{payload}\"");
    }

    // ── 受信 ────────────────────────────────────────────────────────────────

    private void HandleLineFromA(string line) => HandleLine(SerialDevice.A, line);
    private void HandleLineFromB(string line) => HandleLine(SerialDevice.B, line);

    // 期待フォーマット: "(<処理状態>,<currentInside>)" 。括弧・空白の揺れは許容する。
    private void HandleLine(SerialDevice device, string rawLine)
    {
        int index = DeviceIndex(device);
        if (index < 0 || string.IsNullOrEmpty(rawLine))
            return;

        string line = rawLine.Trim().Replace("(", string.Empty).Replace(")", string.Empty);

        int comma = line.IndexOf(',');
        if (comma < 0)
        {
            Debug.LogWarning($"[SerialSystem] {device} 不正な行を無視: \"{rawLine}\"");
            return;
        }

        if (!int.TryParse(line.Substring(0, comma).Trim(), out int processingState))
        {
            Debug.LogWarning($"[SerialSystem] {device} 処理状態をパースできません: \"{rawLine}\"");
            return;
        }

        if (!int.TryParse(line.Substring(comma + 1).Trim(), out int currentInside))
        {
            Debug.LogWarning($"[SerialSystem] {device} currentInside をパースできません: \"{rawLine}\"");
            return;
        }

        DeviceStatus prev = _status[index];

        // 処理状態: 1 = 駆動中 / 0 = 停止中。仕様外の値が来ても
        // 0 以外は駆動中として扱い、行は捨てない。
        bool nowBusy = processingState != 0;

        // 充填率が 0-100 の範囲外なら直前の値を維持する（範囲外は破棄する仕様）。
        int fillPercent = prev.fillPercent;
        if (currentInside < 0 || currentInside > 100)
        {
            Debug.LogWarning(
                $"[SerialSystem] {device} currentInside {currentInside} が範囲外(0-100)。直前値を維持。");
        }
        else
        {
            fillPercent = currentInside;
        }

        DeviceStatus next = new DeviceStatus
        {
            hasData = true,
            busy = nowBusy,
            fillPercent = fillPercent,
        };
        _status[index] = next;

        if (!prev.hasData || prev.fillPercent != next.fillPercent)
            OnFillPercentChanged?.Invoke(device, next.fillPercent);

        bool becameIdle = (!prev.hasData && !nowBusy) || (prev.busy && !nowBusy);
        if (becameIdle)
        {
            OnDeviceIdle?.Invoke(device);

            if (next.fillPercent == 0)
                OnFullyDeflated?.Invoke(device);
        }
    }

    // ── 内部ヘルパ ──────────────────────────────────────────────────────────

    private static int DeviceIndex(SerialDevice device)
    {
        switch (device)
        {
            case SerialDevice.A: return 0;
            case SerialDevice.B: return 1;
            default: return -1;
        }
    }

    private ArduinoSerialBridge BridgeOf(SerialDevice device)
    {
        switch (device)
        {
            case SerialDevice.A: return bridgeA;
            case SerialDevice.B: return bridgeB;
            default: return null;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Test: A を 100% 充填")]
    private void CtxFillA() => Fill(SerialDevice.A, 100);

    [ContextMenu("Test: A を 100% 吸引")]
    private void CtxSuckA() => Suck(SerialDevice.A, 100);

    [ContextMenu("Test: A を停止")]
    private void CtxStopA() => Stop(SerialDevice.A);
#endif
}
