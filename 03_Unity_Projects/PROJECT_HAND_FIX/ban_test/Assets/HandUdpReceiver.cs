using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/*
DATN - HAND UDP RECEIVER V23 PERFECT 4FINGER TARGET + THUMB FORCE
- Receiver chính cho Python -> Unity port 5052
- Nhận chung 1 JSON gồm:
  + 15 active joint: thumb1..pinky3
  + dữ liệu force/weight: mass_kg, recommended_n, total_force_n, status...
- JsonUtility vẫn parse được HandPacket15 dù JSON có thêm field force.
- Force UI được cập nhật trên MAIN THREAD trong Update(), không cập nhật trong ReceiveLoop.
*/

[Serializable]
public class HandPacket15
{
    public float thumb1;
    public float thumb2;
    public float thumb3;

    public float index1;
    public float index2;
    public float index3;

    public float middle1;
    public float middle2;
    public float middle3;

    public float ring1;
    public float ring2;
    public float ring3;

    public float pinky1;
    public float pinky2;
    public float pinky3;
}

[Serializable]
public class HandForcePacketMini
{
    public float thumb_force;
    public float index_force;
    public float middle_force;
    public float ring_force;
    public float pinky_force;
    public float total_force_n;
}


[Serializable]
public class HandTargetForcePacket
{
    public string object_label;
    public string object_raw_label;

    public float recommended_n;
    public float target_total_n;
    public float object_target_force_n;

    public float thumb_target_n;
    public float index_target_n;
    public float middle_target_n;
    public float ring_target_n;
    public float pinky_target_n;

    public int target_active_count;
    public string target_mode;
}


[Serializable]
public class HandSystemStatusPacket
{
    public string object_label;
    public string object_raw_label;
    public float object_mass_g;
    public float mass_g;

    public string auto_grip_state;
    public string auto_grip_status;

    public string status;
    public bool grip_active;
    public bool force_hold_allowed;
}

public class HandUdpReceiver : MonoBehaviour
{
    [Header("Network")]
    public int listenPort = 5052;

    [Header("Force UI Binder")]
    [Tooltip("Có thể để trống. Script sẽ tự tìm hoặc tự add ForceAnalysisUIBinder vào cùng GameObject.")]
    public ForceAnalysisUIBinder forceAnalysisBinder;

    [Header("System Status UI")]
    [Tooltip("Kéo object có SystemStatusUIManual vào đây. Nếu để trống, script sẽ tự tìm trong scene.")]
    public SystemStatusUIManual systemStatusUI;

    [Tooltip("Bật để HandUdpReceiver cập nhật panel SYSTEM STATUS bằng JSON từ Python.")]
    public bool updateSystemStatusUI = true;

    [Tooltip("Quá thời gian này không nhận UDP thì Sync = Lost.")]
    public float systemSyncLostTimeout = 0.80f;

    [Header("Finger Status UI Bridge")]
    [Tooltip("Kéo object có FingerStatusUI vào đây, ví dụ DemoManager.")]
    public FingerStatusUI fingerStatusUI;

    [Tooltip("Bật để HandUdpReceiver cập nhật khung Finger Status Angle/Force.")]
    public bool updateFingerStatusUI = true;

    [Tooltip("Thang lực để đổi Newton sang 0..1 cho thanh Force trong FingerStatusUI.")]
    public float fingerStatusForceMaxN = 20f;

    [Tooltip("Debug bridge FingerStatus. Bật khi cần xem có gọi đúng UI không.")]
    public bool debugFingerStatusBridge = false;

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

    [Header("Thumb base 2-axis motion")]
    public Vector3 thumb1OpposeAxis = new Vector3(0f, 0f, 1f);
    public Vector3 thumb1CurlAxis = new Vector3(0f, 1f, 0f);

    [Header("Thumb distal axes")]
    public Vector3 thumb2Axis = new Vector3(0f, 0f, -1f);
    public Vector3 thumb3Axis = new Vector3(0f, 0f, -1f);

