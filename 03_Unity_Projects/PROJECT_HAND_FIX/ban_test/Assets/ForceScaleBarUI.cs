using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class ForceScaleBarUI : MaskableGraphic
{
    [Header("Force")]
    public float currentForce = 0f;
    public float minForce = 0f;
    public float maxForce = 20f;

    [Header("Shape")]
    public float barWidth = 46f;
    public float borderWidth = 2f;
    public float tickLengthMajor = 28f;
    public float tickLengthMinor = 14f;
    public float pointerWidth = 28f;
    public float pointerHeight = 34f;

    [Header("Colors")]
    public Color borderColor = new Color32(80, 235, 255, 230);
    public Color gridColor = new Color32(80, 235, 255, 80);
    public Color barBackColor = new Color32(5, 30, 45, 180);
    public Color barFillBottom = new Color32(0, 120, 255, 255);
    public Color barFillTop = new Color32(35, 240, 255, 255);
    public Color pointerColor = new Color32(80, 235, 255, 255);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;

        float graphHeight = r.height - 30f;
        float bottom = -graphHeight * 0.5f;
        float top = graphHeight * 0.5f;
        float xCenter = 15f;

        float ratio = Mathf.InverseLerp(minForce, maxForce, currentForce);
        ratio = Mathf.Clamp01(ratio);

        float barLeft = xCenter - barWidth * 0.5f;
        float barRight = xCenter + barWidth * 0.5f;
        float fillTop = Mathf.Lerp(bottom, top, ratio);

        AddRect(vh, barLeft, bottom, barRight, top, barBackColor);

        if (ratio > 0.001f)
            AddRectGradient(vh, barLeft + 3f, bottom + 3f, barRight - 3f, fillTop, barFillBottom, barFillTop);

        AddLine(vh, new Vector2(barLeft, bottom), new Vector2(barLeft, top), borderWidth, borderColor);
        AddLine(vh, new Vector2(barRight, bottom), new Vector2(barRight, top), borderWidth, borderColor);
        AddLine(vh, new Vector2(barLeft, bottom), new Vector2(barRight, bottom), borderWidth, borderColor);
        AddLine(vh, new Vector2(barLeft, top), new Vector2(barRight, top), borderWidth, borderColor);

        for (int i = 0; i <= 20; i++)
        {
            float t = i / 20f;
            float y = Mathf.Lerp(bottom, top, t);

            bool major = (i == 0 || i == 5 || i == 10 || i == 15 || i == 20);
            float len = major ? tickLengthMajor : tickLengthMinor;

            AddLine(
                vh,
                new Vector2(barLeft - len, y),
                new Vector2(barLeft, y),
                major ? 2f : 1.2f,
                major ? borderColor : gridColor
            );

            if (major)
            {
                AddLine(
                    vh,
                    new Vector2(barRight, y),
                    new Vector2(barRight + 18f, y),
                    1.5f,
                    gridColor
                );
            }
        }

        float py = fillTop;
        Vector2 p1 = new Vector2(barRight + 18f, py);
        Vector2 p2 = new Vector2(barRight + 18f + pointerWidth, py + pointerHeight * 0.5f);
        Vector2 p3 = new Vector2(barRight + 18f + pointerWidth, py - pointerHeight * 0.5f);
        AddTriangle(vh, p1, p2, p3, pointerColor);
    }

    private void AddRect(VertexHelper vh, float left, float bottom, float right, float top, Color c)
    {
        int idx = vh.currentVertCount;
        AddVert(vh, new Vector2(left, bottom), c);
        AddVert(vh, new Vector2(left, top), c);
        AddVert(vh, new Vector2(right, top), c);
        AddVert(vh, new Vector2(right, bottom), c);

        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx + 2, idx + 3, idx);
    }

    private void AddRectGradient(VertexHelper vh, float left, float bottom, float right, float top, Color cBottom, Color cTop)
    {
        if (top <= bottom) return;

        int idx = vh.currentVertCount;
        AddVert(vh, new Vector2(left, bottom), cBottom);
        AddVert(vh, new Vector2(left, top), cTop);
        AddVert(vh, new Vector2(right, top), cTop);
        AddVert(vh, new Vector2(right, bottom), cBottom);

        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx + 2, idx + 3, idx);
    }

    private void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color col)
    {
        int idx = vh.currentVertCount;
        AddVert(vh, a, col);
        AddVert(vh, b, col);
        AddVert(vh, c, col);
        vh.AddTriangle(idx, idx + 1, idx + 2);
    }

    private void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
    {
        Vector2 dir = (b - a).normalized;
        Vector2 n = new Vector2(-dir.y, dir.x) * (width * 0.5f);

        int idx = vh.currentVertCount;

        AddVert(vh, a - n, c);
        AddVert(vh, a + n, c);
        AddVert(vh, b + n, c);
        AddVert(vh, b - n, c);

        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx + 2, idx + 3, idx);
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
    }

    public void SetForce(float force, float displayMax)
    {
        minForce = 0f;
        maxForce = Mathf.Max(displayMax, 0.01f);
        currentForce = Mathf.Clamp(force, minForce, maxForce);
        SetVerticesDirty();
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
