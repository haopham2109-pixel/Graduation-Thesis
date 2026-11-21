using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Linq;

public class HoloScanBarTest : MonoBehaviour
{
    [Header("AUTO LAY GACH THEO PARENT")]
    public bool autoLayGachTheoParent = true;
    public Transform parentChuaGach;
    public int soGachGiua = 19;
    public int soGachPhai = 11;   // tu 20 den 30 la 11 gach

    [Header("NEU KHONG AUTO THI KEO TAY VAO DAY")]
    public Image[] gachGiua_1_19;
    public Image[] gachPhai_20_30;

    [Header("TEXT VA THANH %")]
    public TMP_Text percentText;
    public Image scanFill; // neu co thanh fill thi keo vao, khong co thi bo trong

    [Header("THOI GIAN")]
    public float thoiGianChay100 = 4.0f;
    public float tocDoNhayGiua = 5.0f;
    public float tocDoChayPhai = 7.0f;
    public float doDaiDuoiSangPhai = 3.5f;

    [Header("MAU SAC")]
    public Color mauTat = new Color(0.02f, 0.18f, 0.22f, 0.25f);
    public Color mauSang = new Color(0.05f, 0.90f, 1.00f, 1.00f);
    public Color mauLoiSang = new Color(0.80f, 1.00f, 1.00f, 1.00f);

    private Coroutine scanRoutine;
    private Coroutine nhayRoutine;
    private Coroutine phaiRoutine;

    void Start()
    {
        LayGachTuParentNeuCan();
        BatDauTest();
    }

    [ContextMenu("BAT DAU TEST")]
    public void BatDauTest()
    {
        if (scanRoutine != null) StopCoroutine(scanRoutine);
        if (nhayRoutine != null) StopCoroutine(nhayRoutine);
        if (phaiRoutine != null) StopCoroutine(phaiRoutine);

        TatTatCaGach();
        CapNhatPhanTram(0f);

        scanRoutine = StartCoroutine(ChayGachGiuaTu0Den100());
        phaiRoutine = StartCoroutine(ChayGachBenPhaiLienTuc());
    }

    void LayGachTuParentNeuCan()
    {
        if (!autoLayGachTheoParent || parentChuaGach == null) return;

        Image[] tatCaGach = parentChuaGach
            .GetComponentsInChildren<Image>(true)
            .Where(img => img.name.ToLower().Contains("gach"))
            .ToArray();

        if (tatCaGach.Length >= soGachGiua)
        {
            gachGiua_1_19 = tatCaGach.Take(soGachGiua).ToArray();
        }

        if (tatCaGach.Length >= soGachGiua + soGachPhai)
        {
            gachPhai_20_30 = tatCaGach
                .Skip(soGachGiua)
                .Take(soGachPhai)
                .ToArray();
        }
    }

    IEnumerator ChayGachGiuaTu0Den100()
    {
        float t = 0f;

        while (t < thoiGianChay100)
        {
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / thoiGianChay100);

            CapNhatPhanTram(progress);
            CapNhatGachGiuaTheoTienDo(progress);

            yield return null;
        }

        CapNhatPhanTram(1f);
        BatSangHetGachGiua();

        nhayRoutine = StartCoroutine(NhayGachGiuaSauKhiDay());
    }

    void CapNhatGachGiuaTheoTienDo(float progress)
    {
        if (gachGiua_1_19 == null || gachGiua_1_19.Length == 0) return;

        float viTriSang = progress * gachGiua_1_19.Length;

        for (int i = 0; i < gachGiua_1_19.Length; i++)
        {
            if (gachGiua_1_19[i] == null) continue;

            float mucSang = Mathf.Clamp01(viTriSang - i);
            SetMauGach(gachGiua_1_19[i], mucSang);
        }
    }

    IEnumerator NhayGachGiuaSauKhiDay()
    {
        float t = 0f;

        while (true)
        {
            t += Time.deltaTime;

            float pulse = 0.55f + 0.45f * Mathf.Sin(t * tocDoNhayGiua);

            for (int i = 0; i < gachGiua_1_19.Length; i++)
            {
                if (gachGiua_1_19[i] == null) continue;

                Color c = Color.Lerp(mauSang, mauLoiSang, pulse);
                c.a = Mathf.Lerp(0.55f, 1.0f, pulse);
                gachGiua_1_19[i].color = c;
            }

            yield return null;
        }
    }

    IEnumerator ChayGachBenPhaiLienTuc()
    {
        while (true)
        {
            if (gachPhai_20_30 != null && gachPhai_20_30.Length > 0)
            {
                int count = gachPhai_20_30.Length;
                float head = (Time.time * tocDoChayPhai) % count;

                for (int i = 0; i < count; i++)
                {
                    if (gachPhai_20_30[i] == null) continue;

                    float dist = head - i;
                    if (dist < 0) dist += count;

                    float mucSang = Mathf.Clamp01(1f - dist / doDaiDuoiSangPhai);
                    SetMauGach(gachPhai_20_30[i], mucSang);
                }
            }

            yield return null;
        }
    }

    void CapNhatPhanTram(float progress)
    {
        int percent = Mathf.RoundToInt(progress * 100f);

        if (percentText != null)
            percentText.text = percent + "%";

        if (scanFill != null)
            scanFill.fillAmount = progress;
    }

    void BatSangHetGachGiua()
    {
        if (gachGiua_1_19 == null) return;

        for (int i = 0; i < gachGiua_1_19.Length; i++)
        {
            if (gachGiua_1_19[i] == null) continue;
            SetMauGach(gachGiua_1_19[i], 1f);
        }
    }

    void TatTatCaGach()
    {
        if (gachGiua_1_19 != null)
        {
            foreach (Image img in gachGiua_1_19)
                if (img != null) img.color = mauTat;
        }

        if (gachPhai_20_30 != null)
        {
            foreach (Image img in gachPhai_20_30)
                if (img != null) img.color = mauTat;
        }
    }

    void SetMauGach(Image img, float mucSang)
    {
        Color c = Color.Lerp(mauTat, mauSang, mucSang);
        c.a = Mathf.Lerp(0.18f, 1.0f, mucSang);
        img.color = c;
    }
}