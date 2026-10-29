# Cómo instalar Al Toque en Windows

Al Toque funciona en **Windows 10 (22H2) y Windows 11**, en PCs x64 y ARM64. No necesita .NET ni permisos de
administrador. Hay que instalarlo **en cada PC** que va a enviar o recibir archivos, y todas deben estar en la misma
red (el mismo Wi-Fi o cable).

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

Cuando haya versiones publicadas, también estarán en la pestaña **Releases** del repositorio.

## 2a. Instalar (recomendado)

1. Descomprime `AlToque-win-x64-instalador.zip`.
2. Ejecuta **`AlToque-win-x64-Setup.exe`**.
3. Como la app todavía no está firmada, Windows puede mostrar **"Windows protegió tu PC"**. Haz clic en
   **Más información → Ejecutar de todas formas**. Solo pasa la primera vez.
4. Se instala para tu usuario, crea accesos directos en el escritorio y en el menú Inicio, y se abre sola.

Las actualizaciones se descargan en segundo plano y se aplican al cerrar la app.

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

## 4. Emparejar con un compañero (una sola vez)

1. En **las dos PC**: clic en el ícono de Al Toque en la bandeja (junto al reloj) → botón **Emparejar**.
2. En una de ellas, elige a la otra en la lista.
3. Las dos pantallas muestran un **código de 6 dígitos**. Si es el mismo, pulsen **Sí, coincide** en ambas.

Desde ese momento ya son compañeros de confianza.

## 5. Enviar archivos

- **Clic derecho** sobre archivos o carpetas → **Mostrar más opciones** (en Windows 11) → **Enviar con Al Toque**
  → elige a tu compañero.
- O **arrastra** los archivos al panel de la bandeja, sobre el avatar de tu compañero.
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

## Desinstalar

- **Versión instalada:** *Configuración → Aplicaciones → Al Toque → Desinstalar*. Se quitan también el menú del clic
  derecho y el inicio automático.
- **Versión portable:** ejecuta `AlToque.exe unintegrate` desde una terminal en su carpeta y luego borra la carpeta.
