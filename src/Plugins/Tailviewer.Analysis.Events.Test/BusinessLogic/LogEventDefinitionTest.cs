﻿using FluentAssertions;
using NUnit.Framework;
using Tailviewer.Analysis.Events.BusinessLogic;
using Tailviewer.BusinessLogic;
using Tailviewer.BusinessLogic.LogFiles;

namespace Tailviewer.Analysis.Events.Test.BusinessLogic
{
	[TestFixture]
	public sealed class LogEventDefinitionTest
	{
		[Test]
		public void TestTryExtractEventFrom1()
		{
			var definition = new LogEventDefinition(@"Found channel ([0-9]*\.?[0-9]MHz)");
			var values = definition.TryExtractEventFrom(new LogLine(0, 0, "4th April, 2017 Found channel 816MHz", LevelFlags.Info));
			values.Should().Equal("816MHz");
		}
	}
}