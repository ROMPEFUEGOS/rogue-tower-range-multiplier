# RangeMultiplier

> Un mod de [BepInEx](https://github.com/BepInEx/BepInEx) para **Rogue Tower** que hace que las cartas de mejora de rango **multipliquen** de verdad el alcance de la torre (×2, ×3, … y de forma acumulativa) en lugar de sumar una cantidad casi inapreciable.

---

## Español

### ¿Qué hace y por qué?

En el juego base, el rango de una torre se calcula de forma **aditiva** (en `Tower.SetStats()`):

```
range = baseRange + heightBonus/2 · (1 + heightRangeBonusMultiplier) + bonusRange
```

Las cartas de rango (por ejemplo **`Longbow`**) **no multiplican** el alcance: solo suben `heightRangeBonusMultiplier`, que escala únicamente la pequeña parte del rango que aporta la **elevación**. En terreno llano apenas se nota, así que una carta de "rango" se siente irrelevante.

**RangeMultiplier** cambia eso: cuando coges una carta que afecta al rango, aplica un **multiplicador real** sobre el rango **final** de la torre. Por defecto cada carta de rango **duplica** (×2) el alcance de ese tipo de torre, y varias cartas **se acumulan multiplicativamente** (dos seguidas = ×4). Así un upgrade de rango por fin se nota.

> Nota: Rogue Tower **no tiene** cartas llamadas literalmente "Double Range" / "Triple Range". Las cartas de rango se llaman cosas como `Longbow I/II/III`. Por eso el mod, de serie, trata **cualquier** carta de rango como un ×`DefaultMultiplier` (configurable), y además permite asignar factores concretos por nombre.

### ¿Cómo funciona?

Dos parches de Harmony:

1. **`TowerUpgradeCard.Upgrade`** (Prefix + Postfix + Finalizer)
   - Detecta una carta de rango si su campo `range` (plano) ≠ 0 **o** su `heightRangeBonusMultiplier` ≠ 0.
   - Decide el factor: si el título coincide con una palabra de `CardMultipliers` usa ese factor; si no, usa `DefaultMultiplier` (con `ApplyToAllRangeCards = true`).
   - Acumula el factor en un multiplicador **por tipo de torre** (o global) y llama a `TowerManager.UpdateAllTowers()` para que se note al instante.
   - Si la carta tiene rango plano y `SuppressVanillaFlatRange = true`, anula esa suma plana (y la restaura en el `Finalizer`, incluso si algo falla).

2. **`Tower.SetStats`** (Postfix)
   - Tras calcular la torre su `range` final, multiplica el resultado por el multiplicador acumulado de ese tipo de torre.

El mod **reinicia** los multiplicadores al empezar una partida nueva (detecta el cambio de instancia de `TowerManager`), y todos los parches están envueltos en `try/catch`: si algo fallara, degrada a comportamiento vanilla en vez de romper la partida.

### Instalación

1. Necesitas **[BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases)** (x86 / 32-bit, que es la build del juego) instalado en Rogue Tower. Arranca el juego una vez para generar `BepInEx/plugins`.
2. Descarga `RangeMultiplier.dll` desde la página de **[Releases](https://github.com/ROMPEFUEGOS/rogue-tower-range-multiplier/releases)**.
3. Cópialo en `Rogue Tower/BepInEx/plugins/`.
4. Arranca el juego. En `BepInEx/LogOutput.log` verás:
   ```
   Range Multiplier 1.1.0 cargado: las cartas de rango ahora multiplican el rango.
   ```

La configuración se genera en `BepInEx/config/com.fran.roguetower.rangemultiplier.cfg` tras el primer arranque.

### Nota para Linux / Proton

Rogue Tower corre vía **Proton**, y BepInEx (que se engancha mediante `winhttp.dll`) **no carga** salvo que le digas a Wine/Proton que use la DLL local. Opción recomendada — en Steam, clic derecho en el juego → Propiedades → *Opciones de lanzamiento*:

```
WINEDLLOVERRIDES="winhttp.dll=n,b" %command%
```

(alternativa: añadir un override de DLL `winhttp = native,builtin` en el registro del prefijo de Proton). Sin esto, ni BepInEx ni el mod se cargan.

### Configuración

Sección `[General]` de `BepInEx/config/com.fran.roguetower.rangemultiplier.cfg`:

| Clave | Tipo | Por defecto | Descripción |
|---|---|---|---|
| `Enabled` | bool | `true` | Activa/desactiva el mod. |
| `ApplyToAllRangeCards` | bool | `true` | Cualquier carta de rango cuyo título no coincida con una palabra usa `DefaultMultiplier`. Es lo que hace que el mod actúe de serie (las cartas reales no se llaman "double/triple"). |
| `DefaultMultiplier` | float | `2` | Multiplicador por carta de rango sin palabra coincidente. 2 = duplicar. Se acumula. |
| `CardMultipliers` | string | `…double:2,doble:2,triple:3,…` | Lista `palabra:factor`. Coincidencia por **palabra completa** (sin mayúsculas); la primera gana. Para dar un factor concreto a una carta por su nombre real. |
| `SuppressVanillaFlatRange` | bool | `true` | En cartas con rango plano, anula la suma plana y aplica solo el multiplicador. No afecta a las cartas tipo Longbow. |
| `LogCards` | bool | `true` | Registra cada carta (`título`, `tipo`, `rangoPlano`, `heightMult`) en el log. |

> ⚠️ Con `SuppressVanillaFlatRange`, el texto **`+Range`** del botón de construcción puede no reflejar el multiplicador. El **alcance real** (el círculo de rango y el panel de la torre) **sí** lo refleja.

### Descubrir y afinar por nombre de carta

1. Con `LogCards = true`, juega y coge cartas. Mira `BepInEx/LogOutput.log`:
   ```
   [carta] titulo='Longbow II' torre=Crossbow rangoPlano=0 heightMult=1
   [rango x2] Crossbow -> multiplicador total x4
   ```
2. Para dar a una carta concreta un factor distinto, añade su nombre a `CardMultipliers`, p. ej. `longbow:3`. Se re-parsea en caliente al editar el ajuste.

### Compilar desde el código fuente

Requiere el **.NET SDK** y una referencia a `Assembly-CSharp.dll` **del propio juego** (no redistribuible, no se incluye aquí).

```bash
# opcional si tu Rogue Tower no está en la ruta por defecto:
export ROGUE_TOWER_DIR="/ruta/a/steamapps/common/Rogue Tower"
./build.sh
```

El proyecto va contra `netstandard2.0` (Mono / Unity 32-bit). Si no quieres compilar, la DLL ya construida está en **[Releases](https://github.com/ROMPEFUEGOS/rogue-tower-range-multiplier/releases)**.

---

## English

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Rogue Tower** that makes range-upgrade cards **actually multiply** a tower's range (×2, ×3, … and stacking) instead of adding a barely-noticeable flat amount.

**Why:** vanilla range is additive — `range = baseRange + heightBonus/2·(1 + heightRangeBonusMultiplier) + bonusRange`. Range cards (e.g. **`Longbow`**) only raise `heightRangeBonusMultiplier`, which scales just the small elevation part, so they feel useless on flat ground. Rogue Tower has **no** card literally named "Double/Triple Range", so by default this mod treats **every** range card as a ×`DefaultMultiplier` (default ×2), and lets you set per-name factors via `CardMultipliers`.

**How:** two Harmony patches — `TowerUpgradeCard.Upgrade` detects range cards (flat `range` ≠ 0 **or** `heightRangeBonusMultiplier` ≠ 0), accumulates a per-tower-type multiplier and refreshes towers; `Tower.SetStats` multiplies the final computed range. Multipliers reset on a new run; all patches are wrapped in `try/catch` and degrade to vanilla on error.

**Install:** needs BepInEx 5.x (x86). Drop `RangeMultiplier.dll` (from [Releases](https://github.com/ROMPEFUEGOS/rogue-tower-range-multiplier/releases)) into `Rogue Tower/BepInEx/plugins/`. On Linux/Proton, add the Steam launch option `WINEDLLOVERRIDES="winhttp.dll=n,b" %command%` so BepInEx loads. Config: `BepInEx/config/com.fran.roguetower.rangemultiplier.cfg` (see the Spanish table). Build from source with `dotnet`/`./build.sh` (needs the game's non-redistributable `Assembly-CSharp.dll`).

---

## Licencia / License

MIT — ver [LICENSE](LICENSE). Rogue Tower y sus assets son propiedad de sus respectivos dueños; esto es un mod no oficial hecho por fans. / Unofficial fan-made mod.
