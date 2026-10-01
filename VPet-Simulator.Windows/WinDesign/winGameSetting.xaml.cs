using LinePutScript;
using LinePutScript.Dictionary;
using LinePutScript.Localization.WPF;
using NAudio.Midi;
using NAudio.SoundFont;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Windows.Win32;

namespace VPet_Simulator.Windows
{
    /// <summary>
    /// winGameSetting.xaml 的交互逻辑
    /// </summary>
    public partial class winGameSetting : WindowX
    {
        MainWindow mw;
        private bool AllowChange = false;

        public winGameSetting(MainWindow mw)
        {
            this.mw = mw;
            //Console.WriteLine(DateTime.Now.ToString("mm:ss.fff"));
            InitializeComponent();
            //Console.WriteLine(DateTime.Now.ToString("mm:ss.fff"));
            //var bit = new BitmapImage(new Uri("pack://application:,,,/Res/TopLogo2019.png"));
            //Console.WriteLine(DateTime.Now.ToString("mm:ss.fff"));
            ////ImageWHY.Source = bit;
            //Console.WriteLine(DateTime.Now.ToString("mm:ss.fff"));

            mod = mw.CoreMODs[0];

            Title = "设置".Translate() + ' ' + mw.PrefixSave;
            SettingMenuWidth.Width = new GridLength(LocalizeCore.GetDouble("SettingMenuWidth", 150));
            TopMostBox.IsChecked = mw.Set.TopMost;
            if (mw.Set.IsBiggerScreen)
            {
                FullScreenBox.IsChecked = true;
                ZoomSlider.Maximum = 8;
            }
            else
            {
                FullScreenBox.IsChecked = false;
                ZoomSlider.Maximum = 3;
            }
            ZoomSlider.Value = mw.Set.ZoomLevel * 2;
            //this.Width = 400 * Math.Sqrt(ZoomSlider.Value);
            //this.Height = 450 * Math.Sqrt(ZoomSlider.Value);

            CalFunctionBox.IsChecked = mw.Set.EnableFunction;
            CalSlider.Value = mw.Set.LogicInterval;
            InteractionSlider.Value = mw.Set.InteractionCycle;
            MoveEventBox.IsChecked = mw.Set.AllowMove;
            SmartMoveEventBox.IsChecked = mw.Set.SmartMove;
            AutoChangeWindowEvent.IsChecked = mw.Set.AutoChangeWindow;
            PressLengthSlider.Value = mw.Set.PressLength / 1000.0;
            SwitchMsgOut.IsChecked = mw.Set.MessageBarOutside;
            SwitchHideFromTaskControl.IsChecked = mw.Set.HideFromTaskControl;
            ConsoleBox.IsChecked = mw.Set.DeBug;

            StartUpBox.IsChecked = mw.Set.StartUPBoot;
            TextBoxPetName.Text = mw.Core.Save!.Name;
            foreach (PetLoader pl in mw.Pets)
            {
                PetBox.Items.Add(pl.Name.Translate());
            }
            int petboxid = mw.Pets.FindIndex(x => x.Name == mw.Set.PetGraph);
            if (petboxid == -1)
                petboxid = 0;
            PetBox.SelectedIndex = petboxid;
            petboxbef = petboxid;
            PetIntor.Text = mw.Pets[petboxid].Intor.Translate();

            TextBoxStartUpX.Text = mw.Set.StartRecordPoint.X.ToString();
            TextBoxStartUpY.Text = mw.Set.StartRecordPoint.Y.ToString();
            numBackupSaveMaxNum.Value = mw.Set.BackupSaveMaxNum;
            combCalFunState.SelectedIndex = (int)mw.Set.CalFunState;
            combCalFunState.IsEnabled = !mw.Set.EnableFunction;

            TextBoxHostBDay.MaxDateTime = DateTime.Now;
            TextBoxHostBDay.SelectedDateTime = mw.GameSavesData.GetDateTime("HostBDay", mw.GameSavesData[(gdat)"birthday"]);
            TextBoxHostName.Text = mw.GameSavesData.GameSave.HostName;

            CalTimeInteraction();

            swAutoCal.IsChecked = !mw.Set["gameconfig"].GetBool("noAutoCal");

            LoadMutiUI();

            LanguageBox.Items.Add("null");
            foreach (string v in LocalizeCore.AvailableCultures)
            {
                LanguageBox.Items.Add(v);
            }
            LanguageBox.SelectedItem = LocalizeCore.CurrentCulture;

            HitThroughBox.IsChecked = mw.Set.HitThrough;
            PetHelperBox.IsChecked = mw.Set.PetHelper;

            if (mw.Set.StartRecordLast == true)
            {
                StartPlace.IsChecked = true;
                TextBoxStartUpX.IsEnabled = false;
                TextBoxStartUpY.IsEnabled = false;
                BtnStartUpGet.IsEnabled = false;
            }
            else
            {
                StartPlace.IsChecked = false;
                TextBoxStartUpX.IsEnabled = true;
                TextBoxStartUpY.IsEnabled = true;
                BtnStartUpGet.IsEnabled = true;
            }

            foreach (ComboBoxItem v in CBAutoSave.Items)
            {
                if ((int)v.Tag == mw.Set.AutoSaveInterval)
                {
                    CBAutoSave.SelectedItem = v;
                    break;
                }
            }
            foreach (ComboBoxItem v in CBSmartMove.Items)
            {
                if ((int)v.Tag == mw.Set.SmartMoveInterval)
                {
                    CBSmartMove.SelectedItem = v;
                    break;
                }
            }

            FontBox.Items.Add("Police système (Segoe UI)".Translate());
            foreach (var v in mw.Fonts)
            {
                FontBox.Items.Add(v.TranslateName);
            }
            FontBox.SelectedIndex = mw.Set.Font == SystemTheme.SystemFont ? 0 : mw.Fonts.FindIndex(x => x.Name == mw.Set.Font) + 1;

            ThemeBox.Items.Add("Suivre Windows (clair / sombre)".Translate());
            foreach (var v in mw.Themes)
            {
                ThemeBox.Items.Add(v.TranslateName);
            }
            if (mw.ThemeFollowsSystem)
                ThemeBox.SelectedIndex = 0;
            else if (mw.Theme != null)
                ThemeBox.SelectedItem = mw.Theme.TranslateName;

            VoiceCatchSilder.Value = mw.Set.MusicCatch;
            VoiceMaxSilder.Value = mw.Set.MusicMax;

            foreach (Sub sub in mw.Set["diy"])
                StackDIY.Children.Add(new DIYViewer(sub));

            SliderResolution.Maximum = Math.Min(System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Width,
                System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height);
            SliderResolution.Value = mw.Set.Resolution;

            SwitchOpacityHitThrough.IsChecked = mw.Set.OpacityHitThrough;
            SwitchOpacity.IsChecked = mw.Set.OpacityMain;
            OpacitySlider.Value = mw.Set.Opacity;

#if X64
            GameVerison.Content = "游戏版本".Translate() + $"v{mw.Version} x64";
#else
            GameVerison.Content = "游戏版本".Translate() + $"v{mw.Version} x86";
#endif
            //关于ui
            runUserName.Text = Environment.UserName;
            //CGPT
            if (mw.TalkAPI.Count > 0)
            {
                foreach (var v in mw.TalkAPI)
                    cbChatAPISelect.Items.Add(v.APIName.Translate());
                if (mw.TalkAPIIndex != -1)
                    cbChatAPISelect.SelectedIndex = mw.TalkAPIIndex;
            }
            else
            {
                cbChatAPISelect.Items.Add("暂无聊天API, 您可以通过订阅MOD添加".Translate());
                cbChatAPISelect.SelectedIndex = 0;
                cbChatAPISelect.IsEnabled = false;
            }
            switch (mw.Set["CGPT"][(gstr)"type"])
            {
                //case "API":
                //    RBCGPTUseAPI.IsChecked = true;
                //    BtnCGPTReSet.Content = "打开 ChatGPT API 设置".Translate();
                //    break;
                case "DIY":
                    RBCGPTDIY.IsChecked = true;
                    BtnCGPTReSet.Content = "打开 {0} 设置".Translate(mw.TalkBoxCurr?.APIName ?? "API");
                    break;
                case "LB":
                    RBCGPTUseLB.IsChecked = true;
                    BtnCGPTReSet.Content = "初始化桌宠聊天程序".Translate();
                    //if (!mf.IsSteamUser)
                    //    BtnCGPTReSet.IsEnabled = false;
                    break;
                case "OFF":
                default:
                    RBCGPTClose.IsChecked = true;
                    BtnCGPTReSet.Content = "聊天框已关闭".Translate();
                    break;
            }
            runabVer.Text = $"v{mw.Version} ({mw.version})";

            //mod列表
            ShowModList();

            voicetimer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromSeconds(0.2)
            };
            voicetimer.Tick += Voicetimer_Tick;

