using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LevelNameTranslator
{
    /// <summary>
    /// 在编辑器里以协程方式跑异步任务，不阻塞主线程。
    /// 用 EditorApplication.update 驱动，所以全程都在主线程上，
    /// 可以安全地访问 UnityEngine.Object。
    /// </summary>
    internal static class EditorCoroutineHost
    {
        private sealed class RoutineState
        {
            public readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();

            /// <summary>还要等几帧（对应 yield return 秒数 / WaitForSeconds）。</summary>
            public int WaitingFrames;

            /// <summary>正在等待的 AsyncOperation（UnityWebRequest 等）。</summary>
            public AsyncOperation PendingAsync;

            /// <summary>正在等待的自定义 yield 指令。</summary>
            public CustomYieldInstruction PendingCustom;
        }

        private static readonly List<RoutineState> Routines = new List<RoutineState>();
        private static bool hooked;

        public static void Start(IEnumerator routine)
        {
            if (routine == null)
            {
                return;
            }

            var state = new RoutineState();
            state.Stack.Push(routine);
            Routines.Add(state);
            EnsureHooked();
        }

        public static bool IsBusy
        {
            get { return Routines.Count > 0; }
        }

        private static void EnsureHooked()
        {
            if (hooked)
            {
                return;
            }

            hooked = true;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            for (int i = Routines.Count - 1; i >= 0; i--)
            {
                bool finished;
                try
                {
                    finished = Step(Routines[i]);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    finished = true;
                }

                if (finished)
                {
                    Routines.RemoveAt(i);
                }
            }

            if (Routines.Count == 0 && hooked)
            {
                EditorApplication.update -= Tick;
                hooked = false;
            }
        }

        /// <summary>推进一帧。返回 true 表示整个协程结束。</summary>
        private static bool Step(RoutineState state)
        {
            if (state.WaitingFrames > 0)
            {
                state.WaitingFrames--;
                return false;
            }

            // 先把上一帧挂着的等待处理完，再推进协程
            if (state.PendingAsync != null)
            {
                if (!state.PendingAsync.isDone)
                {
                    return false;
                }

                state.PendingAsync = null;
            }

            if (state.PendingCustom != null)
            {
                if (state.PendingCustom.keepWaiting)
                {
                    return false;
                }

                state.PendingCustom = null;
            }

            while (state.Stack.Count > 0)
            {
                IEnumerator top = state.Stack.Peek();

                bool moved;
                try
                {
                    moved = top.MoveNext();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    state.Stack.Pop();
                    continue;
                }

                if (!moved)
                {
                    state.Stack.Pop();
                    continue;
                }

                object current = top.Current;

                if (current is IEnumerator nested)
                {
                    state.Stack.Push(nested);
                    continue;
                }

                // UnityWebRequest.SendWebRequest() 返回的是 AsyncOperation。
                // 必须等它真正完成，否则下一帧读到的是 InProgress，会被误判成失败。
                if (current is AsyncOperation asyncOperation)
                {
                    if (!asyncOperation.isDone)
                    {
                        state.PendingAsync = asyncOperation;
                    }

                    return false;
                }

                if (current is CustomYieldInstruction customYield)
                {
                    if (customYield.keepWaiting)
                    {
                        state.PendingCustom = customYield;
                    }

                    return false;
                }

                if (current is float seconds)
                {
                    state.WaitingFrames = Mathf.Max(1, Mathf.RoundToInt(seconds * 60f));
                    return false;
                }

                return false;
            }

            return true;
        }
    }
}
