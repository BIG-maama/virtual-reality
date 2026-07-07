using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SurfacePanelUI : MonoBehaviour
{
    public SceneConnectorFinal connector;

    [Header("أزرار اختيار السطح")]
    public Button btnCanvas, btnWood, btnMetal, btnPaper;

    [Header("عرض حي")]
    public TMP_Text readout;

    [Header("ميلان اللوحة")]
    public Slider sliderTilt;
    public TMP_Text labelTilt;

    

    private void Start()
    {
        btnCanvas?.onClick.AddListener(() => SetSurface(SurfaceMaterial.Canvas));
        btnWood?.onClick.AddListener(() => SetSurface(SurfaceMaterial.Wood));
        btnMetal?.onClick.AddListener(() => SetSurface(SurfaceMaterial.Metal));
        btnPaper?.onClick.AddListener(() => SetSurface(SurfaceMaterial.Paper));
        sliderTilt?.onValueChanged.AddListener(v =>
        {
            connector?.SetCanvasTilt(v);
            if (labelTilt) labelTilt.text = $"Tilt: {v:F0}°";
        });
    }

    private void SetSurface(SurfaceMaterial m)
    {
        if (connector == null) return;
        connector.canvasSurfaceSelection = m;
        connector.Restart(); // يعيد بناء المشهد بالسطح الجديد
    }

    private void Update()
    {
        if (readout == null || connector == null) return;
        var painter = connector.GetPainter();
        readout.text = painter == null ? "—" :
            $"Surface: {connector.canvasSurfaceSelection}\n" +
            $"Paths: {painter.TotalPathCount}\n" +
            $"Area: {painter.PaintedAreaM2 * 10000:F1} cm²";
    }
}