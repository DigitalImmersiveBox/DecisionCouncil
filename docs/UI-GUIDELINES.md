# dib desktop UI guidelines

The shared look for Digital Immersive Box desktop apps (WPF). It is taken from **Session Deck** (`Claude_Helper/ClaudeSessions`), the reference app, and packaged as one resource dictionary: [`src/DecisionCouncil.App/Theme/DibDark.xaml`](../src/DecisionCouncil.App/Theme/DibDark.xaml).

Each app keeps its own copy of the theme. There is no shared package, and Session Deck is the reference: when its look changes, bring the change into the apps' copies by hand.

**To use it in another app:** copy `DibDark.xaml` (and this file), merge it in `App.xaml`, call the dark title bar helper on every window (`WindowTheme.UseDarkTitleBar`), and follow the rules below.

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceDictionary Source="Theme/DibDark.xaml"/>
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

## Principles

- **Dark, calm, dense.** The app sits next to code and terminals. The surfaces are near-black greys, and colour is kept for meaning.
- **Colour means status, not decoration.** The same five status colours mean the same thing in every app.
- **One orange button per view.** Claude orange marks the single main action (Start, Save, Done). Everything else uses the neutral grey button.
- **Labels whisper, content speaks.** Section labels are small, uppercase and grey. The content is the brightest thing on screen.

## Tokens

### Surfaces

| Token | Hex | Use |
| --- | --- | --- |
| `Bg.Window` | `#141518` | Window background |
| `Bg.Panel` | `#121316` | Left side panel, code editors |
| `Bg.Banner` | `#17181C` | Banner strip with big figures |
| `Bg.Card` | `#1D1F24` | Cards |
| `Bg.CardHover` | `#23262C` | Clickable card on hover |
| `Bg.Dialog` | `#1E1F23` | Dialogs, dropdowns, tooltips |
| `Bg.Input` | `#141518` | Text boxes, segment hosts |
| `Bg.Button` | `#2C2D33` | Neutral button |
| `Bg.Hover` / `Bg.Pressed` / `Bg.Checked` | `#24262B` / `#30333A` / `#3A3D45` | Hover, pressed or checked pill, checked segment |
| `Bg.Selected` | `#24324A` | Selected list row |
| `Bg.Tag` | `#262930` | Neutral tag |

### Lines

| Token | Hex | Use |
| --- | --- | --- |
| `Line.Divider` | `#22252A` | Header, footer and panel edges |
| `Line.Card` | `#292C33` | Card border |
| `Line.Input` / `Line.InputHover` | `#3A3D45` / `#4A4E58` | Input border at rest and on hover |
| `Line.Section` | `#2C2F36` | Divider between sections inside a card |

### Text

| Token | Hex | Use |
| --- | --- | --- |
| `Text.Primary` | `#E6E7EA` | Titles, values, button text |
| `Text.Body` | `#D9DBE0` | Long text (answers, descriptions) |
| `Text.Secondary` | `#A9ACB4` | Subtitles, hints, inactive pills |
| `Text.Meta` | `#9A9DA6` | Card meta line (project, date) |
| `Text.Label` | `#7D808A` | UPPERCASE section labels, timestamps |
| `Text.Faint` | `#6E717B` | Least important text (file names, branches) |
| `Text.OnStatus` | `#111215` | Text on a status chip |

### Accents and status

| Token | Hex | Meaning |
| --- | --- | --- |
| `Accent.Primary` | `#C15F3C` | Claude orange: the main action, checked checkboxes |
| `Accent.PrimaryLight` | `#D97757` | Selected outline, Claude marker dot |
| `Accent.Link` | `#60A5FA` | Links, focused input border |
| `Status.NeedsYou` | `#FF7A59` | Needs the user now: errors, reviews due, no-go |
| `Status.Check` | `#F5B83D` | Look at this: warnings, open items, retrying |
| `Status.Good` | `#4ADE80` | Finished and good: done, decided, go |
| `Status.Working` | `#60A5FA` | In progress |
| `Status.Done` | `#6B707B` | Finished and closed, cancelled |
| `Status.Idle` | `#4A4E57` | Waiting, not started |

## Type

- **Segoe UI 13**, `TextFormattingMode="Display"`, `UseLayoutRounding="True"` on every window.
- **Title** (app name, page title): 17 SemiBold.
- **Subtitle**: 12 in `Text.Secondary`, directly under the title.
- **Heading** (section label): 11 SemiBold, UPPERCASE, `Text.Label`.
- **Hint**: 11.5 in `Text.Secondary`.
- **Figure** (one key number): Segoe UI Light 34 to 42, with the unit at a smaller size beside it.
- **Icons**: Segoe Fluent Icons (falling back to Segoe MDL2 Assets), 14 px in icon buttons.
- **Code and editors**: Cascadia Mono, falling back to Consolas.

## Layout

