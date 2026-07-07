using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// واجهة المستخدم للمحاكاة
/// تتحكم في جميع عناصر الإدخال والإخراج على الشاشة
/// المرجع: المخرجات المتوقعة 1-7 في مشروع الحقائق الافتراضية
/// </summary>
public class SimulationUI : MonoBehaviour
{
    [Header("Simulation Manager Reference")]
    [SerializeField] private SimulationManager _simulationManager;

    // ===== Bucket Controls =====
    [Header("Bucket")]
    [SerializeField] private TMP_Dropdown _bucketShapeDropdown;
    [SerializeField] private Slider _bucketRadiusSlider;
    [SerializeField] private Slider _bucketHeightSlider;
    [SerializeField] private Slider _bucketMassSlider;
    [SerializeField] private TMP_Text _bucketRadiusLabel;
    [SerializeField] private TMP_Text _bucketHeightLabel;
    [SerializeField] private TMP_Text _bucketMassLabel;

    // ===== Hole Controls =====
    [Header("Holes")]
    [SerializeField] private Slider _holeRadiusSlider;
    [SerializeField] private Slider _holeHeightSlider;
    [SerializeField] private Slider _holeCountSlider;
    [SerializeField] private TMP_Dropdown _holeShapeDropdown;
    [SerializeField] private TMP_Text _holeRadiusLabel;
    [SerializeField] private TMP_Text _holeCountLabel;

    // ===== Rope Controls =====
    [Header("Rope")]
    [SerializeField] private Slider _ropeLengthSlider;
    [SerializeField] private TMP_Dropdown _ropeMaterialDropdown;
    [SerializeField] private TMP_Text _ropeLengthLabel;

    // ===== Paint Controls =====
    [Header("Paint")]
    [SerializeField] private TMP_Dropdown _paintTypeDropdown;
    [SerializeField] private Slider _paintHeightSlider;
    [SerializeField] private TMP_Text _paintHeightLabel;
    //  [SerializeField] private FlexibleColorPicker _colorPicker; // Or use custom Image

    // ===== Initial Motion Controls =====
    [Header("Initial Motion")]
    [SerializeField] private Slider _initialAngleSlider;
    [SerializeField] private Slider _initialPhiSlider;
    [SerializeField] private Slider _initialVelocitySlider;
    [SerializeField] private TMP_Text _initialAngleLabel;
    [SerializeField] private TMP_Text _initialPhiLabel;

    // ===== Environment Controls =====
    [Header("Environment")]
    [SerializeField] private Slider _gravitySlider;
    [SerializeField] private Slider _temperatureSlider;
    [SerializeField] private Slider _humiditySlider;
    [SerializeField] private Slider _windSpeedSlider;
    [SerializeField] private Slider _windAngleSlider;
    [SerializeField] private TMP_Text _gravityLabel;
    [SerializeField] private TMP_Text _temperatureLabel;
    [SerializeField] private TMP_Text _humidityLabel;
    [SerializeField] private TMP_Text _windSpeedLabel;

    // ===== Canvas Controls =====
    [Header("Canvas")]
    [SerializeField] private Slider _canvasWidthSlider;
    [SerializeField] private Slider _canvasHeightSlider;
    [SerializeField] private Slider _canvasTiltSlider;
    [SerializeField] private TMP_Dropdown _canvasSurfaceDropdown;
    [SerializeField] private TMP_Text _canvasTiltLabel;

    // ===== Output Elements (Canvas and Statistics) =====
    [Header("Output")]
    [SerializeField] private RawImage _canvasDisplay;          // Live canvas display
    [SerializeField] private TMP_Text _statsText;              // Live statistics
    [SerializeField] private TMP_Text _reportText;             // Final report text
    [SerializeField] private ScrollRect _reportScrollRect;       // Report scrolling

    // ===== Control Buttons =====
    [Header("Buttons")]
    [SerializeField] private Button _startButton;
    [SerializeField] private Button _pauseButton;
    [SerializeField] private Button _stopButton;
    [SerializeField] private Button _saveImageButton;
    [SerializeField] private Button _showReportButton;
    [SerializeField] private Button _compareButton;

    // ===== UI State =====
    private SimulationConfig _currentConfig;
    private bool _isPaused = false;
    private float _uiUpdateTimer = 0f;
    private const float UI_UPDATE_INTERVAL = 0.1f; // Update UI every 100ms

    private void Awake()
    {
        _currentConfig = new SimulationConfig();
        SetupUICallbacks();
        SetDefaultValues();
    }

    private void Update()
    {
        // Update live statistics periodically
        _uiUpdateTimer += Time.deltaTime;
        if (_uiUpdateTimer >= UI_UPDATE_INTERVAL)
        {
            _uiUpdateTimer = 0f;
            UpdateLiveStats();
            UpdateCanvasDisplay();
        }
    }

    // ===== Setup Callbacks =====

