# ADR-0020: iPhone and Android app with .NET MAUI Blazor Hybrid

Status: accepted (2026-11-15). The owner approved the plan for iPhone and Android.

## Context

The owner wants an iPhone app alongside the web application and asked for the site to be
"upgraded to MAUI". The web app is a .NET 10 Blazor Web App (Interactive Server, MudBlazor). Its
pages call Application service interfaces (`IMessagingService`, `IInboxService`, ...) in-process,
and those services reach PostgreSQL through Infrastructure.

## Decision

- **Keep the web app; add a .NET MAUI Blazor Hybrid app.** MAUI hosts Razor components in a native
  `BlazorWebView`, so the iPhone app reuses the same components, MudBlazor and the Warm theme.
  Nothing about the web app is replaced; "upgrading to MAUI" means adding a second host for the
  same UI, not rewriting the site.
- **Shared UI in `Couples.UI`** (Razor class library). Pages move there from `Couples.Web` one area
  at a time. Code in it must run in both hosts: no `HttpContext`, no `DbContext`, no
  Infrastructure, and no `@rendermode` on components (BlazorWebView doesn't support render modes;
  the web app will set Interactive Server once, at the router, with account pages kept static).
- **The phone talks to a versioned JSON API** (`/api/v1`, `Couples.Web/Api`). On the phone, the
  shared components run on the device, so they can't call services in-process. `Couples.Client`
  will hold one HTTP client per Application interface, implementing that interface, so a shared
  page asks for `IMessagingService` and gets the in-process service on the web and the HTTP client
  on the phone.
- **The server decides who the user is.** The Application interfaces take a `userId` parameter.
  API endpoints ignore any user id a client sends and use the signed-in principal; the HTTP
  clients don't send one. Each endpoint calls the same Application service its page uses, so
  consent, privacy, pace and safety checks stay in one place.
- **Phone sign-in uses tokens, not cookies** (ASP.NET Core Identity bearer tokens with refresh,
  stored in the iOS Keychain through MAUI `SecureStorage`). Two-factor stays required where it is
  today. Sign in with Apple is offered in the app whenever Google is (App Store rule 4.8).
- **No message text stored on the phone** at first. Push notifications, when added, are
  content-free like today's in-app notices (ADR-0011).
- **Admin, partner check links and the partner application stay web-only.**
- **iPhone and Android from one project** (`src/Couples.Mobile`, `net10.0-ios26.0;net10.0-android`).
  iOS builds need macOS, so the project is not in `Couples.slnx`; `.github/workflows/mobile.yml`
  builds iOS for the simulator on a macOS runner and Android on Linux, only when the app or code
  it shares changes. Android disables cloud backups so drafts and tokens never leave the phone.

## Phase 0 (this change)

- `Couples.UI` with the Warm theme (`AppTheme`) and brand fonts, used by the web app.
- `GET /api/v1/app-status` (anonymous, holds no user data): service name, API version and the
  oldest supported app version (`MobileApp:MinimumVersion`, default 0.1.0).
- `Couples.Client` with a typed client for it (https only outside local development).
- `Couples.Mobile`: the phone app shell (iPhone and Android), which shows whether it can reach the service.

## Consequences

- Every page moved to `Couples.UI` must stop using `ScopedPage`'s `OwningComponentBase` scope
  tricks that assume a server circuit and `CurrentUser` that reads the server's auth state; both
  get small host-specific replacements.
- Each Application service used by a phone page needs an endpoint, a client and tests. That is
  most of the work, and it is where privacy mistakes could happen, so endpoints get integration
  tests that a user can't read or act on another user's data.
- An Apple Developer Program membership is needed to run the app on a real iPhone, for TestFlight
  and for the App Store (it is also already needed for Sign in with Apple, ADR-0016). Google Play
  needs a developer account ($25 once).
