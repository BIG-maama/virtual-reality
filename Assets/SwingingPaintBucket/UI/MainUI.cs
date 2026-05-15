using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// واجهة المستخدم - النسخة المصححة
/// الخطأ المُصلَح: reportPanel لم يكن مربوطاً → أصبح nullable بـ ?
/// </summary>
public class MainUI : MonoBehaviour
{
    [Header("=== Core References ===")]
    public SceneConnectorFinal connector;
    public SimulationManager simulationManager;

    // ══════════════════════════════════════════
    // Panel_Left - Bucket Buttons
    // ══════════════════════════════════════════
    [Header("=== Panel_Left: Bucket Buttons ===")]
    [Tooltip("Drag Button_BucketMetal from Panel_Left")]
    public Button btnBucketMetal;

    [Tooltip("Drag Button_BucketWood from Panel_Left")]
    public Button btnBucketWood;

    // ══════════════════════════════════════════
    // Panel_Left - Rope Buttons
    // ══════════════════════════════════════════
    [Header("=== Panel_Left: Rope Buttons ===")]
    [Tooltip("Drag Button_RopeCotton from Panel_Left")]
    public Button btnRopeCotton;

    [Tooltip("Drag Button_RopeNylon from Panel_Left")]
    public Button btnRopeNylon;

    [Tooltip("Drag Button_RopeSteel from Panel_Left")]
    public Button btnRopeSteel;

    // ══════════════════════════════════════════
    // Panel_Left - Paint Buttons
    // ══════════════════════════════════════════
    [Header("=== Panel_Left: Paint Buttons ===")]
    [Tooltip("Drag Button_PaintWater from Panel_Left")]
    public Button btnPaintWater;

    [Tooltip("Drag Button_PaintOil from Panel_Left")]
    public Button btnPaintOil;

    [Tooltip("Drag Button_PaintAcrylic from Panel_Left")]
    public Button btnPaintAcrylic;

    // ══════════════════════════════════════════
    // Panel_Left - Color Picker
    // ══════════════════════════════════════════
    [Header("=== Panel_Left: Color ===")]
    [Tooltip("Drag Image_ColorPreview from Panel_Left")]
    public Image colorPreviewImage;

    [Tooltip("Drag Slider_Red from Panel_Left")]
    public Slider sliderRed;

    [Tooltip("Drag Slider_Green from Panel_Left")]
    public Slider sliderGreen;

    [Tooltip("Drag Slider_Blue from Panel_Left")]
    public Slider sliderBlue;

    // ══════════════════════════════════════════
    // Panel_Right - Motion Settings
    // ══════════════════════════════════════════
    [Header("=== Panel_Right: Motion Settings ===")]
    [Tooltip("Drag Slider_Angle from Panel_Right")]
    public Slider sliderAngle;
    public TMP_Text labelAngle;

    [Tooltip("Drag Slider_RopeLength from Panel_Right")]
    public Slider sliderRopeLength;
    public TMP_Text labelRopeLength;

    [Tooltip("Drag Slider_PaintAmount from Panel_Right")]
    public Slider sliderPaintAmount;
    public TMP_Text labelPaintAmount;

    [Tooltip("Drag Slider_WindSpeed from Panel_Right")]
    public Slider sliderWindSpeed;
    public TMP_Text labelWindSpeed;

    [Tooltip("Drag Slider_Temperature from Panel_Right")]
    public Slider sliderTemperature;
    public TMP_Text labelTemperature;

    [Tooltip("Drag Slider_Humidity from Panel_Right")]
    public Slider sliderHumidity;
    public TMP_Text labelHumidity;

    [Tooltip("Drag Slider_HoleRadius from Panel_Right")]
    public Slider sliderHoleRadius;
    public TMP_Text labelHoleRadius;

    [Tooltip("Drag Slider_HoleCount from Panel_Right")]
    public Slider sliderHoleCount;
    public TMP_Text labelHoleCount;

    // ══════════════════════════════════════════
    // Panel_Bottom - Control Buttons
    // ══════════════════════════════════════════
    [Header("=== Panel_Bottom: Control Buttons ===")]
    [Tooltip("Drag Button_Start from Panel_Bottom")]
    public Button btnStart;

    [Tooltip("Drag Button_Pause from Panel_Bottom")]
    public Button btnPause;

    [Tooltip("Drag Button_Stop from Panel_Bottom")]
    public Button btnStop;

    [Tooltip("Drag Button_Save from Panel_Bottom")]
    public Button btnSaveImage;

    [Tooltip("Drag Button_Report from Panel_Bottom")]
    public Button btnShowReport;

    // ══════════════════════════════════════════
    // Panel_Stats - Statistics
    // ══════════════════════════════════════════
    [Header("=== Panel_Stats: Statistics ===")]
    [Tooltip("Drag Text_Stats from Panel_Stats")]
    public TMP_Text textStats;

