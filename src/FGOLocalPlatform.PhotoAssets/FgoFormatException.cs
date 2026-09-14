using System;

namespace FGOLocalPlatform.PhotoAssets;

public sealed class FgoFormatException : Exception
{
	public FgoFormatException(string message)
		: base(message)
	{
	}

	public FgoFormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
