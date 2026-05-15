using UnityEngine;

/// <summary>
/// BucketPhysics — النسخة المصححة
///
/// الإصلاحات:
///  1. معامل التخميد C مُصحَّح: القيمة القديمة كانت تُوقف البندول في ثوانٍ
///     C_new = pivotFriction · m  فقط (0.01 × m)
///     هذا يعطي ~60-90 تأرجحة قبل التوقف (واقعي لبندول حقيقي)
///  2. إضافة Runge-Kutta 4 بدلاً من Euler لاستقرار أفضل
///  3. dampingTerm محدود بـ maxDamping لمنع الفوضى العددية
/// </summary>
public class BucketPhysics
{
    // ===== State Variables =====
    private float _theta;
    private float _phi;
    private float _thetaDot;
    private float _phiDot;

    // ===== References =====
    private readonly BucketData _bucket;
    private readonly RopeData _rope;
    private readonly PaintData _paint;
    private readonly EnvironmentData _env;

    // ===== Dynamics =====
    private float _currentPaintHeight;
    private float _currentRopeLength;
    private float _emptyingTime;
    private float _simulationTime;
    private int _swingCount;
    private bool _wasPositive;

    // ===== Read-only Properties =====
    public float Theta => _theta;
    public float Phi => _phi;
    public float ThetaDot => _thetaDot;
    public float PhiDot => _phiDot;
    public float CurrentPaintHeight => _currentPaintHeight;
    public float CurrentRopeLength => _currentRopeLength;
    public float SimulationTime => _simulationTime;
    public int SwingCount => _swingCount;
    public float CurrentMass => _bucket.GetTotalMass(_currentPaintHeight, _paint.Density);
    public bool IsMoving => Mathf.Abs(_thetaDot) > 0.0005f || Mathf.Abs(_phiDot) > 0.0005f;

    // =====================================================================
    // BucketPosition: Cartesian coordinates relative to pivot point
    //   x = L·sin(θ)·cos(φ)
    //   y = -L·cos(θ)
    //   z = L·sin(θ)·sin(φ)
    // =====================================================================
    public Vector3 BucketPosition
    {
        get
        {
            float sT = Mathf.Sin(_theta), cT = Mathf.Cos(_theta);
            float sP = Mathf.Sin(_phi), cP = Mathf.Cos(_phi);
            float L = _currentRopeLength;
            return new Vector3(L * sT * cP, -L * cT, L * sT * sP);
        }
    }

    // =====================================================================
    // BucketVelocity: derivative of position
    //   vx = L·θ̇·cos(θ)·cos(φ) − L·φ̇·sin(θ)·sin(φ)
    //   vy = L·θ̇·sin(θ)
    //   vz = L·θ̇·cos(θ)·sin(φ) + L·φ̇·sin(θ)·cos(φ)
    // =====================================================================
    public Vector3 BucketVelocity
    {
        get
        {
            float sT = Mathf.Sin(_theta), cT = Mathf.Cos(_theta);
            float sP = Mathf.Sin(_phi), cP = Mathf.Cos(_phi);
            float L = _currentRopeLength;
            return new Vector3(
                L * (_thetaDot * cT * cP - _phiDot * sT * sP),
                L * _thetaDot * sT,
                L * (_thetaDot * cT * sP + _phiDot * sT * cP)
            );
        }
    }

    // =====================================================================
    // Constructor
    // =====================================================================
    public BucketPhysics(BucketData bucket, RopeData rope,
                         PaintData paint, EnvironmentData env)
    {
        _bucket = bucket; _rope = rope; _paint = paint; _env = env;
    }

    // =====================================================================
    // Initialize
    // =====================================================================
    public void Initialize(float initialAngleDeg, float initialPhiDeg,
                           float initialAngVelocity)
    {
        _theta = initialAngleDeg * Mathf.Deg2Rad;
        _phi = initialPhiDeg * Mathf.Deg2Rad;
        _thetaDot = initialAngVelocity;
        _phiDot = 0f;

        _currentPaintHeight = _paint.initialHeight;
        _currentRopeLength = _rope.GetCurrentLength(_env.temperature);
        _simulationTime = 0f;
        _swingCount = 0;
        _wasPositive = _theta > 0f;

        _emptyingTime = _bucket.GetEmptyingTime(_paint.initialHeight, _env.gravity);

        Debug.Log($"[BucketPhysics] Init: θ={initialAngleDeg}°  L={_currentRopeLength:F3}m  " +
                  $"T_empty={_emptyingTime:F1}s  m={CurrentMass:F3}kg");
    }

