using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public static class AutoProgressIntroOverlay
{
    public static Task WaitUntilDismissed(string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        var root = new GameObject("AutoProgressIntroRoot");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        root.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(root.transform, false);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;

        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.08f, 0.08f, 0.1f, 0.94f);
        panelImg.raycastTarget = true;

        var textGo = new GameObject("Message", typeof(RectTransform));
        textGo.transform.SetParent(panel.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.08f, 0.45f);
        trt.anchorMax = new Vector2(0.92f, 0.72f);
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        var txt = textGo.AddComponent<Text>();
        txt.text = message;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.fontSize = 42;
        txt.color = Color.white;
        txt.font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 42);

        var btnGo = new GameObject("Proceed", typeof(RectTransform));
        btnGo.transform.SetParent(panel.transform, false);
        var brt = btnGo.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.35f, 0.28f);
        brt.anchorMax = new Vector2(0.65f, 0.38f);
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;

        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(0.22f, 0.45f, 0.85f, 1f);
        btnImg.sprite = Sprite.Create(Texture2D.whiteTexture,
            new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
            new Vector2(0.5f, 0.5f), 100f);

        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnImg;

        var btnLabelGo = new GameObject("Label", typeof(RectTransform));
        btnLabelGo.transform.SetParent(btnGo.transform, false);
        var lrt = btnLabelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        var btnTxt = btnLabelGo.AddComponent<Text>();
        btnTxt.text = "Continue";
        btnTxt.alignment = TextAnchor.MiddleCenter;
        btnTxt.fontSize = 36;
        btnTxt.color = Color.white;
        btnTxt.font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 36);

        btn.onClick.AddListener(() =>
        {
            if (tcs.Task.IsCompleted) return;
            tcs.TrySetResult(true);
            Object.Destroy(root);
        });

        return tcs.Task;
    }
}
