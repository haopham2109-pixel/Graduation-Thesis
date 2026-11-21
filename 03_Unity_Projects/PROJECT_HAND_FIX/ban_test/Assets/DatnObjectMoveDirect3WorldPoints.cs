using UnityEngine;

/*
DATN - OBJECT MOVE DIRECT 3 - WORLD POINTS V2
Fix loi Copy Component bi sai toa do khi object va point khac parent.

Nguyen nhan loi cu:
- Unity Copy Component / Paste Component Values tren Transform thuong copy LOCAL transform.
- Neu object con nam trong parent, con ShelfPoint nam ngoai root,
  local Position giong nhau nhung WORLD Position khac nhau -> bam Play vat chay lech.

Cach dung:
1) Remove script cu DatnObjectMoveDirect3 tren DATN_ObjectMoveController.
2) Gan script nay.
3) Keo thang object con vao:
   chiliObject, kimquatObject, orangeObject.
4) Keo 6 Empty point vao:
   Shelf/Palm point tung vat.
5) De object dung tren ke dung vi tri.
6) Right click component nay -> Capture Shelf Points From Current Object Worlds.
7) Keo tung vat vao long ban tay dung vi tri.
8) Right click component nay -> Capture Palm Points From Current Object Worlds.
9) Right click -> Snap Objects To Shelf Points.
10) Play -> tick useDebugObject -> chon Chili/Kimquat/Orange de test.
*/

public class DatnObjectMoveDirect3WorldPoints : MonoBehaviour
{
    public enum DetectedObject
    {
        None,
        Chili,
        Kimquat,
        Orange
    }

    [Header("OPTIONAL UI SOURCE")]
    public SystemStatusUIManual systemStatusUI;
    public bool readObjectFromSystemStatus = false;

    [Header("DEBUG TEST")]
    public bool useDebugObject = false;
    public DetectedObject debugObject = DetectedObject.None;

    [Header("DIRECT OBJECTS - KEO THANG OBJECT CON VAO DAY")]
    public Transform chiliObject;
    public Transform kimquatObject;
    public Transform orangeObject;

    [Header("TARGET POINTS - 6 EMPTY POINTS")]
    public Transform chiliShelfPoint;
    public Transform chiliPalmPoint;

    public Transform kimquatShelfPoint;
    public Transform kimquatPalmPoint;

    public Transform orangeShelfPoint;
    public Transform orangePalmPoint;

    [Header("MOVE SETTINGS")]
    public bool autoMove = true;

    [Tooltip("De FALSE de khi Debug=None thi vat dung yen, khong tu chay ve ke.")]
    public bool moveAllToShelfWhenNone = false;

    public bool resetOthersToShelf = true;

    public float moveSpeed = 1800f;
    public float rotateSpeed = 7f;
    public float arriveDistance = 8f;

    [Tooltip("Nen de TRUE voi mesh con de tranh xoay/pivot kho chiu.")]
    public bool keepCurrentScale = true;

    [Tooltip("Neu vat xoay sai khi chay thi tick TRUE de chi chay position.")]
    public bool keepCurrentRotation = false;

    [Header("RUNTIME")]
    public DetectedObject currentObject = DetectedObject.None;
    public bool sourceValid = false;

    private void Update()
    {
        if (!autoMove)
            return;

        currentObject = GetCurrentObject(out sourceValid);

        if (!sourceValid)
            return;

        if (currentObject == DetectedObject.None)
        {
            if (moveAllToShelfWhenNone)
                MoveAllToShelf();

            return;
        }

        MoveSelectedToPalm(currentObject);

        if (resetOthersToShelf)
            MoveOthersToShelf(currentObject);
    }

    private DetectedObject GetCurrentObject(out bool valid)
    {
        valid = false;

        if (useDebugObject)
        {
            valid = true;
            return debugObject;
        }

        if (readObjectFromSystemStatus && systemStatusUI != null)
        {
            valid = true;

            string s = systemStatusUI.objectValue == null ? "" : systemStatusUI.objectValue.ToLowerInvariant();

            if (s.Contains("chili") || s.Contains("ot") || s.Contains("ớt") || s.Contains("bot") || s.Contains("bột"))
                return DetectedObject.Chili;

            if (s.Contains("kimquat") || s.Contains("kim qu") || s.Contains("sauce") || s.Contains("kim_quat"))
                return DetectedObject.Kimquat;

            if (s.Contains("orange") || s.Contains("cam"))
                return DetectedObject.Orange;

            return DetectedObject.None;
        }

        return DetectedObject.None;
    }

    private void MoveSelectedToPalm(DetectedObject obj)
    {
        if (obj == DetectedObject.Chili)
            MoveToPoint(chiliObject, chiliPalmPoint);

        else if (obj == DetectedObject.Kimquat)
            MoveToPoint(kimquatObject, kimquatPalmPoint);

        else if (obj == DetectedObject.Orange)
            MoveToPoint(orangeObject, orangePalmPoint);
    }

