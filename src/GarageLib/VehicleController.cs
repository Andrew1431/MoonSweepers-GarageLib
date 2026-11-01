using System;
using MoonCleaners;
using UnityEngine;

namespace GarageLib
{
    /// <summary>
    /// Base class for custom vehicle controllers (flight, hover, ...). Subclasses need an <c>(IntPtr ptr) : base(ptr)</c>
    /// constructor; the core registers them with IL2CPP. Seats, enter/exit, cargo and storage stay with the game's
    /// MoonPrototypeVehicle on the same GameObject.
    /// </summary>
    public class VehicleController : MonoBehaviour
    {
        public VehicleController(IntPtr ptr) : base(ptr) { }

        public VehicleDefinition Definition { get; private set; }
        public MoonPrototypeVehicle Vehicle { get; private set; }
        /// <summary>Storage id ("garage/&lt;g&gt;/&lt;index&gt;/&lt;owner&gt;") used for vanilla upgrade and cargo lookups.</summary>
        public string Storage { get; private set; }

        /// <summary>Skip MoonPrototypeVehicle.FixedUpdate (wheel physics, drift, flipping). Default true.</summary>
        public virtual bool SuppressVanillaFixedUpdate => true;
        /// <summary>Skip MoonPrototypeVehicle.Update (input, cleaning, lights, enter/exit keys). Default false.</summary>
        public virtual bool SuppressVanillaUpdate => false;

        internal void Bind(VehicleDefinition d, MoonPrototypeVehicle v, string storage)
        {
            Definition = d; Vehicle = v; Storage = storage;
            try { OnBound(); } catch (Exception e) { CoreLog.Error($"{GetType().Name}.OnBound threw for '{d.Id}': {e}"); }
        }

        /// <summary>Called once after the controller is attached and Definition/Vehicle/Storage are set.</summary>
        protected virtual void OnBound() { }

        /// <summary>Vanilla upgrade level for this vehicle, read through the game so upgrade mods apply.</summary>
        protected int UpgradeLevel(MoonVehicleUpgrades.Kind kind) => MoonVehicles.VanillaLevel(Storage, kind);
        /// <summary>Custom upgrade level for this vehicle, read through <see cref="MoonVehicles.Upgrades"/>.</summary>
        protected int CustomUpgradeLevel(string id) => MoonVehicles.CustomLevel(Storage, id);
    }
}
