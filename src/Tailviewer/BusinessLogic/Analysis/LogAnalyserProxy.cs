﻿using System;
using System.Reflection;
using System.Threading;
using log4net;
using Tailviewer.BusinessLogic.LogFiles;

namespace Tailviewer.BusinessLogic.Analysis
{
	/// <summary>
	///     Represents a (user defined) analysis of a data source.
	///     Encapsulates an underlying <see cref="ILogAnalyser" />, forwards its result
	///     and hides all of its (possible) failures.
	/// </summary>
	public sealed class LogAnalyserProxy
		: IDataSourceAnalysisHandle
		, IDisposable
	{
		private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		private readonly object _syncRoot;
		private ILogAnalyser _analyser;
		private readonly ILogAnalyserConfiguration _configuration;
		private readonly ILogFile _logFile;
		private readonly ITaskScheduler _scheduler;
		private readonly ILogAnalyserPlugin _plugin;
		private readonly IDataSourceAnalysisListener _listener;
		private IPeriodicTask _task;

		private ILogAnalysisResult _result;
		private bool _isDisposed;
		private Percentage _progress;

		public LogAnalyserProxy(ITaskScheduler scheduler,
			ILogFile logFile,
			ILogAnalyserPlugin plugin,
			ILogAnalyserConfiguration configuration,
			IDataSourceAnalysisListener listener)
		{
			if (scheduler == null)
				throw new ArgumentNullException(nameof(scheduler));
			if (logFile == null)
				throw new ArgumentNullException(nameof(logFile));
			if (plugin == null)
				throw new ArgumentNullException(nameof(plugin));
			if (listener == null)
				throw new ArgumentNullException(nameof(listener));

			_syncRoot = new object();
			_scheduler = scheduler;
			_logFile = logFile;
			_plugin = plugin;
			_configuration = configuration;
			_listener = listener;
		}

		public ILogAnalysisResult Result
		{
			get { return _result; }
			private set
			{
				if (Equals(_result, value))
					return;

				_result = value;
				_listener.OnAnalysisResultChanged(this, value);
			}
		}

		public Percentage Progress
		{
			set
			{
				if (_progress == value)
					return;

				_progress = value;
				_listener.OnProgress(this, value);
			}
		}

		public void Start()
		{
			lock (_syncRoot)
			{
				if (_isDisposed)
				{
					Log.WarnFormat("Ignoring Start(): This analysis has already been disposed of");
					return;
				}

				_analyser = TryCreateAnalyser();
				_task = _scheduler.StartPeriodic(OnUpdate, TimeSpan.FromSeconds(0.5), "");
			}
		}

		public void Dispose()
		{
			lock (_syncRoot)
			{
				_scheduler.StopPeriodic(_task);
				_analyser?.Dispose();
				_isDisposed = true;
			}
		}

		private ILogAnalyser TryCreateAnalyser()
		{
			try
			{
				var analyser = _plugin.Create(_scheduler, _logFile, _configuration);
				return analyser;
			}
			catch (Exception e)
			{
				Log.ErrorFormat("Caught unexpected exception while trying to create analyser '{0}': {1}",
					_plugin.Id,
					e);
				return null;
			}
		}

		private void OnUpdate()
		{
			try
			{
				Result = _analyser.Result;
				Progress = _analyser.Progress;
			}
			catch (Exception e)
			{
				Log.ErrorFormat("Caught unexpected exception while trying to fetch analysis result '{0}': {1}",
					_plugin.Id,
					e);
			}
		}
	}
}