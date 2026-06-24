using UnityEngine;
using System.IO;

public class InspectorExporter : MonoBehaviour
{
    public SceneConnectorFinal connector;

    void Start()
    {
        var cfg = connector?.GetConfig();
        if (cfg == null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== SIMULATION CONFIG ===");
        sb.AppendLine($"RopeLength: {cfg.rope.initialLength}");
        sb.AppendLine($"RopeMaterial: {cfg.rope.material}");
        sb.AppendLine($"BucketRadius: {cfg.bucket.innerRadius}");
        sb.AppendLine($"BucketHeight: {cfg.bucket.totalHeight}");
        sb.AppendLine($"BucketMass: {cfg.bucket.emptyMass}");
        sb.AppendLine($"PaintHeight: {cfg.paint.initialHeight}");
        sb.AppendLine($"PaintType: {cfg.paint.paintType}");
        sb.AppendLine($"HolesCount: {cfg.bucket.holes.Count}");
        for (int i = 0; i < cfg.bucket.holes.Count; i++)
            sb.AppendLine($"  Hole[{i}]: r={cfg.bucket.holes[i].radius} angle={cfg.bucket.holes[i].angularPosition}");
        sb.AppendLine($"InitAngle: {cfg.initialAngleDeg}");
        sb.AppendLine($"InitAngVel: {cfg.initialAngularVelocity}");
        sb.AppendLine($"Gravity: {cfg.environment.gravity}");
        sb.AppendLine($"CanvasY: {cfg.canvas.position.y}");
        sb.AppendLine($"PivotPos: {connector?.GetPivotPos()}");

        sb.AppendLine("\n=== SCENE OBJECTS ===");
        sb.AppendLine($"BucketPos: {GameObject.Find("Bucket_Metal")?.transform.position}");
        sb.AppendLine($"PivotPoint: {GameObject.Find("PivotPoint")?.transform.position}");
        sb.AppendLine($"Canvas_Surface: {GameObject.Find("Canvas_Surface")?.transform.position}");

        File.WriteAllText(Application.dataPath + "/inspector_report.txt", sb.ToString());
        Debug.Log("[Exporter] Saved inspector_report.txt");
    }
}