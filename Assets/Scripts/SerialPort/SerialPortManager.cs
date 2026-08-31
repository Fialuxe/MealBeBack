using UnityEngine;
using UnityEngine.InputSystem;

public enum UserChoice { idle, left, right }
public enum WindowType { idle, chewing, notChewing }

public class SerialPortManager : MonoBehaviour
{
    [Header("UserAnswer")]
    public UserChoice userChoice = UserChoice.idle;
    public bool isCorrect = false;

    [Header("WindowType")]
    public WindowType windowType = WindowType.idle;

    [Header("同じGameObjectにArduinoSerialBridgeを2つアタッチしてここへ割り当て")]
    public ArduinoSerialBridge bridge1;
    public ArduinoSerialBridge bridge2; // 未使用ならInspectorでnullのまま

    void OnEnable()
    {
        if (bridge1 != null) bridge1.OnLineReceived += OnLineFromBridge1;
        if (bridge2 != null) bridge2.OnLineReceived += OnLineFromBridge2;
    }

    void OnDisable()
    {
        if (bridge1 != null) bridge1.OnLineReceived -= OnLineFromBridge1;
        if (bridge2 != null) bridge2.OnLineReceived -= OnLineFromBridge2;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Debug.Log("put space");
            WriteData();
        }
    }

    void OnLineFromBridge1(string line) => HandleLine(line);
    void OnLineFromBridge2(string line) => HandleLine(line);

    void HandleLine(string line)
    {
        Debug.Log($"[SerialPortManager] recv raw: \"{line}\"");

        // 想定フォーマット: "a,b,windowType"
        var parts = line.Split(',');
        if (parts.Length < 3)
        {
            Debug.LogWarning($"[SerialPortManager] フィールド数不足 ({parts.Length}) のため無視: \"{line}\"");
            return;
        }

        if (int.TryParse(parts[2].Trim(), out int w))
            windowType = (WindowType)w;
        else
            Debug.LogWarning($"[SerialPortManager] windowType をパースできません: \"{parts[2]}\"");

        Debug.Log($"[SerialPortManager] recv: {line} -> windowType={windowType}");
    }

    public void setUserChoice(UserChoice c)
    {
        userChoice = c;
    }

    public void WriteData()
    {
        string data =
            (int)userChoice + ","
            + (isCorrect ? 1 : 0) + ","
            + (int)windowType;

        // SendLineはキューに積むだけで即座に返る（ここではブロックしない）
        Debug.Log("[SerialPort] write data:"+data);
        bridge1?.SendLine(data);
        bridge2?.SendLine(data);
    }

    void OnApplicationQuit()
    {
        WriteData();
        // SendLineは非同期なので、writeThreadが実際に送信するまで一瞬待つ
        System.Threading.Thread.Sleep(50);
    }
}
