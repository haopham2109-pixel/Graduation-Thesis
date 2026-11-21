using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

[Serializable]
public class HandPacket5
{
    public float thumb;
    public float index;
    public float middle;
    public float ring;
    public float pinky;
}

public class HandUdpReceiver : MonoBehaviour
{
    public int listenPort = 5052;

    [Header("Thumb bones")]
    public Transform thumb1;
    public Transform thumb2;
    public Transform thumb3;

    [Header("Index bones")]
    public Transform index1;
    public Transform index2;
    public Transform index3;

    [Header("Middle bones")]
    public Transform middle1;
    public Transform middle2;
    public Transform middle3;

    [Header("Ring bones")]
    public Transform ring1;
    public Transform ring2;
    public Transform ring3;

    [Header("Pinky bones")]
    public Transform pinky1;
    public Transform pinky2;
    public Transform pinky3;

    [Header("Curl Axis")]
    public Vector3 thumbAxis = new Vector3(1, 0, 0);
    public Vector3 indexAxis = new Vector3(1, 0, 0);
    public Vector3 middleAxis = new Vector3(1, 0, 0);
    public Vector3 ringAxis = new Vector3(1, 0, 0);
    public Vector3 pinkyAxis = new Vector3(1, 0, 0);

    [Header("Invert Direction")]
    public bool thumbInvert = false;
    public bool indexInvert = false;
    public bool middleInvert = false;
    public bool ringInvert = false;
    public bool pinkyInvert = false;

    [Header("Bend Max")]
    public float thumbMaxBend = 75f;
    public float fingerMaxBend = 125f;
    public float smoothSpeed = 14f;

    [Header("Finger Gain")]
    public float thumbGain = 1.0f;
    public float indexGain = 1.0f;
    public float middleGain = 1.0f;
    public float ringGain = 1.0f;
    public float pinkyGain = 1.0f;

    [Header("Manual Test Mode")]
    public bool testMode = true;
    [Range(0, 100)] public float testThumb = 0;
    [Range(0, 100)] public float testIndex = 0;
    [Range(0, 100)] public float testMiddle = 0;
    [Range(0, 100)] public float testRing = 0;
    [Range(0, 100)] public float testPinky = 0;

    private UdpClient udp;
    private Thread recvThread;
    private bool running = false;

    private float thumbTarget, indexTarget, middleTarget, ringTarget, pinkyTarget;
    private float thumbCur, indexCur, middleCur, ringCur, pinkyCur;

    private Quaternion thumb1Start, thumb2Start, thumb3Start;
    private Quaternion index1Start, index2Start, index3Start;
    private Quaternion middle1Start, middle2Start, middle3Start;
    private Quaternion ring1Start, ring2Start, ring3Start;
    private Quaternion pinky1Start, pinky2Start, pinky3Start;

    void Start()
    {
        SaveStartRotations();

        udp = new UdpClient(listenPort);
        running = true;
        recvThread = new Thread(ReceiveLoop);
        recvThread.IsBackground = true;
        recvThread.Start();
    }

    void SaveStartRotations()
    {
        if (thumb1) thumb1Start = thumb1.localRotation;
        if (thumb2) thumb2Start = thumb2.localRotation;
        if (thumb3) thumb3Start = thumb3.localRotation;

        if (index1) index1Start = index1.localRotation;
        if (index2) index2Start = index2.localRotation;
        if (index3) index3Start = index3.localRotation;

        if (middle1) middle1Start = middle1.localRotation;
        if (middle2) middle2Start = middle2.localRotation;
        if (middle3) middle3Start = middle3.localRotation;

        if (ring1) ring1Start = ring1.localRotation;
        if (ring2) ring2Start = ring2.localRotation;
        if (ring3) ring3Start = ring3.localRotation;

        if (pinky1) pinky1Start = pinky1.localRotation;
        if (pinky2) pinky2Start = pinky2.localRotation;
        if (pinky3) pinky3Start = pinky3.localRotation;
    }

