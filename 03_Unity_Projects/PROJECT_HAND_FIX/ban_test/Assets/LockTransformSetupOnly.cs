using UnityEngine;

/*
DATN - LOCK TRANSFORM SETUP ONLY
Muc dich:
- Giu vat dung yen trong Scene, khong bi xoay theo camera.
- Dung de khoa rotation/position luc canh vat.
- Khac voi FaceCameraSetupOnly: script nay KHONG nhin theo camera.

Cach dung:
1) Gan vao OBJECT CHA, vi du ChiliObject_Control.
2) Chinh vat dung vi tri/hhuong mong muon.
3) Bam chuot phai tren component -> Capture Current Transform.
4) Tick lockPosition / lockRotation neu muon khoa.
5) Khi khong can nua thi tat enableLock hoac remove component.
*/

[ExecuteAlways]
public class LockTransformSetupOnly : MonoBehaviour
{
    [Header("Enable Lock")]
    public bool enableLock = true;

    [Header("Lock Options")]
    public bool lockPosition = false;
    public bool lockRotation = true;
    public bool lockScale = false;

    [Header("Locked Values")]
    public Vector3 lockedPosition;
    public Vector3 lockedEulerRotation;
    public Vector3 lockedScale = Vector3.one;

    private void OnEnable()
    {
        if (lockedScale == Vector3.zero)
            lockedScale = transform.localScale;
    }

    private void LateUpdate()
    {
        if (!enableLock) return;

        if (lockPosition)
            transform.position = lockedPosition;

        if (lockRotation)
            transform.rotation = Quaternion.Euler(lockedEulerRotation);

        if (lockScale)
            transform.localScale = lockedScale;
    }

    [ContextMenu("Capture Current Transform")]
    public void CaptureCurrentTransform()
    {
        lockedPosition = transform.position;
        lockedEulerRotation = transform.rotation.eulerAngles;
        lockedScale = transform.localScale;
    }

    [ContextMenu("Unlock All")]
    public void UnlockAll()
    {
        enableLock = false;
        lockPosition = false;
        lockRotation = false;
        lockScale = false;
    }
}
