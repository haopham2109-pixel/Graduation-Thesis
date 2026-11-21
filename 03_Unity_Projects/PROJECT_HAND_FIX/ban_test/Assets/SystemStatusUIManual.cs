using System.Globalization;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/*
DATN - SYSTEM STATUS UI MANUAL V2 SAFE
- Dieu khien bang SYSTEM STATUS:
  Mode, Object, Grip State, Latency, Sync, Force Mode.
- Nhan truc tiep data tu HandUdpReceiver / Python JSON port 5052.
- Khong hien Palm Gate LOCK/UNLOCK.
- Object moi: none / orange / kimquat_sauce / red_chili.
*/

public class SystemStatusUIManual : MonoBehaviour
{
    [Header("SYSTEM STATUS TEXTS")]
    public TMP_Text modeText;
    public TMP_Text objectText;
    public TMP_Text gripText;
    public TMP_Text latencyText;
    public TMP_Text syncText;
    public TMP_Text forceModeText;

    [Header("AUTO GRIP STATE SOURCE - OPTIONAL")]
    public TMP_Text sensorReadingText;
    public TMP_Text requiredRangeText;

    [Header("STATUS VALUES")]
    public string modeValue = "Tracking";
    public string objectValue = "None";
    public string forceModeValue = "Standby";
    public string manualGripState = "Idle";
    public int latencyMs = 0;
    public bool syncGood = false;

    [Header("GRIP STATE SETTINGS")]
    public bool autoGripState = false;
    public float contactThresholdN = 0.08f;
    public float fallbackRequiredMinN = 4.5f;
    public float fallbackRequiredMaxN = 6.5f;
    public float highForceFactor = 1.15f;
    public float dangerForceFactor = 1.35f;

    [Header("COLORS")]
    public Color labelColor = new Color32(190, 255, 255, 255);
    public Color cyanColor = new Color32(0, 245, 255, 255);
    public Color greenColor = new Color32(45, 255, 120, 255);
    public Color yellowColor = new Color32(255, 210, 0, 255);
    public Color orangeColor = new Color32(255, 145, 0, 255);
    public Color redColor = new Color32(255, 45, 45, 255);
    public Color whiteColor = new Color32(255, 255, 255, 255);

    [Header("Options")]
    public bool updateEveryFrame = true;
    public bool forceTextColorWhite = false;

    private void Start()
    {
        Refresh();
    }

    private void Update()
    {
        if (updateEveryFrame)
            Refresh();
    }

    [ContextMenu("Refresh System Status")]
    public void Refresh()
    {
        string gripState = manualGripState;
        Color gripColor = cyanColor;

        if (autoGripState)
        {
            float forceN = ReadCurrentForceN();
            float minN, maxN;
            ReadRequiredRange(out minN, out maxN);

            gripState = GetGripState(forceN, minN, maxN);
            gripColor = GetGripColor(gripState);
        }
        else
        {
            gripColor = GetGripColor(gripState);
        }

        SetLine(modeText, "Mode:", modeValue, GetModeColor(modeValue));
        SetLine(objectText, "Object:", objectValue, GetObjectColor(objectValue));
        SetLine(gripText, "Grip State:", gripState, gripColor);
        SetLine(latencyText, "Latency:", latencyMs.ToString() + " ms", syncGood ? (latencyMs <= 80 ? greenColor : yellowColor) : redColor);
        SetLine(syncText, "Sync:", syncGood ? "Good" : "Lost", syncGood ? greenColor : redColor);
        SetLine(forceModeText, "Force Mode:", forceModeValue, GetForceModeColor(forceModeValue));
    }

    public void ApplyPythonPacket(
        string objectLabel,
        float objectMassG,
        string autoGripState,
        string autoGripStatus,
        string forceStatus,
        bool gripActive,
        bool forceHoldAllowed
    )
    {
        SetMode(NormalizeMode(autoGripState));
        SetObjectInfo(objectLabel, objectMassG);
        SetGripState(NormalizeGripState(autoGripState, autoGripStatus));
        SetForceStatus(forceStatus, gripActive, forceHoldAllowed);
        Refresh();
    }

    public void SetMode(string value)
    {
        modeValue = string.IsNullOrWhiteSpace(value) ? "Tracking" : value;
        Refresh();
    }

