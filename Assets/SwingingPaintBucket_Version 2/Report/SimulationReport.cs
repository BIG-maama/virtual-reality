using UnityEngine;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// بيانات تقرير تجربة واحدة - يُحفظ عند نهاية المحاكاة
/// المرجع: المخرجات المتوقعة رقم 7 في مشروع الحقائق الافتراضية
/// </summary>
public class SimulationReport
{
    // ===== Inputs =====
    public SimulationConfig Config { get; set; }

    // ===== Motion Results =====
    public float TotalSimulationTime { get; set; }  // Total motion time (s)
    public int TotalSwingCount { get; set; }  // Number of swings
    public float MaxAngleDeg { get; set; }  // Maximum angle reached by bucket
    public float FinalPaintHeightM { get; set; }  // Final paint height
    public float FinalRopeLengthM { get; set; }  // Final rope length
    public float MaxRopeTensionN { get; set; }  // Maximum rope tension (N)

    // ===== Canvas Results =====
    public int TotalPaintPaths { get; set; }  // Number of paths on canvas
    public float PaintedAreaM2 { get; set; }  // Paint spread area (m²)
    public int TotalParticlesEmitted { get; set; }  // Total paint particles emitted

    // ===== Physical values used =====
    public float InitialMassKg { get; set; }
    public float FinalMassKg { get; set; }
    public float InitialPeriodSec { get; set; }  // Initial pendulum period
    public float EffectiveDampingCoeff { get; set; }  // Effective damping coefficient

    // ===== Experiment date and time =====
    public System.DateTime ExperimentDate { get; set; } = System.DateTime.Now;

    /// <summary>
    /// يولّد تقريراً نصياً كاملاً بالعربية
    /// المرجع: المخرجات المتوقعة - التقرير (المدخلات، زمن الحركة، عدد المسارات، مساحة الانتشار)
    /// </summary>
    public string GenerateTextReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("══════════════════════════════════════════════════════");
        sb.AppendLine("         Swinging Paint Bucket Simulation Report");
        sb.AppendLine("══════════════════════════════════════════════════════");
        sb.AppendLine($"Experiment Name   : {Config.experimentName}");
        sb.AppendLine($"Date and Time     : {ExperimentDate:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("──────── Inputs ────────");
        sb.AppendLine("[Bucket]");
        sb.AppendLine($"  Shape                : {Config.bucket.shape}");
        sb.AppendLine($"  Inner Radius         : {Config.bucket.innerRadius * 100:F1} cm");
        sb.AppendLine($"  Total Height         : {Config.bucket.totalHeight * 100:F1} cm");
        sb.AppendLine($"  Empty Mass           : {Config.bucket.emptyMass * 1000:F0} g");
        sb.AppendLine($"  Number of Holes      : {Config.bucket.holes.Count}");
        foreach (HoleData h in Config.bucket.holes)
        {
            sb.AppendLine($"    - Hole {h.shape}: r={h.radius * 1000:F1}mm, height={h.heightFromBottom * 100:F1}cm, Cd={h.dischargeCoefficient:F2}");
        }

        sb.AppendLine("[Rope]");
        sb.AppendLine($"  Type                 : {Config.rope.material}");
        sb.AppendLine($"  Initial Length       : {Config.rope.initialLength:F2} m");
        sb.AppendLine($"  Radius               : {Config.rope.radius * 1000:F1} mm");
        sb.AppendLine($"  Young's Modulus      : {Config.rope.YoungModulus / 1e9f:F1} GPa");
        sb.AppendLine($"  Thermal Expansion    : {Config.rope.ThermalExpansion * 1e6f:F0} ×10⁻⁶ K⁻¹");

        sb.AppendLine("[Paint]");
        sb.AppendLine($"  Type                 : {Config.paint.paintType}");
        sb.AppendLine($"  Initial Height       : {Config.paint.initialHeight * 100:F1} cm");
        sb.AppendLine($"  Density              : {Config.paint.Density:F0} kg/m³");
        sb.AppendLine($"  Zero Shear Viscosity μ₀ : {Config.paint.Mu0:F3} Pa·s");
        sb.AppendLine($"  Surface Tension σ    : {Config.paint.SigmaRef * 1000:F1} mN/m");
        sb.AppendLine($"  Drying Time τ_dry    : {Config.paint.TauDryRef:F0} s");

        sb.AppendLine("[Canvas]");
        sb.AppendLine($"  Dimensions           : {Config.canvas.width:F2} × {Config.canvas.height:F2} m");
        sb.AppendLine($"  Surface Type         : {Config.canvas.surface}");
        sb.AppendLine($"  Tilt Angle           : {Config.canvas.tiltAngle:F1}°");

        sb.AppendLine("[Environment]");
        sb.AppendLine($"  Gravity g            : {Config.environment.gravity:F4} m/s²");
        sb.AppendLine($"  Temperature          : {Config.environment.temperature:F1} °C");
        sb.AppendLine($"  Humidity             : {Config.environment.humidity:F0}%");
        sb.AppendLine($"  Atmospheric Pressure : {Config.environment.atmosphericPressure:F1} hPa");
        sb.AppendLine($"  Air Density (calc)   : {Config.environment.CalculateHumidAirDensity():F3} kg/m³");
        sb.AppendLine($"  Wind Speed           : {Config.environment.windSpeed:F1} m/s @ {Config.environment.windAngle:F0}°");

        sb.AppendLine("[Initial Motion]");
        sb.AppendLine($"  Initial Angle θ₀     : {Config.initialAngleDeg:F1}°");
        sb.AppendLine($"  Direction Angle φ    : {Config.initialPhiDeg:F1}°");
        sb.AppendLine($"  Angular Velocity θ̇₀ : {Config.initialAngularVelocity:F3} rad/s");

        sb.AppendLine();
        sb.AppendLine("──────── Motion Results ────────");
        sb.AppendLine($"  Total Motion Time    : {TotalSimulationTime:F2} s");
        sb.AppendLine($"  Number of Swings     : {TotalSwingCount}");
        sb.AppendLine($"  Maximum Swing Angle  : {MaxAngleDeg:F1}°");
        sb.AppendLine($"  Initial Mass         : {InitialMassKg * 1000:F1} g");
        sb.AppendLine($"  Final Mass           : {FinalMassKg * 1000:F1} g");
        sb.AppendLine($"  Initial Pendulum Period : {InitialPeriodSec:F3} s");
        sb.AppendLine($"  Maximum Rope Tension : {MaxRopeTensionN:F2} N");
        sb.AppendLine($"  Final Rope Length    : {FinalRopeLengthM:F4} m");

        sb.AppendLine();
        sb.AppendLine("──────── Canvas Results ────────");
        sb.AppendLine($"  Total Particles      : {TotalParticlesEmitted}");
        sb.AppendLine($"  Number of Paths      : {TotalPaintPaths}");
        sb.AppendLine($"  Paint Spread Area    : {PaintedAreaM2 * 10000:F2} cm²");
        sb.AppendLine($"  Final Paint Height   : {FinalPaintHeightM * 100:F2} cm");

        sb.AppendLine();
        sb.AppendLine("══════════════════════════════════════════════════════");
        return sb.ToString();
    }
}

