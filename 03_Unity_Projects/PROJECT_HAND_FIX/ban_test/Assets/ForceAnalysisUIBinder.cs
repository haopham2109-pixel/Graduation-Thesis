using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/*
DATN - FORCE ANALYSIS UI BINDER V16 SAFE
MUC TIEU SUA LOI V15:
1) KHONG dong vao FingerStatusUI nua.
   Ly do: FingerStatusUI dang duoc he thong hand/HandUdpReceiver cap nhat goc rieng.
   V15 goi SetLiveData fallback = 0 lam mat Thumb Angle/Index Angle.
   Ban V16 de nguyen FingerStatusUI, khong ghi de goc nua.

2) Top Force Analysis van cap nhat theo hierarchy chinh xac:
   ForceAnalysisPanel/RecommendedGrip/RG_ValueText
   ForceAnalysisPanel/ObjectWeightPanel/OW_ValueText
   ForceAnalysisPanel/RequiredForcePanel/RF_ValueText
   ForceAnalysisPanel/FrictionPanel/FR_ValueText
   ForceAnalysisPanel/SensorReadingPanel/SR_ValueText
   ForceAnalysisPanel/StatusGripPanel/ST_ValueText

3) 5 khung FINGER FORCE duoi:
   Tim theo TITLE "FINGER FORCE 1..5".
   Chi cap nhat text luc dang "... N" va percent noi bo card.
   Khong cap nhat label THUMB/INDEX/MIDDLE/RING/PINKY.
   Khong leo root len qua cao de tranh nham sang FINGER STATUS.
*/

[Serializable]
public class RobotForcePacket
{
    public float mass_kg;
    public float mass_g;
    public float mu;
    public float safety_factor;
    public float weight_n;

    public float recommended_n;
    public float required_min_n;
    public float required_max_n;

    public float sensor_n;
    public float total_force_n;

    public float thumb_force;
    public float index_force;
    public float middle_force;
    public float ring_force;
    public float pinky_force;

    public float thumb_target_n;
    public float index_target_n;
    public float middle_target_n;
    public float ring_target_n;
    public float pinky_target_n;

    public bool grip_active;
    public bool grip_contact;
    public float force_ratio;
    public string status;
}

public class ForceAnalysisUIBinder : MonoBehaviour
{
    [Header("Scale")]
    public float totalForceDisplayMaxN = 20f;
    public float fingerForceDisplayMaxN = 20f;

    [Header("Debug")]
    public bool logFirstPacket = true;
    public bool logBindingOnce = true;
    public bool logFingerCardsOnce = true;

    [Header("Exact Root")]
    public Transform forceAnalysisPanel;

    [Header("Top Texts")]
    public TMP_Text objectWeightText;
    public TMP_Text requiredRangeText;
    public TMP_Text recommendedText;
    public TMP_Text recommendedStateText;
    public TMP_Text recommendedMinText;
    public TMP_Text recommendedMaxText;
    public TMP_Text frictionText;
    public TMP_Text sensorReadingText;
    public TMP_Text statusText;

    [Header("Code Drawn UI")]
    public CurvedGripGaugeUI gripGauge;
    public ForceScaleBarUI forceScaleBar;
    public SensorReadingGraphUI sensorGraph;
    public ST_SegmentedBarUI statusBar;

    [Serializable]
    public class FingerCard
    {
        public string title;
        public string fingerLabel;

        public Transform root;
        public TMP_Text forceText;
        public TMP_Text percentText;
        public Image barFill;

        public FingerCard(string title, string fingerLabel)
        {
            this.title = title;
            this.fingerLabel = fingerLabel;
        }
    }

    [Header("Bottom Finger Force Cards")]
    public FingerCard thumb = new FingerCard("FINGER FORCE 1", "THUMB");
    public FingerCard index = new FingerCard("FINGER FORCE 2", "INDEX");
    public FingerCard middle = new FingerCard("FINGER FORCE 3", "MIDDLE");
    public FingerCard ring = new FingerCard("FINGER FORCE 4", "RING");
    public FingerCard pinky = new FingerCard("FINGER FORCE 5", "PINKY");

