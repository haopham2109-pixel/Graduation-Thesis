using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class ST_SegmentedBarUI : MaskableGraphic
{
    [Header("Value")]
    [Range(0f, 1f)] public float fillPercent = 0f;

    [Header("Segments")]
    [Range(3, 20)] public int segmentCount = 6;
    [Range(0f, 20f)] public float gap = 6f;
    [Range(0f, 10f)] public float padding = 6f;

    [Header("Style")]
    public float borderThickness = 2f;
    public Color borderColor = new Color32(70, 220, 255, 180);
    public Color bgColor = new Color32(4, 18, 30, 180);
    public Color emptySegmentColor = new Color32(10, 25, 40, 160);
    public Color fillStartColor = new Color32(40, 220, 255, 255);
    public Color fillEndColor = new Color32(30, 110, 255, 255);

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif

    public void SetPercent(float value01)
    {
        fillPercent = Mathf.Clamp01(value01);
        SetVerticesDirty();
    }

    public void SetPercent(float value01, Color startColor, Color endColor)
    {
        fillPercent = Mathf.Clamp01(value01);
        fillStartColor = startColor;
        fillEndColor = endColor;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;

        AddRect(vh, r.xMin, r.yMin, r.xMax, r.yMax, bgColor);
        AddBorder(vh, r, borderThickness, borderColor);

        float innerX = r.xMin + padding;
        float innerY = r.yMin + padding;
        float innerW = r.width - padding * 2f;
        float innerH = r.height - padding * 2f;

        if (segmentCount <= 0) return;

        float totalGap = gap * (segmentCount - 1);
        float segW = (innerW - totalGap) / segmentCount;

        float filledSegments = Mathf.Clamp01(fillPercent) * segmentCount;

        for (int i = 0; i < segmentCount; i++)
        {
            float x = innerX + i * (segW + gap);
            float y = innerY;

            Color col = emptySegmentColor;

            if (i < Mathf.FloorToInt(filledSegments))
            {
                float t = (segmentCount <= 1) ? 0 : (float)i / (segmentCount - 1);
                col = Color.Lerp(fillStartColor, fillEndColor, t);
            }
            else if (i == Mathf.FloorToInt(filledSegments) && filledSegments % 1f > 0.001f)
            {
                float t = (segmentCount <= 1) ? 0 : (float)i / (segmentCount - 1);
                Color partialCol = Color.Lerp(fillStartColor, fillEndColor, t);

                AddRect(vh, x, y, x + segW, y + innerH, emptySegmentColor);

                float pw = segW * (filledSegments % 1f);
                AddRect(vh, x, y, x + pw, y + innerH, partialCol);
                continue;
            }

            AddRect(vh, x, y, x + segW, y + innerH, col);
        }
    }

    void AddBorder(VertexHelper vh, Rect r, float t, Color c)
    {
        AddRect(vh, r.xMin, r.yMax - t, r.xMax, r.yMax, c);
        AddRect(vh, r.xMin, r.yMin, r.xMax, r.yMin + t, c);
        AddRect(vh, r.xMin, r.yMin, r.xMin + t, r.yMax, c);
        AddRect(vh, r.xMax - t, r.yMin, r.xMax, r.yMax, c);
    }

    void AddRect(VertexHelper vh, float xMin, float yMin, float xMax, float yMax, Color c)
    {
        int start = vh.currentVertCount;

        UIVertex v = UIVertex.simpleVert;
        v.color = c;

        v.position = new Vector2(xMin, yMin); vh.AddVert(v);
        v.position = new Vector2(xMin, yMax); vh.AddVert(v);
        v.position = new Vector2(xMax, yMax); vh.AddVert(v);
        v.position = new Vector2(xMax, yMin); vh.AddVert(v);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
