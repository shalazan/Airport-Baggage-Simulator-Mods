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