            //为侧边添加目录           
            ListMenuItems.Add(listmenuswith("置于顶层", 0, TopMostBox));
            ListMenuItems.Add(listmenuswith("开机启动", 0, StartUpBox));
            ListMenuItems.Add(listmenuswith("宠物动画", 0, PetBox));
            ListMenuItems.Add(listmenuswith("隐藏窗口", 0, SwitchHideFromTaskControl));

            ListMenuItems.Add(listmenuswith("自动保存频率", 1, CBAutoSave));
            ListMenuItems.Add(listmenuswith("从备份中还原", 1, numBackupSaveMaxNum));
            ListMenuItems.Add(listmenuswith("聊天设置", 1, RBCGPTUseLB));
            ListMenuItems.Add(listmenuswith("游戏操作", 1, btn_cleancache));
            ListMenuItems.Add(listmenuswith("桌宠多开", 1, btn_mutidel));

            ListMenuItems.Add(listmenuswith("互动设置", 2, CalFunctionBox));
            ListMenuItems.Add(listmenuswith("计算间隔", 2, CalSlider));
            ListMenuItems.Add(listmenuswith("桌宠移动", 2, MoveEventBox));
            ListMenuItems.Add(listmenuswith("操作设置", 2, PressLengthSlider));
            ListMenuItems.Add(listmenuswith("桌宠名字", 2, TextBoxPetName));
            ListMenuItems.Add(listmenuswith("音乐识别设置", 2, VoiceMaxSilder));

            ListMenuItems.Add(listmenuswith("自定义链接", 3, btn_DIY));

            ListMenuItems.Add(listmenuswith("自动超模MOD优化", 4, swAutoCal));
            ListMenuItems.Add(listmenuswith("当前存档Hash验证信息", 4, RHashCheckBlock));

            ListMenuItems.Add(listmenuswith("MOD管理", 5, ButtonOpenModFolder));

            ListMenuItems.Add(listmenuswith("关于", 6, ImageWHY));

            foreach (var v in ListMenuItems)
                ListMenu.Items.Add(v);

            AllowChange = true;

            UpdateMoveAreaText();

            ToolTipService.SetInitialShowDelay(runMODGameVerInfo, 0);

        }

        public List<ListBoxItem> ListMenuItems = new List<ListBoxItem>();
        private void tb_seach_menu_textchange(object sender, TextChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            ListMenu.Items.Clear();
            if (string.IsNullOrEmpty(tb_seach_menu.Text))
            {
                foreach (var v in ListMenuItems)
                    ListMenu.Items.Add(v);
                return;
            }
            foreach (var v in ListMenuItems)
            {
                if (((string)v.Content).Contains(tb_seach_menu.Text))
                    ListMenu.Items.Add(v);
            }
        }

        private ListBoxItem listmenuswith(string content, int page, FrameworkElement element)
        {
            var lbi = new ListBoxItem() { Content = content.Translate() };
            lbi.PreviewMouseLeftButtonDown += (_, _) =>
            {
                if (page >= 0 && page <= 6)
                    MainTab.SelectedIndex = page;
                if (page == 2)
                {
                    voicetimer.Start();
                }
                Task.Run(() =>
                {
                    Thread.Sleep(100);
                    Dispatcher.Invoke(element.BringIntoView);
                });

            };
            return lbi;
        }
        private void Voicetimer_Tick(object? sender, EventArgs? e)
        {
            var v = mw.AudioPlayingVolume();
            RVoice.Text = v.ToString("p2");
            if (v > mw.Set.MusicCatch)
            {
                RVoice.Foreground = new SolidColorBrush(Colors.Green);
                if (v > mw.Set.MusicMax)
                {
                    RVoice.FontWeight = FontWeights.Bold;
                }
                else
                    RVoice.FontWeight = FontWeights.Normal;
            }
            else
                RVoice.Foreground = Function.ResourcesBrush(Function.BrushType.PrimaryText);
        }

        private class ModInfo
        {
            public CoreMOD? CoreMod { get; }
            public int LoadOrder { get; }
            public string Name { get; }
            public string Author { get; }
            public long AuthorID { get; }
            public ulong ItemID { get; }
            public string Intro { get; }
            public DirectoryInfo Path { get; }
            public int GameVer { get; }
            public int Ver { get; }
            public HashSet<string> Tag { get; }

            public bool IsLoaded => CoreMod != null;
            public bool IsPlugin => Tag.Contains("plugin");
            public bool HasTrustedCertificate { get; }

            private ModInfo(CoreMOD? coreMod, int loadOrder, string name, string author, long authorID, ulong itemID, string intro, DirectoryInfo path, int gameVer, int ver, HashSet<string> tag)
            {
                CoreMod = coreMod;
                LoadOrder = loadOrder;
                Name = name;
                Author = author;
                AuthorID = authorID;
                ItemID = itemID;
                Intro = intro;
                Path = path;
                GameVer = gameVer;
                Ver = ver;
                Tag = tag;
                HasTrustedCertificate = !IsPlugin || PluginTrustStore.IsModApproved(name, path);
            }




