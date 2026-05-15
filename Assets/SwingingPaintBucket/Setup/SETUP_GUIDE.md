# دليل الربط الكامل خطوة بخطوة
# Swinging Paint Bucket - Unity 6

==========================================================
## الخطوة 1 — نسخ السكريبتات إلى Unity
==========================================================

انسخ هذه الملفات إلى المجلدات الموجودة عندك في Assets/SwingingPaintBucket:

  Setup/SceneConnector.cs        → Assets/SwingingPaintBucket/Simulation/
  Setup/BucketVisualController.cs → Assets/SwingingPaintBucket/Simulation/
  UI/MainUI.cs                   → Assets/SwingingPaintBucket/UI/

(الملفات القديمة اتركها كما هي)

==========================================================
## الخطوة 2 — إنشاء SimulationController
==========================================================

1. في Hierarchy → اضغط كليك يمين → Create Empty
2. سمّه: "SimulationController"
3. اضغط عليه → في Inspector اضغط "Add Component"
4. أضف هذه المكونات الثلاث:
   ✅ SimulationManager   (من ملفاتنا)
   ✅ SceneConnector      (الجديد)

==========================================================
## الخطوة 3 — ربط SceneConnector بالكائنات
==========================================================

اضغط على SimulationController في Hierarchy
في Inspector ستجد SceneConnector مع هذه الحقول:

اسحب من Hierarchy إلى كل حقل:

  [Bucket_Metal]           ← اسحب Bucket_Metal
  [Bucket_Wood]            ← اسحب Bucket_Wood
  [Rope_Cotton]            ← اسحب Rope_Cotton
  [Rope_Nylon]             ← اسحب Rope_Nylon
  [Rope_Steel]             ← اسحب Rope_Steel
  [Metal Bucket AttachPoint] ← افتح Bucket_Metal → Handle_Top → اسحب RopeAttachPoint
  [Wood Bucket AttachPoint]  ← افتح Bucket_Wood  → Handle_Top → اسحب RopeAttachPoint
  [Pivot Point]            ← اسحب PivotPoint
  [Canvas Surface]         ← اسحب Canvas_Surface
  [Wall Roof]              ← اسحب Wall_Roof      (للضوء التلقائي)

==========================================================
## الخطوة 4 — ربط BucketVisualController
==========================================================

أضف BucketVisualController على كل دلو:

أ. اضغط على Bucket_Metal في Hierarchy
   → Add Component → BucketVisualController
   → [Pivot Point]          اسحب PivotPoint
   → [Simulation Manager]   اسحب SimulationController

ب. اضغط على Bucket_Wood في Hierarchy
   → Add Component → BucketVisualController
   → [Pivot Point]          اسحب PivotPoint
   → [Simulation Manager]   اسحب SimulationController

==========================================================
## الخطوة 5 — إنشاء واجهة المستخدم (UI Canvas)
==========================================================

### إنشاء Canvas:
1. Hierarchy → كليك يمين → UI → Canvas
2. سمّه "MainCanvas"
3. Canvas Scaler → Scale With Screen Size → 1920×1080

### إنشاء الهيكل داخل Canvas:

MainCanvas
├── Panel_Left          (عرض 300px، يسار الشاشة)
│   ├── Text "🪣 الدلو"
│   ├── Button_BucketMetal   (نص: "معدن 🔩")
│   ├── Button_BucketWood    (نص: "خشب 🪵")
│   ├── Text "🪢 الحبل"
│   ├── Button_RopeCotton    (نص: "قطن")
│   ├── Button_RopeNylon     (نص: "نايلون")
│   ├── Button_RopeSteel     (نص: "فولاذ")
│   ├── Text "🎨 الطلاء"
│   ├── Button_PaintWater    (نص: "مائي")
│   ├── Button_PaintOil      (نص: "زيتي")
│   ├── Button_PaintAcrylic  (نص: "أكريليك")
│   ├── Text "اللون:"
│   ├── Image_ColorPreview   (مربع ملوّن 50×50)
│   ├── Slider_Red           (0→1)
│   ├── Slider_Green         (0→1)
│   └── Slider_Blue          (0→1)
│
├── Panel_Right         (عرض 300px، يمين الشاشة)
│   ├── Text "⚙ الإعدادات"
│   ├── Slider_Angle         (0→90، افتراضي 30)
│   ├── Label_Angle
│   ├── Slider_RopeLength    (0.3→3، افتراضي 1)
│   ├── Label_RopeLength
│   ├── Slider_PaintAmount   (0.02→0.18)
│   ├── Label_PaintAmount
│   ├── Slider_WindSpeed     (0→20)
│   ├── Label_WindSpeed
│   ├── Slider_Temperature   (0→50، افتراضي 20)
│   ├── Label_Temperature
│   ├── Slider_Humidity      (0→100، افتراضي 50)
│   ├── Label_Humidity
│   ├── Slider_HoleRadius    (0.001→0.01)
│   ├── Label_HoleRadius
│   ├── Slider_HoleCount     (1→8)
│   └── Label_HoleCount
│
├── Panel_Bottom        (أسفل الشاشة)
│   ├── Button_Start    (نص: "▶ ابدأ"     لون أخضر)
│   ├── Button_Pause    (نص: "⏸ إيقاف"   لون أصفر)
│   ├── Button_Stop     (نص: "⏹ إنهاء"   لون أحمر)
│   ├── Button_Save     (نص: "💾 حفظ الصورة")
│   └── Button_Report   (نص: "📋 التقرير")
│
├── Panel_Stats         (أعلى يمين، شفاف)
│   └── Text_Stats      (نص الإحصاءات المباشرة)
│
└── Panel_Report        (في المنتصف، مخفي افتراضياً)
    ├── Text_Report     (ScrollView)
    └── Button_Close    (نص: "✕ إغلاق")

