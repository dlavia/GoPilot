<div align="center">

# GoPilot

**The control panel for the Lenovo Legion Go S, inside Xbox Game Bar.**

[**Download**](https://github.com/dlavia/GoPilot/releases/latest) · [Installation](#installation) · [Features](#features) · [Español](README.md)

<img src="docs/img/pestanas.gif" width="340" alt="GoPilot in Xbox Game Bar: switching between the Home, Power, Display, Device and Settings tabs">

</div>

GoPilot replaces Legion Space with a Game Bar widget built for the controller: performance, display, controller,
lights, Legion buttons and per-game profiles, without leaving your game.

> The interface is currently in Spanish.

## Why GoPilot

- **Instant, on top of your game.** Legion R opens it over the game; LT and RT switch tabs.
- **Real numbers.** The footer shows FPS, temperature, fan and the console's **real power draw in watts**.
- **One profile per game, automatically.** Every new game gets its own profile and what you tweak while playing stays there.
- **Steam Deck-style Manual mode.** A single power limit; bursts and peaks adjust themselves.
- **Lights that react to sound.** The sticks light up with the game's gunshots and explosions.
- **Part of Windows and Xbox.** It uses the **Windows accent colour**, the **Xbox app's sounds** and the manufacturer's
  slot in Game Bar.
- **Updates itself and uninstalls cleanly.** No telemetry, no leftovers.

## Screenshots

<table>
  <tr>
    <td align="center"><img src="docs/img/inicio.png" width="240" alt="Home tab"><br><sub><b>Home</b> · quick tiles</sub></td>
    <td align="center"><img src="docs/img/potencia.png" width="240" alt="Power tab"><br><sub><b>Power</b> · modes with real watts</sub></td>
    <td align="center"><img src="docs/img/pantalla.png" width="240" alt="Display tab"><br><sub><b>Display</b> · resolution, Hz and AMD</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/img/equipo-avanzado.png" width="240" alt="Device tab"><br><sub><b>Device</b> · controller, lights and gyro</sub></td>
    <td align="center"><img src="docs/img/ajustes.png" width="240" alt="Settings tab"><br><sub><b>Settings</b> · theme, overlay and buttons</sub></td>
    <td align="center"><img src="docs/img/editar-baldosas.gif" width="240" alt="Editing tiles"><br><sub><b>Edit tiles</b> · your way</sub></td>
  </tr>
</table>

---

## Installation

1. Download `GoPilot-<version>.zip` from [Releases](https://github.com/dlavia/GoPilot/releases/latest) and extract it.
2. Double-click **`Instalar GoPilot.cmd`** and accept the administrator prompt.
3. Press **Legion R**. Done!

> Windows warns because the installer comes from the internet: "Windows protected your PC" › **More info** ›
> **Run anyway**. It can't be installed with Smart App Control turned on.

When installed, GoPilot **replaces Legion Space** (it disables it, it doesn't delete it). You can turn it back on in
Settings › Legion buttons.

**Updating:** when a new version is out you get a notice on Home; install it from Settings › Updates in about
20 seconds, keeping your settings and profiles.

**Uninstalling:** Settings › Updates › Uninstall GoPilot (press twice). It leaves the console as it was, with Legion
Space back.

---

## How to use it

| Button | What it does |
|---|---|
| **Legion R** | Opens and closes GoPilot (hold: Task View) |
| **Legion L** | Opens the Xbox app (hold: Game Bar) |
| **LT / RT** | Switch tabs |
| **A / B** | Select / back. On sliders: A to enter, D-pad to adjust, A or B to leave |

The **footer** at the bottom is always visible: the applied profile (A to change it) and live data.
It also works with the touch screen.

---

## Features

### Home

Brightness, volume and **quick tiles** you can reorder, remove and add ("Editar baldosas").

<details>
<summary>All 35 available tiles</summary>

| Group | Tiles |
|---|---|
| Performance | Mode · CPU Boost · Max fan · Fan curve |
| Display | Hz · VRR · Lossless Scaling · AFMF · RSR · Chill · Sharpening · Anti-Lag · Vari-Bright |
| Controller | Lights · Gyro · Vibration · Touchpad vibration · Controller auto-off · Polling rate · D-pad 8/4 |
| Touch & system | Touchpad · Touch screen · On-screen keyboard · Close game · Charge to 80 % |
| GoPilot | Overlay · Footer data · Theme · Animations · Sounds · Legion buttons · Legion Space · Auto profiles · Auto-save · Re-apply after sleep |

</details>

### Power

Lenovo's modes with their **real watts**, which change depending on whether the console is plugged in or on battery:

| Mode | Plugged in | On battery |
|---|---|---|
| Quiet | 10 W | 10 W |
| Balanced | 16 W | 15 W |
| Performance | 30 W | 25 W |

- **Manual, Steam Deck style:** pick a limit (5–30 W) and see right away what it draws. Under "Advanced" you can set
  bursts (sPPT) and peaks (fPPT) by hand.
- **CPU Boost**, **max fan** and **fan curve**.
- **Legion L + Y** switches modes and GoPilot shows it big with its watts.

### Display

- **Resolution**, **refresh rate** (up to 120 Hz) and **VRR (FreeSync)**, also for windowed games.
- **AMD Radeon without opening Adrenalin:** AFMF (more FPS), RSR (upscaling), Chill (FPS limit), Sharpening,
  Anti-Lag and Vari-Bright.
- **Lossless Scaling:** turn it on with the game in front.

### Device

- **Stick lights:** colour, brightness and speed, with Solid, Breathing, Chroma, Rainbow and **Reactive** effects —
  Reactive pulses with the game's sound.
- **Controller:** vibration (touchpad too), auto-off, polling rate and 8 or 4-way D-pad.
- **Advanced settings:** stick dead zones, trigger travel and **gyro** (as mouse or as a stick), with calibrate, live
  test and defaults.
- Turn the **touchpad** and **touch screen** on or off, and factory-reset the controller.

### Settings

Theme (Simple or Contrast), overlay, sounds, animations, **charge to 80 %** with battery health, Legion buttons,
profiles and updates.

### Per-game profiles

- A **Global** profile and one per game, applied automatically when each game opens and closes.
- **Every new game gets its profile automatically**, and what you change while playing is saved to it.
- Detects games from Steam, Epic, Xbox, GOG, EA… and games with anti-cheat too (like Call of Duty).
- Profiles store mode and TDP, CPU Boost, resolution, Hz, Lossless, AMD settings, lights and gyro.

### Legion buttons and overlay

- Every press and long press of **Legion L and R** can be changed, and "Advanced shortcuts" adds Legion + button
  combinations.
- **Its own overlay** on top of the game with FPS, temperature and battery, in the corner you like.

<details>
<summary>Actions available for the buttons</summary>

GoPilot · Game Bar · Xbox home · Task View · Desktop · Keyboard · Snipping · Screenshot · Record · Thermal mode ·
Ctrl+Alt+Del · Task Manager · Lossless · Overlay · Close game.
The firmware's own combinations (Legion L + Y, X, A, B, LB) keep working.

</details>

---

## Coming soon

- **AutoTDP:** GoPilot will adjust the power limit by itself, game by game, to hold the FPS you choose while using as
  little battery as possible.

---

## FAQ

**Can it damage the console?** No. It only uses values within the limits Lenovo's firmware itself allows, just like
Legion Space.

**Does it send data?** No. The only thing it does online is check for a new version, and that can be turned off.

**Does it work on other handhelds?** It is built for the Legion Go S. On other devices many features won't be there.

**Do I lose Legion Space?** No: it's only disabled. Re-enabling it or uninstalling GoPilot brings it back as it was.

> Personal project, not affiliated with Lenovo, AMD or Microsoft. Tested on a Legion Go S Z1 Extreme (83N6) with
> Windows 11.

---

<details>
<summary><b>For developers</b></summary>

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

The widget can't touch hardware from its sandbox: everything goes through the agent and the service, which only
exposes specific commands. Protocol: `{"cmd":"tdp.set","args":{"spl":15,"sppt":18,"fppt":20}}` → `{"ok":true,"data":{...}}`.

### Build and publish

Requirements: Visual Studio 2022 with UWP, .NET 8 SDK and Windows SDK 10.0.26100.

```powershell
.\scripts\build.ps1                                   # service + widget into dist\
.\scripts\release.ps1 -Notes "- Change 1`n- Change 2"     # builds and publishes the Release with latest.json
```

The first build creates a development certificate in `certs\` (not committed).

</details>

## Credits

Controller and BIOS protocol worked out from the Linux driver **hid-lenovo-go-s** (Derek J. Clark), **lenovo-wmi** in
the Linux kernel, **HHD / Handheld Daemon** and Legion Space's controller configuration library (only the protocol is
reimplemented; no Lenovo code is included). **AMD ADLX**, **Xbox Game Bar Widget SDK**, **PresentMon** and
**Phosphor Icons**.

License: [MIT](LICENSE).