    private TMP_Text[] allTexts = new TMP_Text[0];
    private Image[] allImages = new Image[0];

    private bool loggedPacket = false;
    private bool loggedBinding = false;
    private bool loggedFingerCards = false;
    private float lastBindTime = -999f;

    private readonly Color cyan = new Color32(0, 245, 255, 255);
    private readonly Color green = new Color32(0, 255, 122, 255);
    private readonly Color orange = new Color32(255, 176, 0, 255);
    private readonly Color red = new Color32(255, 59, 48, 255);
    private readonly Color gray = new Color32(150, 160, 170, 255);

    private void Awake()
    {
        BindReferences(true);
    }

    public void ApplyJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        RobotForcePacket p;
        try
        {
            p = JsonUtility.FromJson<RobotForcePacket>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ForceAnalysisUIBinder V16 JSON parse error: " + ex.Message);
            return;
        }

        if (p == null) return;

        if (logFirstPacket && !loggedPacket)
        {
            loggedPacket = true;
            Debug.Log("ForceAnalysisUIBinder V16 received first packet: " + json);
        }

        BindReferences(false);

        float measuredTotal = Mathf.Max(0f, p.total_force_n);
        float recommended = Mathf.Max(0f, p.recommended_n);
        float requiredMin = Mathf.Max(0f, p.required_min_n);
        float requiredMax = Mathf.Max(requiredMin, p.required_max_n);

        // Status bar theo 0..20N de khong full khi cham nhe.
        float statusPercent = Mathf.Clamp01(measuredTotal / Mathf.Max(totalForceDisplayMaxN, 0.001f));

        UpdateForceAnalysisTexts(p, measuredTotal, recommended, requiredMin, requiredMax);
        UpdateGeneratedForceAnalysisUI(p, measuredTotal, recommended, requiredMin, requiredMax, statusPercent);

        // QUAN TRONG:
        // KHONG goi FingerStatusUI.SetLiveData o day nua.
        // De script hand/receiver hien tai cua ban tu cap nhat goc Finger Status.

