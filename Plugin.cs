using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace RangeMultiplier
{
    // Hace que las cartas de rango de Rogue Tower MULTIPLIQUEN de verdad el rango
    // de la torre (x2, x3, ... de forma acumulativa) en vez de sumar una cantidad
    // plana o escalar solo el bonus por elevacion.
    //
    // En el juego base el rango se calcula de forma aditiva en Tower.SetStats():
    //   range = baseRange + heightBonus/2 * (1 + heightRangeBonusMultiplier) + bonusRange
    // Las cartas de rango (p.ej. "Longbow") suben 'heightRangeBonusMultiplier' (solo
    // escala el bonus por altura), no multiplican el rango total. Este mod detecta
    // esas cartas (campo 'range' != 0 O 'heightRangeBonusMultiplier' != 0) y aplica
    // un multiplicador real sobre el rango final de la torre.
    [BepInPlugin(GUID, "Range Multiplier", "1.1.0")]
    public class RangeMultiplierPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.fran.roguetower.rangemultiplier";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<bool> CfgSuppressFlat;
        internal static ConfigEntry<bool> CfgVerbose;
        internal static ConfigEntry<string> CfgMultipliers;
        internal static ConfigEntry<bool> CfgApplyAll;
        internal static ConfigEntry<float> CfgDefault;

        // Multiplicador acumulado por tipo de torre, y uno global.
        internal static readonly Dictionary<TowerType, float> Mult = new Dictionary<TowerType, float>();
        internal static float GlobalMult = 1f;

        internal struct Keyword { public string word; public Regex rx; public float factor; }
        internal static readonly List<Keyword> Keywords = new List<Keyword>();
        private static object _lastManager;

        private void Awake()
        {
            Log = Logger;

            CfgEnabled = Config.Bind("General", "Enabled", true,
                "Activa o desactiva el mod por completo.");
            CfgApplyAll = Config.Bind("General", "ApplyToAllRangeCards", true,
                "Si es true (por defecto), CUALQUIER carta que afecte al rango (campo 'range' o 'heightRangeBonusMultiplier') usa DefaultMultiplier cuando su titulo no coincide con ninguna palabra de CardMultipliers. Las cartas de rango de Rogue Tower (p.ej. Longbow) NO se llaman 'double/triple', asi que esto es lo que hace que el mod actue de serie.");
            CfgDefault = Config.Bind("General", "DefaultMultiplier", 2f,
                "Multiplicador aplicado a cada carta de rango cuyo titulo no coincide con una palabra (con ApplyToAllRangeCards=true). 2 = duplicar. Se acumula: dos cartas seguidas = x4.");
            CfgMultipliers = Config.Bind("General", "CardMultipliers",
                "quintuple:5,quintuplicar:5,quadruple:4,cuadruple:4,cuadruplicar:4,triple:3,triplicar:3,double:2,doble:2,duplicar:2",
                "Lista 'palabra:factor' separada por comas. Se compara contra el titulo de la carta por PALABRA COMPLETA (sin distinguir mayusculas); la primera coincidencia gana. Util para dar un factor distinto a una carta concreta segun su nombre real (mira las lineas [carta] del log).");
            CfgSuppressFlat = Config.Bind("General", "SuppressVanillaFlatRange", true,
                "Si es true, en las cartas con campo 'range' plano != 0 anula esa suma plana y aplica SOLO el multiplicador (para que 'doble' sea exactamente x2). No afecta a las cartas tipo Longbow (heightRangeBonusMultiplier), que conservan su efecto vanilla ademas del multiplicador.");
            CfgVerbose = Config.Bind("General", "LogCards", true,
                "Registra en el log cada carta de mejora de torre (titulo, tipo, rango plano y heightMult). Util para ver los nombres reales de las cartas. NOTA: con SuppressVanillaFlatRange, el texto '+Range' del boton de construccion puede no reflejar el multiplicador; el rango REAL (circulo de alcance y panel de la torre) si lo hace.");

            ParseKeywords();
            CfgMultipliers.SettingChanged += (s, e) => ParseKeywords();

            new Harmony(GUID).PatchAll();
            Log.LogInfo("Range Multiplier 1.1.0 cargado: las cartas de rango ahora multiplican el rango.");
        }

        private static void ParseKeywords()
        {
            Keywords.Clear();
            foreach (var part in CfgMultipliers.Value.Split(','))
            {
                var p = part.Trim();
                if (p.Length == 0) continue;
                int idx = p.LastIndexOf(':');
                if (idx <= 0) continue;
                string key = p.Substring(0, idx).Trim().ToLowerInvariant();
                string val = p.Substring(idx + 1).Trim();
                if (key.Length == 0) continue;
                if (!float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
                    || f <= 0f || float.IsNaN(f) || float.IsInfinity(f))
                {
                    Log?.LogWarning($"CardMultipliers: entrada ignorada (factor invalido): '{p}'");
                    continue;
                }
                // Coincidencia por palabra completa (evita falsos positivos por subcadena).
                var rx = new Regex(@"(^|[^\p{L}\p{N}])" + Regex.Escape(key) + @"($|[^\p{L}\p{N}])",
                    RegexOptions.CultureInvariant);
                Keywords.Add(new Keyword { word = key, rx = rx, factor = f });
            }
        }

        internal static float MatchFactor(string title)
        {
            if (string.IsNullOrEmpty(title)) return 0f;
            string t = title.ToLowerInvariant();
            foreach (var kv in Keywords)
                if (kv.rx.IsMatch(t)) return kv.factor;
            return 0f;
        }

        // Detecta una partida nueva (cambia la instancia del TowerManager) y reinicia los multiplicadores.
        internal static void MaybeReset()
        {
            object cur = TowerManager.instance;
            if (!ReferenceEquals(cur, _lastManager))
            {
                _lastManager = cur;
                Mult.Clear();
                GlobalMult = 1f;
                if (CfgVerbose != null && CfgVerbose.Value)
                    Log?.LogInfo("Nueva partida detectada: multiplicadores de rango reiniciados.");
            }
        }

        internal static float GetMult(TowerType type)
        {
            float m = GlobalMult;
            if (Mult.TryGetValue(type, out float v)) m *= v;
            return m;
        }
    }

    // Intercepta la aplicacion de la carta: convierte el efecto de rango en multiplicador.
    [HarmonyPatch(typeof(TowerUpgradeCard), "Upgrade")]
    internal static class Patch_TowerUpgradeCard_Upgrade
    {
        private static readonly AccessTools.FieldRef<TowerUpgradeCard, float> RangeField =
            AccessTools.FieldRefAccess<TowerUpgradeCard, float>("range");
        private static readonly AccessTools.FieldRef<TowerUpgradeCard, int> HeightMultField =
            AccessTools.FieldRefAccess<TowerUpgradeCard, int>("heightRangeBonusMultiplier");
        private static readonly AccessTools.FieldRef<TowerUpgradeCard, TowerType> TypeField =
            AccessTools.FieldRefAccess<TowerUpgradeCard, TowerType>("towerType");

        internal struct State
        {
            public bool apply;
            public float factor;
            public TowerType type;
            public bool suppressed;
            public float savedRange;
        }

        private static void Prefix(TowerUpgradeCard __instance, out State __state)
        {
            __state = default(State);
            try
            {
                if (!RangeMultiplierPlugin.CfgEnabled.Value) return;
                RangeMultiplierPlugin.MaybeReset();

                float flat = RangeField(__instance);
                int heightMult = HeightMultField(__instance);
                TowerType type = TypeField(__instance);
                string title = __instance.title ?? "";

                if (RangeMultiplierPlugin.CfgVerbose.Value)
                    RangeMultiplierPlugin.Log.LogInfo(
                        $"[carta] titulo='{title}' torre={type} rangoPlano={flat.ToString(CultureInfo.InvariantCulture)} heightMult={heightMult}");

                // Carta de rango = aporta rango plano O sube el multiplicador por altura.
                bool isRangeCard = flat != 0f || heightMult != 0;
                if (!isRangeCard) return;

                float factor = RangeMultiplierPlugin.MatchFactor(title);
                if (factor <= 0f)
                {
                    if (RangeMultiplierPlugin.CfgApplyAll.Value)
                        factor = RangeMultiplierPlugin.CfgDefault.Value;
                    else
                        return; // titulo no reconocido y no aplicamos a todas: dejar la carta vanilla
                }
                if (factor <= 0f) return;

                __state.apply = true;
                __state.factor = factor;
                __state.type = type;

                // Solo anulamos la suma plana 'range' (no el efecto Longbow de altura).
                if (RangeMultiplierPlugin.CfgSuppressFlat.Value && flat != 0f)
                {
                    __state.suppressed = true;
                    __state.savedRange = flat;
                    RangeField(__instance) = 0f;
                }
            }
            catch (Exception ex)
            {
                __state = default(State);
                RangeMultiplierPlugin.Log?.LogError("Prefix Upgrade: " + ex);
            }
        }

        private static void Postfix(TowerUpgradeCard __instance, State __state)
        {
            try
            {
                if (!__state.apply) return;

                if (__state.type == TowerType.Global)
                    RangeMultiplierPlugin.GlobalMult *= __state.factor;
                else
                {
                    float cur = RangeMultiplierPlugin.Mult.TryGetValue(__state.type, out float v) ? v : 1f;
                    RangeMultiplierPlugin.Mult[__state.type] = cur * __state.factor;
                }

                RangeMultiplierPlugin.Log.LogInfo(
                    $"[rango x{__state.factor}] {__state.type} -> multiplicador total x{RangeMultiplierPlugin.GetMult(__state.type)}");

                // Refresca todas las torres ya colocadas para que el efecto se note al instante.
                if (TowerManager.instance != null)
                    TowerManager.instance.UpdateAllTowers();
            }
            catch (Exception ex)
            {
                RangeMultiplierPlugin.Log?.LogError("Postfix Upgrade: " + ex);
            }
        }

        // Se ejecuta SIEMPRE (incluso si Upgrade() lanza): restaura el campo 'range' del asset.
        private static void Finalizer(TowerUpgradeCard __instance, State __state)
        {
            if (!__state.suppressed) return;
            try { RangeField(__instance) = __state.savedRange; }
            catch (Exception ex) { RangeMultiplierPlugin.Log?.LogError("Finalizer Upgrade: " + ex); }
        }
    }

    // Aplica el multiplicador sobre el rango final calculado por la torre.
    [HarmonyPatch(typeof(Tower), "SetStats")]
    internal static class Patch_Tower_SetStats
    {
        private static readonly AccessTools.FieldRef<Tower, float> RangeBacking =
            AccessTools.FieldRefAccess<Tower, float>("<range>k__BackingField");

        private static void Postfix(Tower __instance)
        {
            try
            {
                if (!RangeMultiplierPlugin.CfgEnabled.Value) return;
                RangeMultiplierPlugin.MaybeReset();
                float m = RangeMultiplierPlugin.GetMult(__instance.towerType);
                if (m != 1f)
                    RangeBacking(__instance) *= m;
            }
            catch (Exception ex)
            {
                RangeMultiplierPlugin.Log?.LogError("Postfix SetStats: " + ex);
            }
        }
    }
}
