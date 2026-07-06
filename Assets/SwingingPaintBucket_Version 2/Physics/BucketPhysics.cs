using UnityEngine;

/// <summary>
/// BucketPhysics — بندول كروي مع فيزياء فتل حقيقية (Torsional Coupling)
///
/// المبدأ الفيزيائي:
///   • الحبل والدلو نظام واحد مترابط (Torsional Coupling)
///   • الدوران الأفقي φ̇ يولّد عزماً يفتل الحبل (ψ)
///   • الحبل المفتول يحاول العودة بصلابته (k_rope)
///   • الدلو يدور بنفس ψ حول محور الحبل
///   • الرياح: قوة أفقية ثابتة تُضاف كقوة معمّمة على θ, φ (Generalized Force)
///
/// النتيجة: تفتل الدلو ↔ تفتل الحبل — علاقة ثنائية الاتجاه
/// </summary>
public class BucketPhysics
{
    // ══════════════════════════════════════════════════════════════
    // الحالة الأساسية للبندول الكروي
    // ══════════════════════════════════════════════════════════════
    private float _theta;       // زاوية الميل عن الشاقول (rad)
    private float _phi;         // زاوية الدوران الأفقي (rad)
    private float _thetaDot;
    private float _phiDot;

    private readonly BucketData _bucket;
    private readonly RopeData _rope;
    private readonly PaintData _paint;
    private readonly EnvironmentData _env;

    private float _currentPaintHeight;
    private float _currentRopeLength;
    private float _simulationTime;
    private int _swingCount;
    private float _lastPhi;

    // ══════════════════════════════════════════════════════════════
    // ① نظام الفتل (Torsion) — الحبل والدلو معاً
    // ══════════════════════════════════════════════════════════════
    private float _psi;
    private float _psiDot;

    [Tooltip("صلابة الفتل للحبل: كبيرة = حبل صلب يرجع بسرعة")]
    public float ropeStiffness = 0.8f;

    [Tooltip("تخميد الفتل: كبير = يتوقف سريعاً")]
    public float twistDamping = 0.05f;

    [Tooltip("قوة الترابط: φ̇ كبير → فتل قوي")]
    public float twistCoupling = 5.0f;

    [Tooltip("أقصى فتل مسموح (درجة)")]
    public float maxTwistAngleDeg = 120f;

    [Tooltip("فتل ابتدائي (rad/s)")]
    public float initialTwistVelocity = 2.0f;

    public float twistStiffness
    {
        get => ropeStiffness;
        set => ropeStiffness = value;
    }

    public float Psi => _psi;
    public float PsiDeg => _psi * Mathf.Rad2Deg;
    public float PsiDot => _psiDot;

    // ══════════════════════════════════════════════════════════════
    // ②ب نظام الرياح (Wind) — قوة أفقية ثابتة على الدلو
    // ══════════════════════════════════════════════════════════════
    // الفيزياء: الريح قوة F ثابتة الاتجاه بمستوى X-Z (أفقي)
    // بتتحول لعزم معمّم Q_θ و Q_φ عبر مبدأ الشغل الافتراضي (Virtual Work):
    //
    //   Q_θ = F · (∂r/∂θ) = L·cosθ·(Fx·cosφ + Fz·sinφ)
    //   Q_φ = F · (∂r/∂φ) = L·sinθ·(-Fx·sinφ + Fz·cosφ)
    //
    // وبعدين بتنضاف على معادلات الحركة بعد القسمة على m·L² (نفس أسلوب θ̈, φ̈):
    //
    //   θ̈ += Q_θ/(m·L²) = cosθ·(ax·cosφ + az·sinφ)/L
    //   φ̈ += Q_φ/(m·L²·sin²θ) = (-ax·sinφ + az·cosφ)/(L·sinθ)
    //
    // حيث ax = Fx/m, az = Fz/m (تسارع الريح — يعتمد على الكتلة، فكلما
    // كان الدلو أثقل (طلاء أكتر) كان تأثير نفس قوة الريح أقل → واقعي)
    // ══════════════════════════════════════════════════════════════
    [Header("=== الرياح (Wind) ===")]
    [Tooltip("قوة الرياح الأفقية الثابتة بالنيوتن. صفر = بدون رياح")]
    public float windForce = 0f;