    public void SetObjectName(string value)
    {
        objectValue = string.IsNullOrWhiteSpace(value) ? "None" : value;
        Refresh();
    }

    public void SetObjectInfo(string label, float massG)
    {
        objectValue = NormalizeObjectLabel(label, massG);
        Refresh();
    }

    public void SetGripState(string value)
    {
        autoGripState = false;
        manualGripState = string.IsNullOrWhiteSpace(value) ? "Idle" : value;
        Refresh();
    }

    public void SetForceMode(string value)
    {
        forceModeValue = string.IsNullOrWhiteSpace(value) ? "Standby" : value;
        Refresh();
    }

    public void SetForceStatus(string status, bool gripActive, bool forceHoldAllowed)
    {
        forceModeValue = NormalizeForceStatus(status, gripActive, forceHoldAllowed);
        Refresh();
    }

    public void SetSync(bool good)
    {
        syncGood = good;
        Refresh();
    }

    public void SetLatencyMs(int ms)
    {
        latencyMs = Mathf.Max(0, ms);
        Refresh();
    }

    private string NormalizeMode(string state)
    {
        string s = SafeUpper(state);

        if (s.Contains("FREE_TRACKING")) return "Auto Tracking";
        if (s.Contains("OBJECT_LOCKED")) return "Object Locked";
        if (s.Contains("WAIT_PALM")) return "Wait Palm";
        if (s.Contains("VISION_GRIP") || s.Contains("VISION_CLOSING")) return "Vision Closing";
        if (s.Contains("FORCE_HOLD")) return "Force Hold";
        if (s.Contains("RELEASE")) return "Release";

        if (string.IsNullOrWhiteSpace(state)) return "Tracking";
        return CleanStateText(state);
    }

    private string NormalizeGripState(string state, string status)
    {
        string st = SafeUpper(state);
        string ss = SafeUpper(status);

        if (st.Contains("FREE_TRACKING")) return "Free Tracking";
        if (st.Contains("OBJECT_LOCKED")) return "Object Locked";
        if (st.Contains("WAIT_PALM") || ss.Contains("WAIT PALM")) return "Wait Palm Contact";
        if (st.Contains("VISION_GRIP") || ss.Contains("FOLLOW HAND")) return "Vision Closing";
        if (st.Contains("FORCE_HOLD") || ss.Contains("FORCE HOLD")) return "Force Hold";
        if (st.Contains("RELEASE")) return "Release";

        if (!string.IsNullOrWhiteSpace(status)) return CleanStateText(status);
        return "Idle";
    }

    private string NormalizeObjectLabel(string label, float massG)
    {
        string s = SafeUpper(label);

        if (string.IsNullOrWhiteSpace(label) || s == "NONE" || s.Contains("NO"))
            return "None";

        string mass = massG > 0.01f ? " " + massG.ToString("0") + "g" : "";

        if (s.Contains("ORANGE")) return "Orange" + mass;
        if (s.Contains("KIMQUAT") || s.Contains("SAUCE")) return "Kimquat Sauce" + mass;
        if (s.Contains("RED_CHILI") || s.Contains("CHILI")) return "Red Chili" + mass;

        return CleanStateText(label) + mass;
    }

    private string NormalizeForceStatus(string status, bool gripActive, bool forceHoldAllowed)
    {
        string s = SafeUpper(status);

        if (forceHoldAllowed || gripActive) return "Active Hold";
        if (string.IsNullOrWhiteSpace(status)) return "Standby";
        if (s.Contains("NO_OBJECT") || s.Contains("NO TARGET")) return "Standby";
        if (s.Contains("WAIT_CONTACT")) return "Wait Contact";
        if (s.Contains("LOW_GRIP")) return "Low Grip";
        if (s.Contains("OPTIMAL")) return "Stable Hold";
        if (s.Contains("HIGH")) return "High Force";
        if (s.Contains("DANGER")) return "Danger";

        return CleanStateText(status);
    }

    private string CleanStateText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        string s = raw.Replace("_AUTO", "");
        s = s.Replace("_", " ");
        s = s.ToLowerInvariant();

