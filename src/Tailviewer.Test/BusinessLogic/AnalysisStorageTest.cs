﻿using System;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Tailviewer.BusinessLogic.Analysis;
using Tailviewer.Core;
using Tailviewer.Core.Analysis;

namespace Tailviewer.Test.BusinessLogic
{
	[TestFixture]
	public sealed class AnalysisStorageTest
	{
		private ManualTaskScheduler _taskScheduler;
		private InMemoryFilesystem _filesystem;
		private Mock<IDataSourceAnalyserEngine> _dataSourceAnalyserEngine;
		private TypeFactory _typeFactory;

		[SetUp]
		public void Setup()
		{
			_taskScheduler = new ManualTaskScheduler();
			_filesystem = new InMemoryFilesystem(new ImmediateTaskScheduler());

			var root = Path.GetPathRoot(Constants.AnalysisDirectory);
			_filesystem.AddRoot(root);

			_dataSourceAnalyserEngine = new Mock<IDataSourceAnalyserEngine>();

			_typeFactory = new TypeFactory();
			_typeFactory.Add<AnalysisTemplate>();
			_typeFactory.Add<AnalysisViewTemplate>();
			_typeFactory.Add<ActiveAnalysisConfiguration>();
			_typeFactory.Add<AnalyserTemplate>();
			_typeFactory.Add<PageTemplate>();
			_typeFactory.Add<WidgetTemplate>();
		}

		[Test]
		public void TestCreateAnalysisFolder()
		{
			string analysisFolderPath = Constants.AnalysisDirectory;
			_filesystem.DirectoryExists(analysisFolderPath).Result.Should().BeFalse();

			var storage = new AnalysisStorage(_taskScheduler,
			                                  _filesystem,
			                                  _dataSourceAnalyserEngine.Object,
			                                  _typeFactory);

			_filesystem.DirectoryExists(analysisFolderPath).Result.Should().BeTrue("because the storage should've created the analysis folder");
		}

		[Test]
		public void TestCreateAnalysis1()
		{
			var storage = new AnalysisStorage(_taskScheduler,
			                                  _filesystem,
			                                  _dataSourceAnalyserEngine.Object,
			                                  _typeFactory);
			storage.Analyses.Should().BeEmpty();

			var analysis = storage.CreateAnalysis(new AnalysisTemplate(), new AnalysisViewTemplate());
			analysis.Should().NotBeNull();
			analysis.Should().BeOfType<ActiveAnalysis>();

			storage.Analyses.Should().BeEquivalentTo(new object[] {analysis});
		}

		[Test]
		[Description("Verifies that creating a new analysis immediately writes it to disk")]
		public void TestCreateAnalysis2()
		{
			var storage = new AnalysisStorage(_taskScheduler,
			                                   _filesystem,
			                                   _dataSourceAnalyserEngine.Object,
			                                  _typeFactory);
			var analysis = storage.CreateAnalysis(new AnalysisTemplate(), new AnalysisViewTemplate());

			var id = analysis.Id;
			var filename = Path.Combine(Constants.AnalysisDirectory, string.Format("{0}.{1}", id, Constants.AnalysisExtension));
			_filesystem.FileExists(filename).Result.Should().BeTrue("because the storage should've created a new file on disk");
		}

		[Test]
		[Description("Verifies that creating a new analysis immediately writes it to disk")]
		public void TestCreateAnalysis3()
		{
			var storage = new AnalysisStorage(_taskScheduler,
			                                  _filesystem,
			                                  _dataSourceAnalyserEngine.Object,
			                                  _typeFactory);
			var analysisTemplate = new AnalysisTemplate();
			var analysisViewTemplate = new AnalysisViewTemplate();
			var analysis = storage.CreateAnalysis(analysisTemplate, analysisViewTemplate);

			analysisViewTemplate.Add(new PageTemplate{Title = "A great page"});

			var task = storage.SaveAsync(analysis.Id);
			task.Should().NotBeNull();
			task.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("because the modified template should've been saved");

			var anotherStorage = new AnalysisStorage(_taskScheduler,
			                                  _filesystem,
			                                  _dataSourceAnalyserEngine.Object,
			                                  _typeFactory);
			anotherStorage.AnalysisTemplates.Should().HaveCount(1, "because we've created and saved one template");
			var template = anotherStorage.AnalysisTemplates.First();
			template.Should().NotBeNull();
			template.ViewTemplate.Pages.Should().HaveCount(1, "because we've added one page to that template");
			var pageTemplate = template.ViewTemplate.Pages.First();
			pageTemplate.Should().NotBeNull();
			pageTemplate.Title.Should().Be("A great page");
		}