    [Header("Finger axes")]
    public Vector3 indexAxis = new Vector3(0f, 0f, -1f);
    public Vector3 middleAxis = new Vector3(0f, 0f, -1f);
    public Vector3 ringAxis = new Vector3(0f, 0f, -1f);
    public Vector3 pinkyAxis = new Vector3(0f, 0f, 1f);

    [Header("Thumb base invert")]
    public bool thumb1OpposeInvert = true;
    public bool thumb1CurlInvert = false;

    [Header("Thumb distal invert")]
    public bool thumb2Invert = false;
    public bool thumb3Invert = false;

    [Header("Finger invert")]
    public bool indexInvert = false;
    public bool middleInvert = false;
    public bool ringInvert = false;
    public bool pinkyInvert = false;

    [Header("Safe visual bend limits (deg)")]
    public Vector2 thumb1BaseMax = new Vector2(82f, 24f);
    public Vector2 thumbDistalMax = new Vector2(64f, 78f);
    public Vector3 fingerMaxBend = new Vector3(78f, 100f, 84f);

    [Header("Extra add for visual tuning")]
    public Vector3 indexAdd = new Vector3(2f, 4f, 4f);
    public Vector3 middleAdd = new Vector3(4f, 6f, 6f);
    public Vector3 ringAdd = new Vector3(6f, 8f, 8f);
    public Vector3 pinkyAdd = new Vector3(4f, 6f, 6f);

    [Header("Response curves")]
    [Range(0.6f, 1.8f)] public float proximalCurvePower = 1.08f;
    [Range(0.6f, 1.8f)] public float middleCurvePower = 1.10f;
    [Range(0.6f, 1.8f)] public float distalCurvePower = 1.14f;
    [Range(0.6f, 1.8f)] public float thumbCurvePower = 1.08f;

    [Header("Thumb base mix")]
    [Range(0f, 1f)] public float thumbBaseCurlFromThumb2 = 0.38f;

    [Header("Unity visual scaling")]
    [Range(0.4f, 1.2f)] public float fingerMasterScale = 1.00f;
    [Range(0.4f, 1.2f)] public float thumbMasterScale = 0.98f;

    [Header("Zero / small motion")]
    public float inputZeroSnap = 1.0f;
    public float currentZeroSnap = 0.6f;
    public float fingerEnter = 6f;
    public float fingerExit = 3f;

    [Header("Extra inward bias")]
    public Vector3 indexInwardBias = new Vector3(1f, 2f, 2f);
    public Vector3 middleInwardBias = new Vector3(2f, 3f, 2f);
    public Vector3 ringInwardBias = new Vector3(3f, 4f, 3f);
    public Vector3 pinkyInwardBias = new Vector3(2f, 3f, 2f);
    [Range(0.0f, 1.0f)] public float inwardBlend = 0.12f;

    [Header("High-closure grip bias")]
    public float gripBiasStart = 58f;
    public Vector2 thumbGripBias = new Vector2(6f, 10f);
    public Vector3 indexGripBias = new Vector3(6f, 10f, 12f);
    public Vector3 middleGripBias = new Vector3(8f, 12f, 14f);
    public Vector3 ringGripBias = new Vector3(8f, 12f, 14f);
    public Vector3 pinkyGripBias = new Vector3(6f, 10f, 12f);

    [Header("Smoothing")]
    public float smoothSpeed = 11f;

    [Header("UI Bridge Values (0..1)")]
    [Range(0f, 1f)] public float thumbValue;
    [Range(0f, 1f)] public float indexValue;
    [Range(0f, 1f)] public float middleValue;
    [Range(0f, 1f)] public float ringValue;
    [Range(0f, 1f)] public float pinkyValue;

    private static bool portInUse = false;

    private UdpClient udp;
    private Thread recvThread;
    private volatile bool running;
    private bool socketReady = false;

    private readonly object packetLock = new object();
    private HandPacket15 latestPacket = new HandPacket15();

    // Lưu JSON mới nhất để cập nhật Force UI trong Update() trên main thread.
    private string latestJsonMessage = "";
    private string lastAppliedForceJson = "";

