using UnityEngine;
using UnityEngine.UI;
using TMPro;

/*
DATN - FingerStatusUI V23 DYNAMIC TARGET FORCE - 4 FINGERS + THUMB FORCE
- Angle: doc tu HandUdpReceiver de hien goc gap tung ngon.
- Target: hien luc muc tieu/dieu khien theo vat cho 4 ngon Index/Middle/Ring/Pinky.
- 4 ngon nay LUON hien target force, khong hien Closing.
- Rieng THUMB: khong tham gia chia target kep, nhung VAN hien luc thumb/palm sensor thuc te.
- Thanh bar hien theo Angle 0..100%, KHONG nhan 1.6 nua.
- Target force:
  + Ban dau biet vat/chua co ngon cham: Python chia deu cho 4 ngon.
  + Khi FSR ngon nao cham: Python chia target cho cac ngon dang active.
  + Ngon chua cham se hien "Closing".
*/

public class FingerStatusUI : MonoBehaviour
{
    [Header("Finger Texts")]
    public TMP_Text thumbText;
    public TMP_Text indexText;
    public TMP_Text middleText;
    public TMP_Text ringText;
    public TMP_Text pinkyText;
    public TMP_Text totalForceText;

    [Header("Finger Bars")]
    public Image thumbBarFill;
    public Image indexBarFill;
    public Image middleBarFill;
    public Image ringBarFill;
    public Image pinkyBarFill;

    [Header("Auto Follow HandUdpReceiver")]
    public bool autoFollowHandReceiver = true;
    public HandUdpReceiver handReceiver;
    public bool debugAutoFollow = false;

    [Header("Object / Dynamic Target Force")]
    public string currentObjectLabel = "none";
    public float totalTargetForceN = 0f;
    public float thumbTargetN = 0f;

    [Tooltip("Luc thuc te o thumb/palm sensor. Thumb khong chia target kep nhung van hien luc nay.")]
    public float thumbPalmForceN = 0f;

    public float indexTargetN = 0f;
    public float middleTargetN = 0f;
    public float ringTargetN = 0f;
    public float pinkyTargetN = 0f;

    [Tooltip("V23: Tat mac dinh. 4 ngon kep luon hien target force, khong hien Closing.")]
    public bool showClosingForZeroTarget = false;

    [Header("Fallback Object Target (N)")]
    public float kimquatTargetN = 8.30f;
    public float redChiliTargetN = 2.85f;
    public float orangeTargetN = 5.88f;

    [Header("Demo Mode")]
    public bool demoMode = false;
    public float demoSpeed = 1.0f;

    [Header("Live Curl Values (0..1)")]
    [Range(0, 1)] public float thumbCurl;
    [Range(0, 1)] public float indexCurl;
    [Range(0, 1)] public float middleCurl;
    [Range(0, 1)] public float ringCurl;
    [Range(0, 1)] public float pinkyCurl;

    private float lastDebugTime;

    private void Awake()
    {
        PrepareBar(thumbBarFill);
        PrepareBar(indexBarFill);
        PrepareBar(middleBarFill);
        PrepareBar(ringBarFill);
        PrepareBar(pinkyBarFill);

        FindHandReceiverIfNeeded();
        ApplyObjectTargetByLabel(currentObjectLabel);
    }

    private void Update()
    {
        if (!autoFollowHandReceiver && demoMode)
        {
            float t = Time.time * demoSpeed;

            thumbCurl = Mathf.PingPong(t * 0.55f, 1f);
            indexCurl = Mathf.PingPong(t * 0.70f + 0.20f, 1f);
            middleCurl = Mathf.PingPong(t * 0.85f + 0.35f, 1f);
            ringCurl = Mathf.PingPong(t * 0.65f + 0.50f, 1f);
            pinkyCurl = Mathf.PingPong(t * 0.50f + 0.70f, 1f);
        }
    }

