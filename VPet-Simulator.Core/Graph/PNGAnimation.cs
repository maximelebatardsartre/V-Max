using LinePutScript;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static VPet_Simulator.Core.IGraph;
using static VPet_Simulator.Core.Picture;

namespace VPet_Simulator.Core
{

    /// <summary>
    /// PNGAnimation.xaml 的交互逻辑
    /// </summary>
    /// <remarks>
    /// V-Max : moteur de lecture réécrit.
    /// - Une boucle asynchrone par lecture (await Task.Delay) au lieu d'un thread bloqué par Thread.Sleep.
    /// - Mise à jour de l'image sans Invoke synchrone.
    /// - Les images sont matérialisées une fois (Pbgra32, figées) et réutilisées à chaque boucle,
    ///   au lieu d'une nouvelle CroppedBitmap à chaque image.
    /// - Respecte <see cref="AnimationGate"/> (pause quand le compagnon n'est pas visible).
    /// - La taille des images est lue dans l'en-tête PNG (plus de décodage complet au démarrage).
    /// </remarks>
    public partial class PNGAnimation : IImageRun
    {
        /// <summary>
        /// 所有动画帧
        /// </summary>
        public List<Animation> Animations;
        /// <summary>
        /// 是否循环播放
        /// </summary>
        public bool IsLoop { get; set; }

        /// <summary>
        /// 动画信息
        /// </summary>
        public GraphInfo GraphInfo { get; private set; }

        /// <summary>
        /// 是否准备完成
        /// </summary>
        public bool IsReady { get; private set; } = false;

        public TaskControl? Control { get; set; }

        int nowid;
        /// <summary>
        /// 图片资源
        /// </summary>
        public string Path { get; set; } = "";
        private GraphCore GraphCore;
        private Int32Rect[]? FrameRects;
        private readonly object FramesLock = new object();
        /// <summary>
        /// Images matérialisées (null tant que la sprite sheet n'a pas été chargée, ou après libération du cache)
        /// </summary>
        private BitmapSource[]? Frames;
        private int FrameWidth;
        private int FrameHeight;
        public long LastUseTimeTicks = DateTime.UtcNow.Ticks;

        public bool IsFail { get; set; } = false;

        public string FailMessage { get; set; } = "";

        /// <summary>
        /// 新建 PNG 动画
        /// </summary>
        /// <param name="path">文件夹位置</param>
        /// <param name="paths">文件内容列表</param>
        /// <param name="isLoop">是否循环</param>
        public PNGAnimation(GraphCore graphCore, string path, FileInfo[] paths, GraphInfo graphinfo, bool isLoop = false)
        {
            Animations = new List<Animation>();
            IsLoop = isLoop;
            GraphInfo = graphinfo;
            GraphCore = graphCore;
            if (!GraphCore.CommConfig.ContainsKey("PA_Setup"))
            {
                GraphCore.CommConfig["PA_Setup"] = true;
                GraphCore.Dispatcher.Invoke(() =>
                {
                    GraphCore.CommUIElements["Image1.PNGAnimation"] = new System.Windows.Controls.Image() { Height = 500 };
                    GraphCore.CommUIElements["Image2.PNGAnimation"] = new System.Windows.Controls.Image() { Height = 500 };
                    GraphCore.CommUIElements["Image3.PNGAnimation"] = new System.Windows.Controls.Image() { Height = 500 }; // 多整个, 防止动画闪烁
                });
            }
            Task.Run(() => startup(path, paths));
        }

        public static void LoadGraph(GraphCore graph, FileSystemInfo path, ILine info)
        {
            if (!(path is DirectoryInfo p))
            {
                Picture.LoadGraph(graph, path, info);
                return;
            }
            var paths = p.GetFiles("*.png");
            if (paths.Length == 0)
            {
                return;
            }
            else if (paths.Length == 1)
            {
                Picture.LoadGraph(graph, paths[0], info);
                return;
            }

            bool isLoop = info[(gbol)"loop"];
            PNGAnimation pa = new PNGAnimation(graph, path.FullName, paths, new GraphInfo(path, info), isLoop);
            graph.AddGraph(pa);
        }

        /// <summary>
        /// 最大同时加载数
        /// </summary>
        public static int MaxLoadMemory = 2000;

        /// <summary>
        /// V-Max : nombre maximal de constructions de sprite sheets simultanées (limite les pics mémoire au premier lancement)
        /// </summary>
        private static readonly SemaphoreSlim BuildSlots = new(Math.Max(1, Environment.ProcessorCount / 4), Math.Max(1, Environment.ProcessorCount / 4));

