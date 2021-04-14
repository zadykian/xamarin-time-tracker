﻿using System.Collections.Generic;
using System.Threading.Tasks;
using TimeTracker.Services.ConnectionFactory;
using TimeTracker.Services.Models;

namespace TimeTracker.Services.TimeTracking
{
	/// <summary>
	/// Tracked periods sub-service which is responsible for communication with SQLite database. 
	/// </summary>
	public class SqliteTrackedPeriodService : SqliteServiceBase, ITrackedPeriodService
	{
		public SqliteTrackedPeriodService(string databaseDirectoryPath) : base(databaseDirectoryPath)
		{
		}

		/// <inheritdoc />
		async Task ITrackedPeriodService.UpsertAsync(TrackedPeriod trackedPeriod)
		{
			var dbConnection = await Connection.Value;
			await dbConnection.InsertOrReplaceAsync(trackedPeriod);
		}

		/// <inheritdoc />
		async Task<TrackedPeriod> ITrackedPeriodService.GetCurrentAsync(int userId)
			=> await (await Connection.Value)
				.Table<TrackedPeriod>()
				.FirstOrDefaultAsync(period => period.UserId == userId && period.End == null);

		/// <inheritdoc />
		async Task<IReadOnlyCollection<TrackedPeriod>> ITrackedPeriodService.GetAllAsync(int userId)
			=> await (await Connection.Value)
				.Table<TrackedPeriod>()
				.Where(period => period.UserId == userId)
				.OrderByDescending(period => period.Start)
				.ToArrayAsync();

		/// <inheritdoc />
		async Task ITrackedPeriodService.ClearDataAsync(int userId)
		{
			var dbConnection = await Connection.Value;

			var notFinishedPeriod = await dbConnection
				.Table<TrackedPeriod>()
				.Where(period => period.UserId == userId && period.End == null)
				.OrderByDescending(period => period.Start)
				.FirstOrDefaultAsync();

			if (notFinishedPeriod is null)
			{
				await dbConnection.DeleteAllAsync<TrackedPeriod>();
				return;
			}

			await dbConnection
				.Table<TrackedPeriod>()
				.DeleteAsync(period => period.Id != notFinishedPeriod.Id);
		}

		/// <inheritdoc />
		async Task ITrackedPeriodService.AddImageAsync(Image image)
			=> await (await Connection.Value).InsertAsync(image);
	}
}