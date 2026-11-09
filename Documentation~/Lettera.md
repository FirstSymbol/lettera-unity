# Lettera

[Русская версия](Lettera.ru.md)

Lettera is a text engine for Unity. It draws text on a Canvas, in world space and as solid 3D geometry, and it reads every script the way a browser does: OpenType shaping through HarfBuzz, right-to-left and mixed-direction lines, Unicode line breaking and clusters, colour emoji. With no font assigned, a text takes the operating system's own fonts, so Arabic, Hindi, Chinese and emoji work in an empty project.

What a range of text looks like is set by markup. A tag means something only because a style binds it to an effect, and the default vocabulary already binds more than sixty: weight, colour, gradients, strokes, shadows, motion, links, formulas. You can add your own tags, or find ranges by other means, such as Markdown or `@mentions`.

On top of that:

- **Fields.** Any text can be selected, and any text can be an input field, with rules for what goes in.
- **Motion.** Text that waves, types itself or reacts to the pointer, with no rebuild and nothing allocated per frame.
- **Documents.** Text of any length through one window near the view.
- **Formulas.** A subset of TeX inside a sentence, and LaTeX tables.
- **3D text.** Real lit geometry with bevels, for logos and titles.

## Install

Open **Window > Package Manager**, press **+**, choose **Add package from git URL...** and paste:

```text
https://github.com/FirstSymbol/lettera-unity.git
```

To pin a version, add its tag:

```text
https://github.com/FirstSymbol/lettera-unity.git#1.1.0
```

Lettera needs Unity 6.3 LTS (6000.3) or newer. The Package Manager brings its dependencies (uGUI, Burst, Collections, Mathematics). A first open of the project prints one line in the console pointing to **Tools > FirstSymbol > Lettera > Getting Started**, which lists these guides and imports the samples.

## Five minutes

**1. A text on screen.** In the Hierarchy choose **GameObject > Lettera > Text**. Lettera makes a Canvas and an EventSystem if the scene has none. Type in the **Text** field of the inspector:

```text
Hello, <b>world</b> · مرحبا · 你好 👋
```

Every script and the emoji draw at once, from the system's fonts.

**2. Text from a script.** Add this component next to the text and press Play:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class IndexScore : MonoBehaviour
{
    LetteraText m_Label;
    int m_Score;
    float m_Next;

    void Start()
    {
        m_Label = GetComponent<LetteraText>();
        m_Label.FontSize = 36;
        m_Label.Text = "<b>Score</b>: <color=#FC0>0</color>";
    }

    void Update()
    {
        if (Time.time < m_Next) return;
        m_Next = Time.time + 0.5f;
        m_Label.SetText("Score: " + ++m_Score);
    }
}
```

`Text` is the saved value. `SetText` is for values that change at run time: it leaves the saved text alone, and characters the text has shown before cost nothing new.

**3. Your own font.** Select a `.ttf`, `.otf` or `.ttc` file in the Project window and choose **Assets > Create > Lettera > Font Asset From Selection**. Drag the new asset to the text's **Font** field. Characters the font lacks still come from the system.

**4. A look.** Write `<stroke=#000,0.08><shadow=#0008,0.05,-0.05>Title</shadow></stroke>`, or open the inspector's **Styles** card, press **+ Add Style** and pick **Fill**, then a gradient.

**5. Motion.** Write `<wave>hello</wave>`. It moves in Play mode, and in edit mode after **Preview Motion** in the inspector's Interaction card.

Next, [Getting Started](GettingStarted.md) walks through the same ground in more depth, one task at a time.

## The guides

| Guide | What it covers |
|---|---|
| [Getting Started](GettingStarted.md) | Text on screen, fonts, markup, styles, fields, motion, documents, math and 3D, in that order. |
| [Fonts](Fonts.md) | Font assets, stacks, system fonts, weights and variable fonts, memory, subsets, pixel fonts, colour fonts, the web. |
| [Styles](Styles.md) | How tags become looks: matchers and effects, presets, paints, layers, holds, your own effect and shader. |
| [Fields](Fields.md) | Selection, editing, rules, the clipboard, composition, the platform's own controls and the gates for them. |
| [Motion](Motion.md) | Motions, reveals, the sequencer, Timeline, ranges that answer a pointer, inline sprites, accessibility. |
| [Documents](Documents.md) | Very long text: the rope, the window, the scroller, search, word dictionaries. |
| [Math](Math.md) | Formulas in text and by themselves, chemistry, LaTeX tables. |
| [3D Text](Text3D.md) | Solid lit text: edges, materials, looks, layers, deformers, colliders. |
| [Tools](Tools.md) | Every window, the overlay, editing in the scene, the palette and the keys. |
| [Migration](Migration.md) | Moving a project from TextMesh Pro. |
| [Comparison](Comparison.md) | Lettera and TextMesh Pro, measured. |
| [Platforms](Platforms.md) | What has run where, plainly. |
| [Agent Guide](AgentGuide.md) | Driving Lettera from the command line, for a coding agent. English only. |

## Samples

**Window > Package Manager > Lettera > Samples** holds thirteen samples. Each is built from code: import it, add its component to an empty object in an empty scene, and press Play. The [README](../README.md#samples) lists what each shows.

## Help and licence

Lettera is MIT licensed: free for commercial and non-commercial use. See [LICENSE](../LICENSE.md) and the [third-party notices](../THIRD-PARTY-NOTICES.md). What changed in each version is in the [changelog](../CHANGELOG.md).
