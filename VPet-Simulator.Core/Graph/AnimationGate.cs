using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VPet_Simulator.Core
{
    /// <summary>
    /// V-Max : interrupteur global des animations.
    /// Quand au moins une raison de pause est active (session verrouillée, écran éteint, application plein écran,
    /// compagnon masqué…), les boucles d'animation attendent sans consommer de CPU, puis reprennent là où elles étaient.
    /// La logique de jeu (minuteur de 15 s) n'est pas concernée.
    /// </summary>
    public static class AnimationGate
    {
        private static readonly object Lock = new();
        private static readonly HashSet<string> Reasons = new();
        private static TaskCompletionSource? resume;

        /// <summary>
        /// Les animations sont-elles en pause ?
        /// </summary>
        public static bool IsPaused
        {
            get { lock (Lock) return Reasons.Count > 0; }
        }

        /// <summary>
        /// Raisons de pause actives (diagnostic)
        /// </summary>
        public static string[] ActiveReasons
        {
            get { lock (Lock) return [.. Reasons]; }
        }

        /// <summary>
        /// Déclenché quand l'état de pause change (true = en pause)
        /// </summary>
        public static event Action<bool>? PausedChanged;

        /// <summary>
        /// Active ou désactive une raison de pause
        /// </summary>
        public static void Set(string reason, bool paused)
        {
            bool changed;
            bool nowPaused;
            TaskCompletionSource? toRelease = null;
            lock (Lock)
            {
                bool before = Reasons.Count > 0;
                if (paused)
                    Reasons.Add(reason);
                else
                    Reasons.Remove(reason);
                nowPaused = Reasons.Count > 0;
                changed = before != nowPaused;
                if (nowPaused)
                    resume ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                else
                {
                    toRelease = resume;
                    resume = null;
                }
            }
            toRelease?.TrySetResult();
            if (changed)
                PausedChanged?.Invoke(nowPaused);
        }

        /// <summary>
        /// Tâche terminée immédiatement si les animations ne sont pas en pause, sinon à la reprise
        /// </summary>
        public static Task WaitAsync()
        {
            lock (Lock)
                return resume?.Task ?? Task.CompletedTask;
        }
    }
}
