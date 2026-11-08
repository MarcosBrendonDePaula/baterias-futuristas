using System.Collections.Generic;
using System.IO;
using System.Reflection;
using STRINGS;

namespace BateriasFuturistas
{
    internal static class ModText
    {
        private const string UiPrefix = "STRINGS.BATERIASFUTURISTAS.UI.";

        private static readonly Dictionary<string, string> Ui = new Dictionary<string, string>
        {
            { "UNSUPPORTED", "Must rest on a base or a rail, aligned with it" },
            { "DRAIN_START", "Drain through pipe" },
            { "DRAIN_STOP", "Stop draining" },
            { "RANGE_TITLE", "Sand temperature range" },
            { "RANGE_MAX", "Maximum" },
            { "RANGE_MIN", "Minimum" },
            { "RANGE_MAX_TIP", "Maximum temperature of the range" },
            { "RANGE_MIN_TIP", "Minimum temperature of the range" },
            { "SENSOR_PORT", "Temperature sensor" },
            { "SENSOR_ACTIVE", "Green: sand is inside the selected range" },
            { "SENSOR_INACTIVE", "Red: sand is outside the selected range" },
        };

        private static readonly string[][] Buildings =
        {
            new[] { "GRAVITYBATTERY", "Compact Gravity Battery", "A raised weight stores energy indefinitely, with no leakage.", "Legacy fixed-height tower." },
            new[] { "GRAVITYBATTERYBASE", "Gravity Battery Base", "The base holds the weight and connects the tower to power.", "Needs stacked rails and a motor on top. Stored energy grows with mass and tower height.\n\nDoes not lose charge while idle, but the motor produces heat while charging and discharging." },
            new[] { "GRAVITYBATTERYHYDRAULICBASE", "Hydraulic Gravity Base", "A sealed liquid tank serves as the tower weight.", "Fills through a liquid pipe. Needs stacked rails and a motor on top. Stored energy grows with mass and tower height.\n\nDoes not lose charge while idle, but the motor produces heat while charging and discharging." },
            new[] { "GRAVITYBATTERYRAIL", "Gravity Rail", "Each rail increases the height of the weight drop.", "Stack on a base or another rail. Maximum height depends on the base metal." },
            new[] { "GRAVITYBATTERYMOTOR", "Gravity Motor", "The motor lifts the weight and closes the top of the tower.", "Place on top of the last rail to complete the battery." },
            new[] { "THERMALSANDBATTERY", "Thermal Sand Battery", "Hot sand holds heat for a long time and turns water into steam.", "Heats sand with electricity. Above 150 °C, water from the pipe leaves as steam.\n\nSand does not lose heat while idle and is delivered by Duplicants. Use automation to cut or allow heating according to the temperature range." },
            new[] { "FLYWHEELBATTERY", "Flywheel Battery", "A spinning disc stores energy and returns it almost without loss, but slowly stops on its own.", "Charges and discharges very quickly. Loses charge quickly while idle." },
            new[] { "COMPRESSEDAIRBATTERY", "Compressed Air Battery", "Gas compressed in a tank stores energy.", "Capacity grows with gas received by pipe. Heats up while charging and cools while discharging." },
            new[] { "FLOWBATTERY", "Liquid Flow Battery", "Liquid tanks store large amounts of energy.", "Capacity grows with liquid received by pipe." },
            new[] { "HEATCOMPRESSOR", "Heat Compressor", "Compressing a gas into liquid pulls the heat out of it.", "Turns piped gas into liquid. The heat removed from the gas and the electricity used are released at the compressor." },
            new[] { "EXPANSIONVALVE", "Expansion Valve", "A narrow valve lets a little liquid through and it leaves as very cold gas.", "Turns piped liquid into a small flow of gas just above its boiling point. Needs no power." },
            new[] { "MOLTENSALTBATTERY", "Molten Salt Battery", "A huge mass of salt stores energy at high temperature.", "Needs salt delivered by Duplicants. The largest capacity in the mod, with low losses." },
        };

        public static string Get(string key) => Strings.Get(UiPrefix + key);

        public static void Register()
        {
            foreach (var entry in Ui)
            {
                Strings.Add(UiPrefix + entry.Key, entry.Value);
            }
            foreach (string[] building in Buildings)
            {
                string prefix = "STRINGS.BUILDINGS.PREFABS." + building[0];
                Strings.Add(prefix + ".NAME", UI.FormatAsLink(building[1], building[0]));
                Strings.Add(prefix + ".DESC", building[2]);
                Strings.Add(prefix + ".EFFECT", building[3]);
            }
            LoadTranslation();
        }

        private static void LoadTranslation()
        {
            Localization.Locale locale = Localization.GetLocale();
            if (locale == null)
            {
                return;
            }
            string folder = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "translations");
            string file = Path.Combine(folder, locale.Code + ".po");
            if (File.Exists(file))
            {
                // OverloadStrings only reaches LocString fields, and these texts live in the Strings table.
                foreach (var entry in Localization.LoadStringsFile(file, false))
                {
                    Strings.Add(entry.Key, entry.Value);
                }
            }
        }
    }
}
