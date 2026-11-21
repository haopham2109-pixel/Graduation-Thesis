using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class HoloMainFrameClean : MonoBehaviour
{
    [Header("Frame Size")]
    public Vector2 frameSize = new Vector2(2550f, 1450f);

    [Header("Style")]
    public float lineThickness = 4f;
    public float cornerLength = 120f;
    public float innerOffset = 28f;

    [Header("Background")]
    public bool showBackground = true;
    public bool showTechGrid = true;
    [Range(0f, 1f)] public float backgroundAlpha = 0.42f;
    [Range(0f, 1f)] public float gridAlpha = 0.16f;

    [Header("READY Box")]
    public bool showReady = true;
    public Vector2 readyPos = new Vector2(0f, 455f);
    public Vector2 readySize = new Vector2(190f, 58f);
    public int readyFontSize = 26;

    [Header("Colors")]
    public Color mainCyan = new Color(0f, 0.95f, 1f, 0.95f);
    public Color softCyan = new Color(0f, 0.95f, 1f, 0.28f);
    public Color bgColor = new Color(0f, 0.08f, 0.12f, 1f);
    public Color readyGreen = new Color(0.1f, 1f, 0.35f, 0.95f);
    public Color readyBg = new Color(0.02f, 0.32f, 0.10f, 0.88f);

    [ContextMenu("Build Clean Frame")]
    public void Build()
    {
        ClearChildren();

        RectTransform root = GetComponent<RectTransform>();
        if (root == null) root = gameObject.AddComponent<RectTransform>();

        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = frameSize;
        root.localScale = Vector3.one;
        root.localRotation = Quaternion.identity;

        if (showBackground)
            BuildBackground();

        if (showTechGrid)
            BuildTechGrid();

        BuildOuterFrame();
        BuildInnerFrame();
        BuildHeaderAccent();
        BuildBottomAccent();

        if (showReady)
            BuildReadyBox();
    }

    void BuildBackground()
    {
        GameObject g = new GameObject("BG_Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        g.transform.SetParent(transform, false);
        g.transform.SetAsFirstSibling();

        RectTransform rt = g.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = frameSize;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;

        Image img = g.GetComponent<Image>();
        Color c = bgColor;
        c.a = backgroundAlpha;
        img.color = c;
        img.raycastTarget = false;
    }

    void BuildTechGrid()
    {
        GameObject g = new GameObject("BG_TechGrid", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        g.transform.SetParent(transform, false);
        g.transform.SetSiblingIndex(1);

        RectTransform rt = g.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = frameSize;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;

        RawImage raw = g.GetComponent<RawImage>();
        raw.texture = MakeCleanGridTexture(256, 32);
        raw.uvRect = new Rect(0f, 0f, 8f, 4.5f);

        Color c = new Color(0f, 1f, 1f, gridAlpha);
        raw.color = c;
        raw.raycastTarget = false;
    }

    Texture2D MakeCleanGridTexture(int size, int step)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color line = new Color(0f, 1f, 1f, 0.35f);
        Color strong = new Color(0f, 1f, 1f, 0.65f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool normalLine = x % step == 0 || y % step == 0;
                bool strongLine = x % (step * 4) == 0 || y % (step * 4) == 0;

                if (strongLine)
                    tex.SetPixel(x, y, strong);
                else if (normalLine)
                    tex.SetPixel(x, y, line);
                else
                    tex.SetPixel(x, y, clear);
            }
        }

        tex.Apply();
        return tex;
    }

    void BuildOuterFrame()
    {
        float w = frameSize.x;
        float h = frameSize.y;
        float t = lineThickness;
        float c = cornerLength;

        CreateLine("Top_Left", new Vector2(-w / 2f + c + 260f, h / 2f), 520f, t, 0f, mainCyan);
        CreateLine("Top_Center", new Vector2(0f, h / 2f), 760f, t, 0f, mainCyan);
        CreateLine("Top_Right", new Vector2(w / 2f - c - 260f, h / 2f), 520f, t, 0f, mainCyan);

        CreateLine("Bottom_Left", new Vector2(-w / 2f + c + 360f, -h / 2f), 720f, t, 0f, mainCyan);
        CreateLine("Bottom_Right", new Vector2(w / 2f - c - 360f, -h / 2f), 720f, t, 0f, mainCyan);

        CreateLine("Left_Main", new Vector2(-w / 2f, 0f), h - c * 2f, t, 90f, mainCyan);
        CreateLine("Right_Main", new Vector2(w / 2f, 0f), h - c * 2f, t, 90f, mainCyan);

        CreateLine("LT_Diag", new Vector2(-w / 2f + c / 2f, h / 2f - c / 2f), c * 1.42f, t, -45f, mainCyan);
        CreateLine("RT_Diag", new Vector2(w / 2f - c / 2f, h / 2f - c / 2f), c * 1.42f, t, 45f, mainCyan);
        CreateLine("LB_Diag", new Vector2(-w / 2f + c / 2f, -h / 2f + c / 2f), c * 1.42f, t, 45f, mainCyan);
        CreateLine("RB_Diag", new Vector2(w / 2f - c / 2f, -h / 2f + c / 2f), c * 1.42f, t, -45f, mainCyan);
    }

    void BuildInnerFrame()
    {
        float w = frameSize.x - innerOffset * 2f;
        float h = frameSize.y - innerOffset * 2f;
        float t = 2f;
        float c = cornerLength - 30f;

        CreateLine("Inner_Top", new Vector2(0f, h / 2f), w - c * 2f, t, 0f, softCyan);
        CreateLine("Inner_Bottom", new Vector2(0f, -h / 2f), w - c * 2f, t, 0f, softCyan);
        CreateLine("Inner_Left", new Vector2(-w / 2f, 0f), h - c * 2f, t, 90f, softCyan);
        CreateLine("Inner_Right", new Vector2(w / 2f, 0f), h - c * 2f, t, 90f, softCyan);
    }

    void BuildHeaderAccent()
    {
        float h = frameSize.y;

        CreateLine("Header_Accent_1", new Vector2(0f, h / 2f - 48f), 1280f, 3f, 0f, softCyan);

        CreateLine("Header_Left_Accent", new Vector2(-880f, h / 2f - 26f), 120f, 5f, 0f, mainCyan);
        CreateLine("Header_Right_Accent", new Vector2(880f, h / 2f - 26f), 120f, 5f, 0f, mainCyan);
    }

    void BuildBottomAccent()
    {
        float h = frameSize.y;

        CreateLine("Bottom_Accent", new Vector2(0f, -h / 2f + 42f), 980f, 3f, 0f, softCyan);

        float startX = -90f;
        for (int i = 0; i < 6; i++)
        {
            CreateLine(
                "Bottom_Block_" + i,
                new Vector2(startX + i * 36f, -h / 2f + 42f),
                18f,
                8f,
                0f,
                mainCyan
            );
        }
    }

    void BuildReadyBox()
    {
        GameObject g = new GameObject("READY_Box", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        g.transform.SetParent(transform, false);

        RectTransform rt = g.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = readyPos;
        rt.sizeDelta = readySize;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        Image bg = g.GetComponent<Image>();
        bg.color = readyBg;
        bg.raycastTarget = false;

        CreateLineChild("R_Top", rt, new Vector2(0f, readySize.y / 2f), readySize.x, 3f, 0f, readyGreen);
        CreateLineChild("R_Bot", rt, new Vector2(0f, -readySize.y / 2f), readySize.x, 3f, 0f, readyGreen);
        CreateLineChild("R_Left", rt, new Vector2(-readySize.x / 2f, 0f), readySize.y, 3f, 90f, readyGreen);
        CreateLineChild("R_Right", rt, new Vector2(readySize.x / 2f, 0f), readySize.y, 3f, 90f, readyGreen);

        GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        txtObj.transform.SetParent(g.transform, false);

        RectTransform txtRt = txtObj.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;
        txtRt.localScale = Vector3.one;

        Text txt = txtObj.GetComponent<Text>();
        txt.text = "READY";
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = readyFontSize;
        txt.fontStyle = FontStyle.Bold;
        txt.color = readyGreen;
        txt.raycastTarget = false;
    }

    Image CreateLine(string name, Vector2 pos, float length, float thickness, float rotZ, Color col)
    {
        GameObject g = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        g.transform.SetParent(transform, false);

        RectTransform rt = g.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(length, thickness);
        rt.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        rt.localScale = Vector3.one;

        Image img = g.GetComponent<Image>();
        img.color = col;
        img.raycastTarget = false;

        return img;
    }

    Image CreateLineChild(string name, RectTransform parent, Vector2 pos, float length, float thickness, float rotZ, Color col)
    {
        GameObject g = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        g.transform.SetParent(parent, false);

        RectTransform rt = g.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(length, thickness);
        rt.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        rt.localScale = Vector3.one;

        Image img = g.GetComponent<Image>();
        img.color = col;
        img.raycastTarget = false;

        return img;
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(transform.GetChild(i).gameObject);
            else
                DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }
}