    // ══════════════════════════════════════════
    // Panel_Report - Report
    // ══════════════════════════════════════════
    [Header("=== Panel_Report: Report ===")]
    [Tooltip("Drag Panel_Report from MainCanvas")]
    public GameObject reportPanel;   // ← this was the error cause - now nullable

    [Tooltip("Drag Text_Report from Panel_Report")]
    public TMP_Text textReport;

    [Tooltip("Drag Button_Close from Panel_Report")]
    public Button btnCloseReport;

    // ══════════════════════════════════════════
    // Internal variables
    // ══════════════════════════════════════════
    private Color _selectedColor = Color.red;
    private bool _isPaused = false;
    private float _statsTimer = 0f;

    private readonly Color _activeColor = new Color(0.2f, 0.8f, 0.3f);
    private readonly Color _inactiveColor = new Color(0.35f, 0.35f, 0.35f);

    // ══════════════════════════════════════════
    private void Start()
    {
        // ← Fix: Hide Panel_Report only if assigned
        if (reportPanel != null)
            reportPanel.SetActive(false);

        SetupButtons();
        SetDefaults();
    }

    private void Update()
    {
        _statsTimer += Time.deltaTime;
        if (_statsTimer >= 0.1f)
        {
            _statsTimer = 0f;
            RefreshStats();
        }
    }

    // ══════════════════════════════════════════
    // Connect all buttons and sliders
    // ══════════════════════════════════════════
    private void SetupButtons()
    {
        // ── Bucket ──
        btnBucketMetal?.onClick.AddListener(() =>
        {
            connector?.SwitchBucket(true);
            HighlightBucket(true);
        });
        btnBucketWood?.onClick.AddListener(() =>
        {
            connector?.SwitchBucket(false);
            HighlightBucket(false);
        });

        // ── Rope ──
        btnRopeCotton?.onClick.AddListener(() => { connector?.SwitchRope(0); HighlightRope(0); });
        btnRopeNylon?.onClick.AddListener(() => { connector?.SwitchRope(1); HighlightRope(1); });
        btnRopeSteel?.onClick.AddListener(() => { connector?.SwitchRope(2); HighlightRope(2); });

        // ── Paint ──
        btnPaintWater?.onClick.AddListener(() => HighlightPaint(0));
        btnPaintOil?.onClick.AddListener(() => HighlightPaint(1));
        btnPaintAcrylic?.onClick.AddListener(() => HighlightPaint(2));

        // ── RGB Color ──
        sliderRed?.onValueChanged.AddListener(_ => OnColorChanged());
        sliderGreen?.onValueChanged.AddListener(_ => OnColorChanged());
        sliderBlue?.onValueChanged.AddListener(_ => OnColorChanged());

        // ── Motion Sliders ──
        sliderAngle?.onValueChanged.AddListener(v =>
        {
            if (labelAngle) labelAngle.text = $"Angle: {v:F0}°";
        });
        sliderRopeLength?.onValueChanged.AddListener(v =>
        {
            if (labelRopeLength) labelRopeLength.text = $"Rope: {v:F2} m";
        });
        sliderPaintAmount?.onValueChanged.AddListener(v =>
        {
            if (labelPaintAmount) labelPaintAmount.text = $"Paint: {v * 100:F0} cm";
        });
        sliderWindSpeed?.onValueChanged.AddListener(v =>
        {
            if (labelWindSpeed) labelWindSpeed.text = $"Wind: {v:F1} m/s";
        });
        sliderTemperature?.onValueChanged.AddListener(v =>
        {
            if (labelTemperature) labelTemperature.text = $"Temp: {v:F0} C";
        });
        sliderHumidity?.onValueChanged.AddListener(v =>
        {
            if (labelHumidity) labelHumidity.text = $"Humidity: {v:F0}%";
        });
        sliderHoleRadius?.onValueChanged.AddListener(v =>
        {
            if (labelHoleRadius) labelHoleRadius.text = $"Hole: {v * 1000:F1} mm";
        });
        sliderHoleCount?.onValueChanged.AddListener(v =>
        {
            if (labelHoleCount) labelHoleCount.text = $"Holes: {Mathf.RoundToInt(v)}";
        });

        // ── Main Control Buttons ──
        btnStart?.onClick.AddListener(OnStart);
        btnPause?.onClick.AddListener(OnPause);
        btnStop?.onClick.AddListener(OnStop);
        btnSaveImage?.onClick.AddListener(OnSave);
        btnShowReport?.onClick.AddListener(() =>
        {
            if (reportPanel != null) reportPanel.SetActive(true);
        });
        btnCloseReport?.onClick.AddListener(() =>
        {
            if (reportPanel != null) reportPanel.SetActive(false);
        });
    }