    // Dùng cho SYSTEM STATUS: tính Sync/Latency theo gói UDP mới nhất.
    private int latestPacketSeq = 0;
    private int lastAppliedSystemSeq = -1;
    private float lastSystemPacketTime = -999f;

    // Luu lai JSON moi nhat de LateUpdate cap nhat FingerStatus sau cung trong frame.
    private string lastJsonForFingerStatus = "";

    private float cur_thumb1, cur_thumb2, cur_thumb3;
    private float cur_index1, cur_index2, cur_index3;
    private float cur_middle1, cur_middle2, cur_middle3;
    private float cur_ring1, cur_ring2, cur_ring3;
    private float cur_pinky1, cur_pinky2, cur_pinky3;

    private bool latchThumb = true;
    private bool latchIndex = true;
    private bool latchMiddle = true;
    private bool latchRing = true;
    private bool latchPinky = true;

    private Quaternion thumb1Start, thumb2Start, thumb3Start;
    private Quaternion index1Start, index2Start, index3Start;
    private Quaternion middle1Start, middle2Start, middle3Start;
    private Quaternion ring1Start, ring2Start, ring3Start;
    private Quaternion pinky1Start, pinky2Start, pinky3Start;

    void Start()
    {
        SaveStartRotations();
        SetupForceBinder();
        SetupFingerStatusUI();
        SetupSystemStatusUI();

        if (portInUse)
        {
            Debug.LogError("HandUdpReceiver: port 5052 dang duoc listener khac dung. Chi de 1 receiver trong scene.");
            enabled = false;
            return;
        }

        try
        {
            udp = new UdpClient(listenPort);
            socketReady = true;
            portInUse = true;

            running = true;
            recvThread = new Thread(ReceiveLoop);
            recvThread.IsBackground = true;
            recvThread.Start();

            Debug.Log("HandUdpReceiver started on port " + listenPort);
        }
        catch (Exception e)
        {
            Debug.LogError("HandUdpReceiver bind fail: " + e.Message);
            socketReady = false;
            enabled = false;
        }
    }

    void SetupSystemStatusUI()
    {
        if (systemStatusUI == null)
            systemStatusUI = GetComponent<SystemStatusUIManual>();

#if UNITY_2023_1_OR_NEWER
        if (systemStatusUI == null)
            systemStatusUI = FindFirstObjectByType<SystemStatusUIManual>();
#else
        if (systemStatusUI == null)
            systemStatusUI = FindObjectOfType<SystemStatusUIManual>();
#endif
    }

    void ApplySystemStatusJson(string json)
    {
        if (systemStatusUI == null || string.IsNullOrEmpty(json))
            return;

        try
        {
            HandSystemStatusPacket sp = JsonUtility.FromJson<HandSystemStatusPacket>(json);
            if (sp == null)
                return;

            float objMass = sp.object_mass_g > 0.01f ? sp.object_mass_g : sp.mass_g;

            systemStatusUI.ApplyPythonPacket(
                sp.object_label,
                objMass,
                sp.auto_grip_state,
                sp.auto_grip_status,
                sp.status,
                sp.grip_active,
                sp.force_hold_allowed
            );
        }
        catch (Exception ex)
        {
            Debug.LogWarning("HandUdpReceiver SystemStatus parse warning: " + ex.Message);
        }
    }

    void UpdateSystemStatusConnection()
    {
        if (systemStatusUI == null)
            return;

        bool hasPacket = lastSystemPacketTime > 0f;
        float age = hasPacket ? (Time.realtimeSinceStartup - lastSystemPacketTime) : 999f;
        bool good = hasPacket && age <= systemSyncLostTimeout;
        int latencyMs = hasPacket ? Mathf.Max(0, Mathf.RoundToInt(age * 1000f)) : 0;

        systemStatusUI.SetLatencyMs(latencyMs);
        systemStatusUI.SetSync(good);
    }

