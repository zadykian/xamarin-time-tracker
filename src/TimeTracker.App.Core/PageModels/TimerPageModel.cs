﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;
using TimeTracker.App.Core.PageModels.Base;
using TimeTracker.App.Core.Services.Notifications;
using TimeTracker.App.Core.Services.PhotoCapturing;
using TimeTracker.App.Core.Services.UserLocation;
using TimeTracker.App.Core.ViewModels;
using TimeTracker.Services.Account;
using TimeTracker.Services.Images;
using TimeTracker.Services.Models;
using TimeTracker.Services.TimeTracking;

namespace TimeTracker.App.Core.PageModels
{
	/// <summary>
	/// Timer page model.
	/// </summary>
	internal class TimerPageModel : PageModelBase
	{
		private readonly Timer generalTimer;
		private readonly Timer notificationTimer;

		private readonly ViewAllPageModel viewAllPageModel;

		private readonly ITrackedPeriodService trackedPeriodService;
		private readonly IImageService imageService;
		private readonly IAccountService accountService;

		private readonly ILocationService locationService;
		private readonly IPhotoCapturingService photoCapturingService;
		private readonly INotificationService notificationService;

		public TimerPageModel(
			ITrackedPeriodService trackedPeriodService,
			IAccountService accountService,
			IImageService imageService,
			ILocationService locationService,
			IPhotoCapturingService photoCapturingService,
			INotificationService notificationService,
			ViewAllPageModel viewAllPageModel)
		{
			this.trackedPeriodService = trackedPeriodService;
			this.accountService = accountService;
			this.imageService = imageService;

			this.locationService = locationService;
			this.photoCapturingService = photoCapturingService;
			this.notificationService = notificationService;

			this.viewAllPageModel = viewAllPageModel;

			TimerButtonViewModel = new ButtonViewModel("start timer", OnTimerButtonPressed);
			AttachPhotoButtonViewModel = new ButtonViewModel("attach photo", OnAttachPhotoButtonClicked, isEnabled: false);

			generalTimer = new Timer {Interval = 1000};
			generalTimer.Elapsed += (sender, args) => RunningTotal += TimeSpan.FromSeconds(1);

			notificationTimer = new Timer {Interval = TimeSpan.FromMinutes(1).TotalMilliseconds};
			notificationTimer.Elapsed += async (sender, args) => await OnNotificationTimerElapsed();
		}

		private bool timerIsStarted;

		public bool TimerIsStarted
		{
			get => timerIsStarted;
			private set => SetProperty(ref timerIsStarted, value);
		}

		private TimeSpan runningTotal;

		public TimeSpan RunningTotal
		{
			get => runningTotal;
			private set => SetProperty(ref runningTotal, value);
		}

		private DateTime currentStartTime;

		public DateTime CurrentStartTime
		{
			get => currentStartTime;
			private set => SetProperty(ref currentStartTime, value);
		}

		private ButtonViewModel timerButtonViewModel;

		public ButtonViewModel TimerButtonViewModel
		{
			get => timerButtonViewModel;
			private set => SetProperty(ref timerButtonViewModel, value);
		}

		private ButtonViewModel attachPhotoButtonViewModel;

		public ButtonViewModel AttachPhotoButtonViewModel
		{
			get => attachPhotoButtonViewModel;
			private set => SetProperty(ref attachPhotoButtonViewModel, value);
		}

		private async void OnTimerButtonPressed()
		{
			if (TimerIsStarted) await OnTimerStopRequest();
			else await OnTimerStartRequest();
		}

		private async Task OnTimerStartRequest()
		{
			if (TimerIsStarted)
			{
				return;
			}

			TimerButtonViewModel.IsEnabled = false;
			try
			{
				generalTimer.Start();
				TimerIsStarted = true;

				CurrentStartTime = DateTime.Now;
				TimerButtonViewModel.Text = "stop timer";

				var currentLocation = await locationService.GetCurrentLocationAsync();
				var periodLocation = new Geolocation(currentLocation.Latitude, currentLocation.Longitude);
				var newTrackedPeriod = new TrackedPeriod(accountService.CurrentUser.Id!.Value, CurrentStartTime, periodLocation);

				viewAllPageModel.AllForCurrentUser.Insert(0, newTrackedPeriod);
				await trackedPeriodService.UpsertAsync(newTrackedPeriod);

				notificationTimer.Start();
				AttachPhotoButtonViewModel.IsEnabled = true;
			}
			finally
			{
				TimerButtonViewModel.IsEnabled = true;
			}
		}

		private async Task OnTimerStopRequest()
		{
			var isFingerprintAvailable = await CrossFingerprint.Current.IsAvailableAsync();

			if (!isFingerprintAvailable)
			{
				await StopTimer();
				return;
			}

			var authRequestConfig = new AuthenticationRequestConfiguration(
				"Authentication",
				"Authenticate access to your personal data");
			var authResult = await CrossFingerprint.Current.AuthenticateAsync(authRequestConfig);

			if (authResult.Authenticated)
			{
				await StopTimer();
			}
		}

		/// <summary>
		/// Stop timer.
		/// </summary>
		public async ValueTask StopTimer()
		{
			if (!TimerIsStarted)
			{
				return;
			}

			TimerButtonViewModel.IsEnabled = false;
			try
			{
				generalTimer.Stop();
				notificationTimer.Stop();
				TimerIsStarted = false;

				RunningTotal = TimeSpan.Zero;
				TimerButtonViewModel.Text = "start timer";
				AttachPhotoButtonViewModel.IsEnabled = false;

				var currentPeriod = await trackedPeriodService.GetCurrentAsync(accountService.CurrentUser.Id!.Value);
				currentPeriod.End = DateTime.Now;
				await trackedPeriodService.UpsertAsync(currentPeriod);
				viewAllPageModel.AllForCurrentUser.RemoveAt(0);
				viewAllPageModel.AllForCurrentUser.Insert(0, currentPeriod);
			}
			finally
			{
				TimerButtonViewModel.IsEnabled = true;
			}
		}

		private async Task OnNotificationTimerElapsed()
		{
			var currentPeriod = await trackedPeriodService.GetCurrentAsync(accountService.CurrentUser.Id!.Value);
			await notificationService.PushNotificationAsync(currentPeriod.Total);
		}

		private async void OnAttachPhotoButtonClicked()
		{
			var imageContent = await photoCapturingService.CapturePhotoAsync();

			if (!imageContent.Any())
			{
				return;
			}

			var currentTrackedPeriod = await trackedPeriodService.GetCurrentAsync(accountService.CurrentUser.Id!.Value);
			var image = new Image(imageContent, currentTrackedPeriod.Id!.Value);
			await imageService.AddImageAsync(image);
		}
	}
}