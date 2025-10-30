using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace NoteNest.UI.Helpers
{
    /// <summary>
    /// Custom animation for animating GridLength properties
    /// (DoubleAnimation doesn't work with GridLength)
    /// </summary>
    public class GridLengthAnimation : AnimationTimeline
    {
        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));

        public static readonly DependencyProperty EasingFunctionProperty =
            DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(GridLengthAnimation));

        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public IEasingFunction EasingFunction
        {
            get => (IEasingFunction)GetValue(EasingFunctionProperty);
            set => SetValue(EasingFunctionProperty, value);
        }

        public override Type TargetPropertyType => typeof(GridLength);

        protected override Freezable CreateInstanceCore()
        {
            return new GridLengthAnimation();
        }

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            double fromValue = From.Value;
            double toValue = To.Value;

            if (fromValue > toValue)
            {
                // Apply easing function if available
                double progress = animationClock.CurrentProgress ?? 0.0;
                if (EasingFunction != null)
                {
                    progress = EasingFunction.Ease(progress);
                }

                return new GridLength((1 - progress) * (fromValue - toValue) + toValue, GridUnitType.Pixel);
            }
            else
            {
                // Apply easing function if available
                double progress = animationClock.CurrentProgress ?? 0.0;
                if (EasingFunction != null)
                {
                    progress = EasingFunction.Ease(progress);
                }

                return new GridLength(progress * (toValue - fromValue) + fromValue, GridUnitType.Pixel);
            }
        }
    }
}
