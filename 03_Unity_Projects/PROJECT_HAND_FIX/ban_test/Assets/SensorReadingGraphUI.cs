using UnityEngine;
using UnityEngine.UI;
using TMPro;

[ExecuteAlways]
public class SensorReadingGraphUI : MaskableGraphic
{
    [Header("Sensor")]
    public float currentReading = 5.28f;   // gia tri dang hien tai
    public float targetReading = 5.28f;    // muc muon huong toi
    public float minReading = 0f;
    public float maxReading = 10f;
    public TMP_Text valueText;

    [Header("Demo/Test")]
    public bool demoRun = true;
    public float demoCenter = 5.28f;       // luc trung binh demo
    public float demoSwing = 2.0f;         // do dao dong demo
    public float demoSpeed = 0.35f;        // toc do demo tang giam

    [Header("Heartbeat Wave")]
    public float heartRate = 1.2f;         // nhip / giay
    public float spikeAmplitude = 1.2f;    // do cao dinh nhon
    public float jitterAmplitude = 0.08f;  // rung nhe
    public float smoothingSpeed = 4.0f;    // toc do currentReading bam targetReading

    [Header("Graph")]
    public int sampleCount = 50;
    public float lineWidth = 2.8f;
    public float glowWidth = 6f;
    public float gridWidth = 1f;
    public float sampleInterval = 0.035f;

    [Header("Colors")]
    public Color gridColor = new Color32(80, 235, 255, 35);
    public Color lineColor = new Color32(40, 240, 255, 255);
    public Color glowColor = new Color32(40, 240, 255, 70);

    private float[] samples;
    private float sampleTimer;
    private float waveTimer;

    protected override void Awake()
    {
        base.Awake();
        InitSamples();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        InitSamples();
        SetVerticesDirty();
    }

    void InitSamples()
    {
        sampleCount = Mathf.Max(16, sampleCount);
        samples = new float[sampleCount];

        float start = Mathf.Clamp(currentReading, minReading, maxReading);
        for (int i = 0; i < sampleCount; i++)
            samples[i] = start;
    }

    private void Update()
    {
        if (samples == null || samples.Length != sampleCount)
            InitSamples();

        if (Application.isPlaying)
        {
            // demo luc tang giam de test
            if (demoRun)
            {
                float demo = demoCenter + Mathf.Sin(Time.time * demoSpeed) * demoSwing;
                targetReading = Mathf.Clamp(demo, minReading, maxReading);
            }

            // currentReading bam ve targetReading mem hon
            currentReading = Mathf.Lerp(currentReading, targetReading, Time.deltaTime * smoothingSpeed);

            waveTimer += Time.deltaTime;
            sampleTimer += Time.deltaTime;

            while (sampleTimer >= sampleInterval)
            {
                sampleTimer -= sampleInterval;

                float newValue = GenerateHeartLikeValue(waveTimer, currentReading);
                ShiftAdd(newValue);
            }

            if (valueText != null)
                valueText.text = currentReading.ToString("0.00") + " N";

            SetVerticesDirty();
        }
        else
        {
            if (valueText != null)
                valueText.text = currentReading.ToString("0.00") + " N";
        }
    }

