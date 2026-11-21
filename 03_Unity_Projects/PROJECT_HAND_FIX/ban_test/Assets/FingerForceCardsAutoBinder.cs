using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/*
DATN - AUTO BIND 5 FINGER FORCE CARDS V2
Fix loi script khong hien trong Add Component:
- Ban ForceAnalysisUIBinder hien tai co the dat field la "root" thay vi "cardRoot".
- V2 dung Reflection nen tuong thich ca root/cardRoot.
- Gan dung 5 o luc:
  LUC_THUMB  -> luc_thumb_thucte  / phantram_luc / Thanhday_Fill
  LUC_INDEX  -> luc_index_thucte  / phantram_luc / Thanhday_Fill
  LUC_MIDDLE -> luc_middle_thucte / phantram_luc / Thanhday_Fill
  LUC_RING   -> luc_ring_thucte   / phantram_luc / Thanhday_Fill
  LUC_PINKY  -> luc_pinky_thucte  / phantram_luc / Thanhday_Fill
*/

public class FingerForceCardsAutoBinder : MonoBehaviour
{
    [Header("References")]
    public ForceAnalysisUIBinder forceBinder;
    public Transform searchRoot;

    [Header("Options")]
    public bool bindOnStart = true;
    public bool logResult = true;

    private void Start()
    {
        if (bindOnStart)
            BindNow();
    }

    [ContextMenu("Bind Finger Force Cards Now")]
    public void BindNow()
    {
        if (forceBinder == null)
            forceBinder = GetComponent<ForceAnalysisUIBinder>();

#if UNITY_2023_1_OR_NEWER
        if (forceBinder == null)
            forceBinder = FindFirstObjectByType<ForceAnalysisUIBinder>();
#else
        if (forceBinder == null)
            forceBinder = FindObjectOfType<ForceAnalysisUIBinder>();
#endif

        if (forceBinder == null)
        {
            Debug.LogError("FingerForceCardsAutoBinder V2: Khong tim thay ForceAnalysisUIBinder.");
            return;
        }

        if (searchRoot == null)
        {
            GameObject main = GameObject.Find("HoloCanvas_Main");
            if (main != null) searchRoot = main.transform;
            else searchRoot = forceBinder.transform.root;
        }

        BindCard("thumb", "LUC_THUMB", "luc_thumb_thucte");
        BindCard("index", "LUC_INDEX", "luc_index_thucte");
        BindCard("middle", "LUC_MIDDLE", "luc_middle_thucte");
        BindCard("ring", "LUC_RING", "luc_ring_thucte");
        BindCard("pinky", "LUC_PINKY", "luc_pinky_thucte");

        if (logResult)
            Debug.Log("FingerForceCardsAutoBinder V2 DONE. Neu khong co warning NULL la da bind xong 5 o luc.");
    }

    private void BindCard(string cardFieldName, string rootName, string forceTextName)
    {
        object card = GetFieldValue(forceBinder, cardFieldName);
        if (card == null)
        {
            Debug.LogWarning("FingerForceCardsAutoBinder V2: Khong tim thay card field: " + cardFieldName);
            return;
        }

        Transform root = FindDeepChild(searchRoot, rootName);
        TMP_Text forceText = null;
        TMP_Text percentText = null;
        Image fillImage = null;

        if (root != null)
        {
            Transform forceTf = FindDeepChild(root, forceTextName);
            if (forceTf != null)
                forceText = forceTf.GetComponent<TMP_Text>();

            Transform pctTf = FindDeepChild(root, "phantram_luc");
            if (pctTf == null)
                pctTf = FindFirstTMPContaining(root, "%");

            if (pctTf != null)
                percentText = pctTf.GetComponent<TMP_Text>();

            Transform fillTf = FindDeepChild(root, "Thanhday_Fill");
            if (fillTf != null)
                fillImage = fillTf.GetComponent<Image>();

            if (fillImage != null)
                PrepareFillImage(fillImage);
        }

        // Tuong thich ca ban ForceAnalysisUIBinder cu va moi
        SetCardField(card, "root", root);
        SetCardField(card, "cardRoot", root);
        SetCardField(card, "forceText", forceText);
        SetCardField(card, "percentText", percentText);
        SetCardField(card, "barFill", fillImage);

        if (logResult)
        {
            Debug.Log(
                "Bind " + cardFieldName.ToUpper() +
                " | root=" + ObjName(root) +
                " | force=" + ObjName(forceText) +
                " | percent=" + ObjName(percentText) +
                " | fill=" + ObjName(fillImage)
            );

            if (root == null) Debug.LogWarning(rootName + " NULL");
            if (forceText == null) Debug.LogWarning(rootName + " forceText NULL: " + forceTextName);
            if (percentText == null) Debug.LogWarning(rootName + " percentText NULL: phantram_luc");
            if (fillImage == null) Debug.LogWarning(rootName + " barFill NULL: Thanhday_Fill");
        }
    }

    private object GetFieldValue(object obj, string fieldName)
    {
        if (obj == null) return null;

        FieldInfo f = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null) return null;

        return f.GetValue(obj);
    }

    private void SetCardField(object card, string fieldName, Object value)
    {
        if (card == null) return;

        FieldInfo f = card.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null) return;

        if (value == null)
        {
            f.SetValue(card, null);
            return;
        }

        if (f.FieldType.IsAssignableFrom(value.GetType()))
            f.SetValue(card, value);
    }

    private void PrepareFillImage(Image img)
    {
        if (img == null) return;

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 0f;
        img.raycastTarget = false;
    }

    private string ObjName(Object obj)
    {
        return obj == null ? "NULL" : obj.name;
    }

    private Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName)) return null;

        if (parent.name == childName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (child.name == childName)
                return child;

            Transform result = FindDeepChild(child, childName);
            if (result != null)
                return result;
        }

        return null;
    }

    private Transform FindFirstTMPContaining(Transform root, string contains)
    {
        if (root == null) return null;

        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text t in texts)
        {
            if (t != null && t.text != null && t.text.Contains(contains))
                return t.transform;
        }

        return null;
    }
}
