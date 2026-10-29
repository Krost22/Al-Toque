# Al Toque

Envía cualquier archivo a otra PC de tu red con un clic derecho. El receptor confirma, la transferencia viaja cifrada
y **Chasqui**, un avión de papel con ojos, acompaña cada envío.

*Clic derecho → nombre de tu compañero → listo.* Sin cuentas, sin internet y sin escribir direcciones IP.

<p>
  <img src="docs/img/panel.png" alt="Panel rápido con Chasqui y un compañero de confianza" width="300" />
  <img src="docs/img/solicitud.png" alt="Solicitud entrante de un equipo no verificado, con la advertencia del .exe" width="520" />
</p>

La especificación completa está en [`docs/especificacion.md`](docs/especificacion.md) (escrita con el nombre de trabajo
"Pásalo"; el producto se llama **Al Toque**).

## Estado

Primera versión para **Windows** (el código ya es multiplataforma y corre en Linux; macOS llega en v1).

| Área | Hecho |
|---|---|
| Descubrimiento | UDP multicast `239.255.45.83:45832` (TTL 1) + broadcast dirigido; anuncios cada 30–60 s, consulta al abrir el panel, `BYE` al salir; límites contra saturación; compañeros conocidos al instante desde caché ("verificando…") |
| Identidad | Clave ECDSA P-256 por equipo; `deviceId` = 128 bits del SHA-256 de la clave; color y símbolos de huella; clave privada protegida con DPAPI |
| Canal | TLS 1.3/1.2 **mutuo** con claves fijadas, ALPN `altoque/1`, solo cifrados AEAD con ECDHE; la identidad sale del certificado, nunca del JSON |
| Recepción | Modos **Nadie** · **Solo confianza** · **Todos durante 10 minutos** (vuelve solo); tarjeta de aceptación con quién, verificación, archivos, advertencias y mensaje; cambiar carpeta; rechazar y bloquear; aceptación automática por compañero |
| Receptor blindado | Máquina de estados estricta, tokens de un solo uso por archivo, saneamiento de rutas (recorrido, nombres reservados, `:`/ADS, controles y bidi), `.altoque-part` + SHA-256 + renombrado atómico, Mark of the Web (`Zone.Identifier`, ZoneId=3), espacio libre, cuotas y plazos |
| Envío | Archivos y carpetas (incluidas las vacías), 1 a 4 conexiones de datos, **modo ráfaga** para miles de archivos pequeños, hash al vuelo, fechas originales, mensaje opcional, cancelar desde cualquiera de los dos lados |
| Emparejamiento | Código de **6 dígitos** comparado en ambas pantallas, con compromiso previo (contra intermediarios); bloqueo de 5 min tras 3 fallos |
| Conexión manual | Por IP o por **código de conexión** (`K7Q2-9XM4`) para redes que bloquean el multicast |
| Windows | Menú clásico **"Enviar con Al Toque ›"** con tus compañeros (sin firma ni DLL, en `HKCU`), **"Enviar a"**, inicio con Windows, regla de firewall solo para redes privadas, pausa en redes públicas, sonidos y "reducir movimiento" del sistema |
| Interfaz | Bandeja, panel rápido con radar de compañeros y zona para soltar, Chasqui animado (13 estados, respeta "reducir movimiento"), ventana flotante de progreso, historial, ajustes, bienvenida |
| Distribución | NativeAOT (sin .NET instalado), modo portable (`portable.flag`), instalador y actualizaciones con Velopack, CI en GitHub Actions |

Pendiente para próximas versiones (ver la hoja de ruta de la especificación): reanudación tras cortes, pausa manual,
envío a varios destinatarios y grupos, notificaciones nativas del Windows App SDK, animaciones Lottie de Chasqui,
inglés revisado, macOS e integración con gestores de archivos de Linux, IPv6 en el descubrimiento.

## Instalar

Descarga el instalador o la versión portable desde **Actions → Compilar y probar → Artifacts** (o desde
*Releases*). Las instrucciones paso a paso están en [`docs/INSTALAR.md`](docs/INSTALAR.md).

## Uso

1. Abre Al Toque en las dos PC. Elige cómo te verán tus compañeros.
2. **Emparejen sus equipos**: en ambos, panel → *Emparejar*; uno elige al otro y los dos confirman que ven el mismo código.
3. Desde entonces: **clic derecho** sobre archivos o carpetas → *Mostrar más opciones* (Windows 11) → **Enviar con Al Toque** → tu compañero.

Para alguien que todavía no es de confianza, el receptor activa **Recibir de: Todos (10 min)**; la solicitud llega
marcada como *No verificado*. Si la red no deja que los equipos se encuentren (Wi-Fi de invitados), usa *Mi código* y
*Conectar por código*.

### Línea de comandos

