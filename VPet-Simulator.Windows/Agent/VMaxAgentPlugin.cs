using LinePutScript;
using System;
using System.Threading.Tasks;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.Agent;

/// <summary>
/// V-Max : plugin intégré qui fournit l'agent IA comme module de discussion (API TalkBox de VPet).
/// Il n'est pas chargé depuis une DLL : l'application l'ajoute elle-même à la liste des plugins.
/// </summary>
public sealed class VMaxAgentPlugin : MainPlugin
{
    public const string TalkName = "V-Max (Gemini)";

    public override string PluginName => "V-Max Agent";

    public AgentOrchestrator Orchestrator { get; }
    public AgentTalkBox? Box { get; private set; }

    public VMaxAgentPlugin(MainWindow mw) : base(mw)
    {
        Orchestrator = new AgentOrchestrator(mw);
    }

    public override void LoadPlugin()
    {
        Box = new AgentTalkBox(this);
        MW.TalkAPI.Add(Box);
    }

    public override void Setting() => ((MainWindow)MW).ShowSetting("ia");

    /// <summary>
    /// Active ou désactive l'agent comme module de discussion
    /// </summary>
    public static void Activate(MainWindow mw, bool enable)
    {
        mw.RemoveTalkBox();
        if (enable)
        {
            mw.Set["CGPT"][(gstr)"type"] = "DIY";
            mw.Set["CGPT"][(gstr)"DIY"] = TalkName;
            mw.TalkAPIIndex = mw.TalkAPI.FindIndex(x => x.APIName == TalkName);
            mw.LoadTalkDIY();
        }
        else
        {
            mw.Set["CGPT"][(gstr)"type"] = "LB";
            mw.TalkBox = new TalkSelect(mw);
            mw.Main.ToolBar!.MainGrid.Children.Add(mw.TalkBox);
        }
    }

    public static bool IsActive(MainWindow mw) =>
        mw.Set["CGPT"][(gstr)"type"] == "DIY" && mw.Set["CGPT"][(gstr)"DIY"] == TalkName;
}

/// <summary>
/// Boîte de discussion de l'agent (zone de saisie de la barre du compagnon)
/// </summary>
public sealed class AgentTalkBox : TalkBox
{
    private readonly VMaxAgentPlugin plugin;

    public AgentTalkBox(VMaxAgentPlugin plugin) : base(plugin)
    {
        this.plugin = plugin;
    }

    public override string APIName => VMaxAgentPlugin.TalkName;

    public override void Responded(string text)
    {
        _ = Task.Run(() => plugin.Orchestrator.RespondAsync(text));
    }

    public override void Setting() => plugin.Setting();
}
