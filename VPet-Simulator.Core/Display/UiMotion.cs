using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VPet_Simulator.Core
{
    /// <summary>
    /// V-Max : micro-animations de l'interface (apparitions douces).
    /// Respecte le réglage Windows « Afficher les animations » (SystemParameters.ClientAreaAnimation).
    /// </summary>
    public static class UiMotion
    {
        private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        /// <summary>
        /// Les animations de l'interface sont-elles autorisées par Windows ?
        /// </summary>
        public static bool Enabled => SystemParameters.ClientAreaAnimation;

        /// <summary>
        /// Apparition : fondu + légère mise à l'échelle depuis <paramref name="origin"/> (ex. 0.5,1 = bas centre)
        /// </summary>
        public static void PopIn(UIElement element, Point origin, double fromScale = 0.94, int durationMs = 200)
        {
            if (!Enabled)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
                return;
            }
            var scale = EnsureScale(element, origin);
            var d = TimeSpan.FromMilliseconds(durationMs);
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs * 0.7)) { EasingFunction = Ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(fromScale, 1, d) { EasingFunction = Ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(fromScale, 1, d) { EasingFunction = Ease });
        }

        /// <summary>
        /// Glissement vertical + fondu (barres, menus)
        /// </summary>
        public static void SlideIn(UIElement element, double fromY = 10, int durationMs = 200)
        {
            if (!Enabled)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
                return;
            }
            if (element.RenderTransform is not TranslateTransform tt || tt.IsFrozen)
            {
                tt = new TranslateTransform();
                element.RenderTransform = tt;
            }
            var d = TimeSpan.FromMilliseconds(durationMs);
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = Ease });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, d) { EasingFunction = Ease });
        }

        private static ScaleTransform EnsureScale(UIElement element, Point origin)
        {
            element.RenderTransformOrigin = origin;
            if (element.RenderTransform is ScaleTransform st && !st.IsFrozen)
                return st;
            st = new ScaleTransform();
            element.RenderTransform = st;
            return st;
        }
    }
}
