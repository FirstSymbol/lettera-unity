using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// A camera sweeping over a lit ground: a line per edge shape, each look, a bidirectional line, a wave, a counter with warm digits, a formula, 500 shared labels with a live count, and solid text in a Canvas through a render texture.
    /// Add this to an empty object in an empty scene, assign a material with the Lettera/Solid Lit shader, and press Play.
    /// </summary>
    public class LetteraText3DSample : MonoBehaviour
    {
        [Tooltip("A material with the Lettera/Solid Lit shader. Without one, solid text has nothing to draw with in a build.")]
        public Material Material;
        [Tooltip("Your own looks, in place of the five this sample makes: Plain, Gold, Chrome, Candy and Stone.")]
        public LetteraSolidLook[] Looks;
        [Tooltip("Shows many identical labels, sharing one mesh.")]
        public int SharedLabels = 500;

        Camera m_Camera;
        LetteraText3D m_Counter;
        LetteraText m_Stats;
        float m_Spin;
        int m_Count;

        void Start()
        {
            RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.4f);
            m_Camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0.15f, 0.16f, 0.19f);
            m_Camera.farClipPlane = 500;
            Update();

            var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.position = new Vector3(0, -18, 0);
            ground.transform.localScale = new Vector3(30, 1, 30);
            if (Material != null)
            {
                var groundMaterial = new Material(Material);
                groundMaterial.SetColor("_BaseColor", new Color(0.45f, 0.46f, 0.5f));
                ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            }

            float y = 26;
            foreach (LetteraBevelShape shape in System.Enum.GetValues(typeof(LetteraBevelShape)))
            {
                if (shape == LetteraBevelShape.Custom) continue;
                LetteraText3D line = Word(shape + " Edge", new Vector3(-22, y, 0), new Color(0.95f, 0.72f, 0.3f));
                LetteraBevel bevel = LetteraBevel.Default;
                bevel.Shape = shape;
                bevel.Width = 0.04f;
                bevel.Height = 0.04f;
                line.FrontBevel = bevel;
                line.Depth = 0.25f;
                y -= 4;
            }

            if ((Looks == null || Looks.Length == 0) && Material != null) Looks = MakeLooks();
            if (Looks != null)
            {
                y = 26;
                foreach (LetteraSolidLook look in Looks)
                {
                    if (look == null) continue;
                    LetteraText3D line = Word(look.name, new Vector3(22, y, 0), Color.white);
                    look.ApplyTo(line);
                    y -= 4;
                }
            }

            Word("مرحبا Hello שלום", new Vector3(0, -2, 0), new Color(0.45f, 0.85f, 0.55f));
            Word("<wave>Each letter moves</wave>", new Vector3(0, -6, 0), new Color(0.5f, 0.75f, 1));
            m_Counter = Word("0", new Vector3(0, -10, 0), new Color(1, 0.5f, 0.45f));
            m_Counter.WarmCharacters = "0123456789";
            Word("<u>Underlined</u>, and <depth=3>deeper</depth>", new Vector3(0, -14, 0), new Color(0.9f, 0.9f, 0.92f));
            Word("<math>\\frac{a}{\\sqrt{b+1}} + \\sum_{i=0}^n i</math>", new Vector3(0, 8, 0), new Color(0.95f, 0.85f, 0.4f));

            // Many of the same text share one mesh, which is what lets them instance.
            var labels = new GameObject("Labels").transform;
            for (int i = 0; i < SharedLabels; i++)
            {
                LetteraText3D label = Word("Label", new Vector3(-60 + i % 25 * 5, -17, 20 + i / 25 * 3), new Color(0.7f, 0.72f, 0.78f), 1.2f);
                label.transform.SetParent(labels, true);
            }

            BuildCanvas();
        }

        // A look is an asset in a real project, made with Create > Lettera > Solid Look. These are made here so that the sample brings none into a build.
        LetteraSolidLook[] MakeLooks()
        {
            LetteraSolidLook Look(string name, Color color, float metallic, float smoothness, LetteraBevelShape shape, float depth)
            {
                var look = ScriptableObject.CreateInstance<LetteraSolidLook>();
                look.name = name;
                look.Depth = depth;
                var material = new Material(Material) { name = name };
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", smoothness);
                LetteraBevel bevel = LetteraBevel.Default;
                bevel.Shape = shape;
                bevel.Width = 0.03f;
                bevel.Height = 0.03f;
                look.FrontBevel = bevel;
                look.FaceMaterial = material;
                return look;
            }
            return new[]
            {
                Look("Plain", new Color(0.85f, 0.85f, 0.88f), 0, 0.3f, LetteraBevelShape.None, 0.15f),
                Look("Gold", new Color(1, 0.76f, 0.3f), 1, 0.75f, LetteraBevelShape.Round, 0.2f),
                Look("Chrome", new Color(0.9f, 0.92f, 0.95f), 1, 0.95f, LetteraBevelShape.Chamfer, 0.2f),
                Look("Candy", new Color(1, 0.35f, 0.55f), 0, 0.85f, LetteraBevelShape.Round, 0.3f),
                Look("Stone", new Color(0.5f, 0.5f, 0.48f), 0, 0.1f, LetteraBevelShape.Step, 0.25f),
            };
        }

        LetteraText3D Word(string text, Vector3 position, Color color, float size = 3)
        {
            var go = new GameObject("Text 3D", typeof(RectTransform));
            go.transform.position = position;
            ((RectTransform)go.transform).sizeDelta = new Vector2(40, size * 2);
            LetteraText3D solid = go.AddComponent<LetteraText3D>();
            if (Material != null) solid.FaceMaterial = Material;
            solid.FontSize = size;
            solid.WordWrap = false;
            solid.Color = color;
            solid.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            solid.Text = text;
            return solid;
        }

        // Solid text does not draw as a Graphic: a Canvas has no light. It draws through a camera of its own, and an image shows that.
        void BuildCanvas()
        {
            var target = m_Target = new RenderTexture(512, 128, 24);
            var far = new Vector3(0, 1000, 0);
            var inner = new GameObject("Canvas Camera", typeof(Camera)).GetComponent<Camera>();
            inner.transform.position = far + new Vector3(0, 0, -6);
            inner.clearFlags = CameraClearFlags.SolidColor;
            inner.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 1);
            inner.targetTexture = target;
            Word("In A Canvas", far, new Color(0.6f, 0.9f, 1f), 4);

            var canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var image = new GameObject("Render Texture", typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform)image.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(12, 12);
            rect.sizeDelta = new Vector2(256, 64);
            image.GetComponent<RawImage>().texture = target;

            var stats = new GameObject("Stats", typeof(RectTransform));
            var statsRect = (RectTransform)stats.transform;
            statsRect.SetParent(canvas.transform, false);
            statsRect.anchorMin = statsRect.anchorMax = statsRect.pivot = new Vector2(0, 1);
            statsRect.anchoredPosition = new Vector2(12, -12);
            statsRect.sizeDelta = new Vector2(600, 40);
            m_Stats = stats.AddComponent<LetteraText>();
            m_Stats.FontSize = 18;
        }

        RenderTexture m_Target;

        void OnDestroy()
        {
            if (m_Target != null) m_Target.Release();
        }

        void Update()
        {
            m_Spin += Time.deltaTime * 0.5f;
            float angle = Mathf.Sin(m_Spin) * 22 * Mathf.Deg2Rad;
            m_Camera.transform.position = new Vector3(Mathf.Sin(angle) * 62, 14, -Mathf.Cos(angle) * 62);
            m_Camera.transform.LookAt(new Vector3(0, 5, 0));
            if (m_Counter != null && Time.frameCount % 6 == 0) m_Counter.SetText((++m_Count).ToString());
            if (m_Stats != null && Time.frameCount % 30 == 0) m_Stats.SetText($"{Time.smoothDeltaTime * 1000:0.0} ms a frame, {SharedLabels} labels on one mesh");
        }
    }
}
