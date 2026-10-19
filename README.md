# GoPilot

**Panel de control para la Lenovo Legion Go S como widget de Xbox Game Bar.**
Sustituye a Legion Space: rendimiento, pantalla, mando, botones Legion, perfiles por juego y un overlay
propio sobre los juegos, todo desde Game Bar y pensado para usarse con el mando.

> Proyecto personal, no afiliado a Lenovo, AMD ni Microsoft. Probado en una Legion Go S **Z1 Extreme**
> (modelo 83N6, BIOS S0CN27WW) con Windows 11. Úsalo bajo tu responsabilidad: cambia ajustes de la BIOS
> (TDP, ventilador, carga) y del firmware del mando.

---

## Funciones

### Inicio
- Brillo y volumen.
- Baldosas de acceso rápido **editables**: «Editar baldosas» → quitar (✕, X o mantener A), añadir (+),
  cambiar de sitio (A sobre una y A sobre otra, con animación). 24 baldosas disponibles.
- Monitor fijo abajo: FPS del juego, carga, temperatura, ventilador y batería (completo, compacto u oculto).

### Rendimiento
- Modos **Silencio / Equilibrado / Rendimiento / Manual** (los del firmware de Lenovo) y **TDP manual**
  (SPL, sPPT y fPPT por separado).
- **CPU Boost**, **ventilador al máximo** y curva de ventilador propia (solo en modo Manual; desactivada por ahora).
- **Legion L + Y** (lo resuelve el propio firmware) muestra un aviso grande en el centro con el modo y los vatios.
- Se vuelve a aplicar todo al salir de la suspensión.

### Pantalla y AMD Radeon (ADLX)
- Resolución y frecuencia de refresco.
- **VRR (FreeSync)**: al activarlo también se activa el ajuste de Windows para juegos en ventana/sin bordes.
  Verificado con PresentMon: con V-Sync y por debajo de 120 FPS la pantalla sigue al juego fotograma a fotograma.
- **AFMF**, **RSR**, **Radeon Chill** (con FPS mínimos y máximos), **Nitidez**, **Anti-Lag** y **Vari-Bright**.
- **Lossless Scaling**: activar/desactivar con su atajo (el estado se lee de su ventana real).

### Mando (sin Legion Space)
- Luces de los sticks: color, efecto, brillo y velocidad (en Ajustes avanzados › Luces).
- Efecto **Reactivo**: pulsos con los golpes del sonido del juego (disparos, explosiones). Usa la iluminación dinámica
  de Windows (el mando recibe los colores en tiempo real sin guardarlos en su memoria); GoPilot se pone solo como app
  de luces en segundo plano.
- Touchpad y pantalla táctil: activar/desactivar (como dispositivos de Windows).
- Vibración del mando y del touchpad (4 niveles), apagado automático del mando, frecuencia de sondeo
  (125–1000 Hz) y cruceta de 8 o 4 direcciones.
- **Ajustes avanzados** (desplegables):
  - **Sticks**: zona muerta central y de borde de cada stick.
  - **Gatillos**: inicio y fin de recorrido de cada gatillo.
  - **Giroscopio**: como ratón, stick izquierdo o stick derecho, con sensibilidad.
  - En cada uno: **Calibrar**, **Probar** (valores en directo) y **Por defecto**.
- **Restablecer el mando de fábrica**.

### Botones Legion
- **Legion L** → app Xbox, **Legion R** → GoPilot (abre el widget directamente en Game Bar).
- Pulsación larga configurable (por defecto Legion L mantenido → Game Bar, Legion R mantenido → vista de tareas).
- Atajos avanzados opcionales (Legion + botón).
- Interruptor para **desactivar Legion Space** (servicio y arranque) sin desinstalarlo.

### Perfiles por juego
- Perfil **Global** y uno por juego, aplicados automáticamente al abrir/cerrar el juego. Cuenta como juego lo que
  se ejecuta desde una biblioteca (Steam, Epic, Xbox, GOG, EA…), lo que ocupa toda la pantalla o lo que ya tiene perfil;
  consolas y programas de Windows nunca.
- Guardan modo, TDP, CPU Boost, resolución y Hz, Lossless, ajustes de AMD (incluido VRR), luces y giroscopio.
- **Autoguardado**: lo que cambies se guarda solo en el perfil activo.
- Lista desplegable para activar o editar cualquier perfil aunque su juego no esté abierto.

### Overlay sobre los juegos
- Ventana propia (no depende de fijar widgets): FPS, temperatura y batería.
- Solo en juegos o siempre; contenido y posición configurables.

