using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// لوحة تحكم السوائل — تتحكم بكل إعدادات الطلاء والثقوب والألوان
///
/// كيفية الربط في Unity:
///   1. أضف هذا السكريبت على GameObject داخل Canvas
///   2. اسحب SceneConnectorFinal إلى حقل sceneConnector
///   3. اسحب عناصر UI المقابلة لكل حقل
///   4. اضغط Play
///
/// ملاحظة: جميع التغييرات تُطبَّق فوراً على الـ Sliders/Toggles
///         وزر "تطبيق وإعادة تشغيل" يُعيد بناء المحاكاة كاملةً
/// </summary>
public class PaintControlPanel : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════
    // References
    // ══════════════════════════════════════════════════════════
    [Header("── المرجع الرئيسي ──")]
    public SceneConnectorFinal sceneConnector;

    // ══════════════════════════════════════════════════════════
    // Paint Type
    // ══════════════════════════════════════════════════════════
    [Header("── نوع السائل ──")]
    public TMP_Dropdown paintTypeDropdown;
    // 0=مائي  1=زيتي  2=أكريليك  3=مطاطي

    // ══════════════════════════════════════════════════════════
    // Viscosity
    // ══════════════════════════════════════════════════════════
    [Header("── اللزوجة ──")]
    public Slider viscositySlider;   // min=0.1  max=5.0
    public TMP_Text viscosityLabel;

    // ══════════════════════════════════════════════════════════
    // Paint Amount
    // ══════════════════════════════════════════════════════════
    [Header("── كمية الطلاء ──")]
    public Slider paintHeightSlider;  // min=0.02m  max=0.18m
    public TMP_Text paintHeightLabel;

    // ══════════════════════════════════════════════════════════
    // Holes — عدد الثقوب وأقطارها
    // ══════════════════════════════════════════════════════════
    [Header("── الثقوب ──")]
    [Tooltip("Slider من 1 إلى 4، wholeNumbers=true")]
    public Slider holesCountSlider;
    public TMP_Text holesCountLabel;

    [Tooltip("4 GameObject للثقوب — كل واحد يحتوي Slider + TMP_Text")]
    public GameObject[] holePanels = new GameObject[4];   // يُخفى/يُظهر حسب العدد
    public Slider[] holeDiamSliders = new Slider[4];       // mm: min=2  max=15
    public TMP_Text[] holeDiamLabels = new TMP_Text[4];

    // ══════════════════════════════════════════════════════════
    // Color Mode
    // ══════════════════════════════════════════════════════════
    [Header("── وضع الألوان ──")]
    public Toggle blendToggle;    // مزج الألوان مع بعض
    public Toggle layeredToggle;  // طبقات مرتبة

    // ══════════════════════════════════════════════════════════
    // Colors — حتى 4 ألوان
    // ══════════════════════════════════════════════════════════
    [Header("── الألوان ──")]
    [Tooltip("أزرار اختيار الألوان (حتى 4)")]
    public Button[] colorButtons = new Button[4];
    public Image[] colorPreviews = new Image[4];   // مستطيل يعرض اللون المختار

    [Tooltip("Sliders لتحديد نسبة كل لون في Layered mode (0-1)")]
    public Slider[] colorRatioSliders = new Slider[4];
    public TMP_Text[] colorRatioLabels = new TMP_Text[4];

    [Tooltip("زر + لإضافة لون آخر")]
    public Button addColorButton;
    [Tooltip("زر − لحذف آخر لون")]
    public Button removeColorButton;
    public TMP_Text colorCountLabel;

    // ══════════════════════════════════════════════════════════
    // Droplet Size
    // ══════════════════════════════════════════════════════════
    [Header("── حجم القطرة ──")]
    public Slider dropletSlider;   // mm: min=0.5  max=20
    public TMP_Text dropletLabel;

    // ══════════════════════════════════════════════════════════
    // Flow Rate
    // ══════════════════════════════════════════════════════════
    [Header("── معدل التدفق ──")]
    public Slider flowRateSlider;  // min=5  max=200
    public TMP_Text flowRateLabel;

    // ══════════════════════════════════════════════════════════
    // Buttons
    // ══════════════════════════════════════════════════════════
    [Header("── أزرار ──")]
    public Button applyButton;    // تطبيق كل الإعدادات وإعادة التشغيل
    public Button restartButton;  // إعادة تشغيل بدون تغيير الإعدادات

    // ══════════════════════════════════════════════════════════
    // Internal State
    // ══════════════════════════════════════════════════════════
    private Color[] _colors = { Color.red, Color.blue, Color.yellow, Color.green };
    private int _colorCount = 1;
    private bool _isLayered = false;
    private int _holeCount = 1;

    // Bucket dimensions (m) — يجب أن تطابق SceneConnectorFinal.BuildConfig()
    private const float BUCKET_RADIUS = 0.10f;
    private const float BUCKET_HEIGHT = 0.20f;
    private float MaxPaintHeight => BUCKET_HEIGHT * 0.90f;  // 90% من الحجم الكلي

    // Color picker palette بسيطة
    private static readonly Color[] _palette =
    {
        Color.red,     Color.blue,    Color.yellow, Color.green,
        Color.white,   Color.black,   Color.cyan,   Color.magenta,
        new Color(1f,0.5f,0f),   // برتقالي
        new Color(0.5f,0f,1f),   // بنفسجي
        new Color(0.6f,0.3f,0f), // بني
        new Color(1f,0.8f,0.8f)  // زهري فاتح
    };
    private int[] _paletteIndex = { 0, 1, 2, 3 }; // الفهرس الحالي لكل زر

    // ══════════════════════════════════════════════════════════
    // Unity Lifecycle
    // ══════════════════════════════════════════════════════════
    private void Start()
    {
        InitUI();
        BindButtons();
        // تطبيق القيم الافتراضية
        UpdateHolePanelsVisibility(_holeCount);
        UpdateColorSliderVisibility();
        RefreshColorCountLabel();
    }

    // ══════════════════════════════════════════════════════════
    // UI Initialization
    // ══════════════════════════════════════════════════════════
    private void InitUI()
    {
        // Paint Type Dropdown
        if (paintTypeDropdown != null)
        {
            paintTypeDropdown.ClearOptions();
            paintTypeDropdown.AddOptions(new List<string>
            {
                "💧 مائي (رقيق سريع)",
                "🛢 زيتي (كثيف بطيء)",
                "🎨 أكريليك (متوسط)",
                "🔵 مطاطي (مرن)"
            });
            paintTypeDropdown.value = 0;
            paintTypeDropdown.onValueChanged.AddListener(v => sceneConnector?.SetPaintType(v));
        }

        // Viscosity Slider
        SetupSlider(viscositySlider, 0.1f, 5.0f, 1.0f, v =>
        {
            UpdateLabel(viscosityLabel,
                v < 0.8f ? $"اللزوجة: رقيق جداً ({v:F1})" :
                v < 1.5f ? $"اللزوجة: رقيق ({v:F1})" :
                v < 3.0f ? $"اللزوجة: متوسط ({v:F1})" :
                            $"اللزوجة: كثيف ({v:F1})");
            sceneConnector?.SetViscosityScale(v);
        });

        // Paint Height Slider
        SetupSlider(paintHeightSlider, 0.02f, MaxPaintHeight, 0.15f, v =>
        {
            float pct = (v / MaxPaintHeight) * 100f;
            UpdateLabel(paintHeightLabel, $"كمية الطلاء: {v * 100:F1} cm ({pct:F0}% من الدلو)");
        });

        // Holes Count Slider
        if (holesCountSlider != null)
        {
            holesCountSlider.minValue = 1;
            holesCountSlider.maxValue = 4;
            holesCountSlider.wholeNumbers = true;
            holesCountSlider.value = 1;
            holesCountSlider.onValueChanged.AddListener(v =>
            {
                _holeCount = Mathf.RoundToInt(v);
                UpdateLabel(holesCountLabel, $"عدد الثقوب: {_holeCount}");
                UpdateHolePanelsVisibility(_holeCount);
            });
        }

        // Hole Diameter Sliders (4 ثقوب)
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            float defaultDiam = 5f; // 5mm
            SetupSlider(holeDiamSliders[i], 2f, 15f, defaultDiam, v =>
            {
                float flowApprox = v * v; // تناسب تقريبي Q ~ d²
                UpdateLabel(holeDiamLabels[idx],
                    $"ثقب {idx + 1}: {v:F1} mm  — تدفق: {(v <= 4f ? "بطيء" : v <= 8f ? "متوسط" : "سريع")}");
            });
        }

        // Droplet Size
        SetupSlider(dropletSlider, 0.5f, 20f, 2f, v =>
            UpdateLabel(dropletLabel, $"حجم القطرة: {v:F1} mm"));

        // Flow Rate
        SetupSlider(flowRateSlider, 5f, 200f, 80f, v =>
        {
            UpdateLabel(flowRateLabel, $"معدل التدفق: {v:F0} جزيئ/ثانية");
            sceneConnector?.SetFlowRate(v);
        });

        // Color Mode Toggles
        if (blendToggle != null) blendToggle.onValueChanged.AddListener(v => { if (v) OnBlendMode(); });
        if (layeredToggle != null) layeredToggle.onValueChanged.AddListener(v => { if (v) OnLayeredMode(); });

        // تهيئة ألوان الزرار
        for (int i = 0; i < 4; i++) RefreshColorButton(i);

        // Color Ratio Sliders
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            SetupSlider(colorRatioSliders[i], 0f, 1f, 1f, v =>
                UpdateLabel(colorRatioLabels[idx], $"نسبة اللون {idx + 1}: {v * 100:F0}%"));
        }
    }

    private void BindButtons()
    {
        // Color buttons: كل ضغطة تدوّر على palette
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            if (colorButtons[i] != null)
                colorButtons[i].onClick.AddListener(() => CycleColor(idx));
        }

        if (addColorButton != null) addColorButton.onClick.AddListener(AddColor);
        if (removeColorButton != null) removeColorButton.onClick.AddListener(RemoveColor);
        if (applyButton != null) applyButton.onClick.AddListener(ApplyAll);
        if (restartButton != null) restartButton.onClick.AddListener(() => sceneConnector?.Restart());
    }

    // ══════════════════════════════════════════════════════════
    // Color Management
    // ══════════════════════════════════════════════════════════
    private void CycleColor(int idx)
    {
        if (idx >= _colorCount) return;
        _paletteIndex[idx] = (_paletteIndex[idx] + 1) % _palette.Length;
        _colors[idx] = _palette[_paletteIndex[idx]];
        RefreshColorButton(idx);
    }

    private void RefreshColorButton(int idx)
    {
        if (colorPreviews != null && idx < colorPreviews.Length && colorPreviews[idx] != null)
            colorPreviews[idx].color = _colors[idx];
    }

    private void AddColor()
    {
        if (_colorCount >= 4) return;
        _colorCount++;
        UpdateHolePanelsVisibility(_holeCount); // refresh
        UpdateColorSliderVisibility();
        RefreshColorCountLabel();
    }

    private void RemoveColor()
    {
        if (_colorCount <= 1) return;
        _colorCount--;
        UpdateColorSliderVisibility();
        RefreshColorCountLabel();
    }

    private void RefreshColorCountLabel()
    {
        if (colorCountLabel != null)
            colorCountLabel.text = $"عدد الألوان: {_colorCount}";
    }

    private void OnBlendMode()
    {
        _isLayered = false;
        UpdateColorSliderVisibility();
    }

    private void OnLayeredMode()
    {
        _isLayered = true;
        UpdateColorSliderVisibility();
    }

    /// <summary>تُظهر Ratio Sliders فقط في Layered mode وفقط للألوان النشطة</summary>
    private void UpdateColorSliderVisibility()
    {
        for (int i = 0; i < 4; i++)
        {
            bool show = _isLayered && i < _colorCount;
            if (colorRatioSliders[i] != null)
                colorRatioSliders[i].gameObject.SetActive(show);
            if (colorRatioLabels[i] != null)
                colorRatioLabels[i].gameObject.SetActive(show);

            // إظهار/إخفاء زر اللون نفسه
            if (colorButtons[i] != null)
                colorButtons[i].gameObject.SetActive(i < _colorCount);
            if (colorPreviews[i] != null)
                colorPreviews[i].gameObject.SetActive(i < _colorCount);
        }
    }

    // ══════════════════════════════════════════════════════════
    // Holes Visibility
    // ══════════════════════════════════════════════════════════
    private void UpdateHolePanelsVisibility(int count)
    {
        for (int i = 0; i < 4; i++)
        {
            bool active = i < count;
            if (holePanels[i] != null) holePanels[i].SetActive(active);
        }
        UpdateLabel(holesCountLabel, $"عدد الثقوب: {count}");
    }

    // ══════════════════════════════════════════════════════════
    // Build Data
    // ══════════════════════════════════════════════════════════

    /// <summary>يبني قائمة الثقوب موزّعة بشكل متناسق على محيط قاع الدلو</summary>
    private List<HoleData> BuildHoleList()
    {
        var list = new List<HoleData>();
        for (int i = 0; i < _holeCount; i++)
        {
            float angleDeg = (360f / _holeCount) * i;  // توزيع متناسق
            float diamMM = holeDiamSliders[i] != null ? holeDiamSliders[i].value : 5f;
            float radiusM = (diamMM * 0.5f) / 1000f;  // mm → m

            list.Add(new HoleData
            {
                shape = HoleShape.Circular,
                radius = radiusM,
                heightFromBottom = 0f,          // في قاع الدلو
                angularPosition = angleDeg,
                dischargeCoefficient = 0.70f
            });
        }
        return list;
    }

    /// <summary>يبني مصفوفة الألوان للمحاكاة</summary>
    private Color[] BuildColors()
    {
        var list = new List<Color>();
        for (int i = 0; i < _colorCount; i++) list.Add(_colors[i]);
        return list.ToArray();
    }

    /// <summary>يبني مصفوفة النسب للـ Layered mode</summary>
    private float[] BuildColorAmounts()
    {
        var list = new List<float>();
        for (int i = 0; i < _colorCount; i++)
        {
            float ratio = (colorRatioSliders[i] != null) ? colorRatioSliders[i].value : 1f;
            list.Add(Mathf.Max(ratio, 0.01f));
        }
        return list.ToArray();
    }

    // ══════════════════════════════════════════════════════════
    // Apply All Settings
    // ══════════════════════════════════════════════════════════
    public void ApplyAll()
    {
        if (sceneConnector == null)
        {
            Debug.LogWarning("[PaintControlPanel] sceneConnector غير مربوط!");
            return;
        }

        // 1. نوع الطلاء
        if (paintTypeDropdown != null)
            sceneConnector.SetPaintType(paintTypeDropdown.value);

        // 2. اللزوجة
        if (viscositySlider != null)
            sceneConnector.SetViscosityScale(viscositySlider.value);

        // 3. كمية الطلاء
        if (paintHeightSlider != null)
            sceneConnector.SetInitialPaintHeight(paintHeightSlider.value);

        // 4. الثقوب
        sceneConnector.SetHoles(BuildHoleList());

        // 5. الألوان
        sceneConnector.SetPaintColors(BuildColors());

        // 6. نظام الألوان (طبقات أو مزج)
        sceneConnector.SetColorLayeringMode(_isLayered, BuildColorAmounts());

        // 7. حجم القطرة
        if (dropletSlider != null)
            sceneConnector.SetDropletRadius((dropletSlider.value * 0.5f) / 1000f); // mm→m

        // 8. معدل التدفق
        if (flowRateSlider != null)
            sceneConnector.SetFlowRate(flowRateSlider.value);

        // 9. إعادة بناء المحاكاة
        sceneConnector.Restart();

        Debug.Log($"[PaintControlPanel] Applied: {_colorCount} colors | " +
                  $"{_holeCount} holes | mode={(_isLayered ? "Layered" : "Blend")}");
    }

    // ══════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════
    private static void SetupSlider(Slider s, float min, float max, float def,
                                    UnityEngine.Events.UnityAction<float> cb)
    {
        if (s == null) return;
        s.minValue = min;
        s.maxValue = max;
        s.value = def;
        s.onValueChanged.AddListener(cb);
        cb(def); // تطبيق القيمة الافتراضية فوراً
    }

    private static void UpdateLabel(TMP_Text label, string text)
    {
        if (label != null) label.text = text;
    }

#if UNITY_EDITOR
    // ═══════ Debug Gizmo ═══════
    private void OnValidate()
    {
        // تحقق من ربط المراجع
        if (sceneConnector == null)
            Debug.LogWarning("[PaintControlPanel] لم يُربط sceneConnector بعد.");
    }
#endif
}