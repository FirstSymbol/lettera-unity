# Motion

[Русская версия](Motion.ru.md) · [All guides](Lettera.md)

Text in Lettera can move, appear piece by piece, and answer a pointer and a keyboard. All of it runs on a **pose pass**: the vertices the last rebuild left are moved, tinted and faded each frame, with no parsing, shaping or layout, and nothing allocated. A text with nothing moving is not visited at all.

- [Motions](#motions)
- [Settings every motion has](#settings-every-motion-has)
- [Reveals: a typewriter and more](#reveals-a-typewriter-and-more)
- [Marquee](#marquee)
- [The sequencer](#the-sequencer)
- [Animator and parameter links](#animator-and-parameter-links)
- [Timeline](#timeline)
- [Ranges that answer](#ranges-that-answer)
- [Links and spoilers](#links-and-spoilers)
- [Range rules](#range-rules)
- [Inline sprites and objects](#inline-sprites-and-objects)
- [Semantics](#semantics)
- [Reduced motion](#reduced-motion)
- [Your own motion](#your-own-motion)
- [Stage hooks](#stage-hooks)
- [Limits](#limits)

## Motions

These tags are in the default vocabulary, so they work with nothing set up. Slots are optional and go in the order listed; an empty slot keeps the effect's value.

| Tag | Effect | What it does | Slots |
|---|---|---|---|
| `<wave>` | `WaveMotion` | Glyphs bob and sway | amplitude y, amplitude x, rate, step |
| `<wobble>` | `WobbleMotion` | Glyphs rock and squash | turn, squash, rate, step |
| `<shake>` | `ShakeMotion` | Glyphs jitter, holding still between jumps | amplitude, rate, turn |
| `<bounce>` | `BounceMotion` | Glyphs hop, never below where they rest | height, rate, step, share of the cycle in the air |
| `<float>` | `FloatMotion` | Glyphs drift on smooth noise | amplitude y, amplitude x, rate, step |
| `<spin>` | `SpinMotion` | Glyphs turn round | rate, step |
| `<pulse>` | `PulseMotion` | Glyphs grow and shrink | amount, rate, step |
| `<pendulum>` | `PendulumMotion` | Glyphs swing from their top edge | turn, rate, step |
| `<deform>` | `DeformMotion` | The outline flows | amplitude, scale, travel |
| `<glitch>` | `GlitchMotion` | Glyphs jerk and split into colour fringes in bursts | split, rate, chance, jerk |
| `<scramble>` | `ScrambleMotion` | Random symbols settle into the real text, in reading order | seconds, rate |
| `<roll>` | `RollMotion` | Characters turn on a wheel and settle on their value, as a counter does | seconds, start, lag |

Lengths are in em. **Rate** is cycles per second; **step** is how far behind its neighbour each glyph runs, in cycles, which is what turns a bob into a wave.

```text
<wave=0.3,0,2>Higher and faster</wave>  <shake=0.02,12>nervous</shake>  <roll=1.5>1 250 000</roll>
```

Motions combine: `<wave><pulse>both</pulse></wave>`. Selection, carets, hit tests and copied text stay where the layout put the text; only what is drawn moves. `<scramble>` and `<roll>` change what shows and leave the real characters in place for copying and screen readers.

## Settings every motion has

Every motion derives from `LetteraMotionEffect` and has:

| Property | What it does |
|---|---|
| `Unit` | What moves as one body: `Cluster`, `Word`, `Line` or the whole `Range`. |
| `Clock` | `Scaled` (follows `Time.timeScale`), `Unscaled`, or `Manual`, where `Phase` is the input. |
| `Phase` | Under a manual clock, the time; otherwise a start offset. |
| `Strength` | Scales the whole motion; 0 is still. |
| `Seed` | For motions with chance in them, so two texts do not move alike. |
| `ReducedMotionPolicy` | What happens when the player asks for less motion: `Rest` stops it, `Keep` lets it run. |

To change a running motion's numbers, set them and call `MarkEffectsDirty(DirtyLevel.Pose)`, the cheapest level:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionCalmDown : MonoBehaviour
{
    public LetteraText Title;
    WaveMotion m_Wave;

    void Start()
    {
        m_Wave = new WaveMotion { Amplitude = 0.25f, Rate = 1.5f, Unit = LetteraMotionUnit.Cluster };
        Title.Styles.Add(LetteraStyle.ForWholeText(m_Wave));
        Title.MarkStylesDirty();
    }

    void Update()
    {
        // Settles over four seconds.
        m_Wave.Strength = Mathf.Clamp01(1 - Time.timeSinceLevelLoad / 4);
        Title.MarkEffectsDirty(DirtyLevel.Pose);
    }
}
```

The inspector's Interaction card has **Preview Motion**, which runs motion in edit mode and writes nothing to the scene.

## Reveals: a typewriter and more

`RevealEffect` shows the leading part of what it covers. Its **reach** is the frontier: `100%` shows everything, `50%` half, and a bare number counts units. Its **unit** is `Cluster`, `Word`, `Line` or `Paragraph`. Its **entrance** says how each unit arrives: `Cut`, `Fade`, `Slide`, `Scale`, `Spin`, `Flip`, `Skew`, `Stretch`, `Spiral`, `Pop`, `Drop`, `Rain`, `Burst`, `Domino`, `Swing`, `Wave`, `Shake`, `Glitch`, `Chaos` or `Tint`, or a name from an entrance library.

In markup the slots are entrance, reach, unit: `<reveal=pop,40%,word>`. A typewriter moves the reach over time:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionTypewriter : MonoBehaviour
{
    public LetteraText Line;
    public float CharactersPerSecond = 20;
    RevealEffect m_Reveal;
    float m_Shown;

    void Start()
    {
        m_Reveal = new RevealEffect { Entrance = "fade", Reach = "0", Unit = LetteraRevealUnit.Cluster };
        m_Reveal.UnitShown += (start, end) => { /* a key click sound here */ };
        m_Reveal.Settled += () => Debug.Log("All shown");
        Line.Styles.Add(LetteraStyle.ForWholeText(m_Reveal));
        Line.Text = "The door was open, and nobody was home.";
    }

    void Update()
    {
        m_Shown += CharactersPerSecond * Time.deltaTime;
        m_Reveal.Reach = m_Shown.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        Line.MarkEffectsDirty(DirtyLevel.Pose);
    }

    public void Skip() => m_Reveal.SnapToTarget();
}
```

- `Reflow` off keeps the space of hidden text, so lines do not move as words arrive; on closes it up.
- `Independent` fills each range on its own instead of one frontier through all of them.
- Moving the reach back plays units out: an entry's **Leave** entrance, if it has one.
- `LetteraEntranceLibrary` assets hold named entrances with their timing and easing, for a project's own; **Project Settings > Lettera > Animation & Interaction > Default Entrance Library** makes one available to every text.

A sequencer or a Timeline clip can drive the reach too, with no code.

## Marquee

`MarqueeEffect` scrolls a Canvas text sideways through its box when it does not fit: a ticker. `Mode` is `Loop` or `BackAndForth`; `Speed`, `Gap` and `Pause` set its pace. It applies to the whole text; turn word wrap off so the line can run past the box.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionTicker : MonoBehaviour
{
    void Start()
    {
        var ticker = GetComponent<LetteraText>();
        ticker.WordWrap = false;
        ticker.Styles.Add(LetteraStyle.ForWholeText(new MarqueeEffect { Mode = LetteraMarqueeMode.Loop }));
        ticker.Text = "Breaking: the river is high, the bridge is closed, take the north road until Friday.";
    }
}
```

## The sequencer

`LetteraSequencer` ramps any published parameter of any effect of a text over one timeline, with no code: a **cue** takes a parameter from one value to another over its time, on the ranges you choose, staggered from one unit to the next. Everything goes through holds, so no field of an effect is ever written and stopping gives the markup's values back.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionIntro : MonoBehaviour
{
    void Start()
    {
        var title = GetComponent<LetteraText>();
        title.Styles.Add(LetteraStyle.ForWholeText(new WaveMotion { Amplitude = 0 }));   // Style 0.
        title.Text = "Welcome back";

        var sequencer = gameObject.AddComponent<LetteraSequencer>();
        sequencer.Cues.Add(new LetteraCue
        {
            Target = new LetteraParamTarget { Style = 0, Parameter = "Amplitude" },
            From = LetteraValue.Of(0),
            To = LetteraValue.Of(0.3f),
            UnitSeconds = 0.6f,     // How long each glyph's ramp takes.
            StepSeconds = 0.05f,    // How far each glyph starts after the one before.
            Curve = LetteraEase.OutBack,
        });
        sequencer.Repeat = LetteraRepeat.Retrace;
        sequencer.Cycles = 2;
        sequencer.Finished += () => Debug.Log("Intro done");
        sequencer.Play();
    }
}
```

- A cue's `Target` names a style of the text by index, a path down its composites (`Node`) and the parameter by name. The inspector's parameter picker lists them, saying number or colour.
- `Select` narrows a cue to some ranges: by label, primary value, token, overlap, `Skip` and `Take`.
- `Repeat` is `Restart`, `Retrace` (back the same way), `Return` or `Accumulate`; `Cycles` says how many passes, and a negative number repeats forever.
- `Play`, `Pause`, `Stop`, `Seek` and `Progress` drive it. Setting `Progress` shows exactly that moment, so an `AnimationCurve` or a slider can scrub it.
- **Tools > Lettera > Sequencer**, also a button on its inspector, shows cues on rows: drag and resize with snapping, split at the playhead, copy, paste, mute, zoom and scrub. Space plays.

## Animator and parameter links

An Animator can animate a text's `color` and alpha like any Graphic; an alpha curve costs only an alpha pass. For an effect's parameter, `LetteraParamLink` gives that one parameter a field (`Value`, `ColorValue` or `VectorValue`) that animation curves can bind to:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionLinkGlow : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        label.Styles.Add(LetteraStyle.ForWholeText(new GlowEffect { Radius = 0.1f }));   // Style 0.

        var link = gameObject.AddComponent<LetteraParamLink>();
        link.Target.Style = 0;
        link.Target.Parameter = "Radius";
        link.Value = 0.3f;   // An Animator can now record and play this field.
    }
}
```

## Timeline

With the Timeline package installed, **Add Track > Lettera** in the Timeline window offers three tracks. Nothing of this compiles without the package.

| Track | Binds to | Its clips |
|---|---|---|
| Text Track | A text's object | Make the text say the clip's words while the playhead is over the clip, optionally typed over the clip's length. The text's own value is untouched and comes back in a gap. |
| Sequence Track | A `LetteraSequencer` | Play the sequencer from one progress to another. |
| Parameter Track | A `LetteraParamLink` | Ramp the linked parameter from one value to another along a curve. |

Scrubbing inside a text clip does no rebuild. The Timeline sample builds a short cut scene with all three from code.

## Ranges that answer

A **range effect** (`LetteraRangeEffect`) makes its ranges answer a pointer and a keyboard. The shipped ones are `LinkEffect` (`<a>`, `<link>`), `SpoilerEffect` (`<spoiler>`) and `ActionEffect`, which only raises events: use it for mentions, hashtags and buttons of your own.

Events come through the text's router, for all ranges or for one topic:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionClickableWords : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        var action = new ActionEffect();
        label.Styles.Add(LetteraStyle.ForTag(action, "item"));
        label.MarkStylesDirty();
        label.Text = "You found <item=sword>a sword</item> and <item=key>a rusty key</item>.";

        action.Events.Activated += e => Debug.Log("Picked up " + e.Primary);

        LetteraRangeRouter router = LetteraRangeRouter.Of((Component)label);
        router.All.Entered += e => Debug.Log("Pointing at " + e.Primary);
        router.All.MenuRequested += e => e.Stop();
    }
}
```

- An event says which range (`Id`, `Start`, `End`, `Primary`, `Label`), how it came (`PointerKind`, `ViaKeyboard`, `Button`) and where. `Stop()` ends its delivery; `PreventDefault()` skips the effect's own action, such as opening a link.
- Overlapping ranges get one target, by priority, then style, then the shortest; events go down and back up through nested ranges.
- `HitPadding` widens a range's hit area; touch adds its own padding. A long press raises `PressHoldProgress` and `MenuRequested`.
- **The text's route** decides what a press on the rest of the text does: `Auto` takes it and passes what no range takes to the parents, `Ranges` lets presses through everywhere but the ranges, `Observe` lets the ranges and what lies behind both have it.
- **Keyboard:** Tab and Shift+Tab step through focusable ranges of a read-only text, Enter activates, and a focus ring shows when focus came from a key. `LetteraRangeNavigator` puts a text's ranges in Unity's UI navigation. In a field, Ctrl+Enter or Cmd+Enter follows the link at the caret.
- On read-only text a click activates and a drag selects; in a field a plain press places the caret and the modifier follows the link.
- `LetteraRangeTopic` assets group ranges from many texts, so one listener hears them all.

## Links and spoilers

`<a=https://unity.com>Unity</a>` draws as a link and opens its address when clicked; `<link=...>` opens it without the look. Addresses open through `LetteraLinks.Open`, which takes only the schemes in **Project Settings > Lettera > Animation & Interaction > Link Schemes** (`http`, `https`, `mailto` and `tel` by default) and refuses the rest with a warning; `www.` gains `https://`. On the web the page's own release handler opens it, so popup blockers let it through.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionLinkPolicy : MonoBehaviour
{
    void OnEnable() => LetteraLinks.Opening += Check;
    void OnDisable() => LetteraLinks.Opening -= Check;

    // Every link passes through here first.
    static void Check(LetteraLinkRequest request)
    {
        if (request.Url.Contains("example.org")) request.Refused = true;
    }
}
```

`LinkEffect.OpenOnActivate` off keeps links from opening, and its `Followed` event says when one was. `LetteraLinks.Opener` replaces how an address is opened.

`<spoiler>` draws a cover over text that is not drawn at all underneath, so nothing shows through; a tap lifts it. `CoverColor`, `FadeSeconds` and `CopyPolicy` (`Mask`, `Skip` or `Keep` what plain copy gets) set it up.

## Range rules

A `LetteraRangeRule` changes parameters of a range while a condition holds: on hover, press, focus or disabled, or on a signal of your own, with an envelope that eases in and out. It costs what the parameter's level costs, so a tint on hover costs only a pose:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionHoverTint : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        var tint = new RangeTintEffect { Color = new Color(1f, 0.55f, 0.2f, 1f) };
        var action = new ActionEffect();
        action.Rules.Add(new LetteraRangeRule
        {
            When = new SignalIs(LetteraSignals.Hovered),
            Envelope = LetteraEnvelopeKind.Transition,
            Enter = 0.1f,
            Exit = 0.3f,
            Changes = { new LetteraRangeChange { Target = tint, Parameter = "Weight", ToNumber = 1 } },
        });
        label.Styles.Add(LetteraStyle.ForTag(new CompositeEffect(action, tint), "hot"));
        label.MarkStylesDirty();
        label.Text = "Point at <hot>this</hot> or <hot>that</hot>.";
    }
}
```

Conditions combine with `AllOf`, `AnyOf` and `NotOf`; `ScalarAtLeast` reads a signal you publish. In the inspector a rule reads as a sentence, with chips for the states and pickers for the effect and parameter. In Play mode a text with ranges shows each range's state.

## Inline sprites and objects

`<sprite=name>` puts a sprite in the line as one character: it wraps, selects, copies as its alt text, and moves, fades and reveals with its neighbours. It is one quad in the text's own mesh. `<obj=name>` places a prefab, or adopts a scene object, where its character lands.

Names are looked up in the effect's own items, then its library, then the project's **Inline Library** in **Project Settings > Lettera > Animation & Interaction**. A `LetteraInlineLibrary` asset (**Assets > Create > Lettera > Inline Library**) holds entries with a sprite or object, alt text, size and lift in em, and a tint.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionInlineIcons : MonoBehaviour
{
    public Sprite Coin, Heart;

    void Start()
    {
        var label = GetComponent<LetteraText>();
        var icons = new InlineEffect();
        icons.Items.Add(new LetteraInlineEntry { Key = "coin", Sprite = Coin, Alt = "coins" });
        icons.Items.Add(new LetteraInlineEntry { Key = "heart", Sprite = Heart, Alt = "lives", Size = 1.2f });
        label.Styles.Add(LetteraStyle.ForMatcher(new InlineMediaMatcher("icon"), icons));
        label.MarkStylesDirty();
        label.Text = "<icon=coin> 250   <wave><icon=heart> 3</wave>";
    }
}
```

## Semantics

`SemanticEffect` gives a range a role (link, button, heading, mention, code, math and more), a label, a value and the actions it takes. `LetteraSemanticTree.Of(text)` describes a text's ranges for an assistive bridge of your own: roles, labels, states, screen rectangles and actions, kept up to date as ranges change. Links, formulas and inline items take the roles Link, Math and Image by themselves.

## Reduced motion

`LetteraAccessibility.ReducedMotion` is honoured everywhere: motions with `ReducedMotionPolicy` `Rest` stand still, reveals arrive at once, and sequencers with `HonorReducedMotion` jump to their end. It follows the system's preference until a script sets it, and on the web it follows the browser. `LetteraAccessibility.SteadyCaret` stops the caret blinking.

## Your own motion

Derive from `LetteraMotionEffect`. `Resolve` reads a range's numbers from its slots once; `PoseBody` moves one body each frame and must stay pure: no allocation and no Unity object. Name the slots that hold rate and step, and the phase `Phi` comes ready with both applied:

```csharp
using System;
using FirstSymbol.Lettera;
using UnityEngine;

// Glyphs lean left and right like grass in the wind. Slots: lean in degrees, rate, step.
[Serializable]
public class MotionGrassMotion : LetteraMotionEffect
{
    [SerializeField] float m_Lean = 10, m_Rate = 0.6f, m_Step = 0.12f;

    protected override int RateSlot => 1;
    protected override int StepSlot => 2;

    public override void Resolve(in LetteraSlots slots, ref LetteraMotionValues values)
    {
        values.V0 = Slot(slots, 0, m_Lean);
        values.V1 = Slot(slots, 1, m_Rate);
        values.V2 = Slot(slots, 2, m_Step);
    }

    public override void PoseBody(in LetteraMotionContext context, ref LetteraGlyphPose pose)
    {
        float lean = context.Values.V0 * Mathf.Sin(context.Phi * Mathf.PI * 2);
        pose.Turn(lean, context.Box.At(0.5f, 0));   // About the glyph's foot.
    }

    public override float MaxReachEm(in LetteraMotionValues values) => 0.3f;
}

public class MotionUseGrass : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        label.Styles.Add(LetteraStyle.ForTag(new MotionGrassMotion(), "grass"));
        label.MarkStylesDirty();
        label.Text = "<grass>A field of tall letters</grass>";
    }
}
```

`MaxReachEm` tells culling and masks how far a glyph can leave its place. `LetteraGlyphPose` has `Move`, `Turn`, `Scale` and `Skew` about any point, and `Alpha` and `Tint`.

## Stage hooks

An effect can join any stage of a rebuild: override `Stages` with the flags it wants (`Analyzed`, `Shaped`, `LinesBroken`, `Placed`, `Mesh`, `Pose`) and the matching `OnAnalyzed`, `OnShaped`, `OnLinesBroken`, `OnPlaced`, `OnMesh` and `OnPose`. Each gets a view of that stage's data for its range, valid during the call only. An effect that asks for none costs nothing.

## Limits

- A hit test stays at the rest position: a bouncing link's area does not move with it. A world text whose transform moves re-merges its whole batch. Marquee is Canvas only.
- `<scramble>` and `<roll>` collapse layers while they run and replace whole clusters, so joined letters stand apart until they settle.
- A selection holding an inline item copies as plain text, so it does not paste back as the item. Layers such as stroke and shadow skip sprites; rotated atlas sprites and a sprite's turn are not applied. An object spawns only in Play mode or during a preview. A spoiler's underline draws under its cover.
- Stage hooks: `OnAnalyzed` runs after shaping, so hiding a cluster does not reshape its neighbours. `OnPlaced` cannot add a glyph. Built-in motions do not run through `OnPose`.
- A rule's target inside a style preset is not followed into a text's own copy of shared styles: such a rule finds nothing.
- Not done: custom envelopes, gesture recognizers on a range, and topic filters on queries.
- The Auto route takes the whole rect; text behind it gets a press only with `Ranges` or `Observe`. `Observe` and parent `Button` and `ScrollRect` paths were tried by calling handlers, not with a real input module.
- With many parts, a long text's alpha through `CrossFadeAlpha` reaches the first part only until its next rebuild, and a part's motion rewrites its whole mesh.
- A long text with a marquee, a reveal or an inline object places every line, not only those near the view.
- A managed stripping `High` player was not run with effects reached only through serialized references; those types carry `[Preserve]`.
