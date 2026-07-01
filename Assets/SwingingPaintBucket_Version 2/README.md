# دليل إعداد مشروع Swinging Paint Bucket في Unity

## هيكل الملفات
```
SwingingPaintBucket/
├── Environment/
│   └── EnvironmentData.cs       ← بيانات البيئة (جاذبية، هواء، رطوبة، رياح)
├── Rope/
│   └── RopeData.cs              ← بيانات الحبل + القوانين الفيزيائية
├── Bucket/
│   ├── BucketData.cs            ← بيانات الدلو + حساب الكتلة المتغيرة
│   └── HoleData.cs              ← بيانات ثقب واحد (تورتشيلي، هاغن-بواظيل)
├── Paint/
│   ├── PaintData.cs             ← بيانات الطلاء (Cross Model، أندريد، ويبر)
│   ├── PaintParticle.cs         ← جزيء طلاء واحد (حركة مقذوفة + سحب)
│   └── PaintEmitter.cs          ← مُولّد الجزيئات من الثقوب
├── Canvas/
│   └── CanvasPainter.cs         ← رسم اللوحة (VOF + مزج ألوان + جفاف)
├── Physics/
│   └── BucketPhysics.cs         ← محرك لاغرانج الرئيسي (معادلتا θ̈ و φ̈)
├── Simulation/
│   ├── SimulationConfig.cs      ← تكوين كامل لتجربة واحدة
│   └── SimulationManager.cs     ← المكوّن الرئيسي في Unity
├── UI/
│   └── SimulationUI.cs          ← واجهة المستخدم الكاملة
└── Report/
    └── SimulationReport.cs      ← توليد التقارير ومقارنة التجارب
```

---

## خطوات الإعداد في Unity

### 1. نسخ الملفات
انسخ جميع ملفات .cs إلى مجلد `Assets/Scripts/` في مشروع Unity.

### 2. إعداد المشهد
لديك الدلو والحبل ومركز التعليق واللوحة مرسومة مسبقاً.
افعل الآتي:

#### أ. إنشاء GameObject رئيسي
- أضف GameObject فارغ باسم `SimulationController`
- أضف عليه مكوّن `SimulationManager`

#### ب. ربط الـ Transforms
في Inspector الـ SimulationManager:
- `Bucket Transform` ← اسحب GameObject الدلو
- `Rope Transform`   ← اسحب GameObject الحبل (يحتاج LineRenderer)
- `Canvas Renderer`  ← اسحب MeshRenderer اللوحة

#### ج. إعداد الحبل (LineRenderer)
- أضف مكوّن LineRenderer على GameObject الحبل
- ضبط `Position Count = 2`

#### د. إعداد الـ Canvas Display (اختياري)
- أنشئ UI Canvas
- أضف RawImage لعرض اللوحة الحية

### 3. إعداد SimulationConfig
في Inspector SimulationManager:
- نقطة التعليق: `rope.pivotPoint` = (0, 2, 0) مثلاً
- أبعاد اللوحة: `canvas.width = 1.5`, `canvas.height = 1.5`
- موضع اللوحة: `canvas.position = (0, 0, 0)`

### 4. تشغيل المحاكاة
- اضغط Play → تبدأ المحاكاة تلقائياً
- أو اربط SimulationUI وتحكم من الواجهة

---

## القوانين الفيزيائية المطبّقة

| القانون | الملف | الدالة |
|---------|-------|--------|
| معادلات لاغرانج θ̈, φ̈ | BucketPhysics.cs | Step() |
| توريتشيلي v=√(2gh) | HoleData.cs | GetExitVelocity() |
| هاغن-بواظيل Q=πr⁴ΔP/8μL | PaintData.cs | GetDynamicViscosity() |
| Cross Model لزوجة | PaintData.cs | GetDynamicViscosity() |
| أندريد μ(T)=A·e^(B/T) | PaintData.cs | GetViscosityAtTemperature() |
| CIPM-2007 كثافة هواء | EnvironmentData.cs | CalculateHumidAirDensity() |
| كتلة متغيرة m(t) | BucketData.cs | GetTotalMass() |
| طول متغير L(t) | BucketPhysics.cs | Step() |
| مركز ثقل χ(t) | BucketData.cs | GetChiDistance() |
| VOF رسم الطلاء | CanvasPainter.cs | UpdateVOFGrid() |
| مزج الألوان C=F_dry·C_old+... | PaintData.cs | BlendColors() |
| جفاف F_dry(t)=1-e^(-t/τ) | PaintData.cs | GetDrynessFactor() |
| ويبر We=ρv²d/σ | PaintData.cs | GetWeberNumber() |
| انتشار البقعة r=a·t^b | PaintData.cs | GetSpreadRadius() |
| قوة السحب Fd=0.5CdρAv² | BucketPhysics.cs | CalculateDampingCoefficient() |
| قوة الرياح F_wind=0.5ρCdAV² | EnvironmentData.cs | CalculateWindForce() |
| التمدد الحراري L(T)=L₀(1+αΔT) | RopeData.cs | GetCurrentLength() |
| توتر الحبل T=mg·cos(θ)+mv²/L | BucketPhysics.cs | GetRopeTension() |
| قوة طفو هوائي F_b=ρ_air·V·g | BucketData.cs | (في EnvironmentData) |

---

## ملاحظات مهمة
- **لا Rigidbody**: جميع الفيزياء محسوبة يدوياً ← لا تضف Rigidbody للدلو
- **لا Collider**: الاصطدام محسوب رياضياً ← لا تضف Collider
- **FixedUpdate**: المحاكاة تعمل في FixedUpdate لضمان استقرار dt
- **dt صغير**: إذا لاحظت اهتزازاً، قلّل Fixed Timestep في Project Settings