    private void LateUpdate()
    {
        if (autoFollowHandReceiver)
        {
            FindHandReceiverIfNeeded();

            if (handReceiver != null)
            {
                thumbCurl = Mathf.Clamp01(handReceiver.thumbValue);
                indexCurl = Mathf.Clamp01(handReceiver.indexValue);
                middleCurl = Mathf.Clamp01(handReceiver.middleValue);
                ringCurl = Mathf.Clamp01(handReceiver.ringValue);
                pinkyCurl = Mathf.Clamp01(handReceiver.pinkyValue);

                if (debugAutoFollow && Time.time - lastDebugTime > 0.5f)
                {
                    lastDebugTime = Time.time;
                    Debug.Log(
                        "FingerStatusUI V23: " +
                        $"T={thumbCurl * 100f:0} I={indexCurl * 100f:0} M={middleCurl * 100f:0} " +
                        $"R={ringCurl * 100f:0} P={pinkyCurl * 100f:0} | " +
                        $"Obj={currentObjectLabel} TotalTarget={totalTargetForceN:0.00}N " +
                        $"ThumbPalm={thumbPalmForceN:0.00}N " +
                        $"Target I={indexTargetN:0.00} M={middleTargetN:0.00} R={ringTargetN:0.00} P={pinkyTargetN:0.00}"
                    );
                }
            }
        }

        RefreshUI();
    }

    private void FindHandReceiverIfNeeded()
    {
        if (handReceiver != null) return;

#if UNITY_2023_1_OR_NEWER
        handReceiver = FindFirstObjectByType<HandUdpReceiver>();
#else
        handReceiver = FindObjectOfType<HandUdpReceiver>();
#endif
    }

    private void PrepareBar(Image img)
    {
        if (img == null) return;

        RectTransform rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 0f;
        img.preserveAspect = false;
        img.raycastTarget = false;
    }

    public void ApplyObjectTargetByLabel(string label)
    {
        string normalized = NormalizeLabel(label);
        currentObjectLabel = normalized;

        if (normalized == "kimquat_sauce")
            SetTotalTargetForceEqual4(normalized, kimquatTargetN);
        else if (normalized == "red_chili")
            SetTotalTargetForceEqual4(normalized, redChiliTargetN);
        else if (normalized == "orange")
            SetTotalTargetForceEqual4(normalized, orangeTargetN);
        else
            SetDynamicFingerTargets("none", 0f, 0f, 0f, 0f, 0f);
    }

    public void SetTotalTargetForceEqual4(string label, float totalN)
    {
        currentObjectLabel = NormalizeLabel(label);
        totalTargetForceN = Mathf.Max(0f, totalN);

        thumbTargetN = 0f;

        if (totalTargetForceN <= 0f)
        {
            indexTargetN = middleTargetN = ringTargetN = pinkyTargetN = 0f;
            return;
        }

        float each = totalTargetForceN / 4f;
        indexTargetN = each;
        middleTargetN = each;
        ringTargetN = each;
        pinkyTargetN = each;
    }

    public void SetDynamicFingerTargets(string label, float totalN, float indexN, float middleN, float ringN, float pinkyN)
    {
        currentObjectLabel = NormalizeLabel(label);
        totalTargetForceN = Mathf.Max(0f, totalN);

        thumbTargetN = 0f;
        indexTargetN = Mathf.Max(0f, indexN);
        middleTargetN = Mathf.Max(0f, middleN);
        ringTargetN = Mathf.Max(0f, ringN);
        pinkyTargetN = Mathf.Max(0f, pinkyN);
    }

    // Alias de cac script khac goi de hon.
    public void SetObjectTargetForce(string label, float totalN)
    {
        SetTotalTargetForceEqual4(label, totalN);
    }

    private string NormalizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "none";

        string s = label.Trim().ToLower();

        if (s == "kimquat" || s == "kim_quat" || s == "kimquat_sauce" || s.Contains("kimquat"))
            return "kimquat_sauce";

        if (s == "red_chili" || s == "chili" || s == "ot_bot" || s == "ot" || s.Contains("chili"))
            return "red_chili";