    [Tooltip("اتجاه الرياح بالدرجات: 0°=+X (يمين) | 90°=+Z | 180°=-X (يسار) | 270°=-Z")]
    public float windDirectionDeg = 0f;

    [Tooltip("عامل التخميد عند تفعيل الريح: 1.0=تخميد حرج (يستقر بدون تذبذب) | أكبر من 1=أبطأ وأكثر أماناً | أقل من 1=ممكن يتجاوز الهدف قليلاً قبل الاستقرار")]
    public float windSettleFactor = 1.3f;
    // ══════════════════════════════════════════════════════════════
    // ② نظام النايلون (مرونة + زحف)
    // ══════════════════════════════════════════════════════════════
    private float _creepExt;
    private float _elasticExt;
    private float _elasticExtDot;

    private const float K_STATIC = 20f;
    private const float C_VISCOUS = 0.3f;
    private const float CREEP_RATE = 0.00008f;
    private const float CREEP_MAX = 0.06f;

    // ══════════════════════════════════════════════════════════════
    // خصائص عامة
    // ══════════════════════════════════════════════════════════════
    public float Theta => _theta;
    public float Phi => _phi;
    public float ThetaDot => _thetaDot;
    public float PhiDot => _phiDot;
    public float CurrentPaintHeight => _currentPaintHeight;
    public float CurrentRopeLength => _currentRopeLength;
    public float SimulationTime => _simulationTime;
    public int SwingCount => _swingCount;
    public float CurrentMass => _bucket.GetTotalMass(_currentPaintHeight, _paint.Density);
    public float ElasticExtension => _elasticExt;
    public float CreepExtension => _creepExt;

    private bool IsNylon => _rope.material == RopeMaterial.Nylon;

    private float GetDampingCoeff()
    {
        float m = CurrentMass;
        return (m > 0.001f) ? _env.pivotFriction / m : _env.pivotFriction;
    }

    private float GetEffectiveLength()
    {
        float chi = _bucket.GetChiDistance(_currentPaintHeight, _paint.Density);
        if (!IsNylon) return _currentRopeLength + chi;
        return _currentRopeLength + chi + _elasticExt + _creepExt;
    }

    public Vector3 BucketPosition
    {
        get
        {
            float sT = Mathf.Sin(_theta);
            float cT = Mathf.Cos(_theta);
            float sP = Mathf.Sin(_phi);
            float cP = Mathf.Cos(_phi);
            float L = GetEffectiveLength();
            return new Vector3(L * sT * cP, -L * cT, L * sT * sP);
        }
    }

    public Vector3 BucketVelocity
    {
        get
        {
            float sT = Mathf.Sin(_theta);
            float cT = Mathf.Cos(_theta);
            float sP = Mathf.Sin(_phi);
            float cP = Mathf.Cos(_phi);
            float L = GetEffectiveLength();
            float vx = L * (_thetaDot * cT * cP - _phiDot * sT * sP);
            float vy = L * _thetaDot * sT;
            float vz = L * (_thetaDot * cT * sP + _phiDot * sT * cP);

            if (IsNylon && _elasticExtDot != 0f)
            {
                Vector3 ropeDir = new Vector3(sT * cP, -cT, sT * sP);
                return new Vector3(vx, vy, vz) + ropeDir * _elasticExtDot;
            }
            return new Vector3(vx, vy, vz);
        }
    }

    public BucketPhysics(BucketData bucket, RopeData rope,
                         PaintData paint, EnvironmentData env)
    {
        _bucket = bucket;
        _rope = rope;
        _paint = paint;
        _env = env;
    }