    private void SetupUICallbacks()
    {
        // Control buttons
        _startButton?.onClick.AddListener(OnStartClicked);
        _pauseButton?.onClick.AddListener(OnPauseClicked);
        _stopButton?.onClick.AddListener(OnStopClicked);
        _saveImageButton?.onClick.AddListener(OnSaveImageClicked);
        _showReportButton?.onClick.AddListener(OnShowReportClicked);
        _compareButton?.onClick.AddListener(OnCompareClicked);

        // Sliders - Bucket
        _bucketRadiusSlider?.onValueChanged.AddListener(v => {
            _currentConfig.bucket.innerRadius = v;
            _bucketRadiusLabel.text = $"Radius: {v * 100:F1} cm";
        });
        _bucketHeightSlider?.onValueChanged.AddListener(v => {
            _currentConfig.bucket.totalHeight = v;
            _bucketHeightLabel.text = $"Height: {v * 100:F1} cm";
        });
        _bucketMassSlider?.onValueChanged.AddListener(v => {
            _currentConfig.bucket.emptyMass = v;
            _bucketMassLabel.text = $"Mass: {v * 1000:F0} g";
        });

        // Sliders - Rope
        _ropeLengthSlider?.onValueChanged.AddListener(v => {
            _currentConfig.rope.initialLength = v;
            _ropeLengthLabel.text = $"Length: {v:F2} m";
        });

        // Sliders - Paint
        _paintHeightSlider?.onValueChanged.AddListener(v => {
            _currentConfig.paint.initialHeight = v;
            _paintHeightLabel.text = $"Quantity: {v * 100:F1} cm";
        });

        // Sliders - Initial Motion
        _initialAngleSlider?.onValueChanged.AddListener(v => {
            _currentConfig.initialAngleDeg = v;
            _initialAngleLabel.text = $"Angle θ₀: {v:F1}°";
        });
        _initialPhiSlider?.onValueChanged.AddListener(v => {
            _currentConfig.initialPhiDeg = v;
            _initialPhiLabel.text = $"Direction φ: {v:F1}°";
        });
        _initialVelocitySlider?.onValueChanged.AddListener(v => {
            _currentConfig.initialAngularVelocity = v;
        });

        // Sliders - Environment
        _gravitySlider?.onValueChanged.AddListener(v => {
            _currentConfig.environment.gravity = v;
            _gravityLabel.text = $"Gravity g: {v:F2} m/s²";
        });
        _temperatureSlider?.onValueChanged.AddListener(v => {
            _currentConfig.environment.temperature = v;
            _temperatureLabel.text = $"Temperature: {v:F0} °C";
        });
        _humiditySlider?.onValueChanged.AddListener(v => {
            _currentConfig.environment.humidity = v;
            _humidityLabel.text = $"Humidity: {v:F0}%";
        });
        _windSpeedSlider?.onValueChanged.AddListener(v => {
            _currentConfig.environment.windSpeed = v;
            _windSpeedLabel.text = $"Wind: {v:F1} m/s";
        });
        _windAngleSlider?.onValueChanged.AddListener(v => {
            _currentConfig.environment.windAngle = v;
        });

        // Sliders - Canvas
        _canvasTiltSlider?.onValueChanged.AddListener(v => {
            _currentConfig.canvas.tiltAngle = v;
            _canvasTiltLabel.text = $"Tilt: {v:F0}°";
        });

        // Dropdowns
        _bucketShapeDropdown?.onValueChanged.AddListener(v => {
            _currentConfig.bucket.shape = (BucketShape)v;
        });
        _ropeMaterialDropdown?.onValueChanged.AddListener(v => {
            _currentConfig.rope.material = (RopeMaterial)v;
        });
        _paintTypeDropdown?.onValueChanged.AddListener(v => {
            _currentConfig.paint.paintType = (PaintType)v;
        });
        _canvasSurfaceDropdown?.onValueChanged.AddListener(v => {
            _currentConfig.canvas.surface = (SurfaceMaterial)v;
            // _currentConfig.canvas.surface = (CanvasSurface)v;
        });
        _holeShapeDropdown?.onValueChanged.AddListener(v => {
            foreach (HoleData h in _currentConfig.bucket.holes)
                h.shape = (HoleShape)v;
        });
    }

    // ===== Default Values =====

