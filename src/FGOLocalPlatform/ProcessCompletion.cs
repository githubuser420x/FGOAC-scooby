using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

internal static class ProcessCompletion
{
	public static async Task WaitAsync(Process process, TimeSpan timeout, CancellationToken cancellation = default(CancellationToken))
	{
		using CancellationTokenSource deadline = new CancellationTokenSource(timeout);
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
		try
		{
			while (!process.HasExited)
			{
				await Task.Delay(100, linked.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException)
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill();
				}
			}
			catch (InvalidOperationException)
			{
			}
			if (cancellation.IsCancellationRequested)
			{
				throw;
			}
			throw new TimeoutException($"操作超过 {timeout.TotalSeconds:0} 秒，已结束控制命令。请查看 logs/server-control.log。");
		}
	}
}
