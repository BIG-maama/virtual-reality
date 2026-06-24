using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// نظام الألوان المتعددة — يدعم وضعين:
///   Blend   → ألوان مخلوطة مع بعض داخل الدلو من البداية
///   Layered → طبقات مرتبة (الطبقة السفلى تخرج أولاً)
///
/// مثال Layered: أسود في القاع + أصفر فوقه
///   → أول ما يبدأ التدفق يطلع أسود
///   → لما ينتهي الأسود يطلع الأصفر تلقائياً
/// </summary>
public class LayeredColorSystem
{
    // ══════════════════════════════════════════════════════════
    // Enum
    // ══════════════════════════════════════════════════════════
    public enum ColorMode { Blend, Layered }

    // ══════════════════════════════════════════════════════════
    // Internal Layer
    // ══════════════════════════════════════════════════════════
    private struct ColorLayer
    {
        public Color color;
        public float fillFraction;   // حصة هذه الطبقة من الكمية الكلية (0-1)
        public float cumulativeTop;  // نسبة الملء التراكمية من القاع
    }

    // ══════════════════════════════════════════════════════════
    // State
    // ══════════════════════════════════════════════════════════
    public ColorMode Mode { get; private set; }
    private List<ColorLayer> _layers = new List<ColorLayer>();
    private Color _blendedColor;

    // ══════════════════════════════════════════════════════════
    // Constructor
    // ══════════════════════════════════════════════════════════
    /// <param name="colors">  ألوان الطبقات (index 0 = القاع / يخرج أولاً) </param>
    /// <param name="amounts"> كمية كل طبقة بالنسبة (قيم نسبية، لا يلزم تجمع=1) </param>
    /// <param name="mode">    Blend أو Layered </param>
    public LayeredColorSystem(Color[] colors, float[] amounts, ColorMode mode)
    {
        Mode = mode;
        if (colors == null || colors.Length == 0) colors = new[] { Color.red };
        if (amounts == null || amounts.Length == 0) amounts = new float[] { 1f };

        int count = Mathf.Min(colors.Length, amounts.Length);

        // نحسب مجموع الكميات
        float total = 0f;
        for (int i = 0; i < count; i++) total += Mathf.Max(amounts[i], 0f);
        if (total <= 0f) total = count;

        // نبني الطبقات من القاع للأعلى
        float cumulative = 0f;
        for (int i = 0; i < count; i++)
        {
            float frac = Mathf.Max(amounts[i], 0f) / total;
            _layers.Add(new ColorLayer
            {
                color = colors[i],
                fillFraction = frac,
                cumulativeTop = cumulative + frac
            });
            cumulative += frac;
        }

        // نحسب اللون المخلوط مرة واحدة للـ Blend mode
        _blendedColor = Color.black;
        foreach (var l in _layers)
            _blendedColor += l.color * l.fillFraction;
    }

    // ══════════════════════════════════════════════════════════
    // GetCurrentColor
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// يُعيد لون الطلاء الحالي بناءً على مستوى الملء المتبقي.
    /// fillRatio: 1.0 = ممتلئ تماماً، 0.0 = فارغ تماماً.
    /// </summary>
    public Color GetCurrentColor(float fillRatio)
    {
        fillRatio = Mathf.Clamp01(fillRatio);
        return Mode == ColorMode.Blend
            ? _blendedColor
            : GetLayeredColor(fillRatio);
    }

    // ══════════════════════════════════════════════════════════
    // Layered logic
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// الطبقة السفلى (index 0) تخرج أولاً عند بدء التدفق.
    ///
    ///  fillRatio = 1.0 → الدلو ممتلئ → نحن عند القمة → طبقة علوية
    ///  fillRatio = 0.5 → نصف ممتلئ  → الطبقة السفلى خرجت نصفها
    ///  fillRatio = 0.0 → فارغ        → آخر طبقة علوية
    ///
    /// مثال: طبقتان 50%+50%
    ///   fillRatio > 0.5 → اللون الأسفل (قاع الدلو)
    ///   fillRatio < 0.5 → اللون الأعلى
    /// </summary>
    private Color GetLayeredColor(float fillRatio)
    {
        if (_layers.Count == 1) return _layers[0].color;

        // الكمية التي خرجت = 1 - fillRatio
        float emptied = 1f - fillRatio;

        // نتحرك من القاع للأعلى ونرى أي طبقة وصلنا إليها
        float layerBase = 0f;
        for (int i = 0; i < _layers.Count; i++)
        {
            float layerTop = _layers[i].cumulativeTop;
            if (emptied < layerTop)
            {
                // نحن داخل هذه الطبقة
                // هل نحن في المنطقة الانتقالية (20% آخر الطبقة)؟
                float withinLayer = (emptied - layerBase) / _layers[i].fillFraction;
                if (withinLayer > 0.80f && i + 1 < _layers.Count)
                {
                    // مزج تدريجي مع الطبقة التالية عند الحافة
                    float t = (withinLayer - 0.80f) / 0.20f;
                    return Color.Lerp(_layers[i].color, _layers[i + 1].color, t);
                }
                return _layers[i].color;
            }
            layerBase = layerTop;
        }
        return _layers[_layers.Count - 1].color;
    }

    // ══════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════
    /// <summary>يُعيد رقم الطبقة النشطة حالياً (0-based من القاع)</summary>
    public int GetActiveLayerIndex(float fillRatio)
    {
        float emptied = 1f - Mathf.Clamp01(fillRatio);
        for (int i = 0; i < _layers.Count; i++)
            if (emptied < _layers[i].cumulativeTop) return i;
        return _layers.Count - 1;
    }

    public int LayerCount => _layers.Count;

    public Color GetLayerColor(int idx) =>
        (idx >= 0 && idx < _layers.Count) ? _layers[idx].color : Color.white;
}