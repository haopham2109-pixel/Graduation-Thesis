using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// DATN temporary calibration tool.
/// Purpose: tune object-specific grip percentages in Unity using the EXISTING HandUdpReceiver.
/// It sends the same UDP JSON hand packet format as Python to localhost:5052.
/// No finger bone assignment is needed.
/// Version: Inspector Buttons. You can tune directly from Inspector, no Game View panel required.
/// Remove this script after collecting the presets.
/// </summary>
public class GripPresetUdpTuner : MonoBehaviour
{
    [Serializable]
    public class ObjectEntry
    {
        public string label = "orange";
        public string displayName = "Orange";
        public Transform objectRoot;
        public Transform shelfPoint;
        public Transform palmPoint;
        public Vector3 palmLocalScale = Vector3.zero; // 0,0,0 = keep current scale
    }

    [Header("Objects")]
    public List<ObjectEntry> objects = new List<ObjectEntry>();

    [Header("UDP to existing HandUdpReceiver")]
    public string udpIp = "127.0.0.1";
    public int handUdpPort = 5052;
    public bool sendHandPacket = true;
    public float sendRateHz = 60f;

    [Header("Object motion")]
    public bool moveSelectedObjectToPalm = true;
    public bool hideOtherObjects = false;
    public float objectMoveTime = 0.45f;

    [Header("Grip sliders (%)")]
    [Range(0, 100)] public float thumb = 0f;
    [Range(0, 100)] public float index = 0f;
    [Range(0, 100)] public float middle = 0f;
    [Range(0, 100)] public float ring = 0f;
    [Range(0, 100)] public float pinky = 0f;

    [Header("Inspector / Game Panel")]
    [SerializeField] private int selectedObject = 0;
    public bool showWindow = false; // default false: tune in Inspector instead of Game View
    public Rect windowRect = new Rect(25, 25, 430, 470);

    private UdpClient udp;
    private IPEndPoint handEndPoint;
    private float nextSendTime;

    private readonly Dictionary<string, Vector3> originalScale = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, Vector3> originalPos = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, Quaternion> originalRot = new Dictionary<string, Quaternion>();

    private struct Preset
    {
        public float thumb, index, middle, ring, pinky;
        public Preset(float t, float i, float m, float r, float p)
        {
            thumb = t; index = i; middle = m; ring = r; pinky = p;
        }
    }

    private readonly Dictionary<string, Preset> savedPresets = new Dictionary<string, Preset>();

    void Start()
    {
        InitUdpIfNeeded();
        CacheOriginalTransforms();
        SelectObject(Mathf.Clamp(selectedObject, 0, Mathf.Max(0, objects.Count - 1)));
    }

    void OnDestroy()
    {
        try { udp?.Close(); } catch { }
        udp = null;
    }

    void Update()
    {
        if (!sendHandPacket) return;
        if (!Application.isPlaying) return;
        InitUdpIfNeeded();
        if (udp == null) return;
        if (Time.unscaledTime < nextSendTime) return;
        nextSendTime = Time.unscaledTime + 1f / Mathf.Max(1f, sendRateHz);
        SendHandPacket();
    }

    void InitUdpIfNeeded()
    {
        if (udp != null) return;
        try
        {
            udp = new UdpClient();
            handEndPoint = new IPEndPoint(IPAddress.Parse(udpIp), handUdpPort);
        }
        catch (Exception e)
        {
            Debug.LogError("GripPresetUdpTuner UDP init error: " + e.Message);
        }
    }

    void CacheOriginalTransforms()
    {
        foreach (var obj in objects)
        {
            if (obj == null || obj.objectRoot == null) continue;
            string key = obj.objectRoot.GetInstanceID().ToString();
            if (!originalScale.ContainsKey(key))
            {
                originalScale[key] = obj.objectRoot.localScale;
                originalPos[key] = obj.objectRoot.position;
                originalRot[key] = obj.objectRoot.rotation;
            }
        }
    }