    // ══════════════════════════════════════════
    // Default values
    // ══════════════════════════════════════════
    private void SetDefaults()
    {
        sliderAngle?.SetValueWithoutNotify(30f);
        sliderRopeLength?.SetValueWithoutNotify(2.5f);
        sliderPaintAmount?.SetValueWithoutNotify(0.12f);
        sliderWindSpeed?.SetValueWithoutNotify(0f);
        sliderTemperature?.SetValueWithoutNotify(20f);
        sliderHumidity?.SetValueWithoutNotify(50f);
        sliderHoleRadius?.SetValueWithoutNotify(0.003f);
        sliderHoleCount?.SetValueWithoutNotify(1f);
        sliderRed?.SetValueWithoutNotify(1f);
        sliderGreen?.SetValueWithoutNotify(0f);
        sliderBlue?.SetValueWithoutNotify(0f);

        if (labelAngle) labelAngle.text = "Angle: 30°";
        if (labelRopeLength) labelRopeLength.text = "Rope: 2.50 m";
        if (labelPaintAmount) labelPaintAmount.text = "Paint: 12 cm";
        if (labelTemperature) labelTemperature.text = "Temp: 20 C";
        if (labelHumidity) labelHumidity.text = "Humidity: 50%";
        if (labelWindSpeed) labelWindSpeed.text = "Wind: 0.0 m/s";
        if (labelHoleRadius) labelHoleRadius.text = "Hole: 3.0 mm";
        if (labelHoleCount) labelHoleCount.text = "Holes: 1";

        UpdateColorPreview();
        HighlightBucket(true);
        HighlightRope(0);
        HighlightPaint(0);
    }

    // ══════════════════════════════════════════
    // Button handlers
    // ══════════════════════════════════════════
    private void OnStart()
    {
        connector?.Restart();
        _isPaused = false;
        var txt = btnPause?.GetComponentInChildren<TMP_Text>();
        if (txt) txt.text = "Pause";
    }

    private void OnPause()
    {
        if (_isPaused) simulationManager?.ResumeSimulation();
        else simulationManager?.PauseSimulation();
        _isPaused = !_isPaused;
        var txt = btnPause?.GetComponentInChildren<TMP_Text>();
        if (txt) txt.text = _isPaused ? "Resume" : "Pause";
    }

    private void OnStop()
    {
        var report = simulationManager?.StopAndGenerateReport();
        if (report != null && textReport != null)
        {
            textReport.text = report.GenerateTextReport();
            if (reportPanel != null) reportPanel.SetActive(true);
        }
    }

    private void OnSave()
    {
        string path = Application.persistentDataPath + "/canvas_"
                    + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        simulationManager?.SaveCanvasImage(path);
        Debug.Log("[UI] Saved: " + path);
    }

    private void OnColorChanged()
    {
        float r = sliderRed?.value ?? 1f;
        float g = sliderGreen?.value ?? 0f;
        float b = sliderBlue?.value ?? 0f;
        _selectedColor = new Color(r, g, b);
        connector?.SetPaintColor(_selectedColor);
        UpdateColorPreview();
    }

    private void UpdateColorPreview()
    {
        if (colorPreviewImage)
            colorPreviewImage.color = _selectedColor;
    }

    // ══════════════════════════════════════════
    // Update statistics from Panel_Stats → Text_Stats
    // ══════════════════════════════════════════
    private void RefreshStats()
    {
        if (textStats == null || connector == null) return;
        textStats.text = connector.GetStatsText();
    }

    // ══════════════════════════════════════════
    // Highlight active buttons
    // ══════════════════════════════════════════
    private void HighlightBucket(bool metal)
    {
        SetBtnColor(btnBucketMetal, metal ? _activeColor : _inactiveColor);
        SetBtnColor(btnBucketWood, !metal ? _activeColor : _inactiveColor);
    }

    private void HighlightRope(int idx)
    {
        SetBtnColor(btnRopeCotton, idx == 0 ? _activeColor : _inactiveColor);
        SetBtnColor(btnRopeNylon, idx == 1 ? _activeColor : _inactiveColor);
        SetBtnColor(btnRopeSteel, idx == 2 ? _activeColor : _inactiveColor);
    }

    private void HighlightPaint(int idx)
    {
        SetBtnColor(btnPaintWater, idx == 0 ? _activeColor : _inactiveColor);
        SetBtnColor(btnPaintOil, idx == 1 ? _activeColor : _inactiveColor);
        SetBtnColor(btnPaintAcrylic, idx == 2 ? _activeColor : _inactiveColor);
    }

    private void SetBtnColor(Button btn, Color col)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img) img.color = col;
    }
}