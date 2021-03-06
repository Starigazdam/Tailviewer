﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tailviewer.BusinessLogic.LogFiles;
using Tailviewer.Core.Analysis;
using Tailviewer.Core.LogFiles;

namespace Tailviewer.BusinessLogic.Analysis
{
	public sealed class ActiveAnalysis
		: IAnalysis
		, IDisposable
	{
		private readonly AnalysisTemplate _template;
		private readonly Dictionary<IDataSourceAnalyser, AnalyserTemplate> _analysers;
		private readonly IDataSourceAnalyserEngine _analyserEngine;
		private readonly LogFileProxy _logFile;
		private readonly Dictionary<DataSourceId, ILogFile> _logFiles;
		private readonly TimeSpan _maximumWaitTime;
		private readonly object _syncRoot;
		private readonly ITaskScheduler _taskScheduler;
		private readonly AnalysisId _id;
		private bool _isDisposed;

		public ActiveAnalysis(
			AnalysisId id,
			AnalysisTemplate template,
			ITaskScheduler taskScheduler,
			IDataSourceAnalyserEngine analyserEngine,
			TimeSpan maximumWaitTime)
		{
			if (template == null)
				throw new ArgumentNullException(nameof(template));
			if (taskScheduler == null)
				throw new ArgumentNullException(nameof(taskScheduler));
			if (analyserEngine == null)
				throw new ArgumentNullException(nameof(analyserEngine));

			_id = id;
			_template = template;
			_taskScheduler = taskScheduler;
			_maximumWaitTime = maximumWaitTime;
			_logFiles = new Dictionary<DataSourceId, ILogFile>();
			_logFile = new LogFileProxy(taskScheduler, maximumWaitTime);
			_analyserEngine = analyserEngine;
			_analysers = new Dictionary<IDataSourceAnalyser, AnalyserTemplate>();
			_syncRoot = new object();

			foreach (var analysisTemplate in template.Analysers)
			{
				// TODO: Solve this conundrum
				Add((AnalyserTemplate) analysisTemplate);
			}
		}

		public AnalysisTemplate Template => _template;

		public IEnumerable<IDataSourceAnalyser> Analysers
		{
			get
			{
				lock (_syncRoot)
				{
					return _analysers.Keys.ToList();
				}
			}
		}

		public IEnumerable<ILogFile> LogFiles
		{
			get
			{
				lock (_syncRoot)
				{
					return _logFiles.Values.ToList();
				}
			}
		}

		public Percentage Progress
		{
			get
			{
				// TODO: Move this portion into a separate timer that updates at 10Hz or so...
				var progress = Percentage.Zero;
				lock (_syncRoot)
				{
					if (_analysers.Count == 0)
						return Percentage.HundredPercent;

					foreach (var analyser in _analysers.Keys)
					{
						var tmp = analyser.Progress;
						if (!Percentage.IsNan(tmp))
							progress += tmp;
					}

					return progress;
				}
			}
		}

		public bool IsFrozen => false;

		public AnalysisId Id => _id;

		public bool IsDisposed
		{
			get { return _isDisposed; }
		}

		/// <summary>
		/// </summary>
		/// <param name="pluginId"></param>
		/// <param name="configuration"></param>
		/// <returns></returns>
		public IDataSourceAnalyser Add(AnalyserPluginId pluginId, ILogAnalyserConfiguration configuration)
		{
			var template = new AnalyserTemplate
			{
				Id = AnalyserId.CreateNew(),
				AnalyserPluginId = pluginId,
				Configuration = configuration
			};

			var analyser = Add(template);

			_template.Add(template);

			return analyser;
		}

		private IDataSourceAnalyser Add(AnalyserTemplate template)
		{
			var analyser = _analyserEngine.CreateAnalyser(_logFile, template);
			try
			{
				lock (_syncRoot)
				{
					_analysers.Add(analyser, template);
					foreach (var pair in _logFiles)
					{
						analyser.OnLogFileAdded(pair.Key, pair.Value);
					}
				}

				return analyser;
			}
			catch (Exception)
			{
				analyser.Dispose();
				throw;
			}
		}

		/// <summary>
		/// </summary>
		/// <param name="analyser"></param>
		public void Remove(IDataSourceAnalyser analyser)
		{
			lock (_syncRoot)
			{
				AnalyserTemplate template;
				if (analyser != null && _analysers.TryGetValue(analyser, out template))
				{
					_template.Remove(template);
					_analysers.Remove(analyser);

					analyser.Dispose();
				}
			}
		}

		public bool TryGetAnalyser(AnalyserId analyserId, out IDataSourceAnalyser analyser)
		{
			lock (_syncRoot)
			{
				analyser = _analysers.Keys.FirstOrDefault(x => x.Id == analyserId);
				return analyser != null;
			}
		}

		public void Dispose()
		{
			lock (_syncRoot)
			{
				_analysers.Clear();
				_logFile.Dispose();
				_isDisposed = true;
			}
		}

		public void Add(DataSourceId id, ILogFile logFile)
		{
			lock (_syncRoot)
			{
				_logFiles.Add(id, logFile);
				UpdateProxy();
				NotifyAnalysersAddLogFile(id, logFile);
			}
		}

		public void Remove(DataSourceId id, ILogFile logFile)
		{
			lock (_syncRoot)
			{
				_logFiles.Remove(id);
				UpdateProxy();
				NotifyAnalysersRemoveLogFile(id, logFile);
			}
		}

		private void UpdateProxy()
		{
			var merged = new MergedLogFile(_taskScheduler,
				_maximumWaitTime,
				_logFiles.Values);
			_logFile.InnerLogFile = merged;
		}

		private void NotifyAnalysersAddLogFile(DataSourceId id, ILogFile logFile)
		{
			// Not every analyser works with the proxy, some prefer
			// to analyse each selected log file on its own, therefore
			// we have to notify them that a log file has been added
			foreach (var analyser in _analysers.Keys)
			{
				analyser.OnLogFileAdded(id, logFile);
			}
		}

		private void NotifyAnalysersRemoveLogFile(DataSourceId id, ILogFile logFile)
		{
			// Not every analyser works with the proxy, some prefer
			// to analyse each selected log file on its own, therefore
			// we have to notify them that a log file has been removed
			foreach (var analyser in _analysers.Keys)
			{
				analyser.OnLogFileRemoved(id, logFile);
			}
		}

		/// <summary>
		///     Creates a snapshot of this group's analysers.
		/// </summary>
		/// <returns></returns>
		public AnalysisSnapshot CreateSnapshot()
		{
			lock (_syncRoot)
			{
				var analysers = new List<DataSourceAnalyserSnapshot>(_analysers.Count);
				foreach (var analyser in _analysers.Keys)
					analysers.Add(CreateSnapshot(analyser));
				return new AnalysisSnapshot(Progress, analysers);
			}
		}

		private DataSourceAnalyserSnapshot CreateSnapshot(IDataSourceAnalyser analyser)
		{
			var configuration = analyser.Configuration?.Clone() as ILogAnalyserConfiguration;
			var result = analyser.Result?.Clone() as ILogAnalysisResult;
			var progress = Progress;
			return new DataSourceAnalyserSnapshot(analyser.Id,
			                                      analyser.AnalyserPluginId,
			                                      configuration,
			                                      result,
			                                      progress);
		}
	}
}