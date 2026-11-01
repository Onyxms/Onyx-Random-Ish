using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using StarMap.API;

namespace OnyxRandomIsh;

[StarMapMod]
public class Mod
{
    // ---- settings you choose ----
    private const double InstantRadius = 5;      // metres: a kitten this close to a firing nozzle dies at once
    private const double Radius = 100;           // metres: beyond this distance a kitten is safe
    private const double NearSeconds = 2;        // seconds to kill a kitten just outside the instant zone
    private const double FarSeconds = 15;        // seconds to kill a kitten at the very edge of the radius
    private const double RecoverRate = 0.1;      // fraction of a lethal dose lost per second when safe
    private const double Variation = 0.25;       // 0 = every kitten identical, 0.25 = each kitten is +/-25% tougher or weaker
    private const bool DryRun = false;           // true = only log "WOULD KILL"

    public bool ImmediateUnload => false;
    private double _timer;
    private static readonly HashSet<string> _poisoned = new();   // kittens we have queued for removal; used to reword their alert
    private readonly HashSet<string> _pending = new();           // kittens already queued, so we never queue one twice

    private const string GreenExplosionId = "ORI_Explosion_ConflagrationGreen";   // from OnyxRandomIshExplosions.xml
    private static HashSet<string> _pentaVehicles = new();       // ids of vehicles that carry a pentaborane engine

    // The game's own "destroy at the next safe moment" method. It is not public, so we find it by name.
    private static readonly MethodInfo? _destroyDeferred =
        typeof(Universe).GetMethod("Destroy", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
    private readonly Dictionary<string, double> _exposure = new();

    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OnyxRandomIsh.log");

    static Mod() => Log($"OnyxRandomIsh: mod class initialised (v7, DryRun={DryRun})");

    private static void Log(string msg)
    {
        Console.WriteLine(msg);
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { }
    }

    // A fixed number per kitten, so the same kitten always has the same tolerance
    private static double Susceptibility(string id)
    {
        var h = 17;
        foreach (var c in id) h = unchecked(h * 31 + c);
        var unit = (h & 0xFFFF) / 65535.0;          // 0 to 1
        return 1.0 + Variation * (unit * 2 - 1);    // 1 +/- Variation
    }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        Log("OnyxRandomIsh: loaded");
        InstallPatches();
    }