    private void SetDefaultValues()
    {
        _bucketRadiusSlider.SetValueWithoutNotify(0.1f);
        _bucketHeightSlider.SetValueWithoutNotify(0.2f);
        _bucketMassSlider.SetValueWithoutNotify(0.5f);
        _ropeLengthSlider.SetValueWithoutNotify(1.0f);
        _paintHeightSlider.SetValueWithoutNotify(0.15f);
        _initialAngleSlider.SetValueWithoutNotify(30f);
        _initialPhiSlider.SetValueWithoutNotify(0f);
        _gravitySlider.SetValueWithoutNotify(9.80665f);
        _temperatureSlider.SetValueWithoutNotify(20f);
        _humiditySlider.SetValueWithoutNotify(50f);
        _windSpeedSlider.SetValueWithoutNotify(0f);
        _canvasTiltSlider.SetValueWithoutNotify(0f);

        // Update labels
        if (_bucketRadiusLabel) _bucketRadiusLabel.text = "Radius: 10.0 cm";
        if (_ropeLengthLabel) _ropeLengthLabel.text = "Length: 1.00 m";
        if (_gravityLabel) _gravityLabel.text = "Gravity g: 9.81 m/s²";
        if (_temperatureLabel) _temperatureLabel.text = "Temperature: 20 °C";
        if (_initialAngleLabel) _initialAngleLabel.text = "Angle θ₀: 30.0°";
    }

    // ===== Event Handlers =====

    private void OnStartClicked()
    {
        // Build holes according to specified count
        _currentConfig.bucket.holes.Clear();
        int holeCount = Mathf.RoundToInt(_holeCountSlider != null ? _holeCountSlider.value : 1);
        for (int i = 0; i < holeCount; i++)
        {
            _currentConfig.bucket.holes.Add(new HoleData
            {
                shape = (HoleShape)(_holeShapeDropdown?.value ?? 0),
                radius = _holeRadiusSlider?.value ?? 0.003f,
                heightFromBottom = _holeHeightSlider?.value ?? 0.01f,
                angularPosition = (360f / holeCount) * i // Even distribution around circumference
            });
        }

        // Experiment name
        _currentConfig.experimentName = $"Experiment {System.DateTime.Now:HH:mm:ss}";

        _simulationManager.StartSimulation(_currentConfig);
        _isPaused = false;
        _pauseButton.GetComponentInChildren<TMP_Text>().text = "Pause";
    }

    private void OnPauseClicked()
    {
        if (_isPaused)
        {
            _simulationManager.ResumeSimulation();
            _pauseButton.GetComponentInChildren<TMP_Text>().text = "Pause";
        }
        else
        {
            _simulationManager.PauseSimulation();
            _pauseButton.GetComponentInChildren<TMP_Text>().text = "Resume";
        }
        _isPaused = !_isPaused;
    }

    private void OnStopClicked()
    {
        SimulationReport report = _simulationManager.StopAndGenerateReport();
        DisplayReport(report.GenerateTextReport());
    }

    private void OnSaveImageClicked()
    {
        // Save canvas to application folder
        string path = Application.persistentDataPath + "/canvas_" +
                      System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png";
        _simulationManager.SaveCanvasImage(path);
        Debug.Log($"[UI] Image saved at: {path}");
    }

    private void OnShowReportClicked()
    {
        if (_reportText != null && _reportScrollRect != null)
        {
            _reportScrollRect.gameObject.SetActive(true);
        }
    }

    private void OnCompareClicked()
    {
        // Reference: Output #6 - Compare multiple experiments
        string comparison = _simulationManager.GetComparisonReport();
        DisplayReport(comparison);
    }

    // ===== Update Live Output =====

    /// <summary>
    /// يُحدّث الإحصاءات المباشرة أثناء التشغيل
    /// المرجع: المخرجات رقم 5 - عرض القيم المستخدمة في التجربة
    /// </summary>
    private void UpdateLiveStats()
    {
        if (_statsText == null || _simulationManager?.Physics == null) return;

        BucketPhysics p = _simulationManager.Physics;

        _statsText.text =
            $"Time: {p.SimulationTime:F2} s\n" +
            $"Angle θ: {p.Theta * Mathf.Rad2Deg:F1}°\n" +
            $"Angle φ: {p.Phi * Mathf.Rad2Deg:F1}°\n" +
            $"Mass m(t): {p.CurrentMass * 1000:F1} g\n" +
            $"Paint Height h(t): {p.CurrentPaintHeight * 100:F1} cm\n" +
            $"Rope Length L(t): {p.CurrentRopeLength:F4} m\n" +
            $"Swing Count: {p.SwingCount}\n" +
            $"Kinetic Energy: {p.GetKineticEnergy():F4} J\n" +
            $"Rope Tension T: {p.GetRopeTension():F2} N\n" +
            $"Pendulum Period: {p.GetPeriod():F3} s";
    }

    /// <summary>
    /// يُحدّث صورة اللوحة المباشرة
    /// المرجع: المخرجات رقم 2 - رسم المسارات الناتجة على اللوحة بشكل حي
    /// </summary>
    private void UpdateCanvasDisplay()
    {
        // Canvas texture is updated directly from CanvasPainter
        // Here we ensure RawImage is updated with current Texture
    }

    private void DisplayReport(string text)
    {
        if (_reportText == null) return;
        _reportText.text = text;
        if (_reportScrollRect != null)
        {
            _reportScrollRect.gameObject.SetActive(true);
            _reportScrollRect.verticalNormalizedPosition = 1f; // Scroll to top
        }
    }
}