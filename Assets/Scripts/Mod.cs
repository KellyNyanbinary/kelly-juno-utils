using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// A singleton object representing this mod that is instantiated and initialized when the mod is loaded.
/// </summary>
// ReSharper disable once ClassCannotBeInstantiated
public class Mod : ModApi.Mods.GameMod
{
    /// <summary>
    /// Prevents a default instance of the <see cref="Mod"/> class from being created.
    /// </summary>
    [SuppressMessage("ReSharper", "RedundantBaseConstructorCall")]
    private Mod() : base()
    {
    }

    /// <summary>
    /// Gets the singleton instance of the mod object.
    /// </summary>
    /// <value>The singleton instance of the mod object.</value>
    public static Mod Instance { get; } = GetModInstance<Mod>();

    protected override void OnModInitialized()
    {
        base.OnModInitialized();

        try
        {
            var harmony = new Harmony("kelly.utils");
            harmony.PatchAll(typeof(Mod).Assembly);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[KellyUtils] Failed to apply Harmony patches: " + ex);
        }
    }
}