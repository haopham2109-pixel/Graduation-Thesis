using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GripStateManager : MonoBehaviour
{
    public enum GripState
    {
        Idle,
        Ready,
        Contact,
        Grasping,
        Stable
    }

    [Header("References")]
    public FruitDemoManager fruitDemoManager;
    public FingerStatusUI fingerStatusUI;
    public Image statusBadgeBg;

    [Header("Thresholds")]
    [Range(0f, 1f)] public float readyThreshold = 0.15f;
    [Range(0f, 1f)] public float contactThreshold = 0.35f;
    [Range(0f, 1f)] public float graspingThreshold = 0.55f;
    [Range(0f, 1f)] public float stableThreshold = 0.80f;

    [Header("Stable Condition")]
    public float stableHoldSeconds = 0.6f;

    [Header("Debug")]
    public GripState currentState = GripState.Idle;
    public float avgCurl = 0f;

    private GripState lastState = GripState.Idle;
    private float stableTimer = 0f;

    private void Update()
    {
        if (fruitDemoManager == null || fingerStatusUI == null)
            return;

        bool objectDetected = IsAnyObjectActive();

        avgCurl = (
            fingerStatusUI.thumbCurl +
            fingerStatusUI.indexCurl +
            fingerStatusUI.middleCurl +
            fingerStatusUI.ringCurl +
            fingerStatusUI.pinkyCurl
        ) / 5f;

        GripState newState = EvaluateState(objectDetected, avgCurl);

        if (newState != currentState)
        {
            currentState = newState;
            ApplyStateUI(currentState);
        }

        if (currentState != lastState)
        {
            lastState = currentState;
        }
    }

    private bool IsAnyObjectActive()
    {
        bool orange = fruitDemoManager.orangeObject != null && fruitDemoManager.orangeObject.activeSelf;
        bool apple = fruitDemoManager.appleObject != null && fruitDemoManager.appleObject.activeSelf;
        bool guava = fruitDemoManager.guavaObject != null && fruitDemoManager.guavaObject.activeSelf;

        return orange || apple || guava;
    }

    private GripState EvaluateState(bool objectDetected, float avg)
    {
        if (!objectDetected)
        {
            stableTimer = 0f;
            return GripState.Idle;
        }

        if (avg < readyThreshold)
        {
            stableTimer = 0f;
            return GripState.Ready;
        }

        if (avg < contactThreshold)
        {
            stableTimer = 0f;
            return GripState.Contact;
        }

        if (avg < graspingThreshold)
        {
            stableTimer = 0f;
            return GripState.Grasping;
        }

        if (avg >= stableThreshold)
        {
            stableTimer += Time.deltaTime;
            if (stableTimer >= stableHoldSeconds)
                return GripState.Stable;

            return GripState.Grasping;
        }

        stableTimer = 0f;
        return GripState.Grasping;
    }

    private void ApplyStateUI(GripState state)
    {
        string gripStateText = "";
        string badgeText = "";
        Color badgeColor = Color.gray;

        switch (state)
        {
            case GripState.Idle:
                gripStateText = "Idle";
                badgeText = "IDLE";
                badgeColor = new Color(0.45f, 0.45f, 0.45f, 1f);
                break;

            case GripState.Ready:
                gripStateText = "Ready";
                badgeText = "READY";
                badgeColor = new Color(0.15f, 0.75f, 0.20f, 1f);
                break;

            case GripState.Contact:
                gripStateText = "Contact";
                badgeText = "CONTACT";
                badgeColor = new Color(0.95f, 0.80f, 0.15f, 1f);
                break;

            case GripState.Grasping:
                gripStateText = "Grasping";
                badgeText = "GRASP";
                badgeColor = new Color(1.00f, 0.55f, 0.10f, 1f);
                break;

            case GripState.Stable:
                gripStateText = "Stable";
                badgeText = "STABLE";
                badgeColor = new Color(0.10f, 0.80f, 0.85f, 1f);
                break;
        }

        if (fruitDemoManager.gripText != null)
            fruitDemoManager.gripText.text = "Grip State: " + gripStateText;

        if (fruitDemoManager.objectCardState != null)
            fruitDemoManager.objectCardState.text = gripStateText;

        if (fruitDemoManager.statusBadgeText != null)
            fruitDemoManager.statusBadgeText.text = badgeText;

        if (statusBadgeBg != null)
            statusBadgeBg.color = badgeColor;

        if (fruitDemoManager.logText != null)
            fruitDemoManager.logText.text = "[INFO] Grip state: " + gripStateText;
    }
}