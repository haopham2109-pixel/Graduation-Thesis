using UnityEngine;
using UnityEngine.UI;
using TMPro;

[ExecuteAlways]
public class CurvedGripGaugeUI : MaskableGraphic
{
    [Header("LIVE FORCE")]
    [Tooltip("Luc do duoc hien tai tu FSR / total_force_n. Bien nay dieu khien kim xanh va do sang cung.")]
    [Range(0f, 50f)] public float currentForce = 0f;

    [Tooltip("Luc khuyen nghi/tinh toan tu khoi luong vat. Bien nay hien o so giua gauge.")]
    public float recommendedForce = 0f;

    public float minForce = 0f;
    public float maxForce = 20f;

    [Header("OPTIMAL RANGE")]
    public float optimalMin = 0f;
    public float optimalMax = 0f;

    [Header("TEXT")]
    public TMP_Text valueText;
    public TMP_Text stateText;
    public TMP_Text minText;
    public TMP_Text maxText;

    [Tooltip("Bat true de so giua gauge hien luc can thiet/recommended, con kim van chay theo luc do.")]
    public bool valueTextShowsRecommended = true;

    [Header("ARC SETTING")]
    public int segmentCount = 10;
    public float startAngle = 180f;
    public float endAngle = 0f;
    public float outerRadius = 230f;
    public float innerRadius = 175f;
    public float centerYOffset = -115f;
    public float gapAngle = 4.0f;
    public int curveStepsPerSegment = 12;

    [Header("NEEDLE")]
    public bool showNeedle = true;
    public float needleStartRadius = 20f;
    public float needleEndRadius = 155f;
    public float needleWidth = 7f;
    public float needleHeadLength = 28f;
    public float needleHeadWidth = 30f;
    public Color needleColor = new Color32(80, 255, 40, 255);

    [Header("SEGMENT COLORS")]
    public Color inactiveColor = new Color32(20, 80, 95, 85);
    [Range(0f, 0.5f)] public float inactiveTintAmount = 0.18f;

    public Color cyanColor = new Color32(24, 235, 255, 255);
    public Color blueColor = new Color32(0, 145, 255, 255);
    public Color orangeColor = new Color32(255, 160, 30, 255);
    public Color darkOrangeColor = new Color32(255, 110, 20, 255);
    public Color redOrangeColor = new Color32(255, 65, 20, 255);

    [Header("STATE COLORS")]
    public Color waitStateColor = new Color32(80, 235, 255, 255);
    public Color lowStateColor = new Color32(255, 190, 45, 255);
    public Color optimalStateColor = new Color32(0, 255, 170, 255);
    public Color highStateColor = new Color32(255, 85, 45, 255);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        float normalized = Mathf.InverseLerp(minForce, maxForce, currentForce);
        normalized = Mathf.Clamp01(normalized);

        DrawArcSegments(vh, normalized);

        if (showNeedle)
            DrawNeedle(vh, normalized);