        /// <summary>
        /// Calcule la taille d'une image dans la sprite sheet à partir de la taille d'origine
        /// </summary>
        private void ComputeFrameSize(int srcWidth, int srcHeight, int frameCount)
        {
            int w = srcWidth;
            int h = srcHeight;
            if (w > GraphCore.Resolution)
            {
                w = GraphCore.Resolution;
                h = (int)(srcHeight * (GraphCore.Resolution / (double)srcWidth));
            }
            if (frameCount * w >= 60000)
            {//修复大长动画导致过长分辨率导致可能的报错
                w = 60000 / frameCount;
                h = (int)(srcHeight * (w / (double)srcWidth));
            }
            FrameWidth = w;
            FrameHeight = h;
        }

        private async Task startup(string path, FileInfo[] paths)
        {
            while (Function.MemoryUsage() > MaxLoadMemory)
            {
                await Task.Delay(100);
            }
            try
            {
                // V-Max : taille lue dans l'en-tête PNG uniquement (auparavant : décodage complet de la première image à chaque lancement)
                using (var codec = SKCodec.Create(paths[0].FullName))
                {
                    if (codec == null)
                        throw new InvalidDataException("PNG illisible : " + paths[0].FullName);
                    ComputeFrameSize(codec.Info.Width, codec.Info.Height, paths.Length);
                }

                //新方法:加载大图片
                //生成大文件加载非常慢,先看看有没有缓存能用
                Path = System.IO.Path.Combine(GraphCore.CachePath, $"{GraphCore!.Resolution}_{Math.Abs(Sub.GetHashCode(path))}_{paths.Length}.png");
                var sem = GraphCore.SpriteSheetBuildLocks.GetOrAdd(Path, _ => new SemaphoreSlim(1, 1));
                await sem.WaitAsync();
                try
                {
                    bool needBuild;
                    var cacheList = (List<string>)GraphCore.CommConfig["Cache"];
                    lock (cacheList)
                    {
                        needBuild = !File.Exists(Path) && !cacheList.Contains(path);
                        if (needBuild)
                            cacheList.Add(path);
                    }
                    if (needBuild)
                    {
                        await BuildSlots.WaitAsync();
                        try
                        {
                            BuildSpriteSheet(paths);
                        }
                        finally
                        {
                            BuildSlots.Release();
                        }
                    }
                }
                finally
                {
                    sem.Release();
                }

                FrameRects = new Int32Rect[paths.Length];

                for (int i = 0; i < paths.Length; i++)
                {
                    FrameRects[i] = new Int32Rect(FrameWidth * i, 0, FrameWidth, FrameHeight);
                    var noExtFileName = System.IO.Path.GetFileNameWithoutExtension(paths[i].Name);
                    int time = int.Parse(noExtFileName.Substring(noExtFileName.LastIndexOf('_') + 1));
                    Animations.Add(new Animation(this, time, i));
                }
                IsReady = true;
            }
            catch (Exception e)
            {
                IsFail = true;
                FailMessage = $"--PNGAnimation--{GraphInfo}--\nPath: {path}\n{e.Message}";
            }
        }

