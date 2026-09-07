# Snapshot de sesión — GTA IV Presence (Luis / Niko)

> Guardado para retomar contexto en la próxima sesión.

## Estado ACTUAL (verificado en vivo)

- **Personaje**: `base+0xd73240` resuelto: 0=Niko, 1=Johnny, 2=Luis — funciona ("Niko Belic" OK con cambio a "The Cousins Belic").
- **Vehículo**: `[base+0x1200184]` válido = en coche → `+0x1654` → entry nombre → mapa (CAVCADE→Cavalcade). Verificado.
- **Velocidad (mph)**: delta de posición `car+0x90/94/98`. Verificado 46→51 mph.
- **Misión**: `0x119af24` NO distingue misión vs free roam (4 en ambos). El nombre sale del buffer `base+0xd734e8` ("TBoGT - Clocking Off") y **queda atascado en la última misión** tras terminar. → BUG pendiente.
- **Layout**: details = situación (zona | personaje | arma | a pie/en coche · mph), state = stats (wanted | health | cash).

## BUGS PENDIENTES (prioridad)

1. **Arma**: implementada como placeholder con weapon slot `ped+0x768` (int) y ammo `ped+0x5e8`, vía player ped:
   - En coche: `[vhp+0x50]` = player ped (probado con Luis: ammo 89 pistol en +0x5e8, 0x768=16 estable pero NO se pueden equipar armas conduciendo → quedó fijo).
   - A pie: cadenas de punteros `base+0x14c6998` etc. → pero **no se resuelven con Niko** (dieron 0). El player ped real a pie es otro.
   - Nombres mapeados (de la sesión Luis): 0=Punches, 15=Knife, 16=Pistol, 22=AK47, 28=Grenade.
   - Config: `WeaponSlotOffset=768`, `WeaponAmmoOffset=5e8`, `PlayerPedPtrOffset/Offsets`, `DriverPedOffset=50`, `PedHealthField=20`, `HealthMatchTolerance=4.0`.
2. **Misión vs no-misión**: quiere diferenciar free roam vs en misión (sin quedarse pegado al título viejo).
3. **Health**: `base+0x119d06c` da valores raros/imposibles (31 → 139 → 36 → 29, cambia solo). NO es el health real del jugador. Debe leerse del player ped real.

## Datos del player ped (para retomar el bug del health/on-foot)

- En coche (Luis): `[base+0x1200184]` → veh → `+0x50` → player ped. Health match válido con `0x119d06c` cuando era conductor (Luis).
- Por coordenadas: las coords del jugador (`0xd736b0/b4/b8`) NO aparecen dentro del player ped a pie (scan por posición en 0x1800000-0x2d00000 dio 1 solo hit `0x1ca37b0` que es un CVEctor3 suelto, no el ped).
- Punteros a pie candidatos (matches por health, sesión Luis): `0x14bcaa8`, `0x14c6998/ac/b8`, `0x14c9a88`, `0x14cac94`, `0x14cfba8/bbc/bc8` → pool `0x24fxxx`. Con Niko dieron 0 → buscar cadena del player ped para Niko (distinta).

## Repo

- GitHub: https://github.com/dizzi1222/GTA-IV-Presence (origin, rama main).
- Local real: `G:\Mi unidad\Mi unidad\[Herramientas]\MultiPresence - Discord Presence para multilpes juegos; DMC, Dark souls, Resident etc (WindowsOnly)\Otros Games\GTA IV Presence`.
- Archivos en producción: `C:\Games\Grand Theft Auto IV Complete Edition\IVRPC\` (IVRPC.cs + compile.bat → IVRPC.exe, config.json, run.bat).
- Último commit: `f0c1ee4` (arma placeholder + vehículo + personaje).
- Compilar: `cmd /c compile.bat` (csc v4.0.30319). Log en vivo: `ivrpc_run2.log` (usar `run_log.bat`).

## Scripts de diagnóstico (C:\Users\Administrator\AppData\Local\Temp\opencode\)

- `health_live.py` — muestrea global health vs player ped.
- `find_ped_full2.py` — localiza player ped por coordenadas a pie.
- `dump_ped_now.py` — dump floats del ped candidato.