    // =====================================================================
    // Step — RK4 integration for Lagrange equations
    // =====================================================================
    public void Step(float dt)
    {
        // Update h(t) and L(t)
        float prevH = _currentPaintHeight;
        _currentPaintHeight = _paint.GetCurrentHeight(_simulationTime, _emptyingTime);
        float prevL = _currentRopeLength;
        _currentRopeLength = _rope.GetCurrentLength(_env.temperature);

        float m = CurrentMass;
        float L = _currentRopeLength;
        float g = _env.gravity;

        // ṁ and L̇
        float mDot = Mathf.Clamp((m - _bucket.GetTotalMass(prevH, _paint.Density)) / dt,
                                  -10f, 0f);   // always negative (paint flowing out)
        float lDot = (L - prevL) / dt;

        // Corrected damping coefficient
        // C_eff = pivotFriction · m   (≈ 0.01·m → pendulum oscillates 60-90 times)
        // No ropeDamping added because it was stopping the pendulum too quickly
        float C = _env.pivotFriction * m;  // ≈ 0.01 × m

        // Common factor for total damping (capped to prevent numerical chaos)
        float rawDamping = (mDot / m) + (2f * lDot / (L + 1e-6f)) + (C / (m + 1e-6f));
        float dampingTerm = Mathf.Clamp(rawDamping, -0.5f, 0.5f);

        // Wind force
        Vector3 windF = _env.CalculateWindForce(_bucket.GetCrossSectionArea());
        float qWindT = CalcWindTorqueTheta(windF, L, _theta, _phi);
        float qWindP = CalcWindTorquePhi(windF, L, _theta, _phi);

        // ===== RK4 =====
        // State: [θ, φ, θ̇, φ̇]
        float th = _theta, ph = _phi, thD = _thetaDot, phD = _phiDot;

        // k1
        float[] k1 = Derivatives(th, ph, thD, phD, g, L, dampingTerm, m, qWindT, qWindP);
        // k2
        float[] k2 = Derivatives(th + 0.5f * dt * k1[2], ph + 0.5f * dt * k1[3],
                                  thD + 0.5f * dt * k1[0], phD + 0.5f * dt * k1[1],
                                  g, L, dampingTerm, m, qWindT, qWindP);
        // k3
        float[] k3 = Derivatives(th + 0.5f * dt * k2[2], ph + 0.5f * dt * k2[3],
                                  thD + 0.5f * dt * k2[0], phD + 0.5f * dt * k2[1],
                                  g, L, dampingTerm, m, qWindT, qWindP);
        // k4
        float[] k4 = Derivatives(th + dt * k3[2], ph + dt * k3[3],
                                  thD + dt * k3[0], phD + dt * k3[1],
                                  g, L, dampingTerm, m, qWindT, qWindP);

        _thetaDot += (dt / 6f) * (k1[0] + 2f * k2[0] + 2f * k3[0] + k4[0]);
        _phiDot += (dt / 6f) * (k1[1] + 2f * k2[1] + 2f * k3[1] + k4[1]);
        _theta += (dt / 6f) * (k1[2] + 2f * k2[2] + 2f * k3[2] + k4[2]);
        _phi += (dt / 6f) * (k1[3] + 2f * k2[3] + 2f * k3[3] + k4[3]);

        // Clamp θ to prevent numerical chaos
        _theta = Mathf.Clamp(_theta, -Mathf.PI * 0.90f, Mathf.PI * 0.90f);

        // Swing counter
        bool isPos = _theta > 0f;
        if (isPos != _wasPositive) { _swingCount++; _wasPositive = isPos; }

        _simulationTime += dt;
    }

    // =====================================================================
    // Derivatives: f([θ, φ, θ̇, φ̇]) → [θ̈, φ̈, θ̇, φ̇]
    // =====================================================================
    private float[] Derivatives(float th, float ph, float thD, float phD,
                                 float g, float L, float damp,
                                 float m, float qT, float qP)
    {
        float sT = Mathf.Sin(th);
        float cT = Mathf.Cos(th);
        float cotT = (Mathf.Abs(sT) > 0.002f) ? (cT / sT) : 0f;
        float L2 = L * L + 1e-6f;

        // θ̈ = φ̇²·sin(θ)·cos(θ) − (g/L)·sin(θ) − damp·θ̇ + Q_θ/(m·L²)
        float thDD = phD * phD * sT * cT
                   - (g / L) * sT
                   - damp * thD
                   + qT / (m * L2);

        // φ̈ = −2·θ̇·φ̇·cot(θ) − damp·φ̇ + Q_φ/(m·L²·sin²θ)
        float sin2T = sT * sT + 1e-6f;
        float phDD = -2f * thD * phD * cotT
                    - damp * phD
                    + qP / (m * L2 * sin2T);

        // Return [θ̈, φ̈, θ̇, φ̇]
        return new float[] { thDD, phDD, thD, phD };
    }

    // =====================================================================
    // Wind torques
    // =====================================================================
    private float CalcWindTorqueTheta(Vector3 wF, float L, float th, float ph)
    {
        float cT = Mathf.Cos(th), cP = Mathf.Cos(ph), sP = Mathf.Sin(ph);
        return L * (wF.x * cT * cP + wF.z * cT * sP);
    }

    private float CalcWindTorquePhi(Vector3 wF, float L, float th, float ph)
    {
        float sT = Mathf.Sin(th), cP = Mathf.Cos(ph), sP = Mathf.Sin(ph);
        return L * sT * (-wF.x * sP + wF.z * cP);
    }

    // =====================================================================
    // Derived calculations
    // =====================================================================
    public float GetKineticEnergy()
    {
        float m = CurrentMass, L = _currentRopeLength;
        float sT = Mathf.Sin(_theta);
        return 0.5f * m * L * L * (_thetaDot * _thetaDot + sT * sT * _phiDot * _phiDot);
    }

    public float GetPotentialEnergy()
        => -CurrentMass * _env.gravity * _currentRopeLength * Mathf.Cos(_theta);

    public float GetPeriod()
    {
        float m = CurrentMass, L = _currentRopeLength;
        float mR = _rope.GetMass(L);
        float I = m * L * L + (1f / 3f) * mR * L * L;
        float mgd = (m + mR) * _env.gravity * L;
        if (mgd <= 0f) return 2f;
        return 2f * Mathf.PI * Mathf.Sqrt(I / mgd);
    }

    public float GetRopeTension()
    {
        float m = CurrentMass, L = _currentRopeLength;
        float v = BucketVelocity.magnitude;
        return m * _env.gravity * Mathf.Cos(_theta) + m * v * v / (L + 1e-6f);
    }
}