#!/usr/bin/env bash
# Instala el APK en el emulador, lanza la autoprueba (emparejar y enviar por TLS entre dos agentes del teléfono)
# y espera el resultado en logcat. Deja una captura de la pantalla en $2 para revisar la interfaz.
set -uo pipefail
apk="$1"
shot="${2:-android-pantalla.png}"
pkg=com.krostgames.altoque

# -g concede los permisos de antemano: la captura muestra la app y no el diálogo de notificaciones.
adb install -r -g "$apk" || exit 1
adb logcat -c
adb shell am start -W -n "$pkg/$pkg.MainActivity" --ez altoque_selftest true || exit 1

result=""
for _ in $(seq 1 120); do
  result=$(adb logcat -d -s AlToque:I | grep -m1 "ALTOQUE_SELFTEST" || true)
  [ -n "$result" ] && break
  sleep 2
done

sleep 4
adb exec-out screencap -p > "$shot" || true
echo "── Registro de Al Toque ──"
adb logcat -d -s AlToque:I | tail -60
if [ -z "$result" ]; then
  echo "La autoprueba no respondió."
  adb logcat -d | grep -iE "altoque|mono|dotnet|crash|fatal" | tail -120
  exit 1
fi
echo "$result"
[[ "$result" == *" OK "* ]]