    void ReceiveLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, listenPort);

        while (running)
        {
            try
            {
                byte[] data = udp.Receive(ref ep);
                string json = Encoding.UTF8.GetString(data);
                HandPacket5 p = JsonUtility.FromJson<HandPacket5>(json);

                if (p != null)
                {
                    thumbTarget = p.thumb;
                    indexTarget = p.index;
                    middleTarget = p.middle;
                    ringTarget = p.ring;
                    pinkyTarget = p.pinky;
                }
            }
            catch { }
        }
    }

    float Curve01(float x)
    {
        x = Mathf.Clamp01(x);
        return Mathf.Pow(x, 0.85f);
    }

    void Update()
    {
        float srcThumb  = testMode ? testThumb  : thumbTarget;
        float srcIndex  = testMode ? testIndex  : indexTarget;
        float srcMiddle = testMode ? testMiddle : middleTarget;
        float srcRing   = testMode ? testRing   : ringTarget;
        float srcPinky  = testMode ? testPinky  : pinkyTarget;

        thumbCur  = Mathf.Lerp(thumbCur,  srcThumb,  Time.deltaTime * smoothSpeed);
        indexCur  = Mathf.Lerp(indexCur,  srcIndex,  Time.deltaTime * smoothSpeed);
        middleCur = Mathf.Lerp(middleCur, srcMiddle, Time.deltaTime * smoothSpeed);
        ringCur   = Mathf.Lerp(ringCur,   srcRing,   Time.deltaTime * smoothSpeed);
        pinkyCur  = Mathf.Lerp(pinkyCur,  srcPinky,  Time.deltaTime * smoothSpeed);

        float thumbBend  = Mathf.Lerp(0f, thumbMaxBend,  Curve01((thumbCur  / 100f) * thumbGain));
        float indexBend  = Mathf.Lerp(0f, fingerMaxBend, Curve01((indexCur  / 100f) * indexGain));
        float middleBend = Mathf.Lerp(0f, fingerMaxBend, Curve01((middleCur / 100f) * middleGain));
        float ringBend   = Mathf.Lerp(0f, fingerMaxBend, Curve01((ringCur   / 100f) * ringGain));
        float pinkyBend  = Mathf.Lerp(0f, fingerMaxBend, Curve01((pinkyCur  / 100f) * pinkyGain));

        ApplyFinger(thumb1, thumb2, thumb3, thumb1Start, thumb2Start, thumb3Start, thumbAxis, thumbBend, thumbInvert);
        ApplyFinger(index1, index2, index3, index1Start, index2Start, index3Start, indexAxis, indexBend, indexInvert);
        ApplyFinger(middle1, middle2, middle3, middle1Start, middle2Start, middle3Start, middleAxis, middleBend, middleInvert);
        ApplyFinger(ring1, ring2, ring3, ring1Start, ring2Start, ring3Start, ringAxis, ringBend, ringInvert);
        ApplyFinger(pinky1, pinky2, pinky3, pinky1Start, pinky2Start, pinky3Start, pinkyAxis, pinkyBend, pinkyInvert);
    }

    void ApplyFinger(
        Transform b1, Transform b2, Transform b3,
        Quaternion q1, Quaternion q2, Quaternion q3,
        Vector3 axis, float bend, bool invert)
    {
        float sign = invert ? -1f : 1f;
        Vector3 a = axis.normalized;

        if (b1) b1.localRotation = q1 * Quaternion.AngleAxis(sign * bend * 0.75f, a);
        if (b2) b2.localRotation = q2 * Quaternion.AngleAxis(sign * bend * 0.18f, a);
        if (b3) b3.localRotation = q3 * Quaternion.AngleAxis(sign * bend * 0.07f, a);
    }

    void OnApplicationQuit()
    {
        running = false;
        try { udp?.Close(); } catch { }
        try { recvThread?.Interrupt(); } catch { }
    }
}