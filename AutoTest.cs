using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace BateriasFuturistas
{
    // Runs only while autotest.flag exists beside the mod DLL; the flag's text is the save to load.
    internal static class AutoTest
    {
        private static readonly string Folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        public static string FlagPath => Path.Combine(Folder, "autotest.flag");

        public static string ResultPath => Path.Combine(Folder, "autotest_result.txt");

        public static bool Enabled => File.Exists(FlagPath);

        private static bool loadRequested;

        [HarmonyPatch(typeof(App), nameof(App.QuitCode))]
        private static class TraceQuit
        {
            private static void Prefix(int exitCode)
            {
                if (Enabled)
                {
                    Debug.Log($"[AutoTest] App.QuitCode({exitCode}) chamado por:\n{Environment.StackTrace}");
                }
            }
        }

        [HarmonyPatch(typeof(MainMenu), "OnSpawn")]
        private static class LoadTestSave
        {
            private static void Postfix(MainMenu __instance)
            {
                if (Enabled && !loadRequested)
                {
                    loadRequested = true;
                    __instance.StartCoroutine(LoadAfterMenuSettles());
                }
            }

            // Loading while the menu's background parsing runs aborts that thread and raises the error dialog.
            private static IEnumerator LoadAfterMenuSettles()
            {
                yield return new WaitForSecondsRealtime(20f);
                LoadScreen.DoLoad(File.ReadAllText(FlagPath).Trim());
            }
        }

        [HarmonyPatch(typeof(Game), "OnSpawn")]
        private static class StartRunner
        {
            private static void Postfix(Game __instance)
            {
                if (Enabled)
                {
                    __instance.gameObject.AddComponent<AutoTestRunner>();
                }
            }
        }
    }

    internal sealed class AutoTestRunner : MonoBehaviour
    {
        private const float Tolerance = 2f;
        private const float ChargeTargetJoules = 5000f;
        private const float DischargeJoules = 1000f;
        private const int Rails = 5;
        private const int ExpectedHeight = GravityBatteryBaseConfig.HeightTiles + Rails + 2;

        private readonly StringBuilder report = new StringBuilder();
        private int passed;
        private int failed;
        private bool finished;

        private void Start()
        {
            Debug.Log("[AutoTest] iniciado");
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(8f);
            IEnumerator steps = Steps();
            while (true)
            {
                object current;
                try
                {
                    if (!steps.MoveNext())
                    {
                        break;
                    }
                    current = steps.Current;
                }
                catch (Exception e)
                {
                    Fail("excecao", e.ToString());
                    break;
                }
                yield return current;
            }
            Finish();
        }

        private void Finish()
        {
            finished = true;
            Record($"TOTAL passed={passed} failed={failed}");
            File.WriteAllText(AutoTest.ResultPath, report.ToString());
            File.Delete(AutoTest.FlagPath);
            Application.Quit();
        }

        private void Pass(string name, string detail)
        {
            passed++;
            Record($"PASS {name}: {detail}");
        }

        private void Record(string line)
        {
            report.AppendLine(line);
            Debug.Log("[AutoTest] " + line);
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[AutoTest] jogo recebeu pedido de fechar; concluido=" + finished);
        }

        private void Fail(string name, string detail)
        {
            failed++;
            Record($"FAIL {name}: {detail}");
        }

        private void Check(string name, bool condition, string detail)
        {
            if (condition)
            {
                Pass(name, detail);
            }
            else
            {
                Fail(name, detail);
            }
        }

        private void CheckNear(string name, float actual, float expected)
        {
            Check(name, Mathf.Abs(actual - expected) <= Tolerance, $"actual={actual} expected={expected}");
        }

        private static WaitForSecondsRealtime Wait(float seconds) => new WaitForSecondsRealtime(seconds);

        private static GameObject Build(string id, int cell, SimHashes material)
        {
            BuildingDef def = Assets.GetBuildingDef(id);
            var elements = new List<Tag> { ElementLoader.FindElementByHash(material).tag };
            return def.Build(cell, Orientation.Neutral, null, elements, 293f, false);
        }

        private static int FindSite()
        {
            WorldContainer world = ClusterManager.Instance.activeWorld;
            int x = world.WorldOffset.x + world.WorldSize.x / 2;
            int y = world.WorldOffset.y + world.WorldSize.y - 40;
            return Grid.XYToCell(x, y);
        }

        private static void ClearArea(int origin, int halfWidth, int height)
        {
            for (int y = -1; y < height; y++)
            {
                for (int x = -halfWidth; x <= halfWidth; x++)
                {
                    int cell = Grid.OffsetCell(origin, x, y);
                    SimMessages.ReplaceElement(cell, SimHashes.Oxygen, CellEventLogger.Instance.DebugTool, 1f, 293f);
                }
            }
        }

        private IEnumerator WaitForMotor(GravityMotor motor, GravityMotorState expected, string name)
        {
            float deadline = Time.realtimeSinceStartup + 6f;
            while (motor.State != expected && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Check(name, motor.State == expected, $"state={motor.State}");
        }

        private IEnumerator Steps()
        {
            SpeedControlScreen.Instance.SetSpeed(1);
            SpeedControlScreen.Instance.Unpause(false);

            int origin = FindSite();
            ClearArea(origin, 12, ExpectedHeight + 3);
            yield return Wait(2f);

            BuildingDef railDef = Assets.GetBuildingDef(GravityBatteryRailConfig.ID);
            bool airValid = railDef.IsValidBuildLocation(railDef.BuildingPreview, origin, Orientation.Neutral, false, out string airReason);
            Check("trilho_no_ar_bloqueado", !airValid, $"valid={airValid} reason={airReason}");

            GameObject baseObject = Build(GravityBatteryBaseConfig.ID, origin, SimHashes.Steel);
            yield return Wait(1f);
            var battery = baseObject.GetComponent<GravityBatteryBase>();
            int railCell = Grid.OffsetCell(origin, 0, GravityBatteryBaseConfig.HeightTiles);
            bool supported = railDef.IsValidBuildLocation(railDef.BuildingPreview, railCell, Orientation.Neutral, false, out string baseReason);
            Check("trilho_sobre_base_permitido", supported, $"valid={supported} reason={baseReason}");

            var rails = new List<GameObject>();
            for (int index = 0; index < Rails; index++)
            {
                rails.Add(Build(GravityBatteryRailConfig.ID, Grid.OffsetCell(railCell, 0, index), SimHashes.Steel));
                yield return null;
            }
            yield return Wait(1f);
            Check("sem_motor_incompleta", !battery.IsComplete, $"height={battery.MeasuredHeight}");

            GameObject motorObject = Build(GravityBatteryMotorConfig.ID, Grid.OffsetCell(railCell, 0, Rails), SimHashes.Steel);
            yield return Wait(3f);
            CheckNear("altura_detectada", battery.MeasuredHeight, ExpectedHeight);
            var motor = motorObject.GetComponent<GravityMotor>();

            Storage storage = baseObject.GetComponent<Storage>();
            CheckNear("limite_aco_16t", storage.capacityKg, GravityBattery.SteelCableMassKg);
            storage.AddOre(SimHashes.IronOre, 400f, 293f, byte.MaxValue, 0);
            yield return Wait(2f);
            float expectedCapacity = 400f * GravityBattery.JoulesPerKgPerTile * ExpectedHeight;
            CheckNear("capacidade_massa_altura", battery.MassCapacity, expectedCapacity);

            float added = 0f;
            bool chargingSeen = false;
            float chargeDeadline = Time.realtimeSinceStartup + 25f;
            while (added < ChargeTargetJoules && Time.realtimeSinceStartup < chargeDeadline)
            {
                float room = Mathf.Max(0f, battery.capacity - battery.JoulesAvailable);
                float amount = Mathf.Min(room, ChargeTargetJoules - added);
                if (amount > 0f)
                {
                    battery.AddEnergy(amount);
                    added += amount;
                }
                chargingSeen |= motor.State == GravityMotorState.Charging;
                yield return null;
            }
            Check("motor_carregando", chargingSeen, $"state={motor.State}");
            yield return Wait(2f);
            float charged = battery.JoulesAvailable;
            CheckNear("perda_5_porcento_carga", charged, added * 0.95f);
            CheckNear("posicao_peso", battery.WeightCenterTiles * 1000f,
                battery.ExpectedWeightCenterTiles * 1000f);

            battery.ConsumeEnergy(DischargeJoules, false);
            yield return WaitForMotor(motor, GravityMotorState.Discharging, "motor_descarregando");
            yield return Wait(2f);
            CheckNear("perda_3_porcento_descarga", battery.JoulesAvailable, charged - DischargeJoules / 0.97f);

            float before = battery.JoulesAvailable;
            yield return Wait(6f);
            CheckNear("sem_perda_parada", battery.JoulesAvailable, before);
            Check("motor_parado", motor.State == GravityMotorState.Idle, $"state={motor.State}");

            Util.KDestroyGameObject(rails[2]);
            yield return Wait(3f);
            Check("trilho_removido_incompleta", !battery.IsComplete, $"height={battery.MeasuredHeight}");
            CheckNear("energia_mantida_sem_trilho", battery.JoulesAvailable, before);

            Build(GravityBatteryRailConfig.ID, Grid.OffsetCell(railCell, 0, 2), SimHashes.Steel);
            yield return Wait(3f);
            CheckNear("altura_apos_reparo", battery.MeasuredHeight, ExpectedHeight);
            CheckNear("energia_apos_reparo", battery.JoulesAvailable, before);

            Util.KDestroyGameObject(motorObject);
            yield return Wait(3f);
            Check("sem_motor_em_upgrade", !battery.IsComplete, $"height={battery.MeasuredHeight}");
            Build(GravityBatteryRailConfig.ID, Grid.OffsetCell(railCell, 0, Rails), SimHashes.Steel);
            yield return null;
            Build(GravityBatteryMotorConfig.ID, Grid.OffsetCell(railCell, 0, Rails + 1), SimHashes.Steel);
            yield return Wait(3f);
            CheckNear("altura_apos_upgrade", battery.MeasuredHeight, ExpectedHeight + 1);
            CheckNear("energia_apos_upgrade", battery.JoulesAvailable, before);

            yield return HydraulicSteps(Grid.OffsetCell(origin, 8, 0));
        }

        private IEnumerator HydraulicSteps(int origin)
        {
            GameObject baseObject = Build(GravityBatteryHydraulicConfig.ID, origin, SimHashes.Iron);
            int railCell = Grid.OffsetCell(origin, 0, GravityBatteryBaseConfig.HeightTiles);
            for (int index = 0; index < Rails; index++)
            {
                Build(GravityBatteryRailConfig.ID, Grid.OffsetCell(railCell, 0, index), SimHashes.Iron);
                yield return null;
            }
            Build(GravityBatteryMotorConfig.ID, Grid.OffsetCell(railCell, 0, Rails), SimHashes.Iron);
            yield return Wait(3f);
            var battery = baseObject.GetComponent<GravityBatteryBase>();
            Storage storage = baseObject.GetComponent<Storage>();
            CheckNear("hidraulica_altura", battery.MeasuredHeight, ExpectedHeight);
            CheckNear("hidraulica_limite_ferro_5t", storage.capacityKg, GravityHydraulicBattery.IronTankMassKg);
            storage.AddLiquid(SimHashes.Water, 300f, 293f, byte.MaxValue, 0);
            yield return Wait(2f);
            CheckNear("hidraulica_capacidade", battery.MassCapacity, 300f * GravityBattery.JoulesPerKgPerTile * ExpectedHeight);
            CheckNear("hidraulica_entrada_cano", baseObject.GetComponent<ConduitConsumer>().capacityKG, GravityHydraulicBattery.IronTankMassKg);
        }
    }
}
