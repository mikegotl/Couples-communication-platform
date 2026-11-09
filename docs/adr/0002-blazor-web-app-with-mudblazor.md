# ADR-0002: Blazor Web App with MudBlazor

- Status: Accepted
- Date: 2026-11-09

## Context
The product needs a responsive, accessible UI that keeps sensitive logic on the server.

## Decision
Use the ASP.NET Core Blazor Web App template with interactive server rendering and MudBlazor
components. Do not load third-party fonts, analytics or CDNs from pages, so partner activity is
not disclosed to other parties.

## Consequences
UI logic runs server-side over a SignalR circuit, keeping private content off client bundles.
Requires sticky sessions or a SignalR backplane when scaled out. MudBlazor analyzers run as
build errors to catch invalid component parameters.
