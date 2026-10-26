@echo off
setlocal
where py >nul 2>nul
if not errorlevel 1 (
  py -3 "%~dp0install.py" %*
  goto finished
)
where python >nul 2>nul
if not errorlevel 1 (
  python "%~dp0install.py" %*
  goto finished
)
echo Python was not found. Paste the full path to ComfyUI's python.exe below.
echo Desktop commonly uses Documents\ComfyUI\.venv\Scripts\python.exe
set /p "ANIMA_INSTALL_PYTHON=Python executable: "
set "ANIMA_INSTALL_PYTHON=%ANIMA_INSTALL_PYTHON:"=%"
"%ANIMA_INSTALL_PYTHON%" "%~dp0install.py" %*
:finished
set "ANIMA_INSTALL_EXIT=%errorlevel%"
echo.
pause
exit /b %ANIMA_INSTALL_EXIT%