- **Header bar** across the top: the app title and a one-line summary with coloured dots (`● 4 need you ● 2 working`). On the right: a status block with UPPERCASE labels and a divider on its left, then icon buttons. A `Line.Divider` runs underneath.
- **Left panel**, 220 px wide on `Bg.Panel`: an UPPERCASE heading, then rows with an icon, a name and a small count on the right. The selected row is `Bg.Selected`.
- **Footer**: the dib logo (`DibLogo` geometry, 11 px, `#8A8D96`) and "made by Digital Immersive Box" (11 px, `Text.Label`).
- **Spacing**: pages have 20 px side margins and 16 px at the top. Cards are 8 to 12 px apart, with 14 to 16 px of padding inside.
- **Cards** have an 8 px radius and a 1 px border. A card that stands for an item (a session or an agent) gets a **4 px stripe** on its left edge in its status or identity colour.

## Components

| Element | Style key | Notes |
| --- | --- | --- |
| Neutral button | implicit `Button` | Grey, 6 px radius, padding 14,6,14,7 |
| Main action | `PrimaryButton` | Orange, at most one per view. Turns grey when disabled |
| Destructive or cancel | `DangerButton` | Grey with an orange-red label |
| Text action | `LinkButton` | Blue text, no background |
| Header icon | `IconButton` | 32×30, transparent until hover |
| Filter or preset pill | `ChipToggle` | 10 px radius; checked or hovered pills get a grey fill |
| Two to five choices | `SegmentHost` + `Segment` | Like °C / °F or On / Off. Use these instead of a dropdown for short fixed lists |
| Status | `Chip` | Status colour as the background, dark SemiBold 11 px text |
| Neutral tag | `Tag` | Counts, labels, "demo" |
| Note or warning | `Callout` | Card with a 3 px left stripe; set `BorderBrush` to a status colour |
| Section divider | `Divider` | 1 px line inside a card |
| Inputs | implicit `TextBox`, `ComboBox`, `CheckBox` | Dark, with a blue border on focus. A checked checkbox is orange |
| Lists | implicit `ListBox` / `ListBoxItem` | Rows with a 6 px radius, hover `Bg.Hover`, selected `Bg.Selected` |
| Tabs | implicit `TabControl` / `TabItem` | Drawn as pills, not as folder tabs |
| Bars | implicit `ProgressBar` | 4 to 5 px high and rounded. Track `#33363D`, fill `#C9CACF` or a status colour |
| Scrollbars | implicit `ScrollBar` | 8 px wide, thumb only |
| Dialogs | code: dark window on `Bg.Dialog` | UPPERCASE heading, message, buttons on the right, dib logo on the left. No system MessageBox |

## Life: mascots and motion

Session Deck feels alive because of its pixel mascot and its banner scene. Apps should carry the same spirit, kept small and purposeful.

- **Pixel characters show state.** Use them for things that work for the user: agents, sessions, jobs. Draw them on a pixel grid (3 px on a banner, 2 px inline), tinted with the item's colour, with eyes 1 cell wide and 2 tall. The states are the same everywhere:
  - **Waiting:** an occasional bob and a blink.
  - **Working:** a quick bob, tapping feet and a white thought bubble whose dots fill up.
  - **Retrying:** an amber bubble with "!".
  - **Done:** a jump with sparks, happy `^ ^` eyes and blush, then a flag in the result's status colour.
  - **Failed:** X eyes.
  - **Off, cancelled or skipped:** dimmed and dozing, with a "z" that drifts up.
- **The dib mascot** (the logo with legs) stands for the app itself: in the header, on empty states, and as the one who sums things up.
- **Banners can be a little scene.** Put the big figure on the left and the characters on a floor line, against a dim twinkling starfield.
- **Motion is short and eased.** Pages fade and rise 10 px in 200 ms. New content fades in when it arrives. A status chip breathes (its opacity pulses) while that item is working. Nothing loops faster than once every 0.4 s.
- **Respect "Show animations".** When Windows animations are off (`SystemParameters.ClientAreaAnimation`), everything stands still, the characters included.
- **One shared clock** (about 12 fps for pixel art) drives every character. It ticks only while one is visible.
- Pixel art uses `EdgeMode.Aliased`, so there are no seams between pixels.

## Do and don't

- **Do** use the dark title bar on every window (`DwmSetWindowAttribute` 20). A light frame around dark content looks broken.
- **Do** write labels in sentence case, with only section headings in UPPERCASE.
- **Don't** use a colour for decoration only. If something is orange, it needs the user.
- **Don't** use light system controls such as `DatePicker`, `Calendar` or `MessageBox`. Use pill choices and dark dialogs instead.
- **Watch out:** some WPF system styles (`TabControl`, `GroupBox`, `Label`) set a black text colour. Give any such control `Foreground="{StaticResource Text.Primary}"` in its style, or its contents turn dark.
