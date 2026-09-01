using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// Arduinoとのシリアル通信を専用バックグラウンドスレッドに隔離する。
/// メインスレッドはWrite/Readを直接呼ばないため、SerialPort.Writeが詰まってもUnityは固まらない。
/// 外部プロセス不要（Unity内で完結）。
///
/// 終了処理 (Play停止 / ドメインリロード) について:
/// ・.NET Framework 互換レベルの <see cref="SerialPort"/> は Close() 中に内部スレッドから
///   "Safe handle has been closed" を投げて Editor ごと落とすことがある。
///   これを避けるため、BaseStream を先に閉じて finalizer を抑制し、
///   Close 自体もハングに備えて別スレッド + タイムアウトで実行する。
/// </summary>
public class ArduinoSerialBridge : MonoBehaviour
{
    [Header("シリアル設定")]
    public string portName = "COM3";
    // External/ 以下の Arduino スケッチ (arduino_mbb_v1 等) は全て 9600。既定を合わせる。
    public int baudRate = 9600;

    [Header("デバッグ")]
    [Tooltip("受信した生の行をすべてConsoleに出す")]
    public bool logRawRx = true;
    [Tooltip("送信した生の行をすべてConsoleに出す")]
    public bool logRawTx = false;

    /// <summary>Openに成功してスレッドが動いているか。Inspectorから確認できる。</summary>
    public bool IsConnected => running && port != null && port.IsOpen;

    public event Action<string> OnLineReceived;

    SerialPort port;
    Thread readThread;
    Thread writeThread;
    volatile bool running;
    int shutdownStarted; // 0/1。OnDestroy と OnApplicationQuit の二重実行を防ぐ

    readonly ConcurrentQueue<string> incoming = new();
    readonly BlockingCollection<string> outgoing = new();

    void Awake()
    {
        port = new SerialPort(portName, baudRate)
        {
            ReadTimeout = 100,
            WriteTimeout = 500,
            NewLine = "\n",
            DtrEnable = true,
            RtsEnable = true,
        };

        try
        {
            port.Open();
        }
        catch (UnauthorizedAccessException e)
        {
            Debug.LogError(
                $"[ArduinoSerialBridge] {portName} を開けません（アクセス拒否）。" +
                $"別プロセスがこのポートを掴んでいます。" +
                $"Arduino IDE / PlatformIO のシリアルモニタ、別のUnity Playセッション、" +
                $"前回Playのハンドルリークなどを確認してください。詳細: {e.Message}");
            port = null;
            enabled = false;
            return;
        }
        catch (Exception e) when (e is IOException || e is ArgumentException || e is FileNotFoundException)
        {
            Debug.LogError(
                $"[ArduinoSerialBridge] {portName} を開けません。ポート名が正しいか" +
                $"（デバイスマネージャーで確認）、ケーブルが挿さっているかを確認してください。詳細: {e.Message}");
            port = null;
            enabled = false;
            return;
        }

        Debug.Log($"[ArduinoSerialBridge] {portName} @ {baudRate} を開きました。IsOpen={port.IsOpen}");

        running = true;
        readThread = new Thread(ReadLoop) { IsBackground = true, Name = "ArduinoSerialBridge.Read" };
        writeThread = new Thread(WriteLoop) { IsBackground = true, Name = "ArduinoSerialBridge.Write" };
        readThread.Start();
        writeThread.Start();
    }

    void ReadLoop()
    {
        while (running)
        {
            SerialPort p = port;
            if (p == null) break;

            try
            {
                string line = p.ReadLine(); // ブロックしてもこのスレッド内だけの話
                if (!string.IsNullOrEmpty(line))
                {
                    string trimmed = line.Trim();
                    if (logRawRx) Debug.Log($"[ArduinoSerialBridge] RX <- \"{trimmed}\"");
                    incoming.Enqueue(trimmed);
                }
            }
            catch (TimeoutException)
            {
                // 読めるデータがなかっただけ。無視して継続
            }
            catch (Exception e)
            {
                // 終了処理で port を閉じると ReadLine が例外を投げる。想定内なので静かに抜ける。
                if (!running || e is ObjectDisposedException || e is InvalidOperationException || e is IOException)
                    break;

                Debug.LogWarning($"[ArduinoSerialBridge] read error: {e}");
                Thread.Sleep(100);
            }
        }
    }

