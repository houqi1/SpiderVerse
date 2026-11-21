param([int]$Port = 8769)
$ErrorActionPreference = 'Stop'
python (Join-Path $PSScriptRoot 'serve_native.py') --port $Port
