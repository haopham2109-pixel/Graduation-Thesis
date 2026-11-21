using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class ST_CheckIconUI : MaskableGraphic
{
    [Header("Circle")]
    [Range(24, 100)] public int segments = 48;
    [Range(0.2f, 0.5f)] public float outerRadius = 0.42f;
    [Range(0.1f, 0.4f)] public float innerRadius = 0.30f;

    [Header("Colors")]
    public Color glowColor = new Color(0f, 1f, 0.5f, 0.14f);
    public Color ringColor = new Color32(0, 255, 150, 255);
    public Color innerColor = new Color32(5, 35, 20, 255);
    public Color tickColor = Color.white;

    [Header("Tick")]
    [Range(0.03f, 0.12f)] public float tickThickness = 0.07f;

    [Header("Pulse")]
    public bool pulse = true;
    [Range(0.1f, 5f)] public float pulseSpeed = 1.8f;
    [Range(0f, 0.2f)] public float pulseAmount = 0.05f;

    private Vector3 baseScale = Vector3.one;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
        baseScale = rectTransform.localScale;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        baseScale = rectTransform.localScale;
        SetVerticesDirty();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif

    void Update()
    {
        if (pulse)
        {
            float s = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
            rectTransform.localScale = baseScale * s;
        }
        else
        {
            rectTransform.localScale = baseScale;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        Vector2 c = r.center;
        float size = Mathf.Min(r.width, r.height);

        float glowR = size * (outerRadius + 0.06f);
        float outerR = size * outerRadius;
        float innerR = size * innerRadius;
        float thick = size * tickThickness;

        AddDisc(vh, c, glowR, glowColor, segments);
        AddDisc(vh, c, outerR, ringColor, segments);
        AddDisc(vh, c, innerR, innerColor, segments);

        // Tick
        Vector2 p1 = c + new Vector2(-outerR * 0.42f, -outerR * 0.03f);
        Vector2 p2 = c + new Vector2(-outerR * 0.12f, outerR * 0.28f);
        Vector2 p3 = c + new Vector2(outerR * 0.45f, -outerR * 0.28f);

        AddLine(vh, p1, p2, thick, tickColor);
        AddLine(vh, p2, p3, thick, tickColor);
    }

    void AddDisc(VertexHelper vh, Vector2 center, float radius, Color col, int seg)
    {
        int start = vh.currentVertCount;

        UIVertex v = UIVertex.simpleVert;
        v.color = col;
        v.position = center;
        vh.AddVert(v);

        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            v.position = p;
            vh.AddVert(v);
        }

        for (int i = 1; i <= seg; i++)
        {
            vh.AddTriangle(start, start + i, start + i + 1);
        }
    }

    void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color col)
    {
        Vector2 dir = (b - a).normalized;
        Vector2 n = new Vector2(-dir.y, dir.x) * thickness * 0.5f;

        int start = vh.currentVertCount;
        UIVertex v = UIVertex.simpleVert;
        v.color = col;

        v.position = a - n; vh.AddVert(v);
        v.position = a + n; vh.AddVert(v);
        v.position = b + n; vh.AddVert(v);
        v.position = b - n; vh.AddVert(v);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}