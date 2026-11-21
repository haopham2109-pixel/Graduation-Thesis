using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class SetPanelColorTest : MonoBehaviour
{
    public Image targetImage;

    public Color panelColor = new Color32(4, 14, 24, 175);

    void OnValidate()
    {
        ApplyColor();
    }

    void Start()
    {
        ApplyColor();
    }

    public void ApplyColor()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        if (targetImage != null)
            targetImage.color = panelColor;
    }
}