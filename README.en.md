# GoPilot

**The control panel for the Lenovo Legion Go S, inside Xbox Game Bar.**
Performance, display, controller, lights, Legion buttons and per-game profiles in a widget built for the controller,
without leaving your game. It replaces Legion Space: lighter, faster and with more options.

[**Download the latest version**](https://github.com/dlavia/GoPilot/releases/latest) · [MIT](LICENSE) license · [Español](README.md)

<p align="center">
  <img src="docs/img/pestanas.gif" width="340" alt="GoPilot in Xbox Game Bar: switching between the Home, Power, Display, Device and Settings tabs">
</p>

> Personal project, not affiliated with Lenovo, AMD or Microsoft. Tested on a Legion Go S **Z1 Extreme**
> (model 83N6, BIOS S0CN27WW) with Windows 11. Use it at your own risk: it changes BIOS settings (TDP, fan, charging)
> and controller firmware settings, always within the limits the firmware itself allows.
>
> **The interface is currently in Spanish.** The names below give the Spanish label in brackets where it helps.

---

## Contents

- [Installation](#installation) · [Updating](#updating) · [Uninstalling](#uninstalling)
- [How to use it](#how-to-use-it)
- [Features](#features): [Home](#home-inicio) · [Power](#power-potencia) · [Display](#display-pantalla) · [Device](#device-equipo) · [Settings](#settings-ajustes)
- [Per-game profiles](#per-game-profiles) · [Legion buttons](#legion-buttons) · [In-game overlay](#in-game-overlay)
- [Privacy](#privacy) · [FAQ](#faq) · [For developers](#for-developers)

---

## Installation

1. Download `GoPilot-<version>.zip` from [Releases](https://github.com/dlavia/GoPilot/releases/latest) and extract it.
2. Double-click **`Instalar GoPilot.cmd`** and accept the administrator prompt.
   Windows warns because the installer comes from the internet: "Windows protected your PC" › **More info** ›
   **Run anyway** (and the same for PowerShell).
3. Press **Legion R**: GoPilot opens. It is also in Game Bar (Win + G) as the first icon of the bar.

When installed, GoPilot **replaces Legion Space**: it disables it (without uninstalling it) and takes its place on the
Legion buttons and in Game Bar. To get it back: Settings › Legion buttons › Legion Space, or uninstall GoPilot.

> The package is signed with its own certificate, which the installer adds as trusted. With **Smart App Control**
> turned on, Windows will not install it.

## Updating

GoPilot tells you when a new version is out: a notice at the top of Home and a dot on the Settings tab.
**Settings › Updates** (Actualizaciones) shows what's new and installs it with **Update** (about 20 seconds; Game Bar
closes for a moment and GoPilot comes back with your settings and profiles). Only packages signed with the same
certificate are installed; if anything fails, you keep the version you had.

## Uninstalling

**Settings › Updates › Uninstall GoPilot** (press twice), or run `uninstall.ps1` as administrator.
It leaves the device as it was before GoPilot: Legion Space comes back with its Game Bar icon, the touchpad and touch
screen are re-enabled if they were off, and the service, data, scheduled tasks and certificate are removed.

---

## How to use it

- **Legion R** opens and closes GoPilot (hold: Task View). **Legion L** opens the Xbox app (hold: Game Bar).
- **LT / RT** switch tabs: Home · Power · Display · Device · Settings.
- **D-pad** to move, **A** to select, **B** to go back. Sliders are entered with A, adjusted with the D-pad and left
  with A or B (so they don't move by accident while scrolling).
- **Footer**: always visible at the bottom, the **applied profile** (A opens the list to change it) and live data:
  game FPS, temperature, **real power draw in watts** and fan speed.
- Everything also works with the touch screen, with navigation sounds like the Xbox app's.

---

## Features

### Home (Inicio)

<img src="docs/img/inicio.png" width="260" align="right" alt="Home tab: brightness, volume and quick tiles">

- **Brightness and volume** always at hand.
- **Editable quick tiles** ("Editar baldosas"): remove (X or hold A), add (+) and reorder (A on one tile, then A on
  another). 15 are shown by default; there are **35** to choose from:

| Group | Tiles |
|---|---|
| Performance | Power mode · CPU Boost · Max fan · Fan curve |
| Display | Refresh rate (Hz) · VRR · Lossless Scaling · AFMF · RSR · Chill · Sharpening · Anti-Lag · Vari-Bright |
| Controller | Lights · Gyro · Vibration · Touchpad vibration · Controller auto-off · Polling rate · D-pad 8/4 |
| Touch & system | Touchpad · Touch screen · On-screen keyboard · Close game · Charge to 80 % |
| GoPilot | Overlay · Footer data · Theme · Animations · Sounds · Legion buttons · Legion Space · Auto profiles · Auto-save · Re-apply after sleep |

- **Close game**: closes the foreground game (press twice), handy if it hangs.
- **Update notice** at the top when a new version is out.

<br clear="right">

### Power (Potencia)

<img src="docs/img/potencia.png" width="260" align="right" alt="Power tab: modes with their watts, CPU Boost and fan">

- **Lenovo modes** with their real watts, which the firmware changes depending on whether it is plugged in or on
  battery (sustained / bursts / peaks):

| Mode | Plugged in | On battery |
|---|---|---|
| Quiet (Silencio) | 10 / 15 / 20 W | 10 / 10 / 20 W |
| Balanced (Equilibrado) | 16 / 25 / 30 W | 15 / 15 / 30 W |
| Performance (Rendimiento) | 30 / 33 / 40 W | 25 / 25 / 35 W |

- **Manual, Steam Deck style**: a single **power limit** (5–30 W) with the real power draw shown below it. Bursts and
  peaks follow automatically above it (× 1.25 and × 1.5, up to 32 and 41 W); under **Advanced** they can be set by hand
  (SPL, sPPT and fPPT). It applies when you let go of the slider, no button needed.
- **Legion L + Y** cycles modes (done by the firmware) and GoPilot shows a large notice with the mode and its watts.
- **CPU Boost**: turning it off lowers temperature and power draw with little difference in many games.
- **Max fan** and a custom **fan curve** (in Manual mode).
- Everything is re-applied after sleep (the firmware sometimes resets its values).

<br clear="right">

### Display (Pantalla)

<img src="docs/img/pantalla.png" width="260" align="right" alt="Display tab: resolution, refresh rate and AMD Radeon settings">

- **Resolution** and **refresh rate** (up to 120 Hz).
- **VRR (FreeSync)**: turning it on also enables the Windows setting for windowed and borderless games.
  Verified with PresentMon: the screen follows the game frame by frame below 120 FPS.
- **AMD Radeon** (without opening Adrenalin):

| Feature | What it does |
|---|---|
| AFMF | AMD frame generation: more perceived FPS in almost any game |
| RSR | AMD upscaling: play at a lower resolution and scale to the screen |
| Chill | Keeps FPS between a minimum and a maximum to save battery and heat |
| Sharpening | Sharpens the image (useful with RSR or low resolutions) |
| Anti-Lag | Reduces the delay between the controller and the screen |
| Vari-Bright | AMD's automatic brightness to save battery |

- **Lossless Scaling**: turn its scaling on or off with the game in front (state read from its real window).

<br clear="right">

### Device (Equipo)

<img src="docs/img/equipo-avanzado.png" width="260" align="right" alt="Device tab: controller settings and advanced settings">

- **Stick lights** (Advanced settings › Lights): 10 colours, brightness and speed, with these effects:

| Effect | What it does |
|---|---|
| Solid (Fijo) | A constant colour |
| Breathing (Respiración) | The colour fades in and out |
| Chroma (Croma) | Cycles through colours, both sticks together |
| Rainbow (Arcoíris) | Moving colour cycle |
| **Reactive (Reactivo)** | Light pulses with the hits in the game's sound (gunshots, explosions), in real time. Idle and peak brightness are adjustable |

- **Vibration** of the controller and touchpad (4 levels), controller **auto-off**, **polling rate** (125–1000 Hz) and
  **D-pad** with 8 or 4 directions.
- **Touchpad** and **touch screen**: turn on or off.
- **Advanced settings**, each with **Calibrate**, **Test** (live values) and **Default**:
  - **Sticks**: centre and edge dead zones of each stick.
  - **Triggers**: start and end of travel of each trigger.
  - **Gyro**: as mouse, left stick or right stick, with sensitivity.
- **Factory-reset the controller**.

<br clear="right">

### Settings (Ajustes)

<img src="docs/img/ajustes.png" width="260" align="right" alt="Settings tab: interface, overlay, battery charging, theme and sounds">

- **Footer data** (shown or hidden), **in-game overlay**, **theme** (Simple or Contrast, with the Windows accent
  colour), **animations**, **sounds** and **charge only to 80 %** (with battery health).
- **Legion buttons**: what each press does (see [Legion buttons](#legion-buttons)) and the Legion Space switch.
- **Profiles**: profile list, apply profiles automatically and save changes automatically.
- **Behaviour**: re-apply settings after sleep and how often data refreshes.
- **Updates**: version, what's new, check automatically and uninstall.

<br clear="right">

---

## Per-game profiles

- A **Global** profile and one per game, applied automatically when a game opens and closes.
- **Every new game gets its own profile automatically** the first time it opens, with the settings of that moment:
  whatever you change while playing stays with that game. If you delete a profile, it isn't recreated while the game is open.
- Games with anti-cheat (Call of Duty, for example) are detected too.
- A game is anything run from a library (Steam, Epic, Xbox, GOG, EA…), anything that fills the screen, or anything
  that already has a profile; consoles and Windows programs never.
- Profiles store mode and TDP, CPU Boost, resolution and Hz, Lossless, AMD settings (including VRR), lights and gyro.
- **Auto-save**: what you change is saved to the applied profile. Without auto-save, save with the floppy button.
- From the footer you can apply any profile even if its game isn't open.

## Legion buttons

Defaults: **Legion L** → Xbox app (hold: Game Bar) and **Legion R** → GoPilot (hold: Task View).
Every press and long press can be changed, and **Advanced shortcuts** adds Legion + button combinations.
Available actions: GoPilot · Game Bar · Xbox home · Task View · Desktop · Keyboard · Snipping · Screenshot ·
Record · Thermal mode · Ctrl+Alt+Del · Task Manager · Lossless · Overlay · Close game.
The firmware's own combinations (Legion L + Y, X, A, B, LB) keep working.

## In-game overlay

GoPilot's own window on top of the game (no need to pin widgets in Game Bar) with **FPS**, **FPS and battery** or
**FPS, temperature and battery**; only in games or always, in the corner you prefer.

---

## Privacy

GoPilot sends no personal data or usage statistics. The only thing it does online is check for a new version (it
downloads a small `latest.json` from this repository), and that can be turned off in Settings › Updates › Check
automatically.

## FAQ

**Can it damage the console?** GoPilot only asks Lenovo's firmware for values within the limits the firmware itself
reports (for example 5–30 W sustained, AMD's maximum for the Z1 Extreme), just like Legion Space.

**Why does Windows warn when installing?** The package is signed with its own certificate, not one from a recognised
authority. That's why you have to confirm "Run anyway", and why it can't be installed with Smart App Control on.

**Does it work on other handhelds?** It is built and tested for the Legion Go S. On other devices many features (TDP,
controller, lights, buttons) won't be available.

**What happens to my Legion Space settings?** They stay: Legion Space is only disabled. Re-enabling it or uninstalling
GoPilot brings it back as it was.

---

## For developers

### Architecture

```
Game Bar widget (UWP, sandbox)                  src/GoSPanel.Widget
      │  AppService "GoSPanel.Bridge"
User-session agent (.NET 8)                     src/GoSPanel.Bridge
      │  display, audio, controller (HID), AMD ADLX, profiles, Legion buttons, overlay, Lossless
      │  named pipe \\.\pipe\GoSPanel (line-delimited JSON)
Windows service (.NET 8, SYSTEM)                src/GoSPanel.Service
         Lenovo WMI (TDP, modes, fan, charging), FPS via ETW, RAPL power, touch, Legion Space,
         updates and uninstall
```

- The widget can't touch hardware from its sandbox: everything goes through the agent and the service.
- The agent starts with the session (package startup task) and the widget connects to it.
- The service only exposes specific commands, never arbitrary WMI calls.
- Protocol: `{"cmd":"tdp.set","args":{"spl":15,"sppt":18,"fppt":20}}` → `{"ok":true,"data":{...}}`.

### Building

Requirements: Visual Studio 2022 with the UWP workload, .NET 8 SDK and Windows SDK 10.0.26100.

```powershell
.\scripts\build.ps1            # service + widget (with the agent inside) into dist\
```

The first run creates a development certificate in `certs\` (not committed to the repository).

### Publishing a release

```powershell
.\scripts\release.ps1 -Notes "- Change 1`n- Change 2"
```

Builds, tags and creates the Release with the zip and `latest.json`, which installed widgets check.

---

## Credits

- Controller and BIOS protocol worked out from: the Linux driver **hid-lenovo-go-s** (Derek J. Clark),
  **lenovo-wmi** in the Linux kernel, **HHD / Handheld Daemon** and the controller configuration library installed by
  Legion Space (only the protocol is reimplemented; no Lenovo code is included).
- **AMD ADLX** for the Radeon settings, **Xbox Game Bar Widget SDK**, **PresentMon** to measure VRR.
- **Phosphor Icons**.

## License

[MIT](LICENSE).