		[Test]
		public void TestRemove1()
		{
			var storage = new AnalysisStorage(_taskScheduler,
			                                   _filesystem,
			                                   _dataSourceAnalyserEngine.Object,
			                                  _typeFactory);
			storage.AnalysisTemplates.Should().BeEmpty();
			var analysis = storage.CreateAnalysis(new AnalysisTemplate(), new AnalysisViewTemplate());
			storage.AnalysisTemplates.Should().HaveCount(1);
			IAnalysis unused;
			storage.TryGetAnalysisFor(analysis.Id, out unused).Should().BeTrue();

			storage.Remove(analysis.Id);
			storage.AnalysisTemplates.Should().BeEmpty("because the only analysis should've been removed");
			storage.TryGetAnalysisFor(analysis.Id, out unused).Should().BeFalse("because the only analysis should've been removed");
		}

		[Test]
		[Description("Verifies that the running analysis is disposed of when it's removed")]
		public void TestRemove2()
		{
			var storage = new AnalysisStorage(_taskScheduler,
			                                   _filesystem,
			                                   _dataSourceAnalyserEngine.Object,
			                                  _typeFactory);
			var analysis = storage.CreateAnalysis(new AnalysisTemplate(), new AnalysisViewTemplate());
			((ActiveAnalysis)analysis).IsDisposed.Should().BeFalse();

			storage.Remove(analysis.Id);
			((ActiveAnalysis)analysis).IsDisposed.Should().BeTrue("because the storage created the analysis so it should dispose of it as well");
		}

		[Test]
		[Description("Verifies that the analysis is also removed from disk")]
		public void TestRemove3()
		{
			var storage = new AnalysisStorage(_taskScheduler,
			                                   _filesystem,
			                                   _dataSourceAnalyserEngine.Object,
			                                  _typeFactory);
			var analysis = storage.CreateAnalysis(new AnalysisTemplate(), new AnalysisViewTemplate());
			((ActiveAnalysis)analysis).IsDisposed.Should().BeFalse();

			var file = _filesystem.GetFileInfo(AnalysisStorage.GetFilename(analysis.Id));
			file.Exists.Result.Should().BeTrue("because CreateAnalysis() should've written the analysis to disk");

			storage.Remove(analysis.Id);
			file.Exists.Result.Should().BeFalse("because Remove should've also removed the analysis from disk");
		}

		[Test]
		public void TestRestoreSavedAnalysis()
		{
			AnalysisId id;
			{
				var storage = new AnalysisStorage(_taskScheduler,
				                                  _filesystem,
				                                  _dataSourceAnalyserEngine.Object,
				                                  _typeFactory);
				var analysis = storage.CreateAnalysis(new AnalysisTemplate(), new AnalysisViewTemplate());
				id = analysis.Id;

				var file = _filesystem.GetFileInfo(AnalysisStorage.GetFilename(analysis.Id));
				file.Exists.Result.Should().BeTrue("because CreateAnalysis() should've written the analysis to disk");
			}

			{
				var storage = new AnalysisStorage(_taskScheduler,
				                                  _filesystem,
				                                  _dataSourceAnalyserEngine.Object,
				                                  _typeFactory);
				storage.Analyses.Should().HaveCount(1);
				storage.AnalysisTemplates.Should().HaveCount(1);

				var analysis = storage.Analyses.First();
				analysis.Should().NotBeNull();
				analysis.Id.Should().Be(id);
				storage.TryGetAnalysisFor(analysis.Id, out var actualAnalysis).Should().BeTrue();
				actualAnalysis.Should().BeSameAs(analysis);

				storage.TryGetTemplateFor(analysis.Id, out var configuration).Should().BeTrue();
				configuration.Should().NotBeNull();
				configuration.Template.Should().NotBeNull();
				configuration.ViewTemplate.Should().NotBeNull();
			}
		}
	}
}