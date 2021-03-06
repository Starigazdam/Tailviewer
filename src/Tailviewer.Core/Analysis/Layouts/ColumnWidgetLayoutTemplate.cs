﻿using System;
using System.Diagnostics.Contracts;

namespace Tailviewer.Core.Analysis.Layouts
{
	/// <summary>
	///     The template for a column widget layout:
	///     Used to persist the settings of the layout in between sessions.
	/// </summary>
	public sealed class ColumnWidgetLayoutTemplate
		: IWidgetLayoutTemplate
	{
		/// <inheritdoc />
		public void Serialize(IWriter writer)
		{
		}

		/// <inheritdoc />
		public void Deserialize(IReader reader)
		{
		}

		object ICloneable.Clone()
		{
			return Clone();
		}

		/// <summary>
		///     Creates a deep clone of this template.
		/// </summary>
		/// <returns></returns>
		[Pure]
		public ColumnWidgetLayoutTemplate Clone()
		{
			return new ColumnWidgetLayoutTemplate();
		}

		/// <inheritdoc />
		public PageLayout PageLayout => PageLayout.Columns;
	}
}