    void SetupForceBinder()
    {
        if (forceAnalysisBinder == null)
            forceAnalysisBinder = GetComponent<ForceAnalysisUIBinder>();

#if UNITY_2023_1_OR_NEWER
        if (forceAnalysisBinder == null)
            forceAnalysisBinder = FindFirstObjectByType<ForceAnalysisUIBinder>();
#else
        if (forceAnalysisBinder == null)
            forceAnalysisBinder = FindObjectOfType<ForceAnalysisUIBinder>();
#endif

        if (forceAnalysisBinder == null)
        {
            forceAnalysisBinder = gameObject.AddComponent<ForceAnalysisUIBinder>();
            Debug.Log("HandUdpReceiver: Auto added ForceAnalysisUIBinder to " + gameObject.name);
        }
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

                // JSON có thêm mass_kg/status vẫn parse được phần 15 khớp.
                HandPacket15 p = JsonUtility.FromJson<HandPacket15>(json);

                if (p != null)
                {
                    lock (packetLock)
                    {
                        latestPacket = p;
                        latestJsonMessage = json;
                        latestPacketSeq++;
                    }
                }
            }
            catch (SocketException)
            {
                // Socket đóng khi thoát Play Mode là bình thường.
            }
            catch (Exception ex)
            {
                Debug.LogWarning("HandUdpReceiver receive warning: " + ex.Message);
            }
        }
    }

    float Curve01(float x, float power)
    {
        x = Mathf.Clamp01(x);
        power = Mathf.Max(0.01f, power);
        return Mathf.Pow(x, power);
    }

    float PercentToAngle(float percent, float maxDeg, float power)
    {
        return Mathf.Lerp(0f, maxDeg, Curve01(percent / 100f, power));
    }

    float SmoothToward(float current, float target)
    {
        if (Mathf.Abs(target) < inputZeroSnap)
            target = 0f;

        float k = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        float v = Mathf.Lerp(current, target, k);

        if (Mathf.Abs(target) <= 0.0001f && Mathf.Abs(v) < currentZeroSnap)
            return 0f;

        return v;
    }

    float CleanPercent(float v)
    {
        v = Mathf.Clamp(v, 0f, 100f);
        return Mathf.Abs(v) < inputZeroSnap ? 0f : v;
    }

    bool FingerLatched(float a, float b, float c, ref bool latch)
    {
        float m = Mathf.Max(a, Mathf.Max(b, c));

        if (latch)
        {
            if (m <= fingerEnter)
                return true;
            latch = false;
            return false;
        }
        else
        {
            if (m <= fingerExit)
            {
                latch = true;
                return true;
            }
            return false;
        }
    }

    float InwardExtra(float a, float b, float c, float maxDeg)
    {
        float closure = Mathf.Clamp01((0.45f * a + 0.35f * b + 0.20f * c) / 100f);
        return Mathf.Lerp(0f, maxDeg, Mathf.Pow(closure, 0.90f)) * inwardBlend;
    }

    float GripBias(float a, float b, float c, float maxDeg)
    {
        float mix = 0.30f * a + 0.45f * b + 0.25f * c;
        if (mix <= gripBiasStart)
            return 0f;

        float t = Mathf.Clamp01((mix - gripBiasStart) / Mathf.Max(100f - gripBiasStart, 0.001f));
        return Mathf.Lerp(0f, maxDeg, Mathf.Pow(t, 1.18f));
    }

    float AngleFromFinger(float percent, float maxDeg, int jointIndex)
    {
        float power = distalCurvePower;
        if (jointIndex == 1) power = proximalCurvePower;
        else if (jointIndex == 2) power = middleCurvePower;
        return PercentToAngle(percent, maxDeg, power);
    }

    float SafePercent(float raw, float scale)
    {
        float v = CleanPercent(raw);
        v *= scale;
        return Mathf.Clamp(v, 0f, 100f);
    }

    void Update()
    {
        if (!socketReady) return;

        HandPacket15 p;
        string jsonToApply;
        int seqToApply;

        lock (packetLock)
        {
            p = latestPacket;
            jsonToApply = latestJsonMessage;
            seqToApply = latestPacketSeq;
        }

        // Cập nhật Force Analysis UI bằng JSON đầy đủ từ Python.
        if (forceAnalysisBinder != null &&
            !string.IsNullOrEmpty(jsonToApply) &&
            jsonToApply != lastAppliedForceJson)
        {
            lastAppliedForceJson = jsonToApply;
            forceAnalysisBinder.ApplyJson(jsonToApply);
        }

        // Cập nhật SYSTEM STATUS bằng JSON đầy đủ từ Python.
        if (updateSystemStatusUI && systemStatusUI != null)
        {
            if (!string.IsNullOrEmpty(jsonToApply) && seqToApply != lastAppliedSystemSeq)
            {
                lastAppliedSystemSeq = seqToApply;
                lastSystemPacketTime = Time.realtimeSinceStartup;
                ApplySystemStatusJson(jsonToApply);
            }

            UpdateSystemStatusConnection();
        }

        float t1 = SafePercent(p.thumb1, thumbMasterScale);
        float t2 = SafePercent(p.thumb2, thumbMasterScale);
        float t3 = SafePercent(p.thumb3, thumbMasterScale);

        float i1 = SafePercent(p.index1, fingerMasterScale);
        float i2 = SafePercent(p.index2, fingerMasterScale);
        float i3 = SafePercent(p.index3, fingerMasterScale);

        float m1 = SafePercent(p.middle1, fingerMasterScale);
        float m2 = SafePercent(p.middle2, fingerMasterScale);
        float m3 = SafePercent(p.middle3, fingerMasterScale);

        float r1 = SafePercent(p.ring1, fingerMasterScale);
        float r2 = SafePercent(p.ring2, fingerMasterScale);
        float r3 = SafePercent(p.ring3, fingerMasterScale);

        float p1 = SafePercent(p.pinky1, fingerMasterScale);
        float p2 = SafePercent(p.pinky2, fingerMasterScale);
        float p3 = SafePercent(p.pinky3, fingerMasterScale);

        if (FingerLatched(t1, t2, t3, ref latchThumb)) { t1 = 0f; t2 = 0f; t3 = 0f; }
        if (FingerLatched(i1, i2, i3, ref latchIndex)) { i1 = 0f; i2 = 0f; i3 = 0f; }
        if (FingerLatched(m1, m2, m3, ref latchMiddle)) { m1 = 0f; m2 = 0f; m3 = 0f; }
        if (FingerLatched(r1, r2, r3, ref latchRing)) { r1 = 0f; r2 = 0f; r3 = 0f; }
        if (FingerLatched(p1, p2, p3, ref latchPinky)) { p1 = 0f; p2 = 0f; p3 = 0f; }

        cur_thumb1 = SmoothToward(cur_thumb1, t1);
        cur_thumb2 = SmoothToward(cur_thumb2, t2);
        cur_thumb3 = SmoothToward(cur_thumb3, t3);

        cur_index1 = SmoothToward(cur_index1, i1);
        cur_index2 = SmoothToward(cur_index2, i2);
        cur_index3 = SmoothToward(cur_index3, i3);

        cur_middle1 = SmoothToward(cur_middle1, m1);
        cur_middle2 = SmoothToward(cur_middle2, m2);
        cur_middle3 = SmoothToward(cur_middle3, m3);

        cur_ring1 = SmoothToward(cur_ring1, r1);
        cur_ring2 = SmoothToward(cur_ring2, r2);
        cur_ring3 = SmoothToward(cur_ring3, r3);

        cur_pinky1 = SmoothToward(cur_pinky1, p1);
        cur_pinky2 = SmoothToward(cur_pinky2, p2);
        cur_pinky3 = SmoothToward(cur_pinky3, p3);

        // ===== UI bridge values 0..1 =====
        thumbValue = Mathf.Clamp01((0.20f * cur_thumb1 + 0.35f * cur_thumb2 + 0.45f * cur_thumb3) / 100f);
        indexValue = Mathf.Clamp01((0.45f * cur_index1 + 0.35f * cur_index2 + 0.20f * cur_index3) / 100f);
        middleValue = Mathf.Clamp01((0.45f * cur_middle1 + 0.35f * cur_middle2 + 0.20f * cur_middle3) / 100f);
        ringValue = Mathf.Clamp01((0.45f * cur_ring1 + 0.35f * cur_ring2 + 0.20f * cur_ring3) / 100f);
        pinkyValue = Mathf.Clamp01((0.45f * cur_pinky1 + 0.35f * cur_pinky2 + 0.20f * cur_pinky3) / 100f);
        if (fingerStatusUI != null)
        {
            fingerStatusUI.SetLiveData(
                thumbValue,
                indexValue,
                middleValue,
                ringValue,
                pinkyValue,
                0f,
                0f,
                0f,
                0f,
                0f
            );
        }

        // Luu JSON de cap nhat FingerStatus o LateUpdate.
        // Ly do: neu script khac ghi de UI trong Update, LateUpdate se ghi sau cung.
        lastJsonForFingerStatus = jsonToApply;

        ApplyThumbBase(
            thumb1, thumb1Start,
            cur_thumb1,
            thumbBaseCurlFromThumb2 * cur_thumb2 + GripBias(cur_thumb1, cur_thumb2, cur_thumb3, thumbGripBias.x)
        );

        ApplyJoint(
            thumb2, thumb2Start, thumb2Axis,
            PercentToAngle(cur_thumb2, thumbDistalMax.x, thumbCurvePower) + GripBias(cur_thumb1, cur_thumb2, cur_thumb3, thumbGripBias.x),
            thumb2Invert
        );

        ApplyJoint(
            thumb3, thumb3Start, thumb3Axis,
            PercentToAngle(cur_thumb3, thumbDistalMax.y, thumbCurvePower) + GripBias(cur_thumb1, cur_thumb2, cur_thumb3, thumbGripBias.y),
            thumb3Invert
        );

        ApplyJoint(index1, index1Start, indexAxis, AngleFromFinger(cur_index1, fingerMaxBend.x + indexAdd.x, 1) + InwardExtra(cur_index1, cur_index2, cur_index3, indexInwardBias.x) + GripBias(cur_index1, cur_index2, cur_index3, indexGripBias.x), indexInvert);
        ApplyJoint(index2, index2Start, indexAxis, AngleFromFinger(cur_index2, fingerMaxBend.y + indexAdd.y, 2) + InwardExtra(cur_index1, cur_index2, cur_index3, indexInwardBias.y) + GripBias(cur_index1, cur_index2, cur_index3, indexGripBias.y), indexInvert);
        ApplyJoint(index3, index3Start, indexAxis, AngleFromFinger(cur_index3, fingerMaxBend.z + indexAdd.z, 3) + InwardExtra(cur_index1, cur_index2, cur_index3, indexInwardBias.z) + GripBias(cur_index1, cur_index2, cur_index3, indexGripBias.z), indexInvert);

        ApplyJoint(middle1, middle1Start, middleAxis, AngleFromFinger(cur_middle1, fingerMaxBend.x + middleAdd.x, 1) + InwardExtra(cur_middle1, cur_middle2, cur_middle3, middleInwardBias.x) + GripBias(cur_middle1, cur_middle2, cur_middle3, middleGripBias.x), middleInvert);
        ApplyJoint(middle2, middle2Start, middleAxis, AngleFromFinger(cur_middle2, fingerMaxBend.y + middleAdd.y, 2) + InwardExtra(cur_middle1, cur_middle2, cur_middle3, middleInwardBias.y) + GripBias(cur_middle1, cur_middle2, cur_middle3, middleGripBias.y), middleInvert);
        ApplyJoint(middle3, middle3Start, middleAxis, AngleFromFinger(cur_middle3, fingerMaxBend.z + middleAdd.z, 3) + InwardExtra(cur_middle1, cur_middle2, cur_middle3, middleInwardBias.z) + GripBias(cur_middle1, cur_middle2, cur_middle3, middleGripBias.z), middleInvert);

        ApplyJoint(ring1, ring1Start, ringAxis, AngleFromFinger(cur_ring1, fingerMaxBend.x + ringAdd.x, 1) + InwardExtra(cur_ring1, cur_ring2, cur_ring3, ringInwardBias.x) + GripBias(cur_ring1, cur_ring2, cur_ring3, ringGripBias.x), ringInvert);
        ApplyJoint(ring2, ring2Start, ringAxis, AngleFromFinger(cur_ring2, fingerMaxBend.y + ringAdd.y, 2) + InwardExtra(cur_ring1, cur_ring2, cur_ring3, ringInwardBias.y) + GripBias(cur_ring1, cur_ring2, cur_ring3, ringGripBias.y), ringInvert);
        ApplyJoint(ring3, ring3Start, ringAxis, AngleFromFinger(cur_ring3, fingerMaxBend.z + ringAdd.z, 3) + InwardExtra(cur_ring1, cur_ring2, cur_ring3, ringInwardBias.z) + GripBias(cur_ring1, cur_ring2, cur_ring3, ringGripBias.z), ringInvert);

        ApplyJoint(pinky1, pinky1Start, pinkyAxis, AngleFromFinger(cur_pinky1, fingerMaxBend.x + pinkyAdd.x, 1) + InwardExtra(cur_pinky1, cur_pinky2, cur_pinky3, pinkyInwardBias.x) + GripBias(cur_pinky1, cur_pinky2, cur_pinky3, pinkyGripBias.x), pinkyInvert);
        ApplyJoint(pinky2, pinky2Start, pinkyAxis, AngleFromFinger(cur_pinky2, fingerMaxBend.y + pinkyAdd.y, 2) + InwardExtra(cur_pinky1, cur_pinky2, cur_pinky3, pinkyInwardBias.y) + GripBias(cur_pinky1, cur_pinky2, cur_pinky3, pinkyGripBias.y), pinkyInvert);
        ApplyJoint(pinky3, pinky3Start, pinkyAxis, AngleFromFinger(cur_pinky3, fingerMaxBend.z + pinkyAdd.z, 3) + InwardExtra(cur_pinky1, cur_pinky2, cur_pinky3, pinkyInwardBias.z) + GripBias(cur_pinky1, cur_pinky2, cur_pinky3, pinkyGripBias.z), pinkyInvert);
    }


    void LateUpdate()
    {
        // Ghi UI sau cung trong frame de khong bi script khac ghi de ve 0.
        UpdateFingerStatusUI(lastJsonForFingerStatus);
    }

    void SetupFingerStatusUI()
    {
        if (fingerStatusUI == null)
            fingerStatusUI = GetComponent<FingerStatusUI>();

#if UNITY_2023_1_OR_NEWER
        if (fingerStatusUI == null)
            fingerStatusUI = FindFirstObjectByType<FingerStatusUI>();
#else
        if (fingerStatusUI == null)
            fingerStatusUI = FindObjectOfType<FingerStatusUI>();
#endif
    }

    float ForceNTo01(float forceN)
    {
        return Mathf.Clamp01(Mathf.Max(0f, forceN) / Mathf.Max(fingerStatusForceMaxN, 0.001f));
    }

    void UpdateFingerStatusUI(string json)
    {
        if (!updateFingerStatusUI) return;

        if (fingerStatusUI == null)
            SetupFingerStatusUI();

        if (fingerStatusUI == null) return;

        string objLabel = "";
        float totalTargetN = 0f;
        float thumbActualForceN = 0f;
        float indexTargetN = 0f;
        float middleTargetN = 0f;
        float ringTargetN = 0f;
        float pinkyTargetN = 0f;

        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                HandForcePacketMini fp = JsonUtility.FromJson<HandForcePacketMini>(json);
                if (fp != null)
                    thumbActualForceN = Mathf.Max(0f, fp.thumb_force);
            }
            catch
            {
            }

            try
            {
                HandTargetForcePacket tp = JsonUtility.FromJson<HandTargetForcePacket>(json);
                if (tp != null)
                {
                    objLabel = !string.IsNullOrEmpty(tp.object_label) ? tp.object_label : tp.object_raw_label;

                    if (tp.object_target_force_n > 0.001f)
                        totalTargetN = tp.object_target_force_n;
                    else if (tp.target_total_n > 0.001f)
                        totalTargetN = tp.target_total_n;
                    else if (tp.recommended_n > 0.001f)
                        totalTargetN = tp.recommended_n;

                    indexTargetN = Mathf.Max(0f, tp.index_target_n);
                    middleTargetN = Mathf.Max(0f, tp.middle_target_n);
                    ringTargetN = Mathf.Max(0f, tp.ring_target_n);
                    pinkyTargetN = Mathf.Max(0f, tp.pinky_target_n);
                }
            }
            catch
            {
            }

            // Fallback lay object label neu packet target khong co.
            if (string.IsNullOrEmpty(objLabel))
            {
                try
                {
                    HandSystemStatusPacket sp = JsonUtility.FromJson<HandSystemStatusPacket>(json);
                    if (sp != null)
                        objLabel = sp.object_label;
                }
                catch
                {
                }
            }
        }

        // V23:
        // Finger Status khong hien actual FSR force nua.
        // Actual force da co khung Finger Force / Force Analysis.
        // O day chi hien target force dong do Python tinh.
        if (!string.IsNullOrEmpty(objLabel))
        {
            if (totalTargetN > 0.001f)
            {
                fingerStatusUI.SetDynamicFingerTargets(
                    objLabel,
                    totalTargetN,
                    indexTargetN,
                    middleTargetN,
                    ringTargetN,
                    pinkyTargetN
                );
            }
            else
            {
                fingerStatusUI.ApplyObjectTargetByLabel(objLabel);
            }
        }

        // THUMB khong tham gia chia target kep, nhung van hien luc thumb/palm sensor thuc te.
        fingerStatusUI.SetThumbPalmForceN(thumbActualForceN);

        fingerStatusUI.SetLiveData(
            thumbValue,
            indexValue,
            middleValue,
            ringValue,
            pinkyValue,
            0f,
            0f,
            0f,
            0f,
            0f
        );

        if (debugFingerStatusBridge)
        {
            Debug.Log(
                "HAND->FINGER_STATUS V23 Target: " +
                "UI=" + fingerStatusUI.name +
                $" T={thumbValue * 100f:0} I={indexValue * 100f:0} M={middleValue * 100f:0} R={ringValue * 100f:0} P={pinkyValue * 100f:0} " +
                $"Obj={objLabel} TotalTarget={totalTargetN:0.00}N ThumbPalm={thumbActualForceN:0.00}N " +
                $"Target I={indexTargetN:0.00} M={middleTargetN:0.00} R={ringTargetN:0.00} P={pinkyTargetN:0.00}"
            );
        }
    }


    void ApplyThumbBase(Transform bone, Quaternion startRot, float opposePercent, float curlPercent)
    {
        if (!bone) return;

        float opposeSign = thumb1OpposeInvert ? -1f : 1f;
        float curlSign = thumb1CurlInvert ? -1f : 1f;

        float opposeAngle = PercentToAngle(opposePercent, thumb1BaseMax.x, thumbCurvePower);
        float curlAngle = PercentToAngle(curlPercent, thumb1BaseMax.y, thumbCurvePower);

        Vector3 opposeAxis = thumb1OpposeAxis.normalized;
        Vector3 curlAxis = thumb1CurlAxis.normalized;

        bone.localRotation =
            startRot
            * Quaternion.AngleAxis(curlSign * curlAngle, curlAxis)
            * Quaternion.AngleAxis(opposeSign * opposeAngle, opposeAxis);
    }

    void ApplyJoint(Transform bone, Quaternion startRot, Vector3 axis, float angleDeg, bool invert)
    {
        if (!bone) return;

        float sign = invert ? -1f : 1f;
        Vector3 a = axis.normalized;
        bone.localRotation = startRot * Quaternion.AngleAxis(sign * angleDeg, a);
    }

    void CloseReceiver()
    {
        running = false;

        try { udp?.Close(); } catch { }

        try
        {
            if (recvThread != null && recvThread.IsAlive)
                recvThread.Join(100);
        }
        catch { }

        if (socketReady)
        {
            socketReady = false;
            portInUse = false;
        }
    }

    void OnDisable()
    {
        CloseReceiver();
    }

    void OnApplicationQuit()
    {
        CloseReceiver();
    }
}
