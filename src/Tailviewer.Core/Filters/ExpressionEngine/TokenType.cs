﻿namespace Tailviewer.Core.Filters.ExpressionEngine
{
	internal enum TokenType
	{
		Invalid = 0,

		Whitespace,

		OpenBracket,
		CloseBracket,

		#region Binary Operators
		Equals,
		NotEquals,
		LessThan,
		LessOrEquals,
		GreaterThan,
		GreaterOrEquals,
		And,
		Or,
		Contains,
		Is,
		#endregion

		#region Unary Operators
		Not,
		#endregion

		Quotation,
		BackwardSlash,
		Dollar,
		Literal,
		True,
		False
	}
}