using LinePutScript;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using static VPet_Simulator.Core.IGraph;
using static VPet_Simulator.Core.Picture;


namespace VPet_Simulator.Core
{
    /// <summary>
    /// Picture.xaml 的交互逻辑
    /// </summary>
    public class Picture : IImageRun
    {
        /// <summary>
        /// 新建新静态图像
        /// </summary>
        /// <param name="path">图片路径</param>
        public Picture(GraphCore graphCore, string path, GraphInfo graphinfo, int length = 1000, bool isloop = false)
        {
            GraphInfo = graphinfo;
            IsLoop = isloop;
            Length = length;
            GraphCore = graphCore;
            Path = path;
            if (!GraphCore.CommConfig.ContainsKey("PIC_Setup"))
            {
                GraphCore.CommConfig["PIC_Setup"] = true;
                GraphCore.Dispatcher.Invoke(() =>
                {
                    GraphCore.CommUIElements["Image1.Picture"] = new Image() { Width = 500, Height = 500 };
                    GraphCore.CommUIElements["Image2.Picture"] = new Image() { Width = 500, Height = 500 };
                    GraphCore.CommUIElements["Image3.Picture"] = new Image() { Width = 500, Height = 500 };
                });
            }
            IsReady = true;
        }
        public static void LoadGraph(GraphCore graph, FileSystemInfo path, ILine info)
        {
            if (!(path is FileInfo))
            {
                PNGAnimation.LoadGraph(graph, path, info);
                return;
            }
            if (path.Extension != ".png")
                return;
            try
            {
                using var stream = File.OpenRead(path.FullName);
                using var codec = SKCodec.Create(stream);
                if (codec != null && codec.FrameCount > 1)
                {
                    APNGAnimation.LoadGraph(graph, path, info);
                    return;
                }
            }
            catch
            {
            }
            int length = info.GetInt("length");
            if (length == 0)
            {
                var nameParts = path.Name.Split('.');
                if (nameParts.Length > 1 && !int.TryParse(nameParts[nameParts.Length - 2].Split('_').Last(), out length))
                    length = 1000;
            }
            bool isLoop = info[(gbol)"loop"];
            Picture pa = new Picture(graph, path.FullName, new GraphInfo(path, info), length, isLoop);
            graph.AddGraph(pa);
        }
        /// <summary>
        /// 图片资源
        /// </summary>
        public string Path { get; set; }
        private GraphCore? GraphCore;
        public bool IsLoop { get; set; }
        /// <summary>
        /// 播放持续时间 毫秒
        /// </summary>
        public int Length { get; set; }
        //public bool StoreMemory => true;//经过测试,储存到内存好处多多,不储存也要占用很多内存,干脆存了吧

        /// <summary>
        /// 动画信息
        /// </summary>
        public GraphInfo GraphInfo { get; private set; }

        public bool IsReady { get; set; } = false;

        public TaskControl? Control { get; set; }

        public bool IsFail => false;

        public string FailMessage => "";

        public void Run(Decorator parant, Action? EndAction = null)
        {
            if (Control?.PlayState == true)
            {//如果当前正在运行,重置状态
                Control.SetContinue();
                Control.EndAction = EndAction;
                return;
            }
            var NEWControl = new TaskControl(EndAction);
            Control = NEWControl;

            parant.Dispatcher.Invoke(() =>
            {
                if (parant.Tag != this)
                {
                    Image img;
                    if (parant.Child == GraphCore!.CommUIElements["Image1.Picture"])
                    {
                        img = (Image)GraphCore.CommUIElements["Image1.Picture"];
                    }
                    else if (parant.Child == GraphCore.CommUIElements["Image3.Picture"])
                    {
                        img = (Image)GraphCore.CommUIElements["Image3.Picture"];
                    }
                    else
                    {
                        img = (Image)GraphCore.CommUIElements["Image2.Picture"];
                        if (parant.Child != img)
                        {
                            if (img.Parent == null)
                            {
                                parant.Child = img;
                            }
                            else
                            {
                                img = (Image)GraphCore.CommUIElements["Image1.Picture"];
                                if (img.Parent != null)
                                    ((Decorator)img.Parent).Child = null;
                                parant.Child = img;
                            }
                        }
                    }
                    img.Width = 500;
                    img.Source = GetImage();
                    parant.Tag = this;
                }
                _ = RunAsync(NEWControl);
            });
        }
        /// <summary>
        /// 通过控制器运行
        /// </summary>
        /// <param name="Control"></param>
        public void Run(TaskControl Control) => RunAsync(Control).GetAwaiter().GetResult();

        /// <summary>
        /// V-Max : attente asynchrone (aucun thread bloqué), respecte <see cref="AnimationGate"/>
        /// </summary>
        public async Task RunAsync(TaskControl Control)
        {
            await Task.Delay(Length).ConfigureAwait(false);
            //判断是否要下一步
            switch (Control.Type)
            {
                case TaskControl.ControlType.Stop:
                    Control.EndAction?.Invoke();
                    return;
                case TaskControl.ControlType.Status_Stoped:
                    return;
                case TaskControl.ControlType.Continue:
                    Control.Type = TaskControl.ControlType.Status_Quo;
                    await RunAsync(Control).ConfigureAwait(false);
                    return;
                case TaskControl.ControlType.Status_Quo:
                    if (IsLoop)
                    {
                        _ = RunAsync(Control);
                    }
                    else
                    {
                        Control.Type = TaskControl.ControlType.Status_Stoped;
                        Control.EndAction?.Invoke(); //运行结束动画时事件
                    }
                    return;
            }
        }

        private BitmapSource? cachedImage;
        /// <summary>
        /// V-Max : image décodée une seule fois, figée, à la résolution du cache
        /// </summary>
        private BitmapSource GetImage()
        {
            var image = cachedImage;
            if (image != null)
                return image;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (GraphCore?.Resolution > 0)
                bi.DecodePixelWidth = GraphCore.Resolution;
            bi.UriSource = new Uri(Path);
            bi.EndInit();
            bi.Freeze();
            cachedImage = bi;
            return bi;
        }

        public Task Run(Image img, Action? EndAction = null)
        {
            if (Control?.PlayState == true)
            {//如果当前正在运行,重置状态
                Control.EndAction = null;
                Control.Type = TaskControl.ControlType.Stop;
            }
            Control = new TaskControl(EndAction);
            return img.Dispatcher.Invoke(() =>
            {
                if (img.Tag == this)
                {
                    return new Task(() => Run(Control));
                }
                img.Tag = this;
                img.Source = GetImage();
                img.Width = 500;
                return new Task(() => Run(Control));
            });
        }
        /// <summary>
        /// 可以通过图片模块运行该动画
        /// </summary>
        public interface IImageRun : IGraph
        {
            /// <summary>
            /// 指定图像图像控件准备运行该动画
            /// </summary>
            /// <param name="img">用于显示的Image</param>
            /// <param name="EndAction">结束动画</param>
            /// <returns>准备好的线程</returns>
            Task Run(System.Windows.Controls.Image img, Action? EndAction = null);
        }
        public void Dispose()
        {
            GraphCore = null;
        }
    }


}
