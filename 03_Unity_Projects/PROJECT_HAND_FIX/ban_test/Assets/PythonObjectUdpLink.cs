using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/*
DATN - PYTHON OBJECT UDP LINK V23
Nhan ten vat tu Python qua UDP 5053:
- Cap nhat System Status UI.
- Dieu khien DatnObjectMoveDirect3WorldPoints cho vat bay vao PalmPoint.
- Bao FingerStatusUI biet vat nao dang duoc detect de hien target ban dau.
  Sau do HandUdpReceiver se cap nhat target dong theo FSR active.
*/

public class PythonObjectUdpLink : MonoBehaviour
{
    [Header("UDP SETTINGS")]
    public int listenPort = 5053;

    [Header("REFERENCES")]
    public SystemStatusUIManual systemStatusUI;
    public DatnObjectMoveDirect3WorldPoints objectMover;
    public FingerStatusUI fingerStatusUI;

    [Header("OPTIONS")]
    public bool updateSystemStatus = true;
    public bool controlObjectMover = true;
    public bool updateFingerStatusTarget = true;
    public bool printDebugLog = true;

    [Header("RUNTIME")]
    public string lastRawMessage = "";
    public string lastObjectName = "None";
    public DatnObjectMoveDirect3WorldPoints.DetectedObject lastDetected =
        DatnObjectMoveDirect3WorldPoints.DetectedObject.None;

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool running = false;

    private readonly object msgLock = new object();
    private string pendingMessage = "";

    private void Start()
    {
        FindFingerStatusIfNeeded();
        StartReceiver();
    }

    private void Update()
    {
        string msg = "";

        lock (msgLock)
        {
            if (!string.IsNullOrEmpty(pendingMessage))
            {
                msg = pendingMessage;
                pendingMessage = "";
            }
        }

        if (string.IsNullOrEmpty(msg))
            return;

        ApplyMessage(msg);
    }

    private void FindFingerStatusIfNeeded()
    {
        if (fingerStatusUI != null) return;

#if UNITY_2023_1_OR_NEWER
        fingerStatusUI = FindFirstObjectByType<FingerStatusUI>();
#else
        fingerStatusUI = FindObjectOfType<FingerStatusUI>();
#endif
    }

    private void StartReceiver()
    {
        try
        {
            udpClient = new UdpClient(listenPort);
            running = true;

            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log("PythonObjectUdpLink V23 started on UDP port " + listenPort);
        }
        catch (Exception ex)
        {
            Debug.LogError("PythonObjectUdpLink UDP start error: " + ex.Message);
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, listenPort);

        while (running)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEP);
                string message = Encoding.UTF8.GetString(data).Trim();

                lock (msgLock)
                {
                    pendingMessage = message;
                }
            }
            catch (SocketException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning("PythonObjectUdpLink receive warning: " + ex.Message);
            }
        }
    }

    private void ApplyMessage(string raw)
    {
        lastRawMessage = raw;

        DatnObjectMoveDirect3WorldPoints.DetectedObject obj = ParseObject(raw);
        lastDetected = obj;
        lastObjectName = ObjectToDisplayName(obj);

        if (updateSystemStatus && systemStatusUI != null)
        {
            systemStatusUI.SetObjectName(lastObjectName);
        }

        if (updateFingerStatusTarget)
        {
            FindFingerStatusIfNeeded();

            if (fingerStatusUI != null)
            {
                // Target ban dau theo vat. HandUdpReceiver se ghi de target dong neu co JSON 5052.
                fingerStatusUI.ApplyObjectTargetByLabel(raw);
            }
        }

        if (controlObjectMover && objectMover != null)
        {
            objectMover.useDebugObject = true;
            objectMover.debugObject = obj;
            objectMover.autoMove = true;
        }

        if (printDebugLog)
        {
            Debug.Log("PythonObjectUdpLink V23 raw=[" + raw + "] -> " + lastObjectName);
        }
    }

    private DatnObjectMoveDirect3WorldPoints.DetectedObject ParseObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DatnObjectMoveDirect3WorldPoints.DetectedObject.None;

        string s = raw.Trim().ToLowerInvariant();

        if (s.Contains("kimquat") || s.Contains("kim_quat") || s.Contains("kim qu") || s.Contains("kimquat_sauce") || s.Contains("sauce"))
            return DatnObjectMoveDirect3WorldPoints.DetectedObject.Kimquat;

        if (s.Contains("red_chili") || s.Contains("chili") || s.Contains("ot_bot") || s.Contains("ot do") || s.Contains("ớt") || s.Contains("bot") || s.Contains("bột"))
            return DatnObjectMoveDirect3WorldPoints.DetectedObject.Chili;

        if (s.Contains("orange") || s.Contains("cam"))
            return DatnObjectMoveDirect3WorldPoints.DetectedObject.Orange;

        if (s.Contains("none") || s.Contains("no_object") || s.Contains("no object") || s.Contains("null"))
            return DatnObjectMoveDirect3WorldPoints.DetectedObject.None;

        return DatnObjectMoveDirect3WorldPoints.DetectedObject.None;
    }

    private string ObjectToDisplayName(DatnObjectMoveDirect3WorldPoints.DetectedObject obj)
    {
        if (obj == DatnObjectMoveDirect3WorldPoints.DetectedObject.Chili)
            return "Chili";

        if (obj == DatnObjectMoveDirect3WorldPoints.DetectedObject.Kimquat)
            return "Kimquat";

        if (obj == DatnObjectMoveDirect3WorldPoints.DetectedObject.Orange)
            return "Orange";

        return "None";
    }

    private void CloseReceiver()
    {
        running = false;

        try
        {
            if (udpClient != null)
                udpClient.Close();
        }
        catch { }

        try
        {
            if (receiveThread != null && receiveThread.IsAlive)
                receiveThread.Join(100);
        }
        catch { }
    }

    private void OnDisable()
    {
        CloseReceiver();
    }

    private void OnApplicationQuit()
    {
        CloseReceiver();
    }
}
