using System;

namespace LivingPartner.Shared
{
    public enum RadishStage { Soil, Seed, Sprout, Leaves, Ripe }

    [Serializable]
    public sealed class RadishGarden
    {
        public const int Plots = 3, GrowSeconds = 45;
        // Zero is empty, -1 is a dry seed, a UTC timestamp starts growth after watering.
        public long[] beds = new long[Plots];
        public int harvested;
        public int stars, modules;
        public static readonly string[] ModuleNames = { "Rain Bell", "Sun Dial", "Twin Bloom" };
        public static readonly string[] Benefits = { "Waters new seeds", "Grows in 25s", "Doubles each harvest" };
        public static readonly int[] Prices = { 3, 6, 9 };
        public bool Owns(int module) => module >= 0 && module < Prices.Length && (modules & (1 << module)) != 0;
        public int Yield => Owns(2) ? 6 : 3;
        public int GrowthTime => Owns(1) ? 25 : GrowSeconds;

        public bool Buy(int module)
        {
            if (module < 0 || module >= Prices.Length || Owns(module) || stars < Prices[module]) return false;
            stars -= Prices[module];
            modules |= 1 << module;
            return true;
        }

        public static string Request(string words)
        {
            string order = (words ?? "").Trim().TrimEnd('.', '!', '?').Replace(" ", "").ToLowerInvariant();
            switch (order)
            {
                case "농사하자": case "농사시작": case "텃밭열어": case "텃밭열어줘": case "텃밭보여줘": case "openmygarden": case "showmethegarden": case "openthestargarden": case "let'sfarm": case "openthegarden": return "gardenOpen";
                case "텃밭닫아": case "텃밭닫아줘": case "텃밭치워줘": case "closethegarden": return "gardenClose";
                default: return "";
            }
        }

        public RadishStage Stage(int plot, long now)
        {
            long planted = beds[plot];
            if (planted == 0) return RadishStage.Soil;
            if (planted < 0) return RadishStage.Seed;
            long age = now - planted;
            if (age >= GrowthTime) return RadishStage.Ripe;
            return age >= GrowthTime / 2 ? RadishStage.Leaves : RadishStage.Sprout;
        }

        public bool Tend(int plot, long now)
        {
            if (plot < 0 || plot >= Plots || now <= 0) return false;
            switch (Stage(plot, now))
            {
                case RadishStage.Soil: beds[plot] = Owns(0) ? now : -1; return true;
                case RadishStage.Seed: beds[plot] = now; return true;
                case RadishStage.Ripe: beds[plot] = 0; harvested++; stars += Yield; return true;
                default: return false;
            }
        }
    }
}