    void SelectObject(int idx)
    {
        if (objects == null || objects.Count == 0) return;
        selectedObject = Mathf.Clamp(idx, 0, objects.Count - 1);
        CacheOriginalTransforms();

        for (int i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            if (obj == null || obj.objectRoot == null) continue;

            if (hideOtherObjects)
                obj.objectRoot.gameObject.SetActive(i == selectedObject);
            else
                obj.objectRoot.gameObject.SetActive(true);
        }

        if (moveSelectedObjectToPalm)
            MoveCurrentObjectToPalmImmediate();
    }

    void MoveCurrentObjectToPalmImmediate()
    {
        var obj = CurrentObject();
        if (obj == null || obj.objectRoot == null || obj.palmPoint == null) return;

        obj.objectRoot.position = obj.palmPoint.position;
        obj.objectRoot.rotation = obj.palmPoint.rotation;

        if (obj.palmLocalScale != Vector3.zero)
            obj.objectRoot.localScale = obj.palmLocalScale;
    }

    void MoveCurrentObjectToShelfImmediate()
    {
        var obj = CurrentObject();
        if (obj == null || obj.objectRoot == null) return;

        if (obj.shelfPoint != null)
        {
            obj.objectRoot.position = obj.shelfPoint.position;
            obj.objectRoot.rotation = obj.shelfPoint.rotation;
        }
        else
        {
            string key = obj.objectRoot.GetInstanceID().ToString();
            if (originalPos.ContainsKey(key))
            {
                obj.objectRoot.position = originalPos[key];
                obj.objectRoot.rotation = originalRot[key];
            }
        }

        string k = obj.objectRoot.GetInstanceID().ToString();
        if (originalScale.ContainsKey(k)) obj.objectRoot.localScale = originalScale[k];
    }

    ObjectEntry CurrentObject()
    {
        if (objects == null || objects.Count == 0) return null;
        selectedObject = Mathf.Clamp(selectedObject, 0, objects.Count - 1);
        return objects[selectedObject];
    }

    string CurrentLabel()
    {
        var obj = CurrentObject();
        if (obj == null || string.IsNullOrWhiteSpace(obj.label)) return "none";
        return obj.label.Trim();
    }

    void SendHandPacket()
    {
        string label = CurrentLabel();

        // Make one percentage control all three joints of each finger.
        string json = string.Format(CultureInfo.InvariantCulture,
            "{{" +
            "\"thumb1\":{0:0.###},\"thumb2\":{0:0.###},\"thumb3\":{0:0.###}," +
            "\"index1\":{1:0.###},\"index2\":{1:0.###},\"index3\":{1:0.###}," +
            "\"middle1\":{2:0.###},\"middle2\":{2:0.###},\"middle3\":{2:0.###}," +
            "\"ring1\":{3:0.###},\"ring2\":{3:0.###},\"ring3\":{3:0.###}," +
            "\"pinky1\":{4:0.###},\"pinky2\":{4:0.###},\"pinky3\":{4:0.###}," +
            "\"object_label\":\"{5}\"," +
            "\"object_raw_label\":\"{5}\"," +
            "\"auto_grip_state\":\"PRESET_TUNING\"," +
            "\"auto_grip_status\":\"PRESET_TUNING\"," +
            "\"thumb_force\":0,\"index_force\":0,\"middle_force\":0,\"ring_force\":0,\"pinky_force\":0," +
            "\"palm_force_n\":0,\"total_force_n\":0,\"target_total_n\":0" +
            "}}",
            thumb, index, middle, ring, pinky, EscapeJson(label));

        byte[] data = Encoding.UTF8.GetBytes(json);
        try { udp.Send(data, data.Length, handEndPoint); }
        catch (Exception e) { Debug.LogWarning("GripPresetUdpTuner send error: " + e.Message); }
    }

