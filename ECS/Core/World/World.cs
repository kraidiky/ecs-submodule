﻿#if WORLD_TICK_THREADED
#define TICK_THREADED
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
#define CHECKPOINT_COLLECTOR
#endif
using System.Collections.Generic;
using Unity.Jobs;

namespace ME.ECS {
    
    using ME.ECS.Collections;

    [System.Flags]
    public enum WorldStep : byte {

        None = 0x0,
        
        // Default types
        Modules = 0x1,
        Systems = 0x2,
        Plugins = 0x4,
        
        // Tick type
        LogicTick = 0x8,
        VisualTick = 0x10,
        Simulate = 0x20,
        
        // Fast links
        ModulesVisualTick = WorldStep.Modules | WorldStep.VisualTick,
        SystemsVisualTick = WorldStep.Systems | WorldStep.VisualTick,
        ModulesLogicTick = WorldStep.Modules | WorldStep.LogicTick,
        PluginsLogicTick = WorldStep.Plugins | WorldStep.LogicTick,
        SystemsLogicTick = WorldStep.Systems | WorldStep.LogicTick,
        PluginsLogicSimulate = WorldStep.Plugins | WorldStep.Simulate | WorldStep.LogicTick,

    }

    [System.Flags]
    public enum ModuleState : byte {

        AllActive = 0x0,
        VisualInactive = 0x1,
        LogicInactive = 0x2,

    }

    #pragma warning disable
    [System.Serializable]
    public partial struct WorldViewsSettings {

    }

    [System.Serializable]
    public struct WorldSettings {

        public bool useJobsForSystems;
        public bool useJobsForViews;
        public bool createInstanceForFeatures;
        public int maxTicksSimulationCount;
        public bool turnOffViews;
        
        public WorldViewsSettings viewsSettings;
        
        public static WorldSettings Default => new WorldSettings() {
            useJobsForSystems = true,
            useJobsForViews = true,
            createInstanceForFeatures = true,
            turnOffViews = false,
            viewsSettings = new WorldViewsSettings()
        };

    }

    [System.Serializable]
    public partial struct WorldDebugViewsSettings { }

    [System.Serializable]
    public struct WorldDebugSettings {

        public bool createGameObjectsRepresentation;
        public bool showViewsOnScene;
        public WorldDebugViewsSettings viewsSettings;

        public static WorldDebugSettings Default => new WorldDebugSettings() {
            createGameObjectsRepresentation = false,
            showViewsOnScene = false,
            viewsSettings = new WorldDebugViewsSettings()
        };

    }
    #pragma warning restore
    
    public interface ICheckpointCollector {

        void Reset();
        void Checkpoint(object interestObj, WorldStep step);

    }