    private void MoveOthersToShelf(DetectedObject active)
    {
        if (active != DetectedObject.Chili)
            MoveToPoint(chiliObject, chiliShelfPoint);

        if (active != DetectedObject.Kimquat)
            MoveToPoint(kimquatObject, kimquatShelfPoint);

        if (active != DetectedObject.Orange)
            MoveToPoint(orangeObject, orangeShelfPoint);
    }

    private void MoveAllToShelf()
    {
        MoveToPoint(chiliObject, chiliShelfPoint);
        MoveToPoint(kimquatObject, kimquatShelfPoint);
        MoveToPoint(orangeObject, orangeShelfPoint);
    }

    private void MoveToPoint(Transform obj, Transform targetPoint)
    {
        if (obj == null || targetPoint == null)
            return;

        obj.position = Vector3.MoveTowards(
            obj.position,
            targetPoint.position,
            moveSpeed * Time.deltaTime
        );

        if (!keepCurrentRotation)
        {
            obj.rotation = Quaternion.Slerp(
                obj.rotation,
                targetPoint.rotation,
                rotateSpeed * Time.deltaTime
            );
        }

        if (!keepCurrentScale)
        {
            obj.localScale = Vector3.Lerp(
                obj.localScale,
                targetPoint.localScale,
                rotateSpeed * Time.deltaTime
            );
        }

        if (Vector3.Distance(obj.position, targetPoint.position) <= arriveDistance)
            obj.position = targetPoint.position;
    }

    private void CopyObjectWorldToPoint(Transform obj, Transform point)
    {
        if (obj == null || point == null)
            return;

        point.position = obj.position;
        point.rotation = obj.rotation;
        point.localScale = obj.localScale;
    }

    private void SnapObjectToPoint(Transform obj, Transform point)
    {
        if (obj == null || point == null)
            return;

        obj.position = point.position;

        if (!keepCurrentRotation)
            obj.rotation = point.rotation;

        if (!keepCurrentScale)
            obj.localScale = point.localScale;
    }

    [ContextMenu("Capture Shelf Points From Current Object Worlds")]
    public void CaptureShelfPointsFromCurrentObjectWorlds()
    {
        CopyObjectWorldToPoint(chiliObject, chiliShelfPoint);
        CopyObjectWorldToPoint(kimquatObject, kimquatShelfPoint);
        CopyObjectWorldToPoint(orangeObject, orangeShelfPoint);

        Debug.Log("DATN: Captured SHELF points from current object WORLD transforms.");
    }

    [ContextMenu("Capture Palm Points From Current Object Worlds")]
    public void CapturePalmPointsFromCurrentObjectWorlds()
    {
        CopyObjectWorldToPoint(chiliObject, chiliPalmPoint);
        CopyObjectWorldToPoint(kimquatObject, kimquatPalmPoint);
        CopyObjectWorldToPoint(orangeObject, orangePalmPoint);

        Debug.Log("DATN: Captured PALM points from current object WORLD transforms.");
    }

    [ContextMenu("Snap Objects To Shelf Points")]
    public void SnapObjectsToShelfPoints()
    {
        SnapObjectToPoint(chiliObject, chiliShelfPoint);
        SnapObjectToPoint(kimquatObject, kimquatShelfPoint);
        SnapObjectToPoint(orangeObject, orangeShelfPoint);

        Debug.Log("DATN: Snapped objects to SHELF points.");
    }

    [ContextMenu("Snap Objects To Palm Points")]
    public void SnapObjectsToPalmPoints()
    {
        SnapObjectToPoint(chiliObject, chiliPalmPoint);
        SnapObjectToPoint(kimquatObject, kimquatPalmPoint);
        SnapObjectToPoint(orangeObject, orangePalmPoint);

        Debug.Log("DATN: Snapped objects to PALM points.");
    }

    [ContextMenu("Test Instant Chili")]
    public void TestInstantChili()
    {
        SnapObjectToPoint(chiliObject, chiliPalmPoint);
        SnapObjectToPoint(kimquatObject, kimquatShelfPoint);
        SnapObjectToPoint(orangeObject, orangeShelfPoint);
    }

    [ContextMenu("Test Instant Kimquat")]
    public void TestInstantKimquat()
    {
        SnapObjectToPoint(chiliObject, chiliShelfPoint);
        SnapObjectToPoint(kimquatObject, kimquatPalmPoint);
        SnapObjectToPoint(orangeObject, orangeShelfPoint);
    }

    [ContextMenu("Test Instant Orange")]
    public void TestInstantOrange()
    {
        SnapObjectToPoint(chiliObject, chiliShelfPoint);
        SnapObjectToPoint(kimquatObject, kimquatShelfPoint);
        SnapObjectToPoint(orangeObject, orangePalmPoint);
    }
}
