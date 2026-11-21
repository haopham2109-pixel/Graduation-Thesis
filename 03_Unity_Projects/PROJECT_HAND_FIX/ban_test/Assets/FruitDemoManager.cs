using UnityEngine;
using TMPro;

public class FruitDemoManager : MonoBehaviour
{
    [Header("Fruit Objects")]
    public GameObject orangeObject;
    public GameObject appleObject;
    public GameObject guavaObject;

    [Header("Left Panel UI")]
    public TMP_Text objectText;
    public TMP_Text gripText;
    public TMP_Text logText;

    [Header("Object Card UI")]
    public TMP_Text objectCardName;
    public TMP_Text objectCardState;

    [Header("Top Badge UI")]
    public TMP_Text statusBadgeText;

    private void Start()
    {
        ShowOrange();
    }

    public void ShowOrange()
    {
        SetOnlyOneActive(orangeObject);

        if (objectText != null) objectText.text = "Object: Orange";
        if (gripText != null) gripText.text = "Grip State: Ready";
        if (logText != null) logText.text = "[INFO] Orange object loaded";

        if (objectCardName != null) objectCardName.text = "Orange";
        if (objectCardState != null) objectCardState.text = "Ready to Grasp";

        if (statusBadgeText != null) statusBadgeText.text = "READY";
    }

    public void ShowApple()
    {
        SetOnlyOneActive(appleObject);

        if (objectText != null) objectText.text = "Object: Apple";
        if (gripText != null) gripText.text = "Grip State: Ready";
        if (logText != null) logText.text = "[INFO] Apple object loaded";

        if (objectCardName != null) objectCardName.text = "Apple";
        if (objectCardState != null) objectCardState.text = "Ready to Grasp";

        if (statusBadgeText != null) statusBadgeText.text = "READY";
    }

    public void ShowGuava()
    {
        SetOnlyOneActive(guavaObject);

        if (objectText != null) objectText.text = "Object: Guava";
        if (gripText != null) gripText.text = "Grip State: Ready";
        if (logText != null) logText.text = "[INFO] Guava object loaded";

        if (objectCardName != null) objectCardName.text = "Guava";
        if (objectCardState != null) objectCardState.text = "Ready to Grasp";

        if (statusBadgeText != null) statusBadgeText.text = "READY";
    }

    public void HideAll()
    {
        if (orangeObject != null) orangeObject.SetActive(false);
        if (appleObject != null) appleObject.SetActive(false);
        if (guavaObject != null) guavaObject.SetActive(false);

        if (objectText != null) objectText.text = "Object: None";
        if (gripText != null) gripText.text = "Grip State: Idle";
        if (logText != null) logText.text = "[INFO] No object detected";

        if (objectCardName != null) objectCardName.text = "No Object";
        if (objectCardState != null) objectCardState.text = "Waiting";

        if (statusBadgeText != null) statusBadgeText.text = "IDLE";
    }

    private void SetOnlyOneActive(GameObject activeObject)
    {
        if (orangeObject != null) orangeObject.SetActive(activeObject == orangeObject);
        if (appleObject != null) appleObject.SetActive(activeObject == appleObject);
        if (guavaObject != null) guavaObject.SetActive(activeObject == guavaObject);
    }
}