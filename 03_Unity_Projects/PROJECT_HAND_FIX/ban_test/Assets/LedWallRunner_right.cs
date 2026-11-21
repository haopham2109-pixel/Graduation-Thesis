using UnityEngine;

public class LedFillDownRight_Standard : MonoBehaviour
{
    [Header("Keo LED vao day theo thu tu TREN -> DUOI")]
    public Renderer[] leds;

    [Header("Mau den")]
    [ColorUsage(true, true)]
    public Color runColor = new Color(0.1f, 0.85f, 1.0f, 1f);

    [Header("Do sang")]
    [Range(0f, 1f)] public float offAlpha = 0f;
    [Range(0f, 1f)] public float onAlpha = 0.18f;
    [Range(0f, 50f)] public float peakEmission = 20f;

    [Header("Thoi gian")]
    [Range(0.02f, 2f)] public float stepTime = 0.12f;     // thoi gian moi LED sang/tat
    [Range(0f, 2f)] public float holdOnTime = 0.25f;      // dung khi sang het
    [Range(0f, 2f)] public float holdOffTime = 0.15f;     // dung khi tat het

    private Material[] mats;
    private float timer;
    private int currentIndex = 0;

    private enum Phase
    {
        FillOn,     // sang dan tu tren xuong
        HoldOn,     // giu sang het
        FillOff,    // tat dan tu tren xuong
        HoldOff     // giu tat het
    }

    private Phase phase = Phase.FillOn;

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

            mats[i] = leds[i].material;
            mats[i].EnableKeyword("_EMISSION");

            SetLedState(i, false);
        }

        timer = 0f;
        currentIndex = 0;
        phase = Phase.FillOn;
    }

    void Update()
    {
        if (mats == null || mats.Length == 0) return;

        timer += Time.deltaTime;

        switch (phase)
        {
            case Phase.FillOn:
                if (timer >= stepTime)
                {
                    timer = 0f;

                    if (currentIndex < mats.Length)
                    {
                        SetLedState(currentIndex, true);
                        currentIndex++;
                    }

                    if (currentIndex >= mats.Length)
                    {
                        phase = Phase.HoldOn;
                        timer = 0f;
                    }
                }
                break;

            case Phase.HoldOn:
                if (timer >= holdOnTime)
                {
                    phase = Phase.FillOff;
                    timer = 0f;
                    currentIndex = 0;
                }
                break;

            case Phase.FillOff:
                if (timer >= stepTime)
                {
                    timer = 0f;

                    if (currentIndex < mats.Length)
                    {
                        SetLedState(currentIndex, false);
                        currentIndex++;
                    }

                    if (currentIndex >= mats.Length)
                    {
                        phase = Phase.HoldOff;
                        timer = 0f;
                    }
                }
                break;

            case Phase.HoldOff:
                if (timer >= holdOffTime)
                {
                    phase = Phase.FillOn;
                    timer = 0f;
                    currentIndex = 0;
                }
                break;
        }
    }

    void SetLedState(int index, bool isOn)
    {
        if (index < 0 || index >= mats.Length || mats[index] == null) return;

        if (isOn)
        {
            mats[index].SetColor("_Color", new Color(runColor.r, runColor.g, runColor.b, onAlpha));
            mats[index].SetColor("_EmissionColor", runColor * peakEmission);
        }
        else
        {
            mats[index].SetColor("_Color", new Color(runColor.r, runColor.g, runColor.b, offAlpha));
            mats[index].SetColor("_EmissionColor", runColor * 0f);
        }
    }
}