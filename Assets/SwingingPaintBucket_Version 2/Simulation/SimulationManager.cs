using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// مدير المحاكاة - يربط SceneConnectorFinal مع واجهة المستخدم
/// ضعه على SimulationController مع SceneConnectorFinal
/// </summary>
public class SimulationManager : MonoBehaviour
{
    [Header("المرجع الرئيسي")]
    public SceneConnectorFinal connector;

    private bool _isRunning  = false;
    private bool _isPaused   = false;
    private ReportManager _reportManager = new ReportManager();

    public bool IsRunning  => _isRunning;
    public bool IsPaused   => _isPaused;

    // خاصية للوصول للفيزياء من BucketVisualController و SimulationUI
    public BucketPhysics Physics => connector?.GetPhysics();

    private void Awake()
    {
        if (connector == null)
            connector = GetComponent<SceneConnectorFinal>();
    }

    /// <summary>بدء محاكاة جديدة</summary>
    public void StartSimulation(SimulationConfig config)
    {
        if (connector == null) return;
        connector.Restart();
        _isRunning = true;
        _isPaused  = false;
        Debug.Log("[SimManager] Simulation started");
    }

    /// <summary>إيقاف مؤقت</summary>
    public void PauseSimulation()
    {
        _isPaused = true;
        Time.timeScale = 0f;
    }

    /// <summary>استئناف</summary>
    public void ResumeSimulation()
    {
        _isPaused = false;
        Time.timeScale = 1f;
    }

    /// <summary>إنهاء وتوليد تقرير</summary>
    public SimulationReport StopAndGenerateReport()
    {
        _isRunning = false;
        Time.timeScale = 1f;

        var physics = connector?.GetPhysics();
        var painter = connector?.GetPainter();

        var report = new SimulationReport
        {
            Config              = new SimulationConfig(),
            TotalSimulationTime = physics?.SimulationTime ?? 0f,
            TotalSwingCount     = physics?.SwingCount ?? 0,
            FinalPaintHeightM   = physics?.CurrentPaintHeight ?? 0f,
            FinalRopeLengthM    = physics?.CurrentRopeLength ?? 0f,
            TotalPaintPaths     = painter?.TotalPathCount ?? 0,
            PaintedAreaM2       = painter?.PaintedAreaM2 ?? 0f,
            FinalMassKg         = physics?.CurrentMass ?? 0f,
            InitialPeriodSec    = physics?.GetPeriod() ?? 0f,
            MaxRopeTensionN     = physics?.GetRopeTension() ?? 0f,
        };

        _reportManager.AddReport(report);
        Debug.Log("[SimManager] Report generated");
        return report;
    }

    /// <summary>حفظ صورة اللوحة</summary>
    public void SaveCanvasImage(string filePath)
    {
        var painter = connector?.GetPainter();
        if (painter == null) return;
        Texture2D tex  = painter.GenerateCanvasTexture();
        byte[]    data = tex.EncodeToPNG();
        System.IO.File.WriteAllBytes(filePath, data);
        Debug.Log("[SimManager] Canvas saved: " + filePath);
    }

    /// <summary>تقرير المقارنة</summary>
    public string GetComparisonReport() => _reportManager.GenerateComparisonReport();
}
