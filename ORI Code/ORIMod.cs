using Brutal.Numerics;
using KSA;
using StarMap.API;

namespace OnyxRandomIsh;

[StarMapMod]
public class Mod
{
    // ---- settings you choose ----
    private const double Radius = 50;          // metres from a firing pentaborane nozzle
    private const double LethalSeconds = 4;   // seconds of exposure before a kitten is removed
    private const double RecoverRate = 0.5;     // exposure lost per second outside the zone (0 = never recovers)
    private const bool DryRun = false;           // true = only log "WOULD KILL"

    public bool ImmediateUnload => false;
    private double _timer;
    private readonly Dictionary<string, double> _exposure = new();

    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OnyxRandomIsh.log");

   static Mod() => Log($"OnyxRandomIsh: mod class initialised (v2, DryRun={DryRun})");

    private static void Log(string msg)
    {
        Console.WriteLine(msg);
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { }
    }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded() => Log("OnyxRandomIsh: loaded");

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
        foreach (var b in all)
        {
            if (b is not Vehicle v || v is KittenEva) continue;

            var burning = false;
            foreach (var engine in v.Parts.Modules.Get<EngineController>())
            {
                if (!engine.IsActive) continue;
                foreach (var core in engine.Cores)
                    if (core.Reaction.Id == "Pentaborane") burning = true;   // your reaction Id
            }
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

        // 2. update each EVA kitten's exposure
        var victims = new List<KittenEva>();
        foreach (var a in all)
        {
            if (a is not KittenEva kitten) continue;

            var pos = kitten.GetPositionEcl();
            var nearest = double.MaxValue;
            foreach (var s in sources) nearest = Math.Min(nearest, (pos - s).Length());

            _exposure.TryGetValue(kitten.Id, out var e);
            e = nearest <= Radius ? e + step : Math.Max(0, e - RecoverRate * step);
            _exposure[kitten.Id] = e;

            if (e > 0) Log($"OnyxRandomIsh: {kitten.Id} exposure {e:F1}s (nearest nozzle {nearest:F1} m)");
            if (e >= LethalSeconds) victims.Add(kitten);
        }

        // 3. remove the victims after the loop (never change the list while looping)
        foreach (var kitten in victims)
        {
            Log($"OnyxRandomIsh: {(DryRun ? "WOULD KILL" : "KILLING")} {kitten.Id}");
            _exposure.Remove(kitten.Id);
            if (!DryRun) Universe.DestroyVehicle(kitten, CrewDisposition.Kill);
        }
    }
}