@echo off
setlocal
chcp 65001 >nul
set "PYTHONUTF8=1"
pushd "%~dp0" || exit /b 1

echo Card temporary output cleanup
echo Queue: tools\cleanup_queue.json
echo.

where py.exe >nul 2>nul
if not errorlevel 1 (
    py -3 -B "%~dp0tools\cleanup_temp.py" %*
    goto finished
)
where python.exe >nul 2>nul
if not errorlevel 1 (
    python -B "%~dp0tools\cleanup_temp.py" %*
    goto finished
)
echo Python 3 was not found. Install Python and run this file again.
set "cleanupExit=1"
goto showResult

:finished
set "cleanupExit=%errorlevel%"
:showResult
echo.
if "%cleanupExit%"=="0" (
    echo Command completed. Review the results above.
) else (
    echo Failed entries remain in the queue. See the errors above and retry later.
)
popd
pause
exit /b %cleanupExit%
