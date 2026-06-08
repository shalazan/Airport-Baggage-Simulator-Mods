// -----------------------------------------------------------------------------
// This file is part of an AI-assisted/generated mod for Airport Baggage Simulator.
// Developed with the assistance of Antigravity, an agentic AI coding assistant.
// -----------------------------------------------------------------------------

using _scripts._by_scene._game._upgrades._specific_upgrades;
using UnityEngine;

namespace CounterSorterMod
{
    public class Upgrade_Counter_AutoReset : Upgrade
    {
        [SerializeField]
        public CounterDirectionProvider provider;

        public override string GetKey()
        {
            return "counter-auto-reset";
        }

        public override int GetMaxLevel()
        {
            return 1;
        }

        public override void ApplyEffect()
        {
            if (provider != null)
            {
                provider.SetAutoReset(GetCurrentLevel() > 0);
            }
        }
    }
}