    public void Initialize(float initialAngleDeg, float initialPhiDeg,
                       float initialAngVelocity,
                       float initialPhiAngVelocity = 0f)
    {
        _theta = initialAngleDeg * Mathf.Deg2Rad;
        _phi = initialPhiDeg * Mathf.Deg2Rad;
        _thetaDot = initialAngVelocity;
        _phiDot = initialPhiAngVelocity;

        _currentPaintHeight = _paint.initialHeight;
        _currentRopeLength = _rope.GetCurrentLength(_env.temperature);
        _simulationTime = 0f;
        _swingCount = 0;
        _lastPhi = _phi;
        _creepExt = 0f;

        _psi = initialTwistVelocity;
        _psiDot = 0f;

        if (IsNylon)
        {
            float m0 = CurrentMass;
            float cosT = Mathf.Cos(_theta);
            _elasticExt = Mathf.Max(0f, m0 * _env.gravity * cosT / K_STATIC);
            _elasticExtDot = 0f;
        }
        else
        {
            _elasticExt = 0f;
            _elasticExtDot = 0f;
        }

        Debug.Log($"[BucketPhysics] Init | ψ₀={_psi * Mathf.Rad2Deg:F1}° | ψ̇₀=0 (released from rest)");
    }

    public void SetTwistState(float psiRad, float psiDotRad)
    {
        _psi = psiRad;
        _psiDot = psiDotRad;
    }

    private void StepTwist(float dt)
    {
        float m = CurrentMass;
        float r = _bucket.innerRadius;

        float I = 0.5f * m * (r * r + 0.004f);
        I = Mathf.Max(I, 0.001f);

        float k = ropeStiffness * GetRopeTorsionalFactor();
        float c = twistDamping;

        float psiDD = (-k * _psi - c * _psiDot) / I;

        _psiDot += psiDD * dt;
        _psi += _psiDot * dt;

        if (Mathf.Abs(_psi) > maxTwistAngleDeg * Mathf.Deg2Rad * 1.5f)
        {
            _psi = Mathf.Sign(_psi) * maxTwistAngleDeg * Mathf.Deg2Rad * 1.5f;
            _psiDot *= 0.5f;
        }
    }

    private float GetRopeTorsionalFactor()
    {
        switch (_rope.material)
        {
            case RopeMaterial.SteelWire: return 4.0f;
            case RopeMaterial.Nylon: return 1.5f;
            case RopeMaterial.Polyester: return 1.2f;
            default: return 0.8f;
        }
    }

    private void StepCreep(float dt, float tension)
    {
        if (!IsNylon) return;
        if (_creepExt >= CREEP_MAX) return;
        float creepSpeed = CREEP_RATE * Mathf.Max(tension, 0f);
        _creepExt = Mathf.Min(_creepExt + creepSpeed * dt, CREEP_MAX);
    }

    private void StepElastic(float dt, float tension)
    {
        if (!IsNylon) return;
        float m = CurrentMass;
        float netF = tension - K_STATIC * _elasticExt - C_VISCOUS * _elasticExtDot;
        float acc = (m > 0.001f) ? netF / m : 0f;
        _elasticExtDot += acc * dt;
        _elasticExt += _elasticExtDot * dt;
        if (_elasticExt < 0f) { _elasticExt = 0f; _elasticExtDot = Mathf.Max(0f, _elasticExtDot); }
        _elasticExt = Mathf.Min(_elasticExt, _currentRopeLength * 0.35f);
    }

    // ══════════════════════════════════════════════════════════════
    // RK4 للبندول الكروي (+ الريح كقوة معمّمة)
    // ══════════════════════════════════════════════════════════════
    private struct State { public float theta, phi, thetaDot, phiDot; }

    private State Derivatives(State s, float gammaTheta, float gammaPhi, float g, float L, float windAx, float windAz)
    {
        float sT = Mathf.Sin(s.theta);
        float cT = Mathf.Cos(s.theta);
        float sT_safe = Mathf.Abs(sT) < 0.002f ? Mathf.Sign(sT) * 0.002f : sT;
        float sP = Mathf.Sin(s.phi);
        float cP = Mathf.Cos(s.phi);

        float windThetaDD = cT * (windAx * cP + windAz * sP) / L;
        float windPhiDD = (-windAx * sP + windAz * cP) / (L * sT_safe);

        float thetaDD = s.phiDot * s.phiDot * sT * cT
                      - (g / L) * sT
                      - gammaTheta * s.thetaDot
                      + windThetaDD;

        float phiDD = -2f * s.thetaDot * s.phiDot * (cT / sT_safe)
                      - gammaPhi * s.phiDot
                      + windPhiDD;

        return new State
        {
            theta = s.thetaDot,
            phi = s.phiDot,
            thetaDot = thetaDD,
            phiDot = phiDD
        };
    }

