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
        private readonly Dictionary<string, Vector3> shots = new Dictionary<string, Vector3>();

        private void Start()
        {
            Application.runInBackground = true;
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

        // Waits in simulation time, so an autosave or a pause cannot skip the battery updates.
        private static IEnumerator Wait(float seconds)
        {
            float target = GameClock.Instance.GetTime() + seconds;
            float deadline = Time.realtimeSinceStartup + seconds + 12f;
            while (GameClock.Instance.GetTime() < target && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            if (GameClock.Instance.GetTime() < target)
            {
                Debug.Log($"[AutoTest] espera travada: pausado={SpeedControlScreen.Instance.IsPaused} velocidade={SpeedControlScreen.Instance.GetSpeed()} timeScale={Time.timeScale}");
                SpeedControlScreen.Instance.Unpause(false);
            }
        }

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
            SpeedControlScreen.Instance.SetSpeed(2);
            SpeedControlScreen.Instance.Unpause(false);

            string salt = Strings.Get("STRINGS.BUILDINGS.PREFABS.MOLTENSALTBATTERY.NAME");
            string locale = Localization.GetLocale()?.Code;
            Check("traducao_do_jogo", locale != "pt_BR" || salt.Contains("Sal Fundido"), $"locale={locale} nome={salt}");
            int origin = FindSite();
            for (int y = -3; y <= 30; y++)
            {
                for (int x = -32; x <= 20; x++)
                {
                    int cell = Grid.OffsetCell(origin, x, y);
                    if (Grid.IsValidCell(cell))
                    {
                        Grid.Reveal(cell);
                    }
                }
            }
            ClearArea(origin, 14, ExpectedHeight + 3);
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
            CheckNear("potencia_cresce_com_altura", battery.ChargeWatts,
                GravityBattery.MotorWatts * (ExpectedHeight + 1) / 6f);
            yield return ConnectPowerAndCheck("gravitacional", baseObject, 1, () => battery.JoulesAvailable);
            AddShot("gravitacional", baseObject);

            yield return HydraulicSteps(Grid.OffsetCell(origin, 8, 0));
            yield return ThermalSteps(Grid.OffsetCell(origin, -8, 0));
        }

        private IEnumerator ThermalSteps(int origin)
        {
            const float Sand = 1000f;
            const float Hot = 573.15f;
            for (int x = -ThermalSandBatteryConfig.HalfWidth; x <= ThermalSandBatteryConfig.HalfWidth; x++)
            {
                SimMessages.ReplaceElement(Grid.OffsetCell(origin, x, -1), SimHashes.Granite,
                    CellEventLogger.Instance.DebugTool, 1000f, 293f);
            }
            yield return Wait(2f);
            GameObject building = Build(ThermalSandBatteryConfig.ID, origin, SimHashes.Steel);
            yield return Wait(1f);
            var battery = building.GetComponent<ThermalSandBattery>();
            Storage[] storages = building.GetComponents<Storage>();
            storages[0].AddOre(SimHashes.Sand, Sand, 293.15f, byte.MaxValue, 0);
            yield return null;
            CheckNear("termica_massa_areia", battery.SandMass, Sand);

            float start = battery.Temperature;
            battery.ApplyHeater(100f);
            CheckNear("termica_aquecimento", battery.Temperature,
                start + ThermalSandBattery.HeaterKdtuPerSecond * 100f / (Sand * ThermalSandBattery.SandHeatCapacityKdtuPerKg));

            battery.SetTemperature(ThermalSandBattery.MaxTemperature);
            battery.ApplyHeater(100f);
            CheckNear("termica_limite_500c", battery.Temperature, ThermalSandBattery.MaxTemperature);

            int generatorCell = Grid.OffsetCell(origin, ThermalSandBatteryConfig.HalfWidth + 1, 0);
            var wires = Game.Instance.electricalConduitSystem;
            for (int x = 0; x <= ThermalSandBatteryConfig.HalfWidth + 1; x++)
            {
                Build("Wire", Grid.OffsetCell(origin, x, 0), SimHashes.Copper);
            }
            yield return Wait(2f);
            for (int x = 0; x <= ThermalSandBatteryConfig.HalfWidth + 1; x++)
            {
                UtilityConnections links = x == 0 ? UtilityConnections.Right
                    : x == ThermalSandBatteryConfig.HalfWidth + 1 ? UtilityConnections.Left
                    : UtilityConnections.Left | UtilityConnections.Right;
                wires.AddConnection(links, Grid.OffsetCell(origin, x, 0), true);
            }
            wires.ForceRebuildNetworks();
            GameObject generator = Build("DevGenerator", generatorCell, SimHashes.Copper);
            battery.SetTemperature(293.15f);
            yield return Wait(10f);
            var energy = building.GetComponent<EnergyConsumer>();
            Check("termica_energia_por_fio", energy.IsPowered, $"connected={energy.IsConnected} powered={energy.IsPowered} watts={energy.WattsUsed}");
            var blocked = new List<string>();
            foreach (var flag in building.GetComponent<Operational>().Flags)
            {
                if (!flag.Value)
                {
                    blocked.Add(flag.Key.Name);
                }
            }
            Check("termica_esquenta_com_energia", battery.Temperature > 293.65f,
                $"temp={battery.Temperature} bloqueios={string.Join(",", blocked)}");

            Util.KDestroyGameObject(generator);
            yield return Wait(2f);

            battery.SetTemperature(373.15f);
            yield return Wait(5f);
            CheckNear("termica_nao_perde_calor", battery.Temperature, 373.15f);
            storages[1].AddLiquid(SimHashes.Water, 10f, 293.15f, byte.MaxValue, 0);
            yield return Wait(2f);
            var ports = building.GetComponent<LogicPorts>();
            Check("termica_sensor_fora_da_faixa", ports.GetOutputValue(ThermalSandBattery.RangePortId) == 0,
                $"temp={battery.Temperature} sinal={ports.GetOutputValue(ThermalSandBattery.RangePortId)}");
            Check("termica_fria_sem_vapor", battery.SteamStored <= 0f, $"steam={battery.SteamStored}");

            battery.SetTemperature(Hot);
            float before = battery.Temperature;
            yield return Wait(6f);
            Check("termica_gera_vapor", battery.SteamStored > 0f, $"steam={battery.SteamStored} water={battery.WaterStored}");
            Check("termica_areia_esfria", battery.Temperature < before, $"before={before} after={battery.Temperature}");
            Check("termica_sensor_dentro_da_faixa", ports.GetOutputValue(ThermalSandBattery.RangePortId) == 1,
                $"temp={battery.Temperature} sinal={ports.GetOutputValue(ThermalSandBattery.RangePortId)}");
            float waterDeadline = Time.realtimeSinceStartup + 20f;
            while (battery.WaterStored > 0f && Time.realtimeSinceStartup < waterDeadline)
            {
                yield return null;
            }
            yield return Wait(1f);
            float steamBefore = battery.SteamStored;
            Build("GasConduit", Grid.OffsetCell(origin, ThermalSandBatteryConfig.HalfWidth, 7), SimHashes.Copper);
            yield return Wait(4f);
            Check("termica_vapor_sai_no_cano", battery.SteamStored < steamBefore, $"before={steamBefore} after={battery.SteamStored}");
            report.AppendLine($"INFO termica_final: temp={battery.Temperature} steam={battery.SteamStored} water={battery.WaterStored}");
            yield return Wait(2f);
            CheckDeliveryRequested("termica", building);
            AddShot("termica", building);
            float fluids = battery.SteamStored + battery.WaterStored;
            int inlet = building.GetComponent<Building>().GetUtilityInputCell();
            Build("LiquidConduit", inlet, SimHashes.Copper);
            yield return Wait(2f);
            Game.Instance.liquidConduitFlow.AddElement(inlet, SimHashes.Water, 1f, 293f, byte.MaxValue, 0);
            yield return Wait(4f);
            Check("termica_recebe_agua_por_cano", battery.SteamStored + battery.WaterStored > fluids + 0.5f,
                $"before={fluids} after={battery.SteamStored + battery.WaterStored}");
            yield return SimpleBatterySteps(Grid.OffsetCell(origin, -9, 0));
        }

        private IEnumerator ChargeAndCheck(string name, MassBattery battery, float amount, float keptFraction)
        {
            float start = battery.JoulesAvailable;
            battery.AddEnergy(amount);
            yield return Wait(2f);
            float kept = start + amount * keptFraction;
            float idleAllowance = kept * battery.idleLossPerSecond * 10f + Tolerance;
            Check(name + "_perda_carga", Mathf.Abs(battery.JoulesAvailable - kept) <= idleAllowance,
                $"actual={battery.JoulesAvailable} expected={kept} allowance={idleAllowance}");
        }

        private IEnumerator SimpleBatterySteps(int origin)
        {
            yield return PlaceFloor(origin);
            GameObject flywheelObject = Build(FlywheelBatteryConfig.ID, origin, SimHashes.Steel);
            yield return Wait(1f);
            var flywheel = flywheelObject.GetComponent<MassBattery>();
            CheckNear("volante_capacidade", flywheel.FullCapacity, FlywheelBatteryConfig.Capacity);
            flywheel.AddEnergy(50000f);
            yield return Wait(1f);
            Check("volante_carga_rapida", flywheel.JoulesAvailable > 45000f, $"joules={flywheel.JoulesAvailable}");
            float spinning = flywheel.JoulesAvailable;
            yield return Wait(4f);
            Check("volante_perde_parado", flywheel.JoulesAvailable < spinning, $"before={spinning} after={flywheel.JoulesAvailable}");
            yield return ConnectPowerAndCheck("volante", flywheelObject, 2, () => flywheel.JoulesAvailable);

            yield return PlaceFloor(Grid.OffsetCell(origin, -6, 0));
            GameObject airObject = Build(CompressedAirBatteryConfig.ID, Grid.OffsetCell(origin, -6, 0), SimHashes.Steel);
            yield return Wait(1f);
            var air = airObject.GetComponent<MassBattery>();
            airObject.GetComponent<Storage>().AddGasChunk(SimHashes.Oxygen, 100f, 293f, byte.MaxValue, 0, false);
            yield return Wait(1f);
            CheckNear("ar_capacidade", air.FullCapacity, 100f * CompressedAirBatteryConfig.JoulesPerKg);
            yield return ChargeAndCheck("ar", air, 10000f, 1f - CompressedAirBatteryConfig.Loss);
            yield return PipeAndCheck("ar", airObject, "GasConduit", Game.Instance.gasConduitFlow, SimHashes.Oxygen);
            yield return DrainAndCheck("ar", airObject, "GasConduit");
            AddShot("ar", airObject);
            yield return ConnectPowerAndCheck("ar", airObject, 2, () => air.JoulesAvailable);

            yield return PlaceFloor(Grid.OffsetCell(origin, 6, 16));
            GameObject flowObject = Build(FlowBatteryConfig.ID, Grid.OffsetCell(origin, 6, 16), SimHashes.Steel);
            yield return Wait(1f);
            var flow = flowObject.GetComponent<MassBattery>();
            flowObject.GetComponent<Storage>().AddLiquid(SimHashes.Water, 500f, 293f, byte.MaxValue, 0);
            yield return Wait(1f);
            CheckNear("fluxo_capacidade", flow.FullCapacity, 500f * FlowBatteryConfig.JoulesPerKg);
            yield return ChargeAndCheck("fluxo", flow, 20000f, 1f - FlowBatteryConfig.Loss);
            yield return PipeAndCheck("fluxo", flowObject, "LiquidConduit", Game.Instance.liquidConduitFlow, SimHashes.Water);
            yield return DrainAndCheck("fluxo", flowObject, "LiquidConduit");
            AddShot("fluxo", flowObject);
            yield return ConnectPowerAndCheck("fluxo", flowObject, 3, () => flow.JoulesAvailable);

            yield return PlaceFloor(Grid.OffsetCell(origin, -6, 16));
            GameObject saltObject = Build(MoltenSaltBatteryConfig.ID, Grid.OffsetCell(origin, -6, 16), SimHashes.Steel);
            yield return Wait(1f);
            var salt = saltObject.GetComponent<MassBattery>();
            saltObject.GetComponent<Storage>().AddOre(SimHashes.Salt, 1000f, 293f, byte.MaxValue, 0);
            yield return Wait(1f);
            CheckNear("sal_capacidade", salt.FullCapacity, 1000f * MoltenSaltBatteryConfig.JoulesPerKg);
            yield return ChargeAndCheck("sal", salt, 50000f, 1f - MoltenSaltBatteryConfig.Loss);
            yield return Wait(2f);
            CheckDeliveryRequested("sal", saltObject);
            yield return ConnectPowerAndCheck("sal", saltObject, 3, () => salt.JoulesAvailable);
            AddShot("sal", saltObject);
            AddShot("volante", flywheelObject);
            yield return CheckAnimationRuns("volante", flywheelObject);
            yield return CheckAnimationRuns("ar", airObject);
            yield return CheckAnimationRuns("fluxo", flowObject);
            yield return CheckAnimationRuns("sal", saltObject);
            yield return HeatPumpSteps(Grid.OffsetCell(origin, -24, 0));
            yield return TakeScreenshots();
            yield return SaltDeliveredByDuplicant();
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
            yield return PipeAndCheck("hidraulica", baseObject, "LiquidConduit", Game.Instance.liquidConduitFlow, SimHashes.Water);
            yield return DrainAndCheck("hidraulica", baseObject, "LiquidConduit");
            AddShot("hidraulica", baseObject);
            yield return ConnectPowerAndCheck("hidraulica", baseObject, 1, () => battery.JoulesAvailable);
        }

        private IEnumerator WirePower(int origin, int last)
        {
            for (int x = 0; x <= last; x++)
            {
                Build("Wire", Grid.OffsetCell(origin, x, 0), SimHashes.Copper);
            }
            yield return Wait(2f);
            var wires = Game.Instance.electricalConduitSystem;
            for (int x = 0; x <= last; x++)
            {
                UtilityConnections links = x == 0 ? UtilityConnections.Right
                    : x == last ? UtilityConnections.Left
                    : UtilityConnections.Left | UtilityConnections.Right;
                wires.AddConnection(links, Grid.OffsetCell(origin, x, 0), true);
            }
            wires.ForceRebuildNetworks();
        }

        private IEnumerator ConnectPowerAndCheck(string name, GameObject building, int halfWidth, Func<float> joules)
        {
            int origin = Grid.PosToCell(building);
            int last = halfWidth + 1;
            yield return WirePower(origin, last);
            yield return Wait(1f);
            float before = joules();
            Build("DevGenerator", Grid.OffsetCell(origin, last, 0), SimHashes.Copper);
            yield return Wait(4f);
            Check(name + "_carrega_por_fio", joules() > before, $"before={before} after={joules()}");
        }

        private IEnumerator PipeAndCheck(string name, GameObject building, string conduit, ConduitFlow flow, SimHashes element)
        {
            Storage storage = building.GetComponents<Storage>()[building.GetComponents<Storage>().Length - 1];
            int cell = building.GetComponent<Building>().GetUtilityInputCell();
            float before = storage.MassStored();
            Build(conduit, cell, SimHashes.Copper);
            yield return Wait(2f);
            float offered = flow.AddElement(cell, element, 1f, 293f, byte.MaxValue, 0);
            yield return Wait(4f);
            Check(name + "_recebe_por_cano", storage.MassStored() > before, $"offered={offered} before={before} after={storage.MassStored()}");
        }

        private IEnumerator DrainAndCheck(string name, GameObject building, string conduit)
        {
            Storage storage = building.GetComponent<Storage>();
            float before = storage.MassStored();
            Build(conduit, building.GetComponent<Building>().GetUtilityOutputCell(), SimHashes.Copper);
            yield return Wait(2f);
            building.GetComponent<PipeDrain>().SetDraining(true);
            yield return Wait(4f);
            building.GetComponent<PipeDrain>().SetDraining(false);
            Check(name + "_drena_por_cano", storage.MassStored() < before, $"before={before} after={storage.MassStored()}");
        }

        private IEnumerator CheckAnimationRuns(string name, GameObject building, System.Action eachFrame = null)
        {
            var controller = building.GetComponent<KBatchedAnimController>();
            CameraController.Instance.SnapTo(building.transform.GetPosition() + Vector3.up * 4f, 14f);
            yield return Wait(1f);
            var seen = new HashSet<int>();
            float deadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < deadline)
            {
                eachFrame?.Invoke();
                seen.Add(controller.currentFrame);
                yield return null;
            }
            Check(name + "_animacao_roda", controller.CurrentAnim != null && controller.CurrentAnim.name == "working_loop" && seen.Count > 1,
                $"anim={controller.CurrentAnim?.name} quadros={seen.Count} bloqueios={BlockedFlags(building)} ativo={building.GetComponent<Operational>().IsActive}");
        }

        private IEnumerator PlaceFloor(int origin)
        {
            for (int x = -3; x <= 4; x++)
            {
                SimMessages.ReplaceElement(Grid.OffsetCell(origin, x, -1), SimHashes.Granite,
                    CellEventLogger.Instance.DebugTool, 1000f, 293f);
            }
            yield return Wait(2f);
        }

        private static string BlockedFlags(GameObject building)
        {
            var blocked = new List<string>();
            foreach (var flag in building.GetComponent<Operational>().Flags)
            {
                if (!flag.Value)
                {
                    blocked.Add(flag.Key.Name);
                }
            }
            return string.Join(",", blocked);
        }

        private IEnumerator HeatPumpSteps(int origin)
        {
            yield return PlaceFloor(origin);
            GameObject compressor = Build(HeatCompressorConfig.ID, origin, SimHashes.Steel);
            yield return Wait(1f);
            yield return WirePower(origin, 2);
            Build("DevGenerator", Grid.OffsetCell(origin, 2, 0), SimHashes.Copper);
            Building compressorBuilding = compressor.GetComponent<Building>();
            int gasIn = compressorBuilding.GetUtilityInputCell();
            int liquidOut = compressorBuilding.GetUtilityOutputCell();
            Build("InsulatedGasConduit", gasIn, SimHashes.Ceramic);
            Build("InsulatedLiquidConduit", liquidOut, SimHashes.Ceramic);
            yield return Wait(2f);
            Game.Instance.gasConduitFlow.AddElement(gasIn, SimHashes.Oxygen, 1f, 300f, byte.MaxValue, 0);
            yield return Wait(4f);
            var condensed = Game.Instance.liquidConduitFlow.GetContents(liquidOut);
            Element oxygen = ElementLoader.FindElementByHash(SimHashes.Oxygen);
            Check("compressor_gas_vira_liquido", condensed.element == SimHashes.LiquidOxygen && condensed.mass > 0f,
                $"element={condensed.element} mass={condensed.mass} temp={condensed.temperature} bloqueios={BlockedFlags(compressor)}");
            Check("compressor_liquido_abaixo_ebulicao", condensed.temperature < oxygen.lowTemp, $"temp={condensed.temperature} ebulicao={oxygen.lowTemp}");

            int valveCell = Grid.OffsetCell(origin, 5, 0);
            GameObject valve = Build(ExpansionValveConfig.ID, valveCell, SimHashes.Steel);
            yield return Wait(1f);
            Building valveBuilding = valve.GetComponent<Building>();
            int liquidIn = valveBuilding.GetUtilityInputCell();
            int gasOut = valveBuilding.GetUtilityOutputCell();
            Build("InsulatedLiquidConduit", liquidIn, SimHashes.Ceramic);
            Build("InsulatedGasConduit", gasOut, SimHashes.Ceramic);
            yield return Wait(2f);
            Game.Instance.liquidConduitFlow.AddElement(liquidIn, SimHashes.LiquidOxygen, 1f, 80f, byte.MaxValue, 0);
            yield return Wait(1f);
            var released = Game.Instance.gasConduitFlow.GetContents(gasOut);
            Check("redutor_liquido_vira_gas", released.element == SimHashes.Oxygen && released.mass > 0f,
                $"element={released.element} mass={released.mass} temp={released.temperature}");
            Check("redutor_gas_gelado", released.temperature < oxygen.lowTemp + 5f, $"temp={released.temperature}");
            Check("redutor_libera_pouco", released.mass < 0.6f, $"mass={released.mass}");
            AddShot("compressor", compressor);
            AddShot("redutor", valve);
            yield return CheckAnimationRuns("compressor", compressor,
                () => Game.Instance.gasConduitFlow.AddElement(gasIn, SimHashes.Oxygen, 1f, 300f, byte.MaxValue, 0));
        }

        private IEnumerator TakeScreenshots()
        {
            string folder = Path.GetDirectoryName(AutoTest.ResultPath);
            foreach (var shot in shots)
            {
                CameraController.Instance.SnapTo(shot.Value, 14f);
                yield return Wait(1.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "autotest", "shot_" + shot.Key + ".png"));
                yield return Wait(1.5f);
            }
        }

        private void AddShot(string name, GameObject building)
        {
            shots[name] = building.transform.GetPosition() + Vector3.up * 5f;
        }

        private IEnumerator SaltDeliveredByDuplicant()
        {
            GameObject telepad = Components.Telepads[0].gameObject;
            int pad = Grid.PosToCell(telepad);
            int site = Grid.OffsetCell(pad, 9, 0);
            ClearArea(site, 3, 10);
            yield return Wait(2f);
            GameObject building = Build(MoltenSaltBatteryConfig.ID, site, SimHashes.Steel);
            yield return Wait(1f);
            building.GetComponent<Prioritizable>().SetMasterPriority(
                new PrioritySetting(PriorityScreen.PriorityClass.topPriority, 1));
            Vector3 position = Grid.CellToPosCCC(Grid.OffsetCell(pad, 2, 1), Grid.SceneLayer.Ore);
            ElementLoader.FindElementByHash(SimHashes.Salt).substance.SpawnResource(position, 500f, 293f, byte.MaxValue, 0);
            SpeedControlScreen.Instance.SetSpeed(2);
            Storage storage = building.GetComponent<Storage>();
            float deadline = Time.realtimeSinceStartup + 75f;
            while (storage.MassStored() <= 0f && Time.realtimeSinceStartup < deadline)
            {
                yield return Wait(1f);
            }
            Check("sal_entregue_por_duplicante", storage.MassStored() > 0f, $"mass={storage.MassStored()}");
        }

        private void CheckDeliveryRequested(string name, GameObject building)
        {
            object fetch = Traverse.Create(building.GetComponent<ManualDeliveryKG>()).Field("fetchList").GetValue();
            Check(name + "_pede_entrega", fetch != null, $"fetchList={(fetch != null)}");
        }
    }
}