        if (s == "orange" || s == "cam")
            return "orange";

        return "none";
    }

    public void SetThumbPalmForceN(float forceN)
    {
        thumbPalmForceN = Mathf.Max(0f, forceN);
    }

    public void SetThumbPalmForce01(float force01, float forceMaxN)
    {
        thumbPalmForceN = Mathf.Clamp01(force01) * Mathf.Max(forceMaxN, 0.001f);
    }

    public void SetLiveData(
        float thumbCurlValue, float indexCurlValue, float middleCurlValue, float ringCurlValue, float pinkyCurlValue,
        float thumbForceValue, float indexForceValue, float middleForceValue, float ringForceValue, float pinkyForceValue)
    {
        // V23:
        // - 4 ngon INDEX/MIDDLE/RING/PINKY hien target dong.
        // - THUMB khong chia target kep nhung van hien luc thumb/palm sensor neu duoc gui vao.
        demoMode = false;

        // Neu script cu van truyen thumbForceValue 0..1 thi doi thanh luc N theo thang 20N tam thoi.
        // HandUdpReceiver V22 se uu tien goi SetThumbPalmForceN(fp.thumb_force) voi don vi N that.
        if (thumbForceValue > 0f)
            thumbPalmForceN = Mathf.Max(thumbPalmForceN, Mathf.Clamp01(thumbForceValue) * 20f);

        thumbCurl = Mathf.Clamp01(thumbCurlValue);
        indexCurl = Mathf.Clamp01(indexCurlValue);
        middleCurl = Mathf.Clamp01(middleCurlValue);
        ringCurl = Mathf.Clamp01(ringCurlValue);
        pinkyCurl = Mathf.Clamp01(pinkyCurlValue);

        RefreshUI();
    }

    private void RefreshUI()
    {
        SetFingerUI(thumbText, thumbBarFill, "Thumb", thumbCurl, thumbPalmForceN, false);
        SetFingerUI(indexText, indexBarFill, "Index", indexCurl, indexTargetN, true);
        SetFingerUI(middleText, middleBarFill, "Middle", middleCurl, middleTargetN, true);
        SetFingerUI(ringText, ringBarFill, "Ring", ringCurl, ringTargetN, true);
        SetFingerUI(pinkyText, pinkyBarFill, "Pinky", pinkyCurl, pinkyTargetN, true);

        if (totalForceText != null)
            totalForceText.text = $"Total Target Force: {totalTargetForceN:0.00} N";
    }

    private void SetFingerUI(TMP_Text txt, Image bar, string fingerName, float curl, float targetForceN, bool canGrip)
    {
        curl = Mathf.Clamp01(curl);
        targetForceN = Mathf.Max(0f, targetForceN);

        string labelText;
        string targetText;

        if (!canGrip)
        {
            // THUMB: khong chia deu luc muc tieu kep, nhung van hien luc thumb/palm sensor.
            labelText = "Palm";
            targetText = $"{targetForceN:0.00} N";
        }
        else
        {
            // V23: 4 ngon kep luon hien gia tri luc, khong hien Closing.
            // Neu Python chua co object thi targetForceN = 0.00 N.
            labelText = "Target";
            targetText = $"{targetForceN:0.00} N";
        }

        if (txt != null)
            txt.text = $"{fingerName} Angle: {curl * 100f:0}%  {labelText}: {targetText}";

        if (bar != null)
        {
            // FIX: curl da la 0..1, khong nhan 1.6f nua.
            bar.fillAmount = curl;

            Color lowColor = new Color(0.10f, 0.95f, 1.00f, 1f);
            Color highColor = new Color(1.00f, 0.70f, 0.10f, 1f);

            float forceVisual = Mathf.Clamp01(targetForceN / 3.0f);
            if (targetText == "Closing") forceVisual = 0.15f;

            bar.color = Color.Lerp(lowColor, highColor, forceVisual);
        }
    }
}
