@echo off
REM ---------------------------------------------------------------------------
REM  publish-production.cmd
REM
REM  Convenience wrapper that runs publish-production.ps1 with an execution
REM  policy bypass, so it works even when the machine's PowerShell execution
REM  policy would otherwise block running local scripts. Only this single
REM  invocation is affected; no system-wide policy is changed.
REM
REM  Any arguments passed to this .cmd are forwarded to the .ps1 script, e.g.:
REM      publish-production.cmd -Clean
REM      publish-production.cmd -OutputRoot C:\publish\LunchOrganizer -Clean
REM      publish-production.cmd -SelfContained
REM ---------------------------------------------------------------------------

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-production.ps1" %*

exit /b %ERRORLEVEL%
