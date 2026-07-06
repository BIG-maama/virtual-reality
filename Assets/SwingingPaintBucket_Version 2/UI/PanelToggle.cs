using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// يتحكم بإظهار/إخفاء لوحة التحكم (BucketTwistPanel_UI) بأنيميشن سلس.
/// ضيفي هذا السكربت على الزر (Button) نفسه.
/// </summary>
public class PanelToggle : MonoBehaviour
{
    [Header("=== المرجع الأساسي ===")]
    [Tooltip("اللوحة يلي بدك تتحكمي فيها (BucketTwistPanel_UI (1))")]
    public GameObject panel;

    [Header("=== إعدادات الأنيميشن ===")]
    [Tooltip("مدة ظهور/اختفاء اللوحة بالثواني")]
    public float animationDuration = 0.25f;

    [Tooltip("هل اللوحة تبدأ ظاهرة عند تشغيل المشهد؟")]
    public bool startVisible = false;

    [Header("=== نص الزر (اختياري) ===")]
    [Tooltip("إذا بدك نص الزر يتغير مثلاً من ⚙ إلى ✕")]
    public Text buttonLabelLegacy;      // إذا الزر يستخدم Text عادي
    public TMPro.TMP_Text buttonLabelTMP; // إذا الزر يستخدم TextMeshPro

    private CanvasGroup _canvasGroup;
    private RectTransform _panelRect;
    private bool _isVisible;
    private Coroutine _animCoroutine;

    private void Awake()
    {
        if (panel == null)
        {
            Debug.LogError("PanelToggle: لازم تربطي حقل Panel بالإنسبكتور!");
            return;
        }

        // تأكيد وجود CanvasGroup على اللوحة (لو مش موجود منضيفه تلقائياً)
        _canvasGroup = panel.GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = panel.AddComponent<CanvasGroup>();

        _panelRect = panel.GetComponent<RectTransform>();

        // ربط الزر بالحدث
        Button btn = GetComponent<Button>();
        if (btn != null)
            btn.onClick.AddListener(Toggle);
    }

    private void Start()
    {
        _isVisible = startVisible;
        SetPanelStateInstant(_isVisible);
    }

    public void Toggle()
    {
        _isVisible = !_isVisible;

        if (_animCoroutine != null)
            StopCoroutine(_animCoroutine);

        _animCoroutine = StartCoroutine(AnimatePanel(_isVisible));

        UpdateButtonLabel();
    }

    private IEnumerator AnimatePanel(bool show)
    {
        if (show)
        {
            panel.SetActive(true);
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
        }

        float startAlpha = _canvasGroup.alpha;
        float endAlpha = show ? 1f : 0f;

        Vector3 startScale = _panelRect.localScale;
        Vector3 endScale = show ? Vector3.one : new Vector3(0.9f, 0.9f, 1f);

        float t = 0f;
        while (t < animationDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / animationDuration);
            // Ease Out
            p = 1f - Mathf.Pow(1f - p, 3f);

            _canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, p);
            _panelRect.localScale = Vector3.Lerp(startScale, endScale, p);

            yield return null;
        }

        _canvasGroup.alpha = endAlpha;
        _panelRect.localScale = show ? Vector3.one : new Vector3(0.9f, 0.9f, 1f);

        if (!show)
        {
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            panel.SetActive(false);
        }
    }

    private void SetPanelStateInstant(bool show)
    {
        panel.SetActive(show);
        _canvasGroup.alpha = show ? 1f : 0f;
        _canvasGroup.interactable = show;
        _canvasGroup.blocksRaycasts = show;
        _panelRect.localScale = show ? Vector3.one : new Vector3(0.9f, 0.9f, 1f);
        UpdateButtonLabel();
    }

    private void UpdateButtonLabel()
    {
        string txt = _isVisible ? "X" : "=";
        if (buttonLabelLegacy != null) buttonLabelLegacy.text = txt;
        if (buttonLabelTMP != null) buttonLabelTMP.text = txt;
    }
}