```text
AlToque send --to "PC de Andrés" [--message "texto"] <rutas…>
AlToque send --pick <rutas…>      Elegir destino en el panel
AlToque send --code <rutas…>      Conectar por código o IP
AlToque show | quit
AlToque unintegrate               Quitar el menú contextual y el inicio automático
```

El CLI entrega las rutas al agente residente por una tubería con nombre limitada al usuario actual y termina en
milisegundos; si el agente no está corriendo, lo inicia.

## Compilar

Requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet test tests/AlToque.Core.Tests          # 85 pruebas, incluidas dos instancias reales hablando por TLS
dotnet run --project src/AlToque              # ejecutar en modo desarrollo
```

Publicar para Windows con NativeAOT (en Windows, con las herramientas de C++ de Visual Studio):

```powershell
dotnet publish src/AlToque -c Release -r win-x64 -o publish
New-Item publish/portable.flag   # opcional: modo portable, guarda todo junto al .exe
```

El flujo de GitHub Actions ([`.github/workflows/build.yml`](.github/workflows/build.yml)) corre las pruebas en Linux y
Windows, publica `win-x64` y `win-arm64` con NativeAOT, hace una prueba de humo del `.exe` en Windows (agente, menú
contextual, "Enviar a" y CLI) y sube como artefactos el `.zip` portable y el instalador de Velopack. Al publicar una etiqueta `v*` crea la versión en GitHub, desde donde la app se actualiza sola.

Para desarrollar con dos instancias en el mismo equipo, cada una con sus propios datos:

```bash
ALTOQUE_HOME=/tmp/a dotnet run --project src/AlToque
ALTOQUE_HOME=/tmp/b dotnet run --project src/AlToque
```

## Arquitectura

```text
src/AlToque.Core      Núcleo sin interfaz (compatible con NativeAOT)
  Security/           Identidad, DPAPI, sellado HMAC de la configuración, huella visual
  Storage/            Ajustes, lista de confianza, caché de direcciones, historial
  Discovery/          Anuncios UDP, directorio de compañeros, código de conexión
  Protocol/           Tramas [u32 longitud][u8 tipo][contenido], mensajes JSON estrictos, conexión TLS mutua
  Transfer/           Emisor, servidor receptor, saneamiento de nombres, plan de recepción, escritura segura
  Pairing/            Emparejamiento con compromiso y código de 6 dígitos
  Ipc/                Tubería local entre el CLI y el agente
  Agent/              AgentHost: orquesta todo lo anterior
src/AlToque           App de escritorio Avalonia 12
  Chasqui/            Chasqui: control vectorial animado y sus estados
  Views/, ViewModels/ Panel, tarjetas de solicitud y progreso, ventanas
  Platform/           Menú contextual, "Enviar a", firewall, red pública, actualizaciones
tests/                xUnit: saneamiento, tramas, códigos, sellado, ataques simulados y envíos reales
```

Un solo proceso hace de agente y de interfaz; las ventanas se crean la primera vez que hacen falta y luego solo se
ocultan. Datos locales: `%APPDATA%\AlToque` (o `AlToque.data` junto al ejecutable en modo portable).

### Protocolo en breve

1. Descubrimiento: el anuncio UDP es solo una pista; un equipo se marca como verificado únicamente tras el apretón de
   manos TLS, comprobando que su clave corresponde al `deviceId` anunciado o a la clave fijada.
2. Control: `HELLO` → `OFFER` (manifiesto) → `DECISION` (con un token aleatorio por archivo aceptado).
3. Datos: 1 a 4 conexiones TLS del mismo emisor; cada archivo es `STREAM_OPEN` (token) → `CHUNK`… → `FILE_END`
   (SHA-256) → `FILE_RESULT`. Los archivos pequeños van seguidos por el mismo flujo, sin esperar respuesta.

### Decisiones respecto a la especificación

- **Notificaciones**: tarjetas propias (Avalonia) en lugar del Windows App SDK, que la especificación marca como riesgo
  con NativeAOT y sin paquete. Tienen botones, cuenta regresiva y progreso, y no roban el foco.
- **Chasqui**: dibujado con vectores y animado por fotogramas (sin Lottie hasta tener las animaciones diseñadas);
  el control `ChasquiView` es la abstracción que permitirá cambiar de tecnología sin tocar la lógica.
- **Clave privada**: PKCS#12 protegido con DPAPI del usuario (las claves CNG no exportables quedan para cuando haya
  firma de código).
- **Puertos**: TCP 45831 (si está ocupado, otro libre que se anuncia) y UDP 45832, no registrados en IANA.

## Seguridad

Ver la sección 12 de la especificación. Para reportar un fallo, abre un *issue* privado (*Security advisory*) en este
repositorio.

## Licencia

Código abierto bajo la [licencia MIT](LICENSE).
