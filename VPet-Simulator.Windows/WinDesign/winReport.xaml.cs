using LinePutScript.Localization.WPF;
using Panuon.WPF.UI;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Web;
using System.Windows;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows
{
    /// <summary>
    /// winReport.xaml 的交互逻辑
    /// </summary>
    public partial class winReport : WindowX
    {
        MainWindow mw;
        string save;
        public bool IsUniformSizeChanged => false;
        public bool StoreSize => false;
        public winReport(MainWindow mainw, string? errmsg = null)
        {
            mainw.Windows.Add(this);
            InitializeComponent();
            mw = mainw;
            Title = "反馈中心".Translate() + ' ' + mw.PrefixSave;
            save = mw!.Core?.Save?.ToLine().ToString() + mw!.Set?.ToString();
            if (errmsg != null)
            {
                tType.SelectedIndex = 0;
                tContent.Text = errmsg;
                tContent.IsReadOnly = true;
            }

        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void tUpload_Click(object sender, RoutedEventArgs e)
        {//游戏设置比存档更重要,桌宠大部分内容存设置里了,所以一起上传
            if (tUpload.IsChecked == true)
                save = mw.Core.Save!.ToLine().ToString() + mw.Set.ToString();
            else
                save = "玩家取消上传存档".Translate();
        }

        private void btn_upload(object sender, RoutedEventArgs e)
        {
            if (tDescription.Text == "" && tType.SelectedIndex != 0)
            {
                MessageBoxX.Show("问题详细描述是反馈具体问题\n例如如何触发这个报错,游戏有什么地方不合理等".Translate(), "请填写问题描述".Translate());
                return;
            }
            // V-Max : le rapport n'est plus envoyé au serveur d'origine de VPet.
            // On prépare le texte, on le copie dans le presse-papiers et on ouvre un ticket GitHub.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("**Type :** " + tType.Text);
            sb.AppendLine("**Version :** " + mw.Version + " (" + LocalizeCore.CurrentCulture + ")");
            if (!string.IsNullOrWhiteSpace(tContact.Text))
                sb.AppendLine("**Contact :** " + tContact.Text);
            sb.AppendLine();
            sb.AppendLine(tDescription.Text);
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(tContent.Text);
            sb.AppendLine("```");
            if (tUpload.IsChecked == true)
            {
                sb.AppendLine("<details><summary>Sauvegarde et paramètres</summary>");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(save);
                sb.AppendLine("```");
                sb.AppendLine("</details>");
            }
            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch
            {
            }
            string title = HttpUtility.UrlEncode("[" + tType.Text + "] " + (tDescription.Text.Split('\n')[0].Trim() is { Length: > 0 } t ? t[..System.Math.Min(t.Length, 80)] : "Rapport"));
            ExtensionFunction.StartURL(ExtensionValue.IssueURL + "?title=" + title);
            MessageBoxX.Show("Le rapport a été copié dans le presse-papiers.\nCollez-le dans le ticket GitHub qui vient de s'ouvrir.".Translate(),
                "Rapport prêt".Translate());
            Close();
        }
        public void ShowTimeLoading()
        {
            Dispatcher.Invoke(() =>
             {
                 if (gridLoading.Visibility == Visibility.Visible)
                 {
                     if (pgload.Value >= 117)
                     {
                         pgload.Value = 120;
                     }
                     else
                     {
                         pgload.Value += Function.Rnd.Next(5, 15) * 0.1;
                         Task.Run(() =>
                         {
                             Thread.Sleep(Function.Rnd.Next(500, 1500));
                             ShowTimeLoading();
                         });
                     }
                 }
             });

        }
        private void MainGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Height = MainGrid.ActualHeight + 50;
        }

        private void tType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (tType.SelectedIndex == 5)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var v in LocalizeCore.StoreTranslationList)
                    {
                        sb.AppendLine(v.Replace("\n", @"\n").Replace("\r", @"\r"));
                    }
                    tContent.Text = sb.ToString();
                    if (string.IsNullOrEmpty(tContent.Text))
                    {
                        tContent.Text = "没有需要提交的翻译的内容".Translate();
                    }
                    tUpload.IsChecked = false;
                }
                catch
                {

                }
            }
        }

        private void WindowX_Closed(object sender, EventArgs e)
        {
            mw.Windows.Remove(this);
        }
    }
}
