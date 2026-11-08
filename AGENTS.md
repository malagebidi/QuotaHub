# QuotaHub --- Agent Instructions

## Project overview

QuotaHub is a small, native Windows application for displaying usage
quotas from multiple AI providers, initially including OpenRouter and
DeepSeek.

The intended interaction is:

-   QuotaHub runs in the background.
-   A notification-area (system tray) icon provides the main entry
    point.
-   Clicking the tray icon opens or hides a compact quota panel.
-   The panel displays provider quota information.
-   Settings are embedded in the panel (quota view plus settings view,
    no separate settings window).

The current goal is to build a simple, maintainable Windows application.
Do not introduce unnecessary frameworks, dependencies, abstractions, or
features.

## Technology choices

Use the following stack unless the user explicitly changes the decision:

-   C#
-   WinUI 3
-   Windows App SDK
-   .NET 10
-   Visual Studio 2026 Community

This is intended to be a native WinUI 3 application, not a Tauri,
Electron, browser-based, or WebView application.

Build, run, and debug in Visual Studio. VS Code is text-editing only:
decline its "add build and debug assets" prompt and do not use C# Dev Kit
to build or debug this packaged app.

### Current project configuration

The project was created from the **Blank App, Packaged (WinUI 3 in
Desktop)** template, using the single-project MSIX packaging approach.

The project and solution are both named `QuotaHub`.

The package identity is `DY.QuotaHub` with publisher `CN=DY` (local
development certificate).

The project currently targets:

``` xml
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
```

The existing project also uses:

-   `Microsoft.Windows.SDK.BuildTools` version `10.0.28000.2957`
-   `Microsoft.WindowsAppSDK` version `2.6.5`
-   `UseWinUI` enabled
-   `EnableMsixTooling` enabled
-   Nullable reference types enabled

Do not change package versions, target frameworks, packaging settings,
or project structure without a concrete reason. Avoid adding packages
merely for convenience.

## Development environment and verified state

-   Visual Studio 2026 Community is installed with the **WinUI
    application development** workload.
-   Windows Developer Mode is enabled.
-   .NET SDK `10.0.403` is installed.
-   One Windows 11 SDK version was selected during Visual Studio
    installation.
-   The initial project build succeeded after its first NuGet/Windows
    App SDK restore completed.
-   The application was launched successfully with F5 and displayed its
    WinUI window.

The basic development environment and blank application have therefore
been verified.

## Product and implementation constraints

-   Keep the application lightweight and focused.
-   Prefer Windows-native APIs and WinUI 3 facilities.
-   Avoid adding a large UI framework or unrelated dependencies.
-   The tray icon is an entry point; the quota panel itself should be a
    WinUI 3 window.
-   Do not assume that implementing the tray icon also implements the
    popup panel. These are separate responsibilities.
-   Do not add startup-on-boot, automatic update, logging
    infrastructure, or other secondary features until the basic
    application flow works and the user requests or approves them.
-   No dedicated settings window. Settings live inside the panel.

## Tray icon decision --- finalized

Decision: use direct Win32 `Shell_NotifyIcon` interop.

Rejected: `H.NotifyIcon.WinUI`. It is compatible with the current
`net10.0-windows10.0.19041.0` / Windows App SDK `2.6.5` setup, but it is
not needed for the minimal tray-plus-panel prototype and would add an
extra dependency and abstraction.

Do not add a tray library. Implement the notification-area icon with
`Shell_NotifyIcon` and minimal Win32 interop.

## Intended behavior

The following behavior is implemented:

-   Run as a tray-resident app with a notification-area icon.
-   Create a notification-area icon.
-   Show the quota panel at startup so the app is easy to find.
-   Left-clicking the tray icon toggles the quota panel.
-   Right-clicking the tray icon opens a small context menu.
-   Clicking outside the panel hides it.
-   Pressing `Esc` hides it.
-   Closing the panel hides it instead of terminating the background
    application.
-   An explicit Exit command in the tray menu terminates the
    application.
-   The panel contains a settings entry point.