    private State RK4Step(State s, float dt, float gammaTheta, float gammaPhi, float g, float L, float windAx, float windAz)
    {
        var k1 = Derivatives(s, gammaTheta, gammaPhi, g, L, windAx, windAz);
        var s2 = new State
        {
            theta = s.theta + .5f * dt * k1.theta,
            phi = s.phi + .5f * dt * k1.phi,
            thetaDot = s.thetaDot + .5f * dt * k1.thetaDot,
            phiDot = s.phiDot + .5f * dt * k1.phiDot
        };
        var k2 = Derivatives(s2, gammaTheta, gammaPhi, g, L, windAx, windAz);
        var s3 = new State
        {
            theta = s.theta + .5f * dt * k2.theta,
            phi = s.phi + .5f * dt * k2.phi,
            thetaDot = s.thetaDot + .5f * dt * k2.thetaDot,
            phiDot = s.phiDot + .5f * dt * k2.phiDot
        };
        var k3 = Derivatives(s3, gammaTheta, gammaPhi, g, L, windAx, windAz);
        var s4 = new State
        {
            theta = s.theta + dt * k3.theta,
            phi = s.phi + dt * k3.phi,
            thetaDot = s.thetaDot + dt * k3.thetaDot,
            phiDot = s.phiDot + dt * k3.phiDot
        };
        var k4 = Derivatives(s4, gammaTheta, gammaPhi, g, L, windAx, windAz);

        return new State
        {
            theta = s.theta + dt * (k1.theta + 2 * k2.theta + 2 * k3.theta + k4.theta) / 6f,
            phi = s.phi + dt * (k1.phi + 2 * k2.phi + 2 * k3.phi + k4.phi) / 6f,
            thetaDot = s.thetaDot + dt * (k1.thetaDot + 2 * k2.thetaDot + 2 * k3.thetaDot + k4.thetaDot) / 6f,
            phiDot = s.phiDot + dt * (k1.phiDot + 2 * k2.phiDot + 2 * k3.phiDot + k4.phiDot) / 6f
        };
    }

