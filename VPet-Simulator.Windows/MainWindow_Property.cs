using LinePutScript.Dictionary;
using LinePutScript.Localization.WPF;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Windows.Interface.ScheduleTask;

namespace VPet_Simulator.Windows;

public partial class MainWindow
{//主窗口部分数据


    /// <summary>
    /// 版本号
    /// </summary>
    public int version { get; } = 11073;
    /// <summary>
    /// 版本号
    /// </summary>
    public string Version => $"{version / 10000}.{version % 10000 / 100}.{version % 100:00}";
    /// <summary>
    /// V-Max: Steam retiré. Conservé pour compatibilité des plugins (toujours 0).
    /// </summary>
    public ulong SteamID => 0;
    /// <summary>
    /// V-Max: Steam retiré. Conservé pour compatibilité des plugins (toujours 0).
    /// </summary>
    public uint SteamAuthorID => 0;
    public List<LowText> LowFoodText { get; set; } = new List<LowText>();

    public List<LowText> LowDrinkText { get; set; } = new List<LowText>();

    public List<SelectText> SelectTexts { get; set; } = new List<SelectText>();

    public List<ClickText> ClickTexts { get; set; } = new List<ClickText>();
    /// <summary>
    /// 所有食物
    /// </summary>
    public List<Food> Foods { get; } = new List<Food>();
    public List<Photo> Photos { get; } = new List<Photo>();

    public GameSave_v2 GameSavesData { get; set; } = new GameSave_v2("VPET");

    public static readonly string ModPath = ExtensionValue.BaseDirectory + @"\mod";
    /// <summary>
    /// V-Max: Steam retiré. Conservé pour compatibilité des plugins (toujours false).
    /// </summary>
    public bool IsSteamUser => false;
    public LPS_D Args { get; }
    public string PrefixSave { get; } = "";
    private string? prefixsavetrans = null;
    public string PrefixSaveTrans
    {
        get
        {
            if (prefixsavetrans == null)
            {
                if (PrefixSave == "")
                    prefixsavetrans = "";
                else
                    prefixsavetrans = '-' + PrefixSave.TrimStart('-').Translate();
            }
            return prefixsavetrans;
        }
    }
    internal Setting Set { get; set; } = null!;
    ISetting IMainWindow.Set => Set;

    public List<PetLoader> Pets { get; set; } = new List<PetLoader>();
    internal List<CoreMOD> CoreMODs = new List<CoreMOD>();
    public GameCore Core { get; set; } = new GameCore();
    public List<Window> Windows { get; set; } = new List<Window>();
    public Main Main { get; set; } = null!;
    public UIElement? TalkBox;
    public winGameSetting? winSetting { get; set; }
    /// <summary>
    /// V-Max : nouvelle fenêtre de paramètres (créée à la demande)
    /// </summary>
    public winVMaxSettings? winVMaxSetting { get; set; }
    /// <summary>
    /// V-Max : plugin intégré de l'agent IA
    /// </summary>
    public Agent.VMaxAgentPlugin? AgentPlugin { get; private set; }
    /// <summary>
    /// V-Max HUD : interface autour du compagnon
    /// </summary>
    public HUD.HudController? Hud { get; private set; }
    /// <summary>
    /// V-Max habitat : mode autonome dans un décor personnalisé
    /// </summary>
    public Habitat.HabitatMode? Habitat { get; private set; }
    public winBetterBuy? winBetterBuy { get; set; }
    public winGallery? winGallery { get; set; } 
    public winInventory? winInventory { get; set; }

    public winWorkMenu? winWorkMenu { get; set; }
    //public ChatGPTClient CGPTClient;
    public ImageResources ImageSources { get; set; } = new ImageResources();
    public Resources FileSources { get; set; } = new Resources();
    /// <summary>
    /// 动态资源, 用于给插件MOD存储共享的数据
    /// </summary>
    public Dictionary<string, object> DynamicResources { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// 所有三方插件
    /// </summary>
    public List<MainPlugin> Plugins { get; } = new List<MainPlugin>();
    /// <summary>
    /// 所有字体(位置)
    /// </summary>
    public List<IFont> Fonts { get; } = new List<IFont>();
    /// <summary>
    /// 所有主题
    /// </summary>
    public List<Theme> Themes = new List<Theme>();
    /// <summary>
    /// 当前启用主题
    /// </summary>
    public Theme? Theme = null;

    /// <summary>
    /// 日程表
    /// </summary>
    public ScheduleTask ScheduleTask { get; set; } = null!;

    /// <summary>
    /// 物品栏 桌宠有的物品
    /// </summary>
    public List<Item> Items { get; set; } = new List<Item>();



    /// <summary>
    /// 所有可用套餐
    /// </summary>
    public List<PackageFull> SchedulePackage { get; set; } = new List<PackageFull>();

    /// <summary>
    /// 活动日志 不会保存
    /// </summary>
    public ObservableCollection<ActivityLog> ActivityLogs { get; set; } = new BoundedObservableCollection<ActivityLog>(2000);// V-Max : journal borné
}
