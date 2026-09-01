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
            try
            {
                string line = port.ReadLine(); // ブロックしてもこのスレッド内だけの話
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
                if (!running) break;
                Debug.LogWarning($"[ArduinoSerialBridge] read error: {e}");
                Thread.Sleep(100);
            }
        }
    }

    void WriteLoop()
    {
        foreach (var line in outgoing.GetConsumingEnumerable())
        {
            if (!running) break;
            try
            {
                port.Write(line + "\n"); // ここがフリーズしてもメインスレッドは無関係
                if (logRawTx) Debug.Log($"[ArduinoSerialBridge] TX -> \"{line}\"");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArduinoSerialBridge] write error: {e}");
            }
        }
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
        outgoing.Add(line);
    }

    void OnDestroy()
    {
        running = false;
        try { outgoing.CompleteAdding(); } catch { }

        // Close()前に両スレッドの終了を待つ（待たずに閉じると競合状態でハング/例外の恐れ）
        readThread?.Join(500);
        writeThread?.Join(500);

        try { port?.Close(); } catch { }
        try { port?.Dispose(); } catch { }
        port = null;
    }
}
