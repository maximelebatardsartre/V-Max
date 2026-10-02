using Panuon.WPF.UI;
using System;
using System.Windows;

namespace VPet_Simulator.Core
{
    /// <summary>
    /// V-Max : point d'entrée unique des boîtes de dialogue et des notifications.
    /// L'application branche ses propres fenêtres (verre fumé, notifications HUD) via <see cref="Handler"/> et
    /// <see cref="NoticeHandler"/> ; sans elles, on retombe sur Panuon. Les plugins peuvent l'utiliser aussi.
    /// </summary>
    public static class VDialog
    {
        /// <summary>Affiche une boîte de dialogue (fenêtre propriétaire, texte, titre, boutons, icône) et renvoie le choix</summary>
        public static Func<Window?, string, string, MessageBoxButton, MessageBoxIcon, MessageBoxResult>? Handler { get; set; }

        /// <summary>Affiche une notification non bloquante (texte, titre, icône)</summary>
        public static Action<string, string, MessageBoxIcon>? NoticeHandler { get; set; }

        /// <summary>Dernière erreur du gestionnaire V-Max (diagnostic)</summary>
        public static Exception? LastError { get; private set; }

        public static MessageBoxResult Show(string text) => Show(null, text, "", MessageBoxButton.OK, MessageBoxIcon.None);
        public static MessageBoxResult Show(string text, string caption) => Show(null, text, caption, MessageBoxButton.OK, MessageBoxIcon.None);
        public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons) => Show(null, text, caption, buttons, MessageBoxIcon.None);
        public static MessageBoxResult Show(string text, string caption, MessageBoxIcon icon) => Show(null, text, caption, MessageBoxButton.OK, icon);
        public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxIcon icon) => Show(null, text, caption, buttons, icon);
        public static MessageBoxResult Show(Window? owner, string text, string caption) => Show(owner, text, caption, MessageBoxButton.OK, MessageBoxIcon.None);

        public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxIcon icon)
        {
            if (Handler != null)
            {
                try
                {
                    return Handler(owner, text, caption, buttons, icon);
                }
                catch (Exception e)
                {
                    // l'interface V-Max n'est pas disponible (arrêt en cours…) : repli
                    LastError = e;
                }
            }
            return owner != null
                ? MessageBoxX.Show(owner, text, caption, buttons, icon)
                : MessageBoxX.Show(text, caption, buttons, icon);
        }

        /// <summary>Notification non bloquante (remplace NoticeBox)</summary>
        public static void Notice(string text, string caption = "", MessageBoxIcon icon = MessageBoxIcon.Info, bool autoClose = true, int durationMs = 4000)
        {
            if (NoticeHandler != null)
            {
                try
                {
                    NoticeHandler(text, caption, icon);
                    return;
                }
                catch { }
            }
            NoticeBox.Show(text, caption, icon, autoClose, durationMs);
        }
    }
}
