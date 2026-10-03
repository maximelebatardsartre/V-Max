using System;
using VPet_Simulator.Windows.HUD;

namespace VPet_Simulator.Windows.Assistant;

/// <summary>
/// Branche le routeur de commandes sur le vrai compagnon : bulle (Main.Say), voix (Piper), et petite carte « mode OS »
/// (Toast). Utilisé pour les réponses immédiates ET différées (minuteurs, rappels).
/// </summary>
internal sealed class AssistantHost : IAssistantHost
{
    private readonly MainWindow mw;
    public AssistantHost(MainWindow mw) => this.mw = mw;

    public void OnUi(Action action)
    {
        try { mw.Dispatcher.Invoke(action); } catch { }
    }

    public void OpenPanel(string id) => OnUi(() => { try { mw.Hud?.OpenPanel(id); } catch { } });

    public void Deliver(CommandOutcome outcome)
    {
        OnUi(() =>
        {
            try { mw.Main.Say(outcome.Speak); } catch { }
            try { mw.Voice?.Say(outcome.Speak); } catch { }
            if (!string.IsNullOrEmpty(outcome.Card))
                try { mw.Toast(outcome.Card, Map(outcome.Tone)); } catch { }
        });
    }

    private static HudToast.Kind Map(CommandTone t) => t switch
    {
        CommandTone.Success => HudToast.Kind.Success,
        CommandTone.Warning => HudToast.Kind.Warning,
        _ => HudToast.Kind.Info,
    };
}
