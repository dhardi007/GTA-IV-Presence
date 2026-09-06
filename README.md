# GTA IV Presence (Discord Rich Presence para Grand Theft Auto IV)

Muestra "Jugando GTA IV · En Liberty City" en tu perfil de Discord mientras juegas al GTA IV, usando la conectividad Rich Presence (`arRPC`) integrada en **Vesktop**.

> Hecho para GTA IV Complete Edition (proceso `GTAIV.exe`). Si tu versión usa otro nombre de proceso, edítalo en `config.json`.

---

## Requisitos

- **Vesktop** instalado con la opción **arRPC** activada (ver paso 1).
- **GTA IV** instalado (el proceso a vigilar se pone en `config.json`).
- **Windows** (el script es un ejecutable nativo `.exe`).

---

## Instalación / ubicación

1. Copia la carpeta **`GTA IV Presence`** al lugar que prefieras, por ejemplo:
   - `C:\Games\Grand Theft Auto IV Complete Edition\IVRPC\` (junto al juego), o
   - el acceso que uses para tu colección de presences.
2. La carpeta es **portátil**: `run.bat` usa su propia ruta, así que no acopla nada a una ubicación fija.

---

## Paso 1 — Activar arRPC en Vesktop (una sola vez)

1. Abre **Vesktop** → **Ajustes** (icono de engranaje abajo a la izquierda).
2. En **Rich Presence** activa **"WebRichPresence (arRPC)"** (y deja el modo en **arRPC**).
3. **Reinicia Vesktop** (cierra y vuelve a abrir).
4. Comprueba que el puerto `6463` está escuchando:
   ```powershell
   Get-NetTCPConnection -State Listen -LocalPort 6463
   ```
   Si aparece una fila, ¡listo!

---

## Paso 2 — Configuración (`config.json`)

```json
{
  "ClientId": "1546020334782783539",     // App de Discord que aporta el nombre e iconos
  "ProcessNames": "GTAIV",               // Procesos del juego (separarlos con coma)
  "CheckIntervalSeconds": 5,             // Cada cuántos segundos revisa si el juego corre
  "Details": "Jugando GTA IV",           // Línea principal (blanca)
  "State": "En Liberty City",            // Línea secundaria (gris)
  "LargeImageKey": "gta-iv",             // Imagen grande (logo del juego)
  "LargeImageText": "Grand Theft Auto IV", // Texto al pasar el ratón sobre la imagen grande
  "SmallImageKey": "",                   // Imagen pequeña (vacío = no mostrar)
  "SmallImageText": ""
}
```

**Assets de imagen**: las imágenes se sirven desde los assets oficiales de la aplicación de Discord (`ClientId 1546020334782783539`).

- La clave **`gta-iv`** (logo) ya existe y es la que usa `LargeImageKey`.
- Para usar el **banner** como imagen grande:
  1. Ve a [Discord Developer Portal](https://discord.com/developers/applications) → tu app `1546020334782783539` → **Rich Presence** → **Art Assets** → **Add Asset**.
  2. Sube `assets/GTA-IV-banner.png` y nómbrala exactamente **`gta-iv-banner`**.
  3. En `config.json` cambia: `"LargeImageKey": "gta-iv-banner"`.
- Para la imagen pequeña con `GTA-IV-girls.png`: súbela como **`gta-iv-girls`** y pon `"SmallImageKey": "gta-iv-girls"`.

> Las claves tipo `imgi_110_...` que genera CustomRP NO funcionan aquí (son imágenes alojadas en el servicio de CustomRP). Si vienes de un `.crp` de CustomRP, re-sube el PNG directamente en el portal de la app.

---

## Paso 3 — Ejecutar

1. Arranca **GTA IV** (puede estar abierto antes o después; se detecta solo al comprobarlo, cada 5 s).
2. Ejecuta **`run.bat`** (o el acceso directo que apunte a él).
3. Se abre una ventana de consola con el estado:
   ```
   [OK] Conectado a Discord via arRPC WebSocket 6463.
   [OK] 03:12:04 GTA IV detectado -> presencia activada.
   ```
4. **Deja esa ventana abierta** mientras juegues. Para cerrar, cierra la ventana o usa el Administrador de tareas (proceso `IVRPC.exe`).
5. Revisa tu perfil de Discord/Vesktop: deberías ver el logo de GTA IV con "Jugando GTA IV · En Liberty City".

> Tip: si quieres que se abra solo, crea un acceso directo a `run.bat` en el escritorio y úsalo cada vez antes de jugar.

---

## Solución de problemas

| Síntoma | Causa | Solución |
|---|---|---|
| `[!] No hay arRPC WebSocket: ...No es posible conectar...` | arRPC no está escuchando en `6463` | Activa arRPC en Ajustes de Vesktop y reinícialo (paso 1). |
| `[!] Vesktop abierto pero sin RPC` | arRPC apagado o cayó el puerto | Repite el paso 1 y vuelve a ejecutar `run.bat`. |
| No aparece presencia en Discord | El juego no está corriendo o `ProcessNames` no coincide | Confirma el nombre exacto del proceso y que `GTAIV` esté abierto. |
| Icono genérico/en blanco en vez del arte | `LargeImageKey` no existe en los assets de la app | Sube el asset con la clave exacta (paso 2) o cambia la clave. |
| `crash.txt` aparece en la carpeta | Error interno del script | Reenvía el contenido de `crash.txt` para revisarlo. |

---

## Recompilar desde el código fuente (opcional)

La carpeta incluye `IVRPC.cs` (fuente en C#5/.NET Framework) y `compile.bat`. Si editas el código:

```cmd
compile.bat
```

Debe terminar con `CSC_EXIT=0`.

---

## Archivos

```
GTA IV Presence\
├── IVRPC.exe        ← el lanzador del Rich Presence
├── config.json      ← configuración (app, texto, imágenes, procesos)
├── run.bat          ← ejecutar esto para empezar
├── compile.bat      ← recompilar IVRPC.exe (opcional)
├── IVRPC.cs         ← código fuente (opcional)
├── assets\
│   ├── GTA-IV.png           ← logo oficial
│   ├── GTA-IV-banner.png    ← banner para imagen grande
│   └── GTA-IV-girls.png     ← imagen secundaria opcional
└── README.md        ← este archivo
```