            public bool MatchesSearch(string? searchText)
            {
                if (string.IsNullOrWhiteSpace(searchText))
                    return true;

                var terms = searchText.Split(new[] { ' ', '\t', '\\', '/', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (terms.Length == 0)
                    return true;

                var tagTranslateText = string.Join(" ", Tag.Select(x => x.Translate()));
                var searchPool = string.Join("\n", new[]
                {
                    Name,
                    Name.Translate(),
                    Intro,
                    Intro.Translate(),
                    Author,
                    tagTranslateText
                });

                return terms.Any(term => searchPool.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            public static ModInfo FromCoreMod(CoreMOD mod, int loadOrder)
            {
                return new ModInfo(mod, loadOrder, mod.Name, mod.Author, mod.AuthorID, mod.ItemID, mod.Intro,
                    mod.Path, mod.GameVer, mod.Ver, new HashSet<string>(mod.Tag));
            }

            public static ModInfo FromDirectory(DirectoryInfo directory)
            {
                string name = directory.Name;
                string author = string.Empty;
                long authorID = 0;
                ulong itemID = 0;
                string intro = string.Empty;
                int gameVer = 0;
                int ver = 0;
                HashSet<string> tag = new HashSet<string>();

                foreach (var di in directory.EnumerateDirectories())
                    tag.Add(di.Name.ToLowerInvariant());

                string infoFile = System.IO.Path.Combine(directory.FullName, "info.lps");
                if (File.Exists(infoFile))
                {
                    try
                    {
                        var modlps = new LpsDocument(File.ReadAllText(infoFile));
                        name = modlps.FindLine("vupmod")?.Info ?? name;
                        intro = modlps.FindLine("intro")?.Info ?? string.Empty;
                        gameVer = modlps.FindSub("gamever")?.InfoToInt ?? 0;
                        ver = modlps.FindSub("ver")?.InfoToInt ?? 0;
                        author = modlps.FindSub("author")?.Info.Split('[').FirstOrDefault() ?? string.Empty;
                        authorID = modlps.FindLine("authorid")?.InfoToInt64 ?? 0;
                        var itemStr = modlps.FindLine("itemid")?.info;
                        if (!string.IsNullOrWhiteSpace(itemStr))
                            ulong.TryParse(itemStr, out itemID);
                    }
                    catch
                    {
                    }
                }

                return new ModInfo(null, int.MaxValue, name, author, authorID, itemID, intro, directory, gameVer, ver, tag);
            }
        }

        private readonly List<ModInfo> modInfos = new List<ModInfo>();
        private ModInfo? selectedModInfo;
        CoreMOD? mod;

        private IEnumerable<DirectoryInfo> GetModDirectories()
        {
            if (Directory.Exists(MainWindow.ModPath))
            {
                foreach (var di in new DirectoryInfo(MainWindow.ModPath).EnumerateDirectories())
                    yield return di;
            }
        }

        private void RefreshModInfos()
        {
            modInfos.Clear();
            var modInfoByPath = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);


            for (int i = 0; i < mw.CoreMODs.Count; i++)
            {
                CoreMOD core = mw.CoreMODs[i];
                modInfoByPath[core.Path.FullName] = ModInfo.FromCoreMod(core, i);
            }

            foreach (var di in GetModDirectories())
            {
                if (!File.Exists(Path.Combine(di.FullName, "info.lps")))
                    continue;
                if (!modInfoByPath.ContainsKey(di.FullName))
                    modInfoByPath[di.FullName] = ModInfo.FromDirectory(di);
            }

            modInfos.AddRange(modInfoByPath.Values
                .OrderByDescending(x => mw.Set.IsOnMod(x.Name))
                .ThenByDescending(x => mw.Set.IsOnMod(x.Name) && x.IsLoaded)
                .ThenBy(x => mw.Set.IsOnMod(x.Name) && x.IsLoaded ? x.LoadOrder : int.MaxValue)
                .ThenBy(x => !x.Path.FullName.Contains("DLC"))
                .ThenBy(x => !x.Author.Contains("LorisYounger"))
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase));
        }

        private void RenderModList(string? selectedPath, string? searchText)
        {
            RefreshModInfos();
            ListMod.Items.Clear();

            var visibleMods = modInfos.Where(x => x.MatchesSearch(searchText)).ToList();
            foreach (ModInfo info in visibleMods)
            {
                ListBoxItem moditem = (ListBoxItem)ListMod.Items[ListMod.Items.Add(new ListBoxItem())];
                moditem.Padding = new Thickness(5, 0, 5, 0);
                moditem.Content = info.Name;
                moditem.Tag = info;
                bool isOnMod = mw.Set.IsOnMod(info.Name);
                if (!isOnMod)
                {
                    moditem.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                }
                else
                {
                    if (info.GameVer / 1000 == mw.version / 1000)
                    {
                        moditem.Foreground = Function.ResourcesBrush(Function.BrushType.PrimaryText);
                    }
                    else if (info.IsPlugin)
                    {
                        moditem.Foreground = new SolidColorBrush(Color.FromRgb(190, 0, 0));
                    }
                }
            }

            if (ListMod.Items.Count == 0)
            {
                selectedModInfo = null;
                mod = null;
                return;
            }

            int selectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                for (int i = 0; i < ListMod.Items.Count; i++)
                {
                    if (((ListBoxItem)ListMod.Items[i]).Tag is ModInfo info && info.Path.FullName.Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }
            ListMod.SelectedIndex = selectedIndex;
            if (ListMod.SelectedItem is ListBoxItem item && item.Tag is ModInfo modInfo)
                ShowMod(modInfo);
        }


        public void ShowModList()
        {
            RenderModList(selectedModInfo?.Path.FullName, tb_seach_mod.Text);
        }

        public void ShowMod(string modname)
        {
            var modInfo = modInfos.FirstOrDefault(x => x.Name == modname);
            if (modInfo != null)
                ShowMod(modInfo);
        }

        private void ShowMod(ModInfo modInfo)
        {
            selectedModInfo = modInfo;
            mod = modInfo.CoreMod;

            LabelModName.Content = modInfo.Name.Translate();
            runMODAuthor.Text = modInfo.Author;
            runMODGameVer.Text = CoreMOD.INTtoVER(modInfo.GameVer);
            runMODGameVer.Foreground = Function.ResourcesBrush(Function.BrushType.PrimaryText);
            if (ImageMOD.Source is BitmapImage bitmapImage)
            {
                bitmapImage.StreamSource?.Dispose();
            }
            if (File.Exists(modInfo.Path.FullName + @"\icon.png"))
            {
                bitmapImage = new();
                bitmapImage.BeginInit();
                try
                {
                    var bytes = File.ReadAllBytes(modInfo.Path.FullName + @"\icon.png");
                    bitmapImage.StreamSource = new MemoryStream(bytes);
                    bitmapImage.DecodePixelWidth = 250;
                }
                finally
                {
                    bitmapImage.EndInit();
                }
                ImageMOD.Source = bitmapImage;
            }
            else
                ImageMOD.Source = ImageResources.NewSafeBitmapImage(@"pack://application:,,,/Res/TopLogo2019.PNG");
            runMODGameVerInfo.Visibility = Visibility.Collapsed;
            if (modInfo.GameVer / 100 != mw.version / 100)
                if (modInfo.GameVer < mw.version)
                {
                    if (modInfo.GameVer / 1000 == mw.version / 1000)
                    {
                        runMODGameVer.Text += " (兼容)".Translate();
                    }
                    else
                    {
                        runMODGameVer.Text += " (版本低)".Translate();
                        runMODGameVerInfo.Visibility = Visibility.Visible;
                        if (modInfo.IsPlugin)
                        {
                            runMODGameVer.Foreground = new SolidColorBrush(Color.FromRgb(190, 0, 0));
                            runMODGameVerInfo.ToolTip = "MOD对应游戏版本比当前游戏版本低, 因为包含代码插件, 可能有严重的兼容性问题.\n请联系MOD作者更新MOD".Translate()
                                + $"\nv{CoreMOD.INTtoVER(modInfo.GameVer)} < v{mw.Version}";
                        }
                        else
                            runMODGameVerInfo.ToolTip = "MOD对应游戏版本比当前游戏版本低, 但是游戏的兼容功能可能会生效, MOD可能可以正常使用.\n为确保最佳体验,请联系MOD作者更新MOD".Translate()
                               + $"\nv{CoreMOD.INTtoVER(modInfo.GameVer)} < v{mw.Version}";
                    }
                }
                else if (modInfo.GameVer > mw.version)
                {
                    if (modInfo.GameVer / 1000 == mw.version / 1000)
                    {
                        runMODGameVer.Text += " (兼容)".Translate();
                        runMODGameVer.Foreground = Function.ResourcesBrush(Function.BrushType.PrimaryText);
                    }
                    else
                    {
                        runMODGameVer.Text += " (版本高)".Translate();
                        runMODGameVerInfo.Visibility = Visibility.Visible;
                        runMODGameVer.Foreground = new SolidColorBrush(Color.FromRgb(190, 0, 0));
                        runMODGameVerInfo.ToolTip = "MOD对应游戏版本比当前游戏版本高, 可能会有兼容性问题.\n请更新游戏".Translate()
                            + $"\nv{CoreMOD.INTtoVER(modInfo.GameVer)} > v{mw.Version}";
                    }
                }
            bool isOnMod = mw.Set.IsOnMod(modInfo.Name);
            if (!isOnMod)
            {
                LabelModName.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                ButtonEnable.Foreground = Function.ResourcesBrush(Function.BrushType.DARKPrimaryDarker);
                ButtonDisEnable.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                ButtonEnable.IsEnabled = true;
                ButtonDisEnable.IsEnabled = false;
            }
            else
            {
                LabelModName.Foreground = runMODGameVer.Foreground;
                ButtonDisEnable.Foreground = Function.ResourcesBrush(Function.BrushType.DARKPrimaryDarker);
                ButtonEnable.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                ButtonEnable.IsEnabled = false;
                ButtonDisEnable.IsEnabled = true;
            }
            runMODVer.Text = CoreMOD.INTtoVER(modInfo.Ver);
            GameInfo.Text = modInfo.Intro.Translate();
            string content = "";
            foreach (string tag in modInfo.Tag)
            {
                content += tag.Translate() + "\n";
            }
            GameHave.Text = content;
            ButtonAllow.Visibility = (modInfo.IsPlugin && !PluginTrustStore.IsModApproved(modInfo.Name, modInfo.Path)) ? Visibility.Visible : Visibility.Collapsed;

            if (mod != null)
            {
                foreach (var mainplug in mw.Plugins)
                {
                    try
                    {
                        if (mainplug.PluginName == mod.Name &&
                            mainplug.GetType().GetMethod("Setting")!.DeclaringType != typeof(MainPlugin)
                        && mainplug.GetType().Assembly.Location.Contains(mod.Path.FullName))
                        {
                            ButtonSetting.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                    finally { }
                }
            }
            ButtonSetting.Visibility = Visibility.Collapsed;
        }





        private void FullScreenBox_Check(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (FullScreenBox.IsChecked == true)
            {
                mw.Set.IsBiggerScreen = true;
                ZoomSlider.Maximum = 8;
            }
            else
            {
                mw.Set.IsBiggerScreen = false;
                ZoomSlider.Maximum = 3;
            }
        }

        private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (ThemeBox.SelectedIndex <= 0)
            {
                mw.LoadTheme(SystemTheme.FollowSystem);
                mw.Set.Theme = SystemTheme.FollowSystem;
                return;
            }
            mw.LoadTheme(mw.Themes[ThemeBox.SelectedIndex - 1].xName);
            mw.Set.Theme = mw.Theme?.xName ?? string.Empty;
        }

        private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            string str = FontBox.SelectedIndex <= 0 ? SystemTheme.SystemFont : mw.Fonts[FontBox.SelectedIndex - 1].Name;
            mw.LoadFont(str);
            mw.Set.Font = str;
        }

        private void CBAutoSave_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetAutoSaveInterval((int)((ComboBoxItem)CBAutoSave.SelectedItem).Tag);
        }





        private void ListMod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange || ListMod.SelectedItem is not ListBoxItem item || item.Tag is not ModInfo modInfo)
                return;

