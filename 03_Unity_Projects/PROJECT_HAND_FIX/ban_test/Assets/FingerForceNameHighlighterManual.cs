using System.Globalization;
using TMPro;
using UnityEngine;

/*
DATN - FINGER FORCE NAME HIGHLIGHTER MANUAL
Muc dich:
- Khong auto bind, khong tu tim object.
- Ban tu keo dung Text vao Inspector cho chac.
- Khi luc cua ngon nao > Touch Threshold N -> ten ngon do sang vang.
- Khi luc nho hon Release Threshold N -> ten ngon do ve mau xanh.

Cach gan:
1) Tao file FingerForceNameHighlighterManual.cs
2) Dan code nay vao.
3) Add Component script nay vao HoloCanvas_Main hoac ForceAnalysisPanel.
4) Keo:
   Thumb Name Text  = TEN_THUMB
   Thumb Force Text = luc_thumb_thucte

   Index Name Text  = TEN_INDEX
   Index Force Text = luc_index_thucte

   Middle Name Text  = TEN_MIDDLE
   Middle Force Text = luc_middle_thucte

   Ring Name Text  = TEN_RING
   Ring Force Text = luc_ring_thucte

   Pinky Name Text  = TEN_PINKY
   Pinky Force Text = luc_pinky_thucte
*/

public class FingerForceNameHighlighterManual : MonoBehaviour
{
    [Header("Threshold")]
    [Tooltip("Luc lon hon nguong nay thi ten ngon sang vang.")]
    public float touchThresholdN = 0.08f;

    [Tooltip("Luc nho hon nguong nay thi ten ngon tat vang. Nho hon touchThreshold de tranh nhap nhay.")]
    public float releaseThresholdN = 0.03f;

    [Header("Colors")]
    public Color normalColor = new Color32(0, 245, 255, 255);
    public Color touchedColor = new Color32(255, 210, 0, 255);

    [Header("THUMB")]
    public TMP_Text thumbNameText;
    public TMP_Text thumbForceText;

    [Header("INDEX")]
    public TMP_Text indexNameText;
    public TMP_Text indexForceText;

    [Header("MIDDLE")]
    public TMP_Text middleNameText;
    public TMP_Text middleForceText;

    [Header("RING")]
    public TMP_Text ringNameText;
    public TMP_Text ringForceText;

    [Header("PINKY")]
    public TMP_Text pinkyNameText;
    public TMP_Text pinkyForceText;

    private bool thumbTouched;
    private bool indexTouched;
    private bool middleTouched;
    private bool ringTouched;
    private bool pinkyTouched;

    private void Start()
    {
        SetTextColor(thumbNameText, normalColor);
        SetTextColor(indexNameText, normalColor);
        SetTextColor(middleNameText, normalColor);
        SetTextColor(ringNameText, normalColor);
        SetTextColor(pinkyNameText, normalColor);
    }

    private void Update()
    {
        UpdateFinger(ref thumbTouched, thumbNameText, thumbForceText);
        UpdateFinger(ref indexTouched, indexNameText, indexForceText);
        UpdateFinger(ref middleTouched, middleNameText, middleForceText);
        UpdateFinger(ref ringTouched, ringNameText, ringForceText);
        UpdateFinger(ref pinkyTouched, pinkyNameText, pinkyForceText);
    }

    private void UpdateFinger(ref bool touched, TMP_Text nameText, TMP_Text forceText)
    {
        if (nameText == null || forceText == null)
            return;

        float forceN = ParseForceN(forceText.text);

        if (!touched && forceN >= touchThresholdN)
            touched = true;
        else if (touched && forceN <= releaseThresholdN)
            touched = false;

        nameText.color = touched ? touchedColor : normalColor;
    }

    private float ParseForceN(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0f;

        string s = text.Replace("N", "").Replace("n", "").Trim();
        s = s.Replace(",", ".");

        float value;
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return Mathf.Max(0f, value);

        return 0f;
    }

    private void SetTextColor(TMP_Text txt, Color color)
    {
        if (txt != null)
            txt.color = color;
    }
}
