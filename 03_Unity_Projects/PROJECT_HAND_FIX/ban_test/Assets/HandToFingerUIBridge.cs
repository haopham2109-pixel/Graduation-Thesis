using UnityEngine;

public class HandToFingerUIBridge : MonoBehaviour
{
    public HandUdpReceiver handReceiver;
    public FingerStatusUI fingerStatusUI;

    [Header("Force Gain (temporary simulated)")]
    public float thumbForceGain = 0.55f;
    public float indexForceGain = 0.60f;
    public float middleForceGain = 0.65f;
    public float ringForceGain = 0.50f;
    public float pinkyForceGain = 0.45f;

    private void Update()
    {
        if (handReceiver == null || fingerStatusUI == null) return;

        float thumb = Mathf.Clamp01(handReceiver.thumbValue);
        float index = Mathf.Clamp01(handReceiver.indexValue);
        float middle = Mathf.Clamp01(handReceiver.middleValue);
        float ring = Mathf.Clamp01(handReceiver.ringValue);
        float pinky = Mathf.Clamp01(handReceiver.pinkyValue);

        // Tạm thời force giả lập theo mức gập
        float thumbForce = thumb * thumbForceGain;
        float indexForce = index * indexForceGain;
        float middleForce = middle * middleForceGain;
        float ringForce = ring * ringForceGain;
        float pinkyForce = pinky * pinkyForceGain;

        fingerStatusUI.SetLiveData(
            thumb, index, middle, ring, pinky,
            thumbForce, indexForce, middleForce, ringForce, pinkyForce
        );
    }
}