            ShowMod(modInfo);
        }

        private void ButtonOpenModFolder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (selectedModInfo == null)
                return;
            var psi = new ProcessStartInfo
            {
                FileName = selectedModInfo.Path.FullName,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        private void ButtonEnable_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ModInfo? modInfo = selectedModInfo;
            if (modInfo == null)
                return;

            mw.Set.OnMod(modInfo.Name);
            ButtonRestart.Visibility = Visibility.Visible;
            ShowModList();
        }

        private void ButtonDisEnable_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (selectedModInfo == null)
                return;
            if (selectedModInfo.Name == "Core")
            {
                MessageBoxX.Show("模组 Core 为<虚拟桌宠模拟器>核心文件,无法停用".Translate(), "停用失败".Translate());
                return;
            }
            else if (CoreMOD.OnModDefList.Contains(selectedModInfo.Name))
                return;
            mw.Set.OnModRemove(selectedModInfo.Name);
            ButtonRestart.Visibility = Visibility.Visible;
            ShowModList();
        }





        private void ButtonAllow_Click(object sender, RoutedEventArgs e)
        {
            ModInfo? modInfo = selectedModInfo;
            if (modInfo == null)
                return;

            // V-Max : avertissement de sécurité volontairement NON traduisible par les mods (anti-usurpation, AUDIT S-05)
            var dlls = string.Join("\n", PluginTrustStore.PluginDlls(modInfo.Path).Select(d => "  • " + d.Name + "  (SHA-256 " + PluginTrustStore.ComputeHash(d.FullName)[..16] + "…)"));
            if (MessageBoxX.Show($"Autoriser le code du mod « {modInfo.Name} » ?\n\n"
                + "Un plugin de code s'exécute avec tous vos droits : il peut lire vos fichiers, accéder au réseau et lancer des programmes.\n"
                + "N'autorisez que des mods de confiance. L'autorisation est liée à la version exacte des fichiers ci-dessous ; "
                + "toute modification demandera une nouvelle autorisation.\n\n" + dlls,
                "Autoriser un plugin de code", MessageBoxButton.YesNo, MessageBoxIcon.Warning) == MessageBoxResult.Yes)
            {
                PluginTrustStore.ApproveMod(modInfo.Name, modInfo.Path);
                ShowMod(modInfo);
                ButtonRestart.Visibility = Visibility.Visible;
            }
        }

        private void ButtonRestart_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBoxX.Show("是否退出游戏<虚拟桌宠模拟器>?\n请注意保存游戏".Translate(), "重启游戏".Translate(), MessageBoxButton.YesNo, MessageBoxIcon.Warning) == MessageBoxResult.Yes)
            {
                mw.Restart();
            }
        }

        public void UpdateMoveAreaText()
        {
            var mwCtrl = mw.Core.Controller as MWController;
            if (mwCtrl!.IsPrimaryScreen && !mwCtrl.AutoChangeWindow)
            {
                textMoveArea.Text = "主屏幕".Translate();
                return;
            }
            else if (mwCtrl.AutoChangeWindow)
            {
                textMoveArea.Text = "自动选择窗口".Translate();
                return;
            }
            var rect = mwCtrl.ScreenBorder;
            textMoveArea.Text = $"X:{rect.X};Y:{rect.Y};W:{rect.Width};H:{rect.Height}";
        }

        private void BtnSetMoveArea_Default_Click(object sender, RoutedEventArgs e)
        {
            var mwCtrl = mw.Core.Controller as MWController;
            mwCtrl!.ResetScreenBorder();
            UpdateMoveAreaText();
        }

        private void BtnSetMoveArea_DetectScreen_Click(object sender, RoutedEventArgs e)
        {
            var mwCtrl = mw.Core.Controller as MWController;
            mwCtrl!.SetNowScreenActivate();
            UpdateMoveAreaText();
        }

        internal static System.Reflection.FieldInfo? leftGetter, topGetter;
        private void BtnSetMoveArea_Window_Click(object sender, RoutedEventArgs e)
        {
            var wma = new winMoveArea(mw);
            var mwCtrl = mw.Core.Controller as MWController;
            if (!mwCtrl!.IsPrimaryScreen)
            {
                var rect = mwCtrl.ScreenBorder;
                wma.Width = rect.Width;
                wma.Height = rect.Height;
                wma.Left = rect.X;
                wma.Top = rect.Y;
            }
            wma.ShowDialog();
        }


        public new void Show()
        {
            if (MainTab.SelectedIndex == 2)
            {
                voicetimer.Start();
            }
            base.Show();
        }

        private void WindowX_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            mw.Topmost = mw.Set.TopMost;
            e.Cancel = mw.CloseConfirm;
            voicetimer.Stop();
            Hide();
        }

        private void TopMostBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.TopMost = true;
            (mw.notifyIcon.ContextMenuStrip!.Items.Find("NotifyIcon_TopMost", false).First() as System.Windows.Forms.ToolStripMenuItem)!.Checked = true;
        }

        private void TopMostBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.TopMost = false;
            (mw.notifyIcon.ContextMenuStrip!.Items.Find("NotifyIcon_TopMost", false).First() as System.Windows.Forms.ToolStripMenuItem)!.Checked = false;
        }

        private void ZoomSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.SetZoomLevel(ZoomSlider.Value / 2);
        }

        private void PressLengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!AllowChange)
                return;
            mw.Set.PressLength = (int)(PressLengthSlider.Value * 1000);
        }

        private void InteractionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!AllowChange)
                return;
            mw.Set.InteractionCycle = (int)(InteractionSlider.Value);
            CalTimeInteraction();
        }
        private void CalTimeInteraction()
        {
            var interact = (60 / mw.Set.LogicInterval);
            rTimeMinute.Text = interact.ToString("f2");
            RInter.Text = ((0.08 * mw.Set.InteractionCycle - 0.9) / interact * 2).ToString("f2");
        }
        #region Link
        private void Git_Click(object sender, RoutedEventArgs e)
        {
            ExtensionFunction.StartURL("https://github.com/LorisYounger/VPet/graphs/contributors");
        }


        private void Github_Click(object sender, RoutedEventArgs e)
        {
            ExtensionFunction.StartURL("https://github.com/LorisYounger/VPet");
        }

        private void LB_Click(object sender, RoutedEventArgs e)
        {
            ExtensionFunction.StartURL("https://space.bilibili.com/609610777");
        }

        private void VPET_Click(object sender, RoutedEventArgs e)
        {
            ExtensionFunction.StartURL("https://www.exlb.net/");
        }
        private void VUP_Click(object sender, RoutedEventArgs e)
        {
            ExtensionFunction.StartURL("https://store.steampowered.com/app/1352140/_/");
        }

        private void Group_Click(object sender, RoutedEventArgs e)
        {
            if (LocalizeCore.CurrentCulture.StartsWith("zh"))
                ExtensionFunction.StartURL("https://space.bilibili.com/690425399");
            else
                ExtensionFunction.StartURL("https://github.com/LorisYounger/VPet");
        }
        private void sendkey_click(object sender, RoutedEventArgs e)
        {
            if (LocalizeCore.CurrentCulture.StartsWith("zh"))
                ExtensionFunction.StartURL("https://www.exlb.net/SendKeys");
            else if (LocalizeCore.CurrentCulture == "null")
                ExtensionFunction.StartURL("https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.sendkeys?view=windowsdesktop-7.0#remarks");
            else
                ExtensionFunction.StartURL($"https://learn.microsoft.com/{LocalizeCore.CurrentCulture}/dotnet/api/system.windows.forms.sendkeys?view=windowsdesktop-7.0#remarks");
        }
        #endregion        

        private void CalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetLogicInterval(CalSlider.Value);
            CalTimeInteraction();
        }

        private void MoveEventBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetAllowMove(MoveEventBox.IsChecked == true);
        }

        private void SmartMoveEventBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetSmartMove(SmartMoveEventBox.IsChecked == true);
        }

        private void CBSmartMove_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetSmartMoveInterval((int)((ComboBoxItem)CBSmartMove.SelectedItem).Tag);
        }


        public void GenStartUP()
        {
            mw.Set["v"][(gbol)"newverstartup"] = true;
            var path = Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\V-Max.lnk";
            if (mw.Set.StartUPBoot)
            {
                if (File.Exists(path))
                    File.Delete(path);
                var link = (IShellLink)new ShellLink();
                link.SetPath(System.Reflection.Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe"));

                link.SetDescription("V-Max");
                link.SetIconLocation(System.IO.Path.Combine(ExtensionValue.BaseDirectory, "vpeticon.ico"), 0);
                try
                {
                    var file = (IPersistFile)link;
                    file.Save(path, false);
                }
                catch
                {
                    MessageBox.Show("创建快捷方式失败,权限不足\n请以管理员身份运行后重试".Translate(), "权限不足".Translate());
                }
            }
            else
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
        private void StartUpBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (StartUpBox.IsChecked == true)
                if (MessageBoxX.Show("该游戏随着开机启动该程序\r如需卸载游戏\r请关闭该选项".Translate() + "\n------\n" + "我已确认,并在卸载游戏前会关闭该功能".Translate(), "开机启动重要消息".Translate(),
                    MessageBoxButton.YesNo, MessageBoxIcon.Warning) != MessageBoxResult.Yes)
                {
                    StartUpBox.IsChecked = false;
                    return;
                }
            //else
            //{
            //    mf.Set["SingleTips"][(gint)"open"] = 1;
            //    MessageBoxX.Show("游戏开机启动的实现方式是创建快捷方式,不是注册表,更健康,所以游戏卸了也不知道\n如果游戏打不开,可以去这里手动删除游戏开机启动快捷方式:\n%appdata%\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\".Translate()
            //        , "关于卸载不掉的问题是因为开启了开机启动".Translate(), MessageBoxIcon.Info);
            //}

            mw.Set.StartUPBoot = StartUpBox.IsChecked == true;
            GenStartUP();
        }

        private int petboxbef;
        private void petbox_back()
        {
            AllowChange = false;
            PetBox.SelectedIndex = petboxbef;
            AllowChange = true;
        }
        private void LoadMutiUI()
        {
            LBHave.Items.Clear();
            foreach (var str in App.MutiSaves)
            {
                var rn = str.Translate();
                if (str == "")
                    rn = "默认存档".Translate();
                if (str == mw.PrefixSave.Trim('-'))
                    rn += " (" + "当前存档".Translate() + ')';
                LBHave.Items.Add(rn);
            }
        }
        private void PetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;

            var petloader = mw.Pets.Find(x => x.Name == mw.Set.PetGraph);
            petloader ??= mw.Pets[0];

            if (mw.PrefixSave == "" && petloader.PetName != mw.Pets[PetBox.SelectedIndex].PetName)
            {//多一个名称判断, 如果宠物名称一致,则切换皮肤不提示多开
                switch (MessageBoxX.Show("是否多开一个新的桌宠使用 {0} 皮肤\n各自存档独立保存,互不影响\n支持同时显示多个宠物".Translate(mw.Pets[PetBox.SelectedIndex].Name.Translate()),
                    "是否多开".Translate(), MessageBoxButton.YesNoCancel))
                {
                    case MessageBoxResult.Yes:
                        var savename = mw.Pets[PetBox.SelectedIndex].Name;
                        petbox_back();
                        //如果有这个皮肤的多开,自动多开
                        if (App.MutiSaves.Contains(savename))
                        {
                            if (App.MainWindows.FirstOrDefault(x => x.PrefixSave.Trim('-') == savename) != null)
                            {
                                MessageBoxX.Show("当前多开已经加载".Translate());
                            }
                            else
                                new MainWindow(savename, mw).Show();
                            return;
                        }
                        foreach (var c in @"()#:|/\?*<>-")
                            if (savename.Contains(c))
                            {
                                MessageBoxX.Show("存档名不能包括特殊符号".Translate());
                                return;
                            }
                        var lps = new LPS(mw.Set.ToString());
                        lps.SetInt("savetimes", 0);
                        lps["gameconfig"].SetString("petgraph", savename);
                        File.WriteAllText(ExtensionValue.DataDirectory + @$"\Setting-{savename}.lps", lps.ToString());
                        App.MutiSaves.Add(savename);
                        new MainWindow(savename, mw).Show();
                        LoadMutiUI();
                        return;
                    case MessageBoxResult.No:
                        break;
                    default:
                        petbox_back();
                        return;
                }
            }


            bool ischangename = mw.Core.Save!.Name == petloader.PetName.Translate();
            petboxbef = PetBox.SelectedIndex;
            mw.Set.PetGraph = mw.Pets[petboxbef].Name;
            PetIntor.Text = mw.Pets[petboxbef].Intor.Translate();
            ButtonRestartGraph.Visibility = Visibility.Visible;

            if (ischangename)
            {
                mw.Core.Save!.Name = mw.Pets[petboxbef].PetName.Translate();
                TextBoxPetName.Text = mw.Core.Save!.Name;
            }
        }

        private void TextBoxPetName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Core.Save!.Name = TextBoxPetName.Text;
        }

        private void DIY_ADD_Click(object sender, RoutedEventArgs e)
        {
            StackDIY.Children.Add(new DIYViewer());
        }

        private void DIY_Save_Click(object sender, RoutedEventArgs e)
        {
            mw.Set["diy"].Clear();
            foreach (DIYViewer dv in StackDIY.Children)
            {
                mw.Set["diy"].Add(dv.ToSub());
            }
            mw.LoadDIY();
        }


        private void ChatGPT_Reset_Click(object sender, RoutedEventArgs e)
        {
            switch (mw.Set["CGPT"][(gstr)"type"])
            {
                //case "API":
                //    new winCGPTSetting(mf).ShowDialog();
                //    break;
                case "DIY":
                    mw.TalkBoxCurr?.Setting();
                    break;
                case "LB":
                    //Task.Run(() =>
                    //{
                    //    if (((TalkBox)mf.TalkBox).ChatGPT_Reset())
                    //    {
                    //        ((TalkBox)mf.TalkBox).btn_startup.Visibility = Visibility.Visible;
                    //        MessageBoxX.Show("桌宠重置成功".Translate());
                    //    }
                    //});
                    //((TalkSelect)mf.TalkBox).RelsTime
                    break;
                case "OFF":
                default:
                    break;
            }
        }

        private void CGPType_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (RBCGPTUseLB.IsChecked == true)
            {
                mw.Set["CGPT"][(gstr)"type"] = "LB";
            }
            else if (RBCGPTDIY.IsChecked == true)
            {
                mw.Set["CGPT"][(gstr)"type"] = "DIY";
            }
            //else if (RBCGPTUseAPI.IsChecked == true)
            //{
            //    mf.Set["CGPT"][(gstr)"type"] = "API";
            //}
            else
            {
                mw.Set["CGPT"][(gstr)"type"] = "OFF";
            }


            switch (mw.Set["CGPT"][(gstr)"type"])
            {
                //case "API":
                //    BtnCGPTReSet.IsEnabled = true;
                //    BtnCGPTReSet.Content = "打开 ChatGPT API 设置".Translate();
                //    if (mf.TalkBox != null)
                //        mf.Main.ToolBar.MainGrid.Children.Remove(mf.TalkBox);
                //    mf.TalkBox = new TalkBoxAPI(mf);
                //    mf.Main.ToolBar.MainGrid.Children.Add(mf.TalkBox);
                //    break;
                case "DIY":
                    BtnCGPTReSet.IsEnabled = true;
                    mw.RemoveTalkBox();
                    BtnCGPTReSet.Content = "打开 {0} 设置".Translate(mw.TalkBoxCurr?.APIName ?? "API");
                    mw.LoadTalkDIY();
                    break;
                case "LB":
                    mw.RemoveTalkBox();
                    BtnCGPTReSet.IsEnabled = true;
                    BtnCGPTReSet.Content = "初始化桌宠聊天程序".Translate();
                    mw.TalkBox = new TalkSelect(mw);
                    mw.Main.ToolBar!.MainGrid.Children.Add(mw.TalkBox);
                    break;
                case "OFF":
                default:
                    mw.RemoveTalkBox();
                    BtnCGPTReSet.IsEnabled = false;
                    BtnCGPTReSet.Content = "聊天框已关闭".Translate();
                    break;
            }
        }

        private void ButtonSetting_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (mod == null)
                return;
            foreach (var mainplug in mw.Plugins)
            {
                try
                {
                    if (mainplug.PluginName == mod.Name && mainplug.GetType().GetMethod("Setting")!.DeclaringType != typeof(MainPlugin)
                    && mainplug.GetType().Assembly.Location.Contains(mod.Path.FullName))
                    {
                        mainplug.Setting();
                        return;
                    }
                }
                finally { }
            }
        }

        private void StartPlace_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (StartPlace.IsChecked == true)
            {
                mw.Set.StartRecordLast = true;
                TextBoxStartUpX.IsEnabled = false;
                TextBoxStartUpY.IsEnabled = false;
                BtnStartUpGet.IsEnabled = false;
            }
            else
            {
                mw.Set.StartRecordLast = false;
                TextBoxStartUpX.IsEnabled = true;
                TextBoxStartUpY.IsEnabled = true;
                BtnStartUpGet.IsEnabled = true;
            }
        }

        private void TextBoxStartUp_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (double.TryParse(TextBoxStartUpX.Text, out double x) && double.TryParse(TextBoxStartUpY.Text, out double y))
                mw.Set.StartRecordPoint = new Point(x, y);
        }

        private void BtnStartUpGet_Click(object sender, RoutedEventArgs e)
        {
            AllowChange = false;
            TextBoxStartUpX.Text = mw.Left.ToString();
            TextBoxStartUpY.Text = mw.Top.ToString();
            mw.Set.StartRecordPoint = new Point(mw.Left, mw.Top);
            AllowChange = true;
        }

        private void CalFunctionBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            //MessageBoxX.Show("由于没做完,暂不支持数据计算\n敬请期待后续更新", "没做完!", MessageBoxButton.OK, MessageBoxIcon.Warning);
            if (CalFunctionBox.IsChecked == true)
            {
                mw.Set.EnableFunction = true;
                combCalFunState.IsEnabled = false;
            }
            else
            {
                mw.Set.EnableFunction = false;
                combCalFunState.IsEnabled = true;
                if (mw.Main.State != Main.WorkingState.Nomal)
                {
                    mw.Main.WorkTimer!.Visibility = Visibility.Collapsed;
                    mw.Main.State = Main.WorkingState.Nomal;
                }
            }
        }

        private void SwitchMsgOut_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.MessageBarOutside = SwitchMsgOut?.IsChecked ?? false;
            if (SwitchMsgOut?.IsChecked ?? false)
                mw.Main.MsgBar?.SetPlaceOUT();
            else
                mw.Main.MsgBar?.SetPlaceIN();
        }

        private void numBackupSaveMaxNum_ValueChanged(object sender, Panuon.WPF.SelectedValueChangedRoutedEventArgs<double?> e)
        {
            if (!AllowChange)
                return;
            mw.Set.BackupSaveMaxNum = (int)(numBackupSaveMaxNum?.Value ?? 0);
        }
        private void BtnOpenSaveManager_Click(object sender, RoutedEventArgs e)
        {
            new winSaveManager(mw).ShowDialog();
        }

        private void Mod_Click(object sender, RoutedEventArgs e)
        {
            List<string> list = new List<string>();
            foreach (CoreMOD mod in mw.CoreMODs)
            {
                foreach (string str in mod.Author.Split(','))
                    list.Add(str.Trim());
            }
            list = list.Distinct().ToList();
            MessageBoxX.Show(string.Join("\n", list), "感谢以下MOD开发人员".Translate());
        }

        private void Using_Click(object sender, RoutedEventArgs e)
        {
            string NormalizeDllName(string dll)
            {
                var fileName = Path.GetFileName(dll);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = dll;
                return Path.GetFileNameWithoutExtension(fileName) ?? fileName;
            }

            var rows = CoreMOD.LoadedDLL
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeDllName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => ExtensionValue.DllReferenceDescriptions.TryGetValue(name, out var description)
                    ? $"> {name}\n{description}"
                    : $"> {name}.dll")
                .ToList();

            MessageBoxX.Show(string.Join("\n", rows), "DLL引用名单".Translate());
        }

        private void combCalFunState_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.CalFunState = (IGameSave.ModeType)combCalFunState.SelectedIndex;
            mw.Main.NoFunctionMOD = (IGameSave.ModeType)combCalFunState.SelectedIndex;
            mw.Main.EventTimer_Elapsed();
        }

        private void HitThroughBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set["v"][(gbol)"HitThrough"] = true;
            mw.Set.HitThrough = HitThroughBox.IsChecked ?? false;
            if (HitThroughBox.IsChecked ?? false != mw.HitThrough)
                mw.SetTransparentHitThrough();
            if (HitThroughBox.IsChecked ?? false)
                PetHelperBox.IsChecked = true;
        }

        private void PetHelperBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (PetHelperBox.IsChecked == true)
            {
                mw.Set.PetHelper = true;
                mw.LoadPetHelper();
            }
            else
            {
                mw.Set.PetHelper = false;
                mw.petHelper?.Close();
                mw.petHelper = null;
            }
        }

        private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetLanguage((string)LanguageBox.SelectedItem);
            TextBoxPetName.Text = mw.Core.Save!.Name;
            ButtonRestartGraph.Visibility = Visibility.Visible;

        }

        private void MainTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            voicetimer.Stop();
            switch (MainTab.SelectedIndex)
            {
                case 2://启动音量探测
                    voicetimer.Start();
                    break;
                case 4:
                    if (mw.HashCheck)
                    {
                        RHashCheck.Text = "通过".Translate();
                    }
                    else
                    {
                        RHashCheck.Text = "失败".Translate();
                    }
                    break;
            }
        }
        DispatcherTimer voicetimer;

        private void VoiceCatchSilder_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!AllowChange)
                return;
            mw.Set.MusicCatch = VoiceCatchSilder.Value;
            mw.Set.MusicMax = VoiceMaxSilder.Value;
        }

        private void cleancache_click(object sender, RoutedEventArgs e)
        {
            mw.Set.LastCacheDate = DateTime.MinValue;
            MessageBoxX.Show("清理指令已下达,下次启动桌宠时生效".Translate());
        }

        private void SliderResolution_MouseUp(object sender, MouseButtonEventArgs e)
        {
            mw.Set.Resolution = (int)SliderResolution.Value;
            ButtonRestartGraph.Visibility = Visibility.Visible;
        }

        private void save_click(object sender, RoutedEventArgs e)
        {
            mw.Save();
            MessageBoxX.Show("保存成功".Translate());
        }

        private void swAutoCal_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set["gameconfig"].SetBool("noAutoCal", !(swAutoCal.IsChecked ?? false));
        }

        private void restart_click(object sender, RoutedEventArgs e)
        {
            if (MessageBoxX.Show("是否重置游戏数据重新开始?".Translate(), "重新开始".Translate(), MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                var oldsave = mw.GameSavesData;
                mw.GameSavesData = new GameSave_v2(mw.Core.Save!.Name);
                mw.Core.Save = mw.GameSavesData.GameSave;
                mw.GameSavesData.GameSave.Event_LevelUp += mw.LevelUP;

                if (oldsave.HashCheck) // 对于重开无作弊的玩家保留统计
                {
                    mw.GameSavesData.Statistics = oldsave.Statistics;
                    if (oldsave.GameSave.Money > 10000000 || oldsave.GameSave.Money < -1000000000 || oldsave.GameSave.Exp > 100000000 || oldsave.GameSave.Exp < -10000000000)
                    {
                        mw.Core.Save!.Money = 10000;
                        mw.Core.Save!.Exp = 10000;
                    }
                }
                mw.HashCheck = true;
                MessageBoxX.Show("重置成功".Translate());
            }
        }

        private void cbChatAPISelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.TalkAPIIndex = cbChatAPISelect.SelectedIndex;
            mw.Set["CGPT"][(gstr)"DIY"] = mw.TalkBoxCurr?.APIName ?? "";
            if (RBCGPTDIY.IsChecked == true)
                mw.LoadTalkDIY();
            BtnCGPTReSet.Content = "打开 {0} 设置".Translate(mw.TalkBoxCurr?.APIName ?? "API");

        }

        private void btn_muti_open_click(object sender, RoutedEventArgs e)
        {
            if (LBHave.SelectedIndex == -1)
                return;
            var str = App.MutiSaves[LBHave.SelectedIndex];
            if (str.EndsWith(")") || App.MainWindows.FirstOrDefault(x => x.PrefixSave.Trim('-') == str) != null)
            {
                MessageBoxX.Show("当前多开已经加载".Translate());
                return;
            }
            new MainWindow(str, mw).Show();
        }

        private void btn_mutinew_click(object sender, RoutedEventArgs e)
        {
            var savename = TBNew.Text;
            foreach (var c in @"()#:|/\?*<>-")
                if (savename.Contains(c))
                {
                    MessageBoxX.Show("存档名不能包括特殊符号".Translate());
                    return;
                }
            if (App.MutiSaves.FirstOrDefault(x => x.ToLowerInvariant() == savename.ToLowerInvariant()) != null)
            {
                MessageBoxX.Show("存档名重复".Translate());
                return;
            }

            var lps = new LPS(mw.Set);
            lps.SetInt("savetimes", 0);
            File.WriteAllText(ExtensionValue.DataDirectory + @$"\Setting-{savename}.lps", lps.ToString());
            App.MutiSaves.Add(savename);
            new MainWindow(savename, mw).Show();
        }

        private void btn_mutidel_Click(object sender, RoutedEventArgs e)
        {
            if (LBHave.SelectedIndex == -1)
                return;
            var str = App.MutiSaves[LBHave.SelectedIndex];
            if (str == "默认存档".Translate())
            {
                MessageBoxX.Show("默认存档无法删除,请使用重新开始功能重新开始游戏".Translate());
                return;
            }
            if (str.EndsWith(")") || App.MainWindows.FirstOrDefault(x => x.PrefixSave.Trim('-') == str) != null)
            {
                MessageBoxX.Show("当前多开已经加载,请先关闭改多开后重试".Translate());
                return;
            }
            if (!App.MutiSaves.Contains(str))
            {
                LoadMutiUI();
                return;
            }
            if (MessageBoxX.Show("是否删除当前选择({0})的多开存档?".Translate(str), "删除前确认".Translate(), MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                File.Delete(ExtensionValue.DataDirectory + @$"\Setting-{str}.lps");
                App.MutiSaves.Remove(str);
                LoadMutiUI();
            }
        }

        private void btn_muti_master_click(object sender, RoutedEventArgs e)
        {
            if (LBHave.SelectedIndex == -1)
                return;
            var str = App.MutiSaves[LBHave.SelectedIndex];
            foreach (var sp in new DirectoryInfo(ExtensionValue.DataDirectory).GetFiles("startup_*"))
            {
                sp.Delete();
            }
            if (str != "")
                File.Create(ExtensionValue.DataDirectory + @"\startup_" + str).Close();
            MessageBoxX.Show("已将当前选择 {0} 设为默认启动存档".Translate(str.Translate()));
        }


        private void ConsoleBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.DeBug = ConsoleBox.IsChecked ?? false;
        }

        private void TextBoxHostName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.GameSavesData.GameSave.HostName = TextBoxHostName.Text;
        }

        private void TextBoxHostBDay_SelectedDateTimeChanged(object sender, Panuon.WPF.SelectedValueChangedRoutedEventArgs<DateTime?> e)
        {
            if (!AllowChange || TextBoxHostBDay.SelectedDateTime == null)
                return;
            mw.GameSavesData.SetDateTime("HostBDay", TextBoxHostBDay.SelectedDateTime.Value);
        }

        private void SwitchOpacityHitThrough_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.OpacityHitThrough = SwitchOpacityHitThrough.IsChecked == true;
        }

        private void SwitchOpacity_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (SwitchOpacity.IsChecked == true)
            {
                mw.Set.OpacityHitThrough = true;
                mw.Set.OpacityMain = true;
                SwitchOpacityHitThrough.IsChecked = true;
                mw.Opacity = mw.Set.Opacity;
            }
            else
            {
                mw.Set.OpacityMain = false;
                mw.Opacity = 1;
            }
        }

        private void OpacitySlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            mw.Set.Opacity = OpacitySlider.Value;
            if (mw.Set.OpacityMain)
                mw.Opacity = mw.Set.Opacity;
        }

        private void AutoChangeWindowEvent_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (AutoChangeWindowEvent.IsChecked == true)
            {
                mw.Set.AutoChangeWindow = true;
            }
            else
            {
                mw.Set.AutoChangeWindow = false;
            }
            UpdateMoveAreaText();
        }



        private void ButtonReLS_Click(object sender, RoutedEventArgs e)
        {
            ShowModList();
        }

        private void tb_seach_mod_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            RenderModList(selectedModInfo?.Path.FullName, tb_seach_mod.Text);
        }

        private void SwitchHideFromTaskControl_OnChecked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.HideFromTaskControl = SwitchHideFromTaskControl.IsChecked == true;
            ButtonRestartGraph.Visibility = Visibility.Visible;
        }


    }
}
