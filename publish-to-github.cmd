@echo off
setlocal
rem Publishes the help to GitHub Pages after Flare has built the HTML5 target: copies the built site into Docs,
rem commits Content, Project and Docs, and pushes master. Double-click it, or run it from a command prompt.
rem
rem It uses Git for Windows and the GitHub sign-in already saved on this PC, not Flare's own publish step,
rem which cannot open this repository while the folder is owned by a different Windows account.

cd /D "%~dp0"
set "REPO=%CD:\=/%"
set "OUT=%CD%\Output\%USERNAME%\HTML5"
if not exist "%OUT%\index.html" (
  echo No HTML5 build found at "%OUT%". Build the HTML5 target in Flare first.
  exit /b 1
)

echo Copying the built site into Docs ...
robocopy "%OUT%" "%CD%\Docs" /MIR /NFL /NDL /NJH /NJS /NP /XF *.mclog
if errorlevel 8 (
  echo Copy failed.
  exit /b 1
)

echo Committing ...
git -c "safe.directory=%REPO%" add -A Content Project Docs
git -c "safe.directory=%REPO%" commit -q -m "Publish help built %DATE% %TIME:~0,5%"
if errorlevel 1 (
  echo Nothing new to commit.
) else (
  git -c "safe.directory=%REPO%" log --oneline -1
)

echo Pushing master to GitHub ...
git -c "safe.directory=%REPO%" push origin master
if errorlevel 1 (
  echo Push failed: check the GitHub sign-in on this PC.
  exit /b 1
)
echo Done: https://iain-buchan.github.io/statisticalhelp/