    float GenerateHeartLikeValue(float t, float baseReading)
    {
        // baseline phu thuoc theo luc hien tai
        float baseValue = baseReading;

        // tao 1 chu ky nhip tim
        float phase = Mathf.Repeat(t * heartRate, 1f);

        float pulse = 0f;

        // waveform kieu monitor / ECG don gian
        // 0 -> 1 trong 1 chu ky
        if (phase < 0.10f)
        {
            // baseline rung nhe
            pulse = 0.02f * Mathf.Sin(phase * 60f);
        }
        else if (phase < 0.18f)
        {
            // song nho dau
            float p = Mathf.InverseLerp(0.10f, 0.18f, phase);
            pulse = Mathf.Lerp(0f, 0.18f, p);
        }
        else if (phase < 0.23f)
        {
            float p = Mathf.InverseLerp(0.18f, 0.23f, phase);
            pulse = Mathf.Lerp(0.18f, -0.08f, p);
        }
        else if (phase < 0.28f)
        {
            // dinh nhon vut len
            float p = Mathf.InverseLerp(0.23f, 0.28f, phase);
            pulse = Mathf.Lerp(-0.08f, 1.0f, p);
        }
        else if (phase < 0.33f)
        {
            // roi xuong nhanh
            float p = Mathf.InverseLerp(0.28f, 0.33f, phase);
            pulse = Mathf.Lerp(1.0f, -0.35f, p);
        }
        else if (phase < 0.48f)
        {
            // hoi phuc len hump nho
            float p = Mathf.InverseLerp(0.33f, 0.48f, phase);
            pulse = Mathf.Lerp(-0.35f, 0.22f, p);
        }
        else if (phase < 0.68f)
        {
            float p = Mathf.InverseLerp(0.48f, 0.68f, phase);
            pulse = Mathf.Lerp(0.22f, 0.02f, p);
        }
        else
        {
            pulse = 0.015f * Mathf.Sin(phase * 45f);
        }

        // bien do xung tang nhe theo luc
        float force01 = Mathf.InverseLerp(minReading, maxReading, baseReading);
        float amp = Mathf.Lerp(0.55f, spikeAmplitude, force01);

        // noise nhe cho giong song song
        float jitter =
            Mathf.Sin(t * 18.0f) * jitterAmplitude +
            Mathf.Sin(t * 31.0f) * jitterAmplitude * 0.45f +
            Mathf.Sin(t * 47.0f) * jitterAmplitude * 0.25f;

        float result = baseValue + pulse * amp + jitter;

        return Mathf.Clamp(result, minReading, maxReading);
    }

    void ShiftAdd(float v)
    {
        for (int i = 0; i < samples.Length - 1; i++)
            samples[i] = samples[i + 1];

        samples[samples.Length - 1] = v;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (samples == null || samples.Length != sampleCount)
            InitSamples();

        Rect r = rectTransform.rect;

        float left = r.xMin + 4f;
        float right = r.xMax - 4f;
        float bottom = r.yMin + 4f;
        float top = r.yMax - 4f;

        // grid ngang
        for (int i = 0; i <= 3; i++)
        {
            float y = Mathf.Lerp(bottom, top, i / 3f);
            AddLine(vh, new Vector2(left, y), new Vector2(right, y), gridWidth, gridColor);
        }

        // grid doc
        for (int i = 0; i <= 5; i++)
        {
            float x = Mathf.Lerp(left, right, i / 5f);
            AddLine(vh, new Vector2(x, bottom), new Vector2(x, top), gridWidth, gridColor);
        }

        // ve song
        for (int i = 0; i < samples.Length - 1; i++)
        {
            Vector2 a = GetPoint(i, left, right, bottom, top);
            Vector2 b = GetPoint(i + 1, left, right, bottom, top);

            AddLine(vh, a, b, glowWidth, glowColor);
            AddLine(vh, a, b, lineWidth, lineColor);
        }
    }

    Vector2 GetPoint(int index, float left, float right, float bottom, float top)
    {
        float x = Mathf.Lerp(left, right, index / (float)(samples.Length - 1));
        float y01 = Mathf.InverseLerp(minReading, maxReading, samples[index]);
        float y = Mathf.Lerp(bottom, top, y01);
        return new Vector2(x, y);
    }

    void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
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

    void AddVert(VertexHelper vh, Vector2 pos, Color c)
    {
        UIVertex v = UIVertex.simpleVert;
        v.position = pos;
        v.color = c;
        vh.AddVert(v);
    }

    public void SetReading(float value)
    {
        demoRun = false;
        targetReading = Mathf.Clamp(value, minReading, maxReading);
    }

    public void SetReading(float value, float displayMax)
    {
        demoRun = false;
        maxReading = Mathf.Max(displayMax, minReading + 0.01f);
        targetReading = Mathf.Clamp(value, minReading, maxReading);
    }

    protected override void OnValidate()
    {
        base.OnValidate();

        currentReading = Mathf.Clamp(currentReading, minReading, maxReading);
        targetReading = Mathf.Clamp(targetReading, minReading, maxReading);

        if (valueText != null)
            valueText.text = currentReading.ToString("0.00") + " N";

        SetVerticesDirty();
    }

    protected override void Reset()
    {
        base.Reset();
        raycastTarget = false;
    }
}