The tray icon and panel show/hide lifecycle prototype is complete.
Provider quota retrieval (OpenRouter, DeepSeek) is implemented with
manual refresh.

## Proposed architecture

Keep the application in one project. Do not split it into multiple
projects at this stage.

Potential responsibilities:

-   **Tray service:** create and manage the notification-area icon,
    receive its callbacks, and expose tray interactions.
-   **Window service:** show, hide, position, and manage the compact
    WinUI panel.
-   **Quota service:** retrieve and normalize provider quota data.
-   **Refresh service:** coordinate periodic refreshes.
-   **Settings service:** load and save application settings.
-   **Provider implementations:** retrieve quota data from individual
    providers through a shared interface.

Potential APIs and technologies:

-   Win32 `Shell_NotifyIcon` for the notification-area icon.
-   WinUI 3 `Window` and Windows App SDK `AppWindow` for the panel.
-   Win32 interop where necessary for window positioning, activation,
    and notification-area integration.
-   `HttpClient` for network requests.
-   `PeriodicTimer` for periodic refresh.
-   JSON for settings and provider data.
-   Windows data protection (`DataProtectionProvider`, `LOCAL=user`)
    for credentials. API keys are stored encrypted, never as plain text.

These are architectural directions, not instructions to create every
service or file immediately.

## Possible project organization

Only create directories and files when they are needed. This is a
suggested future structure, not a requirement to scaffold everything at
once.

``` text
QuotaHub/
├── Assets/
│   ├── TrayIcon.ico
│   └── *.png (default template images)
├── Models/
│   └── QuotaSnapshot.cs
├── Services/
│   ├── TrayIconService.cs
│   ├── SettingsService.cs
│   └── QuotaService.cs
├── Providers/
│   ├── IQuotaProvider.cs
│   ├── OpenRouterProvider.cs
│   └── DeepSeekProvider.cs
├── Views/ (future, only when needed)
├── ViewModels/ (future, only when needed)
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
└── QuotaHub.csproj
```

## Working method --- important

Work incrementally and in order.

1.  Explain the purpose of the current step.
2.  Make only the changes needed for that step.
3.  Give exact Visual Studio or terminal instructions where applicable.
4.  Identify the expected result so the user can verify it.
5.  Wait for the user to confirm the step is complete before moving on.

Do not skip ahead, implement several future steps at once, or generate a
large amount of code before the relevant design decisions are settled.

When changing code:

-   Inspect the existing project and relevant files first.
-   Preserve the existing project configuration unless a change is
    necessary.
-   Explain which files will be created or modified.
-   Provide complete, ready-to-use code for the files being changed.
-   Avoid unrelated refactoring.
-   Do not assume a file's contents; ask the user to provide it or
    inspect the project when possible.
-   Use English identifiers and English text in source code. Do not put
    Chinese in code.
-   Keep explanations and user-facing instructions in the language the
    user is using.

The user is learning and building the project step by step. Favor clear
reasoning and small verifiable changes over speed or speculative
scaffolding.

## Current status

-   [x] Install Visual Studio and the required WinUI workload.
-   [x] Enable Windows Developer Mode.
-   [x] Create the packaged WinUI 3 project.
-   [x] Change the target framework to .NET 10.
-   [x] Build and launch the blank WinUI application successfully.
-   [x] Finalize the tray icon implementation approach (direct `Shell_NotifyIcon`).
-   [x] Build the minimal tray icon and panel show/hide prototype.
-   [x] Decide the settings presentation (embedded in the panel).
-   [x] Design and implement provider quota retrieval (OpenRouter + DeepSeek, manual refresh).
-   [x] Implement panel slide animation, frameless styling, and taskbar removal.
-   [x] Add refresh button spinning animation with smooth ease-out and 5-second cooldown.
-   [x] Adapt panel positioning and slide animations to all taskbar orientations (bottom, top, left, right).
-   [x] Implement global shortcut support with Win32 RegisterHotKey (Alt + Q toggle) and embedded settings selection.
