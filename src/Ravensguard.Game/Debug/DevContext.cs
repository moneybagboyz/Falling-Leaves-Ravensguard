using System;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Debug;

namespace Ravensguard.Game.Debug;

/// <summary>Hooks the running sim exposes to dev tooling. Null until a game session wires them (Phase 0: nothing does yet).</summary>
public static class DevContext
{
    public static SimClock? Clock { get; set; }
    public static ulong? Seed { get; set; }
    /// <summary>Resolves an id string (e.g. "Person#3") to something inspectable.</summary>
    public static Func<string, IInspectable?>? Resolver { get; set; }
    /// <summary>The street view's horses and carts (horses-and-carts spec: the debug way to get a horse, a cart or mud): <c>horse [breed]</c>, <c>loan</c>, <c>cart [template]</c>, <c>mud [on|off]</c>. Null when no street is running.</summary>
    public static Func<string, string[], string>? Ride { get; set; }
    /// <summary>P5-A3: the street view room overlay (rooms, rooms off).</summary>
    public static Func<string[], string>? Rooms { get; set; }
    /// <summary>The street view's travel (travel-encounters-camp spec): <c>camp</c>, <c>wood</c>, <c>encounter [id]</c>, <c>kill [species]</c>, <c>butcher</c>. Null when no street is running.</summary>
    public static Func<string, string[], string>? Travel { get; set; }

    public static void RegisterBuiltins(DevCommandRegistry r, Action clear)
    {
        r.Register("time", "Show the current game time", _ => Clock is { } c ? $"{c.Now} (speed {c.Speed})" : "no sim running");
        r.Register("seed", "Show the world seed", _ => Seed is { } s ? s.ToString() : "no world seed");
        r.Register("inspect", "inspect <id> - show an entity in the inspector", a =>
        {
            if (a.Length != 1) return "usage: inspect <id>";
            var target = Resolver?.Invoke(a[0]);
            if (target == null) return Resolver == null ? "inspect: no sim running (stub)" : $"inspect: nothing found for '{a[0]}'";
            SimInspector.Instance?.Show(target);
            return $"inspecting {target.InspectTitle}";
        });
        r.Register("horse", "horse [breed] - a horse beside the hero (palfrey, packhorse, draught, mule, donkey, ox)", a => Ride?.Invoke("horse", a) ?? "no street running");
        r.Register("loan", "loan - a palfrey on loan for three game days (the onboarding hook)", a => Ride?.Invoke("loan", a) ?? "no street running");
        r.Register("cart", "cart [cart|ox_wagon|hay_wain|covered_wagon|handcart|sledge] - a template vehicle with a team and a load beside the hero", a => Ride?.Invoke("cart", a) ?? "no street running");
        r.Register("mud", "mud [on|off] - force deep mud under the wheels", a => Ride?.Invoke("mud", a) ?? "no street running");
        r.Register("camp", "camp - make camp here (or open the camp panel)", a => Travel?.Invoke("camp", a) ?? "no street running");
        r.Register("wood", "wood - 50 kg of firewood and 6 kg of bread into the camp stock", a => Travel?.Invoke("wood", a) ?? "no street running");
        r.Register("encounter", "encounter [id] - put a road encounter on the road ahead (bandit_ambush_small, merchant_cart, wolves, ...)", a => Travel?.Invoke("encounter", a) ?? "no street running");
        r.Register("kill", "kill [species] - a game animal killed here off the land's stock (red_deer, boar, roe_deer, hare, fox)", a => Travel?.Invoke("kill", a) ?? "no street running");
        r.Register("butcher", "butcher - butcher the oldest carcass into the hero's pack", a => Travel?.Invoke("butcher", a) ?? "no street running");
        r.Register("rooms", "rooms [off] - outline the rooms RoomDetector v2 recognises around the hero (P5-A3)", a => Rooms?.Invoke(a) ?? "no street running");
        r.Register("clear", "Clear the console log", _ => { clear(); return ""; });
    }
}
