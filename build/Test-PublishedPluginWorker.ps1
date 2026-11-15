param([Parameter(Mandatory)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new((Resolve-Path -LiteralPath $ExecutablePath).Path)
$start.Arguments = '--island-plugin-worker'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $errors = $process.StandardError.ReadToEndAsync()
    $request = @{ operation='init'; id='studio.release-smoke'; source='function onInit(){island.publish({text:"release-worker-ok",ttlMs:1000});} function onDispose(){island.clear();}' } | ConvertTo-Json -Compress
    $process.StandardInput.WriteLine($request)
    $process.StandardInput.Flush()
    $line = $process.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
    $reply = $line | ConvertFrom-Json
    if (-not $reply.ok -or $reply.content.text -ne 'release-worker-ok') { throw "Published plugin worker did not return expected content: $line" }
    $process.StandardInput.WriteLine('{"operation":"dispose"}')
    $process.StandardInput.Flush()
    $disposed = $process.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult() | ConvertFrom-Json
    if (-not $disposed.ok -or $null -ne $disposed.content) { throw 'Published plugin disposal failed.' }
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) { throw 'Published plugin worker did not exit normally.' }
    '{"PublishedPluginWorkerSmoke":"passed"}'
}
finally {
    if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit(3000) | Out-Null }
    $process.Dispose()
}
