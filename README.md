<div align="center">

# GoPilot

**El panel de control de la Lenovo Legion Go S, dentro de Xbox Game Bar.**

[**Descargar**](https://github.com/dlavia/GoPilot/releases/latest) · [Instalación](#instalación) · [Funciones](#funciones) · [English](README.en.md)

<img src="docs/img/pestanas.gif" width="340" alt="GoPilot en Xbox Game Bar: cambio entre las pestañas Inicio, Potencia, Pantalla, Equipo y Ajustes">

</div>

GoPilot sustituye a Legion Space por un widget de Game Bar pensado para el mando: rendimiento, pantalla, mando,
luces, botones Legion y perfiles por juego, sin salir del juego.

## Por qué GoPilot

- **Al momento y sin salir del juego.** Legion R lo abre encima del juego; LT y RT cambian de pestaña.
- **Datos reales.** El pie muestra FPS, temperatura, ventilador y el **consumo real en vatios** de la consola.
- **Un perfil por juego, solo.** Cada juego nuevo recibe su perfil y lo que ajustas jugando se queda en él.
- **Modo Manual como la Steam Deck.** Un único límite de potencia; ráfagas y picos se ajustan solos.
- **Luces que reaccionan al sonido.** Los sticks se iluminan con los disparos y explosiones del juego.
- **Parte de Windows y Xbox.** Usa el **color de acento de Windows**, los **sonidos de la app Xbox** y el sitio del
  fabricante en Game Bar.
- **Se actualiza solo y se desinstala limpio.** Sin telemetría y sin dejar rastro.

## Capturas

<table>
  <tr>
    <td align="center"><img src="docs/img/inicio.png" width="240" alt="Pestaña Inicio"><br><sub><b>Inicio</b> · baldosas rápidas</sub></td>
    <td align="center"><img src="docs/img/potencia.png" width="240" alt="Pestaña Potencia"><br><sub><b>Potencia</b> · modos con vatios reales</sub></td>
    <td align="center"><img src="docs/img/pantalla.png" width="240" alt="Pestaña Pantalla"><br><sub><b>Pantalla</b> · resolución, Hz y AMD</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/img/equipo-avanzado.png" width="240" alt="Pestaña Equipo"><br><sub><b>Equipo</b> · mando, luces y giroscopio</sub></td>
    <td align="center"><img src="docs/img/ajustes.png" width="240" alt="Pestaña Ajustes"><br><sub><b>Ajustes</b> · tema, overlay y botones</sub></td>
    <td align="center"><img src="docs/img/editar-baldosas.gif" width="240" alt="Editar baldosas"><br><sub><b>Editar baldosas</b> · a tu gusto</sub></td>
  </tr>
</table>

---

## Instalación

1. Descarga `GoPilot-<versión>.zip` de [Releases](https://github.com/dlavia/GoPilot/releases/latest) y descomprímelo.
2. Doble clic en **`Instalar GoPilot.cmd`** y acepta el permiso de administrador.
3. Pulsa **Legion R**. ¡Listo!

> Windows avisa porque el instalador viene de internet: «Windows protegió tu PC» › **Más información** ›
> **Ejecutar de todas formas**. Con Smart App Control activado no se puede instalar.

Al instalarlo, GoPilot **sustituye a Legion Space** (lo desactiva, no lo borra). Puedes volver a activarlo en
Ajustes › Botones Legion.

**Actualizar:** cuando hay versión nueva sale un aviso en Inicio; se instala desde Ajustes › Actualizaciones en unos
20 segundos, sin perder ajustes ni perfiles.

**Desinstalar:** Ajustes › Actualizaciones › Desinstalar GoPilot (pulsar dos veces). Deja la consola como estaba,
con Legion Space de vuelta.

---

## Cómo se usa

| Botón | Qué hace |
|---|---|
| **Legion R** | Abre y cierra GoPilot (mantenido: vista de tareas) |
| **Legion L** | Abre la app Xbox (mantenido: Game Bar) |
| **LT / RT** | Cambian de pestaña |
| **A / B** | Activar / volver. En los deslizadores: A para entrar, cruceta para ajustar, A o B para salir |

Abajo, siempre visible, está el **pie**: el perfil aplicado (A para cambiarlo) y los datos en vivo.
También se puede usar con la pantalla táctil.

---

## Funciones

### Inicio

Brillo, volumen y **baldosas de acceso rápido** que puedes ordenar, quitar y añadir («Editar baldosas»).

<details>
<summary>Las 35 baldosas disponibles</summary>

| Grupo | Baldosas |
|---|---|
| Rendimiento | Modo · CPU Boost · Ventilador al máximo · Curva del ventilador |
| Pantalla | Hz · VRR · Lossless Scaling · AFMF · RSR · Chill · Nitidez · Anti-Lag · Vari-Bright |
| Mando | Luces · Giroscopio · Vibración · Vibración del touchpad · Apagado del mando · Sondeo · Cruceta 8/4 |
| Táctil y sistema | Touchpad · Pantalla táctil · Teclado en pantalla · Cerrar juego · Carga al 80 % |
| GoPilot | Overlay · Datos del pie · Tema · Animaciones · Sonidos · Botones Legion · Legion Space · Perfiles automáticos · Autoguardado · Reaplicar tras suspender |

</details>

### Potencia

Los modos de Lenovo con sus **vatios reales**, que cambian si la consola está enchufada o con batería:

| Modo | Enchufada | Con batería |
|---|---|---|
| Silencio | 10 W | 10 W |
| Equilibrado | 16 W | 15 W |
| Rendimiento | 30 W | 25 W |

- **Manual, como la Steam Deck:** eliges un límite (5–30 W) y ves al momento lo que consume. En «Avanzado» puedes
  ajustar a mano las ráfagas (sPPT) y los picos (fPPT).
- **CPU Boost**, **ventilador al máximo** y **curva del ventilador**.
- **Legion L + Y** cambia de modo y GoPilot lo muestra en grande con sus vatios.

### Pantalla

- **Resolución**, **frecuencia** (hasta 120 Hz) y **VRR (FreeSync)**, también en juegos en ventana.
- **AMD Radeon sin abrir Adrenalin:** AFMF (más FPS), RSR (escalado), Chill (límite de FPS), Nitidez, Anti-Lag y
  Vari-Bright.
- **Lossless Scaling:** actívalo con el juego delante.

### Equipo

- **Luces de los sticks:** color, brillo y velocidad, con los efectos Fijo, Respiración, Croma, Arcoíris y
  **Reactivo**, que late con el sonido del juego.
- **Mando:** vibración (también del touchpad), apagado automático, frecuencia de sondeo y cruceta de 8 o 4 direcciones.
- **Ajustes avanzados:** zonas muertas de los sticks, recorrido de los gatillos y **giroscopio** (como ratón o como
  stick), con calibrar, probar en directo y valores por defecto.
- Activar o desactivar el **touchpad** y la **pantalla táctil**, y restablecer el mando de fábrica.

### Ajustes

Tema (Sencillo o Contraste), overlay, sonidos, animaciones, **carga al 80 %** con la salud de la batería, botones
Legion, perfiles y actualizaciones.

### Perfiles por juego

- Un perfil **Global** y uno por juego, que se aplican solos al abrir y cerrar cada juego.
- **Cada juego nuevo recibe su perfil automáticamente**, y lo que cambias jugando se guarda en él.
- Detecta juegos de Steam, Epic, Xbox, GOG, EA… y también los que tienen antitrampas (como Call of Duty).
- Guardan modo y TDP, CPU Boost, resolución, Hz, Lossless, ajustes de AMD, luces y giroscopio.

### Botones Legion y overlay

- Cada pulsación y pulsación larga de **Legion L y R** se puede cambiar, y en «Atajos avanzados» se añaden
  combinaciones Legion + botón.
- **Overlay propio** encima del juego con FPS, temperatura y batería, en la esquina que quieras.

<details>
<summary>Acciones disponibles para los botones</summary>

GoPilot · Game Bar · Inicio de Xbox · Vista de tareas · Escritorio · Teclado · Recortes · Captura · Grabar ·
Modo térmico · Ctrl+Alt+Supr · Administrador de tareas · Lossless · Overlay · Cerrar juego.
Las combinaciones del propio firmware (Legion L + Y, X, A, B, LB) siguen funcionando.

</details>

---

## Próximamente

- **AutoTDP:** GoPilot ajustará el consumo solo, juego a juego, para mantener los FPS que elijas gastando la menor
  batería posible.

---

## Preguntas frecuentes

**¿Puede dañar la consola?** No. Solo usa valores dentro de los límites que el propio firmware de Lenovo permite,
igual que Legion Space.

**¿Envía datos?** No. Lo único que hace por internet es comprobar si hay una versión nueva, y se puede desactivar.

**¿Funciona en otras consolas?** Está hecho para la Legion Go S. En otros equipos muchas funciones no estarán.

**¿Pierdo Legion Space?** No: solo se desactiva. Al reactivarlo o al desinstalar GoPilot vuelve tal como estaba.

> Proyecto personal, no afiliado a Lenovo, AMD ni Microsoft. Probado en una Legion Go S Z1 Extreme (83N6) con
> Windows 11.

---

<details>
<summary><b>Para desarrolladores</b></summary>

### Arquitectura

```
Widget de Game Bar (UWP, sandbox)               src/GoSPanel.Widget
      │  AppService «GoSPanel.Bridge»
Agente de la sesión del usuario (.NET 8)        src/GoSPanel.Bridge
      │  pantalla, audio, mando (HID), AMD ADLX, perfiles, botones Legion, overlay, Lossless
      │  tubería con nombre \\.\pipe\GoSPanel (JSON por líneas)
Servicio de Windows (.NET 8, SYSTEM)            src/GoSPanel.Service
         WMI de Lenovo (TDP, modos, ventilador, carga), FPS por ETW, consumo RAPL, táctil, Legion Space,
         actualizaciones y desinstalación
```

El widget no puede tocar hardware desde el sandbox: todo pasa por el agente y el servicio, que solo expone comandos
concretos. Protocolo: `{"cmd":"tdp.set","args":{"spl":15,"sppt":18,"fppt":20}}` → `{"ok":true,"data":{...}}`.

### Compilar y publicar

Requisitos: Visual Studio 2022 con UWP, SDK de .NET 8 y Windows SDK 10.0.26100.

```powershell
.\scripts\build.ps1                                   # servicio + widget en dist\
.\scripts\release.ps1 -Notes "- Novedad 1`n- Novedad 2"   # compila y publica la Release con latest.json
```

La primera compilación crea un certificado de desarrollo en `certs\` (no se sube al repositorio).

</details>

## Créditos

Protocolo del mando y de la BIOS deducido del driver de Linux **hid-lenovo-go-s** (Derek J. Clark), **lenovo-wmi**
del kernel de Linux, **HHD / Handheld Daemon** y la librería de configuración del mando de Legion Space (solo se
reimplementa el protocolo; no se incluye código de Lenovo). **AMD ADLX**, **Xbox Game Bar Widget SDK**,
**PresentMon** y **Phosphor Icons**.

Licencia [MIT](LICENSE).