    private static void InstallPatches()
    {
        var harmony = new Harmony("onyxrandomish.patches");

        // 1. reword the "killed in action" alert for kittens we have poisoned
        try
        {
            var alertType = typeof(Universe).Assembly.GetType("KSA.TimedAlert");
            var original = alertType?.GetMethod("CreateWarning", new[] { typeof(string), typeof(double) });
            if (original == null) { Log("OnyxRandomIsh: could not find TimedAlert.CreateWarning"); }
            else
            {
                var prefix = typeof(Mod).GetMethod(nameof(AlertPrefix), BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                Log("OnyxRandomIsh: death message patch installed");
            }
        }
        catch (Exception e) { Log($"OnyxRandomIsh: death message patch failed: {e}"); }

        // 2. swap the big orange explosion for the green one on pentaborane vehicles
        try
        {
            var original = AccessTools.Method(typeof(ExplosionSystem), "SpawnPresetInternal");
            if (original == null) { Log("OnyxRandomIsh: could not find ExplosionSystem.SpawnPresetInternal"); }
            else
            {
                var prefix = typeof(Mod).GetMethod(nameof(ExplosionPrefix), BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                Log("OnyxRandomIsh: green explosion patch installed");
            }
        }
        catch (Exception e) { Log($"OnyxRandomIsh: green explosion patch failed: {e}"); }
    }

    // Runs just before the game spawns an explosion. __0 is the explosion id, __1 says what exploded.
    private static void ExplosionPrefix(ref string __0, ref ExplosionContext __1)
    {
        try
        {
            if (__0 != "Explosion_Conflagration") return;
            var vehicle = __1.Vehicle;
            if (vehicle == null || !_pentaVehicles.Contains(vehicle.Id)) return;
            Log($"OnyxRandomIsh: green explosion for {vehicle.Id}");
            __0 = GreenExplosionId;
        }
        catch (Exception e) { Log($"OnyxRandomIsh: explosion patch error: {e}"); }
    }

    // Runs just before the game shows an alert. __0 is the alert text.
    private static void AlertPrefix(ref string __0)
    {
        if (__0 == null || !__0.Contains("killed in action")) return;
        foreach (var name in _poisoned)
        {
            if (!__0.Contains(name)) continue;
            __0 = __0.Replace("killed in action", "killed by pentaborane poisoning");
            _poisoned.Remove(name);
            return;
        }
    }

    [StarMapBeforeGui]
    public void OnBeforeUi(double dt)
    {
        try
        {
            _timer += dt;
            if (_timer < 0.25) return;
            var step = _timer;
            _timer = 0;
            Check(step);
        }
        catch (Exception e) { Log($"OnyxRandomIsh: {e}"); }
    }

    private void Check(double step)
    {
        var system = Universe.CurrentSystem;
        if (system == null) return;
        var all = system.All.UnsafeAsList();

        // 1. positions of every firing nozzle on a vehicle burning pentaborane
        var sources = new List<double3>();
        var penta = new HashSet<string>();
        foreach (var b in all)
        {
            if (b is not Vehicle v || v is KittenEva) continue;

            var burning = false;
            var hasPenta = false;
            foreach (var engine in v.Parts.Modules.Get<EngineController>())
            {
                foreach (var core in engine.Cores)
                {
                    if (core.Reaction.Id != "Pentaborane") continue;   // your reaction Id
                    hasPenta = true;
                    if (engine.IsActive) burning = true;
                }
            }
            if (hasPenta) penta.Add(v.Id);
            if (!burning) continue;

            var origin = v.GetPositionEcl();
            foreach (var m in v.Parts.RocketNozzles.ModulesAndAllStates)
            {
                if (m.State.ThrustFraction <= 0) continue;
                var locAsmb = v.PosAsmbToBody(m.State.ThrustLocationVehicleAsmb);
                var offset = double3.Unpack(ref locAsmb, 0).Transform(v.Body2Cce);
                sources.Add(origin + offset);
            }
        }

        _pentaVehicles = penta;   // remembered for the explosion patch

        // 2. update each EVA kitten's exposure
        var present = new HashSet<string>();
        foreach (var a in all)
            if (a is KittenEva k) present.Add(k.Id);
        _pending.RemoveWhere(n => !present.Contains(n));   // forget kittens that are really gone

        var victims = new List<KittenEva>();
        foreach (var a in all)
        {
            if (a is not KittenEva kitten) continue;
            if (_pending.Contains(kitten.Id)) continue;    // already queued for removal

            var pos = kitten.GetPositionEcl();
            var nearest = double.MaxValue;
            foreach (var s in sources) nearest = Math.Min(nearest, (pos - s).Length());

            // dose: 0 = unharmed, 1 = lethal
            _exposure.TryGetValue(kitten.Id, out var dose);
            if (nearest <= InstantRadius)
            {
                dose = 1;   // right next to a nozzle: instant death
            }
            else if (nearest < Radius)
            {
                // time to kill grows from NearSeconds to FarSeconds across the zone
                var killTime = NearSeconds + (FarSeconds - NearSeconds) * (nearest - InstantRadius) / (Radius - InstantRadius);
                dose += step / killTime * Susceptibility(kitten.Id);
            }
            else
            {
                dose = Math.Max(0, dose - RecoverRate * step);
            }
            _exposure[kitten.Id] = dose;

            var shown = nearest == double.MaxValue ? "none" : $"{nearest:F1} m";
            if (dose > 0) Log($"OnyxRandomIsh: {kitten.Id} dose {dose:P0} (nearest nozzle {shown})");
            if (dose >= 1) victims.Add(kitten);
        }

        // 3. queue the victims for removal (never change the list while looping)
        foreach (var kitten in victims)
        {
            Log($"OnyxRandomIsh: {(DryRun ? "WOULD KILL" : "KILLING")} {kitten.Id}");
            _exposure.Remove(kitten.Id);
            if (DryRun) continue;

            _poisoned.Add(kitten.Id);
            _pending.Add(kitten.Id);
            if (_destroyDeferred != null)
                _destroyDeferred.Invoke(null, new object[] { kitten.Id });   // removed at the game's next safe moment
            else
                Universe.DestroyVehicle(kitten, CrewDisposition.Kill);       // fallback if the game changes
        }
    }
}