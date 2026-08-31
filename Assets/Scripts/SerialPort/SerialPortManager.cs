using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum UserChoice { idle, bridge1, bridge2 }
public enum DeviceMode { idle, fill, suck }

public class SerialPortManager : MonoBehaviour
{
    private readonly int MES_MAX = 2;
    
    [Header("UserAnswer")]
    public UserChoice userChoice = UserChoice.idle;
    public bool isCorrect = false;

    [Header("同じGameObjectにArduinoSerialBridgeを2つアタッチしてここへ割り当て")]
    public ArduinoSerialBridge bridge1;
    public ArduinoSerialBridge bridge2; // 未使用ならInspectorでnullのまま

    // create message from deviceMode
    private DeviceMode deviceMode = DeviceMode.idle;
    private Dictionary<DeviceMode, char> modeMes = new Dictionary<DeviceMode, char>()
    {
        {DeviceMode.idle, 'i' },
        {DeviceMode.fill, 'f' },
        {DeviceMode.suck, 's' }
    };

    private bool onGoing = false;
    private int fillingRate = 0;

    /*public struct SerialMessage {
        public string mes1; // { onGoing, fillingRate }
        public string mes2; // { otherwise }
    };
    public SerialMessage serialMessage;*/

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

    void OnLineFromBridge1(string line) => HandleLine(line, bridge1);
    void OnLineFromBridge2(string line) => HandleLine(line, bridge2);

    void HandleLine(string line, ArduinoSerialBridge bridge)
    {
        Debug.Log($"[SerialPortManager] recv raw: \"{line}\"");

        // 想定フォーマット: "onGoing, fillingRate"
        var parts = line.Split(',');
        if (parts.Length < MES_MAX)
        {
            Debug.LogWarning($"[SerialPortManager] フィールド数不足 ({parts.Length}) のため無視: \"{line}\"");
            return;
        }

        if (int.TryParse(parts[0].Trim(), out int g))
            onGoing = Convert.ToBoolean(g);
        else
            Debug.LogWarning($"[SerialPortManager] onGoing を変換できません: \"{parts[0]}\"");

        if (int.TryParse(parts[1].Trim(), out int r))
            if(r < 0 || r > 100)
                Debug.LogWarning($"[SerialPortManager] fillingRate が範囲外です: {r}");
            else 
                fillingRate = r;
        else Debug.LogWarning($"[SerialPortManager] fillingRate を変換できません: \"{parts[1]}\"");

        Debug.Log($"[SerialPortManager] recv: {line} -> onGoing={onGoing},fillingRate={fillingRate}");
    }

    public void setUserChoice(UserChoice c)
    {
        userChoice = c;
    }

    public void setDeviceMode(DeviceMode m)
    {
        deviceMode = m;
    }

    public void setFillingRate(int r)
    {
        fillingRate = r;
    }

    public DeviceMode getDeviceMode()
    {
        return deviceMode;
    }

    public void WriteData()
    {
        string data = $"{modeMes[deviceMode]},{fillingRate}";

        // SendLineはキューに積むだけで即座に返る（ここではブロックしない）
        Debug.Log("[SerialPort] write data:"+data);

        if (userChoice == UserChoice.bridge1)
        {
            bridge1?.SendLine(data);
        }
        else if (userChoice == UserChoice.bridge2)
        {
            bridge2?.SendLine(data);
        } else
        {
            Debug.LogWarning("デバイスが選択されていません");
        }
    }

    void OnApplicationQuit()
    {
        WriteData();
        // SendLineは非同期なので、writeThreadが実際に送信するまで一瞬待つ
        System.Threading.Thread.Sleep(50);
    }
}
