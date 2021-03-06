﻿using FluentAssertions;
using NUnit.Framework;
using Tailviewer.BusinessLogic.Analysis;
using Tailviewer.BusinessLogic.Plugins;

namespace Tailviewer.Test.BusinessLogic.Plugins
{
	[TestFixture]
	public sealed class DataSourceAnalyserPluginTest
	{
		[Test]
		public void TestGetDataSourceAnalyserPluginVersion()
		{
			PluginInterfaceVersionAttribute.GetInterfaceVersion(typeof(IDataSourceAnalyserPlugin)).Should().Be(new PluginInterfaceVersion(2),
			                                                                                                   "because this interface has been modified once");
		}
	}
}
