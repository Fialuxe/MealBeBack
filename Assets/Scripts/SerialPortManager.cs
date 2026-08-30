using System;
using System.IO.Ports;
using System.Threading;
using System.Security.Authentication;
using UnityEngine;

public enum UserChoice
{
    idle,
    left,
    right
}

public enum WindowType
{
    idle,
    chewing,
    notChewing
}

public class SerialPortManager : MonoBehaviour
{
    [Header("UserAnswer")]
    public UserChoice userChoice = UserChoice.idle;
    public bool isCorrect = false;

    [Header("Serial Settings")]
    public string portName1 = "COM4";  // Inspectorから変更
    public string portName2 = "COM3";  // Inspectorから変更
    public int baudRate = 9600;

    SerialPort sp1, sp2;
    private bool running = false;
    private Thread receiveThread = null; // 受信用の並列処理

    // TODO
    // add device parameter
    [Header("WindowType")]
    public WindowType windowType = WindowType.idle;

    void Start()
    {
        sp1 = new SerialPort(portName1, baudRate);
        sp1.Open();

        sp2 = new SerialPort(portName2, baudRate);
        sp2.Open();

        running = true;
        receiveThread = new Thread(receiveLoop);
        receiveThread.Start();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log("put space");
            WriteData();
        }
    }

    void OnApplicationQuit()
    {
        running = false;

        if (this.IsAllSerialPortOpen())
        {
            string data =
                /*userChoice*/ 0 + ","
                + /*isCorrect*/ 0 + ","

                //TODO
                //add Parameter
                + /*windowType*/ 0
                //e.t.c.

                + "\n";

            sp1.Write(data);
            sp1.Close();
            //sp2.Write(data);
            //sp2.Close();
        }
    }

    public void setUserChoice(UserChoice c)
    {
        try
        {
            userChoice = c;
        }
        catch (ArithmeticException e)
        {
            Console.WriteLine(e.Message);
        }
    }

    private void receiveData(string message)
    {
        String[] data;
        if (this.IsAllSerialPortOpen())
        {
            data = message.Split(new string[] { "\n" }, System.StringSplitOptions.None);

            //TODO
            //add receiving parameter by Arduino
            windowType = (WindowType)Int32.Parse(data[2]);
            //e.t.c.

            try
            {
                Debug.Log(data[0]);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(e.Message);
            }
        }
    }

    private void receiveLoop()
    {
        while (running)
        {
            try
            {
                if (this.IsAllSerialPortOpen())
                {
                    string message1 = sp1.ReadLine();
                    //string message2 = sp2.ReadLine();

                    Debug.Log(message1);
                    //Debug.Log(message2);

                    //receiveData(message1);
                    //receiveData(message2);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError(e.Message);
            }
        }
    }

    //send to Arduino
    public void WriteData()
    {
        if (this.IsAllSerialPortOpen())
        {
            string data1 =
                (int)userChoice + ","
                + (isCorrect ? 1 : 0) + ","

                //TODO
                //add Parameter
                + (int)windowType
                //e.t.c.

                + "\n";

            string data2 =
                (int)userChoice + ","
                + (isCorrect ? 1 : 0) + ","

                //TODO
                //add Parameter
                + (int)windowType
                //e.t.c.

                + "\n";

            sp1.Write(data1);
            //sp2.Write(data2);
        }
    }

    public bool IsAllSerialPortOpen()
    {
        if (sp1 != null && sp1.IsOpen) return true;
        return (sp1 != null && sp1.IsOpen && sp2 != null && sp2.IsOpen);
    }
}