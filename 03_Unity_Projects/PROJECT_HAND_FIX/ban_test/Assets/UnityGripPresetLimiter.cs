using UnityEngine;

/*
DATN - UNITY GRIP PRESET LIMITER ADDON V1

Muc dich:
- KHONG thay the HandUdpReceiver.cs.
- Gan script nay vao cung GameObject dang co HandUdpReceiver.
- Script chay o LateUpdate, sau khi HandUdpReceiver da xoay tay.
- Neu dang co vat trong long ban tay, no se chan cac ngon khong vuot qua % preset cua vat.
- Neu nguoi dung mo tay nho hon preset thi khong can thiep.

Cach dung:
1) Tao file UnityGripPresetLimiter.cs trong Assets/Scripts.
2) Copy toan bo noi dung file txt nay vao file .cs do.
3) Add component UnityGripPresetLimiter vao GameObject co HandUdpReceiver.
4) Gan HandUdpReceiver va PythonObjectUdpLink neu Unity chua tu tim thay.
*/

public class UnityGripPresetLimiter : MonoBehaviour
{
    [Header("References")]
    public HandUdpReceiver hand;
    public PythonObjectUdpLink objectLink;

    [Header("Enable")]
    public bool enableLimit = true;
    public bool debugLog = false;

    [Header("Orange preset (%)")]
    [Range(0f, 100f)] public float orangeThumb = 50f;
    [Range(0f, 100f)] public float orangeIndex = 75f;
    [Range(0f, 100f)] public float orangeMiddle = 78f;
    [Range(0f, 100f)] public float orangeRing = 70f;
    [Range(0f, 100f)] public float orangePinky = 70f;

    [Header("Kimquat Sauce preset (%)")]
    [Range(0f, 100f)] public float kimquatThumb = 59f;
    [Range(0f, 100f)] public float kimquatIndex = 85f;
    [Range(0f, 100f)] public float kimquatMiddle = 80f;
    [Range(0f, 100f)] public float kimquatRing = 78f;
    [Range(0f, 100f)] public float kimquatPinky = 68f;

    [Header("Red Chili preset (%)")]
    [Range(0f, 100f)] public float chiliThumb = 32f;
    [Range(0f, 100f)] public float chiliIndex = 72f;
    [Range(0f, 100f)] public float chiliMiddle = 70f;
    [Range(0f, 100f)] public float chiliRing = 82f;
    [Range(0f, 100f)] public float chiliPinky = 83f;

    private Quaternion thumb1Start, thumb2Start, thumb3Start;
    private Quaternion index1Start, index2Start, index3Start;
    private Quaternion middle1Start, middle2Start, middle3Start;
    private Quaternion ring1Start, ring2Start, ring3Start;
    private Quaternion pinky1Start, pinky2Start, pinky3Start;

    private string lastObj = "none";

    private void Awake()
    {
        FindRefsIfNeeded();
    }

    private void Start()
    {
        FindRefsIfNeeded();
        SaveStartRotations();
    }

    private void FindRefsIfNeeded()
    {
        if (hand == null)
            hand = GetComponent<HandUdpReceiver>();

#if UNITY_2023_1_OR_NEWER
        if (hand == null)
            hand = FindFirstObjectByType<HandUdpReceiver>();
        if (objectLink == null)
            objectLink = FindFirstObjectByType<PythonObjectUdpLink>();
#else
        if (hand == null)
            hand = FindObjectOfType<HandUdpReceiver>();
        if (objectLink == null)
            objectLink = FindObjectOfType<PythonObjectUdpLink>();
#endif
    }