        UpdateTexts();
    }

    private void DrawArcSegments(VertexHelper vh, float normalized)
    {
        int count = Mathf.Max(1, segmentCount);
        float activeFloat = normalized * count;

        for (int i = 0; i < count; i++)
        {
            float t0 = (float)i / count;
            float t1 = (float)(i + 1) / count;

            float a0 = Mathf.Lerp(startAngle, endAngle, t0);
            float a1 = Mathf.Lerp(startAngle, endAngle, t1);

            if (a0 > a1)
            {
                a0 -= gapAngle * 0.5f;
                a1 += gapAngle * 0.5f;
            }
            else
            {
                a0 += gapAngle * 0.5f;
                a1 -= gapAngle * 0.5f;
            }

            float lit = Mathf.Clamp01(activeFloat - i);

            Color activeColor = GetActiveColor(i);
            Color baseColor = Color.Lerp(inactiveColor, activeColor, inactiveTintAmount);
            Color finalColor = Color.Lerp(baseColor, activeColor, lit);

            AddCurvedSegment(vh, a0, a1, finalColor);
        }
    }

    private void AddCurvedSegment(VertexHelper vh, float angleStart, float angleEnd, Color c)
    {
        int steps = Mathf.Max(2, curveStepsPerSegment);

        for (int i = 0; i < steps; i++)
        {
            float tA = (float)i / steps;
            float tB = (float)(i + 1) / steps;

            float angleA = Mathf.Lerp(angleStart, angleEnd, tA) * Mathf.Deg2Rad;
            float angleB = Mathf.Lerp(angleStart, angleEnd, tB) * Mathf.Deg2Rad;

            Vector2 outerA = new Vector2(
                Mathf.Cos(angleA) * outerRadius,
                Mathf.Sin(angleA) * outerRadius + centerYOffset
            );

            Vector2 outerB = new Vector2(
                Mathf.Cos(angleB) * outerRadius,
                Mathf.Sin(angleB) * outerRadius + centerYOffset
            );

            Vector2 innerB = new Vector2(
                Mathf.Cos(angleB) * innerRadius,
                Mathf.Sin(angleB) * innerRadius + centerYOffset
            );

            Vector2 innerA = new Vector2(
                Mathf.Cos(angleA) * innerRadius,
                Mathf.Sin(angleA) * innerRadius + centerYOffset
            );

            int idx = vh.currentVertCount;

            AddVert(vh, outerA, c);
            AddVert(vh, outerB, c);
            AddVert(vh, innerB, c);
            AddVert(vh, innerA, c);

            vh.AddTriangle(idx + 0, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx + 0);
        }
    }

    private void DrawNeedle(VertexHelper vh, float normalized)
    {
        float angle = Mathf.Lerp(startAngle, endAngle, normalized) * Mathf.Deg2Rad;

        Vector2 center = new Vector2(0f, centerYOffset);
        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 normal = new Vector2(-dir.y, dir.x);

        Vector2 p0 = center + dir * needleStartRadius;
        Vector2 p1 = center + dir * needleEndRadius;

        Vector2 a = p0 - normal * (needleWidth * 0.5f);
        Vector2 b = p0 + normal * (needleWidth * 0.5f);
        Vector2 c = p1 + normal * (needleWidth * 0.5f);
        Vector2 d = p1 - normal * (needleWidth * 0.5f);

        int idx = vh.currentVertCount;

        AddVert(vh, a, needleColor);
        AddVert(vh, b, needleColor);
        AddVert(vh, c, needleColor);
        AddVert(vh, d, needleColor);

        vh.AddTriangle(idx + 0, idx + 1, idx + 2);
        vh.AddTriangle(idx + 2, idx + 3, idx + 0);

        Vector2 tip = center + dir * (needleEndRadius + needleHeadLength);
        Vector2 left = p1 - normal * (needleHeadWidth * 0.5f);
        Vector2 right = p1 + normal * (needleHeadWidth * 0.5f);

        int tri = vh.currentVertCount;

        AddVert(vh, tip, needleColor);
        AddVert(vh, left, needleColor);
        AddVert(vh, right, needleColor);

        vh.AddTriangle(tri + 0, tri + 1, tri + 2);
    }

    private void UpdateTexts()
    {
        if (valueText != null)
        {
            float shownValue = valueTextShowsRecommended ? recommendedForce : currentForce;
            valueText.text = shownValue.ToString("0.00") + " N";
        }

        if (minText != null)
            minText.text = minForce.ToString("0") + " N";

        if (maxText != null)
            maxText.text = maxForce.ToString("0") + " N";

        if (stateText != null)
        {
            string state = GetStateText(currentForce);
            stateText.text = state;
            stateText.color = GetStateColor(state);
        }
    }

    private string GetStateText(float measuredValue)
    {
        // FIX DATN:
        // Trang thai gauge khong nen qua nhay theo requiredMax.
        // Cam bien FSR thuc te hay bi vot luc khi moi cham, nen neu do theo
        // requiredMax sat qua se bao HIGH/DANGER gia.
        //
        // Quy uoc hien thi:
        // - WAIT: chua co luc dang ke
        // - LOW: luc do < 50% luc khuyen nghi
        // - OPTIMAL: 50% -> 160% luc khuyen nghi
        // - HIGH: >160% luc khuyen nghi
        //
        // Vi du 103g: recommended ~3.67N.
        // Chạm nhẹ sensor ~4.87N = 133% recommended -> van la OPTIMAL, khong phai HIGH.
        if (measuredValue <= 0.20f) return "WAIT";

        float target = Mathf.Max(recommendedForce, 0.001f);

        if (measuredValue < target * 0.50f)
            return "LOW";

        if (measuredValue <= target * 1.60f)
            return "OPTIMAL";

        return "HIGH";
    }

    private Color GetStateColor(string state)
    {
        if (state == "WAIT") return waitStateColor;
        if (state == "LOW") return lowStateColor;
        if (state == "HIGH") return highStateColor;
        return optimalStateColor;
    }

    private Color GetActiveColor(int index)
    {
        if (index <= 3) return cyanColor;
        if (index <= 5) return blueColor;
        if (index == 6) return orangeColor;
        if (index == 7) return darkOrangeColor;
        if (index == 8) return new Color32(255, 90, 20, 255);
        return redOrangeColor;
    }

    private void AddVert(VertexHelper vh, Vector2 pos, Color c)
    {
        UIVertex v = UIVertex.simpleVert;
        v.position = pos;
        v.color = c;
        vh.AddVert(v);
    }

    public void SetForce(float force)
    {
        currentForce = Mathf.Clamp(force, minForce, maxForce);
        SetVerticesDirty();
        UpdateTexts();
    }

    public void SetLiveForce(float measuredForce, float recommended, float requiredMin, float requiredMax, float displayMax)
    {
        minForce = 0f;
        maxForce = Mathf.Max(displayMax, 0.01f);

        currentForce = Mathf.Clamp(measuredForce, minForce, maxForce);
        recommendedForce = Mathf.Max(0f, recommended);

        optimalMin = Mathf.Max(0f, requiredMin);
        optimalMax = Mathf.Max(optimalMin, requiredMax);

        SetVerticesDirty();
        UpdateTexts();
    }

    protected override void OnValidate()
    {
        base.OnValidate();

        maxForce = Mathf.Max(maxForce, minForce + 0.01f);
        currentForce = Mathf.Clamp(currentForce, minForce, maxForce);
        SetVerticesDirty();
    }

    protected override void Reset()
    {
        base.Reset();
        raycastTarget = false;
    }
}