    #if ECS_COMPILE_IL2CPP_OPTIONS
    [Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.NullChecks, false),
     Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false),
     Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.DivideByZeroChecks, false)]
    #endif
    public abstract class WorldBase {
        
        internal WorldStep currentStep;
        internal ListCopyable<IFeatureBase> features;
        internal ListCopyable<IModuleBase> modules;
        internal BufferArray<SystemGroup> systemGroups;
        internal int systemGroupsLength;

        internal List<FilterAction> filterActions;

        internal ListCopyable<ModuleState> statesFeatures;
        internal ListCopyable<ModuleState> statesModules;

        internal ICheckpointCollector checkpointCollector;
        
        internal float tickTime;
        internal double timeSinceStart;
        public bool isActive;

        public IContext currentSystemContext { get; internal set; }
        public ISystemFilter currentSystemContextFilter { get; internal set; }
        public BufferArray<bool> currentSystemContextFiltersUsed;
        public bool currentSystemContextFiltersUsedAnyChanged;

        internal Tick simulationFromTick;
        internal Tick simulationToTick;

        public WorldSettings settings { get; internal set; }
        public WorldDebugSettings debugSettings { get; internal set; }

        #if WORLD_THREAD_CHECK
        internal System.Threading.Thread worldThread;
        #endif

    }

    #if ECS_COMPILE_IL2CPP_OPTIONS
    [Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.NullChecks, false),
     Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false),
     Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.DivideByZeroChecks, false)]
    #endif
    public sealed partial class World : WorldBase, IPoolableSpawn, IPoolableRecycle {

        private const int FEATURES_CAPACITY = 100;
        private const int SYSTEMS_CAPACITY = 100;
        private const int MODULES_CAPACITY = 100;
        private const int ENTITIES_CACHE_CAPACITY = 100;
        private const int WORLDS_CAPACITY = 4;
        private const int FILTERS_CACHE_CAPACITY = 10;
        
        private static class FiltersDirectCache {

            internal static BufferArray<BufferArray<bool>> dic = new BufferArray<BufferArray<bool>>(null, 0);//new bool[World.WORLDS_CAPACITY][];

        }

        private static int registryWorldId = 0;

        public int id {
            [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            get;
            [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            private set;
        }
        
        private State resetState;
        internal State currentState;
        internal FiltersTree filtersTree;
        private uint seed;
        private int cpf; // CPF = Calculations per frame
        internal int entitiesCapacity;
        private bool isLoading;
        private bool isLoaded;

        /// <summary>
        /// Returns last frame CPF
        /// </summary>
        /// <returns></returns>
        public int GetCPF() {

            return this.cpf;

        }

        public bool IsLoaded() {

            return this.isLoaded;

        }
        
        public bool IsLoading() {

            return this.isLoading;

        }
        
        public struct WorldState {

            public State state;
            public ME.ECS.StatesHistory.HistoryStorage events;
            public Tick tick;

        }

        public byte[] Serialize() {
            
            var statesHistory = this.GetModule<ME.ECS.StatesHistory.IStatesHistoryModuleBase>();
                    
            var data = new WorldState();
            data.state = statesHistory.GetOldestState();
            data.events = statesHistory.GetHistoryStorage(data.state.tick + Tick.One, this.GetCurrentTick());
            data.tick = this.GetCurrentTick();

            var networkModule = this.GetModule<ME.ECS.Network.INetworkModuleBase>();
            var serializer = networkModule.GetSerializer();
            return serializer.SerializeWorld(data);
            
        }
        
        public void Deserialize<TState>(byte[] worldData, System.Collections.Generic.List<byte[]> eventsWhileConnecting) where TState : State, new() {
            
            var statesHistory = this.GetModule<ME.ECS.StatesHistory.IStatesHistoryModuleBase>();
            statesHistory.Reset();

            var networkModule = this.GetModule<ME.ECS.Network.INetworkModuleBase>();
            var data = networkModule.GetSerializer().DeserializeWorld(worldData);
            
            // Make a ref of current filters to the new state
            this.GetState().filters.Clear();
            data.state.filters = this.GetState().filters;
            this.GetState().filters = null;
            
            this.SetState<TState>(data.state);
            data.state.filters.OnDeserialize(this.GetEntitiesCount());
            statesHistory.AddEvents(data.events.events);

            statesHistory.BeginAddEvents();
            for (int i = 0; i < eventsWhileConnecting.Count; ++i) {

                var package = networkModule.GetSerializer().Deserialize(eventsWhileConnecting[i]);
                statesHistory.AddEvent(package);

            }
            statesHistory.EndAddEvents();
            
            // Update logic
            var list = PoolListCopyable<Entity>.Spawn(World.ENTITIES_CACHE_CAPACITY);
            if (this.ForEachEntity(list) == true) {

                for (int i = 0; i < list.Count; ++i) {

                    ref var entity = ref list[i];
                    ComponentsInitializerWorld.Init(in entity);
                    this.CreateEntityPlugins(entity);
                    this.UpdateFiltersOnFilterCreate(entity);

                }

            }
            PoolListCopyable<Entity>.Recycle(ref list);

            this.SetFromToTicks(data.state.tick, data.tick);
            this.UpdateLogic(0f);

            // Update views
            this.GetModule<ME.ECS.Views.ViewsModule>().SetRequestsAsDirty();

        }
        
        public void Load(System.Action onComplete) {

            this.isLoading = true;
            this.isLoaded = false;
            
            var awatingCount = 0;
            for (int i = 0; i < this.systemGroupsLength; ++i) {

                var group = this.systemGroups.arr[i];
                awatingCount += (group.runtimeSystem.systemLoadable != null ? group.runtimeSystem.systemLoadable.Count : 0);
                
            }

            if (awatingCount == 0) {

                this.isLoading = false;
                this.isLoaded = true;
                onComplete.Invoke();
                return;

            }
            
            for (int i = 0; i < this.systemGroupsLength; ++i) {

                var group = this.systemGroups.arr[i];
                if (group.runtimeSystem.systemLoadable == null) continue;
                for (int j = 0; j < group.runtimeSystem.systemLoadable.Count; ++j) {

                    var loadableSystem = group.runtimeSystem.systemLoadable[j];
                    loadableSystem.Load(() => {

                        --awatingCount;
                        if (awatingCount == 0) {
                            
                            this.isLoading = false;
                            this.isLoaded = true;
                            onComplete.Invoke();
                            
                        }

                    });

                }

            }
            
        }
        
        internal void UpdateGroup(SystemGroup group) {

            this.systemGroups.arr[group.worldIndex] = group;

        }
        
        public int AddSystemGroup(ref SystemGroup group) {

            var index = this.systemGroupsLength++;
            ArrayUtils.Resize(index, ref this.systemGroups, resizeWithOffset: false);
            this.systemGroups.arr[index] = group;
            return index;

        }
        
        #if WORLD_THREAD_CHECK
        public void SetWorldThread(System.Threading.Thread thread) {

            this.worldThread = thread;

        }
        #endif
        
        void IPoolableSpawn.OnSpawn() {

            #if WORLD_THREAD_CHECK
            this.worldThread = System.Threading.Thread.CurrentThread;
            #endif

            #if UNITY_EDITOR
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;
            #endif
            
            this.currentSystemContextFiltersUsed = PoolArray<bool>.Spawn(World.FILTERS_CACHE_CAPACITY);
            this.currentSystemContextFiltersUsedAnyChanged = false;
            
            this.currentState = default;
            this.resetState = default;
            this.currentStep = default;
            this.checkpointCollector = default;
            this.tickTime = default;
            this.timeSinceStart = default;
            this.entitiesCapacity = default;
            this.filtersTree = default;
            
            this.features = PoolListCopyable<IFeatureBase>.Spawn(World.FEATURES_CAPACITY);
            //this.systems = PoolList<ISystemBase>.Spawn(World.SYSTEMS_CAPACITY);
            this.modules = PoolListCopyable<IModuleBase>.Spawn(World.MODULES_CAPACITY);
            this.statesFeatures = PoolListCopyable<ModuleState>.Spawn(World.FEATURES_CAPACITY);
            //this.statesSystems = PoolList<ModuleState>.Spawn(World.SYSTEMS_CAPACITY);
            this.statesModules = PoolListCopyable<ModuleState>.Spawn(World.MODULES_CAPACITY);
            this.systemGroups = PoolArray<SystemGroup>.Spawn(World.FEATURES_CAPACITY);
            this.systemGroupsLength = 0;
            this.filterActions = PoolList<FilterAction>.Spawn(4);
            
            this.OnSpawnStructComponents();
            this.OnSpawnComponents();
            this.OnSpawnMarkers();

            this.isActive = true;

        }

        public void RecycleResetState<TState>() where TState : State, new() {
            
            if (this.resetState != this.currentState) WorldUtilities.ReleaseState<TState>(ref this.resetState);
            
        }

        public void RecycleStates<TState>() where TState : State, new() {
            
            WorldUtilities.ReleaseState<TState>(ref this.currentState);

        }
        
        void IPoolableRecycle.OnRecycle() {
            
            var list = PoolListCopyable<Entity>.Spawn(World.ENTITIES_CACHE_CAPACITY);
            if (this.ForEachEntity(list) == true) {

                for (int i = 0; i < list.Count; ++i) {

                    ref var item = ref list[i];
                    this.RemoveEntity(item);

                }

            }
            PoolListCopyable<Entity>.Recycle(ref list);
            this.GetState().storage.ApplyDead();

            this.filtersTree.Dispose();
            
            PoolArray<bool>.Recycle(ref this.currentSystemContextFiltersUsed);
            this.currentSystemContextFiltersUsedAnyChanged = default;

            for (int i = 0; i < this.filterActions.Count; ++i) {
                
                this.filterActions[i].Dispose();
                
            }
            PoolList<FilterAction>.Recycle(ref this.filterActions);
            
            #if WORLD_THREAD_CHECK
            this.worldThread = null;
            #endif
            this.isActive = false;

            this.OnRecycleMarkers();
            this.OnRecycleComponents();
            this.OnRecycleStructComponents();
            
            PoolArray<bool>.Recycle(ref FiltersDirectCache.dic.arr[this.id]);
            
            PoolListCopyable<IFeatureBase>.Recycle(ref this.features);

            for (int i = 0; i < this.systemGroupsLength; ++i) {

                this.systemGroups.arr[i].Deconstruct();

            }
            PoolArray<SystemGroup>.Recycle(ref this.systemGroups);
            
            for (int i = 0; i < this.modules.Count; ++i) {
                
                this.modules[i].OnDeconstruct();
                PoolModules.Recycle(this.modules[i]);

            }
            PoolListCopyable<IModuleBase>.Recycle(ref this.modules);
            
            PoolListCopyable<ModuleState>.Recycle(ref this.statesModules);
            PoolListCopyable<ModuleState>.Recycle(ref this.statesFeatures);
            
            //PoolInternalBaseNoStackPool.Clear();
            //PoolInternalBase.Clear();
            
        }

        public void SetSettings(WorldSettings settings) {

            this.settings = settings;

        }

        public void SetDebugSettings(WorldDebugSettings settings) {
            
            this.debugSettings = settings;
            
        }

        public void SetCheckpointCollector(ICheckpointCollector checkpointCollector) {

            this.checkpointCollector = checkpointCollector;

        }

        public void Checkpoint(object interestObj) {
            
            #if CHECKPOINT_COLLECTOR
            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(interestObj, this.currentStep);
            #endif
            
        }

        public void SetFeatureState(IFeatureBase feature, ModuleState state) {
            
            var index = this.features.IndexOf(feature);
            if (index >= 0) {

                this.statesFeatures[index] = state;

            }
            
        }
        
        public ModuleState GetFeatureState(IFeatureBase feature) {

            var index = this.features.IndexOf(feature);
            if (index >= 0) {

                return this.statesFeatures[index];

            }

            return ModuleState.AllActive;

        }

        public bool IsSystemActive(ISystemBase system, RuntimeSystemFlag state) {

            for (int i = 0; i < this.systemGroupsLength; ++i) {

                if (this.systemGroups.arr[i].IsSystemActive(system, state) == true) {

                    return true;

                }

            }
            
            return false;

        }

        public void SetModuleState(IModuleBase module, ModuleState state) {
            
            var index = this.modules.IndexOf(module);
            if (index >= 0) {

                this.statesModules[index] = state;

            }
            
        }
        
        public ModuleState GetModuleState(IModuleBase module) {

            var index = this.modules.IndexOf(module);
            if (index >= 0) {

                return this.statesModules[index];

            }

            return ModuleState.AllActive;

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void SetId(int forcedWorldId = 0) {

            if (forcedWorldId > 0) {

                this.id = forcedWorldId;

            } else {
                
                this.id = ++World.registryWorldId;
                
            }

            ArrayUtils.Resize(this.id, ref FiltersDirectCache.dic);

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public UnityEngine.Vector3 GetRandomInSphere(UnityEngine.Vector3 center, float maxRadius) {
            RandomUtils.ThreadCheck(this);
            return this.currentState.randomState.GetRandomInSphere(center, maxRadius);
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int GetRandomRange(int from, int to) {
            RandomUtils.ThreadCheck(this);
            return this.currentState.randomState.GetRandomRange(from, to);
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public float GetRandomRange(float from, float to) {
            RandomUtils.ThreadCheck(this);
            return this.currentState.randomState.GetRandomRange(from, to);
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public float GetRandomValue() {
            RandomUtils.ThreadCheck(this);
            return this.currentState.randomState.GetRandomValue();
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int GetSeedValue() {

            return this.GetCurrentTick();
            
        }

        public void SetSeed(uint seed) {
            this.seed = seed;
            this.currentState?.randomState.SetSeed(this.seed);
            this.resetState?.randomState.SetSeed(this.seed);
        }
        
        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void SetTickTime(float tickTime) {

            this.tickTime = tickTime;

        }
        
        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public float GetTickTime() {

            return this.tickTime;

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public Tick GetStateTick() {

            return this.GetState().tick;

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public double GetTimeSinceStart() {

            return this.timeSinceStart;

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void SetTimeSinceStart(double time) {

            this.timeSinceStart = time;

        }

        partial void OnSpawnMarkers();
        partial void OnRecycleMarkers();

        partial void OnSpawnComponents();
        partial void OnRecycleComponents();

        partial void OnSpawnStructComponents();
        partial void OnRecycleStructComponents();
        
        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int GetStateHash() {

            if (this.statesHistoryModule != null) {

                return this.statesHistoryModule.GetStateHash(this.GetState());

            }

            return this.currentState.GetHash();

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public FilterData GetFilter(int id) {

            return this.currentState.filters.filters.arr[id - 1];

        }

        internal FilterData GetFilterByHashCode(int hashCode) {

            return this.currentState.filters.GetByHashCode(hashCode);

        }

        public FilterData GetFilterEquals(FilterData other) {
            
            return this.currentState.filters.GetFilterEquals(other);
            
        }

        public bool HasFilter(FilterData filterRef) {
            
            //ArrayUtils.Resize(this.id, ref FiltersDirectCache.dic);
            ref var dic = ref FiltersDirectCache.dic.arr[this.id];
            if (dic.arr != null) {
            
                return dic.arr[filterRef.id - 1] == true;

            }

            return false;

        }

        public bool HasFilter(int id) {

            var idx = id - 1;
            //ArrayUtils.Resize(this.id, ref FiltersDirectCache.dic);
            ref var dic = ref FiltersDirectCache.dic.arr[this.id];
            if (dic.arr != null && idx >= 0 && idx < dic.Length) {
            
                return dic.arr[idx] == true;

            }

            return false;

        }

        #region GlobalEvents
        private struct GlobalEventFrameItem {

            public GlobalEvent globalEvent;
            public Entity data;

        }

        public enum GlobalEventType : byte {

            Logic,
            Visual,

        }
        
        private List<GlobalEventFrameItem> globalEventFrameItems = new List<GlobalEventFrameItem>();
        private HashSet<long> globalEventFrameEvents = new HashSet<long>();

        private List<GlobalEventFrameItem> globalEventLogicItems = new List<GlobalEventFrameItem>();
        private HashSet<long> globalEventLogicEvents = new HashSet<long>();

        public void ProcessGlobalEvents(GlobalEventType globalEventType) {

            if (globalEventType == GlobalEventType.Visual) {

                for (int i = 0; i < this.globalEventFrameItems.Count; ++i) {

                    var item = this.globalEventFrameItems[i];
                    item.globalEvent.Run(in item.data);

                }

                this.globalEventFrameItems.Clear();
                this.globalEventFrameEvents.Clear();

            } else if (globalEventType == GlobalEventType.Logic) {
            
                for (int i = 0; i < this.globalEventLogicItems.Count; ++i) {

                    var item = this.globalEventLogicItems[i];
                    item.globalEvent.Run(in item.data);

                }

                this.globalEventLogicItems.Clear();
                this.globalEventLogicEvents.Clear();

            }
            
        }

        public bool CancelGlobalEvent(GlobalEvent globalEvent, in Entity entity, GlobalEventType globalEventType) {

            var key = MathUtils.GetKey(globalEvent.GetHashCode(), entity.GetHashCode());
            if (globalEventType == GlobalEventType.Visual) {

                if (this.globalEventFrameEvents.Contains(key) == true) {

                    for (int i = 0; i < this.globalEventFrameItems.Count; ++i) {
                        
                        var item = this.globalEventFrameItems[i];
                        if (item.globalEvent == globalEvent && item.data == entity) {

                            this.globalEventFrameEvents.Remove(key);
                            this.globalEventFrameItems.RemoveAt(i);
                            return true;

                        } 
                        
                    }

                }

            } else if (globalEventType == GlobalEventType.Logic) {
                
                if (this.globalEventLogicEvents.Contains(key) == true) {

                    for (int i = 0; i < this.globalEventLogicItems.Count; ++i) {
                        
                        var item = this.globalEventLogicItems[i];
                        if (item.globalEvent == globalEvent && item.data == entity) {

                            this.globalEventLogicEvents.Remove(key);
                            this.globalEventLogicItems.RemoveAt(i);
                            return true;

                        } 
                        
                    }

                }
                
            }

            return false;

        }
        
        public void RegisterGlobalEvent(GlobalEvent globalEvent, in Entity entity, GlobalEventType globalEventType) {

            var key = MathUtils.GetKey(globalEvent.GetHashCode(), entity.GetHashCode());
            if (globalEventType == GlobalEventType.Visual) {

                if (this.globalEventFrameEvents.Contains(key) == false) {

                    this.globalEventFrameEvents.Add(key);
                    this.globalEventFrameItems.Add(new GlobalEventFrameItem() {
                        globalEvent = globalEvent,
                        data = entity
                    });

                }

            } else if (globalEventType == GlobalEventType.Logic) {
                
                if (this.globalEventLogicEvents.Contains(key) == false) {

                    this.globalEventLogicEvents.Add(key);
                    this.globalEventLogicItems.Add(new GlobalEventFrameItem() {
                        globalEvent = globalEvent,
                        data = entity
                    });

                }

            }

        }
        #endregion

        #region EntityActions
        #if ENTITY_ACTIONS
        private static class EntityActionDirectCache<TComponent> where TComponent : struct, IStructComponent {

            public static BufferArray<ListCopyable<EntityAction<TComponent>>> data;

        }
        
        public void RaiseEntityActionOnAdd<TComponent>(in Entity entity) where TComponent : struct, IStructComponent {
            
            ArrayUtils.Resize(this.id, ref EntityActionDirectCache<TComponent>.data);
            ref var list = ref EntityActionDirectCache<TComponent>.data.arr[this.id];
            if (list == null) return;
            for (int i = 0, count = list.Count; i < count; ++i) {

                list[i].ExecuteOnAdd(in entity);

            }

        }
        
        public void RaiseEntityActionOnRemove<TComponent>(in Entity entity) where TComponent : struct, IStructComponent {
            
            ArrayUtils.Resize(this.id, ref EntityActionDirectCache<TComponent>.data);
            ref var list = ref EntityActionDirectCache<TComponent>.data.arr[this.id];
            if (list == null) return;
            for (int i = 0, count = list.Count; i < count; ++i) {

                list[i].ExecuteOnRemove(in entity);

            }

        }

        public void RegisterEntityAction<TComponent>(EntityAction<TComponent> action) where TComponent : struct, IStructComponent {
            
            ArrayUtils.Resize(this.id, ref EntityActionDirectCache<TComponent>.data);
            ref var list = ref EntityActionDirectCache<TComponent>.data.arr[this.id];
            if (list == null) list = PoolList<EntityAction<TComponent>>.Spawn(10);
            list.Add(action);

        }
        
        public void UnRegisterEntityAction<TComponent>(EntityAction<TComponent> action) where TComponent : struct, IStructComponent {
            
            ArrayUtils.Resize(this.id, ref EntityActionDirectCache<TComponent>.data);
            ref var list = ref EntityActionDirectCache<TComponent>.data.arr[this.id];
            if (list == null) return;
            list.Remove(action);

        }
        #endif
        #endregion

        public void Register(FilterAction filterAction) {

            this.filterActions.Add(filterAction);

        }

        public void Register(FilterData filterRef) {

            this.currentState.filters.Register(filterRef);

            ArrayUtils.Resize(filterRef.id, ref this.currentSystemContextFiltersUsed);

            //ArrayUtils.Resize(this.id, ref FiltersDirectCache.dic);
            ref var dic = ref FiltersDirectCache.dic.arr[this.id];
            ArrayUtils.Resize(filterRef.id - 1, ref dic);
            dic.arr[filterRef.id - 1] = true;
            this.filtersTree.Add(filterRef);
            
            if (this.entitiesCapacity > 0) filterRef.SetEntityCapacity(this.entitiesCapacity);
            
            var list = PoolListCopyable<Entity>.Spawn(World.ENTITIES_CACHE_CAPACITY);
            if (this.ForEachEntity(list) == true) {

                for (int i = 0; i < list.Count; ++i) {

                    ref var item = ref list[i];
                    ComponentsInitializerWorld.Init(in item);
                    this.UpdateFiltersOnFilterCreate(item);

                }

            }
            PoolListCopyable<Entity>.Recycle(ref list);
            
        }

        public void Register(ref FiltersStorage filtersRef, bool freeze, bool restore) {

            const int capacity = 10;
            if (filtersRef == null) {

                filtersRef = PoolClass<FiltersStorage>.Spawn();
                filtersRef.Initialize(capacity);
                filtersRef.SetFreeze(freeze);

            } else {

                filtersRef.SetFreeze(freeze);

            }
            
            if (freeze == false) {

                //this.filtersStorage = filtersRef;
                
                //ArrayUtils.Resize(this.id, ref FiltersDirectCache.dic);
                ref var dic = ref FiltersDirectCache.dic.arr[this.id];
                if (dic.arr != null) {
                    
                    System.Array.Clear(dic.arr, 0, dic.Length);
                    for (int i = 0; i < filtersRef.filters.Length; ++i) {

                        var filterRef = filtersRef.filters.arr[i];
                        if (filterRef != null) {
                        
                            ArrayUtils.Resize(filterRef.id - 1, ref dic);
                            dic.arr[filterRef.id - 1] = true;

                        }

                    }

                }
                
            }
            
        }

        public void Register(ref Components componentsRef, bool freeze, bool restore) {
            
            const int capacity = 4;
            if (componentsRef == null) {

                componentsRef = PoolClass<Components>.Spawn();
                componentsRef.Initialize(capacity);
                componentsRef.SetFreeze(freeze);

            } else {

                componentsRef.SetFreeze(freeze);

            }

            /*if (restore == true) {

                var data = componentsRef.GetData();
                foreach (var item in data) {

                    var components = item.Value;
                    for (int i = 0, count = components.Count; i < count; ++i) {

                        this.AddComponent<TEntity, IComponent<TState, TEntity>>(Entity.Create<TEntity>(item.Key), components[i]);

                    }

                }

            }*/

        }

        public void Register(ref Storage storageRef, bool freeze, bool restore) {

            this.RegisterPluginsModuleForEntity();

            if (storageRef == null) {
                
                storageRef = PoolClass<Storage>.Spawn();
                storageRef.Initialize(World.ENTITIES_CACHE_CAPACITY);
                storageRef.SetFreeze(freeze);
                
            }

            if (freeze == false) {

                if (this.sharedEntity.id == 0 && this.sharedEntityInitialized == false) {

                    // Create shared entity which should store shared components
                    this.sharedEntityInitialized = true;
                    this.sharedEntity = this.AddEntity();

                }

            }

            if (restore == true) {

                this.BeginRestoreEntities();
                
                // Update entities cache
                var list = PoolListCopyable<Entity>.Spawn(World.ENTITIES_CACHE_CAPACITY);
                if (this.ForEachEntity(list) == true) {

                    for (int i = 0; i < list.Count; ++i) {

                        ref var item = ref list[i];
                        this.UpdateFiltersOnFilterCreate(item);
                        this.CreateEntityPlugins(item);

                    }

                }
                PoolListCopyable<Entity>.Recycle(ref list);
                
                this.EndRestoreEntities();

            }

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public bool HasResetState() {

            return this.resetState != null;

        }

        public void SaveResetState<TState>() where TState : State, new() {

            if (this.resetState != null) WorldUtilities.ReleaseState<TState>(ref this.resetState);
            this.resetState = WorldUtilities.CreateState<TState>();
            this.resetState.Initialize(this, freeze: true, restore: false);
            this.resetState.CopyFrom(this.GetState());

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public State GetResetState() {

            return this.resetState;

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public T GetResetState<T>() where T : State {

            return (T)this.resetState;

        }

        public void SetState<TState>(State state) where TState : State, new() {

            //System.Array.Clear(this.storagesCache, 0, this.storagesCache.Length);
            //System.Array.Clear(this.componentsCache, 0, this.componentsCache.Length);
            
            if (this.currentState != null && this.currentState != state) WorldUtilities.ReleaseState<TState>(ref this.currentState);
            this.currentState = state;
            state.Initialize(this, freeze: false, restore: true);

            this.SetSeed(this.seed);
            
        }

        public void SetStateDirect(State state) {

            this.currentState = state;
            state.Initialize(this, freeze: false, restore: false);

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public State GetState() {

            return this.currentState;

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public T GetState<T>() where T : State {

            return (T)this.currentState;

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public WorldStep GetCurrentStep() {

            return this.currentStep;

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public bool HasStep(WorldStep step) {

            return (this.currentStep & step) != 0;

        }

        public void UpdateAllFilters() {
            
            var filters = this.currentState.filters.GetData();
            for (int i = 0; i < filters.Length; ++i) {
                
                filters.arr[i].Update();
                
            }
            
        }

        public void SetEntityCapacityInFilters(int capacity) {

            //ArrayUtils.Resize(this.id, ref FiltersDirectCache.dic);
            ref var dic = ref FiltersDirectCache.dic.arr[this.id];
            if (dic.arr != null) {

                for (int i = 0; i < dic.Length; ++i) {

                    if (dic.arr[i] == false) continue;
                    var filterId = i + 1;
                    var filter = this.GetFilter(filterId);
                    filter.SetEntityCapacity(capacity);

                }

            }

        }
        
        public void CreateEntityInFilters(Entity entity) {

            //ArrayUtils.Resize(this.id, ref FiltersDirectCache.dic);
            ref var dic = ref FiltersDirectCache.dic.arr[this.id];
            if (dic.arr != null) {

                for (int i = 0; i < dic.Length; ++i) {

                    if (dic.arr[i] == false) continue;
                    var filterId = i + 1;
                    var filter = this.GetFilter(filterId);
                    filter.OnEntityCreate(entity);

                }

            }

        }
        
        public void UpdateFiltersOnFilterCreate(Entity entity) {

            //ArrayUtils.Resize(this.id, ref FiltersDirectCache.dic);
            ref var dic = ref FiltersDirectCache.dic.arr[this.id];
            if (dic.arr != null) {

                for (int i = 0; i < dic.Length; ++i) {

                    if (dic.arr[i] == false) continue;
                    var filterId = i + 1;
                    var filter = this.GetFilter(filterId);
                    filter.OnEntityCreate(in entity);
                    if (filter.IsForEntity(entity.id) == false) continue;
                    filter.OnUpdate(in entity);

                }

            }

        }
        
        public void UpdateFilters(in Entity entity) {

            //ArrayUtils.Resize(this.id, ref FiltersDirectCache.dic);
            ref var dic = ref FiltersDirectCache.dic.arr[this.id];
            if (dic.arr != null) {

                for (int i = 0; i < dic.Length; ++i) {

                    if (dic.arr[i] == false) continue;
                    var filterId = i + 1;
                    var filter = this.GetFilter(filterId);
                    if (filter.IsForEntity(entity.id) == false) continue;
                    filter.OnUpdate(in entity);

                }

            }

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void CopyFrom(in Entity from, in Entity to) {

            #if WORLD_EXCEPTIONS
            if (from.IsAlive() == false) {
                
                EmptyEntityException.Throw(from);
                
            }
            #endif

            #if WORLD_EXCEPTIONS
            if (to.IsAlive() == false) {
                
                EmptyEntityException.Throw(to);
                
            }
            #endif

            {
                // Clear entity
                this.RemoveComponents(in to);
                this.RemoveData(in to);
            }

            {
                // Copy data
                this.currentState.components.CopyFrom(in from, in to);
                this.currentState.structComponents.CopyFrom(in from, in to);
                this.UpdateFilters(in to);
            }
            
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public bool IsAlive(int entityId, ushort generation) {

            // Inline manually
            return this.currentState.storage.cache.arr[entityId].generation == generation;
            //return this.currentState.storage.IsAlive(entityId, version);
            
        }

        public ref Entity GetEntityById(int id) {
            
            ref var entitiesList = ref this.currentState.storage;
            ref var ent = ref entitiesList[id];
            if (this.IsAlive(ent.id, ent.generation) == false) return ref Entity.Empty;
            
            return ref ent;

        }

        public void SetEntitiesCapacity(int capacity) {

            this.entitiesCapacity = capacity;
            this.SetEntityCapacityPlugins(capacity);
            this.SetEntityCapacityInFilters(capacity);
            
        }
        
        public ref Entity AddEntity(string name = null) {

            #if WORLD_STATE_CHECK
            if (this.HasStep(WorldStep.LogicTick) == false && this.HasResetState() == true) {

                OutOfStateException.ThrowWorldStateCheck();
                
            }
            #endif

            ref var entity = ref this.currentState.storage.Alloc();
            this.UpdateEntity(in entity);
            
            if (name != null) {

                entity.SetData(new ME.ECS.Name.Name() {
                    value = name
                });

            }

            return ref entity;

        }

        public int GetEntitiesCount() {

            return this.currentState.storage.AliveCount;

        }
        
        public void UpdateEntity(in Entity entity) {
            
            this.CreateEntityPlugins(entity);
            this.CreateEntityInFilters(entity);
            this.UpdateFilters(entity);

        }

        public bool ForEachEntity(ListCopyable<Entity> results) {

            return this.currentState.storage.ForEach(results);
            
        }

        public ListCopyable<int> GetAliveEntities() {

            return this.currentState.storage.GetAlive();
            
        }

        /*public bool HasEntity<TEntity>(int entityId) where TEntity : struct, IEntity {
            
            var key = MathUtils.GetKey(this.id, entityId);
            return EntitiesDirectCache<TState, TEntity>.dataByint.ContainsKey(key);

        }*/

        /*public void RemoveEntities<TEntity>(TEntity data) where TEntity : struct, IEntity {

            var key = MathUtils.GetKey(this.id, data.entity.id);
            if (EntitiesDirectCache<TState, TEntity>.dataByint.Remove(key) == true) {

                this.RemoveFromFilters(data);
                
                RefList<TEntity> listEntities;
                if (EntitiesDirectCache<TState, TEntity>.entitiesList.TryGetValue(this.id, out listEntities) == true) {
                
                    this.DestroyEntityPlugins<TEntity>(data.entity);
                    listEntities.Remove(data);
                    this.RemoveComponents(data.entity);

                }

            }

        }*/

        public bool RemoveEntity(in Entity entity) {

            #if WORLD_EXCEPTIONS
            if (entity.IsAlive() == false) {

                EmptyEntityException.Throw(entity);

            }
            #endif
            
            if (this.currentState.storage.Dealloc(in entity) == true) {
            
                this.RemoveFromFilters_INTERNAL(entity);
                this.DestroyEntityPlugins(in entity);
                this.RemoveComponents(entity);

                this.currentState.storage.IncrementGeneration(in entity);
                
                return true;

            } else {

                UnityEngine.Debug.LogError("Failed to remove entity " + entity);

            }

            return false;

        }
        
        /// <summary>
        /// Get first module by type
        /// </summary>
        /// <typeparam name="TModule"></typeparam>
        /// <returns></returns>
        public TModule GetModule<TModule>() where TModule : IModuleBase {

            if (this.modules == null) return default;
            
            for (int i = 0, count = this.modules.Count; i < count; ++i) {

                var module = this.modules[i];
                if (module is TModule tModule) {

                    return tModule;

                }

            }

            return default;

        }
        
        public List<TModule> GetModules<TModule>(List<TModule> output) where TModule : IModuleBase {

            output.Clear();
            for (int i = 0, count = this.modules.Count; i < count; ++i) {

                var module = this.modules[i];
                if (module is TModule tModule) {

                    output.Add(tModule);

                }

            }

            return output;

        }

        public bool HasModule<TModule>() where TModule : class, IModuleBase {

            for (int i = 0, count = this.modules.Count; i < count; ++i) {

                var module = this.modules[i];
                if (module is TModule) {

                    return true;

                }

            }

            return false;

        }

        /// <summary>
        /// Add module by type
        /// Retrieve module from pool, OnConstruct() call
        /// </summary>
        /// <typeparam name="TModule"></typeparam>
        /// <returns></returns>
        public bool AddModule<TModule>() where TModule : class, IModuleBase, new() {
            
            WorldUtilities.SetWorld(this);
            
            var instance = PoolModules.Spawn<TModule>();
            instance.world = this;
            if (instance is IModuleValidation instanceValidate) {

                if (instanceValidate.CouldBeAdded() == false) {

                    instance.world = null;
                    PoolModules.Recycle(ref instance);
                    //throw new System.Exception("Couldn't add new module `" + instanceValidate + "`(" + nameof(TModule) + ") because of CouldBeAdded() returns false.");
                    return false;
                    
                }

            }
            
            this.modules.Add(instance);
            this.statesModules.Add(ModuleState.AllActive);
            instance.OnConstruct();

            return true;

        }

        /// <summary>
        /// Remove modules by type
        /// Return modules into pool, OnDeconstruct() call
        /// </summary>
        public void RemoveModules<TModule>() where TModule : class, IModuleBase, new() {

            for (int i = 0, count = this.modules.Count; i < count; ++i) {

                var module = this.modules[i];
                if (module is TModule tModule) {

                    PoolModules.Recycle(tModule);
                    this.modules.RemoveAt(i);
                    this.statesModules.RemoveAt(i);
                    module.OnDeconstruct();
                    --i;
                    --count;

                }

            }

        }
        
        public bool HasFeature<TFeature>() where TFeature : class, IFeatureBase, new() {

            for (int i = 0, count = this.features.Count; i < count; ++i) {

                if (this.features[i] is TFeature) return true;

            }

            return false;

        }

        public TFeature GetFeature<TFeature>() where TFeature : IFeatureBase {

            for (int i = 0, count = this.features.Count; i < count; ++i) {

                if (this.features[i] is TFeature) return (TFeature)this.features[i];

            }

            return default;

        }

        /// <summary>
        /// Add feature manually
        /// Pool will not be used, OnConstruct() call
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="parameters"></param>
        public bool AddFeature(IFeatureBase instance, bool doConstruct = true) {

            WorldUtilities.SetWorld(this);
            
            instance.world = this;
            if (instance is IFeatureValidation instanceValidate) {

                if (instanceValidate.CouldBeAdded() == false) {
                    
                    instance.world = null;
                    return false;
                    
                }

            }
            
            this.features.Add(instance);
            this.statesFeatures.Add(ModuleState.AllActive);
            if (doConstruct == true) ((FeatureBase)instance).DoConstruct();

            return true;

        }

        /// <summary>
        /// Remove feature manually
        /// Pool will not be used, OnDeconstruct() call
        /// </summary>
        /// <param name="instance"></param>
        public void RemoveFeature(IFeatureBase instance) {

            if (this.isActive == false) return;
            
            var idx = this.features.IndexOf(instance);
            if (idx >= 0) {
                
                this.features.RemoveAt(idx);
                this.statesFeatures.RemoveAt(idx);
                ((FeatureBase)instance).DoDeconstruct();
                
            }
            
        }

        #region Systems
        /// <summary>
        /// Search TSystem in all groups
        /// </summary>
        /// <typeparam name="TSystem"></typeparam>
        /// <returns></returns>
        public bool HasSystem<TSystem>() where TSystem : class, ISystemBase, new() {

            for (int i = 0; i < this.systemGroupsLength; ++i) {

                if (this.systemGroups.arr[i].HasSystem<TSystem>() == true) {

                    return true;

                }
                
            }
            
            return false;

        }

        /// <summary>
        /// Get first system by type
        /// </summary>
        /// <typeparam name="TSystem"></typeparam>
        /// <returns></returns>
        public TSystem GetSystem<TSystem>() where TSystem : class, ISystemBase {

            for (int i = 0, count = this.systemGroupsLength; i < count; ++i) {

                var system = this.systemGroups.arr[i].GetSystem<TSystem>();
                if (system != null) return system;

            }

            return default;

        }
        #endregion
        
        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool IsModuleActive(int index) {

            var step = this.currentStep;
            if ((step & WorldStep.LogicTick) != 0) {

                return (this.statesModules[index] & ModuleState.LogicInactive) == 0;

            } else if ((step & WorldStep.VisualTick) != 0) {

                return (this.statesModules[index] & ModuleState.VisualInactive) == 0;

            }

            return false;

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void UpdatePhysics(float deltaTime) {
        
            for (int i = 0, count = this.modules.Count; i < count; ++i) {

                if (this.IsModuleActive(i) == true) {

                    if (this.modules[i] is IModulePhysicsUpdate module) {
                        
                        module.UpdatePhysics(deltaTime);
                        
                    }
                    
                }

            }
            
        }
        
        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void UpdateLogic(float deltaTime) {
            
            if (deltaTime < 0f) return;

            ////////////////
            // Update Logic Tick
            ////////////////
            #if CHECKPOINT_COLLECTOR
            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("Simulate", WorldStep.None);
            #endif

            #if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.BeginSample($"Simulate");
            #endif

            if (this.settings.maxTicksSimulationCount > 0L && this.simulationToTick > this.simulationFromTick + this.settings.maxTicksSimulationCount) {
                
                throw new System.Exception("Simulation failed because of ticks count is out of range: [" + this.simulationFromTick + ".." + this.simulationToTick + ")");
                
            }
            this.Simulate(this.simulationFromTick, this.simulationToTick);

            #if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
            #endif

            #if CHECKPOINT_COLLECTOR
            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("Simulate", WorldStep.None);
            #endif

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void UpdateVisualPre(float deltaTime) {
            
            if (deltaTime < 0f) return;

            ////////////////
            this.currentStep |= WorldStep.ModulesVisualTick;
            ////////////////
            {

                #if CHECKPOINT_COLLECTOR
                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.modules, WorldStep.VisualTick);
                #endif

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.BeginSample($"VisualTick-Pre [All Modules]");
                #endif

                for (int i = 0, count = this.modules.Count; i < count; ++i) {

                    if (this.IsModuleActive(i) == true) {
                        
                        #if CHECKPOINT_COLLECTOR
                        if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.modules[i], WorldStep.VisualTick);
                        #endif

                        #if UNITY_EDITOR
                        UnityEngine.Profiling.Profiler.BeginSample($"[{this.modules[i]}]");
                        #endif

                        if (this.modules[i] is IUpdate moduleBase) {

                            moduleBase.Update(deltaTime);
                            
                        }

                        #if UNITY_EDITOR
                        UnityEngine.Profiling.Profiler.EndSample();
                        #endif

                        #if CHECKPOINT_COLLECTOR
                        if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.modules[i], WorldStep.VisualTick);
                        #endif

                    }

                }

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.EndSample();
                #endif

                #if CHECKPOINT_COLLECTOR
                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.modules, WorldStep.VisualTick);
                #endif

            }
            ////////////////
            this.currentStep &= ~WorldStep.ModulesVisualTick;
            ////////////////

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void UpdateVisualPost(float deltaTime) {
            
            this.ProcessGlobalEvents(GlobalEventType.Visual);
            
            if (deltaTime < 0f) return;

            ////////////////
            this.currentStep |= WorldStep.SystemsVisualTick;
            ////////////////
            {

                #if CHECKPOINT_COLLECTOR
                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.systemGroups.arr, WorldStep.VisualTick);
                #endif

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.BeginSample($"VisualTick-Post [All Modules]");
                #endif

                for (int i = 0, count = this.systemGroupsLength; i < count; ++i) {

                    ref var group = ref this.systemGroups.arr[i];
                    if (group.runtimeSystem.systemUpdates == null) continue;
                    for (int j = 0; j < group.runtimeSystem.systemUpdates.Count; ++j) {

                        ref var system = ref group.runtimeSystem.systemUpdates[j];
                        
                        #if CHECKPOINT_COLLECTOR
                        if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(system, WorldStep.VisualTick);
                        #endif

                        #if UNITY_EDITOR
                        UnityEngine.Profiling.Profiler.BeginSample($"VisualTick [{system}]");
                        #endif

                        system.Update(deltaTime);

                        #if UNITY_EDITOR
                        UnityEngine.Profiling.Profiler.EndSample();
                        #endif

                        #if CHECKPOINT_COLLECTOR
                        if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(system, WorldStep.VisualTick);
                        #endif

                    }

                }

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.EndSample();
                #endif

                #if CHECKPOINT_COLLECTOR
                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.systemGroups.arr, WorldStep.VisualTick);
                #endif

            }
            ////////////////
            this.currentStep &= ~WorldStep.SystemsVisualTick;
            ////////////////
            
            #if CHECKPOINT_COLLECTOR
            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("RemoveMarkers", WorldStep.None);
            #endif

            #if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.BeginSample($"Remove Markers");
            #endif

            this.RemoveMarkers();
            
            #if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
            #endif

            #if CHECKPOINT_COLLECTOR
            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("RemoveMarkers", WorldStep.None);
            #endif

        }

        #if TICK_THREADED
        private struct UpdateJob : Unity.Jobs.IJobParallelFor {

            public float deltaTime;

            void Unity.Jobs.IJobParallelFor.Execute(int index) {

                Worlds.currentWorld.UpdateLogic(this.deltaTime);

            }

        }
        #endif
        
        public void PreUpdate(float deltaTime) {
            
            if (deltaTime < 0f) return;

            this.UpdateVisualPre(deltaTime);

        }

        public void Update(float deltaTime) {

            if (deltaTime < 0f) return;

            #if CHECKPOINT_COLLECTOR
            if (this.checkpointCollector != null) this.checkpointCollector.Reset();
            #endif

            // Setup current static variables
            WorldUtilities.SetWorld(this);

            // Update time
            this.timeSinceStart += deltaTime;
            if (this.timeSinceStart < 0d) this.timeSinceStart = 0d;

            #if TICK_THREADED
            var job = new UpdateJob() {
                deltaTime = deltaTime,
            };
            var jobHandle = job.Schedule(1, 64);
            jobHandle.Complete();
            #else
            this.UpdateLogic(deltaTime);
            #endif

        }

        public void LateUpdate(float deltaTime) {

            this.UpdateVisualPost(deltaTime);
            
        }

        public void SetFromToTicks(Tick from, Tick to) {

            //UnityEngine.Debug.Log("Set FromTo: " + from + " >> " + to);
            this.simulationFromTick = from;
            this.simulationToTick = to;

            if (this.settings.maxTicksSimulationCount > 0L && this.simulationToTick > this.simulationFromTick + this.settings.maxTicksSimulationCount) {
                
                throw new System.Exception("Simulation failed because of ticks count is out of range: [" + this.simulationFromTick + ".." + this.simulationToTick + ")");
                
            }

        }

        private struct ForeachFilterJob : Unity.Jobs.IJobParallelFor {

            [Unity.Collections.NativeDisableParallelForRestrictionAttribute]
            [Unity.Collections.ReadOnlyAttribute] public Unity.Collections.NativeSlice<Entity> slice;
            [Unity.Collections.NativeDisableParallelForRestrictionAttribute]
            [Unity.Collections.ReadOnlyAttribute] public Unity.Collections.NativeArray<bool> dataContains;
            public float deltaTime;

            void Unity.Jobs.IJobParallelFor.Execute(int index) {

                var entity = this.slice[index];
                if (entity.IsAlive() == false) return;
                
                if (this.dataContains[entity.id] == true) Worlds.currentWorld.currentSystemContextFilter.AdvanceTick(entity, in this.deltaTime);

            }

        }

        /*[Unity.Burst.BurstCompileAttribute]
        private unsafe struct ForeachFilterJobBurst : Unity.Jobs.IJobParallelFor {

            [Unity.Collections.LowLevel.Unsafe.NativeDisableUnsafePtrRestrictionAttribute]
            public void* bws;
            public Unity.Burst.FunctionPointer<SystemFilterAdvanceTick> function;
            public Unity.Collections.NativeArray<Entity> entities;
            public float deltaTime;

            void Unity.Jobs.IJobParallelFor.Execute(int index) {

                this.function.Invoke(this.entities[index], this.deltaTime, this.bws);
                
            }

        }

        public unsafe delegate void SystemFilterAdvanceTick(in Entity entity, in float deltaTime, void* burstWorldStructComponentsAccess);*/

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void PrepareAdvanceTickForSystem<T>(T system) where T : class, IContext {
            
            this.currentSystemContext = system;
            System.Array.Clear(this.currentSystemContextFiltersUsed.arr, 0, this.currentSystemContextFiltersUsed.Length);
            this.currentSystemContextFiltersUsedAnyChanged = false;
            
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void PostAdvanceTickForSystem() {
            
            this.currentState.storage.ApplyDead();

            this.currentSystemContext = null;
            this.currentSystemContextFilter = null;

            if (this.currentSystemContextFiltersUsedAnyChanged == true) {

                for (int f = 1, cnt = this.currentSystemContextFiltersUsed.Length; f < cnt; ++f) {

                    if (this.currentSystemContextFiltersUsed.arr[f] == true) {

                        var filter = this.GetFilter(f);
                        filter.ApplyAllRequests();

                    }

                }

            }

        }
        
        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Simulate(Tick from, Tick to) {
            
            #if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.BeginSample($"PlayTasksForFrame");
            #endif

            this.PlayTasksForFrame();

            #if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
            #endif

            if (from > to) {

                //UnityEngine.Debug.LogError( UnityEngine.Time.frameCount + " From: " + from + ", To: " + to);
                return;

            }

            if (from < Tick.Zero) from = Tick.Zero;

            var state = this.GetState();

            //UnityEngine.Debug.Log("Simulate " + from + " to " + to);
            this.cpf = to - from;
            var fixedDeltaTime = this.GetTickTime();
            for (state.tick = from; state.tick < to; ++state.tick) {

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.BeginSample(from.ToString());
                #endif

                ////////////////
                this.currentStep |= WorldStep.ModulesLogicTick;
                ////////////////
                {
                    
                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.modules, WorldStep.LogicTick);
                    #endif
                    
                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.BeginSample($"LogicTick [All Modules]");
                    #endif

                    for (int i = 0, count = this.modules.Count; i < count; ++i) {

                        if (this.IsModuleActive(i) == true) {

                            #if CHECKPOINT_COLLECTOR
                            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.modules[i], WorldStep.LogicTick);
                            #endif

                            var module = this.modules[i];
                            if (module is IAdvanceTickStep step && step.step % state.tick != 0) continue;
                            
                            if (module is IAdvanceTick moduleBase) {

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.BeginSample(moduleBase.ToString());
                                #endif

                                moduleBase.AdvanceTick(in fixedDeltaTime);

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.EndSample();
                                #endif

                            }
                            
                            #if CHECKPOINT_COLLECTOR
                            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.modules[i], WorldStep.LogicTick);
                            #endif
                            
                        }

                    }
                    
                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.EndSample();
                    #endif

                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.modules, WorldStep.LogicTick);
                    #endif

                }
                ////////////////
                this.currentStep &= ~WorldStep.ModulesLogicTick;
                ////////////////
                
                ////////////////
                this.currentStep |= WorldStep.PluginsLogicTick;
                ////////////////
                {
                    
                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("PlayPluginsForTick", WorldStep.None);
                    #endif

                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.BeginSample($"PlayPluginsForTick");
                    #endif

                    this.PlayPluginsForTick(state.tick);
                    
                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.EndSample();
                    #endif

                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("PlayPluginsForTick", WorldStep.None);
                    #endif

                }
                ////////////////
                this.currentStep &= ~WorldStep.PluginsLogicTick;
                ////////////////
                
                ////////////////
                this.currentStep |= WorldStep.SystemsLogicTick;
                ////////////////
                {
                    
                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.systemGroups.arr, WorldStep.LogicTick);
                    #endif

                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.BeginSample($"PlayTasksForTick");
                    #endif

                    this.PlayTasksForTick();
                    
                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.EndSample();
                    #endif

                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("AdvanceTickPre", WorldStep.LogicTick);
                    #endif

                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.BeginSample($"LogicTick [AdvanceTickPre]");
                    #endif

                    for (int i = 0, count = this.systemGroupsLength; i < count; ++i) {

                        ref var group = ref this.systemGroups.arr[i];
                        if (group.runtimeSystem.systemAdvanceTickPre == null) continue;
                        for (int j = 0; j < group.runtimeSystem.systemAdvanceTickPre.Count; ++j) {

                            ref var system = ref group.runtimeSystem.systemAdvanceTickPre[j];
                            if (system is IAdvanceTickStep step && step.step % state.tick != 0) continue;

                            #if CHECKPOINT_COLLECTOR
                            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(system, WorldStep.LogicTick);
                            #endif

                            #if UNITY_EDITOR
                            UnityEngine.Profiling.Profiler.BeginSample(system.ToString());
                            #endif

                            system.AdvanceTickPre(fixedDeltaTime);

                            #if UNITY_EDITOR
                            UnityEngine.Profiling.Profiler.EndSample();
                            #endif

                            #if CHECKPOINT_COLLECTOR
                            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(system, WorldStep.LogicTick);
                            #endif

                        }

                    }

                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.EndSample();
                    #endif

                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("AdvanceTickPre", WorldStep.LogicTick);
                    #endif

                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("AdvanceTickFilters", WorldStep.LogicTick);
                    #endif

                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.BeginSample($"LogicTick [AdvanceTickFilters]");
                    #endif

                    for (int i = 0, count = this.systemGroupsLength; i < count; ++i) {

                        ref var group = ref this.systemGroups.arr[i];
                        if (group.runtimeSystem.systemAdvanceTick == null) continue;
                        for (int j = 0; j < group.runtimeSystem.systemAdvanceTick.Count; ++j) {

                            ref var systemBase = ref group.runtimeSystem.systemAdvanceTick[j];
                            if (systemBase is IAdvanceTickStep step && step.step % state.tick != 0) continue;
                            
                            if (systemBase is ISystemFilter system) {

                                this.currentSystemContextFilter = system;
                                
                                #if CHECKPOINT_COLLECTOR
                                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(system, WorldStep.LogicTick);
                                #endif

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.BeginSample($"PrepareAdvanceTickForSystem");
                                #endif

                                this.PrepareAdvanceTickForSystem(system);

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.EndSample();
                                #endif

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.BeginSample(system.ToString());
                                #endif

                                {

                                    /*if (sysFilter is IAdvanceTickBurst advTick) {
    
                                        // Under the development process
                                        // This should not used right now
                                        
                                        var functionPointer = Unity.Burst.BurstCompiler.CompileFunctionPointer(advTick.GetAdvanceTickForBurst());
                                        var arrEntities = sysFilter.filter.GetArray();
                                        using (var arr = new Unity.Collections.NativeArray<Entity>(arrEntities.arr, Unity.Collections.Allocator.TempJob)) {
    
                                            var length = arrEntities.Length;
                                            var burstWorldStructComponentsAccess = new BurstWorldStructComponentsAccess();
                                            unsafe {
    
                                                var bws = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref burstWorldStructComponentsAccess);
                                                PoolArray<Entity>.Recycle(ref arrEntities);
                                                var job = new ForeachFilterJobBurst() {
                                                    deltaTime = fixedDeltaTime,
                                                    entities = arr,
                                                    function = functionPointer,
                                                    bws = bws
                                                };
                                                var jobHandle = job.Schedule(length, sysFilter.jobsBatchCount);
                                                jobHandle.Complete();
                                                
                                            }
    
                                        }
                                        
                                    }*/

                                    system.filter = (system.filter.IsAlive() == true ? system.filter : system.CreateFilter());
                                    if (system.filter.IsAlive() == true) {

                                        if (this.settings.useJobsForSystems == true && system.jobs == true) {

                                            var arrEntities = this.currentState.storage.cache.arr;
                                            system.filter.GetBounds(out var min, out var max);
                                            if (min > max) {

                                                min = 0;
                                                max = -1;

                                            }
                                            if (min < 0) min = 0;
                                            ++max;
                                            if (max >= arrEntities.Length) max = arrEntities.Length - 1;

                                            if (min < max) {

                                                var arr = new Unity.Collections.NativeArray<Entity>(arrEntities, Unity.Collections.Allocator.TempJob);
                                                var arrContains = new Unity.Collections.NativeArray<bool>(this.GetFilter(system.filter.id).dataContains.arr, Unity.Collections.Allocator.TempJob);
                                        
                                                var length = max - min;
                                                var job = new ForeachFilterJob() {
                                                    deltaTime = fixedDeltaTime,
                                                    slice = new Unity.Collections.NativeSlice<Entity>(arr, min, max - min),
                                                    dataContains = arrContains,
                                                };
                                                var jobHandle = job.Schedule(length, system.jobsBatchCount);
                                                jobHandle.Complete();

                                                arr.Dispose();
                                                arrContains.Dispose();

                                            }

                                        } else {

                                            foreach (var entity in system.filter) {

                                                system.AdvanceTick(in entity, fixedDeltaTime);

                                            }

                                        }

                                    }

                                }

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.EndSample();
                                #endif

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.BeginSample($"PostAdvanceTickForSystem");
                                #endif

                                this.PostAdvanceTickForSystem();

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.EndSample();
                                #endif

                                #if CHECKPOINT_COLLECTOR
                                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(system, WorldStep.LogicTick);
                                #endif
                                
                            } else if (systemBase is IAdvanceTick advanceTickSystem) {

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.BeginSample($"PrepareAdvanceTickForSystem");
                                #endif

                                this.PrepareAdvanceTickForSystem(advanceTickSystem);

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.EndSample();
                                #endif

                                #if CHECKPOINT_COLLECTOR
                                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(advanceTickSystem, WorldStep.LogicTick);
                                #endif

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.BeginSample($"AdvanceTick");
                                #endif

                                advanceTickSystem.AdvanceTick(fixedDeltaTime);

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.EndSample();
                                #endif

                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.BeginSample($"PostAdvanceTickForSystem");
                                #endif

                                this.PostAdvanceTickForSystem();
                            
                                #if UNITY_EDITOR
                                UnityEngine.Profiling.Profiler.EndSample();
                                #endif

                                #if CHECKPOINT_COLLECTOR
                                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(advanceTickSystem, WorldStep.LogicTick);
                                #endif
                            }
                            
                        }

                    }

                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.EndSample();
                    #endif
                    
                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("AdvanceTickFilters", WorldStep.LogicTick);
                    #endif
                    

                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("AdvanceTickPost", WorldStep.LogicTick);
                    #endif

                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.BeginSample($"LogicTick [AdvanceTickPost]");
                    #endif

                    for (int i = 0, count = this.systemGroupsLength; i < count; ++i) {

                        ref var group = ref this.systemGroups.arr[i];
                        if (group.runtimeSystem.systemAdvanceTickPost == null) continue;
                        for (int j = 0; j < group.runtimeSystem.systemAdvanceTickPost.Count; ++j) {

                            ref var system = ref group.runtimeSystem.systemAdvanceTickPost[j];
                            if (system is IAdvanceTickStep step && step.step % state.tick != 0) continue;

                            #if CHECKPOINT_COLLECTOR
                            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(system, WorldStep.LogicTick);
                            #endif

                            #if UNITY_EDITOR
                            UnityEngine.Profiling.Profiler.BeginSample(system.ToString());
                            #endif

                            system.AdvanceTickPost(fixedDeltaTime);

                            #if UNITY_EDITOR
                            UnityEngine.Profiling.Profiler.EndSample();
                            #endif

                            #if CHECKPOINT_COLLECTOR
                            if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(system, WorldStep.LogicTick);
                            #endif

                        }

                    }
                    
                    #if UNITY_EDITOR
                    UnityEngine.Profiling.Profiler.EndSample();
                    #endif

                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("AdvanceTickPost", WorldStep.LogicTick);
                    #endif

                    #if CHECKPOINT_COLLECTOR
                    if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint(this.systemGroups.arr, WorldStep.LogicTick);
                    #endif

                }
                ////////////////
                this.currentStep &= ~WorldStep.SystemsLogicTick;
                ////////////////

                /*#if CHECKPOINT_COLLECTOR
                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("RemoveComponentsOnce", WorldStep.None);
                #endif

                this.RemoveComponentsOnce<IComponentOnce>();

                #if CHECKPOINT_COLLECTOR
                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("RemoveComponentsOnce", WorldStep.None);
                #endif*/

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.BeginSample($"UseLifetimeStep NotifyAllSystemsBelow");
                #endif

                this.UseLifetimeStep(ComponentLifetime.NotifyAllSystemsBelow);

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.EndSample();
                #endif

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.BeginSample($"ProcessGlobalEvents [Logic]");
                #endif

                this.ProcessGlobalEvents(GlobalEventType.Logic);

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.EndSample();
                #endif

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.EndSample();
                #endif

            }
            
            ////////////////
            this.currentStep |= WorldStep.PluginsLogicSimulate;
            ////////////////
            {
                
                #if CHECKPOINT_COLLECTOR
                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("SimulatePluginsForTicks", WorldStep.None);
                #endif

                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.BeginSample($"SimulatePluginsForTicks");
                #endif

                this.SimulatePluginsForTicks(from, to);
                
                #if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.EndSample();
                #endif
                
                #if CHECKPOINT_COLLECTOR
                if (this.checkpointCollector != null) this.checkpointCollector.Checkpoint("SimulatePluginsForTicks", WorldStep.None);
                #endif

            }
            ////////////////
            this.currentStep &= ~WorldStep.PluginsLogicSimulate;
            ////////////////

            #if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.BeginSample($"UseLifetimeStep NotifyAllModulesBelow");
            #endif

            this.UseLifetimeStep(ComponentLifetime.NotifyAllModulesBelow);
            
            #if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
            #endif

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void PlayPluginsForTick(Tick tick) {
            
            this.PlayPlugin1ForTick(tick);
            this.PlayPlugin2ForTick(tick);
            this.PlayPlugin3ForTick(tick);
            this.PlayPlugin4ForTick(tick);
            this.PlayPlugin5ForTick(tick);
            this.PlayPlugin6ForTick(tick);
            this.PlayPlugin7ForTick(tick);
            this.PlayPlugin8ForTick(tick);
            this.PlayPlugin9ForTick(tick);
            
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void SimulatePluginsForTicks(Tick from, Tick to) {
            
            this.SimulatePlugin1ForTicks(from, to);
            this.SimulatePlugin2ForTicks(from, to);
            this.SimulatePlugin3ForTicks(from, to);
            this.SimulatePlugin4ForTicks(from, to);
            this.SimulatePlugin5ForTicks(from, to);
            this.SimulatePlugin6ForTicks(from, to);
            this.SimulatePlugin7ForTicks(from, to);
            this.SimulatePlugin8ForTicks(from, to);
            this.SimulatePlugin9ForTicks(from, to);
            
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void RegisterPluginsModuleForEntity() {
            
            this.RegisterPlugin1ModuleForEntity();
            this.RegisterPlugin2ModuleForEntity();
            this.RegisterPlugin3ModuleForEntity();
            this.RegisterPlugin4ModuleForEntity();
            this.RegisterPlugin5ModuleForEntity();
            this.RegisterPlugin6ModuleForEntity();
            this.RegisterPlugin7ModuleForEntity();
            this.RegisterPlugin8ModuleForEntity();
            this.RegisterPlugin9ModuleForEntity();

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DestroyEntityPlugins(in Entity entity) {
            
            this.DestroyEntityPlugin1(entity);
            this.DestroyEntityPlugin2(entity);
            this.DestroyEntityPlugin3(entity);
            this.DestroyEntityPlugin4(entity);
            this.DestroyEntityPlugin5(entity);
            this.DestroyEntityPlugin6(entity);
            this.DestroyEntityPlugin7(entity);
            this.DestroyEntityPlugin8(entity);
            this.DestroyEntityPlugin9(entity);

        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void BeginRestoreEntities() {
            
            this.BeginRestoreEntitiesPlugins();
            
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void BeginRestoreEntitiesPlugins() {
            
            this.BeginRestoreEntitiesPlugin1();
            this.BeginRestoreEntitiesPlugin2();
            this.BeginRestoreEntitiesPlugin3();
            this.BeginRestoreEntitiesPlugin4();
            this.BeginRestoreEntitiesPlugin5();
            this.BeginRestoreEntitiesPlugin6();
            this.BeginRestoreEntitiesPlugin7();
            this.BeginRestoreEntitiesPlugin8();
            this.BeginRestoreEntitiesPlugin9();

        }

        partial void BeginRestoreEntitiesPlugin1();
        partial void BeginRestoreEntitiesPlugin2();
        partial void BeginRestoreEntitiesPlugin3();
        partial void BeginRestoreEntitiesPlugin4();
        partial void BeginRestoreEntitiesPlugin5();
        partial void BeginRestoreEntitiesPlugin6();
        partial void BeginRestoreEntitiesPlugin7();
        partial void BeginRestoreEntitiesPlugin8();
        partial void BeginRestoreEntitiesPlugin9();

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void EndRestoreEntities() {
            
            this.EndRestoreEntitiesPlugins();
            
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void EndRestoreEntitiesPlugins() {
            
            this.EndRestoreEntitiesPlugin1();
            this.EndRestoreEntitiesPlugin2();
            this.EndRestoreEntitiesPlugin3();
            this.EndRestoreEntitiesPlugin4();
            this.EndRestoreEntitiesPlugin5();
            this.EndRestoreEntitiesPlugin6();
            this.EndRestoreEntitiesPlugin7();
            this.EndRestoreEntitiesPlugin8();
            this.EndRestoreEntitiesPlugin9();

        }

        partial void EndRestoreEntitiesPlugin1();
        partial void EndRestoreEntitiesPlugin2();
        partial void EndRestoreEntitiesPlugin3();
        partial void EndRestoreEntitiesPlugin4();
        partial void EndRestoreEntitiesPlugin5();
        partial void EndRestoreEntitiesPlugin6();
        partial void EndRestoreEntitiesPlugin7();
        partial void EndRestoreEntitiesPlugin8();
        partial void EndRestoreEntitiesPlugin9();

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void SetEntityCapacityPlugins(int capacity) {
            
            this.SetEntityCapacityPlugin1(capacity);
            this.SetEntityCapacityPlugin2(capacity);
            this.SetEntityCapacityPlugin3(capacity);
            this.SetEntityCapacityPlugin4(capacity);
            this.SetEntityCapacityPlugin5(capacity);
            this.SetEntityCapacityPlugin6(capacity);
            this.SetEntityCapacityPlugin7(capacity);
            this.SetEntityCapacityPlugin8(capacity);
            this.SetEntityCapacityPlugin9(capacity);

        }

        partial void SetEntityCapacityPlugin1(int capacity);
        partial void SetEntityCapacityPlugin2(int capacity);
        partial void SetEntityCapacityPlugin3(int capacity);
        partial void SetEntityCapacityPlugin4(int capacity);
        partial void SetEntityCapacityPlugin5(int capacity);
        partial void SetEntityCapacityPlugin6(int capacity);
        partial void SetEntityCapacityPlugin7(int capacity);
        partial void SetEntityCapacityPlugin8(int capacity);
        partial void SetEntityCapacityPlugin9(int capacity);

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void CreateEntityPlugins(in Entity entity) {
            
            this.CreateEntityPlugin1(entity);
            this.CreateEntityPlugin2(entity);
            this.CreateEntityPlugin3(entity);
            this.CreateEntityPlugin4(entity);
            this.CreateEntityPlugin5(entity);
            this.CreateEntityPlugin6(entity);
            this.CreateEntityPlugin7(entity);
            this.CreateEntityPlugin8(entity);
            this.CreateEntityPlugin9(entity);

        }

        partial void CreateEntityPlugin1(Entity entity);
        partial void CreateEntityPlugin2(Entity entity);
        partial void CreateEntityPlugin3(Entity entity);
        partial void CreateEntityPlugin4(Entity entity);
        partial void CreateEntityPlugin5(Entity entity);
        partial void CreateEntityPlugin6(Entity entity);
        partial void CreateEntityPlugin7(Entity entity);
        partial void CreateEntityPlugin8(Entity entity);
        partial void CreateEntityPlugin9(Entity entity);

        partial void DestroyEntityPlugin1(Entity entity);
        partial void DestroyEntityPlugin2(Entity entity);
        partial void DestroyEntityPlugin3(Entity entity);
        partial void DestroyEntityPlugin4(Entity entity);
        partial void DestroyEntityPlugin5(Entity entity);
        partial void DestroyEntityPlugin6(Entity entity);
        partial void DestroyEntityPlugin7(Entity entity);
        partial void DestroyEntityPlugin8(Entity entity);
        partial void DestroyEntityPlugin9(Entity entity);

        partial void RegisterPlugin1ModuleForEntity();
        partial void RegisterPlugin2ModuleForEntity();
        partial void RegisterPlugin3ModuleForEntity();
        partial void RegisterPlugin4ModuleForEntity();
        partial void RegisterPlugin5ModuleForEntity();
        partial void RegisterPlugin6ModuleForEntity();
        partial void RegisterPlugin7ModuleForEntity();
        partial void RegisterPlugin8ModuleForEntity();
        partial void RegisterPlugin9ModuleForEntity();
        
        partial void PlayPlugin1ForTick(Tick tick);
        partial void PlayPlugin2ForTick(Tick tick);
        partial void PlayPlugin3ForTick(Tick tick);
        partial void PlayPlugin4ForTick(Tick tick);
        partial void PlayPlugin5ForTick(Tick tick);
        partial void PlayPlugin6ForTick(Tick tick);
        partial void PlayPlugin7ForTick(Tick tick);
        partial void PlayPlugin8ForTick(Tick tick);
        partial void PlayPlugin9ForTick(Tick tick);

        partial void SimulatePlugin1ForTicks(Tick from, Tick to);
        partial void SimulatePlugin2ForTicks(Tick from, Tick to);
        partial void SimulatePlugin3ForTicks(Tick from, Tick to);
        partial void SimulatePlugin4ForTicks(Tick from, Tick to);
        partial void SimulatePlugin5ForTicks(Tick from, Tick to);
        partial void SimulatePlugin6ForTicks(Tick from, Tick to);
        partial void SimulatePlugin7ForTicks(Tick from, Tick to);
        partial void SimulatePlugin8ForTicks(Tick from, Tick to);
        partial void SimulatePlugin9ForTicks(Tick from, Tick to);

    }

}