    private void SaveStartRotations()
    {
        if (hand == null) return;

        if (hand.thumb1) thumb1Start = hand.thumb1.localRotation;
        if (hand.thumb2) thumb2Start = hand.thumb2.localRotation;
        if (hand.thumb3) thumb3Start = hand.thumb3.localRotation;

        if (hand.index1) index1Start = hand.index1.localRotation;
        if (hand.index2) index2Start = hand.index2.localRotation;
        if (hand.index3) index3Start = hand.index3.localRotation;

        if (hand.middle1) middle1Start = hand.middle1.localRotation;
        if (hand.middle2) middle2Start = hand.middle2.localRotation;
        if (hand.middle3) middle3Start = hand.middle3.localRotation;

        if (hand.ring1) ring1Start = hand.ring1.localRotation;
        if (hand.ring2) ring2Start = hand.ring2.localRotation;
        if (hand.ring3) ring3Start = hand.ring3.localRotation;

        if (hand.pinky1) pinky1Start = hand.pinky1.localRotation;
        if (hand.pinky2) pinky2Start = hand.pinky2.localRotation;
        if (hand.pinky3) pinky3Start = hand.pinky3.localRotation;
    }

    private void LateUpdate()
    {
        if (!enableLimit || hand == null)
            return;

        string obj = GetObjectKey();
        if (obj == "none")
        {
            lastObj = obj;
            return;
        }

        if (debugLog && obj != lastObj)
            Debug.Log("UnityGripPresetLimiter active object = " + obj);
        lastObj = obj;

        float capThumb, capIndex, capMiddle, capRing, capPinky;
        GetCaps(obj, out capThumb, out capIndex, out capMiddle, out capRing, out capPinky);

        // hand.xxxValue la gia tri 0..1 da duoc HandUdpReceiver tinh sau smoothing.
        // Neu nho hon cap thi khong can thiep, de HandUdpReceiver dieu khien binh thuong.
        // Neu lon hon cap thi ghi de rotation ve muc cap de ngan xuyen vat.
        if (hand.thumbValue * 100f > capThumb)
            ApplyThumb(capThumb);

        if (hand.indexValue * 100f > capIndex)
            ApplyIndex(capIndex);

        if (hand.middleValue * 100f > capMiddle)
            ApplyMiddle(capMiddle);

        if (hand.ringValue * 100f > capRing)
            ApplyRing(capRing);

        if (hand.pinkyValue * 100f > capPinky)
            ApplyPinky(capPinky);
    }

    private string GetObjectKey()
    {
        if (objectLink == null)
            return "none";

        string s = "";
        if (!string.IsNullOrEmpty(objectLink.lastRawMessage))
            s += objectLink.lastRawMessage + " ";
        if (!string.IsNullOrEmpty(objectLink.lastObjectName))
            s += objectLink.lastObjectName;

        s = s.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(s)) return "none";

        if (s.Contains("kimquat") || s.Contains("kim_quat") || s.Contains("kim qu") || s.Contains("sauce"))
            return "kimquat_sauce";
        if (s.Contains("red_chili") || s.Contains("chili") || s.Contains("ot_bot") || s.Contains("ot do") || s.Contains("ớt") || s.Contains("bot") || s.Contains("bột"))
            return "red_chili";
        if (s.Contains("orange") || s.Contains("cam"))
            return "orange";