### Interfaz
- Cabecera compacta: pestañas y perfil activo en una sola fila (el título «GoPilot» lo pone Game Bar y no se puede quitar).
  Tocar el perfil abre la lista de perfiles.
- Inicio con baldosas; Potencia, Pantalla, Equipo y Ajustes como listas al estilo de la app de Xbox (icono, nombre, valor
  o interruptor a la derecha). Lo avanzado (sticks, gatillos, giroscopio, luces, curva, Chill, overlay, atajos) se
  despliega dentro de la tarjeta de su fila.
- Dos temas (**Sencillo** y **Contraste**) sobre el fondo de Game Bar, con el color de acento de Windows.
- Sonidos de navegación de la app de Xbox (se copian al compilar de la app instalada).
- Todo usable con el mando: LT/RT cambian de pestaña, A para entrar en los deslizadores.
- Carga de batería limitada al 80 %, salud de la batería, animaciones opcionales.

---

## Arquitectura

```
Widget de Game Bar (UWP, sandbox)               src/GoSPanel.Widget
      │  AppService «GoSPanel.Bridge»
Agente de la sesión del usuario (.NET 8)        src/GoSPanel.Bridge
      │  pantalla, audio, mando (HID), AMD ADLX, perfiles, botones Legion, overlay, Lossless
      │  tubería con nombre \\.\pipe\GoSPanel (JSON por líneas)
Servicio de Windows (.NET 8, SYSTEM)            src/GoSPanel.Service
         WMI de Lenovo (TDP, modos, ventilador, carga), FPS por ETW, táctil, Legion Space, suspensión
```

- El widget no puede tocar hardware desde el sandbox: todo pasa por el agente y el servicio.
- El agente arranca con Windows (tarea de inicio del paquete) y el widget se conecta a él.
- El servicio solo expone comandos concretos, nunca llamadas WMI arbitrarias.

Protocolo: `{"cmd":"tdp.set","args":{"spl":15,"sppt":18,"fppt":20}}` → `{"ok":true,"data":{...}}`.

---

## Instalación (en la consola)

1. Descarga `GoPilot-<versión>.zip` de [Releases](https://github.com/dlavia/GoPilot/releases/latest) y descomprímelo.
2. Doble clic en **`Instalar GoPilot.cmd`** y acepta el aviso de administrador (instala el servicio, el certificado
   y el widget). También vale `.\install.ps1` en PowerShell como administrador.
3. Abre Game Bar (Win+G o **Legion R**): GoPilot ocupa el primer icono, el de Legion Space.

Al instalarlo, GoPilot **sustituye a Legion Space**: lo desactiva (sin desinstalarlo) y se queda con los botones Legion
y su sitio en Game Bar. Se puede volver a activar en Ajustes › Botones Legion, y al desinstalar GoPilot vuelve solo.
Windows avisa al ejecutar el instalador descargado («Windows protegió tu PC» › Más información › Ejecutar de todas formas).

**Actualizar:** GoPilot avisa en Inicio cuando hay una versión nueva y se instala desde Ajustes › Actualizaciones
(solo instala paquetes firmados con el mismo certificado). **Desinstalar:** Ajustes › Actualizaciones ›
«Desinstalar GoPilot» (pulsar dos veces), o `uninstall.ps1` como administrador. Quita el widget, el servicio, sus
datos y el certificado, y deja Legion Space activado.

> El paquete va firmado con un certificado propio. Con **Smart App Control** activado Windows no lo deja
> instalar; para eso haría falta una firma de confianza (p. ej. Azure Artifact Signing).

## Compilar

Requisitos: Visual Studio 2022 con la carga de trabajo UWP, SDK de .NET 8 y Windows SDK 10.0.26100.

```powershell
.\scripts\build.ps1            # servicio + widget (con el agente dentro) en dist\
```

La primera vez crea un certificado de desarrollo en `certs\` (no se sube al repositorio).

Publicar una versión (compila, etiqueta y la sube a Releases con `latest.json`, que es lo que consultan los widgets):

```powershell
.\scripts\release.ps1 -Notes "- Novedad 1`n- Novedad 2"
```

---

## Créditos

- Protocolo del mando y de la BIOS deducido de: el driver de Linux **hid-lenovo-go-s** (Derek J. Clark),
  **lenovo-wmi** del kernel de Linux, **HHD / Handheld Daemon** y la librería de configuración del mando
  que instala Legion Space (solo se reimplementa el protocolo; no se incluye código de Lenovo).
- **AMD ADLX** para los ajustes de Radeon, **Xbox Game Bar Widget SDK**, **PresentMon** para medir el VRR.
- Iconos **Phosphor Icons**.

## Licencia

[MIT](LICENSE).
