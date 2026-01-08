using System;

namespace OGNES.Utils
{
    public static class GammaUtils
  {
    const double Alpha = 0.1115721959217312597711924126997473649680614471435546875;
    const double Beta = 1.1115721959217312875267680283286608755588531494140625;
    const double Cut = 0.0912863421177801115380390228892792947590351104736328125;
    
    // Reference: https://github.com/L-Spiro/BeesNES/blob/main/Src/Utilities/LSNUtilities.h
    public static double LinearTosRGB_Precise(double val)
        {
            if (val <= 0.003039934639778431833823102437008856213651597499847412109375)
                return val * 12.92321018078785499483274179510772228240966796875;
            return 1.055 * Math.Pow(val, 1.0 / 2.4) - 0.055;
        }

        public static double SMPTE240MtoLinear(double val)
        {
            if (val < 0.0913) return val / 4.0;
            return Math.Pow((val + Alpha) / Beta, 1.0 / 0.45);
        }

        /// <summary>
        /// A proper CRT curve with WHITE and BRIGHTNESS controls.
        /// </summary>
        public static double CrtProperToLinear(double val, double lw = 1.0, double db = 0.0181)
        {
            const double alpha1 = 2.6;
            const double alpha2 = 3.0;
            const double vc = 0.35;
            double k = lw / Math.Pow(1.0 + db, alpha1);

            if (val < vc)
            {
                return k * Math.Pow(vc + db, alpha1 - alpha2) * Math.Pow(val + db, alpha2);
            }
            return k * Math.Pow(val + db, alpha1);
        }
 
        /// <summary>
        /// A proper CRT curve based on measurements (Curve 2).
        /// </summary>
        public static double CrtProper2ToLinear(double val)
        {
            
            if (val >= 0.36) return Math.Pow(val, 2.31);
            
            double dFrac = val / 0.36;
            double part1 = (val <= Cut) ? (val / 4.0) : Math.Pow((val + Alpha) / Beta, 1.0 / 0.45);
            return (part1 * (1.0 - dFrac)) + (dFrac * Math.Pow(val, 2.31));
        }
    }
}
