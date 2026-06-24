using UnityEngine;
using System.Text;
using System.IO;

/// <summary>
/// Exports all Scene Objects (Position, Rotation, Scale) to:
/// 1. Unity Console
/// 2. Desktop: SceneObjects.txt
/// 3. Assets folder: SceneObjects.txt
///
/// HOW TO USE:
/// - Attach this script to any empty GameObject
/// - Press Play
/// - Check Desktop for SceneObjects.txt
/// </summary>
public class SceneExporter : MonoBehaviour
{
    private void Start()
    {
        ExportScene();
    }

    public void ExportScene()
    {
        string report = BuildReport();

        // 1. Console
        Debug.Log("===== SCENE EXPORT =====\n" + report);

        // 2. Desktop
        try
        {
            string desktop = System.Environment.GetFolderPath(
                             System.Environment.SpecialFolder.Desktop);
            string file1 = Path.Combine(desktop, "SceneObjects.txt");
            File.WriteAllText(file1, report, Encoding.UTF8);
            Debug.Log("SAVED TO DESKTOP: " + file1);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Desktop save failed: " + e.Message);
        }

        // 3. Assets folder
        try
        {
            string file2 = Application.dataPath + "/SceneObjects.txt";
            File.WriteAllText(file2, report, Encoding.UTF8);
            Debug.Log("SAVED TO ASSETS: " + file2);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Assets save failed: " + e.Message);
        }
    }

    private string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("        SCENE OBJECTS REPORT");
        sb.AppendLine("  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("========================================");
        sb.AppendLine();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        int total = 0;

        foreach (GameObject root in roots)
            AppendTransform(sb, root.transform, 0, ref total);

        sb.AppendLine("========================================");
        sb.AppendLine("TOTAL OBJECTS: " + total);
        sb.AppendLine("========================================");
        sb.AppendLine("\n=== PHYSICS RUNTIME ===");
        var scf = FindFirstObjectByType<SceneConnectorFinal>();
        if (scf != null)
        {
            var ph = scf.GetPhysics();
            var sph = scf.GetSPH();
            var em = scf.GetEmitter();
            sb.AppendLine($"RopeLength: {ph?.CurrentRopeLength}");
            sb.AppendLine($"BucketPos: {ph?.BucketPosition}");
            sb.AppendLine($"PaintHeight: {ph?.CurrentPaintHeight}");
            sb.AppendLine($"SPH_Active: {sph?.GetActiveCount()}");
            sb.AppendLine($"Emitted: {em?.TotalEmittedCount}");
            sb.AppendLine($"CanvasY: {scf.canvasSurface?.position.y}");
            sb.AppendLine($"PivotY: {scf.pivotPoint?.position.y}");
        }
        return sb.ToString();
    }

    private void AppendTransform(StringBuilder sb, Transform t, int depth, ref int count)
    {
        count++;
        string pad = new string(' ', depth * 3);
        string prefix = depth == 0 ? "" : "|-- ";

        sb.AppendLine(pad + prefix + "[" + t.gameObject.name + "]");
        sb.AppendLine(pad + "    Active   : " + t.gameObject.activeSelf);

        Vector3 wp = t.position;
        sb.AppendLine(pad + "    Pos(World): X=" + wp.x.ToString("F4")
                           + "  Y=" + wp.y.ToString("F4")
                           + "  Z=" + wp.z.ToString("F4"));

        Vector3 lp = t.localPosition;
        sb.AppendLine(pad + "    Pos(Local): X=" + lp.x.ToString("F4")
                           + "  Y=" + lp.y.ToString("F4")
                           + "  Z=" + lp.z.ToString("F4"));

        Vector3 rot = t.eulerAngles;
        sb.AppendLine(pad + "    Rotation  : X=" + rot.x.ToString("F2")
                           + "  Y=" + rot.y.ToString("F2")
                           + "  Z=" + rot.z.ToString("F2"));

        Vector3 sc = t.localScale;
        sb.AppendLine(pad + "    Scale     : X=" + sc.x.ToString("F4")
                           + "  Y=" + sc.y.ToString("F4")
                           + "  Z=" + sc.z.ToString("F4"));

        var comps = t.GetComponents<Component>();
        var names = new System.Collections.Generic.List<string>();
        foreach (var c in comps)
        {
            if (c == null) continue;
            string n = c.GetType().Name;
            if (n != "Transform") names.Add(n);
        }
        if (names.Count > 0)
            sb.AppendLine(pad + "    Components: " + string.Join(", ", names));

        sb.AppendLine();

        foreach (Transform child in t)
            AppendTransform(sb, child, depth + 1, ref count);
    }
}