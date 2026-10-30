# Cómo instalar Al Toque

Al Toque funciona en **Windows**, **Linux**, **macOS** y **Android**, y todos se envían archivos entre sí. Hay que
instalarlo **en cada equipo** que va a enviar o recibir, y todos deben estar en la misma red (el mismo Wi-Fi o cable).
No necesita .NET ni permisos de administrador.

- [Windows](#windows) · [Linux](#linux) · [macOS](#macos) · [Android](#android)
- [Emparejar](#4-emparejar-con-un-compañero-una-sola-vez) · [Si no se ven](#si-no-se-ven-las-pc-o-no-pasa-nada) ·
  [Actualizar](#actualizar) · [Desinstalar](#desinstalar)

# Windows

Windows 10 (22H2) y Windows 11, en PCs x64 y ARM64.

## 1. Descargar

1. Entra al repositorio en GitHub → pestaña **Actions** → flujo **Compilar y probar**.
2. Abre la ejecución más reciente con ✓ verde de la rama `Windows` (o `main`).
3. Al final de la página, en **Artifacts**, descarga el que corresponda a tu PC:

| Archivo | Para quién |
|---|---|
| `AlToque-win-x64-instalador` | La mayoría de las PC (Intel o AMD). **Recomendado.** |
| `AlToque-win-x64-portable` | Sin instalar: por ejemplo, desde una memoria USB |
| `AlToque-win-arm64-…` | PC con procesador ARM (Snapdragon, Surface Pro X) |

> ¿No sabes cuál es tu procesador? *Configuración → Sistema → Información → Tipo de sistema*. Si dice
> "procesador basado en x64", usa **x64**.

Las versiones publicadas también están en la pestaña **Releases** del repositorio (`AlToque-win-x64-Setup.exe` o el
`.zip` portable). Instalar desde ahí es lo más cómodo: esa copia se actualiza sola.

## 2a. Instalar (recomendado)

1. Descomprime `AlToque-win-x64-instalador.zip`.
2. Ejecuta **`AlToque-win-x64-Setup.exe`**.
3. Como la app todavía no está firmada, Windows puede mostrar **"Windows protegió tu PC"**. Haz clic en
   **Más información → Ejecutar de todas formas**. Solo pasa la primera vez.
4. Se instala para tu usuario, crea accesos directos en el escritorio y en el menú Inicio, y se abre sola.

Las actualizaciones son automáticas: mira el apartado [Actualizar](#actualizar).

## 2b. Versión portable (sin instalar)

1. Descomprime `AlToque-…-portable.zip` en una carpeta propia (por ejemplo `C:\AlToque` o una memoria USB).
2. Ejecuta **`AlToque.exe`** (mismo aviso de SmartScreen que arriba).
3. Toda la configuración se guarda en la carpeta `AlToque.data`, junto al ejecutable, gracias al archivo
   `portable.flag`. Si lo borras, la configuración pasa a `%APPDATA%\AlToque`.

## 3. Primer uso

1. Al abrir, **Chasqui** te pide el nombre con el que te verán tus compañeros (por ejemplo "Laptop de Laura").
2. Si Windows pregunta por el **firewall**, marca solo **Redes privadas** y acepta. Si no preguntó o elegiste mal:
   ícono de la bandeja → *Historial y ajustes* → *Ajustes* → **Permitir en el firewall**.
3. Comprueba que tu red esté marcada como **privada**: *Configuración → Red e Internet → Wi-Fi/Ethernet →
   Tipo de perfil de red → Privada*. En redes públicas Al Toque no recibe archivos, por seguridad.
4. La primera vez que abres el panel en esa PC, Chasqui te da un **recorrido guiado** de medio minuto. Puedes
   saltarlo con *Saltar* (o Esc) y repetirlo cuando quieras desde *Ajustes → Acerca de → Ver el tutorial otra vez*.

## 4. Emparejar con un compañero (una sola vez)

1. En **los dos equipos**: clic en el ícono de Al Toque en la bandeja (junto al reloj; en Mac, en la barra de menús;
   en Android, dentro de la app) → **Emparejar**.
2. En una de ellas, elige a la otra en la lista.
3. Las dos pantallas muestran un **código de 6 dígitos**. Si es el mismo, pulsen **Sí, coincide** en ambas.

Desde ese momento ya son compañeros de confianza.

## 5. Enviar archivos

- **Clic derecho** sobre archivos o carpetas → **Mostrar más opciones** (en Windows 11) → **Enviar con Al Toque**
  → elige a tu compañero.
- O **arrastra**: abre el panel (clic en el ícono de Al Toque junto al reloj). El panel se queda abierto aunque
  hagas clic en el Explorador; arrastra los archivos **sobre el avatar** de tu compañero para enviarlos directo, o a la
  zona "Suelta archivos aquí" para elegir después a quién. El panel se cierra con la **X**, con **Esc** o con otro
  clic en el ícono.
- O clic derecho → **Enviar a → Al Toque** para elegir el destino.

Tu compañero ve una tarjeta con lo que llega y pulsa **Aceptar**. Los archivos se guardan en
`Descargas\Al Toque`.

**Para alguien que todavía no es de confianza:** quien recibe elige *Recibir de: Todos (10 min)* en el panel. La
solicitud llega marcada como **No verificado**.

## Si no se ven las PC o no pasa nada

1. **¿La red es pública?** Es la causa más común. Por seguridad, en redes públicas Al Toque **no recibe ni acepta
   emparejamientos**, y el panel lo avisa con un recuadro amarillo. Tienes dos opciones, y hay que hacerlo **en las
   dos PC**:
   - **Mejor:** marca la red como privada en Windows (*Configuración → Red e Internet → Wi-Fi/Ethernet → Tipo de
     perfil de red → Privada*).
   - **Si confías en esa red:** en el aviso del panel pulsa **Recibir igual en esta red** y acepta el permiso de
     administrador para el firewall (solo la primera vez). Para hacerlo siempre: *Ajustes → Recibir también en redes
     públicas*.
2. Comprueben que las dos PC están en la **misma red** y con Al Toque abierto (ícono en la bandeja).
3. **Firewall:** *Ajustes → Permitir en el firewall*. Si usas otro antivirus con firewall, permite `AlToque.exe`.
4. **Sin descubrimiento automático:** panel → ícono de código → **Mi código** en una PC, y en la otra **Conectar
   por código** (también acepta la dirección IP).
5. **Wi-Fi de cafeterías, hoteles o invitados:** muchas aíslan los equipos entre sí y ninguna app de red local puede
   funcionar ahí. Conecten las dos PC al **punto de acceso del celular** o a otra red.

# Linux

Distribuciones de 64 bits recientes (Ubuntu 22.04+, Debian 12+, Fedora 39+…), en PCs x64 y ARM64, con escritorio
gráfico. La bandeja necesita soporte de íconos de estado (en GNOME, la extensión *AppIndicator*).

**AppImage (recomendado, se actualiza sola):**

1. Descarga `AlToque-linux-x64.AppImage` (o `linux-arm64`) de *Releases*.
2. Dale permiso de ejecución: `chmod +x AlToque-linux-x64.AppImage` (o *Propiedades → Permitir ejecutar*).
3. Ábrelo. Si tu sistema no tiene FUSE 2 (`libfuse2`), instálalo o ejecútalo con
   `./AlToque-linux-x64.AppImage --appimage-extract-and-run`.

**Portable:** descomprime `AlToque-…-linux-x64-portable.tar.gz` y ejecuta `AlToque/AlToque`. La configuración se guarda
en `AlToque/AlToque.data`.

Lo recibido se guarda en `~/Descargas/Al Toque` (o la carpeta de descargas de tu escritorio). Si usas `ufw` u otro
firewall, permite el puerto TCP 45831 y el UDP 45832 en tu red local.

# macOS

macOS 12 o posterior, en Mac con Apple Silicon (`osx-arm64`) o Intel (`osx-x64`).

**Instalador (recomendado, se actualiza solo):** abre `AlToque-osx-arm64-Setup.pkg` y sigue los pasos.

**App suelta:** descomprime `AlToque-…-osx-arm64.zip` y arrastra **Al Toque** a *Aplicaciones*.

Como la app todavía no está firmada por Apple, la primera vez macOS dice que no puede comprobar el desarrollador:
haz **clic derecho sobre Al Toque → Abrir → Abrir** (o *Ajustes del Sistema → Privacidad y seguridad → Abrir igualmente*).
Si dice que la app "está dañada", ejecuta en Terminal `xattr -cr "/Applications/Al Toque.app"` y vuelve a abrirla.

El ícono aparece en la **barra de menús**, arriba a la derecha, y el panel baja desde ahí. Si macOS pregunta si Al Toque
puede buscar dispositivos en la red local o aceptar conexiones entrantes, pulsa **Permitir**.

# Android

Android 11 o posterior.

1. En el teléfono, descarga `AlToque-…-android.apk` de *Releases*.
2. Ábrelo. Android pide permiso para **instalar apps de fuentes desconocidas** desde el navegador o la app de archivos:
   actívalo y vuelve. Pulsa **Instalar**.
3. Al abrir Al Toque, elige tu nombre y tu avatar. Si pregunta por las **notificaciones**, permítelas: se usan para
   que un envío siga aunque cambies de app.

Para enviar: **Enviar archivos** en la app y toca a tu compañero, o desde la galería o cualquier app → **Compartir →
Al Toque**. Lo recibido aparece en **Descargas › Al Toque** y en la galería.

Para recibir, Al Toque tiene que estar **abierto** (en pantalla o recién minimizado). El teléfono y la PC deben estar
en el mismo Wi-Fi; si el router aísla los equipos, usa **Conectar por código**. También funciona si la PC se conecta
al **punto de acceso** del teléfono.

# Actualizar

- **Versión instalada (con `Setup.exe`):** Al Toque revisa GitHub al poco de abrirse y luego cada 6 horas. Si hay una
  versión nueva, la descarga en segundo plano y te avisa en el panel: pulsa **Reiniciar y actualizar** y en unos
  segundos vuelve a abrirse ya actualizado. Si no lo pulsas, se instala sola la próxima vez que cierres la app. Nunca
  se reinicia en medio de un envío sin preguntarte. Puedes desactivarlo en *Ajustes → Acerca de → Actualizar
  automáticamente* (entonces descarga solo cuando pulsas **Actualizar**).
- **Versión portable:** te avisa de que hay una versión nueva y el botón **Descargar** abre la página de GitHub;
  descomprime el `.zip` nuevo encima de la carpeta anterior (tu configuración en `AlToque.data` se conserva).
- **Linux (AppImage) y macOS (`.pkg`):** igual que la versión instalada de Windows: se descargan y se instalan solas.
- **Android:** la app avisa cuando hay una versión nueva; **Descargar** baja el APK y lo instalas encima, sin perder
  tus compañeros.

# Desinstalar

- **Versión instalada:** *Configuración → Aplicaciones → Al Toque → Desinstalar*. Se quitan también el menú del clic
  derecho y el inicio automático.
- **Versión portable:** ejecuta `AlToque.exe unintegrate` desde una terminal en su carpeta y luego borra la carpeta.
- **Linux:** ejecuta `AlToque unintegrate` (quita el inicio automático) y borra el AppImage o la carpeta;
  la configuración está en `~/.config/altoque`.
- **macOS:** arrastra **Al Toque** de *Aplicaciones* a la papelera; la configuración está en
  `~/Library/Application Support/AlToque` y el inicio automático en `~/Library/LaunchAgents/com.krostgames.altoque.plist`.
- **Android:** mantén pulsado el ícono → **Desinstalar**. Lo que recibiste se queda en Descargas.