        UpdateFingerCard(thumb, p.thumb_force);
        UpdateFingerCard(index, p.index_force);
        UpdateFingerCard(middle, p.middle_force);
        UpdateFingerCard(ring, p.ring_force);
        UpdateFingerCard(pinky, p.pinky_force);
    }

    private void BindReferences(bool force)
    {
        if (!force && Time.realtimeSinceStartup - lastBindTime < 0.50f) return;
        lastBindTime = Time.realtimeSinceStartup;

#if UNITY_2023_1_OR_NEWER
        allTexts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        allImages = FindObjectsByType<Image>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        allTexts = FindObjectsOfType<TMP_Text>();
        allImages = FindObjectsOfType<Image>();
#endif

        if (forceAnalysisPanel == null)
        {
            GameObject root = GameObject.Find("ForceAnalysisPanel");
            if (root != null) forceAnalysisPanel = root.transform;
        }

        if (forceAnalysisPanel != null)
        {
            recommendedText = recommendedText != null ? recommendedText : FindTextPath("RecommendedGrip/RG_ValueText");
            recommendedStateText = recommendedStateText != null ? recommendedStateText : FindTextPath("RecommendedGrip/RG_StateText");
            recommendedMinText = recommendedMinText != null ? recommendedMinText : FindTextPath("RecommendedGrip/RG_MinText");
            recommendedMaxText = recommendedMaxText != null ? recommendedMaxText : FindTextPath("RecommendedGrip/RG_MaxText");

            if (gripGauge == null)
            {
                Transform t = FindPath("RecommendedGrip/RG_GaugeArc");
                if (t != null) gripGauge = t.GetComponent<CurvedGripGaugeUI>();
            }

            if (forceScaleBar == null)
            {
                Transform t = FindPath("ForceScalePanel/FS_BarGraphic");
                if (t != null) forceScaleBar = t.GetComponent<ForceScaleBarUI>();
            }

            sensorReadingText = sensorReadingText != null ? sensorReadingText : FindTextPath("SensorReadingPanel/SR_ValueText");

            if (sensorGraph == null)
            {
                Transform t = FindPath("SensorReadingPanel/SR_Graph");
                if (t != null) sensorGraph = t.GetComponent<SensorReadingGraphUI>();
            }

            objectWeightText = objectWeightText != null ? objectWeightText : FindTextPath("ObjectWeightPanel/OW_ValueText");
            requiredRangeText = requiredRangeText != null ? requiredRangeText : FindTextPath("RequiredForcePanel/RF_ValueText");
            frictionText = frictionText != null ? frictionText : FindTextPath("FrictionPanel/FR_ValueText");
            statusText = statusText != null ? statusText : FindTextPath("StatusGripPanel/ST_ValueText");

            if (statusBar == null)
            {
                Transform t = FindPath("StatusGripPanel/ST_Bar");
                if (t != null) statusBar = t.GetComponent<ST_SegmentedBarUI>();
            }
        }

        if (objectWeightText == null) objectWeightText = FindTextByGO("OW_ValueText");
        if (requiredRangeText == null) requiredRangeText = FindTextByGO("RF_ValueText");
        if (recommendedText == null) recommendedText = FindTextByGO("RG_ValueText");
        if (frictionText == null) frictionText = FindTextByGO("FR_ValueText");
        if (sensorReadingText == null) sensorReadingText = FindTextByGO("SR_ValueText");
        if (statusText == null) statusText = FindTextByGO("ST_ValueText");

        if (gripGauge == null)
        {
            GameObject go = GameObject.Find("RG_GaugeArc");
            if (go != null) gripGauge = go.GetComponent<CurvedGripGaugeUI>();
        }

        if (forceScaleBar == null)
        {
            GameObject go = GameObject.Find("FS_BarGraphic");
            if (go != null) forceScaleBar = go.GetComponent<ForceScaleBarUI>();
        }

        if (statusBar == null)
        {
            GameObject go = GameObject.Find("ST_Bar");
            if (go != null) statusBar = go.GetComponent<ST_SegmentedBarUI>();
        }

        if (sensorGraph == null)
        {
            GameObject go = GameObject.Find("SR_Graph");
            if (go != null) sensorGraph = go.GetComponent<SensorReadingGraphUI>();
        }

        if (sensorGraph == null)
        {
#if UNITY_2023_1_OR_NEWER
            sensorGraph = FindFirstObjectByType<SensorReadingGraphUI>();
#else
            sensorGraph = FindObjectOfType<SensorReadingGraphUI>();
#endif
        }

        BindFingerCard(thumb);
        BindFingerCard(index);
        BindFingerCard(middle);
        BindFingerCard(ring);
        BindFingerCard(pinky);

        if (logBindingOnce && !loggedBinding)
        {
            loggedBinding = true;
            Debug.Log(
                "V16 TOP BIND: " +
                "Root=" + NameOf(forceAnalysisPanel) +
                " OW=" + NameOf(objectWeightText) +
                " RF=" + NameOf(requiredRangeText) +
                " RG=" + NameOf(recommendedText) +
                " FR=" + NameOf(frictionText) +
                " SR=" + NameOf(sensorReadingText) +
                " ST=" + NameOf(statusText) +
                " Gauge=" + NameOf(gripGauge) +
                " Scale=" + NameOf(forceScaleBar) +
                " Graph=" + NameOf(sensorGraph) +
                " STBar=" + NameOf(statusBar)
            );
        }

        if (logFingerCardsOnce && !loggedFingerCards)
        {
            loggedFingerCards = true;
            Debug.Log(
                "V16 FINGER CARDS: " +
                "T=" + CardInfo(thumb) + " | " +
                "I=" + CardInfo(index) + " | " +
                "M=" + CardInfo(middle) + " | " +
                "R=" + CardInfo(ring) + " | " +
                "P=" + CardInfo(pinky)
            );
        }
    }

    private string CardInfo(FingerCard c)
    {
        if (c == null) return "null";
        return "root=" + NameOf(c.root) + ", force=" + NameOf(c.forceText) + ", pct=" + NameOf(c.percentText) + ", bar=" + NameOf(c.barFill);
    }

    private Transform FindPath(string path)
    {
        if (forceAnalysisPanel == null || string.IsNullOrEmpty(path)) return null;
        return forceAnalysisPanel.Find(path);
    }

    private TMP_Text FindTextPath(string path)
    {
        Transform t = FindPath(path);
        if (t == null) return null;

        TMP_Text txt = t.GetComponent<TMP_Text>();
        if (txt != null) return txt;

        return t.GetComponentInChildren<TMP_Text>(true);
    }

    private TMP_Text FindTextByGO(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go == null) return null;

        TMP_Text txt = go.GetComponent<TMP_Text>();
        if (txt != null) return txt;

        return go.GetComponentInChildren<TMP_Text>(true);
    }

    private string NameOf(UnityEngine.Object obj)
    {
        return obj == null ? "None" : obj.name;
    }

    private void UpdateForceAnalysisTexts(RobotForcePacket p, float measuredTotal, float recommended, float requiredMin, float requiredMax)
    {
        if (objectWeightText != null)
            objectWeightText.text = $"{p.mass_kg:0.000} kg";

        if (requiredRangeText != null)
            requiredRangeText.text = $"{requiredMin:0.00} N - {requiredMax:0.00} N";

        if (recommendedText != null)
            recommendedText.text = $"{recommended:0.00} N";

        if (recommendedMinText != null)
            recommendedMinText.text = "0 N";

        if (recommendedMaxText != null)
            recommendedMaxText.text = $"{Mathf.Max(totalForceDisplayMaxN, requiredMax):0.#} N";

        if (frictionText != null)
            frictionText.text = $"{p.mu:0.00}";

        if (sensorReadingText != null)
            sensorReadingText.text = $"{measuredTotal:0.00} N";

        if (statusText != null)
        {
            statusText.text = ConvertStatusToDisplay(p.status);
            statusText.color = GetStatusColor(p.status);
        }
    }

    private void UpdateGeneratedForceAnalysisUI(RobotForcePacket p, float measuredTotal, float recommended, float requiredMin, float requiredMax, float statusPercent)
    {
        if (gripGauge != null)
        {
            gripGauge.valueText = recommendedText != null ? recommendedText : gripGauge.valueText;
            gripGauge.stateText = recommendedStateText != null ? recommendedStateText : gripGauge.stateText;
            gripGauge.minText = recommendedMinText != null ? recommendedMinText : gripGauge.minText;
            gripGauge.maxText = recommendedMaxText != null ? recommendedMaxText : gripGauge.maxText;

            gripGauge.valueTextShowsRecommended = true;
            gripGauge.SetLiveForce(measuredTotal, recommended, requiredMin, requiredMax, totalForceDisplayMaxN);
        }

        if (forceScaleBar != null)
            forceScaleBar.SetForce(measuredTotal, totalForceDisplayMaxN);

        if (sensorGraph != null)
            sensorGraph.SetReading(measuredTotal, totalForceDisplayMaxN);

        if (statusBar != null)
        {
            Color c = GetStatusColor(p.status);
            statusBar.SetPercent(statusPercent, c, c);
        }
    }

    private void BindFingerCard(FingerCard card)
    {
        if (card == null) return;

        if (card.root == null)
            card.root = FindCardRootByTitle(card.title, card.fingerLabel);

        if (card.root == null) return;

        if (card.forceText == null)
            card.forceText = FindForceValueTextInside(card.root);

        if (card.percentText == null)
            card.percentText = FindPercentTextInside(card.root);

        if (card.barFill == null)
            card.barFill = FindBarInside(card.root);
    }

    private Transform FindCardRootByTitle(string title, string fingerLabel)
    {
        if (string.IsNullOrEmpty(title)) return null;

        string upperTitle = title.Trim().ToUpperInvariant();

        foreach (TMP_Text t in allTexts)
        {
            if (t == null) continue;

            string s = (t.text ?? "").Trim().ToUpperInvariant();

            if (s == upperTitle)
            {
                Transform root = FindNearestCardRoot(t.transform, fingerLabel);
                if (root != null) return root;
            }
        }

        return null;
    }

    private Transform FindNearestCardRoot(Transform titleTransform, string fingerLabel)
    {
        Transform p = titleTransform;

        for (int step = 0; step < 7 && p != null; step++)
        {
            string n = p.name.ToUpperInvariant();

            // Dung lai truoc khi leo qua cac panel lon.
            if (n.Contains("CANVAS") || n.Contains("HOLOCANVAS") || n.Contains("FORCEANALYSIS") || n.Contains("FINGERSTATUS"))
                return null;

            TMP_Text[] txts = p.GetComponentsInChildren<TMP_Text>(true);
            Image[] imgs = p.GetComponentsInChildren<Image>(true);

            bool hasFingerLabel = false;
            bool hasForceTitle = false;
            bool hasForceValue = false;
            bool hasPercent = false;
            bool hasBar = false;

            for (int i = 0; i < txts.Length; i++)
            {
                string s = (txts[i].text ?? "").Trim().ToUpperInvariant();

                if (s == fingerLabel.ToUpperInvariant()) hasFingerLabel = true;
                if (s.Contains("FINGER FORCE")) hasForceTitle = true;
                if (IsForceValueText(s)) hasForceValue = true;
                if (s.Contains("%")) hasPercent = true;
            }

            for (int i = 0; i < imgs.Length; i++)
            {
                Rect r = imgs[i].rectTransform.rect;
                float w = Mathf.Abs(r.width);
                float h = Mathf.Abs(r.height);
                if (w > 25f && h > 2f && h < 35f)
                {
                    hasBar = true;
                    break;
                }
            }

            if (hasForceTitle && hasFingerLabel && hasForceValue && hasBar)
                return p;

            // truong hop card khong co percent rieng
            if (hasForceTitle && hasFingerLabel && hasForceValue && imgs.Length >= 2)
                return p;

            p = p.parent;
        }

        return null;
    }

    private TMP_Text FindForceValueTextInside(Transform cardRoot)
    {
        TMP_Text[] arr = cardRoot.GetComponentsInChildren<TMP_Text>(true);

        TMP_Text best = null;
        float bestScore = -999f;

        foreach (TMP_Text t in arr)
        {
            if (t == null) continue;

            string s = (t.text ?? "").Trim().ToUpperInvariant();
            if (!IsForceValueText(s)) continue;

            float score = t.fontSize;

            string n = t.name.ToLowerInvariant();
            if (n.Contains("force")) score += 1000f;
            if (n.Contains("value")) score += 800f;
            if (n.Contains("thucte")) score += 800f;

            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return best;
    }

    private bool IsForceValueText(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;

        if (s == "THUMB" || s == "INDEX" || s == "MIDDLE" || s == "RING" || s == "PINKY") return false;
        if (s.Contains("FINGER FORCE")) return false;
        if (s.Contains("%")) return false;

        return s.EndsWith("N") && ContainsDigit(s);
    }

    private TMP_Text FindPercentTextInside(Transform cardRoot)
    {
        TMP_Text[] arr = cardRoot.GetComponentsInChildren<TMP_Text>(true);

        TMP_Text fallback = null;

        foreach (TMP_Text t in arr)
        {
            if (t == null) continue;

            string s = (t.text ?? "").Trim();
            string n = t.name.ToLowerInvariant();

            if (n.Contains("phantram") || n.Contains("percent"))
                return t;

            if (s.Contains("%") && s != "0%" && s != "100%")
                return t;

            if (fallback == null && s.Contains("%"))
                fallback = t;
        }

        return fallback;
    }

    private Image FindBarInside(Transform cardRoot)
    {
        Image[] imgs = cardRoot.GetComponentsInChildren<Image>(true);

        Image best = null;
        float bestScore = -999f;

        foreach (Image img in imgs)
        {
            if (img == null) continue;

            Rect r = img.rectTransform.rect;
            float w = Mathf.Abs(r.width);
            float h = Mathf.Abs(r.height);

            if (w < 25f || h <= 1f || h > 35f)
                continue;

            string n = img.name.ToLowerInvariant();
            float score = w;

            if (n.Contains("fill")) score += 2000f;
            if (n.Contains("bar")) score += 1000f;
            if (n.Contains("112")) score += 700f;
            if (n.Contains("111")) score += 400f;

            if (score > bestScore)
            {
                bestScore = score;
                best = img;
            }
        }

        return best;
    }

    private void UpdateFingerCard(FingerCard card, float force)
    {
        if (card == null) return;

        if (card.forceText != null)
            card.forceText.text = $"{Mathf.Max(0f, force):0.00} N";

        if (card.percentText != null)
            card.percentText.text = $"{Force01(force) * 100f:0.0}%";

        if (card.barFill != null)
        {
            float ratio = Force01(force);
            card.barFill.type = Image.Type.Filled;
            card.barFill.fillMethod = Image.FillMethod.Horizontal;
            card.barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            card.barFill.fillAmount = ratio;
            card.barFill.color = GetLevelColor(ratio);
        }
    }

    private float Force01(float force)
    {
        return Mathf.Clamp01(Mathf.Max(0f, force) / Mathf.Max(fingerForceDisplayMaxN, 0.001f));
    }

    private bool ContainsDigit(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;

        for (int i = 0; i < s.Length; i++)
            if (char.IsDigit(s[i])) return true;


        return false;
    }

    private Color GetLevelColor(float ratio)
    {
        // 0%: cyan mờ
        if (ratio <= 0.01f)
            return new Color32(0, 245, 255, 150);

        // Chạm nhẹ: cyan sáng
        if (ratio < 0.15f)
            return new Color32(0, 245, 255, 255);

        // Lực vừa: xanh lá neon
        if (ratio < 0.60f)
            return new Color32(0, 255, 120, 255);

        // Lực cao: vàng cảnh báo
        if (ratio < 0.85f)
            return new Color32(255, 220, 0, 255);

        // Nguy hiểm: đỏ
        return new Color32(255, 45, 45, 255);
    }

    private string ConvertStatusToDisplay(string s)
    {
        switch (s)
        {
            case "NO_OBJECT": return "NO OBJECT";
            case "WAIT_CONTACT": return "WAIT CONTACT";
            case "LOW_GRIP": return "LOW GRIP";
            case "OPTIMAL_GRIP": return "OPTIMAL GRIP";
            case "HIGH_GRIP": return "HIGH GRIP";
            case "DANGER_GRIP": return "DANGER";
            case "NO_TARGET": return "NO TARGET";
            default: return "STANDBY";
        }
    }

    private Color GetStatusColor(string s)
    {
        switch (s)
        {
            case "WAIT_CONTACT": return cyan;
            case "LOW_GRIP": return cyan;
            case "OPTIMAL_GRIP": return green;
            case "HIGH_GRIP": return orange;
            case "DANGER_GRIP": return red;
            case "NO_OBJECT": return gray;
            case "NO_TARGET": return gray;
            default: return gray;
        }
    }
}
