using System;
using System.Reflection;
using HarmonyLib;
using KMod;
using UnityEngine;

namespace BateriasFuturistas
{
    public sealed class BateriasFuturistasMod : UserMod2
    {
        private const uint TestedBuild = 744825;
        private const string LogPrefix = "[BateriasFuturistas] ";

        public override void OnLoad(Harmony harmony)
        {
            WarnIfUntestedBuild();
            ApplyPatchesIndependently(harmony);
        }

        private static void WarnIfUntestedBuild()
        {
            uint build = KleiVersion.ChangeList;
            if (build == TestedBuild)
            {
                Debug.Log($"{LogPrefix}Build do jogo testada: {build}");
            }
            else
            {
                Debug.LogWarning($"{LogPrefix}Build {build} ainda nao testada; ultima testada: {TestedBuild}");
            }
        }

        // PatchAll stops at the first failure and the game then disables the whole mod.
        private static void ApplyPatchesIndependently(Harmony harmony)
        {
            int applied = 0;
            int failed = 0;
            foreach (Type type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                if (!type.IsDefined(typeof(HarmonyPatch), false))
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    applied++;
                }
                catch (Exception e)
                {
                    failed++;
                    Debug.LogWarning($"{LogPrefix}Patch desligado: {type.FullName}: {e.Message}");
                }
            }
            Debug.Log($"{LogPrefix}Patches aplicados={applied} falhas={failed}");
        }
    }
}
