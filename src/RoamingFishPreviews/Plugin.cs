using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using BokuMono;
using BokuMono.Data;
using HarmonyLib;
using UnityEngine;

namespace RoamingFishPreviews;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.vic.sosgb.roamingfishpreviews";
    public const string PluginName = "Roaming Fish Previews";
    public const string PluginVersion = "0.15.1";

    private sealed class ExtractedMeshData
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public int[] Indices;
    }

    private sealed class BendMesh
    {
        public Mesh Mesh;
        public Vector3[] RestVertices;
        public Vector3[] WorkingVertices;
        public Vector3[] RestNormals;
        public Vector3[] WorkingNormals;
        public bool LongAxisIsY;
        public float MinimumAxis;
        public float AxisLength;
    }

    private sealed class Habitat
    {
        public FieldAttributeParam Layer;
        public uint GroupId;
        public readonly List<int> AreaIndices = new();
        public readonly List<int> SpawnAreaIndices = new();
        public float NearestDistanceSquared = float.MaxValue;
        public bool SchoolInitialized;
        public int SchoolCapacity;
        public float NextRespawnTime;
    }

    private sealed class Preview
    {
        public int Generation;
        public Habitat Habitat;
        public uint GroupId;
        public Vector2Int Grid;
        public Vector3 WaterPoint;
        public FishingMasterData Master;
        public GameObject Object;
        public readonly List<Material> OwnedMaterials = new();
        public readonly List<Mesh> OwnedNativeMeshes = new();
        public readonly List<BendMesh> BendMeshes = new();
        public Mesh OwnedMesh;
        public GameObject NativeObject;
        public float Phase;
        public float Heading;
        public string PositionSource;
        public float TargetLength;
        public bool Pursuing;
        public bool ReachedBobber;
        public Vector3 PursuitTarget;
        public int FakeNibbleCount;
        public float BiteSequenceStart;
        public Vector3 BiteDirection;
        public Vector3 NibbleRetreatPoint;
        public bool FinalBiteReady;
        public bool SwimmingAway;
        public Vector3 EscapeDirection;
        public float EscapeEndTime;
        public Vector3 Velocity;
        public Vector3 SwimTarget;
        public float NextSwimDecision;
        public float CruiseFactor;
        public float LastFlexHeading;
        public float TurnBend;
        public bool HasFlexHeading;
        public bool Scattering;
        public Vector3 ScatterThreat;
        public float ScatterEndTime;
    }

    private static ConfigEntry<bool> _enabled;
    private static ConfigEntry<int> _populationSize;
    private static ConfigEntry<int> _minimumSchoolSize;
    private static ConfigEntry<int> _maximumSchoolSize;
    private static ConfigEntry<float> _poolLoadDistance;
    private static ConfigEntry<float> _poolUnloadDistance;
    private static ConfigEntry<float> _respawnSeconds;
    private static ConfigEntry<float> _roamRadius;
    private static ConfigEntry<float> _roamSpeed;
    private static ConfigEntry<float> _swimAcceleration;
    private static ConfigEntry<float> _swimTurnRate;
    private static ConfigEntry<float> _surfaceOffset;
    private static ConfigEntry<float> _nativeSurfaceOffset;
    private static ConfigEntry<float> _nativeShadowThickness;
    private static ConfigEntry<float> _shadowSize;
    private static ConfigEntry<float> _bodyFlex;
    private static ConfigEntry<float> _selectionRadius;
    private static ConfigEntry<float> _approachSpeed;
    private static ConfigEntry<float> _biteDistance;
    private static ConfigEntry<int> _minimumFakeNibbles;
    private static ConfigEntry<int> _maximumFakeNibbles;
    private static ConfigEntry<float> _nibbleCycleSeconds;
    private static ConfigEntry<float> _nibbleRetreatDistance;
    private static ConfigEntry<float> _escapeSeconds;
    private static ConfigEntry<float> _escapeSpeed;
    private static ConfigEntry<float> _scatterRadius;
    private static ConfigEntry<float> _scatterSeconds;
    private static ConfigEntry<float> _scatterSpeed;
    private static ConfigEntry<bool> _runScatterEnabled;
    private static ConfigEntry<float> _runScatterRadius;
    private static ConfigEntry<float> _runScatterCooldown;
    private static ConfigEntry<float> _runScatterMinimumSpeed;
    private static ConfigEntry<bool> _strictFiniteMode;
    private static ConfigEntry<bool> _verboseTrace;
    private static BepInEx.Logging.ManualLogSource _log;

    private static readonly List<Preview> Previews = new();
    private static readonly List<Habitat> Habitats = new();
    private static readonly Dictionary<string, ExtractedMeshData> ExtractedMeshes = new();
    private static readonly HashSet<string> MissingExtractedMeshes = new();
    private static readonly System.Random PlacementRandom = new();
    private static string _conditionSignature = string.Empty;
    private static int _generation;
    private static float _nextRefreshTime;
    private static float _nextPoolScanTime;
    private static bool _buildingPopulation;
    private static Vector3 _populationOrigin;
    private static bool _fishingActive;
    private static bool _populationInitialized;
    private static bool _reservationInjected;
    private static bool _catchConfirmed;
    private static bool _strictHoldLogged;
    private static Preview _reservedPreview;
    private static FishingMasterData _reservedMaster;
    private static int _loggedReadyGeneration = -1;
    private static int _loggedNativeGeneration = -1;
    private static int _lastTickFrame = -1;
    private static string _diagnosticState = string.Empty;
    private static string _meshPayloadRoot = string.Empty;
    private static Vector3 _lastPlayerMotionPosition;
    private static float _lastPlayerMotionTime;
    private static float _nextRunScatterTime;
    private static bool _hasPlayerMotionSample;

    public override void Load()
    {
        _enabled = Config.Bind("General", "Enabled", true,
            "Show pre-cast roaming fish silhouettes in currently valid fishing habitats.");
        _populationSize = Config.Bind("Population", "VisibleFish", 18,
            "Maximum number of fish visuals kept loaded near the player (1-32).");
        _minimumSchoolSize = Config.Bind("Population", "MinimumFishPerPool", 4,
            "Minimum finite school created when a fishing pool is first approached (1-12).");
        _maximumSchoolSize = Config.Bind("Population", "MaximumFishPerPool", 12,
            "Maximum finite school for the largest fishing pools (1-24).");
        _poolLoadDistance = Config.Bind("Population", "PoolLoadDistance", 26f,
            "Distance in metres at which an unvisited pool creates and loads its finite school.");
        _poolUnloadDistance = Config.Bind("Population", "PoolUnloadDistance", 38f,
            "Distance in metres at which a visited pool unloads visuals but keeps its fish state.");
        _respawnSeconds = Config.Bind("Population", "RespawnSeconds", 120f,
            "Seconds after a catch before that pool recovers one fish; replacements are staggered.");
        _roamRadius = Config.Bind("Visual", "RoamRadius", 0.30f,
            "Movement radius inside a validated fishing grid cell.");
        _roamSpeed = Config.Bind("Visual", "RoamSpeed", 0.55f,
            "Maximum cruising speed of roaming silhouettes.");
        _swimAcceleration = Config.Bind("Visual", "SwimAcceleration", 0.75f,
            "Acceleration used by the burst-and-coast roaming model.");
        _swimTurnRate = Config.Bind("Visual", "SwimTurnRate", 105f,
            "Maximum roaming turn rate in degrees per second.");
        _surfaceOffset = Config.Bind("Visual", "SurfaceOffset", 0.025f,
            "Vertical offset from the water point returned by the game.");
        _nativeSurfaceOffset = Config.Bind("Visual", "NativeSurfaceOffset", 0.020f,
            "Height of the native fish silhouette's upper edge relative to the water surface.");
        _nativeShadowThickness = Config.Bind("Visual", "NativeShadowThickness", 0.045f,
            "Normalized vertical thickness of every rotated native fish shadow in metres.");
        _shadowSize = Config.Bind("Visual", "ShadowSize", 1.0f,
            "Global multiplier for readable shadow-size classes.");
        _bodyFlex = Config.Bind("Visual", "BodyFlex", 0.065f,
            "Base tail-wave amplitude as a fraction of fish length (0 disables it).");
        _selectionRadius = Config.Bind("Gameplay", "SelectionRadius", 3.0f,
            "How close the bobber must land for the nearest eligible fish to pursue it.");
        _approachSpeed = Config.Bind("Gameplay", "ApproachSpeed", 2.10f,
            "How quickly the nearest targeted silhouette swims to the bobber.");
        _biteDistance = Config.Bind("Gameplay", "BiteDistance", 0.20f,
            "Distance from the bobber at which the visible nibble sequence begins.");
        _minimumFakeNibbles = Config.Bind("BiteBehavior", "MinimumFakeNibbles", 1,
            "Minimum number of visible test bites before the final committed bite.");
        _maximumFakeNibbles = Config.Bind("BiteBehavior", "MaximumFakeNibbles", 3,
            "Maximum number of visible test bites before the final committed bite.");
        _nibbleCycleSeconds = Config.Bind("BiteBehavior", "NibbleCycleSeconds", 0.78f,
            "Duration of each approach-and-back-off test bite.");
        _nibbleRetreatDistance = Config.Bind("BiteBehavior", "NibbleRetreatDistance", 0.28f,
            "How far the fish backs away from the bobber between test bites.");
        _escapeSeconds = Config.Bind("BiteBehavior", "EscapeSeconds", 1.45f,
            "How long a missed or cancelled target remains visible while escaping.");
        _escapeSpeed = Config.Bind("BiteBehavior", "EscapeSpeed", 1.8f,
            "Swimming speed of a missed or cancelled fish as it leaves.");
        _scatterRadius = Config.Bind("SchoolReaction", "ScatterRadius", 3.25f,
            "Fish this close to the selected fish scatter away from the bobber.");
        _scatterSeconds = Config.Bind("SchoolReaction", "ScatterSeconds", 2.1f,
            "How long nearby fish keep making connected-cell escape moves.");
        _scatterSpeed = Config.Bind("SchoolReaction", "ScatterSpeed", 1.35f,
            "Maximum speed of startled nearby fish.");
        _runScatterEnabled = Config.Bind("SchoolReaction", "RunScatterEnabled", true,
            "Running near the shoreline startles nearby visible fish; walking remains quiet.");
        _runScatterRadius = Config.Bind("SchoolReaction", "RunScatterRadius", 4.25f,
            "Horizontal distance in metres at which running footsteps startle fish.");
        _runScatterCooldown = Config.Bind("SchoolReaction", "RunScatterCooldown", 0.75f,
            "Minimum seconds between running-footstep disturbance pulses.");
        _runScatterMinimumSpeed = Config.Bind("SchoolReaction", "RunScatterMinimumSpeed", 1.75f,
            "Measured movement speed required in addition to the game's native running state.");
        _strictFiniteMode = Config.Bind("Gameplay", "StrictFiniteMode", true,
            "Only a displayed fish reaching the bobber may release the native bite lottery.");
        _verboseTrace = Config.Bind("Diagnostics", "VerboseTrace", false,
            "Log population construction and preview-to-catch reservations.");
        _log = Log;
        _meshPayloadRoot = Path.Combine(
            Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? string.Empty,
            "meshes"
        );

        new Harmony(PluginGuid).PatchAll(typeof(Patches));
        _log.LogInfo(
            $"{PluginName} {PluginVersion} loaded; native fish shadows use head-anchored flex; " +
            "optional locally generated mesh payloads enable it for every shadow class."
        );
    }

    private static bool IsActualFish(uint itemId) => itemId >= 114000u && itemId <= 114199u;

    private static uint ActiveFieldId(FieldManager field)
    {
        if (field == null)
            return 0u;
        if (field.CurrentFieldId != 0u)
            return field.CurrentFieldId;
        return field.CurrentFieldRoot?.SeasonalFieldId ?? 0u;
    }

    private static string CurrentSignature(out string waitingFor)
    {
        waitingFor = string.Empty;
        try
        {
            FieldManager field = FieldManager.Instance;
            if (field == null)
            {
                waitingFor = "FieldManager";
                return string.Empty;
            }
            uint fieldId = ActiveFieldId(field);
            if (fieldId == 0u)
            {
                waitingFor = "active field id";
                return string.Empty;
            }
            DateManager date = DateManager.Instance;
            if (date == null)
            {
                waitingFor = "DateManager";
                return string.Empty;
            }
            WeatherManager weather = WeatherManager.Instance;
            if (weather == null)
            {
                waitingFor = "WeatherManager";
                return string.Empty;
            }
            ToolManager tool = ToolManager.Instance;
            if (tool == null)
            {
                waitingFor = "ToolManager";
                return string.Empty;
            }
            BokuMonoDateTime now = date.Now;
            // Population identity deliberately excludes hour, rod, and bait.
            // Once a fish has spawned it persists as a finite field actor until
            // leaving the field (or starting a new day), like Animal Crossing.
            return $"{fieldId}|{now.Year}|{now.Month}|{now.Day}|" +
                   $"{(int)weather.TodayWeather}|{weather.TodayWindLevel}";
        }
        catch (Exception exception)
        {
            waitingFor = $"condition read ({exception.GetType().Name}: {exception.Message})";
            return string.Empty;
        }
    }

    private static void ReportState(string state)
    {
        if (state == _diagnosticState)
            return;
        _diagnosticState = state;
        _log.LogInfo($"STATE {state}");
    }

    private static void TickPopulation()
    {
        int frame = Time.frameCount;
        if (_lastTickFrame == frame)
            return;
        _lastTickFrame = frame;

        if (!_enabled.Value)
        {
            if (Previews.Count > 0)
                ClearPopulation();
            return;
        }

        if (_fishingActive)
        {
            UpdatePreviewMotion();
            ReportState("paused while fishing");
            return;
        }

        string signature = CurrentSignature(out string waitingFor);
        if (string.IsNullOrEmpty(signature))
        {
            ReportState($"waiting for {waitingFor}");
            return;
        }

        if (signature != _conditionSignature)
        {
            ClearPopulation();
            _conditionSignature = signature;
            _nextRefreshTime = Time.time + 0.05f;
            ReportState($"conditions ready ({signature})");
        }

        UpdatePreviewMotion();
        UpdateRunningDisturbance();
        if (Time.time < _nextRefreshTime || _buildingPopulation)
            return;

        if (!_populationInitialized)
            InitializePoolTopology();
        if (_populationInitialized && Time.time >= _nextPoolScanTime)
        {
            _nextPoolScanTime = Time.time + 0.35f;
            RefreshNearbyPools();
        }
    }

    private static void InitializePoolTopology()
    {
        _buildingPopulation = true;
        try
        {
            FieldManager field = FieldManager.Instance;
            uint fieldId = ActiveFieldId(field);
            if (field == null || FishManager.Instance == null || fieldId == 0u ||
                field.ResidentData == null || !field.ResidentData.IsLoaded)
            {
                ReportState(
                    $"waiting for field data (field={fieldId}, fish={FishManager.Instance != null}, " +
                    $"resident={field?.ResidentData != null}, loaded={field?.ResidentData?.IsLoaded})"
                );
                _nextRefreshTime = Time.time + 2f;
                return;
            }

            if (!TryGetPlayerPosition(out _populationOrigin))
            {
                ReportState("waiting for player transform");
                _nextRefreshTime = Time.time + 0.75f;
                return;
            }
            CollectHabitats(field);
            if (Habitats.Count == 0)
            {
                ReportState($"no FishingPossible habitats for field={fieldId}");
                _nextRefreshTime = Time.time + 3f;
                return;
            }
            _populationInitialized = true;
            _nextPoolScanTime = 0f;
            ReportState($"pool topology ready ({Habitats.Count} pools, field={fieldId})");
            _nextRefreshTime = Time.time;
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Population build deferred: {exception.Message}");
            _nextRefreshTime = Time.time + 3f;
        }
        finally
        {
            _buildingPopulation = false;
        }
    }

    private static void RefreshNearbyPools()
    {
        if (!TryGetPlayerPosition(out _populationOrigin))
            return;

        float loadDistance = Mathf.Clamp(_poolLoadDistance.Value, 5f, 50f);
        float unloadDistance = Math.Max(
            loadDistance + 3f,
            Mathf.Clamp(_poolUnloadDistance.Value, 8f, 80f)
        );
        int loadedLimit = Math.Max(1, Math.Min(32, _populationSize.Value));

        for (int index = 0; index < Habitats.Count; index++)
        {
            Habitat habitat = Habitats[index];
            habitat.NearestDistanceSquared = DistanceToPoolSquared(habitat, _populationOrigin);
            float distance = Mathf.Sqrt(habitat.NearestDistanceSquared);
            if (!habitat.SchoolInitialized && distance <= loadDistance)
                CreateSchool(habitat);

            if (!habitat.SchoolInitialized)
                continue;
            if (distance <= loadDistance &&
                Time.time >= habitat.NextRespawnTime &&
                CountSchoolFish(habitat) < habitat.SchoolCapacity)
            {
                if (TryCreateSchoolFish(habitat, FishManager.Instance))
                {
                    habitat.NextRespawnTime = Time.time +
                        Mathf.Clamp(_respawnSeconds.Value, 15f, 600f);
                    _log.LogInfo(
                        $"POOL recovered one fish group={habitat.GroupId}; " +
                        $"school={CountSchoolFish(habitat)}/{habitat.SchoolCapacity}."
                    );
                }
                else
                    habitat.NextRespawnTime = Time.time + 5f;
            }
            if (distance > unloadDistance)
            {
                int unloaded = UnloadPoolVisuals(habitat);
                if (unloaded > 0 && _verboseTrace.Value)
                    _log.LogInfo(
                        $"POOL visuals unloaded group={habitat.GroupId} count={unloaded} " +
                        $"distance={distance:0.0}m; finite school state retained."
                    );
            }
        }

        List<Habitat> loadCandidates = new();
        for (int index = 0; index < Habitats.Count; index++)
            if (Habitats[index].SchoolInitialized &&
                Habitats[index].NearestDistanceSquared <= loadDistance * loadDistance)
                loadCandidates.Add(Habitats[index]);
        loadCandidates.Sort((left, right) =>
            left.NearestDistanceSquared.CompareTo(right.NearestDistanceSquared));

        int missingNearbyVisuals = 0;
        for (int index = 0; index < Previews.Count; index++)
            if (Previews[index].Object == null &&
                loadCandidates.Contains(Previews[index].Habitat))
                missingNearbyVisuals++;
        int availableVisualSlots = Math.Max(0, loadedLimit - CountLoadedVisuals());
        if (missingNearbyVisuals > availableVisualSlots)
        {
            List<Habitat> staleLoadedPools = new();
            for (int index = 0; index < Habitats.Count; index++)
                if (Habitats[index].SchoolInitialized &&
                    Habitats[index].NearestDistanceSquared > loadDistance * loadDistance)
                    staleLoadedPools.Add(Habitats[index]);
            staleLoadedPools.Sort((left, right) =>
                right.NearestDistanceSquared.CompareTo(left.NearestDistanceSquared));
            for (int index = 0;
                index < staleLoadedPools.Count &&
                missingNearbyVisuals > availableVisualSlots;
                index++)
            {
                availableVisualSlots += UnloadPoolVisuals(staleLoadedPools[index]);
            }
        }
        for (int index = 0; index < loadCandidates.Count; index++)
            LoadPoolVisuals(loadCandidates[index], loadedLimit);

        int loaded = CountLoadedVisuals();
        int initialized = 0;
        for (int index = 0; index < Habitats.Count; index++)
            if (Habitats[index].SchoolInitialized)
                initialized++;
        ReportState(
            $"nearby pools active ({loaded} loaded fish, {initialized}/{Habitats.Count} schools initialized)"
        );
    }

    private static float DistanceToPoolSquared(Habitat habitat, Vector3 point)
    {
        float nearest = float.MaxValue;
        for (int index = 0; index < habitat.AreaIndices.Count; index++)
        {
            Vector2Int grid = habitat.Layer.FieldData.ToGridPosition(habitat.AreaIndices[index]);
            Vector3 world = GridWorldPosition(habitat.Layer.FieldData, grid);
            float dx = world.x - point.x;
            float dz = world.z - point.z;
            nearest = Math.Min(nearest, dx * dx + dz * dz);
        }
        return nearest;
    }

    private static int CalculateSchoolCapacity(int cells)
    {
        int minimum = Math.Max(1, Math.Min(12, _minimumSchoolSize.Value));
        int maximum = Math.Max(minimum, Math.Min(24, _maximumSchoolSize.Value));
        int scaled = Mathf.RoundToInt(2f + 0.65f * Mathf.Sqrt(Math.Max(1, cells)));
        return Math.Max(minimum, Math.Min(maximum, scaled));
    }

    private static void CreateSchool(Habitat habitat)
    {
        FishManager fish = FishManager.Instance;
        if (fish == null)
            return;

        habitat.SpawnAreaIndices.Clear();
        List<(int AreaIndex, float DistanceSquared)> ranked = new(habitat.AreaIndices.Count);
        for (int index = 0; index < habitat.AreaIndices.Count; index++)
        {
            int areaIndex = habitat.AreaIndices[index];
            Vector2Int grid = habitat.Layer.FieldData.ToGridPosition(areaIndex);
            Vector3 world = GridWorldPosition(habitat.Layer.FieldData, grid);
            float dx = world.x - _populationOrigin.x;
            float dz = world.z - _populationOrigin.z;
            ranked.Add((areaIndex, dx * dx + dz * dz));
        }
        ranked.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
        int localCells = Math.Min(128, ranked.Count);
        for (int index = 0; index < localCells; index++)
            habitat.SpawnAreaIndices.Add(ranked[index].AreaIndex);

        habitat.SchoolCapacity = CalculateSchoolCapacity(habitat.AreaIndices.Count);
        int created = 0;
        int waterRejected = 0;
        int spacingRejected = 0;
        int lotteryRejected = 0;
        int attempts = Math.Max(40, habitat.SchoolCapacity * 35);
        while (created < habitat.SchoolCapacity && attempts-- > 0)
        {
            int before = Previews.Count;
            if (!TryChooseWaterPoint(habitat, out _, out Vector3 waterPoint))
            {
                waterRejected++;
                continue;
            }
            if (TooCloseToExisting(waterPoint, habitat))
            {
                spacingRejected++;
                continue;
            }
            if (!TryCreateSchoolFishAtPoint(habitat, fish, waterPoint))
            {
                lotteryRejected++;
                continue;
            }
            if (Previews.Count > before)
                created++;
        }
        habitat.SchoolInitialized = created > 0;
        habitat.NextRespawnTime = float.MaxValue;
        _log.LogInfo(
            $"POOL school initialized group={habitat.GroupId} cells={habitat.AreaIndices.Count} " +
            $"capacity={habitat.SchoolCapacity} created={created} " +
            $"rejected(water={waterRejected},spacing={spacingRejected},lottery={lotteryRejected})."
        );
    }

    private static bool TryCreateSchoolFish(Habitat habitat, FishManager fish)
    {
        if (fish == null)
            return false;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            if (!TryChooseWaterPoint(habitat, out _, out Vector3 waterPoint) ||
                TooCloseToExisting(waterPoint, habitat))
                continue;
            if (TryCreateSchoolFishAtPoint(habitat, fish, waterPoint))
                return true;
        }
        return false;
    }

    private static bool TryCreateSchoolFishAtPoint(
        Habitat habitat,
        FishManager fish,
        Vector3 waterPoint)
    {
        FishingMasterData master = RollPreview(fish, habitat.GroupId);
        if (master == null || !IsActualFish(master.ItemId) ||
            string.IsNullOrEmpty(master.ShadowModelId))
            return false;

        Vector2Int nearestGrid = default;
        float nearest = float.MaxValue;
        for (int index = 0; index < habitat.SpawnAreaIndices.Count; index++)
        {
            Vector2Int grid = habitat.Layer.FieldData.ToGridPosition(
                habitat.SpawnAreaIndices[index]
            );
            Vector3 world = GridWorldPosition(habitat.Layer.FieldData, grid);
            float dx = world.x - waterPoint.x;
            float dz = world.z - waterPoint.z;
            float squared = dx * dx + dz * dz;
            if (squared < nearest)
            {
                nearest = squared;
                nearestGrid = grid;
            }
        }
        Previews.Add(new Preview
        {
            Generation = _generation,
            Habitat = habitat,
            GroupId = habitat.GroupId,
            Grid = nearestGrid,
            WaterPoint = waterPoint,
            Master = master,
            Phase = (float)(PlacementRandom.NextDouble() * Math.PI * 2.0),
            Heading = (float)(PlacementRandom.NextDouble() * 360.0),
            PositionSource = _lastPositionSource,
        });
        return true;
    }

    private static int CountSchoolFish(Habitat habitat)
    {
        int count = 0;
        for (int index = 0; index < Previews.Count; index++)
            if (Previews[index].Habitat == habitat)
                count++;
        return count;
    }

    private static int CountLoadedVisuals()
    {
        int loaded = 0;
        for (int index = 0; index < Previews.Count; index++)
            if (Previews[index].Object != null)
                loaded++;
        return loaded;
    }

    private static void LoadPoolVisuals(Habitat habitat, int loadedLimit)
    {
        int loaded = CountLoadedVisuals();
        for (int index = 0; index < Previews.Count && loaded < loadedLimit; index++)
        {
            Preview preview = Previews[index];
            if (preview.Habitat != habitat || preview.Object != null)
                continue;
            CreateProceduralPreview(preview);
            loaded++;
        }
    }

    private static int UnloadPoolVisuals(Habitat habitat)
    {
        int unloaded = 0;
        for (int index = 0; index < Previews.Count; index++)
        {
            Preview preview = Previews[index];
            if (preview.Habitat != habitat || preview.Object == null || preview.Pursuing)
                continue;
            ReleasePreviewVisual(preview);
            unloaded++;
        }
        return unloaded;
    }

    private static void CollectHabitats(FieldManager field)
    {
        Habitats.Clear();
        uint fieldId = ActiveFieldId(field);
        int layerCount = 0;
        int attributeCount = 0;
        int fishingAttributeCount = 0;
        var layers = field.ResidentData.GetAttributeParamList(
            fieldId,
            LayerAttribute.FishingPossible
        );
        if (layers == null || layers.Count == 0)
            layers = field.ResidentData.GetAttributeParamList(fieldId);
        if (layers == null)
            return;
        layerCount = layers.Count;

        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            FieldAttributeParam layer = layers[layerIndex];
            if (layer?.FieldLayer?.Area == null || layer.ParameterizedLayerAttributes == null)
                continue;
            for (int attributeIndex = 0; attributeIndex < layer.ParameterizedLayerAttributes.Count; attributeIndex++)
            {
                ParameterizedLayerAttribute attribute =
                    layer.ParameterizedLayerAttributes[attributeIndex];
                attributeCount++;
                if (attribute == null || attribute.Attribute != LayerAttribute.FishingPossible)
                    continue;

                // IL2CPP proxy objects are not reliably downcast by C# `as`. The
                // game's own FieldAttributeManager.Fishing path calls this
                // conversion accessor instead, so mirror that native path exactly.
                ParameterizedLayerAttributeFishingPossible fishing =
                    attribute.ToFishingPossible;
                if (fishing != null && fishing.GroupId != 0u)
                {
                    fishingAttributeCount++;
                    AddConnectedPools(layer, fishing.GroupId);
                }
            }
        }

        if (_verboseTrace.Value || Habitats.Count == 0)
        {
            _log.LogInfo(
                $"HABITATS field={fieldId} layers={layerCount} attributes={attributeCount} " +
                $"fishing={fishingAttributeCount} usable={Habitats.Count}."
            );
        }

    }

    private static void AddConnectedPools(FieldAttributeParam layer, uint groupId)
    {
        var indices = layer.FieldLayer.Area.BitIndices;
        if (indices == null || indices.Count == 0)
            return;

        Dictionary<Vector2Int, int> areaByGrid = new(indices.Count);
        List<Vector2Int> orderedGrids = new(indices.Count);
        for (int index = 0; index < indices.Count; index++)
        {
            int areaIndex = indices[index];
            Vector2Int grid = layer.FieldData.ToGridPosition(areaIndex);
            if (areaByGrid.ContainsKey(grid))
                continue;
            areaByGrid.Add(grid, areaIndex);
            orderedGrids.Add(grid);
        }

        HashSet<Vector2Int> visited = new();
        Queue<Vector2Int> frontier = new();
        Vector2Int[] directions =
        {
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.down,
        };
        for (int startIndex = 0; startIndex < orderedGrids.Count; startIndex++)
        {
            Vector2Int start = orderedGrids[startIndex];
            if (!visited.Add(start))
                continue;

            Habitat pool = new() { Layer = layer, GroupId = groupId };
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                Vector2Int current = frontier.Dequeue();
                pool.AreaIndices.Add(areaByGrid[current]);
                for (int directionIndex = 0;
                    directionIndex < directions.Length;
                    directionIndex++)
                {
                    Vector2Int neighbor = current + directions[directionIndex];
                    if (areaByGrid.ContainsKey(neighbor) && visited.Add(neighbor))
                        frontier.Enqueue(neighbor);
                }
            }
            if (pool.AreaIndices.Count > 0)
                Habitats.Add(pool);
        }
    }

    private static bool TryChooseWaterPoint(Habitat habitat, out Vector2Int grid, out Vector3 waterPoint)
    {
        grid = default;
        waterPoint = default;
        List<int> indices = habitat.SpawnAreaIndices;
        if (indices.Count == 0)
            return false;

        int selectedAreaIndex = indices[PlacementRandom.Next(indices.Count)];
        grid = habitat.Layer.FieldData.ToGridPosition(selectedAreaIndex);
        return TryResolveWaterPoint(habitat, grid, out waterPoint);
    }

    private static bool TryResolveWaterPoint(
        Habitat habitat,
        Vector2Int grid,
        out Vector3 waterPoint)
    {
        waterPoint = default;
        // GetFishingPoint returns a boxed IL2CPP ValueTuple. Its validity bit is
        // reliable in this game build, but Il2CppInterop currently reads Item1
        // (the Vector3) as zero. Resolve the same layer-4 water surface directly
        // from the field grid instead.
        Vector3 gridWorld = GridWorldPosition(habitat.Layer.FieldData, grid);
        Vector3 rayOrigin = new(
            gridWorld.x,
            Math.Max(gridWorld.y, _populationOrigin.y) + 48f,
            gridWorld.z
        );
        const int waterLayerMask = 1 << 4;
        if (Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit hit,
            120f,
            waterLayerMask,
            QueryTriggerInteraction.Collide))
        {
            waterPoint = hit.m_Point;
            _lastPositionSource = "layer4-raycast";
            return true;
        }

        // Keep the native accessor as a fallback for a future interop build
        // where its boxed tuple vector is marshalled correctly.
        var result = FishManager.GetFishingPoint(grid, false);
        if (!result.Item2 || !UsableWaterPoint(result.Item1, gridWorld))
            return false;
        waterPoint = result.Item1;
        _lastPositionSource = "native-tuple";
        return true;
    }

    private static string _lastPositionSource = string.Empty;

    private static Vector3 GridWorldPosition(FieldData fieldData, Vector2Int grid)
    {
        Vector3 converted = fieldData.ToWorldPosition(ref grid);
        if (IsFinite(converted) &&
            (Math.Abs(converted.x) > 0.001f || Math.Abs(converted.z) > 0.001f))
            return converted;

        // Native FieldData.ToWorldPosition is this cell-centre transform. The
        // X axis is reversed by the game's world/grid convention.
        float halfGrid = fieldData.GridSize * 0.5f;
        return new Vector3(
            fieldData.CenterX - (grid.x * fieldData.GridSize + halfGrid),
            0f,
            grid.y * fieldData.GridSize + halfGrid - fieldData.CenterY
        );
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static bool UsableWaterPoint(Vector3 point, Vector3 expected)
    {
        if (!IsFinite(point))
            return false;
        float dx = point.x - expected.x;
        float dz = point.z - expected.z;
        return dx * dx + dz * dz < 2.25f;
    }

    private static bool TooCloseToExisting(Vector3 point, Habitat habitat)
    {
        for (int index = 0; index < Previews.Count; index++)
        {
            if (Previews[index].Habitat != habitat)
                continue;
            Vector3 other = Previews[index].WaterPoint;
            float dx = point.x - other.x;
            float dz = point.z - other.z;
            if (dx * dx + dz * dz < 1.15f * 1.15f)
                return true;
        }
        return false;
    }

    private static FishingMasterData RollPreview(FishManager fish, uint groupId)
    {
        DateManager date = DateManager.Instance;
        WeatherManager weather = WeatherManager.Instance;
        ToolManager tool = ToolManager.Instance;
        if (date == null || weather == null || tool == null)
            return null;

        UnityEngine.Random.State savedState = UnityEngine.Random.state;
        try
        {
            return fish.ExecuteLottery(
                groupId,
                tool.GetToolLevel(ToolType.FishingRod),
                date.Now,
                weather.TodayWeather,
                weather.TodayWindLevel,
                tool.CurrentFishingBaitId
            );
        }
        finally
        {
            UnityEngine.Random.state = savedState;
        }
    }

    private static void CreateProceduralPreview(Preview preview)
    {
        GameObject root = new($"RoamingFishPreview_{preview.Master.ItemId}");
        preview.Object = root;
        root.transform.position = preview.WaterPoint + new Vector3(0f, _surfaceOffset.Value, 0f);
        root.transform.rotation = Quaternion.Euler(0f, preview.Heading, 0f);

        float averageCentimeters = Math.Max(1f, preview.Master.AverageSize);
        float targetLength = Mathf.Clamp(
            0.38f + 0.60f * Mathf.Sqrt(averageCentimeters / 100f),
            0.45f,
            1.35f
        ) * Mathf.Clamp(_shadowSize.Value, 0.5f, 1.75f);
        float widthRatio = ShadowWidthRatio(preview.Master.ShadowModelId);
        preview.TargetLength = targetLength;
        root.transform.localScale = new Vector3(targetLength, targetLength, targetLength);

        Mesh mesh = CreateFishSilhouetteMesh(widthRatio, preview.Master.ShadowModelId);
        preview.OwnedMesh = mesh;
        RegisterBendMesh(preview, mesh, false);
        MeshFilter filter = root.AddComponent<MeshFilter>();
        MeshRenderer renderer = root.AddComponent<MeshRenderer>();
        filter.sharedMesh = mesh;

        Camera camera = Camera.main;
        int previewLayer = ChooseVisibleWorldLayer(camera);
        root.layer = previewLayer;

        renderer.enabled = true;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader != null)
        {
            Material material = new(shader);
            Color shadow = new(0.018f, 0.065f, 0.080f, 0.82f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", shadow);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", shadow);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);
            if (material.HasProperty("_CullMode"))
                material.SetFloat("_CullMode", 0f);
            material.renderQueue = 3000;
            renderer.sharedMaterial = material;
            preview.OwnedMaterials.Add(material);
        }
        else
            _log.LogWarning("No compatible unlit silhouette shader was found.");

        root.SetActive(true);
        RequestNativePreview(preview, renderer);

        if (_verboseTrace.Value || _loggedReadyGeneration != _generation)
        {
            Vector3 viewport = camera != null
                ? camera.WorldToViewportPoint(root.transform.position)
                : new Vector3(-1f, -1f, -1f);
            bool layerVisible = camera != null &&
                (camera.cullingMask & (1 << previewLayer)) != 0;
            _log.LogInfo(
                $"PREVIEW ready procedural group={preview.GroupId} grid=({preview.Grid.x},{preview.Grid.y}) " +
                $"item={preview.Master.ItemId} model={preview.Master.ShadowModelId} " +
                $"water=({preview.WaterPoint.x:0.00},{preview.WaterPoint.y:0.00},{preview.WaterPoint.z:0.00}) " +
                $"source={preview.PositionSource} " +
                $"length={targetLength:0.00}m active={root.activeInHierarchy} renderer={renderer?.enabled} " +
                $"camera={(camera != null)} layer={previewLayer} visible={layerVisible} " +
                $"shader={shader?.name ?? "none"} " +
                $"viewport=({viewport.x:0.00},{viewport.y:0.00},{viewport.z:0.00})."
            );
            _loggedReadyGeneration = _generation;
        }
    }

    private static void RequestNativePreview(Preview preview, MeshRenderer fallbackRenderer)
    {
        ResourceManager resources = ResourceManager.Instance;
        if (resources == null || preview.Object == null ||
            string.IsNullOrEmpty(preview.Master?.ShadowModelId))
            return;

        int requestedGeneration = preview.Generation;
        string modelId = preview.Master.ShadowModelId;
        try
        {
            resources.InstantiatePrefab(modelId, preview.Object.transform, new Action<GameObject>(native =>
            {
                if (native == null)
                    return;
                if (_generation != requestedGeneration ||
                    preview.Generation != requestedGeneration || preview.Object == null)
                {
                    ResourceManager.Instance?.ReleasePrefab(native);
                    return;
                }

                preview.NativeObject = native;
                native.name = $"Native_{modelId}_{preview.Master.ItemId}";
                native.transform.localPosition = Vector3.zero;
                // Reel-up prefabs are authored with their long fish axis on Y.
                // Lay that exact mesh flat on XZ and normalize it to the same
                // readable world length used by the procedural fallback.
                native.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                float nativeLength = NativeModelLength(modelId);
                float nativeScale = preview.TargetLength / Math.Max(0.05f, nativeLength);
                float nativeThickness = NativeModelThickness(modelId);
                float verticalScale = Mathf.Clamp(
                    _nativeShadowThickness.Value,
                    0.015f,
                    0.12f
                ) / Math.Max(0.02f, nativeThickness);
                // After the 90-degree X rotation, prefab-local Z is world Y.
                // Normalize that axis separately so large full-bodied catch
                // meshes read as surface shadows instead of deep refracted fish.
                native.transform.localScale = new Vector3(
                    nativeScale,
                    nativeScale,
                    verticalScale
                );

                int visibleLayer = ChooseVisibleWorldLayer(Camera.main);
                SetLayerRecursively(native.transform, visibleLayer);
                FishShadow[] fishShadows = native.GetComponentsInChildren<FishShadow>(true);
                for (int index = 0; index < fishShadows.Length; index++)
                    fishShadows[index].enabled = false;

                native.SetActive(true);
                int bendableMeshes = 0;
                int extractedMeshes = 0;
                MeshFilter[] filters = native.GetComponentsInChildren<MeshFilter>(true);
                for (int index = 0; index < filters.Length; index++)
                {
                    Mesh source = filters[index].sharedMesh;
                    if (source == null)
                        continue;
                    try
                    {
                        Mesh clone;
                        if (TryCreateExtractedMesh(modelId, out clone))
                            extractedMeshes++;
                        else if (source.isReadable)
                        {
                            clone = UnityEngine.Object.Instantiate(source);
                            clone.name = $"{source.name}_RoamingFlex";
                        }
                        else
                            continue;
                        clone.MarkDynamic();
                        filters[index].sharedMesh = clone;
                        preview.OwnedNativeMeshes.Add(clone);
                        if (RegisterBendMesh(preview, clone, true))
                            bendableMeshes++;
                    }
                    catch (Exception exception)
                    {
                        if (_verboseTrace.Value)
                            _log.LogWarning(
                                $"Native flex unavailable for {modelId}/{source.name}: {exception.Message}"
                            );
                    }
                }
                Renderer[] renderers = native.GetComponentsInChildren<Renderer>(true);
                bool hasBounds = false;
                float highestPoint = float.MinValue;
                for (int index = 0; index < renderers.Length; index++)
                {
                    renderers[index].enabled = true;
                    highestPoint = Math.Max(highestPoint, renderers[index].bounds.max.y);
                    hasBounds = true;
                    Material[] materials = renderers[index].materials;
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        Material material = materials[materialIndex];
                        if (material == null)
                            continue;
                        Color shadow = new(0.018f, 0.065f, 0.080f, 0.88f);
                        if (material.HasProperty("_BaseColor"))
                            material.SetColor("_BaseColor", shadow);
                        if (material.HasProperty("_Color"))
                            material.SetColor("_Color", shadow);
                        preview.OwnedMaterials.Add(material);
                    }
                }
                if (hasBounds)
                {
                    Vector3 worldPosition = native.transform.position;
                    worldPosition.y += preview.WaterPoint.y +
                        Mathf.Clamp(_nativeSurfaceOffset.Value, -0.03f, 0.08f) -
                        highestPoint;
                    native.transform.position = worldPosition;
                }
                if (fallbackRenderer != null)
                    fallbackRenderer.enabled = false;

                if (_verboseTrace.Value || _loggedNativeGeneration != requestedGeneration)
                {
                    _log.LogInfo(
                        $"NATIVE preview ready item={preview.Master.ItemId} model={modelId} " +
                        $"renderers={renderers.Length} flexibleMeshes={bendableMeshes} " +
                        $"extractedMeshes={extractedMeshes} layer={visibleLayer}."
                    );
                    _loggedNativeGeneration = requestedGeneration;
                }
            }));
        }
        catch (Exception exception)
        {
            if (_verboseTrace.Value)
                _log.LogWarning(
                    $"Native preview load failed for {modelId}; procedural fallback retained: " +
                    exception.Message
                );
        }
    }

    private static bool TryCreateExtractedMesh(string modelId, out Mesh mesh)
    {
        mesh = null;
        if (!TryLoadExtractedMesh(modelId, out ExtractedMeshData data))
            return false;
        mesh = new Mesh { name = $"{modelId}_ExtractedWritable" };
        mesh.vertices = data.Vertices;
        mesh.normals = data.Normals;
        mesh.uv = data.Uvs;
        mesh.triangles = data.Indices;
        mesh.RecalculateBounds();
        return true;
    }

    private static bool TryLoadExtractedMesh(string modelId, out ExtractedMeshData data)
    {
        if (ExtractedMeshes.TryGetValue(modelId, out data))
            return true;
        if (MissingExtractedMeshes.Contains(modelId) || string.IsNullOrEmpty(_meshPayloadRoot))
            return false;

        string path = Path.Combine(_meshPayloadRoot, modelId + ".rfmesh");
        if (!File.Exists(path))
        {
            MissingExtractedMeshes.Add(modelId);
            return false;
        }
        try
        {
            using FileStream stream = File.OpenRead(path);
            using BinaryReader reader = new(stream);
            byte[] magic = reader.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != (byte)'R' || magic[1] != (byte)'F' ||
                magic[2] != (byte)'M' || magic[3] != (byte)'1')
                throw new InvalidDataException("invalid RFM1 header");
            int vertexCount = reader.ReadInt32();
            int indexCount = reader.ReadInt32();
            if (vertexCount <= 0 || vertexCount > 100000 || indexCount <= 0 ||
                indexCount > 600000 || indexCount % 3 != 0)
                throw new InvalidDataException(
                    $"invalid geometry counts ({vertexCount} vertices, {indexCount} indices)"
                );

            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            for (int index = 0; index < vertexCount; index++)
            {
                vertices[index] = new Vector3(
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()
                );
                normals[index] = new Vector3(
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()
                );
                uvs[index] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            }
            int[] indices = new int[indexCount];
            for (int index = 0; index < indexCount; index++)
            {
                uint value = reader.ReadUInt32();
                if (value >= vertexCount)
                    throw new InvalidDataException($"index {value} exceeds vertex count");
                indices[index] = (int)value;
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("unexpected trailing mesh data");

            data = new ExtractedMeshData
            {
                Vertices = vertices,
                Normals = normals,
                Uvs = uvs,
                Indices = indices,
            };
            ExtractedMeshes.Add(modelId, data);
            _log.LogInfo(
                $"MESH loaded writable {modelId}: {vertexCount} vertices, " +
                $"{indexCount / 3} triangles."
            );
            return true;
        }
        catch (Exception exception)
        {
            MissingExtractedMeshes.Add(modelId);
            _log.LogWarning($"Writable mesh {modelId} rejected: {exception.Message}");
            data = null;
            return false;
        }
    }

    private static bool RegisterBendMesh(Preview preview, Mesh mesh, bool longAxisIsY)
    {
        if (mesh == null || !mesh.isReadable)
            return false;
        Vector3[] vertices;
        Vector3[] normals;
        try
        {
            vertices = mesh.vertices;
            normals = mesh.normals;
        }
        catch
        {
            return false;
        }
        if (vertices == null || vertices.Length == 0)
            return false;

        float minimum = float.MaxValue;
        float maximum = float.MinValue;
        for (int index = 0; index < vertices.Length; index++)
        {
            float axis = longAxisIsY ? vertices[index].y : vertices[index].z;
            minimum = Math.Min(minimum, axis);
            maximum = Math.Max(maximum, axis);
        }
        float length = maximum - minimum;
        if (length < 0.001f)
            return false;
        preview.BendMeshes.Add(new BendMesh
        {
            Mesh = mesh,
            RestVertices = vertices,
            WorkingVertices = new Vector3[vertices.Length],
            RestNormals = normals != null && normals.Length == vertices.Length
                ? normals
                : null,
            WorkingNormals = normals != null && normals.Length == vertices.Length
                ? new Vector3[normals.Length]
                : null,
            LongAxisIsY = longAxisIsY,
            MinimumAxis = minimum,
            AxisLength = length,
        });
        return true;
    }

    private static void SetLayerRecursively(Transform transform, int layer)
    {
        if (transform == null)
            return;
        transform.gameObject.layer = layer;
        for (int index = 0; index < transform.childCount; index++)
            SetLayerRecursively(transform.GetChild(index), layer);
    }

    private static float NativeModelLength(string modelId)
    {
        return modelId switch
        {
            "anml_wld_010_00" => 0.324945f,
            "anml_wld_010_01" => 0.815285f,
            "anml_wld_010_02" => 0.324945f,
            "anml_wld_020_00" => 1.112492f,
            "anml_wld_030_00" => 0.910737f,
            _ => 0.325f,
        };
    }

    private static float NativeModelThickness(string modelId)
    {
        return modelId switch
        {
            "anml_wld_010_00" => 0.063f,
            "anml_wld_010_01" => 0.476f,
            "anml_wld_010_02" => 0.063f,
            "anml_wld_020_00" => 0.552f,
            "anml_wld_030_00" => 0.405f,
            _ => 0.063f,
        };
    }

    private static Mesh CreateFishSilhouetteMesh(float widthRatio, string modelId)
    {
        float halfWidth = Mathf.Clamp(widthRatio * 0.5f, 0.10f, 0.32f);
        float nose = modelId == "anml_wld_030_00" ? 0.43f : 0.50f;
        float rear = modelId == "anml_wld_010_01" ? -0.39f : -0.34f;

        List<Vector3> vertices = new()
        {
            new Vector3(0f, 0f, 0.05f),
            new Vector3(0f, 0f, nose),
            new Vector3(halfWidth * 0.48f, 0f, 0.43f),
            new Vector3(halfWidth * 0.84f, 0f, 0.28f),
            new Vector3(halfWidth, 0f, 0.05f),
            new Vector3(halfWidth * 0.88f, 0f, -0.17f),
            new Vector3(halfWidth * 0.52f, 0f, -0.30f),
            new Vector3(halfWidth * 0.22f, 0f, rear),
            new Vector3(-halfWidth * 0.22f, 0f, rear),
            new Vector3(-halfWidth * 0.52f, 0f, -0.30f),
            new Vector3(-halfWidth * 0.88f, 0f, -0.17f),
            new Vector3(-halfWidth, 0f, 0.05f),
            new Vector3(-halfWidth * 0.84f, 0f, 0.28f),
            new Vector3(-halfWidth * 0.48f, 0f, 0.43f),
        };
        List<int> triangles = new();
        for (int edge = 1; edge <= 13; edge++)
        {
            triangles.Add(0);
            triangles.Add(edge);
            triangles.Add(edge == 13 ? 1 : edge + 1);
        }

        // Forked tail, plus two small side fins. These make the silhouette read
        // as a fish at gameplay-camera distance instead of an oval marker.
        int tail = vertices.Count;
        vertices.Add(new Vector3(-halfWidth * 0.18f, 0f, rear + 0.01f));
        vertices.Add(new Vector3(halfWidth * 0.18f, 0f, rear + 0.01f));
        vertices.Add(new Vector3(halfWidth * 0.92f, 0f, -0.52f));
        vertices.Add(new Vector3(0f, 0f, -0.44f));
        vertices.Add(new Vector3(-halfWidth * 0.92f, 0f, -0.52f));
        triangles.AddRange(new[]
        {
            tail, tail + 1, tail + 3,
            tail + 1, tail + 2, tail + 3,
            tail, tail + 3, tail + 4,
        });

        int fins = vertices.Count;
        vertices.Add(new Vector3(halfWidth * 0.55f, 0f, -0.02f));
        vertices.Add(new Vector3(halfWidth * 1.25f, 0f, -0.18f));
        vertices.Add(new Vector3(halfWidth * 0.42f, 0f, -0.20f));
        vertices.Add(new Vector3(-halfWidth * 0.55f, 0f, -0.02f));
        vertices.Add(new Vector3(-halfWidth * 1.25f, 0f, -0.18f));
        vertices.Add(new Vector3(-halfWidth * 0.42f, 0f, -0.20f));
        triangles.AddRange(new[]
        {
            fins, fins + 1, fins + 2,
            fins + 3, fins + 5, fins + 4,
        });

        Mesh mesh = new() { name = $"RoamingFish_{modelId}" };
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static int ChooseVisibleWorldLayer(Camera camera)
    {
        // The game's native fish visual is layer 16. Camera.main in field scenes
        // excludes layer 0, which made the earlier procedural object invisible.
        if (camera != null && (camera.cullingMask & (1 << 16)) != 0)
            return 16;

        try
        {
            Transform player = GameController.Instance?.GetPlayerTransform();
            int playerLayer = player?.gameObject?.layer ?? -1;
            if (playerLayer >= 0 && playerLayer < 32 &&
                (camera == null || (camera.cullingMask & (1 << playerLayer)) != 0))
                return playerLayer;
        }
        catch
        {
            // Fall through to the first camera-visible non-UI layer.
        }

        if (camera != null)
        {
            for (int layer = 0; layer < 32; layer++)
            {
                if (layer != 5 && (camera.cullingMask & (1 << layer)) != 0)
                    return layer;
            }
        }
        return 16;
    }

    private static float ShadowWidthRatio(string modelId)
    {
        if (modelId == "anml_wld_020_00")
            return 0.38f;
        if (modelId == "anml_wld_030_00")
            return 0.42f;
        if (modelId == "anml_wld_010_01")
            return 0.32f;
        if (modelId == "anml_wld_010_02")
            return 0.28f;
        return 0.24f;
    }

    private static void UpdatePreviewMotion()
    {
        float radius = Mathf.Clamp(_roamRadius.Value, 0f, 0.42f);
        float speed = Mathf.Clamp(_roamSpeed.Value, 0f, 1.5f);
        for (int index = Previews.Count - 1; index >= 0; index--)
        {
            Preview preview = Previews[index];
            if (preview.Object == null)
                continue;

            UpdateBodyFlex(preview);

            if (preview.SwimmingAway)
            {
                float escapeSpeed = Mathf.Clamp(_escapeSpeed.Value, 0.5f, 4f);
                preview.Velocity = Vector3.MoveTowards(
                    preview.Velocity,
                    preview.EscapeDirection * escapeSpeed,
                    escapeSpeed * 2.2f * Time.deltaTime
                );
                preview.Object.transform.position += preview.Velocity * Time.deltaTime;
                preview.Heading = Mathf.Atan2(
                    preview.EscapeDirection.x,
                    preview.EscapeDirection.z
                ) * 57.29578f;
                preview.Object.transform.rotation = Quaternion.Euler(0f, preview.Heading, 0f);
                if (Time.time >= preview.EscapeEndTime)
                {
                    Habitat escapedHabitat = preview.Habitat;
                    Previews.RemoveAt(index);
                    ReleasePreviewVisual(preview);
                    SchedulePoolRecovery(escapedHabitat);
                    _log.LogInfo(
                        $"TARGET escaped item={preview.Master.ItemId}; it will recover with the pool timer."
                    );
                }
                continue;
            }

            if (preview.Pursuing)
            {
                if (preview.ReachedBobber)
                {
                    UpdateNibbleSequence(preview);
                    continue;
                }

                Vector3 current = preview.Object.transform.position;
                Vector3 target = preview.PursuitTarget +
                    new Vector3(0f, _surfaceOffset.Value, 0f);
                Vector3 delta = target - current;
                delta.y = 0f;
                float distance = delta.magnitude;
                float biteDistance = Mathf.Clamp(_biteDistance.Value, 0.08f, 0.5f);
                float retreatDistance = Mathf.Clamp(_nibbleRetreatDistance.Value, 0.10f, 0.55f);
                float approachDistance = Math.Max(biteDistance, retreatDistance);
                Vector3 direction = distance > 0.001f ? delta / distance : new Vector3(
                    Mathf.Sin(preview.Heading * Mathf.Deg2Rad),
                    0f,
                    Mathf.Cos(preview.Heading * Mathf.Deg2Rad)
                );
                float desiredHeading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                preview.Heading = Mathf.MoveTowardsAngle(
                    preview.Heading,
                    desiredHeading,
                    Mathf.Clamp(_swimTurnRate.Value, 30f, 240f) * Time.deltaTime
                );
                preview.Object.transform.rotation = Quaternion.Euler(0f, preview.Heading, 0f);
                float headingError = Math.Abs(Mathf.DeltaAngle(preview.Heading, desiredHeading));
                if (distance <= approachDistance + 0.015f && headingError <= 3f)
                {
                    if (!preview.ReachedBobber)
                    {
                        preview.ReachedBobber = true;
                        int minimum = Math.Max(0, Math.Min(4, _minimumFakeNibbles.Value));
                        int maximum = Math.Max(
                            minimum,
                            Math.Min(5, _maximumFakeNibbles.Value)
                        );
                        preview.FakeNibbleCount = PlacementRandom.Next(minimum, maximum + 1);
                        preview.BiteSequenceStart = Time.time;
                        preview.NibbleRetreatPoint = current;
                        preview.BiteDirection = distance > 0.001f
                            ? delta / distance
                            : new Vector3(
                                Mathf.Sin(preview.Heading * Mathf.Deg2Rad),
                                0f,
                                Mathf.Cos(preview.Heading * Mathf.Deg2Rad)
                            );
                        preview.FinalBiteReady = false;
                        preview.Velocity = Vector3.zero;
                        _log.LogInfo(
                            $"TARGET reached bobber item={preview.Master.ItemId}; " +
                            $"beginning {preview.FakeNibbleCount} test nibble(s) before final bite."
                        );
                    }
                    continue;
                }

                float approach = Mathf.Clamp(_approachSpeed.Value, 0.4f, 4f);
                // Face the bobber before advancing. Rebuild velocity along the
                // fish's current heading so old roaming momentum cannot strafe
                // it sideways, and ease to a stop at the nibble staging point.
                float alignment = 1f - Mathf.SmoothStep(0f, 1f, headingError / 60f);
                float remaining = Math.Max(0f, distance - approachDistance);
                float desiredSpeed = Math.Min(approach, remaining * 3f) * alignment;
                float nextSpeed = Mathf.MoveTowards(
                    preview.Velocity.magnitude,
                    desiredSpeed,
                    approach * 2.5f * Time.deltaTime
                );
                Vector3 forward = new(
                    Mathf.Sin(preview.Heading * Mathf.Deg2Rad),
                    0f,
                    Mathf.Cos(preview.Heading * Mathf.Deg2Rad)
                );
                // A fish facing away pivots first, including casts right beside it.
                if (headingError >= 60f || remaining <= 0f)
                    nextSpeed = 0f;
                nextSpeed = Math.Min(nextSpeed, remaining / Math.Max(Time.deltaTime, 0.001f));
                preview.Velocity = forward * nextSpeed;
                Vector3 movement = preview.Velocity * Time.deltaTime;
                preview.Object.transform.position = current + movement;
                continue;
            }

            if (preview.Scattering)
            {
                UpdateScatterSwim(preview);
                continue;
            }

            UpdateRoamingSwim(preview, radius, speed);
        }
    }

    private static void UpdateBodyFlex(Preview preview)
    {
        if (preview.BendMeshes.Count == 0 || (Time.frameCount & 1) != 0)
            return;
        float flex = Mathf.Clamp(_bodyFlex.Value, 0f, 0.12f);
        if (flex <= 0.0001f)
            return;

        float stepSeconds = Math.Max(0.001f, Time.deltaTime * 2f);
        float movement = preview.Velocity.magnitude;
        float referenceSpeed = Math.Max(0.20f, _roamSpeed.Value);
        float speedActivity = Mathf.Clamp01(movement / referenceSpeed);
        float activity = Mathf.Lerp(0.48f, 1f, speedActivity);
        float cyclesPerSecond = Mathf.Lerp(0.62f, 1.75f, speedActivity);
        preview.Phase += stepSeconds * Mathf.PI * 2f * cyclesPerSecond;

        if (!preview.HasFlexHeading)
        {
            preview.LastFlexHeading = preview.Heading;
            preview.HasFlexHeading = true;
        }
        float signedTurnRate = Mathf.DeltaAngle(preview.LastFlexHeading, preview.Heading) /
            stepSeconds;
        preview.LastFlexHeading = preview.Heading;
        float normalizedTurn = Mathf.Clamp(
            signedTurnRate / Math.Max(45f, _swimTurnRate.Value),
            -1f,
            1f
        );
        float turnBlend = 1f - Mathf.Exp(-stepSeconds * 5.5f);
        preview.TurnBend = Mathf.Lerp(preview.TurnBend, normalizedTurn, turnBlend);

        // The native prefab origin sits at the head. Keep that anchor steady;
        // the curved centerline below supplies the motion instead of rotating
        // the entire fish like a rigid pendulum.
        if (preview.NativeObject != null)
            preview.NativeObject.transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);

        for (int meshIndex = 0; meshIndex < preview.BendMeshes.Count; meshIndex++)
        {
            BendMesh bend = preview.BendMeshes[meshIndex];
            float amplitude = bend.AxisLength * flex * activity;
            for (int vertexIndex = 0; vertexIndex < bend.RestVertices.Length; vertexIndex++)
            {
                Vector3 vertex = bend.RestVertices[vertexIndex];
                float axis = bend.LongAxisIsY ? vertex.y : vertex.z;
                float normalized = Mathf.Clamp01(
                    (axis - bend.MinimumAxis) / bend.AxisLength
                );
                // All five native meshes point toward their maximum long-axis
                // coordinate. q therefore runs from a fixed head (0) to tail
                // (1), allowing the body wave to grow naturally toward the fin.
                float q = 1f - normalized;
                float envelope = Mathf.Pow(q, 1.55f);
                float waveAngle = preview.Phase + q * 2.65f;
                float secondaryEnvelope = Mathf.Pow(q, 2.60f);
                float secondaryAngle = preview.Phase * 1.72f + q * 4.35f + 0.7f;
                float turnEnvelope = Mathf.Pow(q, 1.35f);
                float centerline = amplitude * (
                    envelope * Mathf.Sin(waveAngle) +
                    0.13f * secondaryEnvelope * Mathf.Sin(secondaryAngle) +
                    0.55f * preview.TurnBend * turnEnvelope
                );

                // Rotate each cross-section around the curved spine instead of
                // merely sliding vertices sideways. This preserves the outline
                // through turns and stops the body looking rubber-sheared.
                float derivativeQ = amplitude * (
                    1.55f * Mathf.Pow(q, 0.55f) * Mathf.Sin(waveAngle) +
                    2.65f * envelope * Mathf.Cos(waveAngle) +
                    0.13f * (
                        2.60f * Mathf.Pow(q, 1.60f) * Mathf.Sin(secondaryAngle) +
                        4.35f * secondaryEnvelope * Mathf.Cos(secondaryAngle)
                    ) +
                    0.7425f * preview.TurnBend * Mathf.Pow(q, 0.35f)
                );
                float tangent = -derivativeQ / bend.AxisLength;
                float angle = Mathf.Atan(tangent);
                float sine = Mathf.Sin(angle);
                float cosine = Mathf.Cos(angle);
                float lateral = vertex.x;
                vertex.x = centerline + lateral * cosine;
                if (bend.LongAxisIsY)
                    vertex.y = axis - lateral * sine;
                else
                    vertex.z = axis - lateral * sine;
                bend.WorkingVertices[vertexIndex] = vertex;

                if (bend.RestNormals != null)
                {
                    Vector3 normal = bend.RestNormals[vertexIndex];
                    float normalX = normal.x;
                    normal.x = normalX * cosine +
                        (bend.LongAxisIsY ? normal.y : normal.z) * sine;
                    if (bend.LongAxisIsY)
                        normal.y = -normalX * sine + normal.y * cosine;
                    else
                        normal.z = -normalX * sine + normal.z * cosine;
                    bend.WorkingNormals[vertexIndex] = normal;
                }
            }
            bend.Mesh.vertices = bend.WorkingVertices;
            if (bend.WorkingNormals != null)
                bend.Mesh.normals = bend.WorkingNormals;
            bend.Mesh.RecalculateBounds();
        }
    }

    private static void UpdateScatterSwim(Preview preview)
    {
        if (Time.time >= preview.ScatterEndTime)
        {
            preview.Scattering = false;
            preview.NextSwimDecision = 0f;
            return;
        }

        Vector3 current = preview.Object.transform.position;
        Vector3 toTarget = preview.SwimTarget - current;
        toTarget.y = 0f;
        if (preview.NextSwimDecision <= 0f ||
            Time.time >= preview.NextSwimDecision || toTarget.sqrMagnitude < 0.035f)
        {
            ChooseScatterTarget(preview);
            toTarget = preview.SwimTarget - current;
            toTarget.y = 0f;
        }

        float speed = Mathf.Clamp(_scatterSpeed.Value, 0.65f, 3.0f);
        Vector3 desired = toTarget.sqrMagnitude > 0.0001f
            ? toTarget.normalized * speed
            : Vector3.zero;
        preview.Velocity = Vector3.MoveTowards(
            preview.Velocity,
            desired,
            speed * 2.8f * Time.deltaTime
        );
        current += preview.Velocity * Time.deltaTime;
        current.y = Mathf.Lerp(
            current.y,
            preview.SwimTarget.y + _surfaceOffset.Value,
            Math.Min(1f, Time.deltaTime * 7f)
        );
        preview.Object.transform.position = current;
        if (preview.Velocity.sqrMagnitude > 0.0025f)
        {
            preview.Heading = Mathf.Atan2(preview.Velocity.x, preview.Velocity.z) * 57.29578f;
            preview.Object.transform.rotation = Quaternion.Euler(0f, preview.Heading, 0f);
        }
    }

    private static void ChooseScatterTarget(Preview preview)
    {
        Vector2Int selectedGrid = preview.Grid;
        float bestThreatDistance = HorizontalSquared(
            GridWorldPosition(preview.Habitat.Layer.FieldData, selectedGrid),
            preview.ScatterThreat
        );
        Vector2Int[] directions =
        {
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.down,
        };
        for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
        {
            Vector2Int candidate = preview.Grid + directions[directionIndex];
            if (!HabitatContainsGrid(preview.Habitat, candidate))
                continue;
            Vector3 candidateWorld = GridWorldPosition(preview.Habitat.Layer.FieldData, candidate);
            float threatDistance = HorizontalSquared(candidateWorld, preview.ScatterThreat);
            threatDistance += (float)PlacementRandom.NextDouble() * 0.18f;
            if (threatDistance > bestThreatDistance)
            {
                bestThreatDistance = threatDistance;
                selectedGrid = candidate;
            }
        }

        Vector3 center = GridWorldPosition(preview.Habitat.Layer.FieldData, selectedGrid);
        if (!TryResolveWaterPoint(preview.Habitat, selectedGrid, out Vector3 waterPoint))
            waterPoint = preview.WaterPoint;
        Vector3 away = center - preview.ScatterThreat;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
        {
            float angle = (float)(PlacementRandom.NextDouble() * Math.PI * 2.0);
            away = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }
        away.Normalize();
        float offset = Mathf.Clamp(_roamRadius.Value, 0.12f, 0.42f) * 0.85f;
        preview.Grid = selectedGrid;
        preview.WaterPoint = waterPoint;
        preview.SwimTarget = new Vector3(
            center.x + away.x * offset,
            waterPoint.y,
            center.z + away.z * offset
        );
        preview.NextSwimDecision = Time.time + 0.45f +
            0.30f * (float)PlacementRandom.NextDouble();
    }

    private static bool HabitatContainsGrid(Habitat habitat, Vector2Int grid)
    {
        for (int index = 0; index < habitat.AreaIndices.Count; index++)
            if (habitat.Layer.FieldData.ToGridPosition(habitat.AreaIndices[index]) == grid)
                return true;
        return false;
    }

    private static float HorizontalSquared(Vector3 left, Vector3 right)
    {
        float x = left.x - right.x;
        float z = left.z - right.z;
        return x * x + z * z;
    }

    private static void UpdateRoamingSwim(Preview preview, float localRadius, float maximumSpeed)
    {
        Vector3 current = preview.Object.transform.position;
        if (preview.NextSwimDecision <= 0f)
        {
            preview.SwimTarget = current;
            preview.NextSwimDecision = Time.time;
            preview.CruiseFactor = 0.65f;
        }

        Vector3 toTarget = preview.SwimTarget - current;
        toTarget.y = 0f;
        if (Time.time >= preview.NextSwimDecision || toTarget.sqrMagnitude < 0.025f)
        {
            ChooseNextSwimTarget(preview, localRadius);
            toTarget = preview.SwimTarget - current;
            toTarget.y = 0f;
        }

        float acceleration = Mathf.Clamp(_swimAcceleration.Value, 0.15f, 2.5f);
        float turnRadians = Mathf.Clamp(_swimTurnRate.Value, 30f, 240f) *
            Mathf.Deg2Rad * Time.deltaTime;
        float desiredSpeed = Mathf.Clamp(maximumSpeed, 0.05f, 1.5f) *
            Mathf.Clamp(preview.CruiseFactor, 0.20f, 1.15f);
        Vector3 desiredVelocity = toTarget.sqrMagnitude > 0.0001f
            ? toTarget.normalized * desiredSpeed
            : Vector3.zero;

        if (preview.Velocity.sqrMagnitude < 0.0001f)
            preview.Velocity = desiredVelocity * 0.35f;
        else if (desiredVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 turned = Vector3.RotateTowards(
                preview.Velocity.normalized,
                desiredVelocity.normalized,
                turnRadians,
                0f
            );
            float nextSpeed = Mathf.MoveTowards(
                preview.Velocity.magnitude,
                desiredSpeed,
                acceleration * Time.deltaTime
            );
            preview.Velocity = turned * nextSpeed;
        }
        else
            preview.Velocity = Vector3.MoveTowards(
                preview.Velocity,
                Vector3.zero,
                acceleration * 0.45f * Time.deltaTime
            );

        current += preview.Velocity * Time.deltaTime;
        current.y = Mathf.Lerp(
            current.y,
            preview.SwimTarget.y + _surfaceOffset.Value,
            Math.Min(1f, Time.deltaTime * 4f)
        );
        preview.Object.transform.position = current;
        if (preview.Velocity.sqrMagnitude > 0.0025f)
        {
            preview.Heading = Mathf.Atan2(preview.Velocity.x, preview.Velocity.z) * 57.29578f;
            preview.Object.transform.rotation = Quaternion.Euler(0f, preview.Heading, 0f);
        }
    }

    private static void ChooseNextSwimTarget(Preview preview, float localRadius)
    {
        // Most decisions remain in the current fishing cell. Occasionally move
        // into a connected neighbor so the school travels without crossing banks.
        Vector2Int selectedGrid = preview.Grid;
        if (PlacementRandom.NextDouble() < 0.42 && preview.Habitat != null)
        {
            List<Vector2Int> neighbors = new();
            Vector2Int[] directions =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.up,
                Vector2Int.down,
            };
            for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
            {
                Vector2Int candidate = preview.Grid + directions[directionIndex];
                for (int areaIndex = 0;
                    areaIndex < preview.Habitat.AreaIndices.Count;
                    areaIndex++)
                {
                    if (preview.Habitat.Layer.FieldData.ToGridPosition(
                            preview.Habitat.AreaIndices[areaIndex]) == candidate)
                    {
                        neighbors.Add(candidate);
                        break;
                    }
                }
            }
            if (neighbors.Count > 0)
                selectedGrid = neighbors[PlacementRandom.Next(neighbors.Count)];
        }

        Vector3 center = GridWorldPosition(preview.Habitat.Layer.FieldData, selectedGrid);
        if (!TryResolveWaterPoint(preview.Habitat, selectedGrid, out Vector3 waterPoint))
            waterPoint = preview.WaterPoint;
        float angle = (float)(PlacementRandom.NextDouble() * Math.PI * 2.0);
        float distance = localRadius * (0.25f + 0.70f * (float)PlacementRandom.NextDouble());
        preview.Grid = selectedGrid;
        preview.WaterPoint = waterPoint;
        preview.SwimTarget = new Vector3(
            center.x + Mathf.Cos(angle) * distance,
            waterPoint.y,
            center.z + Mathf.Sin(angle) * distance
        );
        preview.CruiseFactor = 0.45f + 0.55f * (float)PlacementRandom.NextDouble();
        preview.NextSwimDecision = Time.time +
            1.2f + 2.8f * (float)PlacementRandom.NextDouble();
    }

    private static void UpdateNibbleSequence(Preview preview)
    {
        if (preview.Object == null)
            return;

        Vector3 bitePoint = preview.PursuitTarget +
            new Vector3(0f, _surfaceOffset.Value, 0f);
        // Begin from the actual aligned approach position, including a cast
        // which landed beside the fish; never teleport it backward to stage a nibble.
        Vector3 retreatPoint = preview.NibbleRetreatPoint;
        // Keep the head aimed down the same axis for every nibble and retreat.
        preview.Heading = Mathf.Atan2(preview.BiteDirection.x, preview.BiteDirection.z) * Mathf.Rad2Deg;
        preview.Object.transform.rotation = Quaternion.Euler(0f, preview.Heading, 0f);
        float cycleSeconds = Mathf.Clamp(_nibbleCycleSeconds.Value, 0.45f, 1.5f);
        float elapsed = Math.Max(0f, Time.time - preview.BiteSequenceStart);
        float fakeDuration = preview.FakeNibbleCount * cycleSeconds;

        if (elapsed < fakeDuration)
        {
            float phase = (elapsed % cycleSeconds) / cycleSeconds;
            if (phase < 0.28f)
                preview.Object.transform.position = Vector3.Lerp(
                    retreatPoint,
                    bitePoint,
                    Mathf.SmoothStep(0f, 1f, phase / 0.28f)
                );
            else if (phase < 0.58f)
                preview.Object.transform.position = Vector3.Lerp(
                    bitePoint,
                    retreatPoint,
                    Mathf.SmoothStep(0f, 1f, (phase - 0.28f) / 0.30f)
                );
            else
                preview.Object.transform.position = retreatPoint;
            return;
        }

        float finalPhase = (elapsed - fakeDuration) / 0.34f;
        preview.Object.transform.position = Vector3.Lerp(
            retreatPoint,
            bitePoint,
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(finalPhase))
        );
        if (finalPhase >= 1f && !preview.FinalBiteReady)
        {
            preview.FinalBiteReady = true;
            _log.LogInfo(
                $"TARGET final bite item={preview.Master.ItemId}; releasing native A-button bite."
            );
        }
    }

    private static void SelectReservation(uint groupId, Vector3 bobber)
    {
        _reservedPreview = null;
        _reservedMaster = null;
        _reservationInjected = false;
        _catchConfirmed = false;
        _strictHoldLogged = false;
        float limit = Mathf.Clamp(_selectionRadius.Value, 0.5f, 4.5f);
        float bestSquared = limit * limit;
        for (int index = 0; index < Previews.Count; index++)
        {
            Preview preview = Previews[index];
            if (preview.GroupId != groupId || preview.Master == null || preview.SwimmingAway ||
                preview.Scattering ||
                preview.Object == null || !preview.Object.activeInHierarchy)
                continue;
            Vector3 position = preview.Object.transform.position;
            float dx = position.x - bobber.x;
            float dz = position.z - bobber.z;
            float squared = dx * dx + dz * dz;
            if (squared <= bestSquared)
            {
                bestSquared = squared;
                _reservedPreview = preview;
                _reservedMaster = preview.Master;
            }
        }

        if (_reservedPreview != null)
        {
            _reservedPreview.Pursuing = true;
            _reservedPreview.PursuitTarget = bobber;
            ScatterNearbyFish(_reservedPreview, bobber);
            _log.LogInfo(
                $"TARGET acquired group={groupId} item={_reservedMaster.ItemId} " +
                $"distance={Mathf.Sqrt(bestSquared):0.00}; nearest eligible fish is pursuing."
            );
        }
        else
            _log.LogInfo(
                _strictFiniteMode.Value
                    ? $"TARGET none within {limit:0.00}m for group={groupId}; " +
                      "strict bobber waits with no bite (cancel/reel-in remains native)."
                    : $"TARGET none within {limit:0.00}m for group={groupId}; vanilla catch remains."
            );
    }

    private static void ScatterNearbyFish(Preview focal, Vector3 threat)
    {
        if (focal?.Habitat == null)
            return;
        float radius = Mathf.Clamp(_scatterRadius.Value, 0f, 6f);
        if (radius < 0.1f)
            return;
        float radiusSquared = radius * radius;
        int scattered = 0;
        Vector3 focalPosition = focal.Object != null
            ? focal.Object.transform.position
            : focal.WaterPoint;
        for (int index = 0; index < Previews.Count; index++)
        {
            Preview preview = Previews[index];
            if (preview == focal || preview.Habitat != focal.Habitat ||
                preview.Object == null || preview.Pursuing || preview.SwimmingAway)
                continue;
            if (HorizontalSquared(preview.Object.transform.position, focalPosition) > radiusSquared)
                continue;
            preview.Scattering = true;
            preview.ScatterThreat = threat;
            preview.ScatterEndTime = Time.time +
                Mathf.Clamp(_scatterSeconds.Value, 0.5f, 5f) *
                (0.82f + 0.35f * (float)PlacementRandom.NextDouble());
            preview.NextSwimDecision = 0f;
            ChooseScatterTarget(preview);
            scattered++;
        }
        if (scattered > 0)
            _log.LogInfo(
                $"SCHOOL startled {scattered} nearby fish; they remain in the finite pool."
            );
    }

    private static void UpdateRunningDisturbance()
    {
        if (!_runScatterEnabled.Value || !_populationInitialized || Previews.Count == 0)
            return;
        if (!TryGetPlayerPosition(out Vector3 playerPosition))
        {
            _hasPlayerMotionSample = false;
            return;
        }

        float now = Time.time;
        if (!_hasPlayerMotionSample)
        {
            _lastPlayerMotionPosition = playerPosition;
            _lastPlayerMotionTime = now;
            _hasPlayerMotionSample = true;
            return;
        }

        float elapsed = now - _lastPlayerMotionTime;
        if (elapsed < 0.10f)
            return;
        float distanceSquared = HorizontalSquared(playerPosition, _lastPlayerMotionPosition);
        _lastPlayerMotionPosition = playerPosition;
        _lastPlayerMotionTime = now;

        // Field transitions and scripted warps are not footsteps.
        if (distanceSquared > 64f || elapsed > 0.75f)
            return;
        float speed = Mathf.Sqrt(distanceSquared) / Math.Max(0.001f, elapsed);
        if (speed < Mathf.Clamp(_runScatterMinimumSpeed.Value, 0.5f, 8f) ||
            now < _nextRunScatterTime)
            return;

        bool isRunning;
        try
        {
            isRunning = GameController.Instance?.GetPlayer()?.IsRun() ?? false;
        }
        catch
        {
            isRunning = false;
        }
        if (!isRunning)
            return;

        float radius = Mathf.Clamp(_runScatterRadius.Value, 1f, 8f);
        float radiusSquared = radius * radius;
        int scattered = 0;
        for (int index = 0; index < Previews.Count; index++)
        {
            Preview preview = Previews[index];
            if (preview.Object == null || preview.Pursuing || preview.SwimmingAway ||
                HorizontalSquared(preview.Object.transform.position, playerPosition) > radiusSquared)
                continue;
            preview.Scattering = true;
            preview.ScatterThreat = playerPosition;
            preview.ScatterEndTime = now +
                Mathf.Clamp(_scatterSeconds.Value, 0.5f, 5f) *
                (0.82f + 0.35f * (float)PlacementRandom.NextDouble());
            preview.NextSwimDecision = 0f;
            ChooseScatterTarget(preview);
            scattered++;
        }

        if (scattered <= 0)
            return;
        _nextRunScatterTime = now + Mathf.Clamp(_runScatterCooldown.Value, 0.3f, 3f);
        _log.LogInfo(
            $"SCHOOL running footsteps startled {scattered} fish within {radius:0.0}m."
        );
    }

    private static bool ReservationIsValid(
        uint groupId,
        int toolLevel,
        BokuMonoDateTime dateTime,
        BokuMonoWeather weather,
        int windLevel,
        uint baitId)
    {
        FishingMasterData master = _reservedMaster;
        if (master == null || _reservedPreview == null || _reservedPreview.GroupId != groupId)
            return false;
        // Eligibility was fixed when this finite actor spawned. Hourly condition
        // changes must not silently turn an already-visible fish into a different
        // invisible lottery result.
        return true;
    }

    private static void ReleasePreviewVisual(Preview preview)
    {
        if (preview.NativeObject != null)
        {
            try
            {
                ResourceManager.Instance?.ReleasePrefab(preview.NativeObject);
            }
            catch
            {
                UnityEngine.Object.Destroy(preview.NativeObject);
            }
            preview.NativeObject = null;
        }

        for (int index = 0; index < preview.OwnedMaterials.Count; index++)
        {
            if (preview.OwnedMaterials[index] != null)
                UnityEngine.Object.Destroy(preview.OwnedMaterials[index]);
        }
        preview.OwnedMaterials.Clear();

        for (int index = 0; index < preview.OwnedNativeMeshes.Count; index++)
        {
            if (preview.OwnedNativeMeshes[index] != null)
                UnityEngine.Object.Destroy(preview.OwnedNativeMeshes[index]);
        }
        preview.OwnedNativeMeshes.Clear();
        preview.BendMeshes.Clear();

        if (preview.OwnedMesh != null)
        {
            UnityEngine.Object.Destroy(preview.OwnedMesh);
            preview.OwnedMesh = null;
        }

        if (preview.Object != null)
        {
            UnityEngine.Object.Destroy(preview.Object);
            preview.Object = null;
        }
    }

    private static void ClearPopulation()
    {
        _generation++;
        for (int index = 0; index < Previews.Count; index++)
            ReleasePreviewVisual(Previews[index]);
        Previews.Clear();
        Habitats.Clear();
        _reservedPreview = null;
        _reservedMaster = null;
        _reservationInjected = false;
        _catchConfirmed = false;
        _strictHoldLogged = false;
        _populationInitialized = false;
        _hasPlayerMotionSample = false;
        _nextRunScatterTime = 0f;
    }

    private static bool TryGetPlayerPosition(out Vector3 position)
    {
        position = default;
        try
        {
            GameController game = GameController.Instance;
            Transform transform = game?.GetPlayerTransform();
            if (transform == null)
                transform = game?.GetPlayerModelTransform();
            if (transform == null)
                return false;
            position = transform.position;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SchedulePoolRecovery(Habitat habitat)
    {
        if (habitat == null)
            return;
        float recoveryTime = Time.time + Mathf.Clamp(_respawnSeconds.Value, 15f, 600f);
        if (habitat.NextRespawnTime == float.MaxValue ||
            habitat.NextRespawnTime < Time.time)
            habitat.NextRespawnTime = recoveryTime;
    }

    private static void StartEscape(Preview preview)
    {
        if (preview == null || preview.Object == null)
        {
            if (preview != null)
            {
                Previews.Remove(preview);
                ReleasePreviewVisual(preview);
                SchedulePoolRecovery(preview.Habitat);
            }
            return;
        }

        preview.Object.SetActive(true);
        Vector3 away = preview.Object.transform.position - preview.PursuitTarget;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
            away = preview.WaterPoint - preview.PursuitTarget;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
            away = new Vector3(
                -Mathf.Sin(preview.Heading * Mathf.Deg2Rad),
                0f,
                -Mathf.Cos(preview.Heading * Mathf.Deg2Rad)
            );
        preview.EscapeDirection = away.normalized;
        preview.EscapeEndTime = Time.time + Mathf.Clamp(_escapeSeconds.Value, 0.6f, 3.0f);
        preview.SwimmingAway = true;
        preview.Pursuing = false;
        preview.ReachedBobber = false;
        preview.FinalBiteReady = false;
        _log.LogInfo(
            $"TARGET missed item={preview.Master.ItemId}; visible escape started."
        );
    }

    private static void CompleteFishing()
    {
        if (!_fishingActive)
            return;
        _fishingActive = false;

        if (_reservedPreview != null)
        {
            bool nativeWin = false;
            try
            {
                nativeWin = FishManager.Instance?.isMiniGameFishingWin ?? false;
            }
            catch
            {
            }
            if (_catchConfirmed || nativeWin)
            {
                Habitat consumedHabitat = _reservedPreview.Habitat;
                Previews.Remove(_reservedPreview);
                ReleasePreviewVisual(_reservedPreview);
                SchedulePoolRecovery(consumedHabitat);
                ReportState(
                    $"finite population now {Previews.Count}; confirmed catch " +
                    $"(callback={_catchConfirmed}, nativeWin={nativeWin}) begins staggered respawn timer"
                );
            }
            else
                StartEscape(_reservedPreview);
        }
        _reservedPreview = null;
        _reservedMaster = null;
        _reservationInjected = false;
        _catchConfirmed = false;
        _strictHoldLogged = false;
        _nextRefreshTime = Time.time + 1.25f;
    }

    private static class Patches
    {
        [HarmonyPatch(typeof(FieldManager), "Update")]
        [HarmonyPostfix]
        private static void FieldManagerUpdatePostfix() => TickPopulation();

        [HarmonyPatch(typeof(GameController), "Update")]
        [HarmonyPostfix]
        private static void GameControllerUpdatePostfix() => TickPopulation();

        [HarmonyPatch(typeof(FishManager), nameof(FishManager.FishingStart))]
        [HarmonyPrefix]
        private static void FishingStartPrefix(uint groupId, Vector3 buoyPosition)
        {
            // A quick cast can land during field-entry settling. Resolve topology
            // and the nearby pool synchronously so the cast never precedes its school.
            if (!_populationInitialized && !_buildingPopulation)
            {
                string signature = CurrentSignature(out _);
                if (!string.IsNullOrEmpty(signature))
                {
                    if (signature != _conditionSignature)
                    {
                        ClearPopulation();
                        _conditionSignature = signature;
                    }
                    InitializePoolTopology();
                }
            }
            if (_populationInitialized && !_buildingPopulation)
                RefreshNearbyPools();
            _fishingActive = true;
            SelectReservation(groupId, buoyPosition);
        }

        [HarmonyPatch(typeof(FishManager), nameof(FishManager.ExecuteLottery))]
        [HarmonyPrefix]
        private static bool ExecuteLotteryPrefix(
            uint groupId,
            int toolLevel,
            BokuMonoDateTime dateTime,
            BokuMonoWeather weather,
            int windLevel,
            uint baitId,
            ref FishingMasterData __result)
        {
            if (!_fishingActive || !ReservationIsValid(
                    groupId, toolLevel, dateTime, weather, windLevel, baitId))
                return true;

            __result = _reservedMaster;
            if (!_reservationInjected)
            {
                _reservationInjected = true;
                _log.LogInfo(
                    $"TARGET reserved group={groupId} item={__result.ItemId}; " +
                    "native lottery row is valid while the fish approaches."
                );
            }
            return false;
        }

        [HarmonyPatch(typeof(FishManager), nameof(FishManager.DecideFish))]
        [HarmonyPrefix]
        private static bool DecideFishPrefix()
        {
            if (!_fishingActive)
                return true;

            if (!_strictFiniteMode.Value)
                return true;

            if (_reservedPreview == null || !_reservedPreview.FinalBiteReady)
            {
                if (!_strictHoldLogged)
                {
                    _strictHoldLogged = true;
                    _log.LogInfo(
                        _reservedPreview == null
                            ? "STRICT holding native bite: no finite fish owns this bobber."
                            : _reservedPreview.ReachedBobber
                                ? $"STRICT holding native bite while item={_reservedMaster.ItemId} test-nibbles."
                                : $"STRICT holding native bite while item={_reservedMaster.ItemId} pursues bobber."
                    );
                }
                return false;
            }

            if (_strictHoldLogged)
                _log.LogInfo("STRICT final visible bite reached bobber; releasing native bite pipeline.");
            _strictHoldLogged = false;
            if (_reservedPreview.Object != null)
            {
                ScatterNearbyFish(_reservedPreview, _reservedPreview.PursuitTarget);
                _reservedPreview.Object.SetActive(false);
            }
            _log.LogInfo(
                $"TARGET bite committed item={_reservedMaster.ItemId}; silhouette hidden for splash/result."
            );
            return true;
        }

        [HarmonyPatch(typeof(PlayerActionFishingRod), "UpdateAction")]
        [HarmonyPrefix]
        private static void PlayerUpdateActionPrefix(PlayerActionFishingRod __instance)
        {
            if (!_fishingActive || !_strictFiniteMode.Value)
                return;

            // UpdateAction owns the hidden timer which calls DecideFish. Merely
            // rejecting DecideFish is too late: its caller continues into the
            // bite/catch animation after the skipped call. Keep the timer below
            // its threshold until a visible finite fish completes its final
            // nibble, then make that same native transition immediately.
            if (_reservedPreview == null || !_reservedPreview.FinalBiteReady)
                __instance.fishingHitTimer = 0f;
        }

        [HarmonyPatch(typeof(FishManager), nameof(FishManager.ForceFishingEnd))]
        [HarmonyPostfix]
        private static void ForceFishingEndPostfix() => CompleteFishing();

        [HarmonyPatch(typeof(FishManager), nameof(FishManager.OnCancelFishing))]
        [HarmonyPostfix]
        private static void OnCancelFishingPostfix() => CompleteFishing();

        [HarmonyPatch(typeof(FishManager), nameof(FishManager.FinishFishing))]
        [HarmonyPrefix]
        private static void FinishFishingPrefix()
        {
            if (_fishingActive && _reservedPreview != null)
                _catchConfirmed = true;
        }

        [HarmonyPatch(typeof(FishManager), nameof(FishManager.FinishFishing))]
        [HarmonyPostfix]
        private static void FinishFishingPostfix() => CompleteFishing();

        [HarmonyPatch(typeof(PlayerActionFishingRod), "HandleFishResult")]
        [HarmonyPrefix]
        private static void HandleFishResultPrefix(FishData data)
        {
            if (_fishingActive && _reservedPreview != null && data != null)
            {
                _catchConfirmed = true;
                _log.LogInfo(
                    $"TARGET catch confirmed by rod result item={_reservedMaster.ItemId}."
                );
            }
        }

        [HarmonyPatch(typeof(PlayerActionFishingRod), "EndAction")]
        [HarmonyPostfix]
        private static void PlayerEndActionPostfix() => CompleteFishing();

    }
}
