﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Metrolib;
using Tailviewer.BusinessLogic.ActionCenter;
using Tailviewer.BusinessLogic.AutoUpdates;
using Tailviewer.Ui.Controls;
using Tailviewer.Ui.ViewModels;
using log4net;
using Tailviewer.Archiver.Plugins;
using Tailviewer.BusinessLogic.Analysis;
using Tailviewer.BusinessLogic.LogFiles;
using Tailviewer.BusinessLogic.Plugins;
using Tailviewer.Core;
using Tailviewer.Core.Analysis;
using Tailviewer.Core.Analysis.Layouts;
using Tailviewer.Core.Settings;
using Tailviewer.Ui.Controls.MainPanel.Analyse.Widgets.Help;
using ApplicationSettings = Tailviewer.Settings.ApplicationSettings;
using DataSources = Tailviewer.BusinessLogic.DataSources.DataSources;
using QuickFilters = Tailviewer.BusinessLogic.Filters.QuickFilters;

namespace Tailviewer
{
	public class App
		: Application
	{
		private static readonly ILog Log =
			LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public App()
		{
			Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Metrolib;component/Themes/Generic.xaml") });
		}

		public static int Start(SingleApplicationHelper.IMutex mutex, string[] args)
		{
			try
			{
				return StartInternal(mutex, args);
			}
			catch (Exception e)
			{
				Log.ErrorFormat("Caught unexpected exception: {0}", e);
				return -1;
			}
		}

		private static int StartInternal(SingleApplicationHelper.IMutex mutex, string[] args)
		{
			InstallExceptionHandlers();
			Log.InfoFormat("Starting tailviewer...");
			Log.InfoFormat("Commandline arguments: {0}", string.Join(" ", args));
			LogEnvironment();

			ApplicationSettings settings = ApplicationSettings.Create();
			settings.Restore(out var neededPatching);

			if (neededPatching)
			{
				// TODO: Save settings right again to complete the upgrade
				//       (maybe we should preserve an old version)
			}

			var actionCenter = new ActionCenter();
			using (var taskScheduler = new DefaultTaskScheduler())
			using (var serialTaskScheduler = new SerialTaskScheduler())
			{
				var filesystem = new Filesystem(serialTaskScheduler);
				using (var pluginArchiveLoader = new PluginArchiveLoader(filesystem, Constants.PluginPath))
				{
					var pluginSystem = CreatePluginSystem(pluginArchiveLoader);

					var fileFormatPlugins = pluginSystem.LoadAllOfType<IFileFormatPlugin>();

					var logFileFactory = new PluginLogFileFactory(taskScheduler, fileFormatPlugins);
					using (var dataSources = new DataSources(logFileFactory, taskScheduler, settings.DataSources))
					using (var updater = new AutoUpdater(actionCenter, settings.AutoUpdate))
					using (var logAnalyserEngine = new LogAnalyserEngine(taskScheduler, pluginSystem))
					using (var dataSourceAnalyserEngine = new DataSourceAnalyserEngine(taskScheduler, logAnalyserEngine, pluginSystem))
					using (var analysisStorage = new AnalysisStorage(taskScheduler, filesystem, dataSourceAnalyserEngine, CreateTypeFactory(pluginSystem)))
					{
						var arguments = ArgumentParser.TryParse(args);
						if (arguments.FileToOpen != null)
						{
							if (File.Exists(arguments.FileToOpen))
							{
								// Not only do we want to add this file to the list of data sources,
								// but we also want to select it so the user can view it immediately, regardless
								// of what was selected previously.
								var dataSource = dataSources.AddDataSource(arguments.FileToOpen);
									settings.DataSources.SelectedItem = dataSource.Id;
								}
								else
								{
									Log.ErrorFormat("File '{0}' does not exist, won't open it!", arguments.FileToOpen);
								}
							}

							if (settings.AutoUpdate.CheckForUpdates)
							{
								// Our initial check for updates is not due to a user action
								// and therefore we don't need to show a notification when the
								// application is up-to-date.
								updater.CheckForUpdates(addNotificationWhenUpToDate: false);
							}

							var quickFilters = new QuickFilters(settings.QuickFilters);
							actionCenter.Add(Build.Current);
							actionCenter.Add(Change.Merge(Changelog.MostRecentPatches));
						var application = new App();
						var dispatcher = Dispatcher.CurrentDispatcher;
						var uiDispatcher = new UiDispatcher(dispatcher);
						dispatcher.UnhandledException += actionCenter.ReportUnhandledException;
						TaskScheduler.UnobservedTaskException += actionCenter.ReportUnhandledException;

						var window = new MainWindow(settings)
						{
							DataContext = new MainWindowViewModel(settings,
							                                      dataSources,
							                                      quickFilters,
							                                      actionCenter,
							                                      updater,
							                                      taskScheduler,
							                                      analysisStorage,
							                                      uiDispatcher,
							                                      pluginSystem)
						};

						settings.MainWindow.RestoreTo(window);

						window.Show();
						mutex.SetListener(window);

						return application.Run();
					}
				}
			}
		}

		private static IPluginLoader CreatePluginSystem(params IPluginLoader[] pluginLoaders)
		{
			// Currently, we deploy some well known "plugins" via the installer and they're
			// not available as *.tvp files just yet (which means the PluginArchiveLoader won't find them).
			// Therefore we register those at a PluginRegistry.
			var wellKnownPlugins = LoadWellKnownPlugins();

			// Even though we're dealing with the limitation above, the rest of the application should not need
			// to care, which is why we make both of those types of plugin accessible from one loader
			var loaders = new List<IPluginLoader>(pluginLoaders);
			loaders.Add(wellKnownPlugins);
			var pluginLoader = new AggregatedPluginLoader(loaders);

			// Last but not least, the PluginArchiveLoader doesn't cache anything which means
			// that multiple Load requests would result in the same plugin being loaded many times.
			// we don't want that (unnecessary work, waste of CPU time, etc..), so that's why there's a cache.
			var pluginCache = new PluginCache(pluginLoader);
			return pluginCache;
		}

		private static IPluginLoader LoadWellKnownPlugins()
		{
			var registry = new PluginRegistry();
			registry.Register(new HelpWidgetPlugin());
			return registry;
		}

		private static ITypeFactory CreateTypeFactory(IPluginLoader pluginLoader)
		{
			var factory = new TypeFactory();
			foreach (var pair in pluginLoader.ResolveSerializableTypes())
			{
				factory.Add(pair.Key, pair.Value);
			}
			factory.Add<AnalysisTemplate>();
			factory.Add<AnalysisViewTemplate>();
			factory.Add<ActiveAnalysisConfiguration>();
			factory.Add<AnalyserTemplate>();
			factory.Add<PageTemplate>();
			factory.Add<HorizontalWidgetLayoutTemplate>();
			factory.Add<ColumnWidgetLayoutTemplate>();
			factory.Add<RowWidgetLayoutTemplate>();
			factory.Add<WidgetTemplate>();
			factory.Add<Core.Settings.QuickFilters>();
			factory.Add<QuickFilter>();
			factory.Add<QuickFilterId>();
			return factory;
		}

		private static void LogEnvironment()
		{
			var builder = new StringBuilder();
			builder.AppendLine();
			builder.AppendFormat("Tailviewer: v{0}, {1}\r\n",
			                     FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion,
			                     Environment.Is64BitProcess ? "64bit" : "32bit");
			builder.AppendFormat(".NET Environment: {0}\r\n", Environment.Version);
			builder.AppendFormat("Operating System: {0}, {1}\r\n",
			                     Environment.OSVersion,
			                     Environment.Is64BitOperatingSystem ? "64bit" : "32bit");
			builder.AppendFormat("Current directory: {0}", Directory.GetCurrentDirectory());

			Log.InfoFormat("Environment: {0}", builder);
		}

		private static void InstallExceptionHandlers()
		{
			AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
		}

		private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs args)
		{
			object exception = args.ExceptionObject;

			Log.ErrorFormat("Caught unhandled exception in AppDomain: {0}", exception);

			MessageBox.Show(string.Format("Oops, something went wrong:\r\n{0}", exception),
			                Constants.MainWindowTitle);
		}
	}
}