    static string EscapeJson(string s)
    {
        return string.IsNullOrEmpty(s) ? "none" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ===================== Public API for Inspector buttons =====================
    public int SelectedObjectIndex => selectedObject;

    public string GetObjectButtonName(int i)
    {
        if (objects == null || i < 0 || i >= objects.Count || objects[i] == null) return "Object " + i;
        var obj = objects[i];
        if (!string.IsNullOrWhiteSpace(obj.displayName)) return obj.displayName.Trim();
        if (!string.IsNullOrWhiteSpace(obj.label)) return obj.label.Trim();
        return "Object " + i;
    }

    public void InspectorSelectObject(int idx)
    {
        SelectObject(idx);
        SendOnceFromInspector();
    }

    public void InspectorMoveToPalm()
    {
        CacheOriginalTransforms();
        MoveCurrentObjectToPalmImmediate();
    }

    public void InspectorMoveToShelf()
    {
        CacheOriginalTransforms();
        MoveCurrentObjectToShelfImmediate();
    }

    public void InspectorOpenAll()
    {
        thumb = index = middle = ring = pinky = 0f;
        SendOnceFromInspector();
    }

    public void InspectorCloseAll()
    {
        thumb = index = middle = ring = pinky = 100f;
        SendOnceFromInspector();
    }

    public void InspectorSendOnce()
    {
        SendOnceFromInspector();
    }

    void SendOnceFromInspector()
    {
        if (!Application.isPlaying) return;
        InitUdpIfNeeded();
        if (udp == null) return;
        SendHandPacket();
    }

    public void InspectorSaveCurrentPreset()
    {
        savedPresets[CurrentLabel()] = new Preset(thumb, index, middle, ring, pinky);
        Debug.Log("Saved preset: " + CurrentPresetLine());
    }

    public void InspectorCopyCurrentPresetLine()
    {
        GUIUtility.systemCopyBuffer = CurrentPresetLine();
        Debug.Log("Copied current preset: " + CurrentPresetLine());
    }

    public void InspectorCopyAllPresets()
    {
        InspectorSaveCurrentPreset();

        StringBuilder sb = new StringBuilder();
        if (objects != null)
        {
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                string label = string.IsNullOrWhiteSpace(obj.label) ? "none" : obj.label.Trim();
                if (savedPresets.TryGetValue(label, out Preset p))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "{0}: THUMB={1:0}, INDEX={2:0}, MIDDLE={3:0}, RING={4:0}, PINKY={5:0}\n",
                        label, p.thumb, p.index, p.middle, p.ring, p.pinky);
                }
            }
        }

        string text = sb.ToString().TrimEnd();
        GUIUtility.systemCopyBuffer = text;
        Debug.Log("Copied ALL presets:\n" + text);
    }

    public string InspectorCurrentPresetLine()
    {
        return CurrentPresetLine();
    }

    string CurrentPresetLine()
    {
        return string.Format(CultureInfo.InvariantCulture,
            "{0}: THUMB={1:0}, INDEX={2:0}, MIDDLE={3:0}, RING={4:0}, PINKY={5:0}",
            CurrentLabel(), thumb, index, middle, ring, pinky);
    }

    // Optional legacy Game View window. Default hidden.
    void OnGUI()
    {
        if (!showWindow) return;
        windowRect = GUI.Window(23052026, windowRect, DrawWindow, "DATN Grip Preset UDP Tuner");
    }

    void DrawWindow(int id)
    {
        GUILayout.Label("Inspector version available. You can disable Show Window.");
        GUILayout.Space(5);

        if (objects != null)
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (GUILayout.Toggle(selectedObject == i, GetObjectButtonName(i), "Button"))
                {
                    if (selectedObject != i) InspectorSelectObject(i);
                }
            }
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Move to Palm")) InspectorMoveToPalm();
        if (GUILayout.Button("Move to Shelf")) InspectorMoveToShelf();
        GUILayout.EndHorizontal();

        DrawGuiSlider("Thumb", ref thumb);
        DrawGuiSlider("Index", ref index);
        DrawGuiSlider("Middle", ref middle);
        DrawGuiSlider("Ring", ref ring);
        DrawGuiSlider("Pinky", ref pinky);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Open 0%")) InspectorOpenAll();
        if (GUILayout.Button("Close 100%")) InspectorCloseAll();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save current")) InspectorSaveCurrentPreset();
        if (GUILayout.Button("Copy ALL")) InspectorCopyAllPresets();
        GUILayout.EndHorizontal();

        GUILayout.TextArea(CurrentPresetLine(), GUILayout.Height(38));
        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }

    void DrawGuiSlider(string name, ref float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(name, GUILayout.Width(60));
        value = GUILayout.HorizontalSlider(value, 0f, 100f, GUILayout.Width(260));
        string txt = GUILayout.TextField(Mathf.RoundToInt(value).ToString(), GUILayout.Width(45));
        if (int.TryParse(txt, out int iv)) value = Mathf.Clamp(iv, 0, 100);
        GUILayout.EndHorizontal();
    }
}


