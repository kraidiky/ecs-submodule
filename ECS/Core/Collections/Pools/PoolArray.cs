﻿namespace ME.ECS {

	using Collections;

	#if ECS_COMPILE_IL2CPP_OPTIONS
	[Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
	[Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
	[Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.DivideByZeroChecks, false)]
	#endif
	public static class PoolArrayUtilities {

		[System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public static int GetArrayLengthPot(int length) {
			
			var bucketIndex = 0;
			while (1 << bucketIndex < length && bucketIndex < 30) {
				++bucketIndex;
			}

			return 1 << bucketIndex;

		}

	}
	
    #if ECS_COMPILE_IL2CPP_OPTIONS
    [Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.DivideByZeroChecks, false)]
    #endif
    public static class PoolArray<T> {

		/// <summary>
		/// Maximum length of an array pooled using ClaimWithExactLength.
		/// Arrays with lengths longer than this will silently not be pooled.
		/// </summary>
		private const int MaximumExactArrayLength = 256;
		private const int MAX_STACK_SIZE = 31;

		/// <summary>
		/// Internal pool.
		/// The arrays in each bucket have lengths of 2^i
		/// </summary>
		#if MULTITHREAD_SUPPORT
		private static readonly CCStack<T[]>[] pool = new CCStack<T[]>[PoolArray<T>.MAX_STACK_SIZE];
		#else
		private static readonly System.Collections.Generic.Stack<T[]>[] pool = new System.Collections.Generic.Stack<T[]>[PoolArray<T>.MAX_STACK_SIZE];
		#endif
		//private static readonly System.Collections.Generic.Stack<T[]>[] pool = new System.Collections.Generic.Stack<T[]>[PoolArray<T>.MAX_STACK_SIZE];
		private static readonly System.Collections.Generic.Stack<T[]>[] exactPool = new System.Collections.Generic.Stack<T[]>[PoolArray<T>.MaximumExactArrayLength+1];

		private static readonly System.Collections.Generic.HashSet<T[]> outArrays = new System.Collections.Generic.HashSet<T[]>();
		
		private static readonly T[] empty = new T[0];
		
		public static void Initialize() {

#if MULTITHREAD_SUPPORT
			lock (PoolArray<T>.pool) {
#endif

				if (PoolArray<T>.pool[0] == null) {

					for (int i = 0; i < PoolArray<T>.MAX_STACK_SIZE; ++i) {

						var bucketIndex = i;
						#if MULTITHREAD_SUPPORT
						PoolArray<T>.pool[bucketIndex] = new CCStack<T[]>(usePool: true);
						#else
						PoolArray<T>.pool[bucketIndex] = new System.Collections.Generic.Stack<T[]>();
						#endif

					}

				}

#if MULTITHREAD_SUPPORT
			}
#endif
			
		}
		
		/// <summary>
		/// Returns an array with at least the specified length.
		/// Warning: Returned arrays may contain arbitrary data.
		/// You cannot rely on it being zeroed out.
		/// </summary>
		[System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		internal static T[] Claim(int minimumLength) {

			//return new T[minimumLength];
			
			if (minimumLength <= 0) {
				return PoolArray<T>.empty; //PoolArray<T>.ClaimWithExactLength(0);
			}

			var bucketIndex = 0;
			while (1 << bucketIndex < minimumLength && bucketIndex < 30) {
				++bucketIndex;
			}

			if (bucketIndex == 30) {
				throw new System.ArgumentException("Too high minimum length");
			}

			if (PoolArray<T>.pool[0] == null) PoolArray<T>.Initialize();
			
			#if MULTITHREAD_SUPPORT
			if (PoolArray<T>.pool[bucketIndex].TryPop(out var result) == true) {

				return result;

			}
			#else
			var pool = PoolArray<T>.pool[bucketIndex];
			if (pool.Count > 0) {

				var arrPooled = pool.Pop();
				#if UNITY_EDITOR
				if (PoolArray<T>.outArrays.Contains(arrPooled) == true) {
					
					UnityEngine.Debug.LogError("You are trying to pool array that has been already in pool");
					
				}
				PoolArray<T>.outArrays.Add(arrPooled);
				#endif
				
				//UnityEngine.Debug.Log("Spawn array: " + arrPooled + " :: " + arrPooled.GetHashCode());
				return arrPooled;

			}
			#endif
			
			/*lock (PoolArray<T>.pool) {
				
				if (PoolArray<T>.pool[bucketIndex] == null) {
					PoolArray<T>.pool[bucketIndex] = new System.Collections.Generic.Stack<T[]>();
				}

				if (PoolArray<T>.pool[bucketIndex].Count > 0) {
					var array= PoolArray<T>.pool[bucketIndex].Pop();
					outArrays.Add(array);
					return array;
				}

			}
			
			var arr = new T[1 << bucketIndex];
			outArrays.Add(arr);
			return arr;*/
			var arr = new T[1 << bucketIndex];
			#if UNITY_EDITOR
			PoolArray<T>.outArrays.Add(arr);
			#endif
			//UnityEngine.Debug.Log("Spawn new array: " + arr + " :: " + arr.GetHashCode());
			return arr;
		}

		/// <summary>
		/// Returns an array with the specified length.
		/// Use with caution as pooling too many arrays with different lengths that
		/// are rarely being reused will lead to an effective memory leak.
		///
		/// Use <see cref="Claim"/> if you just need an array that is at least as large as some value.
		///
		/// Warning: Returned arrays may contain arbitrary data.
		/// You cannot rely on it being zeroed out.
		/// </summary>
		[System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		internal static T[] ClaimWithExactLength(int length) {
			var isPowerOfTwo = length != 0 && (length & (length - 1)) == 0;
			if (isPowerOfTwo) {
				// Will return the correct array length
				return PoolArray<T>.Claim(length);
			}

			if (length <= PoolArray<T>.MaximumExactArrayLength) {
				var stack = PoolArray<T>.exactPool[length];
				return stack.Pop();
				/*lock (PoolArray<T>.pool) {
					var stack = PoolArray<T>.exactPool[length];
					if (stack != null && stack.Count > 0) {
						var array = stack.Pop();
						return array;
					}
				}*/
			}
			return new T[length];
		}

		/// <summary>
		/// Pool an array.
		/// If the array was got using the <see cref="ClaimWithExactLength"/> method then the allowNonPowerOfTwo parameter must be set to true.
		/// The parameter exists to make sure that non power of two arrays are not pooled unintentionally which could lead to memory leaks.
		/// </summary>
		[System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		internal static void Release(ref T[] array, bool allowNonPowerOfTwo = false) {

			//array = null;
			//return;
			
			if (array == null || array.Length == 0) {
				return;
			}

			/*if (array.GetType() != typeof(T[])) {
				throw new System.ArgumentException("Expected array type " + typeof(T[]).Name + " but found " + array.GetType().Name + "\nAre you using the correct generic class?\n");
			}*/

			var isPowerOfTwo = array.Length != 0 && (array.Length & (array.Length - 1)) == 0;
			if (!isPowerOfTwo && !allowNonPowerOfTwo && array.Length != 0) {
				
				throw new System.ArgumentException("Length is not a power of 2");
				//array = null;
				//return;

			}

			if (isPowerOfTwo == true) {
				
				var bucketIndex = 0;
				while (1 << bucketIndex < array.Length && bucketIndex < 30) {
					bucketIndex++;
				}

				if (PoolArray<T>.pool[bucketIndex] == null) return;

				#if UNITY_EDITOR
				if (PoolArray<T>.outArrays.Contains(array) == false) {

					if (PoolArray<T>.pool[bucketIndex].Contains(array) == true) {

						UnityEngine.Debug.LogError("You are trying to push array that already in pool!");

					} else {
						
						UnityEngine.Debug.LogWarning("You are trying to push array was created without pool!");
						
					}

				}
				PoolArray<T>.outArrays.Remove(array);
				#endif

				PoolArray<T>.pool[bucketIndex].Push(array);
				
			} else if (array.Length <= PoolArray<T>.MaximumExactArrayLength) {

				#if MULTITHREAD_SUPPORT
				lock (PoolArray<T>.pool) {
				#endif

					var stack = PoolArray<T>.exactPool[array.Length];
					if (stack == null) {
						stack = PoolArray<T>.exactPool[array.Length] = new System.Collections.Generic.Stack<T[]>();
					}

					#if UNITY_EDITOR
					if (PoolArray<T>.outArrays.Contains(array) == false) {

						if (stack.Contains(array) == true) {

							UnityEngine.Debug.LogError("You are trying to push array that already in pool!");

						}

					}
					PoolArray<T>.outArrays.Remove(array);
					#endif

					//UnityEngine.Debug.Log("Recycle array " + array + " :: " + array.GetHashCode());
					stack.Push(array);

				#if MULTITHREAD_SUPPORT
				}
				#endif

			}
			
			/*
			lock (PoolArray<T>.pool) {
				if (isPowerOfTwo) {
					var bucketIndex = 0;
					while (1 << bucketIndex < array.Length && bucketIndex < 30) {
						bucketIndex++;
					}

					if (PoolArray<T>.pool[bucketIndex] == null) {
						PoolArray<T>.pool[bucketIndex] = new System.Collections.Generic.Stack<T[]>();
					}

					if (outArrays.Contains(array) == false) {
						UnityEngine.Debug.LogError("You are trying to push array that already in pool!");
					}
					PoolArray<T>.pool[bucketIndex].Push(array);
					outArrays.Remove(array);
				} else if (array.Length <= PoolArray<T>.MaximumExactArrayLength) {
					var stack = PoolArray<T>.exactPool[array.Length];
					if (stack == null) {
						stack = PoolArray<T>.exactPool[array.Length] = new System.Collections.Generic.Stack<T[]>();
					}

					stack.Push(array);
				}
			}*/
			array = null;
		}
		
		[System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static BufferArray<T> Spawn(int length) {

	        //return new BufferArray<T>(new T[length], length);
	        
	        //UnityEngine.Debug.Log("Spawn request: " + length);
	        var buffer = new BufferArray<T>(PoolArray<T>.Claim(length), length);
            System.Array.Clear(buffer.arr, 0, length);

            return buffer;
            
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static void Recycle(ref BufferArray<T> buffer) {

	        //buffer = new BufferArray<T>(null, 0);
	        //return;
	        
	        T[] arr = buffer.arr;
	        if (arr != null) System.Array.Clear(arr, 0, arr.Length);
	        PoolArray<T>.Release(ref arr);
	        buffer = new BufferArray<T>(null, 0);
	        
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static void Recycle(BufferArray<T> buffer) {

	        //buffer = new BufferArray<T>(null, 0);
	        //return;
	        
	        T[] arr = buffer.arr;
	        if (arr != null) System.Array.Clear(arr, 0, arr.Length);
	        PoolArray<T>.Release(ref arr);
	        
        }

        [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static void Recycle(ref T[] buffer) {

	        buffer = null;

        }

    }

}