==========================================================
## الخطوة 6 — إضافة MainUI وربطه
==========================================================

1. اضغط على MainCanvas في Hierarchy
2. Add Component → MainUI
3. في Inspector اسحب:

   [Scene Connector]    ← اسحب SimulationController
   [Simulation Manager] ← اسحب SimulationController

   === أزرار الدلو ===
   [Btn Bucket Metal]   ← اسحب Button_BucketMetal
   [Btn Bucket Wood]    ← اسحب Button_BucketWood

   === أزرار الحبل ===
   [Btn Rope Cotton]    ← اسحب Button_RopeCotton
   [Btn Rope Nylon]     ← اسحب Button_RopeNylon
   [Btn Rope Steel]     ← اسحب Button_RopeSteel

   === أزرار الطلاء ===
   [Btn Paint Water]    ← اسحب Button_PaintWater
   [Btn Paint Oil]      ← اسحب Button_PaintOil
   [Btn Paint Acrylic]  ← اسحب Button_PaintAcrylic

   === منتقي اللون ===
   [Color Preview Image] ← اسحب Image_ColorPreview
   [Slider Red]          ← اسحب Slider_Red
   [Slider Green]        ← اسحب Slider_Green
   [Slider Blue]         ← اسحب Slider_Blue
   [Label Color Hex]     ← اسحب Text للون الهيكس

   === إعدادات الحركة ===
   [Slider Angle]        ← اسحب Slider_Angle
   [Label Angle]         ← اسحب Label_Angle
   (... وهكذا لكل slider)

   === أزرار التحكم ===
   [Btn Start]           ← اسحب Button_Start
   [Btn Pause]           ← اسحب Button_Pause
   [Btn Stop]            ← اسحب Button_Stop
   [Btn Save Image]      ← اسحب Button_Save
   [Btn Show Report]     ← اسحب Button_Report

   === الإخراج ===
   [Text Stats]          ← اسحب Text_Stats
   [Report Panel]        ← اسحب Panel_Report
   [Text Report]         ← اسحب Text_Report
   [Btn Close Report]    ← اسحب Button_Close

==========================================================
## الخطوة 7 — الضوء (تلقائي!)
==========================================================

✅ لا تحتاج لأي شيء!
SceneConnector يضيف ضوء Point أبيض على Wall_Roof تلقائياً
عند تشغيل المشروع.

إذا أردت ضبطه يدوياً:
  Wall_Roof → Add Component → Light
  Type: Point
  Color: White
  Intensity: 3
  Range: 10

==========================================================
## الخطوة 8 — تشغيل المشروع ✅
==========================================================

1. اضغط Play
2. اختر الدلو (Metal/Wood)
3. اختر الحبل (Cotton/Nylon/Steel)
4. اختر الطلاء ولونه
5. اضبط الزاوية وكمية الطلاء
6. اضغط ▶ ابدأ
7. شاهد الدلو يتأرجح والطلاء يسقط على اللوحة!
8. اضغط 📋 التقرير لرؤية النتائج الكاملة

==========================================================
## ملاحظات مهمة
==========================================================

❌ لا تضف Rigidbody على الدلو أو الحبل
❌ لا تضف Collider على الدلو أو الحبل
✅ كل الفيزياء محسوبة رياضياً بمعادلات لاغرانج
✅ BucketVisualController يحرك الدلو بناءً على الحسابات

==========================================================
## إذا واجهت مشكلة
==========================================================

خطأ "TMPro not found":
  Window → Package Manager → ابحث عن TextMeshPro → Install

خطأ في LineRenderer:
  تأكد أن Rope_Cotton/Nylon/Steel ليس عليها LineRenderer مسبقاً
  أو احذف اللي موجود وخلّي السكريبت يضيفه

الدلو لا يتحرك:
  تأكد أن BucketVisualController موجود على الدلو
  وأن Simulation Manager مربوط فيه