        TextInfo ti = CultureInfo.InvariantCulture.TextInfo;
        return ti.ToTitleCase(s);
    }

    private string GetGripState(float forceN, float minN, float maxN)
    {
        if (forceN < contactThresholdN) return "Idle";
        if (forceN < minN) return "Contact";
        if (forceN <= maxN) return "Stable Hold";
        if (forceN <= maxN * highForceFactor) return "High Force";
        if (forceN <= maxN * dangerForceFactor) return "Warning";
        return "Danger";
    }

    private Color GetGripColor(string state)
    {
        string s = SafeUpper(state);

        if (s.Contains("IDLE") || s.Contains("FREE")) return cyanColor;
        if (s.Contains("WAIT") || s.Contains("CONTACT") || s.Contains("OBJECT")) return yellowColor;
        if (s.Contains("STABLE") || s.Contains("HOLD") || s.Contains("TRACK") || s.Contains("VISION")) return greenColor;
        if (s.Contains("HIGH") || s.Contains("WARN") || s.Contains("RELEASE")) return orangeColor;
        if (s.Contains("DANGER") || s.Contains("LOST")) return redColor;

        return cyanColor;
    }

    private Color GetModeColor(string mode)
    {
        string s = SafeUpper(mode);

        if (s.Contains("TRACK") || s.Contains("FORCE") || s.Contains("VISION")) return greenColor;
        if (s.Contains("OBJECT") || s.Contains("WAIT")) return yellowColor;
        if (s.Contains("RELEASE")) return orangeColor;
        if (s.Contains("NO") || s.Contains("STANDBY") || s.Contains("LOST")) return redColor;

        return cyanColor;
    }

    private Color GetObjectColor(string objectName)
    {
        string s = SafeUpper(objectName);

        if (s.Contains("ORANGE")) return orangeColor;
        if (s.Contains("KIMQUAT") || s.Contains("SAUCE")) return yellowColor;
        if (s.Contains("CHILI") || s.Contains("RED")) return redColor;
        if (s.Contains("NONE") || s.Contains("NO")) return yellowColor;

        return cyanColor;
    }

    private Color GetForceModeColor(string mode)
    {
        string s = SafeUpper(mode);

        if (s.Contains("ACTIVE") || s.Contains("STABLE") || s.Contains("OPTIMAL") || s.Contains("HOLD")) return greenColor;
        if (s.Contains("WAIT") || s.Contains("LOW") || s.Contains("STANDBY")) return yellowColor;
        if (s.Contains("HIGH") || s.Contains("WARN")) return orangeColor;
        if (s.Contains("DANGER") || s.Contains("LOST")) return redColor;

        return cyanColor;
    }

    private float ReadCurrentForceN()
    {
        if (sensorReadingText == null) return 0f;
        return ParseFirstFloat(sensorReadingText.text);
    }

    private void ReadRequiredRange(out float minN, out float maxN)
    {
        minN = fallbackRequiredMinN;
        maxN = fallbackRequiredMaxN;

        if (requiredRangeText == null || string.IsNullOrWhiteSpace(requiredRangeText.text))
            return;

        MatchCollection matches = Regex.Matches(requiredRangeText.text.Replace(",", "."), @"[-+]?\d*\.?\d+");

        if (matches.Count >= 2)
        {
            float a, b;
            if (float.TryParse(matches[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                float.TryParse(matches[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b))
            {
                minN = Mathf.Min(a, b);
                maxN = Mathf.Max(a, b);
            }
        }
    }

    private float ParseFirstFloat(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0f;

        Match m = Regex.Match(text.Replace(",", "."), @"[-+]?\d*\.?\d+");
        if (!m.Success) return 0f;

        float value;
        if (float.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return Mathf.Max(0f, value);

        return 0f;
    }

    private void SetLine(TMP_Text target, string label, string value, Color valueColor)
    {
        if (target == null) return;

        target.richText = true;

        if (forceTextColorWhite)
        {
            target.color = whiteColor;
            target.text = label + " " + value;
            return;
        }

        target.text =
            "<color=#" + ColorHex(labelColor) + ">" + label + "</color> " +
            "<color=#" + ColorHex(valueColor) + ">" + value + "</color>";
    }

    private string ColorHex(Color color)
    {
        Color32 c = color;
        return c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
    }

    private string SafeUpper(string s)
    {
        return string.IsNullOrEmpty(s) ? "" : s.ToUpperInvariant();
    }
}
