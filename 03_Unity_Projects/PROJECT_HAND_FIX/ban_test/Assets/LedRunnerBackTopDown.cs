using UnityEngine;

public class LedRunnerBackManual_Standard : MonoBehaviour
{
    [Header("Keo tung LED vao day theo thu tu TREN -> DUOI")]
    public Renderer[] leds;

    [Header("Mau den")]
    [ColorUsage(true, true)]
    public Color runColor = new Color(0.1f, 0.85f, 1.0f, 1f);

    [Header("Do sang")]
    [Range(0f, 1f)] public float offAlpha = 0f;      // luc tat
    [Range(0f, 1f)] public float onAlpha = 0.18f;    // luc bat
    [Range(0f, 50f)] public float peakEmission = 20f;

    [Header("Chay tu tren xuong")]
    [Range(0.1f, 30f)] public float speed = 7f;
    [Range(0.1f, 6f)] public float trailLength = 1.0f;
    [Range(1f, 8f)] public float falloffPower = 3.5f;
    [Range(0f, 5f)] public float resetGap = 0.6f;

    private Material[] mats;

    void Awake()
    {
        Setup();
    }

    void OnEnable()
    {
        if (mats == null || mats.Length == 0)
            Setup();
    }

    void Setup()
    {
        if (leds == null || leds.Length == 0)
        {
            mats = new Material[0];
            return;
        }

        mats = new Material[leds.Length];

        for (int i = 0; i < leds.Length; i++)
        {
            if (leds[i] == null) continue;

            // material instance rieng cho tung LED
            mats[i] = leds[i].material;

            // bat emission
            mats[i].EnableKeyword("_EMISSION");

            // trang thai tat ban dau
            mats[i].SetColor("_Color", new Color(runColor.r, runColor.g, runColor.b, offAlpha));
            mats[i].SetColor("_EmissionColor", runColor * 0f);
        }
    }

    void Update()
    {
        if (mats == null || mats.Length == 0) return;

        int count = mats.Length;
        float cycle = count + resetGap;
        float head = Mathf.Repeat(Time.time * speed, cycle);

        for (int i = 0; i < count; i++)
        {
            if (mats[i] == null) continue;

            float glow = 0f;

            // i = 0 la LED tren cung
            if (head < count)
            {
                float d = head - i;

                if (d >= 0f && d <= trailLength)
                {
                    float t = 1f - (d / trailLength);
                    glow = Mathf.Pow(t, falloffPower);
                }
            }

            float alpha = Mathf.Lerp(offAlpha, onAlpha, glow);
            float emission = Mathf.Lerp(0f, peakEmission, glow);

            mats[i].SetColor("_Color", new Color(runColor.r, runColor.g, runColor.b, alpha));
            mats[i].SetColor("_EmissionColor", runColor * emission);
        }
    }
}