        return "none";
    }

    private void GetCaps(string obj, out float th, out float idx, out float mid, out float ring, out float pinky)
    {
        if (obj == "orange")
        {
            th = orangeThumb; idx = orangeIndex; mid = orangeMiddle; ring = orangeRing; pinky = orangePinky;
            return;
        }

        if (obj == "kimquat_sauce")
        {
            th = kimquatThumb; idx = kimquatIndex; mid = kimquatMiddle; ring = kimquatRing; pinky = kimquatPinky;
            return;
        }

        if (obj == "red_chili")
        {
            th = chiliThumb; idx = chiliIndex; mid = chiliMiddle; ring = chiliRing; pinky = chiliPinky;
            return;
        }

        th = idx = mid = ring = pinky = 100f;
    }

    private float Curve01(float x, float power)
    {
        x = Mathf.Clamp01(x);
        power = Mathf.Max(0.01f, power);
        return Mathf.Pow(x, power);
    }

    private float PercentToAngle(float percent, float maxDeg, float power)
    {
        return Mathf.Lerp(0f, maxDeg, Curve01(percent / 100f, power));
    }

    private float AngleFromFinger(float percent, float maxDeg, int jointIndex)
    {
        float power = hand.distalCurvePower;
        if (jointIndex == 1) power = hand.proximalCurvePower;
        else if (jointIndex == 2) power = hand.middleCurvePower;
        return PercentToAngle(percent, maxDeg, power);
    }

    private float InwardExtra(float a, float b, float c, float maxDeg)
    {
        float closure = Mathf.Clamp01((0.45f * a + 0.35f * b + 0.20f * c) / 100f);
        return Mathf.Lerp(0f, maxDeg, Mathf.Pow(closure, 0.90f)) * hand.inwardBlend;
    }

    private float GripBias(float a, float b, float c, float maxDeg)
    {
        float mix = 0.30f * a + 0.45f * b + 0.25f * c;
        if (mix <= hand.gripBiasStart) return 0f;

        float t = Mathf.Clamp01((mix - hand.gripBiasStart) / Mathf.Max(100f - hand.gripBiasStart, 0.001f));
        return Mathf.Lerp(0f, maxDeg, Mathf.Pow(t, 1.18f));
    }

    private void ApplyThumb(float percent)
    {
        float t1 = percent;
        float t2 = percent;
        float t3 = percent;

        ApplyThumbBase(
            hand.thumb1, thumb1Start,
            t1,
            hand.thumbBaseCurlFromThumb2 * t2 + GripBias(t1, t2, t3, hand.thumbGripBias.x)
        );

        ApplyJoint(
            hand.thumb2, thumb2Start, hand.thumb2Axis,
            PercentToAngle(t2, hand.thumbDistalMax.x, hand.thumbCurvePower) + GripBias(t1, t2, t3, hand.thumbGripBias.x),
            hand.thumb2Invert
        );

        ApplyJoint(
            hand.thumb3, thumb3Start, hand.thumb3Axis,
            PercentToAngle(t3, hand.thumbDistalMax.y, hand.thumbCurvePower) + GripBias(t1, t2, t3, hand.thumbGripBias.y),
            hand.thumb3Invert
        );
    }

    private void ApplyIndex(float percent)
    {
        ApplyJoint(hand.index1, index1Start, hand.indexAxis, AngleFromFinger(percent, hand.fingerMaxBend.x + hand.indexAdd.x, 1) + InwardExtra(percent, percent, percent, hand.indexInwardBias.x) + GripBias(percent, percent, percent, hand.indexGripBias.x), hand.indexInvert);
        ApplyJoint(hand.index2, index2Start, hand.indexAxis, AngleFromFinger(percent, hand.fingerMaxBend.y + hand.indexAdd.y, 2) + InwardExtra(percent, percent, percent, hand.indexInwardBias.y) + GripBias(percent, percent, percent, hand.indexGripBias.y), hand.indexInvert);
        ApplyJoint(hand.index3, index3Start, hand.indexAxis, AngleFromFinger(percent, hand.fingerMaxBend.z + hand.indexAdd.z, 3) + InwardExtra(percent, percent, percent, hand.indexInwardBias.z) + GripBias(percent, percent, percent, hand.indexGripBias.z), hand.indexInvert);
    }

    private void ApplyMiddle(float percent)
    {
        ApplyJoint(hand.middle1, middle1Start, hand.middleAxis, AngleFromFinger(percent, hand.fingerMaxBend.x + hand.middleAdd.x, 1) + InwardExtra(percent, percent, percent, hand.middleInwardBias.x) + GripBias(percent, percent, percent, hand.middleGripBias.x), hand.middleInvert);
        ApplyJoint(hand.middle2, middle2Start, hand.middleAxis, AngleFromFinger(percent, hand.fingerMaxBend.y + hand.middleAdd.y, 2) + InwardExtra(percent, percent, percent, hand.middleInwardBias.y) + GripBias(percent, percent, percent, hand.middleGripBias.y), hand.middleInvert);
        ApplyJoint(hand.middle3, middle3Start, hand.middleAxis, AngleFromFinger(percent, hand.fingerMaxBend.z + hand.middleAdd.z, 3) + InwardExtra(percent, percent, percent, hand.middleInwardBias.z) + GripBias(percent, percent, percent, hand.middleGripBias.z), hand.middleInvert);
    }

    private void ApplyRing(float percent)
    {
        ApplyJoint(hand.ring1, ring1Start, hand.ringAxis, AngleFromFinger(percent, hand.fingerMaxBend.x + hand.ringAdd.x, 1) + InwardExtra(percent, percent, percent, hand.ringInwardBias.x) + GripBias(percent, percent, percent, hand.ringGripBias.x), hand.ringInvert);
        ApplyJoint(hand.ring2, ring2Start, hand.ringAxis, AngleFromFinger(percent, hand.fingerMaxBend.y + hand.ringAdd.y, 2) + InwardExtra(percent, percent, percent, hand.ringInwardBias.y) + GripBias(percent, percent, percent, hand.ringGripBias.y), hand.ringInvert);
        ApplyJoint(hand.ring3, ring3Start, hand.ringAxis, AngleFromFinger(percent, hand.fingerMaxBend.z + hand.ringAdd.z, 3) + InwardExtra(percent, percent, percent, hand.ringInwardBias.z) + GripBias(percent, percent, percent, hand.ringGripBias.z), hand.ringInvert);
    }

    private void ApplyPinky(float percent)
    {
        ApplyJoint(hand.pinky1, pinky1Start, hand.pinkyAxis, AngleFromFinger(percent, hand.fingerMaxBend.x + hand.pinkyAdd.x, 1) + InwardExtra(percent, percent, percent, hand.pinkyInwardBias.x) + GripBias(percent, percent, percent, hand.pinkyGripBias.x), hand.pinkyInvert);
        ApplyJoint(hand.pinky2, pinky2Start, hand.pinkyAxis, AngleFromFinger(percent, hand.fingerMaxBend.y + hand.pinkyAdd.y, 2) + InwardExtra(percent, percent, percent, hand.pinkyInwardBias.y) + GripBias(percent, percent, percent, hand.pinkyGripBias.y), hand.pinkyInvert);
        ApplyJoint(hand.pinky3, pinky3Start, hand.pinkyAxis, AngleFromFinger(percent, hand.fingerMaxBend.z + hand.pinkyAdd.z, 3) + InwardExtra(percent, percent, percent, hand.pinkyInwardBias.z) + GripBias(percent, percent, percent, hand.pinkyGripBias.z), hand.pinkyInvert);
    }

    private void ApplyThumbBase(Transform bone, Quaternion startRot, float opposePercent, float curlPercent)
    {
        if (!bone) return;

        float opposeSign = hand.thumb1OpposeInvert ? -1f : 1f;
        float curlSign = hand.thumb1CurlInvert ? -1f : 1f;

        float opposeAngle = PercentToAngle(opposePercent, hand.thumb1BaseMax.x, hand.thumbCurvePower);
        float curlAngle = PercentToAngle(curlPercent, hand.thumb1BaseMax.y, hand.thumbCurvePower);

        Vector3 opposeAxis = hand.thumb1OpposeAxis.normalized;
        Vector3 curlAxis = hand.thumb1CurlAxis.normalized;

        bone.localRotation =
            startRot
            * Quaternion.AngleAxis(curlSign * curlAngle, curlAxis)
            * Quaternion.AngleAxis(opposeSign * opposeAngle, opposeAxis);
    }

    private void ApplyJoint(Transform bone, Quaternion startRot, Vector3 axis, float angleDeg, bool invert)
    {
        if (!bone) return;

        float sign = invert ? -1f : 1f;
        Vector3 a = axis.normalized;
        bone.localRotation = startRot * Quaternion.AngleAxis(sign * angleDeg, a);
    }
}
