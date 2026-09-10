@echo off
chcp 65001 >nul
setlocal

title Claude Code Multi-Provider Selector (April 2026)
echo.
echo ================================================
echo      Claude Code Multi-Provider Selector
echo ================================================
echo.

echo 1. Claude (Anthropic Cloud - Sonnet)
echo 2. Ollama (Remote: 192.168.0.104)
echo 3. LM Studio (Remote: 192.168.0.104)
echo 4. OpenRouter (Cloud Gateway)
echo.
choice /c 1234 /n /m "Select Provider (1-4): "

:: Reset all relevant variables
set ANTHROPIC_BASE_URL=
set ANTHROPIC_AUTH_TOKEN=
set ANTHROPIC_API_KEY=
set ANTHROPIC_MODEL=
set ANTHROPIC_DEFAULT_OPUS_MODEL=
set ANTHROPIC_DEFAULT_SONNET_MODEL=
set ANTHROPIC_DEFAULT_HAIKU_MODEL=
set MODEL_NAME=

if errorlevel 4 goto OPENROUTER
if errorlevel 3 goto LMSTUDIO_REMOTE
if errorlevel 2 goto OLLAMA_REMOTE
if errorlevel 1 goto CLAUDE

:: 1. Anthropic Cloud
:CLAUDE
set MODEL_NAME=claude-sonnet-5
set CLAUDE_CODE_SKIP_AUTH=
goto START_EXECUTION

:: 2. Ollama
:OLLAMA_REMOTE
set MODEL_NAME=qwen3.5:14b
set ANTHROPIC_MODEL=%MODEL_NAME%
set ANTHROPIC_BASE_URL=http://192.168.0.104:11434
set ANTHROPIC_AUTH_TOKEN=ollama
set ANTHROPIC_API_KEY=
goto START_EXECUTION

:: 3. LM Studio
:LMSTUDIO_REMOTE
echo.
echo Available LM Studio models:
echo 1. google/gemma-4-e4b:3
echo 2. unsloth/qwen3.6-35b-a3b-mtp@iq4_nl
echo 3. unsloth/qwen3.6-27b-mtp@iq4_xs
echo 4. lmstudio-community/gemma-4-26b-a4b-it@q4_k_m
echo 5. lmstudio-community/qwen3-coder-30b-a3b-instruct@q4_k_m
echo 6. lmstudio-community/qwen3.8-27b
echo 7. unsloth/qwen3.8-27b
echo.
choice /c 1234567 /n /m "Select Model (1-7): "

if errorlevel 7 (
    set MODEL_NAME=unsloth/qwen3.8-27b
) else if errorlevel 6 (
    set MODEL_NAME=lmstudio-community/qwen3.8-27b
) else if errorlevel 5 (
    set MODEL_NAME=lmstudio-community/qwen3-coder-30b-a3b-instruct@q4_k_m
) else if errorlevel 4 (
    set MODEL_NAME=lmstudio-community/gemma-4-26b-a4b-it@q4_k_m
) else if errorlevel 3 (
    set MODEL_NAME=unsloth/qwen3.6-27b-mtp@iq4_xs
) else if errorlevel 2 (
    set MODEL_NAME=unsloth/qwen3.6-35b-a3b-mtp@iq4_nl
) else (
    set MODEL_NAME=google/gemma-4-e4b:3
)

set ANTHROPIC_MODEL=%MODEL_NAME%
set ANTHROPIC_BASE_URL=http://192.168.0.104:1234/
set ANTHROPIC_AUTH_TOKEN=lm-studio
set ANTHROPIC_API_KEY=lm-studio
goto START_EXECUTION

:: 4. OpenRouter
:OPENROUTER
echo.
set /p USER_KEY="Paste OpenRouter API Key (sk-or-...): "
echo.

echo 1. Minimax-M2.5:free (Free)
echo 2. Minimax-M2.5 (stronger)
echo 3. Qwen 3.6 Plus (Free)
echo.
choice /c 123 /n /m "Choice (1-3): "

if errorlevel 3 (
    set TARGET_MODEL=qwen/qwen3.6-plus:free
) else if errorlevel 2 (
    set TARGET_MODEL=minimax/minimax-m2.5
) else (
    set TARGET_MODEL=minimax/minimax-m2.5:free
)

set ANTHROPIC_BASE_URL=https://openrouter.ai/api/v1/adapters/anthropic
set ANTHROPIC_AUTH_TOKEN=%USER_KEY%
set ANTHROPIC_API_KEY=

set ANTHROPIC_DEFAULT_OPUS_MODEL=%TARGET_MODEL%
set ANTHROPIC_DEFAULT_SONNET_MODEL=%TARGET_MODEL%
set ANTHROPIC_DEFAULT_HAIKU_MODEL=%TARGET_MODEL%
set ANTHROPIC_MODEL=%TARGET_MODEL%
set MODEL_NAME=%TARGET_MODEL%

goto START_EXECUTION

:START_EXECUTION
echo.
echo ------------------------------------------------
echo CONFIGURATION SUMMARY
echo ------------------------------------------------
echo Target Model:     %MODEL_NAME%
if defined ANTHROPIC_BASE_URL echo API Endpoint:    %ANTHROPIC_BASE_URL%
if defined ANTHROPIC_AUTH_TOKEN echo Auth Token:      %ANTHROPIC_AUTH_TOKEN%
if defined ANTHROPIC_API_KEY echo API Key:         %ANTHROPIC_API_KEY%
echo ------------------------------------------------
echo.

if defined ANTHROPIC_BASE_URL (
    echo Launching with custom provider...
    claude
) else (
    echo Launching native Anthropic...
    claude --model %MODEL_NAME%
)

echo.
echo Session ended.
pause
goto END

:END
endlocal