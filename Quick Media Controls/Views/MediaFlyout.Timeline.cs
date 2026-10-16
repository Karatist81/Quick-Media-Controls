using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Windows.Media;
using Windows.Media.Control;

namespace Quick_Media_Controls
{
    /// <summary>
    /// Timeline / repeat / shuffle logic for the flyout, kept in a separate partial
    /// so MediaFlyout.xaml.cs stays untouched. The position timer runs only while
    /// the flyout is loaded AND media is playing (idle footprint ~0).
    /// </summary>
    public partial class MediaFlyout
    {
        private DispatcherTimer? _timelineTimer;
        private bool _isSeeking;

        private void TimelineRoot_Loaded(object sender, RoutedEventArgs e)
        {
            _timelineTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timelineTimer.Tick += (_, _) => RefreshTimelineUi();
            _sessionManager.TimelineChanged += OnTimelineChanged;
            _sessionManager.PlaybackInfoChanged += OnPlaybackInfoChangedForTimeline;
            SyncTimerWithPlayback();
            RefreshTimelineUi();
        }

        private void TimelineRoot_Unloaded(object sender, RoutedEventArgs e)
        {
            _timelineTimer?.Stop();
            _sessionManager.TimelineChanged -= OnTimelineChanged;
            _sessionManager.PlaybackInfoChanged -= OnPlaybackInfoChangedForTimeline;
        }

        private void OnTimelineChanged(object? sender, EventArgs e) =>
            Dispatcher.InvokeAsync(RefreshTimelineUi);

        private void OnPlaybackInfoChangedForTimeline(object? sender, GlobalSystemMediaTransportControlsSessionPlaybackInfo e) =>
            Dispatcher.InvokeAsync(SyncTimerWithPlayback);

        private void SyncTimerWithPlayback()
        {
            if (_timelineTimer == null) return;
            if (_sessionManager.IsPlaying()) _timelineTimer.Start();
            else { _timelineTimer.Stop(); RefreshTimelineUi(); }
        }

        private void RefreshTimelineUi()
        {
            if (_isSeeking) return;
            var (pos, dur) = _sessionManager.GetTimeline();
            TimelineSlider.Maximum = dur.TotalSeconds > 0 ? dur.TotalSeconds : 0;
            TimelineSlider.Value = pos.TotalSeconds;
            TimelineSlider.IsEnabled = dur.TotalSeconds > 0;
            PositionText.Text = pos.ToString(@"m\:ss");
            DurationText.Text = dur.ToString(@"m\:ss");

            var mode = _sessionManager.GetRepeatModeForUi();
            RepeatIcon.Text = mode == MediaPlaybackAutoRepeatMode.Track ? "🔂" : "🔁";
            RepeatButton.Opacity = mode == MediaPlaybackAutoRepeatMode.None ? 0.45 : 1.0;
            ShuffleButton.Opacity = _sessionManager.GetShuffleForUi() == true ? 1.0 : 0.45;
        }

        private void TimelineSlider_DragStarted(object sender, DragStartedEventArgs e) => _isSeeking = true;

        private async void TimelineSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isSeeking = false;
            await _sessionManager.SeekAsync(TimeSpan.FromSeconds(TimelineSlider.Value));
            RefreshTimelineUi();
        }

        private async void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            await _sessionManager.CycleRepeatAsync();
            RefreshTimelineUi();
        }

        private async void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            await _sessionManager.ToggleShuffleAsync();
            RefreshTimelineUi();
        }
    }
}