using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace BuildQoL
{
    // One optional build-tool tweak. Each has its own [Tweaks] toggle, its own Harmony patch classes and the list of game
    // members it relies on; Plugin.Load binds, checks and patches every tweak in Plugin.All. A new tweak is a new file
    // (patches + a Tweak subclass) and one entry in Plugin.All; nothing else in the plugin changes.
    internal abstract class Tweak
    {
        /// <summary>The [Tweaks] toggle's name, e.g. "KeepSelection".</summary>
        public abstract string Name { get; }

        /// <summary>The toggle's description in the config file.</summary>
        protected abstract string Toggle { get; }

        public virtual bool DefaultOn => true;

        /// <summary>"Namespace.Type:member" for every game member the tweak touches. A missing one turns the tweak off at load.</summary>
        public abstract string[] Needed { get; }

        /// <summary>Harmony patch classes applied when the tweak is on. May be empty for a tweak that only calls the game.</summary>
        public virtual Type[] Patches => Array.Empty<Type>();

        public ConfigEntry<bool> Enabled { get; private set; }

        /// <summary>True once the tweak is on, its members were found and its patches applied.</summary>
        public bool Active { get; internal set; }

        public void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Tweaks", Name, DefaultOn, Toggle);
            BindMore(cfg);
        }

        /// <summary>Further [Tweaks] entries (keys, limits). Called right after the toggle is bound.</summary>
        protected virtual void BindMore(ConfigFile cfg) { }

        /// <summary>Per-frame hooks, called by the Driver only while Active, each in its own try with a once-only log.</summary>
        public virtual void Update() { }
        public virtual void LateUpdate() { }

#if DEVBUILD
        /// <summary>Dev-build hooks: [Dev] config entries, probe patches, and a tick with the seconds since the world loaded.</summary>
        public virtual void DevBind(ConfigFile cfg) { }
        public virtual void DevPatch(Harmony h) { }
        public virtual void DevTick(float gameT) { }
#endif
    }
}
