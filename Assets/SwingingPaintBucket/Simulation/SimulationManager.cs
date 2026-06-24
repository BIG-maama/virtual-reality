using UnityEngine;
using System.Collections.Generic;

public class SimulationManager : MonoBehaviour
{
    [Header("المرجع الرئيسي")]
    public SceneConnectorFinal connector;

    [Header("SPH Visual")]
    public Mesh particleMesh;
    public Material particleMaterial;
    private Matrix4x4[] _sphMatrices = new Matrix4x4[200];

    private bool _isRunning = false;
    private bool _isPaused = false;
    private ReportManager _reportManager = new ReportManager();
    private float _reportTimer = 0f;
    private Matrix4x4[] _landedMatrices = new Matrix4x4[8000];

    private void DrawLandedParticles()
    {
        var painter = connector?.GetPainter();
        if (painter == null || particleMesh == null || particleMaterial == null) return;

        var pts = painter.GetLandedPositions();
        if (pts.Count == 0) return;

        const int BATCH = 1023;
        int total = Mathf.Min(pts.Count, 8000);

        // buffer مؤقت لكل batch
        var batchBuf = new Matrix4x4[BATCH];
        int drawn = 0;

        while (drawn < total)
        {
            int batchCount = Mathf.Min(BATCH, total - drawn);
            for (int i = 0; i < batchCount; i++)
                batchBuf[i] = Matrix4x4.TRS(
                    pts[drawn + i],
                    Quaternion.identity,
                    Vector3.one * 0.15f);

            Graphics.DrawMeshInstanced(particleMesh, 0, particleMaterial,
                                       batchBuf, batchCount,
                                       null,
                                       UnityEngine.Rendering.ShadowCastingMode.Off,
                                       false);
            drawn += batchCount;
        }
    }
    void Update()
    {
        _reportTimer += Time.deltaTime;
        if (_reportTimer < 1f) return;
        _reportTimer = 0f;

        var ph = connector?.GetPhysics();
        var sph = connector?.GetSPH();
        var emitter = connector?.GetEmitter();

        Debug.Log($"[RUNTIME] " +
            $"BucketPos={ph?.BucketPosition} | " +
            $"Theta={ph?.Theta * Mathf.Rad2Deg:F1}° | " +
            $"PaintH={ph?.CurrentPaintHeight:F3}m | " +
            $"SPH_Active={sph?.GetActiveCount()} | " +
            $"Particles={emitter?.TotalEmittedCount} | " +
            $"FPS={1f / Time.deltaTime:F0}");
    }
    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;
    public BucketPhysics Physics => connector?.GetPhysics();

    private void Awake()
    {
        if (connector == null)
            connector = GetComponent<SceneConnectorFinal>();
    }

    private void LateUpdate()
    {
        var sph = connector?.GetSPH();
        if (sph == null || particleMesh == null || particleMaterial == null) return;
        int count = Mathf.Min(sph.ParticleCount, 200);
        for (int i = 0; i < count; i++)
            _sphMatrices[i] = Matrix4x4.TRS(sph.GetParticlePos(i), Quaternion.identity, Vector3.one * 0.04f);
        if (count > 0)
            Graphics.DrawMeshInstanced(particleMesh, 0, particleMaterial, _sphMatrices, count);

        DrawLandedParticles();
    }

    public void StartSimulation(SimulationConfig config)
    {
        if (connector == null) return;
        connector.Restart();
        _isRunning = true;
        _isPaused = false;
    }

    public void PauseSimulation()
    {
        _isPaused = true;
        Time.timeScale = 0f;
    }

    public void ResumeSimulation()
    {
        _isPaused = false;
        Time.timeScale = 1f;
    }

    public SimulationReport StopAndGenerateReport()
    {
        _isRunning = false;
        Time.timeScale = 1f;

        var physics = connector?.GetPhysics();
        var painter = connector?.GetPainter();

        var report = new SimulationReport
        {
            Config = new SimulationConfig(),
            TotalSimulationTime = physics?.SimulationTime ?? 0f,
            TotalSwingCount = physics?.SwingCount ?? 0,
            FinalPaintHeightM = physics?.CurrentPaintHeight ?? 0f,
            FinalRopeLengthM = physics?.CurrentRopeLength ?? 0f,
            TotalPaintPaths = painter?.TotalPathCount ?? 0,
            PaintedAreaM2 = painter?.PaintedAreaM2 ?? 0f,
            FinalMassKg = physics?.CurrentMass ?? 0f,
            InitialPeriodSec = physics?.GetPeriod() ?? 0f,
            MaxRopeTensionN = physics?.GetRopeTension() ?? 0f,
        };

        _reportManager.AddReport(report);
        return report;
    }

    public void SaveCanvasImage(string filePath)
    {
        var painter = connector?.GetPainter();
        if (painter == null) return;
        Texture2D tex = painter.GenerateCanvasTexture();
        byte[] data = tex.EncodeToPNG();
        System.IO.File.WriteAllBytes(filePath, data);
    }

    public string GetComparisonReport() => _reportManager.GenerateComparisonReport();
}