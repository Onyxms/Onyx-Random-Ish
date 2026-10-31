using Brutal.Numerics;
using KSA;
using StarMap.API;

namespace OnyxRandomIsh;

[StarMapMod]
public class Mod
{
    // ---- settings you choose ----
    private const double Radius = 50;          // metres from a firing pentaborane nozzle
    private const double LethalSeconds = 3;   // seconds of exposure before a kitten is removed
    private const double RecoverRate = 0.5;     // exposure lost per second outside the zone (0 = never recovers)
    private const bool DryRun = false;           // true = only log "WOULD KILL"
    private const double Falloff = 0.5;           // 1 = danger falls off linearly with distance, 2 = falls off faster
    private const double Variation = 0.25;      // 0 = every kitten identical, 0.25 = each kitten is +/-25% tougher or weaker

    public bool ImmediateUnload => false;
    private double _timer;
    private readonly Dictionary<string, double> _exposure = new();

    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OnyxRandomIsh.log");

    static Mod() => Log($"OnyxRandomIsh: mod class initialised (v3, DryRun={DryRun})");

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

            // danger is 1 right at a nozzle and falls to 0 at the edge of the radius
            var danger = nearest < Radius ? Math.Pow(1 - nearest / Radius, Falloff) : 0;

            _exposure.TryGetValue(kitten.Id, out var e);
            if (danger > 0) e += step * danger * Susceptibility(kitten.Id);
            else e = Math.Max(0, e - RecoverRate * step);
            _exposure[kitten.Id] = e;

            var shown = nearest == double.MaxValue ? "none" : $"{nearest:F1} m";
            if (e > 0) Log($"OnyxRandomIsh: {kitten.Id} exposure {e:F1}/{LethalSeconds}s (danger {danger:F2}, nearest nozzle {shown})");
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