    void WriteLoop()
    {
        try
        {
            foreach (var line in outgoing.GetConsumingEnumerable())
            {
                if (!running) break;

                SerialPort p = port;
                if (p == null || !p.IsOpen) break;

                try
                {
                    p.Write(line + "\n"); // ここがフリーズしてもメインスレッドは無関係
                    if (logRawTx) Debug.Log($"[ArduinoSerialBridge] TX -> \"{line}\"");
                }
                catch (Exception e)
                {
                    if (!running || e is ObjectDisposedException || e is InvalidOperationException)
                        break;

                    Debug.LogWarning($"[ArduinoSerialBridge] write error: {e}");
                }
            }
        }
        catch (ObjectDisposedException) { /* outgoing 破棄。終了処理中なので無視 */ }
        catch (InvalidOperationException) { /* CompleteAdding 済み。無視 */ }
    }

    void Update()
    {
        while (incoming.TryDequeue(out var line))
        {
            OnLineReceived?.Invoke(line);
        }
    }

    /// <summary>メインスレッドから呼ぶ。キューに積むだけで即座に返る（ブロックしない）</summary>
    public void SendLine(string line)
    {
        if (!running) return; // 未接続なら黙って捨てる
        try { outgoing.Add(line); }
        catch (InvalidOperationException) { /* 終了処理中 (CompleteAdding 済み) */ }
    }

    void OnApplicationQuit() => Shutdown();

    void OnDestroy() => Shutdown();

    void Shutdown()
    {
        // OnApplicationQuit → OnDestroy と続けて呼ばれるので一度だけ実行する
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
            return;

        running = false;
        try { outgoing.CompleteAdding(); } catch { }

        SerialPort p = port;
        port = null; // ワーカースレッドがこれ以降 port に触れないように先に切る

        if (p != null)
        {
            // .NET Framework の SerialPort は Close() が
            //   ・内部スレッドから "Safe handle has been closed" を投げて Editor をクラッシュさせる
            //   ・まれにハングする
            // ことがある。BaseStream を先に閉じて finalizer を止め、Close は別スレッド + タイムアウトで。
            ClosePortSafely(p);
        }

        // ブロック中の Read/Write は上の Close で叩き起こされているはず。
        readThread?.Join(1000);
        writeThread?.Join(1000);

        if (readThread != null && readThread.IsAlive)
            Debug.LogWarning("[ArduinoSerialBridge] read スレッドが終了しませんでした（ハンドルを放棄します）");
        if (writeThread != null && writeThread.IsAlive)
            Debug.LogWarning("[ArduinoSerialBridge] write スレッドが終了しませんでした（ハンドルを放棄します）");
    }

    static void ClosePortSafely(SerialPort p)
    {
        var closer = new Thread(() =>
        {
            try
            {
                // BaseStream(内部 SerialStream) を先に閉じ、その finalizer を抑制する。
                // これが Close 時のクラッシュ回避の肝。
                try
                {
                    Stream s = p.BaseStream;
                    if (s != null)
                    {
                        GC.SuppressFinalize(s);
                        s.Close();
                    }
                }
                catch { /* 既に閉じている等 */ }

                p.Close();
            }
            catch { /* Close 自体の例外は握りつぶす */ }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        })
        {
            IsBackground = true,
            Name = "ArduinoSerialBridge.Close",
        };

        closer.Start();

        // 1 秒で閉じられなければ見切る。ハンドルはリークするが Editor は落とさない。
        if (!closer.Join(1000))
            Debug.LogWarning($"[ArduinoSerialBridge] ポートの Close がタイムアウトしました。");
    }
}
