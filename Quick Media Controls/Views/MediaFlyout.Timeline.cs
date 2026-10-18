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
        private bool _suppressValueEvent;
        private DateTime _lastUserSeekUtc = DateTime.MinValue;
        private const int UserSeekCooldownMs = 800;

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
            _suppressValueEvent = true;
            TimelineSlider.Value = pos.TotalSeconds;
            _suppressValueEvent = false;
            TimelineSlider.IsEnabled = dur.TotalSeconds > 0;
            PositionText.Text = pos.ToString(@"m\:ss");
            DurationText.Text = dur.ToString(@"m\:ss");

            var mode = _sessionManager.GetRepeatModeForUi();
            RepeatIcon.Text = mode == MediaPlaybackAutoRepeatMode.Track ? "🔂" : "🔁";
            RepeatButton.Opacity = mode == MediaPlaybackAutoRepeatMode.None ? 0.45 : 1.0;
            ShuffleButton.Opacity = _sessionManager.GetShuffleForUi() == true ? 1.0 : 0.45;
        }

        // Единая точка seek для drag и клика, с анти-спамом
        private async void DoUserSeek(double seconds)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastUserSeekUtc).TotalMilliseconds < UserSeekCooldownMs) return;
            _lastUserSeekUtc = now;
            await _sessionManager.SeekAsync(TimeSpan.FromSeconds(seconds));
            RefreshTimelineUi();
        }

        private void TimelineSlider_DragStarted(object sender, DragStartedEventArgs e) => _isSeeking = true;

        private void TimelineSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isSeeking = false;
            DoUserSeek(TimelineSlider.Value);
        }

        // Клик по дорожке (IsMoveToPointEnabled двигает thumb -> приходит DragCompleted,
        // но если WPF-UI съест событие, ловим через ValueChanged без дебаунса)
        private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSeeking || _suppressValueEvent) return;
            DoUserSeek(e.NewValue);
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