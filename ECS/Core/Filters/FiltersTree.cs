﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ME.ECS {

    #if ECS_COMPILE_IL2CPP_OPTIONS
    [Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.NullChecks, false),
     Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false),
     Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.DivideByZeroChecks, false)]
    #endif
    public struct FiltersTree {

        #if ECS_COMPILE_IL2CPP_OPTIONS
        [Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.NullChecks, false),
         Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false),
         Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.DivideByZeroChecks, false)]
        #endif
        private struct Item {

            public bool isCreated;
            public int bit;
            public int index;
            public ME.ECS.Collections.BufferArray<int> filters;

            public void Dispose() {
                
                PoolArray<int>.Recycle(ref this.filters);
                
            }

            [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            public void Add(FilterData filterData) {

                ArrayUtils.Resize(this.index, ref this.filters, resizeWithOffset: true);
                this.filters.arr[this.index] = filterData.id;
                ++this.index;
                
            }

        }

        private ME.ECS.Collections.BufferArray<Item> itemsContains;
        private ME.ECS.Collections.BufferArray<Item> itemsNotContains;

        public void Dispose() {

            for (int i = 0; i < this.itemsContains.Length; ++i) {

                this.itemsContains.arr[i].Dispose();

            }
            PoolArray<Item>.Recycle(ref this.itemsContains);

            for (int i = 0; i < this.itemsNotContains.Length; ++i) {

                this.itemsNotContains.arr[i].Dispose();

            }
            PoolArray<Item>.Recycle(ref this.itemsNotContains);

        }
        
        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public ME.ECS.Collections.BufferArray<int> GetFiltersContainsFor<T>() {
            
            var idx = WorldUtilities.GetComponentTypeId<T>();
            if (idx >= 0 && idx < this.itemsContains.Length) {

                return this.itemsContains.arr[idx].filters;

            }
            
            return new ME.ECS.Collections.BufferArray<int>(null, 0);
            
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public ME.ECS.Collections.BufferArray<int> GetFiltersNotContainsFor<T>() {
            
            var idx = WorldUtilities.GetComponentTypeId<T>();
            if (idx >= 0 && idx < this.itemsNotContains.Length) {

                return this.itemsNotContains.arr[idx].filters;

            }
            
            return new ME.ECS.Collections.BufferArray<int>(null, 0);
            
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Add(FilterData filter) {

            {
                var bits = filter.archetypeContains.value.BitsCount;
                for (int i = 0; i <= bits; ++i) {

                    if (filter.archetypeContains.value.HasBit(i) == true) {

                        ArrayUtils.Resize(i, ref this.itemsContains, resizeWithOffset: true);
                        ref var item = ref this.itemsContains.arr[i];
                        if (item.isCreated == false) {
                            item = new Item() {
                                isCreated = true,
                                bit = i,
                                index = 0,
                            };
                        }

                        item.Add(filter);

                    }

                }
            }

            {
                var bits = filter.archetypeNotContains.value.BitsCount;
                for (int i = 0; i <= bits; ++i) {

                    if (filter.archetypeNotContains.value.HasBit(i) == true) {

                        ArrayUtils.Resize(i, ref this.itemsNotContains, resizeWithOffset: true);
                        ref var item = ref this.itemsNotContains.arr[i];
                        if (item.isCreated == false) {
                            item = new Item() {
                                isCreated = true,
                                bit = i,
                                index = 0,
                            };
                        }
                        
                        item.Add(filter);

                    }

                }
            }

        }

    }

}