    // ══════════════════════════════════════════════════════════════
    // Step الرئيسي
    // ══════════════════════════════════════════════════════════════
    public void Step(float dt)
    {
        _currentPaintHeight = _paint.GetCurrentHeight(_simulationTime,
            _bucket.GetEmptyingTime(_paint.initialHeight, _env.gravity));
        _currentRopeLength = _rope.GetCurrentLength(_env.temperature);

        float m = CurrentMass;
        float cosT = Mathf.Cos(_theta);
        float sT = Mathf.Sin(_theta);
        float L = GetEffectiveLength();
        float omega2 = _thetaDot * _thetaDot + sT * sT * _phiDot * _phiDot;
        float tension = Mathf.Max(0f, m * _env.gravity * cosT + m * L * omega2);

        StepCreep(dt, tension);
        StepElastic(dt, tension);

        float windRad = windDirectionDeg * Mathf.Deg2Rad;
        float windAx = (m > 0.001f) ? (windForce * Mathf.Cos(windRad)) / m : 0f;
        float windAz = (m > 0.001f) ? (windForce * Mathf.Sin(windRad)) / m : 0f;

        // ══════════════════════════════════════════════════════
        // ✅ معالجة تفرّد القطب (Pole Singularity Handling)
        // عند θ قريبة من الصفر، φ غير مُعرَّف فيزيائياً وأي محاولة "تكامل"
        // له رقمياً بهالمنطقة غير مستقرة بطبيعتها (stiff ODE) بغض النظر عن
        // دقة المعادلة. الحل المعياري: تثبيت φ مباشرة على اتجاه الريح
        // (الاتجاه الفيزيائي الوحيد المنطقي للخروج من القطب)، وتصفير φ̇،
        // بدل محاولة حل معادلة غير مستقرة عددياً. بمجرد خروج θ من نطاق
        // القطب، المعادلات الطبيعية تعمل بشكل مستقر تماماً بدون أي تدخل.
        // ══════════════════════════════════════════════════════
        // عتبة ضيقة جداً - فقط قريب فعلي من القطب حيث L·sinθ مهمل (~ملم فعلياً)
        const float POLE_SIN_THRESHOLD = 0.002f; // ≈ 0.6° فقط حول القطب الحقيقي

        if (windForce > 0.001f && sT < POLE_SIN_THRESHOLD)
        {
            // مزج ناعم بدل الفرض الفوري - أقوى كلما قربنا أكتر من القطب
            float blend = Mathf.Clamp01(1f - sT / POLE_SIN_THRESHOLD);
            float phiDeg = Mathf.LerpAngle(_phi * Mathf.Rad2Deg, windDirectionDeg, blend);
            _phi = phiDeg * Mathf.Deg2Rad;
            _phiDot = Mathf.Lerp(_phiDot, 0f, blend);
        }

        float baseGamma = GetDampingCoeff();
        float gammaTheta = baseGamma;
        float gammaPhi = baseGamma;

        // تم حذف الفرض الصناعي لـ windSettleFactor — التخميد الآن
        // يعتمد فقط على الاحتكاك الفيزيائي الحقيقي (pivotFriction)

        var current = new State
        {
            theta = _theta,
            phi = _phi,
            thetaDot = _thetaDot,
            phiDot = _phiDot
        };
        var next = RK4Step(current, dt, gammaTheta, gammaPhi, _env.gravity, L, windAx, windAz);

        next.theta = Mathf.Clamp(next.theta, 0.001f, Mathf.PI - 0.001f);
        next.thetaDot = Mathf.Clamp(next.thetaDot, -20f, 20f);
        next.phiDot = Mathf.Clamp(next.phiDot, -20f, 20f);

        _theta = next.theta;
        _phi = next.phi;
        _thetaDot = next.thetaDot;
        _phiDot = next.phiDot;

        if (Mathf.Floor(_lastPhi / Mathf.PI) != Mathf.Floor(_phi / Mathf.PI))
            _swingCount++;
        _lastPhi = _phi;

        StepTwist(dt);

        _simulationTime += dt;
    }

    // ══════════════════════════════════════════════════════════════
    // حسابات الطاقة والإحصائيات
    // ══════════════════════════════════════════════════════════════
    public float GetKineticEnergy()
    {
        float m = CurrentMass;
        float L = GetEffectiveLength();
        float sT = Mathf.Sin(_theta);
        float ke = 0.5f * m * L * L *
                   (_thetaDot * _thetaDot + sT * sT * _phiDot * _phiDot);
        if (IsNylon) ke += 0.5f * m * _elasticExtDot * _elasticExtDot;
        float r = _bucket.innerRadius;
        float I = 0.5f * m * (r * r + 0.004f);
        ke += 0.5f * I * _psiDot * _psiDot;
        return ke;
    }

    public float GetPotentialEnergy()
    {
        float pe = CurrentMass * _env.gravity *
                   GetEffectiveLength() * (1f - Mathf.Cos(_theta));
        if (IsNylon) pe += 0.5f * K_STATIC * _elasticExt * _elasticExt;
        pe += 0.5f * ropeStiffness * GetRopeTorsionalFactor() * _psi * _psi;
        return pe;
    }

    public float GetTotalEnergy() => GetKineticEnergy() + GetPotentialEnergy();

    public float GetPeriod() =>
        2f * Mathf.PI * Mathf.Sqrt(GetEffectiveLength() / _env.gravity);

    public float GetRopeTension()
    {
        float m = CurrentMass;
        float L = GetEffectiveLength();
        float v = BucketVelocity.magnitude;
        return m * _env.gravity * Mathf.Cos(_theta) + m * v * v / L;
    }

    public void SetThetaState(float thetaRad, float thetaDot)
    {
        _theta = Mathf.Clamp(thetaRad, 0.001f, Mathf.PI - 0.001f);
        _thetaDot = thetaDot;
    }

    public void SetPhiState(float phiRad, float phiDot)
    {
        _phi = phiRad;
        _phiDot = phiDot;
    }

    public float CalculateDampingCoefficient() => GetDampingCoeff();
}