        /// <summary>
        /// Construit la sprite sheet horizontale dans le cache.
        /// V-Max : chaque image est décodée puis dessinée immédiatement (mémoire bornée),
        /// avec une compression PNG rapide (auparavant : toutes les images en mémoire en même temps).
        /// </summary>
        private void BuildSpriteSheet(FileInfo[] paths)
        {
            int w = FrameWidth, h = FrameHeight;
            using var combinedBitmap = new SKBitmap(w * paths.Length, h);
            using (var canvas = new SKCanvas(combinedBitmap))
            {
                using var paint = new SKPaint { IsAntialias = true };
                var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                for (int i = 0; i < paths.Length; i++)
                {
                    using var img = SKImage.FromEncodedData(paths[i].FullName);
                    if (img == null)
                        continue;
                    canvas.DrawImage(img, new SKRect(w * i, 0, w * (i + 1), h), sampling, paint);
                }
            }
            using var pixmap = combinedBitmap.PeekPixels();
            using var data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 3));
            string tmp = Path + ".tmp";
            using (var stream = File.Create(tmp))
            {
                data.SaveTo(stream);
            }
            File.Move(tmp, Path, true);
        }

        /// <summary>
        /// 单帧动画
        /// </summary>
        public class Animation
        {
            private PNGAnimation parent;
            public int FrameIndex;
            /// <summary>
            /// 帧时间
            /// </summary>
            public int Time;
            public Animation(PNGAnimation parent, int time, int frameIndex)
            {
                this.parent = parent;
                Time = time;
                FrameIndex = frameIndex;
            }
            /// <summary>
            /// 运行该图层 (bloquant, conservé pour compatibilité : préférer la lecture asynchrone interne)
            /// </summary>
            /// <param name="Control">动画控制</param>
            /// <param name="This">显示的图层</param>
            public void Run(FrameworkElement This, TaskControl Control)
            {
                parent.PlayAsync(This, Control, FrameIndex).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// V-Max : boucle de lecture asynchrone (aucun thread bloqué entre deux images)
        /// </summary>
        private async Task PlayAsync(FrameworkElement This, TaskControl control, int startIndex)
        {
            try
            {
                int id = startIndex;
                while (true)
                {
                    nowid = id;
                    // En pause (compagnon invisible) : pas de rendu, mais la chronologie continue
                    // pour que les enchaînements (bulle, fin d'animation…) se produisent normalement.
                    if (!AnimationGate.IsPaused)
                    {
                        var frameSource = GetFrameSource(id);
                        if (This.Dispatcher.CheckAccess())
                            ShowFrame(This, frameSource);
                        else
                            _ = This.Dispatcher.InvokeAsync(() => ShowFrame(This, frameSource), DispatcherPriority.Render);
                    }

                    await Task.Delay(Animations[id].Time).ConfigureAwait(false);

                    //判断是否要下一步
                    switch (control.Type)
                    {
                        case TaskControl.ControlType.Stop:
                            control.EndAction?.Invoke();
                            return;
                        case TaskControl.ControlType.Status_Stoped:
                            return;
                        case TaskControl.ControlType.Status_Quo:
                        case TaskControl.ControlType.Continue:
                            if (++id >= Animations.Count)
                            {
                                if (IsLoop)
                                {
                                    id = 0;
                                }
                                else if (control.Type == TaskControl.ControlType.Continue)
                                {
                                    control.Type = TaskControl.ControlType.Status_Quo;
                                    id = 0;
                                }
                                else
                                {
                                    control.Type = TaskControl.ControlType.Status_Stoped;
                                    control.EndAction?.Invoke(); //运行结束动画时事件
                                    return;
                                }
                            }
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Trace.TraceError($"PNGAnimation {GraphInfo}: {e}");
                control.Type = TaskControl.ControlType.Status_Stoped;
            }
        }

        private static void ShowFrame(FrameworkElement This, BitmapSource? frameSource)
        {
            if (This is System.Windows.Controls.Image image)
            {
                image.Source = frameSource;
            }
            This.Margin = new Thickness(0, 0, 0, 0);
        }

        /// <summary>
        /// 从0开始运行该动画
        /// </summary>
        public void Run(Decorator parant, Action? EndAction = null)
        {
            Touch();
            if (!IsReady)
            {
                EndAction?.Invoke();
                return;
            }
            if (Control?.PlayState == true)
            {//如果当前正在运行,重置状态
                Control.Stop(() => Run(parant, EndAction));
                return;
            }
            nowid = 0;
            var NEWControl = new TaskControl(EndAction);
            Control = NEWControl;
            parant.Dispatcher.Invoke(() =>
            {
                if (parant.Tag == this)
                {
                    _ = PlayAsync((System.Windows.Controls.Image)parant.Child, NEWControl, 0);
                    return;
                }
                System.Windows.Controls.Image img;

                if (parant.Child == GraphCore!.CommUIElements["Image1.PNGAnimation"])
                {
                    img = (System.Windows.Controls.Image)GraphCore.CommUIElements["Image1.PNGAnimation"];
                }
                else if (parant.Child == GraphCore.CommUIElements["Image3.PNGAnimation"])
                {
                    img = (System.Windows.Controls.Image)GraphCore.CommUIElements["Image3.PNGAnimation"];
                }
                else
                {
                    img = (System.Windows.Controls.Image)GraphCore.CommUIElements["Image2.PNGAnimation"];
                    if (!ReferenceEquals(parant.Child, img))
                    {
                        if (img.Parent is not null)
                        {
                            img = (System.Windows.Controls.Image)GraphCore.CommUIElements["Image1.PNGAnimation"];
                        }

                        if (!ReferenceEquals(parant.Child, img))
                        {
                            if (img.Parent is Decorator oldParent)
                                oldParent.Child = null;
                            parant.Child = img;
                        }
                    }
                }
                parant.Tag = this;
                img.Source = GetFrameSource(0);
                img.Width = 500;
                _ = PlayAsync((System.Windows.Controls.Image)parant.Child, NEWControl, 0);
            });
        }
        /// <summary>
        /// 指定图像图像控件准备运行该动画
        /// </summary>
        /// <param name="img">用于显示的Image</param>
        /// <param name="EndAction">结束动画</param>
        /// <returns>准备好的线程</returns>
        public Task Run(System.Windows.Controls.Image img, Action? EndAction = null)
        {
            Touch();
            if (!IsReady)
            {
                EndAction?.Invoke();
                return Task.CompletedTask;
            }
            if (Control?.PlayState == true)
            {//如果当前正在运行,重置状态
                Control.EndAction = null;
                Control.Type = TaskControl.ControlType.Stop;
            }
            nowid = 0;
            Control = new TaskControl(EndAction);
            var control = Control;
            return img.Dispatcher.Invoke(() =>
            {
                if (img.Tag != this)
                {
                    img.Tag = this;
                    img.Source = GetFrameSource(0);
                    img.Width = 500;
                }
                return new Task(() => PlayAsync(img, control, 0).GetAwaiter().GetResult());
            });
        }

        private BitmapSource? GetFrameSource(int frameIndex)
        {
            Touch();
            var frames = EnsureFramesLoaded();
            if (frames == null || frameIndex < 0 || frameIndex >= frames.Length)
                return null;
            return frames[frameIndex];
        }

        /// <summary>
        /// Charge la sprite sheet et matérialise toutes les images (Pbgra32, figées).
        /// La sprite sheet elle-même n'est pas conservée : seules les images restent en mémoire.
        /// </summary>
        private BitmapSource[]? EnsureFramesLoaded()
        {
            var frames = Frames;
            if (frames != null)
                return frames;
            lock (FramesLock)
            {
                if (Frames != null)
                    return Frames;
                var rects = FrameRects;
                if (rects == null || rects.Length == 0)
                    return null;
                try
                {
                    BitmapImage spriteSheet = new BitmapImage();
                    spriteSheet.BeginInit();
                    spriteSheet.CacheOption = BitmapCacheOption.OnLoad;
                    spriteSheet.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    spriteSheet.UriSource = new Uri(Path);
                    spriteSheet.EndInit();
                    spriteSheet.Freeze();

                    BitmapSource source = spriteSheet;
                    if (source.Format != PixelFormats.Pbgra32)
                    {
                        var converted = new FormatConvertedBitmap(spriteSheet, PixelFormats.Pbgra32, null, 0);
                        converted.Freeze();
                        source = converted;
                    }

                    var result = new BitmapSource[rects.Length];
                    int stride = FrameWidth * 4;
                    byte[] buffer = new byte[stride * FrameHeight];
                    for (int i = 0; i < rects.Length; i++)
                    {
                        var rect = rects[i];
                        if (rect.X + rect.Width > source.PixelWidth || rect.Height > source.PixelHeight)
                            rect = new Int32Rect(Math.Min(rect.X, Math.Max(0, source.PixelWidth - rect.Width)), 0,
                                Math.Min(rect.Width, source.PixelWidth), Math.Min(rect.Height, source.PixelHeight));
                        source.CopyPixels(rect, buffer, stride, 0);
                        var frame = BitmapSource.Create(rect.Width, rect.Height, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
                        frame.Freeze();
                        result[i] = frame;
                    }
                    Frames = result;
                    return result;
                }
                catch (Exception e)
                {
                    Trace.TraceError($"PNGAnimation {GraphInfo}: {e.Message}");
                    return null;
                }
            }
        }
        /// <summary>
        /// 修改最后使用时间为当前时间，以便在清理空闲缓存时判断是否需要清理
        /// </summary>
        public void Touch() => Interlocked.Exchange(ref LastUseTimeTicks, DateTime.UtcNow.Ticks);


        public void CleanupIdleCache(long cleanTicks)
        {
            if (Control?.PlayState == true)
                return;
            if (Frames == null)
                return;
            long lastUse = Interlocked.Read(ref LastUseTimeTicks);
            if (cleanTicks < lastUse)
                return;

            lock (FramesLock)
            {
                Frames = null;
            }
        }

        public void Dispose()
        {
            Animations.Clear();
            FrameRects = [];
            lock (FramesLock)
            {
                Frames = null;
            }
            //GraphCore = null;
        }
    }
}
