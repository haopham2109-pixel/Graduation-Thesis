using UnityEngine;
using UnityEngine.UI;
using TMPro;

[ExecuteAlways]
public class KgWeightIconUI : MaskableGraphic
{
    [Header("Label")]
    public string label = "KG";
    public TMP_Text labelText;
    public bool autoCreateLabel = true;

    [Header("Shape")]
    public float bodyTopWidth = 0.58f;
    public float bodyBottomWidth = 0.82f;
    public float bodyHeight = 0.58f;
    public float bodyYOffset = -0.12f;

    [Header("Handle")]
    public float handleOuterRadius = 0.23f;
    public float handleInnerRadius = 0.14f;
    public float handleYOffset = 0.20f;
    public int handleSteps = 18;

    [Header("Border")]
    public float borderWidth = 3.0f;
    public float glowWidth = 8.0f;

    [Header("Colors")]
    public Color bodyColor = new Color32(120, 240, 255, 235);
    public Color bodyInnerColor = new Color32(35, 190, 230, 190);
    public Color borderColor = new Color32(180, 250, 255, 255);
    public Color glowColor = new Color32(0, 230, 255, 70);
    public Color labelColor = new Color32(0, 40, 55, 255);

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
        EnsureLabel();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
        EnsureLabel();
        SetVerticesDirty();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        ConfigureLabel();
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        float w = r.width;
        float h = r.height;
        float s = Mathf.Min(w, h);

        float bodyH = s * bodyHeight;
        float topW = s * bodyTopWidth;
        float bottomW = s * bodyBottomWidth;

        float bodyCenterY = s * bodyYOffset;
        float bodyTopY = bodyCenterY + bodyH * 0.5f;
        float bodyBottomY = bodyCenterY - bodyH * 0.5f;

        Vector2 p1 = new Vector2(-topW * 0.5f, bodyTopY);
        Vector2 p2 = new Vector2(topW * 0.5f, bodyTopY);
        Vector2 p3 = new Vector2(bottomW * 0.5f, bodyBottomY);
        Vector2 p4 = new Vector2(-bottomW * 0.5f, bodyBottomY);

        // Glow than can
        AddPolygon(vh, p1, p2, p3, p4, glowColor);

        // Than can
        AddPolygonGradient(vh, p1, p2, p3, p4, bodyColor, bodyInnerColor);

        // Vien glow
        AddLine(vh, p1, p2, glowWidth, glowColor);
        AddLine(vh, p2, p3, glowWidth, glowColor);
        AddLine(vh, p3, p4, glowWidth, glowColor);
        AddLine(vh, p4, p1, glowWidth, glowColor);

        // Vien sac
        AddLine(vh, p1, p2, borderWidth, borderColor);
        AddLine(vh, p2, p3, borderWidth, borderColor);
        AddLine(vh, p3, p4, borderWidth, borderColor);
        AddLine(vh, p4, p1, borderWidth, borderColor);

        // Quai can
        float handleCY = s * handleYOffset;
        float outerR = s * handleOuterRadius;
        float innerR = s * handleInnerRadius;

        AddArcRing(vh, new Vector2(0f, handleCY), innerR, outerR, 0f, 180f, handleSteps, glowColor);
        AddArcRing(vh, new Vector2(0f, handleCY), innerR, outerR, 0f, 180f, handleSteps, borderColor);

        // Chan quai noi xuong than
        Vector2 lh1 = new Vector2(-innerR, handleCY);
        Vector2 lh2 = new Vector2(-innerR, bodyTopY - 2f);
        Vector2 rh1 = new Vector2(innerR, handleCY);
        Vector2 rh2 = new Vector2(innerR, bodyTopY - 2f);

        AddLine(vh, lh1, lh2, borderWidth, borderColor);
        AddLine(vh, rh1, rh2, borderWidth, borderColor);
    }

    private void EnsureLabel()
    {
        if (!autoCreateLabel) return;

        if (labelText == null)
        {
            Transform found = transform.Find("KG_Text");
            if (found != null)
            {
                labelText = found.GetComponent<TMP_Text>();
            }
        }

        if (labelText == null)
        {
            GameObject go = new GameObject("KG_Text");
            go.transform.SetParent(transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            labelText = go.AddComponent<TextMeshProUGUI>();
        }

        ConfigureLabel();
    }

    private void ConfigureLabel()
    {
        if (labelText == null) return;

        Rect r = rectTransform.rect;

        labelText.text = label;
        labelText.color = labelColor;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.fontStyle = FontStyles.Bold;
        labelText.raycastTarget = false;
        labelText.enableAutoSizing = false;
        labelText.fontSize = Mathf.Min(r.width, r.height) * 0.27f;

        RectTransform rt = labelText.rectTransform;
        rt.sizeDelta = new Vector2(r.width * 0.75f, r.height * 0.45f);
        rt.anchoredPosition = new Vector2(0f, -r.height * 0.10f);
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;
    }

    private void AddPolygon(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        int idx = vh.currentVertCount;

        AddVert(vh, a, col);
        AddVert(vh, b, col);
        AddVert(vh, c, col);
        AddVert(vh, d, col);

        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx + 2, idx + 3, idx);
    }

    private void AddPolygonGradient(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color topColor, Color bottomColor)
    {
        int idx = vh.currentVertCount;

        AddVert(vh, a, topColor);
        AddVert(vh, b, topColor);
        AddVert(vh, c, bottomColor);
        AddVert(vh, d, bottomColor);

        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx + 2, idx + 3, idx);
    }

    private void AddArcRing(VertexHelper vh, Vector2 center, float innerR, float outerR, float startDeg, float endDeg, int steps, Color col)
    {
        steps = Mathf.Max(3, steps);

        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps;
            float t1 = (i + 1) / (float)steps;

            float a0 = Mathf.Lerp(startDeg, endDeg, t0) * Mathf.Deg2Rad;
            float a1 = Mathf.Lerp(startDeg, endDeg, t1) * Mathf.Deg2Rad;

            Vector2 p1 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * outerR;
            Vector2 p2 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * outerR;
            Vector2 p3 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * innerR;
            Vector2 p4 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * innerR;

            AddPolygon(vh, p1, p2, p3, p4, col);
        }
    }

    private void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color col)
    {
        Vector2 dir = (b - a).normalized;
        Vector2 n = new Vector2(-dir.y, dir.x) * (width * 0.5f);

        int idx = vh.currentVertCount;

        AddVert(vh, a - n, col);
        AddVert(vh, a + n, col);
        AddVert(vh, b + n, col);
        AddVert(vh, b - n, col);

        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx + 2, idx + 3, idx);
    }

    private void AddVert(VertexHelper vh, Vector2 pos, Color col)
    {
        UIVertex v = UIVertex.simpleVert;
        v.position = pos;
        v.color = col;
        vh.AddVert(v);
    }
}