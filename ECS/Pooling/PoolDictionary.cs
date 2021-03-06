﻿using System.Collections.Generic;

namespace ME.ECS {

	#if ECS_COMPILE_IL2CPP_OPTIONS
	[Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.NullChecks, false),
	 Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false),
	 Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.DivideByZeroChecks, false)]
	#endif
	public static class PoolDictionary<TKey, TValue> {

		private static IEqualityComparer<TKey> customComparer;
		private static int capacity;
		private static PoolInternalBase pool = new PoolInternalBase(typeof(Dictionary<TKey, TValue>), () => new Dictionary<TKey, TValue>(PoolDictionary<TKey, TValue>.capacity, PoolDictionary<TKey, TValue>.customComparer), (x) => ((Dictionary<TKey, TValue>)x).Clear());

		public static Dictionary<TKey, TValue> Spawn(int capacity, IEqualityComparer<TKey> customComparer = null) {

			PoolDictionary<TKey, TValue>.capacity = capacity;
			PoolDictionary<TKey, TValue>.customComparer = customComparer;
			return (Dictionary<TKey, TValue>)PoolDictionary<TKey, TValue>.pool.Spawn();
		    
		}

		public static void Recycle(ref Dictionary<TKey, TValue> dic) {

			PoolDictionary<TKey, TValue>.pool.Recycle(dic);
			dic = null;

		}

		public static void Recycle(Dictionary<TKey, TValue> dic) {

			PoolDictionary<TKey, TValue>.pool.Recycle(dic);

		}

	}

}