/// <summary>
/// مدير التقارير - يخزن ويقارن التجارب المتعددة
/// المرجع: المخرجات المتوقعة رقم 6 - إمكانية مقارنة أكثر من تجربة
/// </summary>
public class ReportManager
{
    private readonly List<SimulationReport> _reports = new List<SimulationReport>();
    public IReadOnlyList<SimulationReport> Reports => _reports;

    public void AddReport(SimulationReport report) => _reports.Add(report);

    /// <summary>
    /// يولّد تقرير مقارنة بين جميع التجارب المحفوظة
    /// </summary>
    public string GenerateComparisonReport()
    {
        if (_reports.Count == 0) return "No experiments recorded yet.";

        var sb = new StringBuilder();
        sb.AppendLine("══════════════════════════════════════════════════════");
        sb.AppendLine("               Experiment Comparison");
        sb.AppendLine("══════════════════════════════════════════════════════");

        // Table header
        sb.AppendLine($"{"Experiment",-20} | {"Motion Time",-12} | {"Swings",-10} | {"Paths",-10} | {"Area cm²",-12}");
        sb.AppendLine(new string('-', 75));

        foreach (SimulationReport r in _reports)
        {
            sb.AppendLine(
                $"{r.Config.experimentName,-20} | " +
                $"{r.TotalSimulationTime,-12:F2} | " +
                $"{r.TotalSwingCount,-10} | " +
                $"{r.TotalPaintPaths,-10} | " +
                $"{r.PaintedAreaM2 * 10000,-12:F2}"
            );
        }
        sb.AppendLine("══════════════════════════════════════════════════════");
        return sb.ToString();
    }
}