#if UNITY_EDITOR
[CustomEditor(typeof(GripPresetUdpTuner))]
public class GripPresetUdpTunerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        GripPresetUdpTuner t = (GripPresetUdpTuner)target;

        DrawDefaultInspector();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("DATN Inspector Tuning Panel", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Bam Play truoc. Inspector buttons van hien, nhung UDP hand packet chi gui khi Play Mode dang chay.", MessageType.Info);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Select object", EditorStyles.boldLabel);

        if (t.objects != null)
        {
            for (int i = 0; i < t.objects.Count; i++)
            {
                bool active = (i == t.SelectedObjectIndex);
                string btnName = (active ? "● " : "○ ") + t.GetObjectButtonName(i);
                if (GUILayout.Button(btnName, GUILayout.Height(28)))
                {
                    Undo.RecordObject(t, "Select grip preset object");
                    t.InspectorSelectObject(i);
                    EditorUtility.SetDirty(t);
                }
            }
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Move selected to PALM", GUILayout.Height(26)))
        {
            Undo.RecordObject(t, "Move object to palm");
            t.InspectorMoveToPalm();
            EditorUtility.SetDirty(t);
        }
        if (GUILayout.Button("Move selected to SHELF", GUILayout.Height(26)))
        {
            Undo.RecordObject(t, "Move object to shelf");
            t.InspectorMoveToShelf();
            EditorUtility.SetDirty(t);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Grip percent sliders", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        t.thumb = EditorGUILayout.Slider("Thumb", t.thumb, 0f, 100f);
        t.index = EditorGUILayout.Slider("Index", t.index, 0f, 100f);
        t.middle = EditorGUILayout.Slider("Middle", t.middle, 0f, 100f);
        t.ring = EditorGUILayout.Slider("Ring", t.ring, 0f, 100f);
        t.pinky = EditorGUILayout.Slider("Pinky", t.pinky, 0f, 100f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(t, "Change grip sliders");
            t.InspectorSendOnce();
            EditorUtility.SetDirty(t);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open all 0%", GUILayout.Height(26)))
        {
            Undo.RecordObject(t, "Open all fingers");
            t.InspectorOpenAll();
            EditorUtility.SetDirty(t);
        }
        if (GUILayout.Button("Close all 100%", GUILayout.Height(26)))
        {
            Undo.RecordObject(t, "Close all fingers");
            t.InspectorCloseAll();
            EditorUtility.SetDirty(t);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Preset output", EditorStyles.boldLabel);
        EditorGUILayout.TextArea(t.InspectorCurrentPresetLine(), GUILayout.MinHeight(36));

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save current object", GUILayout.Height(28)))
        {
            t.InspectorSaveCurrentPreset();
            EditorUtility.SetDirty(t);
        }
        if (GUILayout.Button("Copy current line", GUILayout.Height(28)))
        {
            t.InspectorCopyCurrentPresetLine();
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Copy ALL saved presets", GUILayout.Height(32)))
        {
            t.InspectorCopyAllPresets();
        }
    }
}

#endif
