using UnityEngine;
using System.Collections.Generic;

// ══════════════════════════════════════════════════════════
// إعدادات ثقب واحد — تظهر في Inspector كـ List
// ══════════════════════════════════════════════════════════
[System.Serializable]
public class HoleInspectorSetting
{
    [Range(2f, 15f)]
    [Tooltip("قطر الثقب بالمليمتر")]
    public float diameterMM = 5f;

    [Range(0f, 360f)]
    [Tooltip("موضع الثقب على محيط الدلو (درجات)")]
    public float angularPosition = 0f;

    [Range(0.6f, 0.85f)]
    public float dischargeCoefficient = 0.70f;
}

// ══════════════════════════════════════════════════════════
// إعدادات طبقة لون واحدة
// ══════════════════════════════════════════════════════════
[System.Serializable]
public class ColorLayerSetting
{
    public Color color = Color.red;

    [Range(0.1f, 1f)]
    [Tooltip("نسبة هذه الطبقة من الكمية الكلية")]
    public float amount = 1f;
}

// ══════════════════════════════════════════════════════════
// الكلاس الرئيسي — يظهر كاملاً في Inspector
// ══════════════════════════════════════════════════════════
[System.Serializable]
public class PaintInspectorSettings
{
    [Header("── paint type ──")]
    [Tooltip("مائي=0  زيتي=1  أكريليك=2  مطاطي=3")]
    public PaintType paintType = PaintType.WaterBased;

    [Header("── paint amount ──")]
    [Range(0.02f, 0.18f)]
    [Tooltip("ارتفاع الطلاء داخل الدلو بالمتر (max=0.18m = 90% من الدلو)")]
    public float paintHeightM = 0.15f;

    [Header("── viscosity ──")]
    [Range(0.1f, 5f)]
    [Tooltip("0.1=رقيق جداً  1=طبيعي  5=كثيف جداً")]
    public float viscosityScale = 1.0f;

    [Header("── holes (1-4) ──")]
    [Tooltip("أضف من 1 إلى 4 ثقوب — تُوزَّع تلقائياً إذا تركت angularPosition=0")]
    public List<HoleInspectorSetting> holes = new List<HoleInspectorSetting>
    {
        new HoleInspectorSetting { diameterMM = 5f, angularPosition = 0f }
    };

    [Tooltip("توزيع الثقوب تلقائياً بشكل متناسق (يتجاهل angularPosition)")]
    public bool autoDistributeHoles = true;

    [Header("── Colors ──")]
    [Tooltip("Blend=مزج فوري  Layered=طبقة تحت طبقة")]
    public LayeredColorSystem.ColorMode colorMode = LayeredColorSystem.ColorMode.Blend;

    [Tooltip("أضف حتى 4 ألوان — في Layered: index 0 يخرج أولاً")]
    public List<ColorLayerSetting> colorLayers = new List<ColorLayerSetting>
    {
        new ColorLayerSetting { color = Color.red, amount = 1f }
    };

    [Header("── Droplet Size ──")]
    [Range(0.5f, 20f)]
    [Tooltip("حجم القطرة بالمليمتر")]
    public float dropletSizeMM = 2f;

    [Header("── Flow Rate ──")]
    [Range(10f, 500f)]
    [Tooltip("عدد الجزيئات في الثانية")]
    public float flowRate = 120f;
}